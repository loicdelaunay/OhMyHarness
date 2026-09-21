const $ = id => document.getElementById(id), api = window.harness;
let snapshot, chatId, projectId, providerId, tab='web', settingsTab='general', projectEdit=null, projectFolders=[], fileSelected='';
const pendingQuestions=new Map();
const subagents=new Map();let selectedChild=null;
let followChatTail=true;
$('auto-scroll').onclick=()=>{followChatTail=!followChatTail;$('auto-scroll').setAttribute('aria-pressed',String(followChatTail));followMessages();};
$('messages').addEventListener('scroll',()=>{const box=$('messages');followChatTail=box.scrollHeight-box.scrollTop-box.clientHeight<=8;$('auto-scroll').setAttribute('aria-pressed',String(followChatTail));});
function followMessages(){if(followChatTail)$('messages').scrollTop=$('messages').scrollHeight;}
let contextDetail=null,contextDetailChat=null,contextCloseTimer;
const drafts=new Map(), histories=new Map(), running=new Set(), inflight=new Set(), metrics=new Map(), statuses=new Map();
const L=(fr,en)=>snapshot?.state.language==='en'?en:fr;
function el(tag,text,cls){const node=document.createElement(tag);if(text!=null)node.textContent=text;if(cls)node.className=cls;return node;}
function status(text,error=false){$('status').textContent=text;$('status').classList.toggle('error',error);}
async function guard(action){try{return await action();}catch(error){status(error.message,true);}}
const call=(method,parameters={})=>api.call(method,parameters);
function currentDraft(){if(!drafts.has(chatId))drafts.set(chatId,{text:'',images:[]});return drafts.get(chatId);}
function saveDraft(){if(chatId)currentDraft().text=$('composer').value;}
function selectedProject(){return snapshot.projects.find(x=>x.id===projectId);}
function option(value,text){const node=el('option',text);node.value=value;return node;}
function translate(){
  $('export-chat').textContent=L('Exporter','Export');
  document.documentElement.lang=snapshot.state.language;
  $('new-chat').textContent=L('＋ Nouvelle conversation','＋ New conversation');$('settings-open').textContent=L('⚙ Réglages','⚙ Settings');
  $('tools-toggle').textContent=L('▤ Outils','▤ Tools');$('tools-title').textContent=L('Outils','Tools');$('composer').placeholder=L('Posez une question…','Ask a question…');
  $('shortcut').textContent=L('Entrée ↵ · Ctrl+Entrée : nouvelle ligne','Enter ↵ · Ctrl+Enter: new line');
  $('terminal-run').textContent=L('Exécuter','Run');$('git-refresh').textContent=L('Actualiser','Refresh');
  $('shell-info').textContent=snapshot.shell+' · '+L('Dossier du projet · nouvelle session','Project directory · fresh session');
  $('file-preview').textContent=L('Ouvrir dans Web','Open in browser');
  const settingNames={general:L('Général','General'),providers:L('Fournisseurs','Providers'),skills:'Skills',mcp:'MCP',permissions:L('Autorisations','Permissions'),templates:'Templates',browser:L('Navigateur','Browser'),rag:'RAG'};
  document.querySelectorAll('[data-settings]').forEach(button=>button.textContent=settingNames[button.dataset.settings]);
  document.querySelector('[data-tab="files"]').textContent=L('Fichiers','Files');
}
async function refresh(){
  snapshot=await call('snapshot');
  for(const item of snapshot.questions||[])pendingQuestions.set(item.id,item);
  renderQuestions();
  running.clear();snapshot.running.forEach(id=>running.add(id));inflight.forEach(id=>running.add(id));
  projectId=snapshot.projects.some(x=>x.id===projectId)?projectId:(snapshot.state.projectId||snapshot.projects[0]?.id);
  providerId=snapshot.providers.some(x=>x.id===providerId)?providerId:(snapshot.state.providerId||snapshot.providers[0]?.id);
  if(!snapshot.providers.some(x=>x.id===providerId))providerId=snapshot.providers[0]?.id;
  $('platform').textContent=snapshot.platform;$('projects').replaceChildren(...snapshot.projects.map(p=>option(p.id,p.name)));$('projects').value=projectId;
  $('providers').replaceChildren(...snapshot.providers.map(p=>option(p.id,`${p.name} · ${p.model}`)));$('providers').value=providerId||'';
  $('thinking').value=snapshot.state.thinkingLevel;
  translate();renderChats();updateControls();
}
function renderChats(){
  $('chats').replaceChildren();
  for(const chat of snapshot.chats.filter(x=>x.projectId===projectId).reverse()){
    const button=el('button',null,'chat-row'+(chat.id===chatId?' selected':''));button.append(el('span',chat.title));button.title=chat.title;
    if([...pendingQuestions.values()].some(x=>x.chatId===chat.id))button.append(el('span','?','waiting-badge'));
    if(running.has(chat.id)){const progress=el('progress');progress.setAttribute('aria-label',L('Génération en cours','Generating'));button.append(progress);}
    button.onclick=()=>guard(()=>selectChat(chat.id));$('chats').append(button);renderChildRows(chat);
  }
}
async function selectChat(id){
  selectedChild=null;saveDraft();chatId=id;closeContextPopover();closeSpeedPopover();renderQuestions();if(tab==='terminal')guard(refreshTerminals);
  gitRevision++;fileRevision++;fileSelected='';$('git-files').replaceChildren();$('git-diff').replaceChildren();$('git-summary').textContent='';
  $('file-list').replaceChildren();$('file-content').textContent='';$('file-preview').hidden=true;$('file-path').value='.';
  $('address').value='about:blank';
  api.host('browser.select',{chatId:id}).then(state=>{if(chatId===id){$('address').value=state.url||'about:blank';updateBrowserBounds();}}).catch(error=>status(error.message,true));
  $('chat-title').textContent=snapshot.chats.find(x=>x.id===id)?.title||L('Créez une conversation','Create a conversation');
  $('composer').value=currentDraft().text;renderAssets();renderChats();updateControls();renderMetrics();await refreshInbox();
  status(statuses.get(id)||'');
  if(!id){$('messages').replaceChildren(el('p',L('Créez une conversation pour commencer.','Create a conversation to start.'),'empty'));return;}
  if(!histories.has(id)||!running.has(id))histories.set(id,await call('history',{chatId:id}));
  const children=await call('subagents',{chatId:id});
  if(chatId!==id)return;for(const child of children){if(child.status==='running'&&!running.has(id))child.status='interrupted';subagents.set(child.id,child);}renderMessages(true);renderChats();
  if(!$('tools').hidden){if(tab==='git')guard(refreshGit);if(tab==='files'&&selectedProject()?.sourceFolder)guard(loadFiles);}
  await call('state.save',{chatId:id,projectId});
}
function updateControls(){if(typeof renderInbox==='function')renderInbox();$('composer').disabled=!!selectedChild;$('send').disabled=!!selectedChild||!chatId||!providerId;$('delivery-mode').hidden=!running.has(chatId);$('stop').disabled=!running.has(chatId);$('rename-chat').disabled=$('delete-chat').disabled=!chatId;$('new-chat').disabled=!projectId;}
function renderAssets(){
  $('assets').replaceChildren();currentDraft().images.forEach((image,index)=>{const box=el('div',null,'asset');const img=el('img');img.src=`data:${image.mime};base64,${image.data}`;img.alt=image.name;const remove=el('button','×');remove.onclick=()=>{currentDraft().images.splice(index,1);renderAssets();};box.append(img,remove);$('assets').append(box);});
}
function renderMessage(message){
  if(message.role==='tasks')return renderTasks(message);
  const node=el('article',null,'message '+message.role);node.dataset.message=message.id;node.dataset.project=projectId;node.append(el('div',message.role==='user'?L('VOUS','YOU'):message.role==='tool'?L('OUTIL','TOOL'):L('ASSISTANT','ASSISTANT'),'role'));
  if(message.reasoning){const details=el('details');details.open=snapshot.state.showReasoningDetails!==false;details.append(el('summary',L('Raisonnement du modèle','Model reasoning')));details.append(el('div',message.reasoning,'reasoning'));node.append(details);}
  const body=el('div',null,'body');if(message.html)body.innerHTML=message.html;else body.textContent=message.content||'…';node.append(body);
  for(const image of message.attachments||[]){const img=el('img',null,'attachment');img.src=`data:${image.mime};base64,${image.data}`;img.alt=image.name;node.append(img);}
  if(message.state==='interrupted'&&!running.has(chatId))node.append(el('p',L('Réponse interrompue','Interrupted response'),'interrupted'));
  return node;
}
function renderMessages(bottom=false){
  if(selectedChild){renderChild();return;}
  const position=$('messages').scrollTop;
  if(bottom)followChatTail=true;
  const list=histories.get(chatId)||[];
  $('messages').replaceChildren(...[...list.filter(x=>x.role==='tasks'),...list.filter(x=>x.role!=='tasks')].map(renderMessage));
  if(!list.length)$('messages').append(el('p',L('Un espace pour vos idées.\nDes outils pour aller plus loin.','A space for your ideas.\nTools to go further.'),'empty'));
  renderChildBubbles();$('messages').scrollTop=position;followMessages();
}
function updateMessage(id,message){
  if(!histories.has(id))histories.set(id,[]);const list=histories.get(id);const index=list.findIndex(x=>x.id===message.id);
  if(index<0)list.push(message);else list[index]={...list[index],...message};
  if(id!==chatId||selectedChild)return;
  const scroll=$('messages'),bottom=followChatTail;
  const old=scroll.querySelector(`[data-message="${message.id}"]`),node=renderMessage(index<0?message:list[index]);
  const oldThinking=old?.querySelector('details'),newThinking=node.querySelector('details');if(oldThinking&&newThinking)newThinking.open=oldThinking.open;
  if(old)old.replaceWith(node);else{scroll.querySelector('.empty')?.remove();message.role==='tasks'?scroll.prepend(node):scroll.append(node);}
  const reasoning=node.querySelector('.reasoning');if(reasoning)reasoning.scrollTop=reasoning.scrollHeight;
  if(bottom)scroll.scrollTop=scroll.scrollHeight;
}
function renderMetrics(){
  renderSpeedDetail();
  renderContextDetail();
  const value=metrics.get(chatId);$('speed').textContent=value?`${value.estimated?'≈ ':''}${value.speed.toFixed(1)} tok/s`:'— tok/s';
  $('context').textContent=value?`${value.estimated?'≈ ':''}${value.tokens.toLocaleString()} / ${value.limit.toLocaleString()}`:'— tokens';
  $('context-progress').value=value?Math.min(100,value.tokens/value.limit*100):0;
}
async function send(){
  if($('send').disabled)return;saveDraft();const id=chatId,draft=currentDraft();if(!draft.text.trim()&&!draft.images.length)return;
  const text=draft.text,images=draft.images;draft.text='';draft.images=[];$('composer').value='';renderAssets();
  if(running.has(id)){try{const result=await call('inbox.add',{chatId:id,providerId,text,images,mode:$('delivery-mode').value});if(!result.running&&result.autoStart)await call('inbox.resume',{chatId:id});}catch(error){draft.text=text+(draft.text?'\n'+draft.text:'');draft.images.unshift(...images);if(chatId===id){$('composer').value=draft.text;renderAssets();}throw error;}return;}
  inflight.add(id);running.add(id);renderChats();updateControls();statuses.set(id,L('Le modèle réfléchit…','Model is thinking…'));status(statuses.get(id));
  try{await call('send',{chatId:id,providerId,text,images});}
  catch(error){status(error.message,true);if(!(histories.get(id)||[]).some(x=>x.role==='user'&&x.content===text)){const next=drafts.get(id);next.text=text+(next.text?'\n'+next.text:'');next.images.unshift(...images);if(chatId===id){$('composer').value=next.text;renderAssets();}}}
  finally{inflight.delete(id);running.delete(id);renderChats();updateControls();if(chatId===id){histories.set(id,await call('history',{chatId:id}));renderMessages();}}
}
api.onEvent(event=>{
  if(event.event==='started'){running.add(event.chatId);renderChats();updateControls();return;}
  if(event.event==='inbox'){inboxes.set(event.chatId,event.items);if(chatId===event.chatId)renderInbox();return;}
  if(event.event==='subagent'){subagents.set(event.child.id,event.child);renderChats();if(event.chatId===chatId){if(selectedChild===event.child.id)renderChild();else if(!selectedChild){renderChildBubbles();followMessages();}}return;}
  if(event.event==='browser-error'){if(event.chatId===chatId){$('browser-surface').textContent=event.error;status(event.error,true);}return;}
  if(event.event==='question'){pendingQuestions.set(event.id,event);renderQuestions();renderChats();return;}
  if(event.event==='question.closed'){pendingQuestions.delete(event.id);renderQuestions();renderChats();return;}
  if(event.event==='fatal'){status(event.error,true);return;}
  if(event.event==='browser'){if(event.chatId===chatId)$('address').value=event.url;return;}
  if(event.event==='stream'){
    running.add(event.chatId);metrics.set(event.chatId,event);updateMessage(event.chatId,{id:event.messageId,role:'assistant',content:event.text,html:event.html,reasoning:event.reasoning,state:'streaming'});if(event.chatId===chatId)renderMetrics();
  }else if(event.event==='message'){
    if(event.title){const chat=snapshot.chats.find(x=>x.id===event.chatId);if(chat)chat.title=event.title;if(event.chatId===chatId)$('chat-title').textContent=event.title;renderChats();}
    updateMessage(event.chatId,event.message);
  }else if(event.event==='status'){statuses.set(event.chatId,event.text);if(event.chatId===chatId)status(event.text);}
  else if(event.event==='done'){running.delete(event.chatId);statuses.set(event.chatId,event.error||event.status||L('Réponse terminée.','Response complete.'));renderChats();updateControls();if(event.chatId===chatId)status(statuses.get(event.chatId),!!event.error);}
});
$('send').onclick=()=>guard(send);$('stop').onclick=()=>guard(()=>call('stop',{chatId}));
$('composer').oninput=saveDraft;$('composer').onkeydown=e=>{if(e.key==='Enter'&&!e.isComposing){e.preventDefault();if(e.ctrlKey){const t=e.target;t.setRangeText('\n',t.selectionStart,t.selectionEnd,'end');saveDraft();}else guard(send);}};
$('projects').onchange=()=>guard(async()=>{saveDraft();projectId=Number($('projects').value);gitRevision++;$('git-files').replaceChildren();$('git-diff').replaceChildren();if(tab==='git')await refreshGit();renderChats();await selectChat(snapshot.chats.find(x=>x.projectId===projectId)?.id);});
$('providers').onchange=()=>guard(async()=>{providerId=Number($('providers').value);await call('state.save',{providerId});});
$('thinking').onchange=()=>guard(()=>call('state.save',{thinkingLevel:$('thinking').value}));
$('new-chat').onclick=()=>guard(async()=>{const result=await call('chat.save',{projectId,title:L('Nouvelle conversation','New conversation')});await refresh();await selectChat(result.id);});
$('rename-chat').onclick=()=>{$('new-name').value=snapshot.chats.find(x=>x.id===chatId)?.title||'';showDialog('name-dialog');};
$('name-form').onsubmit=e=>{e.preventDefault();guard(async()=>{await call('chat.save',{id:chatId,title:$('new-name').value});$('name-dialog').close();await refresh();$('chat-title').textContent=$('new-name').value;});};
$('delete-chat').onclick=()=>guard(async()=>{if(!confirm(L('Supprimer cette conversation ?','Delete this conversation?')))return;await call('chat.delete',{id:chatId});await refresh();await selectChat(snapshot.chats.find(x=>x.projectId===projectId)?.id);});
function showDialog(id){$(id).showModal();updateBrowserBounds();}
document.querySelectorAll('[data-close]').forEach(button=>button.onclick=()=>$(button.dataset.close).close());
document.querySelectorAll('dialog').forEach(dialog=>dialog.addEventListener('close',updateBrowserBounds));
function projectDialog(existing){projectEdit=existing||null;projectFolders=existing?.sourceFolder?.split(/[|;\r\n]/).filter(Boolean)||[];$('project-name').value=existing?.name||'';$('project-delete').hidden=!existing;renderSources();showDialog('project-dialog');}
function renderSources(){$('project-sources').replaceChildren(...projectFolders.map((folder,i)=>{const row=el('div',null,'source-item');const remove=el('button','×');remove.type='button';remove.onclick=()=>{projectFolders.splice(i,1);renderSources();};row.append(el('span',folder),remove);return row;}));}
$('new-project').onclick=()=>projectDialog();$('manage-project').onclick=()=>projectDialog(selectedProject());
$('project-folders').onclick=()=>guard(async()=>{projectFolders=[...new Set([...projectFolders,...await api.host('pick.folders')])];renderSources();});
$('project-form').onsubmit=e=>{e.preventDefault();guard(async()=>{const result=await call('project.save',{id:projectEdit?.id||0,name:$('project-name').value,folders:projectFolders});projectId=result.id;$('project-dialog').close();await refresh();await selectChat(snapshot.chats.find(x=>x.projectId===projectId)?.id);});};
$('project-delete').onclick=()=>guard(async()=>{if(!confirm(L('Supprimer le projet et son historique ?','Delete this project and its history?')))return;await call('project.delete',{id:projectEdit.id});$('project-dialog').close();projectId=null;await refresh();await selectChat(snapshot.chats.find(x=>x.projectId===projectId)?.id);});
$('plus').onclick=()=>{
  const menu=$('composer-menu');menu.replaceChildren();function item(label,fn){const button=el('button',label);button.onclick=()=>guard(async()=>{await fn();});menu.append(button);}
  item(L('Joindre des images','Attach images'),async()=>{const images=await api.host('pick.images');if(currentDraft().images.length+images.length>4)throw new Error('4 images maximum');currentDraft().images.push(...images);renderAssets();});
  item(L('Dossiers sources','Source folders'),()=>projectDialog(selectedProject()));
  const selectedChat=snapshot.chats.find(x=>x.id===chatId);
  if(selectedChat){
    addSandboxMenu(menu, selectedChat);
    const mode=field(menu,L('Mode (prochain envoi)','Mode (next message)'),'select');mode.append(option('execute',L('Exécution','Execution')),option('plan','Plan'));mode.value=selectedChat.executionMode||'execute';
    const orchestration=field(menu,L('Orchestration sous-agents','Subagent orchestration'),'select');for(const [value,label] of [['disabled','Disable'],['auto','Auto'],['forced','Forced']])orchestration.append(option(value,label));orchestration.value=selectedChat.orchestrationMode||'disabled';if(snapshot.providers.find(x=>x.id===providerId)?.kind==='composite'){orchestration.value='forced';orchestration.disabled=true;}
    for(const input of [mode,orchestration])input.onchange=()=>guard(async()=>{await call('chat.modes',{id:selectedChat.id,executionMode:mode.value,orchestrationMode:orchestration.value});await refresh();status(L('Appliqué au prochain envoi','Applied to next message'));});
  }
  const skillDetails=el('details');skillDetails.append(el('summary','Skills'));for(const skill of snapshot.skills){const label=el('label');const check=el('input');check.type='checkbox';check.checked=snapshot.state.enabledSkills.split(',').includes(skill.id);check.onchange=()=>guard(async()=>{const enabled=new Set(snapshot.state.enabledSkills.split(','));check.checked?enabled.add(skill.id):enabled.delete(skill.id);await call('state.save',{enabledSkills:[...enabled].join(',')});await refresh();});label.append(check,document.createTextNode(' '+L(skill.frenchName,skill.englishName)));skillDetails.append(label);}addBrowserSkillControls(skillDetails,true);menu.append(skillDetails);
  const mcpDetails=el('details');mcpDetails.append(el('summary','MCP'));
  for(const server of snapshot.mcpServers||[]){const label=el('label'),check=el('input');check.type='checkbox';check.checked=server.enabled;check.onchange=()=>guard(async()=>{await call('mcp.toggle',{id:server.id,enabled:check.checked});await refresh();});label.append(check,document.createTextNode(' '+server.name));mcpDetails.append(label);}
  button(mcpDetails,L('Configurer MCP…','Configure MCP…'),async()=>{settingsTab='mcp';await showSettings();});menu.append(mcpDetails);
  const templates=el('details');templates.append(el('summary','Templates'));for(const template of snapshot.templates){const button=el('button',template.name);button.onclick=()=>{if($('composer').value&&!confirm(L('Remplacer le brouillon ?','Replace draft?')))return;$('composer').value=template.content;saveDraft();};templates.append(button);}menu.append(templates);menu.hidden=!menu.hidden;
};

