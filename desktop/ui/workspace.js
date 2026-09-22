function conversationResources(){
  const chat=snapshot?.chats.find(x=>x.id===chatId);
  return chat?.resourcePathsJson?JSON.parse(chat.resourcePathsJson):(selectedProject()?.sourceFolder||'').split(/[|;\r\n]/).filter(Boolean);
}
async function saveResources(paths,inherit=false){
  if(!chatId)return;const id=chatId;
  await call('chat.resources',{id,paths:[...new Set(paths)],inherit});await refresh();
  if(chatId===id){renderAssets();if(tab==='files'&&!$('tools').hidden)await loadFiles();if(tab==='git'&&!$('tools').hidden)await refreshGit();}
  status(L('Ressources disponibles au prochain envoi.','Resources available on the next send.'));
}
function renderResourceChips(){
  for(const path of conversationResources()){
    const chip=el('div',null,'resource-chip');chip.title=path;chip.append(el('span','📎 '+path.split(/[\\/]/).filter(Boolean).at(-1)));
    const remove=el('button','×');remove.setAttribute('aria-label',L('Détacher ','Detach ')+path);remove.onclick=()=>guard(()=>saveResources(conversationResources().filter(x=>x!==path)));chip.append(remove);$('assets').append(chip);
  }
}
function resourceDialog(){
  if(!chatId)return;
  const dialog=el('dialog'),list=el('div');let paths=conversationResources();
  dialog.append(el('h3',L('Ressources de la conversation','Conversation resources')),el('p',L('Les changements seront utilisés au prochain envoi. Vous pouvez aussi déposer des fichiers et dossiers sur la zone de saisie.','Changes apply on the next send. You can also drop files and folders on the composer.'),'muted'),list);
  function render(){list.replaceChildren(...paths.map(path=>{const row=el('div',null,'source-item');row.append(el('span',path));button(row,'×',()=>{paths=paths.filter(x=>x!==path);render();});return row;}));}render();
  button(dialog,L('Ajouter des dossiers','Add folders'),async()=>{paths=[...new Set([...paths,...await api.host('pick.folders')])];render();});
  button(dialog,L('Ajouter un fichier','Add file'),async()=>{paths=[...new Set([...paths,...await api.host('pick.file')])];render();});
  button(dialog,L('Hériter du projet','Inherit project folders'),async()=>{await saveResources([],true);dialog.close();});
  button(dialog,L('Enregistrer','Save'),async()=>{await saveResources(paths);dialog.close();});
  button(dialog,L('Annuler','Cancel'),()=>dialog.close());dialog.onclose=()=>{dialog.remove();updateBrowserBounds();};document.body.append(dialog);dialog.showModal();updateBrowserBounds();
}
const dropTarget=document.querySelector('.composer-shell');
dropTarget.addEventListener('dragover',event=>{if([...event.dataTransfer.types].includes('Files')){event.preventDefault();event.dataTransfer.dropEffect='copy';dropTarget.classList.add('drop-active');}});
dropTarget.addEventListener('dragleave',event=>{if(!dropTarget.contains(event.relatedTarget))dropTarget.classList.remove('drop-active');});
dropTarget.addEventListener('drop',event=>{if(!event.dataTransfer.files.length)return;event.preventDefault();dropTarget.classList.remove('drop-active');const paths=[...event.dataTransfer.files].map(file=>api.filePath(file)).filter(Boolean);guard(()=>saveResources([...conversationResources(),...paths]));});
// Avoid navigating the renderer to a dropped local file outside the composer.
document.addEventListener('dragover',event=>{if([...event.dataTransfer.types].includes('Files'))event.preventDefault();});
document.addEventListener('drop',event=>event.preventDefault());

