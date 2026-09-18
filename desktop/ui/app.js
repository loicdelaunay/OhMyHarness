const $ = id => document.getElementById(id), api = window.harness;
let snapshot, chatId, projectId, providerId, tab='web', settingsTab='general', projectEdit=null, projectFolders=[], fileSelected='';
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
  document.documentElement.lang=snapshot.state.language;
  $('new-chat').textContent=L('＋ Nouvelle conversation','＋ New conversation');$('settings-open').textContent=L('⚙ Réglages','⚙ Settings');
  $('tools-toggle').textContent=L('▤ Outils','▤ Tools');$('tools-title').textContent=L('Outils','Tools');$('composer').placeholder=L('Posez une question…','Ask a question…');
  $('shortcut').textContent=L('Entrée ↵ · Ctrl+Entrée : nouvelle ligne','Enter ↵ · Ctrl+Enter: new line');
  $('terminal-run').textContent=L('Exécuter','Run');$('git-refresh').textContent=L('Actualiser','Refresh');
  $('shell-info').textContent=snapshot.shell+' · '+L('Dossier du projet · nouvelle session, 60 s maximum','Project directory · fresh session, 60 s maximum');
  $('file-preview').textContent=L('Ouvrir dans Web','Open in browser');
  const settingNames={general:L('Général','General'),providers:L('Fournisseurs','Providers'),skills:'Skills',permissions:L('Autorisations','Permissions'),templates:'Templates'};
  document.querySelectorAll('[data-settings]').forEach(button=>button.textContent=settingNames[button.dataset.settings]);
  document.querySelector('[data-tab="files"]').textContent=L('Fichiers','Files');
}
async function refresh(){
  snapshot=await call('snapshot');
  running.clear();snapshot.running.forEach(id=>running.add(id));inflight.forEach(id=>running.add(id));
  projectId=snapshot.projects.some(x=>x.id===projectId)?projectId:(snapshot.state.projectId||snapshot.projects[0]?.id);
  providerId=snapshot.providers.some(x=>x.id===providerId)?providerId:(snapshot.state.providerId||snapshot.providers[0]?.id);
  if(!snapshot.providers.some(x=>x.id===providerId))providerId=snapshot.providers[0]?.id;
  $('platform').textContent=snapshot.platform;$('projects').replaceChildren(...snapshot.projects.map(p=>option(p.id,p.name)));$('projects').value=projectId;
  $('providers').replaceChildren(...snapshot.providers.map(p=>option(p.id,`${p.name} · ${p.model}`)));$('providers').value=providerId||'';
  $('thinking').value=snapshot.state.thinkingLevel;$('browser-access').checked=snapshot.browserAccess;$('dom-access').checked=snapshot.domAccess;
  translate();renderChats();updateControls();
}
function renderChats(){
  $('chats').replaceChildren();
  for(const chat of snapshot.chats.filter(x=>x.projectId===projectId).reverse()){
    const button=el('button',null,'chat-row'+(chat.id===chatId?' selected':''));button.append(el('span',chat.title));button.title=chat.title;
    if(running.has(chat.id)){const progress=el('progress');progress.setAttribute('aria-label',L('Génération en cours','Generating'));button.append(progress);}
    button.onclick=()=>guard(()=>selectChat(chat.id));$('chats').append(button);
  }
}
async function selectChat(id){
  saveDraft();chatId=id;
  $('chat-title').textContent=snapshot.chats.find(x=>x.id===id)?.title||L('Créez une conversation','Create a conversation');
  $('composer').value=currentDraft().text;renderAssets();renderChats();updateControls();renderMetrics();
  status(statuses.get(id)||'');
  if(!id){$('messages').replaceChildren(el('p',L('Créez une conversation pour commencer.','Create a conversation to start.'),'empty'));return;}
  if(!histories.has(id)||!running.has(id))histories.set(id,await call('history',{chatId:id}));
  if(chatId!==id)return;renderMessages(true);
  await call('state.save',{chatId:id,projectId});
}
function updateControls(){$('send').disabled=!chatId||!providerId||running.has(chatId);$('stop').disabled=!running.has(chatId);$('rename-chat').disabled=$('delete-chat').disabled=!chatId;$('new-chat').disabled=!projectId;}
function renderAssets(){
  $('assets').replaceChildren();currentDraft().images.forEach((image,index)=>{const box=el('div',null,'asset');const img=el('img');img.src=`data:${image.mime};base64,${image.data}`;img.alt=image.name;const remove=el('button','×');remove.onclick=()=>{currentDraft().images.splice(index,1);renderAssets();};box.append(img,remove);$('assets').append(box);});
}
function renderMessage(message){
  const node=el('article',null,'message '+message.role);node.dataset.message=message.id;node.append(el('div',message.role==='user'?L('VOUS','YOU'):message.role==='tool'?L('OUTIL','TOOL'):L('ASSISTANT','ASSISTANT'),'role'));
  if(message.reasoning){const details=el('details');details.open=running.has(chatId)&&message.state!=='complete';details.append(el('summary',L('Raisonnement du modèle','Model reasoning')));details.append(el('div',message.reasoning,'reasoning'));node.append(details);}
  const body=el('div',null,'body');if(message.html)body.innerHTML=message.html;else body.textContent=message.content||'…';node.append(body);
  for(const image of message.attachments||[]){const img=el('img',null,'attachment');img.src=`data:${image.mime};base64,${image.data}`;img.alt=image.name;node.append(img);}
  if(message.state==='interrupted'&&!running.has(chatId))node.append(el('p',L('Réponse interrompue','Interrupted response'),'interrupted'));
  return node;
}
function renderMessages(bottom=false){
  const list=histories.get(chatId)||[];
  $('messages').replaceChildren(...list.map(renderMessage));
  if(!list.length)$('messages').append(el('p',L('Un espace pour vos idées.\nDes outils pour aller plus loin.','A space for your ideas.\nTools to go further.'),'empty'));
  if(bottom)$('messages').scrollTop=$('messages').scrollHeight;
}
function updateMessage(id,message){
  if(!histories.has(id))histories.set(id,[]);const list=histories.get(id);const index=list.findIndex(x=>x.id===message.id);
  if(index<0)list.push(message);else list[index]={...list[index],...message};
  if(id!==chatId)return;
  const scroll=$('messages'),bottom=scroll.scrollHeight-scroll.scrollTop-scroll.clientHeight<220;
  const old=scroll.querySelector(`[data-message="${message.id}"]`),node=renderMessage(index<0?message:list[index]);
  if(old)old.replaceWith(node);else{scroll.querySelector('.empty')?.remove();scroll.append(node);}
  const reasoning=node.querySelector('.reasoning');if(reasoning)reasoning.scrollTop=reasoning.scrollHeight;
  if(bottom)scroll.scrollTop=scroll.scrollHeight;
}
function renderMetrics(){
  const value=metrics.get(chatId);$('speed').textContent=value?`${value.estimated?'≈ ':''}${value.speed.toFixed(1)} tok/s`:'— tok/s';
  $('context').textContent=value?`${value.estimated?'≈ ':''}${value.tokens.toLocaleString()} / ${value.limit.toLocaleString()}`:'— tokens';
  $('context-progress').value=value?Math.min(100,value.tokens/value.limit*100):0;
}
async function send(){
  if($('send').disabled)return;saveDraft();const id=chatId,draft=currentDraft();if(!draft.text.trim()&&!draft.images.length)return;
  const text=draft.text,images=draft.images;draft.text='';draft.images=[];$('composer').value='';renderAssets();
  inflight.add(id);running.add(id);renderChats();updateControls();statuses.set(id,L('Le modèle réfléchit…','Model is thinking…'));status(statuses.get(id));
  try{await call('send',{chatId:id,providerId,text,images});}
  catch(error){status(error.message,true);if(!(histories.get(id)||[]).some(x=>x.role==='user'&&x.content===text)){const next=drafts.get(id);next.text=text+(next.text?'\n'+next.text:'');next.images.unshift(...images);if(chatId===id){$('composer').value=next.text;renderAssets();}}}
  finally{inflight.delete(id);running.delete(id);renderChats();updateControls();if(chatId===id){histories.set(id,await call('history',{chatId:id}));renderMessages(true);}}
}
api.onEvent(event=>{
  if(event.event==='fatal'){status(event.error,true);return;}
  if(event.event==='browser'){$('address').value=event.url;return;}
  if(event.event==='stream'){
    running.add(event.chatId);metrics.set(event.chatId,event);updateMessage(event.chatId,{id:event.messageId,role:'assistant',content:event.text,html:event.html,reasoning:event.reasoning,state:'streaming'});if(event.chatId===chatId)renderMetrics();
  }else if(event.event==='message'){
    if(event.title){const chat=snapshot.chats.find(x=>x.id===event.chatId);if(chat)chat.title=event.title;if(event.chatId===chatId)$('chat-title').textContent=event.title;renderChats();}
    updateMessage(event.chatId,event.message);
  }else if(event.event==='status'){statuses.set(event.chatId,event.text);if(event.chatId===chatId)status(event.text);}
  else if(event.event==='done'){running.delete(event.chatId);statuses.set(event.chatId,event.error||L('Réponse terminée.','Response complete.'));renderChats();updateControls();if(event.chatId===chatId)status(statuses.get(event.chatId),!!event.error);}
});
$('send').onclick=()=>guard(send);$('stop').onclick=()=>guard(()=>call('stop',{chatId}));
$('composer').oninput=saveDraft;$('composer').onkeydown=e=>{if(e.key==='Enter'&&!e.isComposing){e.preventDefault();if(e.ctrlKey){const t=e.target;t.setRangeText('\n',t.selectionStart,t.selectionEnd,'end');saveDraft();}else guard(send);}};
$('projects').onchange=()=>guard(async()=>{saveDraft();projectId=Number($('projects').value);renderChats();await selectChat(snapshot.chats.find(x=>x.projectId===projectId)?.id);});
$('providers').onchange=()=>guard(async()=>{providerId=Number($('providers').value);await call('state.save',{providerId});});
$('thinking').onchange=()=>guard(()=>call('state.save',{thinkingLevel:$('thinking').value}));
$('new-chat').onclick=()=>guard(async()=>{const result=await call('chat.save',{projectId,title:L('Nouvelle conversation','New conversation')});await refresh();await selectChat(result.id);});
$('rename-chat').onclick=()=>{$('new-name').value=snapshot.chats.find(x=>x.id===chatId)?.title||'';showDialog('name-dialog');};
$('name-form').onsubmit=e=>{e.preventDefault();guard(async()=>{await call('chat.save',{id:chatId,title:$('new-name').value});$('name-dialog').close();await refresh();$('chat-title').textContent=$('new-name').value;});};
$('delete-chat').onclick=()=>guard(async()=>{if(!confirm(L('Supprimer cette conversation ?','Delete this conversation?')))return;await call('chat.delete',{id:chatId});await refresh();await selectChat(snapshot.chats.find(x=>x.projectId===projectId)?.id);});
function showDialog(id){$('composer-menu').hidden=true;$(id).showModal();updateBrowserBounds();}
document.querySelectorAll('[data-close]').forEach(button=>button.onclick=()=>$(button.dataset.close).close());
document.querySelectorAll('dialog').forEach(dialog=>dialog.addEventListener('close',updateBrowserBounds));
function projectDialog(existing){projectEdit=existing||null;projectFolders=existing?.sourceFolder?.split(/[|;\r\n]/).filter(Boolean)||[];$('project-name').value=existing?.name||'';$('project-delete').hidden=!existing;renderSources();showDialog('project-dialog');}
function renderSources(){$('project-sources').replaceChildren(...projectFolders.map((folder,i)=>{const row=el('div',null,'source-item');const remove=el('button','×');remove.type='button';remove.onclick=()=>{projectFolders.splice(i,1);renderSources();};row.append(el('span',folder),remove);return row;}));}
$('new-project').onclick=()=>projectDialog();$('manage-project').onclick=()=>projectDialog(selectedProject());
$('project-folders').onclick=()=>guard(async()=>{projectFolders=[...new Set([...projectFolders,...await api.host('pick.folders')])];renderSources();});
$('project-form').onsubmit=e=>{e.preventDefault();guard(async()=>{const result=await call('project.save',{id:projectEdit?.id||0,name:$('project-name').value,folders:projectFolders});projectId=result.id;$('project-dialog').close();await refresh();await selectChat(snapshot.chats.find(x=>x.projectId===projectId)?.id);});};
$('project-delete').onclick=()=>guard(async()=>{if(!confirm(L('Supprimer le projet et son historique ?','Delete this project and its history?')))return;await call('project.delete',{id:projectEdit.id});$('project-dialog').close();projectId=null;await refresh();await selectChat(snapshot.chats.find(x=>x.projectId===projectId)?.id);});
$('plus').onclick=()=>{
  const menu=$('composer-menu');menu.replaceChildren();function item(label,fn){const button=el('button',label);button.onclick=()=>guard(async()=>{menu.hidden=true;await fn();});menu.append(button);}
  item(L('Joindre des images','Attach images'),async()=>{const images=await api.host('pick.images');if(currentDraft().images.length+images.length>4)throw new Error('4 images maximum');currentDraft().images.push(...images);renderAssets();});
  item(L('Dossiers sources','Source folders'),()=>projectDialog(selectedProject()));
  const skillDetails=el('details');skillDetails.append(el('summary','Skills'));for(const skill of snapshot.skills){const label=el('label');const check=el('input');check.type='checkbox';check.checked=snapshot.state.enabledSkills.split(',').includes(skill.id);check.onchange=()=>guard(async()=>{const enabled=new Set(snapshot.state.enabledSkills.split(','));check.checked?enabled.add(skill.id):enabled.delete(skill.id);await call('state.save',{enabledSkills:[...enabled].join(',')});await refresh();});label.append(check,document.createTextNode(' '+L(skill.frenchName,skill.englishName)));skillDetails.append(label);}menu.append(skillDetails);
  const templates=el('details');templates.append(el('summary','Templates'));for(const template of snapshot.templates){const button=el('button',template.name);button.onclick=()=>{if($('composer').value&&!confirm(L('Remplacer le brouillon ?','Replace draft?')))return;$('composer').value=template.content;saveDraft();menu.hidden=true;};templates.append(button);}menu.append(templates);menu.hidden=!menu.hidden;
};
function showTools(visible){$('tools').hidden=!visible;document.body.classList.toggle('with-tools',visible);if(!visible)document.body.classList.remove('tools-full');updateBrowserBounds();}
$('tools-toggle').onclick=()=>showTools($('tools').hidden);$('tools-close').onclick=()=>showTools(false);$('tools-full').onclick=()=>{document.body.classList.toggle('tools-full');updateBrowserBounds();};
function selectTab(next){tab=next;document.querySelectorAll('[data-tab]').forEach(x=>x.classList.toggle('selected',x.dataset.tab===tab));for(const name of ['web','terminal','git','files'])$(name+'-tool').hidden=name!==tab;updateBrowserBounds();}
document.querySelectorAll('[data-tab]').forEach(button=>button.onclick=()=>selectTab(button.dataset.tab));
function updateBrowserBounds(){const r=$('browser-surface').getBoundingClientRect();api.host('browser.bounds',{x:r.x,y:r.y,width:r.width,height:r.height,visible:!$('tools').hidden&&tab==='web'&&!document.querySelector('dialog[open]')}).catch(()=>{});}
new ResizeObserver(updateBrowserBounds).observe($('browser-surface'));window.addEventListener('resize',updateBrowserBounds);
$('web-go').onclick=()=>guard(()=>api.host('browser.navigate',{url:$('address').value}));$('address').onkeydown=e=>{if(e.key==='Enter')$('web-go').click();};$('web-back').onclick=()=>guard(()=>api.host('browser.back'));
$('web-file').onclick=()=>guard(async()=>{const files=await api.host('pick.file');if(files[0])await call('preview',{projectId,path:files[0]});});
for(const id of ['browser-access','dom-access'])$(id).onchange=()=>guard(()=>call('browser.access',{enabled:$('browser-access').checked,dom:$('dom-access').checked}));
$('terminal-run').onclick=()=>guard(async()=>{$('terminal-run').disabled=true;try{$('terminal-output').textContent=await call('terminal',{projectId,command:$('command').value});}finally{$('terminal-run').disabled=false;}});
$('git-refresh').onclick=()=>guard(async()=>{$('git-output').textContent=await call('git',{projectId});});
async function loadFiles(){const text=await call('files.list',{projectId,path:$('file-path').value});$('file-list').replaceChildren();$('file-content').textContent='';$('file-preview').hidden=true;for(const line of text.split('\n').filter(Boolean)){const directory=line.startsWith('[dossier] '),name=directory?line.slice(10):line;const button=el('button',(directory?'📁 ':'📄 ')+name);button.onclick=()=>guard(async()=>{if(directory){$('file-path').value=name;await loadFiles();}else{fileSelected=name;$('file-content').textContent=await call('files.read',{projectId,path:name});$('file-preview').hidden=false;}});$('file-list').append(button);}}
$('files-go').onclick=()=>guard(loadFiles);$('files-root').onclick=()=>{$('file-path').value='.';guard(loadFiles);};$('file-preview').onclick=()=>guard(async()=>{await call('preview',{projectId,path:fileSelected});selectTab('web');});
document.addEventListener('click',e=>{const a=e.target.closest('a[href]');if(a){e.preventDefault();guard(async()=>{showTools(true);selectTab('web');await api.host('browser.navigate',{url:a.href});});}});