document.addEventListener('click',event=>{
  const menu=$('composer-menu');
  if(!menu.hidden&&!event.composedPath().includes(menu)&&!event.composedPath().includes($('plus')))menu.hidden=true;
});
document.addEventListener('keydown',event=>{
  if(event.key==='Escape'&&!$('composer-menu').hidden&&!document.querySelector('dialog[open]')){
    $('composer-menu').hidden=true;$('plus').focus();event.preventDefault();
  }
});
function showTools(visible){$('tools').hidden=!visible;document.body.classList.toggle('with-tools',visible);if(!visible)document.body.classList.remove('tools-full');updateBrowserBounds();}
$('export-chat').onclick=()=>guard(async()=>{
  if(!chatId)return;
  $('export-chat').disabled=true;
  try {
    const result=await api.host('conversation.export',{chatId,providerId});
    status(result.action==='copied'?L('Conversation copiée en Markdown.','Conversation copied as Markdown.'):result.action==='saved'?L('Conversation enregistrée : ','Conversation saved: ')+result.path:L('Export annulé.','Export cancelled.'));
  } finally {$('export-chat').disabled=false;}
});
$('tools-toggle').onclick=()=>showTools($('tools').hidden);$('tools-close').onclick=()=>showTools(false);$('tools-full').onclick=()=>{document.body.classList.toggle('tools-full');updateBrowserBounds();};
function selectTab(next){tab=next;if(next==='terminal')guard(refreshTerminals);if(next==='git')guard(refreshGit);document.querySelectorAll('[data-tab]').forEach(x=>x.classList.toggle('selected',x.dataset.tab===tab));for(const name of ['web','terminal','git','files'])$(name+'-tool').hidden=name!==tab;updateBrowserBounds();}
document.querySelectorAll('[data-tab]').forEach(button=>button.onclick=()=>selectTab(button.dataset.tab));
function updateBrowserBounds(){if(!chatId)return;const r=$('browser-surface').getBoundingClientRect();api.host('browser.bounds',{chatId,x:r.x,y:r.y,width:r.width,height:r.height,visible:featureConfig().BrowserMode==='embedded'&&!$('tools').hidden&&tab==='web'&&!document.querySelector('dialog[open]')}).catch(()=>{});}
new ResizeObserver(updateBrowserBounds).observe($('browser-surface'));window.addEventListener('resize',updateBrowserBounds);
$('web-go').onclick=()=>guard(async()=>{if(featureConfig().BrowserMode!=='embedded')throw new Error(L('Utilisez les outils Chrome MCP dans le chat.','Use Chrome MCP tools in chat.'));await api.host('browser.navigate',{chatId,url:$('address').value});updateBrowserBounds();});$('address').onkeydown=e=>{if(e.key==='Enter')$('web-go').click();};$('web-back').onclick=()=>guard(()=>api.host('browser.back',{chatId}));
$('web-file').onclick=()=>guard(async()=>{const id=chatId;const files=await api.host('pick.file');if(files[0])await call('preview',{chatId:id,path:files[0]});});