function messageBranchActions(node,message){
  if(!message.canBranch||selectedChild)return;
  const id=chatId,actions=el('div',null,'message-actions');
  async function branch(resume){
    if(resume&&!confirm(L('Reprendre ici ? La suite sera conservée dans une conversation Sauvegarde et la file d’attente sera vidée.','Resume here? Later messages will be kept in a Backup conversation and the queue will be cleared.')))return;
    const result=await call('chat.branch',{chatId:id,messageId:message.id,resume});histories.delete(id);
    if(resume)for(const [key,child] of subagents)if(child.chatId===id)subagents.delete(key);
    await refresh();await selectChat(result.id);$('composer').focus();
  }
  button(actions,L('Créer un fork','Fork here'),()=>branch(false));button(actions,L('Reprendre ici','Resume here'),()=>branch(true));
  actions.lastChild.disabled=running.has(id);node.append(actions);
}
function renderProjectPermissions(){
  const area=$('project-permissions');area.replaceChildren();
  area.append(el('p',L('Ces dossiers sont inclus par défaut. AGENTS.md et Agent.md sont chargés automatiquement.','These folders are included by default. AGENTS.md and Agent.md load automatically.'),'muted'));
  if(!projectEdit?.id){area.append(el('p',L('Enregistrez le projet pour importer permission.json.','Save the project before importing permission.json.')));return;}
  const owner=projectEdit.id,preview=el('pre',projectEdit.permissionProfileJson||L('Aucune règle importée.','No imported rules.'));area.append(preview);
  button(area,L('Importer permission.json','Import permission.json'),async()=>{
    const reviewed=await call('project.permissions.preview',{id:owner});preview.textContent=reviewed||L('Aucune règle trouvée.','No rules found.');
    if(confirm(L('Appliquer ces règles au projet ? Les futures modifications du fichier devront être réimportées. Refuser tout reste prioritaire.\n\n','Apply these rules to this project? Later changes need a new import. Deny all takes priority.\n\n')+(reviewed||'{}'))){await call('project.permissions.apply',{id:owner,reviewed});await refresh();projectEdit=snapshot.projects.find(x=>x.id===owner);}
  });
  button(area,L('Retirer les règles','Clear rules'),async()=>{await call('project.permissions.apply',{id:owner,clear:true});await refresh();projectEdit=snapshot.projects.find(x=>x.id===owner);preview.textContent='';});
}
let cachedGitPreview=null;
const gitMode=el('select');gitMode.id='git-preview-mode';gitMode.setAttribute('aria-label','Git preview');gitMode.append(option('split','Avant / après · Before / after'),option('unified','Diff combiné · Unified'));$('git-refresh').after(gitMode);
gitMode.onchange=()=>{if(cachedGitPreview?.chatId===chatId)renderGitComparison(cachedGitPreview.file,cachedGitPreview.preview);};
function renderGitComparison(file,preview){
  cachedGitPreview={chatId,file,preview};$('git-diff').replaceChildren(el('div',file.path));
  const unified=gitMode.value==='unified',table=el('div',null,unified?'git-unified':'git-comparison');
  if(!unified)table.append(el('div',L('Avant · HEAD','Before · HEAD'),'diff-hunk'),el('div',L('Après · Dossier de travail','After · Working tree'),'diff-hunk'));
  for(const row of preview.rows){
    if(unified)table.append(el('div',(row.kind==='added'?'+ ':row.kind==='removed'?'− ':'  ')+(row.kind==='removed'?row.beforeLine??'':row.afterLine??'')+'  '+(row.kind==='removed'?row.before:row.after??row.before??''),'diff-'+row.kind));
    else table.append(el('div',row.before==null?'':(row.beforeLine??'')+'  '+row.before,row.kind==='removed'?'diff-removed':row.kind==='hunk'?'diff-hunk':''),el('div',row.after==null?'':(row.afterLine??'')+'  '+row.after,row.kind==='added'?'diff-added':row.kind==='hunk'?'diff-hunk':''));
  }
  $('git-diff').append(table);if(preview.notice)$('git-diff').append(el('div',preview.notice));
}
function focusLatestTool(name){
  if(!featureConfig().AutoFocusTool)return;
  const next=/terminal/.test(name)?'terminal':name.startsWith('git')?'git':/source|^rag_/.test(name)?'files':/browser|^browse$|^read_page$|^inspect_dom$|^open_local_file$/.test(name)?'web':null;
  if(next){showTools(true);selectTab(next);if(next==='files')guard(loadFiles);}
}