function field(form,label,type,value){const container=el('label',label),input=el(type==='textarea'?'textarea':type==='select'?'select':'input');if(type!=='textarea'&&type!=='select')input.type=type;if(type==='checkbox')input.checked=!!value;else input.value=value??'';container.append(input);form.append(container);return input;}
function button(parent,label,fn){const b=el('button',label);b.type='button';b.onclick=()=>guard(fn);parent.append(b);return b;}
async function showSettings(){await refresh();renderSettings();showDialog('settings');}
$('settings-open').onclick=()=>guard(showSettings);document.querySelectorAll('[data-settings]').forEach(b=>b.onclick=()=>{settingsTab=b.dataset.settings;renderSettings();});
function renderSettings(){
  document.querySelectorAll('[data-settings]').forEach(b=>b.classList.toggle('selected',b.dataset.settings===settingsTab));const area=$('settings-content');area.replaceChildren();
  if(settingsTab==='general'){
    const language=field(area,L('Langue','Language'),'select');language.append(option('fr','Français'),option('en','English'));language.value=snapshot.state.language;
    button(area,L('Enregistrer','Save'),async()=>{await call('state.save',{language:language.value});await refresh();renderSettings();});
    area.append(el('p',snapshot.database,'muted'));button(area,L('Vérifier les autorisations système','Check system permissions'),async()=>{const result=await api.host('system.permissions');area.append(el('pre',JSON.stringify(result,null,2)));});
  }else if(settingsTab==='skills'){
    for(const skill of snapshot.skills){const label=field(area,L(skill.frenchName,skill.englishName),'checkbox',snapshot.state.enabledSkills.split(',').includes(skill.id));label.dataset.skill=skill.id;area.append(el('p',L(skill.frenchDescription,skill.englishDescription),'muted'));}
    button(area,L('Enregistrer','Save'),async()=>{await call('state.save',{enabledSkills:[...area.querySelectorAll('[data-skill]:checked')].map(x=>x.dataset.skill).join(',')});await refresh();status(L('Skills enregistrés','Skills saved'));});
  }else if(settingsTab==='permissions'){
    const mode=field(area,L('Comportement des autorisations','Permission behavior'),'select');mode.append(option('deny',L('Refuser tout','Deny all')),option('ask',L('Demander (défaut)','Ask (default)')),option('allow',L('Acceptation automatique','Automatically allow')));mode.value=snapshot.state.permissionMode;
    button(area,L('Enregistrer','Save'),async()=>{await call('state.save',{permissionMode:mode.value});await refresh();renderSettings();});
    for(const grant of snapshot.permissions){const card=el('div',null,'card');card.append(el('strong',grant.name),el('p',grant.details));button(card,L('Révoquer','Revoke'),async()=>{await call('permission.revoke',{id:grant.id});await refresh();renderSettings();});area.append(card);}
  }else if(settingsTab==='providers'){
    const actions=el('div',null,'form-actions');for(const kind of ['openai','deepseek','opencode'])button(actions,'＋ '+kind,()=>providerForm({kind,baseUrl:kind==='opencode'?'http://127.0.0.1:4096':kind==='deepseek'?'https://api.deepseek.com':'https://api.openai.com/v1',name:kind,contextLimit:128000,supportsImages:true}));area.append(actions);
    for(const provider of snapshot.providers){const card=el('div',null,'card');card.append(el('strong',provider.name),el('p',provider.model+' · '+provider.baseUrl));button(card,L('Modifier','Edit'),()=>providerForm(provider));button(card,L('Dupliquer','Duplicate'),()=>providerForm({...provider,id:0,name:provider.name+' copy',hasKey:false}));button(card,L('Supprimer','Delete'),async()=>{if(confirm(L('Supprimer ce fournisseur ?','Delete provider?'))){await call('provider.delete',{id:provider.id});await refresh();renderSettings();}});area.append(card);}
  }else if(settingsTab==='templates'){
    button(area,'＋ Template',()=>templateForm({}));for(const template of snapshot.templates){const card=el('div',null,'card');card.append(el('strong',template.name));button(card,L('Modifier','Edit'),()=>templateForm(template));button(card,L('Supprimer','Delete'),async()=>{await call('template.delete',{id:template.id});await refresh();renderSettings();});area.append(card);}
  }
}
function providerForm(provider){
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