let terminalRows=[],terminalScope=null,terminalSelected=null,terminalPolling=false,terminalTabSignature='';
const terminalDrafts=new Map(),terminalSelections=new Map();
function renderTerminalTabs(){
  const signature=terminalRows.map(t=>t.id+':'+t.status+':'+t.name).join('|')+'|'+terminalSelected;
  if(signature!==terminalTabSignature){terminalTabSignature=signature;$('terminal-tabs').replaceChildren();
    for(const t of terminalRows){const group=el('div',null,'terminal-tab'),choose=el('button',t.name+(t.sandbox?' · Sandbox':'')+(t.status==='running'?' ●':'')),close=el('button','×');choose.classList.toggle('selected',t.id===terminalSelected);choose.onclick=()=>{if(terminalSelected)terminalDrafts.set(terminalSelected,$('command').value);terminalSelected=t.id;terminalSelections.set(chatId,t.id);$('command').value=terminalDrafts.get(t.id)||'';renderTerminalTabs();};close.setAttribute('aria-label',L('Fermer ','Close ')+t.name);close.onclick=()=>guard(async()=>{await call('terminals.delete',{chatId:t.chatId,terminalId:t.id});terminalDrafts.delete(t.id);await refreshTerminals();});group.append(choose,close);$('terminal-tabs').append(group);}}
  const selected=terminalRows.find(t=>t.id===terminalSelected);
  $('shell-info').textContent=selected?selected.shell+' · '+selected.status+'\n'+selected.directory:L('Cliquez sur + pour ouvrir un terminal dans cette conversation.','Click + to open a terminal in this conversation.');
  const output=selected?'> '+selected.command+'\n'+selected.output:'';if($('terminal-output').textContent!==output)$('terminal-output').textContent=output;
  $('terminal-run').disabled=!selected||selected.status==='running'||selected.sandbox;$('terminal-stop').disabled=selected?.status!=='running';$('command').disabled=!selected||selected.sandbox;
  $('terminal-add').disabled=!chatId;$('terminal-stop').textContent=L('Arrêter','Stop');
}
async function refreshTerminals(){
  const id=chatId;
  if(terminalScope!==id){if(terminalSelected)terminalDrafts.set(terminalSelected,$('command').value);terminalScope=id;terminalRows=[];terminalSelected=terminalSelections.get(id)||null;$('command').value=terminalDrafts.get(terminalSelected)||'';renderTerminalTabs();}
  if(!id||terminalPolling)return;
  terminalPolling=true;try{const rows=await call('terminals.list',{chatId:id});if(chatId!==id)return;terminalRows=rows;if(!rows.some(t=>t.id===terminalSelected)){terminalSelected=rows[0]?.id||null;$('command').value=terminalDrafts.get(terminalSelected)||'';}renderTerminalTabs();}finally{terminalPolling=false;}
}
$('terminal-add').onclick=()=>guard(async()=>{const id=chatId;const created=await call('terminals.create',{chatId:id});if(chatId===id){if(terminalSelected)terminalDrafts.set(terminalSelected,$('command').value);terminalSelected=created.id;terminalSelections.set(id,created.id);$('command').value='';await refreshTerminals();}});
$('terminal-run').onclick=()=>guard(async()=>{const selected=terminalRows.find(t=>t.id===terminalSelected);if(!selected)return;$('terminal-run').disabled=true;try{await call('terminals.start',{chatId:selected.chatId,terminalId:selected.id,command:$('command').value});}finally{await refreshTerminals();}});
$('terminal-stop').onclick=()=>guard(async()=>{const selected=terminalRows.find(t=>t.id===terminalSelected);if(selected)await call('terminals.stop',{chatId:selected.chatId,terminalId:selected.id});await refreshTerminals();});
setInterval(()=>{if(tab==='terminal'&&!$('tools').hidden)guard(refreshTerminals);},500);

let gitRevision=0,fileRevision=0;
async function refreshGit(){
  const revision=++gitRevision,selectedProjectId=projectId;
  $('git-files').replaceChildren();$('git-diff').replaceChildren();$('git-summary').textContent=L('Chargement…','Loading…');
  const result=await call('git.files',{projectId:selectedProjectId});if(revision!==gitRevision||projectId!==selectedProjectId)return;
  $('git-summary').textContent=!result.hasRepository?L('Aucun dépôt Git dans les sources.','No Git repository in sources.'):result.files.length?L('Modifications depuis le dernier commit','Changes since last commit'):L('Aucun fichier modifié.','No modified files.');
  for(const file of result.files){const button=el('button',file.status.trim()+' · '+file.path+' · '+file.repository.split(/[\\/]/).pop());
    button.onclick=()=>guard(async()=>{const request=++gitRevision;$('git-diff').replaceChildren();
      for(const row of $('git-files').children)row.classList.toggle('selected',row===button);
      const diff=await call('git.diff',{projectId:selectedProjectId,repository:file.repository,path:file.path});if(request!==gitRevision||projectId!==selectedProjectId)return;
      for(const line of diff.split('\n'))$('git-diff').append(el('div',line||' ',line.startsWith('+')?'diff-added':line.startsWith('-')?'diff-removed':line.startsWith('@@')?'diff-hunk':''));
    });$('git-files').append(button);
  }
}
$('git-refresh').onclick=()=>guard(refreshGit);
async function loadFiles(){
  const id=chatId,project=projectId,revision=++fileRevision;
  const text=await call('files.list',{projectId:project,path:$('file-path').value});
  if(id!==chatId||project!==projectId||revision!==fileRevision)return;
  $('file-list').replaceChildren();$('file-content').textContent='';$('file-preview').hidden=true;
  for(const line of text.split('\n').filter(Boolean)){
    const directory=line.startsWith('[dossier] '),name=directory?line.slice(10):line;
    const button=el('button',(directory?'📁 ':'📄 ')+name);
    button.onclick=()=>guard(async()=>{
      if(id!==chatId||project!==projectId)return;
      if(directory){$('file-path').value=name;await loadFiles();}
      else {const request=++fileRevision;const content=await call('files.read',{projectId:project,path:name});
        if(id!==chatId||project!==projectId||request!==fileRevision)return;
        fileSelected=name;$('file-content').textContent=content;$('file-preview').hidden=false;
      }
    });$('file-list').append(button);
  }
}
$('files-go').onclick=()=>guard(loadFiles);$('files-root').onclick=()=>{$('file-path').value='.';guard(loadFiles);};$('file-preview').onclick=()=>guard(async()=>{await call('preview',{chatId,path:fileSelected});selectTab('web');});
document.addEventListener('click',e=>{const a=e.target.closest('a[href]');if(a){e.preventDefault();guard(async()=>{showTools(true);selectTab('web');const href=a.getAttribute('href');if(href.startsWith('omh-file:'))await call('preview',{chatId,path:decodeURIComponent(href.slice(9))});else await api.host('browser.navigate',{chatId,url:a.href});});}});

function field(form,label,type,value){const container=el('label',label),input=el(type==='textarea'?'textarea':type==='select'?'select':'input');if(type!=='textarea'&&type!=='select')input.type=type;if(type==='checkbox')input.checked=!!value;else input.value=value??'';container.append(input);form.append(container);return input;}
function button(parent,label,fn){const b=el('button',label);b.type='button';b.onclick=()=>guard(fn);parent.append(b);return b;}
function addBrowserSkillControls(area,immediate){
  const browser=field(area,L('Accès IA au navigateur','AI browser access'),'checkbox',snapshot.browserAccess);
  const dom=field(area,L('Accès DOM et interaction IA','AI DOM access and interaction'),'checkbox',snapshot.domAccess);
  browser.dataset.browserSkill='access';dom.dataset.browserSkill='dom';
  if(!immediate)for(const input of [browser,dom]){const label=input.parentElement,card=el('section',null,'skill-card');label.before(card);card.append(label);}
  if(immediate)for(const input of [browser,dom])input.onchange=()=>guard(async()=>{await call('browser.access',{enabled:browser.checked,dom:dom.checked});await refresh();});
  return {browser,dom};
}
async function showSettings(){await refresh();renderSettings();showDialog('settings');}
$('settings-open').onclick=()=>guard(showSettings);document.querySelectorAll('[data-settings]').forEach(b=>b.onclick=()=>{settingsTab=b.dataset.settings;renderSettings();});
function renderSettings(){
  document.querySelectorAll('[data-settings]').forEach(b=>b.classList.toggle('selected',b.dataset.settings===settingsTab));const area=$('settings-content');area.replaceChildren();
  const selectedTab=document.querySelector('[data-settings="'+settingsTab+'"]');
  area.append(el('h3',selectedTab?.textContent||''));
  $('settings').querySelector('h2').textContent=L('Réglages','Settings');
  if(settingsTab==='browser'){renderFeatureSettings(area);
  }else if(settingsTab==='general'){
    const language=field(area,L('Langue','Language'),'select');language.append(option('fr','Français'),option('en','English'));language.value=snapshot.state.language;
    const showReasoning=field(area,L('Afficher les détails du raisonnement','Show reasoning details'),'checkbox',snapshot.state.showReasoningDetails!==false);
    const auto=field(area,L('Continuer automatiquement après 12 étapes','Automatically continue after 12 steps'),'checkbox',snapshot.state.autoContinue);
    area.append(el('p',L('Poursuit les outils jusqu’à la réponse finale ou Arrêter. Des tokens supplémentaires peuvent être consommés ; les autorisations restent applicables.','Continue tools until the final answer or Stop. May consume additional tokens; permissions still apply.'),'muted'));
    button(area,L('Enregistrer','Save'),async()=>{await call('state.save',{language:language.value,autoContinue:auto.checked,showReasoningDetails:showReasoning.checked});await refresh();renderSettings();renderMessages();});
    area.append(el('p',snapshot.database,'muted'));button(area,L('Vérifier les autorisations système','Check system permissions'),async()=>{const result=await api.host('system.permissions');area.append(el('pre',JSON.stringify(result,null,2)));});
  }else if(settingsTab==='skills'){
    area.append(el('p',L('Skills personnalisés : copiez un dossier contenant SKILL.md ici, puis rouvrez les réglages. Le modèle exemple-revue est fourni.','Custom skills: copy a folder containing SKILL.md here, then reopen settings. The exemple-revue template is included.')+' '+snapshot.skillsDirectory,'muted'));
    const browserSkills=addBrowserSkillControls(area,false);
    let ragSave=()=>snapshot.state.featuresJson;for(const skill of snapshot.skills){const card=el('section',null,'skill-card');area.append(card);const label=field(card,L(skill.frenchName,skill.englishName),'checkbox',snapshot.state.enabledSkills.split(',').includes(skill.id));label.dataset.skill=skill.id;card.append(el('p',L(skill.frenchDescription,skill.englishDescription),'muted'));if(skill.id==='rag'){const settings=el('div',null,'card');settings.dataset.ragSettings='';card.append(settings);ragSave=renderFeatureSettings(settings,true);settings.hidden=!label.checked;label.onchange=()=>settings.hidden=!label.checked;}}
    button(area,L('Enregistrer','Save'),async()=>{await call('browser.access',{enabled:browserSkills.browser.checked,dom:browserSkills.dom.checked});await call('state.save',{featuresJson:ragSave(),enabledSkills:[...area.querySelectorAll('[data-skill]:checked')].map(x=>x.dataset.skill).join(',')});await refresh();status(L('Skills enregistrés','Skills saved'));});
  }else if(settingsTab==='mcp'){
    area.append(el('p',L('Outils MCP pour les API compatibles OpenAI/DeepSeek. OpenCode utilise sa propre configuration MCP.','MCP tools for OpenAI/DeepSeek compatible APIs. OpenCode uses its own MCP configuration.'),'muted'));
    button(area,L('＋ Ajouter un serveur MCP','＋ Add MCP server'),()=>mcpForm({transport:'stdio',argumentsJson:'[]'}));
    for(const server of snapshot.mcpServers||[]){const card=el('div',null,'card');card.append(el('strong',server.name),el('p',server.transport+' · '+(server.transport==='stdio'?server.command:server.url)));
      const enabled=field(card,L('Activé','Enabled'),'checkbox',server.enabled);enabled.onchange=()=>guard(async()=>{await call('mcp.toggle',{id:server.id,enabled:enabled.checked});await refresh();});
      button(card,L('Modifier','Edit'),()=>mcpForm(server));button(card,L('Tester la connexion','Test connection'),async()=>{const result=await call('mcp.test',{id:server.id});card.append(el('pre',result.error||result.tools.length+' tools\n'+result.tools.join('\n')));});
      button(card,L('Supprimer','Delete'),async()=>{if(!confirm(L('Supprimer ce serveur MCP ?','Delete this MCP server?')))return;await call('mcp.delete',{id:server.id});await refresh();renderSettings();});area.append(card);}
  }else if(settingsTab==='permissions'){
    const mode=field(area,L('Comportement des autorisations','Permission behavior'),'select');mode.append(option('deny',L('Refuser tout','Deny all')),option('ask',L('Demander (défaut)','Ask (default)')),option('allow',L('Acceptation automatique','Automatically allow')));mode.value=snapshot.state.permissionMode;
    button(area,L('Enregistrer','Save'),async()=>{await call('state.save',{permissionMode:mode.value});await refresh();renderSettings();});
    for(const grant of snapshot.permissions){const card=el('div',null,'card');card.append(el('strong',grant.name),el('p',grant.details));button(card,L('Révoquer','Revoke'),async()=>{await call('permission.revoke',{id:grant.id});await refresh();renderSettings();});area.append(card);}
  }else if(settingsTab==='providers'){
    const actions=el('div',null,'form-actions');button(actions,L('＋ Modèle composé','＋ Composite model'),()=>compositeForm({}));for(const kind of ['openai','deepseek','opencode'])button(actions,'＋ '+kind,()=>providerForm({kind,baseUrl:kind==='opencode'?'http://127.0.0.1:4096':kind==='deepseek'?'https://api.deepseek.com':'https://api.openai.com/v1',name:kind,contextLimit:128000,supportsImages:true}));area.append(actions);
    for(const provider of snapshot.providers){const card=el('div',null,'card');card.append(el('strong',provider.name),el('p',provider.model+' · '+provider.baseUrl));button(card,L('Modifier','Edit'),()=>providerForm(provider));button(card,L('Dupliquer','Duplicate'),()=>providerForm({...provider,id:0,name:provider.name+' copy',hasKey:false}));button(card,L('Supprimer','Delete'),async()=>{if(confirm(L('Supprimer ce fournisseur ?','Delete provider?'))){await call('provider.delete',{id:provider.id});await refresh();renderSettings();}});area.append(card);}
  }else if(settingsTab==='templates'){
    button(area,L('＋ Nouveau template','＋ New template'),()=>templateForm({}));for(const template of snapshot.templates){const card=el('div',null,'card');card.append(el('strong',template.name));button(card,L('Modifier','Edit'),()=>templateForm(template));button(card,L('Supprimer','Delete'),async()=>{await call('template.delete',{id:template.id});await refresh();renderSettings();});area.append(card);}
  }
}
function mcpForm(server){
  const area=$('settings-content');area.replaceChildren();const form=el('form');area.append(form);
  const name=field(form,L('Nom','Name'),'text',server.name),enabled=field(form,L('Activé','Enabled'),'checkbox',server.enabled),transport=field(form,'Transport','select');
  name.required=true;['stdio','http','sse'].forEach(value=>transport.append(option(value,value)));transport.value=server.transport||'stdio';
  const command=field(form,L('Commande / exécutable','Command / executable'),'text',server.command),args=field(form,L('Arguments (tableau JSON)','Arguments (JSON array)'),'textarea',server.argumentsJson||'[]'),directory=field(form,L('Dossier de travail (facultatif)','Working directory (optional)'),'text',server.workingDirectory),url=field(form,'URL MCP','url',server.url);
  const secret=field(form,L('Secrets JSON (vide : conserver)','Secrets JSON (blank: keep existing)'),'password',''),clear=field(form,L('Effacer les secrets enregistrés','Clear saved secrets'),'checkbox',false);
  secret.placeholder='{"environment":{"TOKEN":"…"},"headers":{"Authorization":"Bearer …"}}';
  function update(){const local=transport.value==='stdio';for(const input of [command,args,directory])input.parentElement.hidden=!local;url.parentElement.hidden=local;command.required=local;url.required=!local;}
  transport.onchange=update;update();
  const error=el('p',null,'error'),save=el('button',L('Enregistrer','Save'),'accent');form.append(error,save);button(form,L('Annuler','Cancel'),()=>renderSettings());
  form.onsubmit=e=>{e.preventDefault();guard(async()=>{save.disabled=true;try{await call('mcp.save',{id:server.id||0,name:name.value,enabled:enabled.checked,transport:transport.value,command:command.value,argumentsJson:args.value,workingDirectory:directory.value,url:url.value,secrets:secret.value,clearSecrets:clear.checked});secret.value='';await refresh();renderSettings();}catch(ex){error.textContent=ex.message;}finally{save.disabled=false;}});};
}
function providerForm(provider){
  if(provider.kind==='composite')return compositeForm(provider);
  const area=$('settings-content');area.replaceChildren();const form=el('form');area.append(form);
  const name=field(form,L('Nom','Name'),'text',provider.name),url=field(form,'URL','url',provider.baseUrl),model=field(form,L('Modèle','Model'),'text',provider.model),key=field(form,L('Clé API / mot de passe (vide : conserver)','API key / password (blank: keep existing)'),'password','');
  const limit=field(form,L('Limite de contexte','Context limit'),'number',provider.contextLimit),vision=field(form,L('Images acceptées','Supports images'),'checkbox',provider.supportsImages),deleteKey=field(form,L('Supprimer la clé enregistrée','Delete saved key'),'checkbox',false);
  let username,executable,autoStart,openCodeTools;
  if(provider.kind==='opencode'){username=field(form,L('Utilisateur','Username'),'text',provider.username||'opencode');executable=field(form,L('Exécutable CLI opencode (facultatif)','opencode CLI executable (optional)'),'text',provider.executablePath);autoStart=field(form,L('Démarrer automatiquement le serveur','Start server automatically'),'checkbox',provider.autoStart);openCodeTools=field(form,L('Activer les outils OpenCode','Enable OpenCode tools'),'checkbox',provider.openCodeTools);}
  if(provider.id)button(form,L('Charger les modèles / tester','Load models / test'),async()=>{const models=await call('provider.models',{id:provider.id});const select=field(form,L('Modèles disponibles','Available models'),'select');models.forEach(x=>select.append(option(x,x)));select.onchange=()=>model.value=select.value;});
  const actions=el('div',null,'form-actions');button(actions,L('Retour','Back'),()=>renderSettings());const save=el('button',L('Enregistrer','Save'),'accent');save.type='submit';actions.append(save);form.append(actions);
  form.onsubmit=e=>{e.preventDefault();guard(async()=>{await call('provider.save',{id:provider.id||0,kind:provider.kind,name:name.value,baseUrl:url.value,model:model.value,key:key.value,contextLimit:Number(limit.value),supportsImages:vision.checked,deleteKey:deleteKey.checked,username:username?.value||'',executablePath:executable?.value||'',autoStart:autoStart?.checked||false,openCodeTools:openCodeTools?.checked||false});key.value='';await refresh();renderSettings();});};
}
function templateForm(template){const area=$('settings-content');area.replaceChildren();const form=el('form');area.append(form);const name=field(form,L('Nom','Name'),'text',template.name),content=field(form,L('Contenu','Content'),'textarea',template.content);content.rows=13;const save=el('button',L('Enregistrer','Save'),'accent');form.append(save);button(form,L('Retour','Back'),()=>renderSettings());form.onsubmit=e=>{e.preventDefault();guard(async()=>{await call('template.save',{id:template.id||0,name:name.value,content:content.value});await refresh();renderSettings();});};}
guard(async()=>{await refresh();const preferred=snapshot.chats.find(x=>x.id===snapshot.state.chatId&&x.projectId===projectId)||snapshot.chats.find(x=>x.projectId===projectId);await selectChat(preferred?.id);status(L('Prêt','Ready'));});

function renderTasks(message){
  const node=el('details',null,'message task-list');node.dataset.message=message.id;node.open=true;
  let tasks=[];try{tasks=JSON.parse(message.content);}catch{}
  node.append(el('summary',L('Étapes du travail','Work steps')+' · '+tasks.filter(x=>x.status==='completed').length+'/'+tasks.length));
  for(const task of tasks){const labels={pending:L('○ À faire','○ To do'),in_progress:L('◉ En cours','◉ In progress'),completed:L('✓ Terminée','✓ Completed'),cancelled:L('— Annulée','— Cancelled')};node.append(el('p',(labels[task.status]||task.status)+' · '+task.content,'task-'+task.status));}
  return node;
}
function renderQuestions(){
  const region=$('questions');if(!region)return;
  const visible=[...pendingQuestions.values()].filter(x=>x.chatId===chatId);
  for(const card of [...region.children])if(!pendingQuestions.has(card.dataset.question))card.remove();
  for(const request of pendingQuestions.values()){
    if([...region.children].some(x=>x.dataset.question===request.id))continue;
    const form=el('form',null,'question-card');form.dataset.question=request.id;form.append(el('h3',L('Réponse attendue','Waiting for your answer')));
    const readers=[];
    request.questions.forEach((q,index)=>{
      const field=el('fieldset');field.append(el('legend',q.question));const options=[];
      for(const option of q.options){const label=el('label',null,'question-option'),input=el('input');input.type=q.multiple?'checkbox':'radio';input.name=request.id+'-'+index;input.value=option.label;options.push(input);label.append(input,el('span',option.label+(option.description?' — '+option.description:'')));field.append(label);}
      let free;if(q.custom){free=el('textarea');free.placeholder=L('Votre réponse…','Your answer…');free.setAttribute('aria-label',free.placeholder);free.maxLength=4000;free.rows=2;field.append(free);}
      readers.push(()=>{let values=options.filter(x=>x.checked).map(x=>x.value);if(free?.value.trim()){if(!q.multiple)values=[];values.push(free.value.trim());}return values;});form.append(field);
    });
    const error=el('p',null,'error'),actions=el('div',null,'form-actions'),submit=el('button',L('Répondre et reprendre','Answer and resume'),'accent'),cancel=el('button',L('Annuler la question','Dismiss question'));cancel.type='button';submit.type='submit';actions.append(cancel,submit);form.append(error,actions);
    const answer=async cancelled=>{const answers=cancelled?[]:readers.map(read=>read());if(!cancelled&&answers.some(x=>!x.length)){error.textContent=L('Répondez à chaque question.','Answer every question.');return;}submit.disabled=cancel.disabled=true;try{await call('question.answer',{id:request.id,chatId:request.chatId,cancelled,answers});pendingQuestions.delete(request.id);renderQuestions();}catch(e){error.textContent=e.message;submit.disabled=cancel.disabled=false;}};
    form.onsubmit=e=>{e.preventDefault();answer(false);};cancel.onclick=()=>answer(true);region.append(form);
  }
  for(const card of region.children)card.hidden=pendingQuestions.get(card.dataset.question)?.chatId!==chatId;
  region.hidden=visible.length===0;
}

function closeContextPopover(){clearTimeout(contextCloseTimer);$('context-popover').hidden=true;$('context-area').setAttribute('aria-expanded','false');}
async function openContextPopover(){
  clearTimeout(contextCloseTimer);if(!$('context-popover').hidden)return;$('context-popover').hidden=false;$('context-area').setAttribute('aria-expanded','true');
  const id=chatId,provider=providerId;if(contextDetailChat!==id)contextDetail=null;contextDetailChat=id;renderContextDetail();
  if(!id||!provider)return;
  try{const detail=await call('context.details',{chatId:id,providerId:provider});if(chatId===id&&providerId===provider){contextDetail=detail;renderContextDetail();}}
  catch(error){if(chatId===id)$('context-detail').textContent=error.message;}
}
function renderContextDetail(){
  if(!$('context-detail'))return;
  const d=contextDetailChat===chatId?contextDetail:null,live=metrics.get(chatId),button=$('context-compact');
  button.textContent=L('Compacter maintenant','Compact now');button.disabled=!d||!d.activeMessages||running.has(chatId);
  if(!d){$('context-detail').textContent=L('Chargement…','Loading…');return;}
  const used=live?.tokens??d.used,limit=live?.limit??d.limit;
  $('context-detail').textContent=[
    ((live?.estimated??d.estimated)?'≈ ':'')+used.toLocaleString()+' / '+limit.toLocaleString()+' tokens · '+(used*100/Math.max(1,limit)).toFixed(1)+' %',
    L('Disponible : ','Remaining: ')+Math.max(0,limit-used).toLocaleString(),
    L('Dernière entrée déclarée : ','Last reported input: ')+(d.input?.toLocaleString()??'—'),
    L('Dernière sortie déclarée : ','Last reported output: ')+(d.output?.toLocaleString()??'—'),'',
    L('Historique enregistré — estimations','Saved history — estimates'),
    L('Vos messages : ','Your messages: ')+d.user.toLocaleString(),L('Réponses : ','Responses: ')+d.assistant.toLocaleString(),
    L('Résultats d’outils : ','Tool results: ')+d.tools.toLocaleString(),L('Résumé : ','Summary: ')+d.summary.toLocaleString(),
    L('Images (approximation) : ','Images (approximation): ')+d.images.toLocaleString(),'',
    L('Détail hors instructions système et définitions d’outils : il peut différer du total. Compactage automatique à 95 %. Le compactage utilise le modèle et peut consommer des tokens.',
      'Breakdown excludes system instructions and tool definitions; it may differ from the total. Auto-compaction at 95%. Compaction uses the model and may consume tokens.'),
    running.has(chatId)?L('Disponible après la réponse.','Available after the response.'):''].join('\n');
}
function bindMetricPopover(areaId,popoverId,open,close){
  const area=$(areaId),popover=$(popoverId);let timer;
  const enter=()=>{clearTimeout(timer);if(popover.hidden)guard(open);};
  const leave=()=>{clearTimeout(timer);timer=setTimeout(()=>{if(!area.matches(':hover')&&!popover.matches(':hover')&&!area.contains(document.activeElement))close();},400);};
  area.onmouseenter=enter;popover.onmouseenter=enter;area.onmouseleave=leave;popover.onmouseleave=leave;
  area.onfocusin=enter;area.onfocusout=event=>{if(!area.contains(event.relatedTarget))leave();};
  area.onkeydown=event=>{if(event.key==='Escape'){clearTimeout(timer);close();event.stopPropagation();}};
  document.addEventListener('click',event=>{if(!area.contains(event.target)){clearTimeout(timer);close();}});
}
bindMetricPopover('context-area','context-popover',openContextPopover,closeContextPopover);
function renderSpeedDetail(){
  if(!$('speed-detail'))return;
  const value=metrics.get(chatId),valid=Number.isFinite(value?.speedAverage),format=n=>Number.isFinite(n)?n.toFixed(1)+' tok/s':'—';
  $('speed-title').textContent=L('Débit du modèle','Model throughput');
  const lines=[];
  if(valid){lines.push(L('Réponse courante / dernière réponse','Current / last response')+(value.speedEstimated?' ≈':''),L('Minimum : ','Minimum: ')+format(value.speedMin),L('Moyenne : ','Average: ')+format(value.speedAverage),L('Maximum : ','Maximum: ')+format(value.speedMax));}
  const messages=(histories.get(chatId)||[]).filter(m=>m.role==='assistant'&&m.state==='complete'&&m.outputTokens>0&&m.seconds>0);
  if(messages.length){const rates=messages.map(m=>m.outputTokens/Math.max(.1,m.seconds));if(lines.length)lines.push('');lines.push(L('Conversation · moyennes des réponses','Conversation · response averages'),L('Minimum : ','Minimum: ')+format(Math.min(...rates)),L('Moyenne : ','Average: ')+format(messages.reduce((sum,m)=>sum+m.outputTokens,0)/messages.reduce((sum,m)=>sum+Math.max(.1,m.seconds),0)),L('Maximum : ','Maximum: ')+format(Math.max(...rates)));}
  if(!lines.length)lines.push(L('Minimum : —\nMoyenne : —\nMaximum : —\nAucune mesure disponible.','Minimum: —\nAverage: —\nMaximum: —\nNo measurements yet.'));
  lines.push('',L('Les mesures en cours peuvent être estimées.','Live measurements may be estimated.'));
  $('speed-detail').textContent=lines.join('\n');
}
function closeSpeedPopover(){$('speed-popover').hidden=true;$('speed-area').setAttribute('aria-expanded','false');}
bindMetricPopover('speed-area','speed-popover',()=>{renderSpeedDetail();$('speed-popover').hidden=false;$('speed-area').setAttribute('aria-expanded','true');},closeSpeedPopover);
$('context-compact').onclick=()=>guard(async()=>{
  if(running.has(chatId)||!chatId||!providerId)return;
  const id=chatId,provider=providerId;inflight.add(id);running.add(id);renderChats();updateControls();closeContextPopover();
  try{const result=await call('context.compact',{chatId:id,providerId:provider});const d=result.details;metrics.set(id,{tokens:d.used,limit:d.limit,estimated:d.estimated,speed:0});histories.set(id,await call('history',{chatId:id}));if(chatId===id){contextDetail=d;contextDetailChat=id;renderMessages();renderMetrics();}}
  finally{inflight.delete(id);running.delete(id);renderChats();updateControls();}
});

function addSandboxMenu(menu, selectedChat){
  const row=el('div',null,'sandbox-toggle'),label=el('label'),toggle=el('input');toggle.type='checkbox';toggle.checked=!!selectedChat.sandboxEnabled;
  toggle.onchange=()=>guard(async()=>{await call('chat.modes',{id:selectedChat.id,sandboxEnabled:toggle.checked});await refresh();status(L('Sandbox appliquée au prochain envoi','Sandbox applies to the next message'));});
  label.append(toggle,document.createTextNode(' Sandbox'));
  const info=el('button','ⓘ');info.type='button';info.setAttribute('aria-label',L('Fonctionnement de la sandbox','How sandbox works'));
  const explanation=el('p',L('Copie privée par conversation. Commandes Linux dans Docker/Podman sans réseau ni accès aux dossiers du PC. Image requise : node:22-bookworm, à télécharger au préalable. 1 CPU, 512 Mio, 128 processus, 30 s par défaut, jusqu’à 10 min/commande, 30 min/génération ; sources limitées à 64 Mio. Clés API et SQLite hors du conteneur. Navigateur, bureau, MCP et OpenCode désactivés. Secrets connus, dépendances et .git exclus ; vérifiez les secrets présents dans votre code. Les modifications ne sont appliquées au projet réel qu’après votre revue. Le panneau outils manuel reste local. Aucun repli local automatique.', 'Private copy per conversation. Linux commands in Docker/Podman with no network or host folders. Required image: node:22-bookworm, download beforehand. 1 CPU, 512 MiB, 128 processes, 30 s default, up to 10 min/command, 30 min/generation; sources limited to 64 MiB. API keys and SQLite outside the container. Browser, desktop, MCP and OpenCode disabled. Known secrets, dependencies and .git excluded; check for secrets embedded in code. Changes reach the original project only after review. Manual tools remain local. Never falls back to local execution.'),'sandbox-info');explanation.hidden=true;
  info.onclick=()=>{explanation.hidden=!explanation.hidden;info.setAttribute('aria-expanded',String(!explanation.hidden));};info.setAttribute('aria-expanded','false');row.append(label,info);menu.append(row,explanation);
  const review=el('button',L('Examiner les modifications sandbox…','Review sandbox changes…'));review.disabled=running.has(selectedChat.id);review.onclick=()=>guard(()=>reviewSandbox(selectedChat.id));menu.append(review);
}
async function reviewSandbox(id){
  const result=await call('sandbox.review',{chatId:id});
  const dialog=el('dialog'),title=el('h2',L('Sandbox → projet réel','Sandbox → original project')),diff=el('pre',result.diff),close=el('button',L('Fermer','Close')),apply=el('button',L('Appliquer ces modifications','Apply these changes'));
  dialog.className='sandbox-review';apply.disabled=result.count===0;close.onclick=()=>dialog.close();apply.onclick=()=>guard(async()=>{apply.disabled=true;try{await call('sandbox.apply',{chatId:id,token:result.token});status(L('Modifications appliquées au projet réel','Changes applied to original project'));dialog.close();}catch(error){dialog.close();throw error;}});
  dialog.append(title,diff,apply,close);document.body.append(dialog);dialog.addEventListener('close',()=>{call('sandbox.close',{token:result.token}).catch(()=>{});dialog.remove();updateBrowserBounds();},{once:true});dialog.showModal();updateBrowserBounds();
}
