function featureConfig(){return {BrowserMode:'embedded',RagMode:'local',RagModel:'text-embedding-3-small',RagMaxFiles:500,RagTopK:5,...JSON.parse(snapshot?.state.featuresJson||'{}')};}
const inboxes=new Map();
async function refreshInbox(){const id=chatId;if(!id){$('inbox').replaceChildren();return;}inboxes.set(id,await call('inbox.list',{chatId:id}));if(chatId===id)renderInbox();}
function renderInbox(){
  const area=$('inbox');area.replaceChildren();const id=chatId,items=inboxes.get(id)||[];
  for(const item of items){const row=el('div',null,'inbox-row');row.append(el('span',(item.mode==='steering'?L('↳ Prochaine étape : ','↳ Next step: '):L('⏳ En attente : ','⏳ Queued: '))+item.text.slice(0,150)));button(row,'×',async()=>{await call('inbox.delete',{id:item.id,chatId:id});});area.append(row);}
  if(items.length&&!running.has(id))button(area,L('Reprendre la file','Resume queue'),async()=>{await call('inbox.resume',{chatId:id});await refreshInbox();});
}
function compositeForm(provider){
  const area=$('settings-content');area.replaceChildren();const form=el('form');area.append(form);
  const available=snapshot.providers.filter(x=>x.kind!=='composite');
  const config=provider.compositeJson?JSON.parse(provider.compositeJson):{Orchestrator:{},Agents:[]};
  const name=field(form,L('Nom du modèle composé','Composite model name'),'text',provider.name||'');name.required=true;
  form.append(el('p',L('Les tâches des sous-agents sont lancées en parallèle, puis l’orchestrateur exploite leurs résultats. Les permissions et le mode Plan restent appliqués.','Subagent tasks run in parallel, then the orchestrator uses their results. Permissions and Plan mode still apply.'),'muted'));
  function assignment(parent,value,title){
    const card=el('div',null,'card');parent.append(card);card.append(el('strong',title));
    const source=field(card,L('Fournisseur','Provider'),'select');for(const p of available)source.append(option(p.id,p.name));source.value=value.ProviderId||source.value;
    const model=field(card,L('Modèle','Model'),'text',value.Model||available.find(x=>x.id===Number(source.value))?.model||'');model.required=true;
    function save(){value.ProviderId=Number(source.value);value.Model=model.value;}
    source.onchange=()=>{model.value=available.find(x=>x.id===Number(source.value))?.model||'';save();};model.oninput=save;save();
    button(card,L('Charger les modèles','Load models'),async()=>{const models=await call('provider.models',{id:Number(source.value)}),select=field(card,L('Modèles disponibles','Available models'),'select');for(const m of models)select.append(option(m,m));select.value=model.value;select.onchange=()=>{model.value=select.value;save();};});return card;
  }
  assignment(form,config.Orchestrator,L('Orchestrateur','Orchestrator'));
  const children=el('div');form.append(children);
  function render(){children.replaceChildren();for(const agent of config.Agents){const card=assignment(children,agent,L('Sous-agent','Subagent'));const name=field(card,L('Nom','Name'),'text',agent.Name),task=field(card,L('Tâche','Task'),'textarea',agent.Task);name.required=task.required=true;name.oninput=()=>agent.Name=name.value;task.oninput=()=>agent.Task=task.value;button(card,L('Supprimer','Remove'),()=>{config.Agents.splice(config.Agents.indexOf(agent),1);render();});}}
  render();button(form,L('＋ Sous-agent','＋ Subagent'),()=>{if(config.Agents.length<6){config.Agents.push({Name:'Agent '+(config.Agents.length+1),Task:''});render();}});
  const error=el('p',null,'error');form.append(error);const save=el('button',L('Enregistrer','Save'),'accent');save.type='submit';form.append(save);button(form,L('Retour','Back'),()=>renderSettings());
  form.onsubmit=e=>{e.preventDefault();guard(async()=>{save.disabled=true;try{await call('provider.save',{id:provider.id||0,kind:'composite',name:name.value,compositeJson:JSON.stringify(config)});await refresh();renderSettings();}catch(ex){error.textContent=ex.message;}finally{save.disabled=false;}});};
}
function renderFeatureSettings(area,embedded=false){
  const config=featureConfig();
  if(settingsTab==='browser'){
    const mode=field(area,L('Navigateur','Browser'),'select');mode.append(option('embedded','WebView intégré'),option('chrome','Chrome · MCP'),option('disabled',L('Désactivé','Disabled')));mode.value=config.BrowserMode;
    const executable=field(area,L('Chemin Chrome (facultatif)','Chrome path (optional)'),'text',config.ChromePath||'');
    area.append(el('p',L('Le panneau fonctionne sans navigateur. Chrome MCP utilise une fenêtre externe avec un profil par conversation. Chrome et Node.js doivent être installés ; les autorisations MCP restent applicables.','The panel works without a browser. Chrome MCP uses an external window with a per-conversation profile. Chrome and Node.js are required; MCP permissions still apply.')));
    button(area,L('Enregistrer','Save'),async()=>{config.BrowserMode=mode.value;config.ChromePath=executable.value;await call('state.save',{featuresJson:JSON.stringify(config)});await refresh();updateBrowserBounds();renderSettings();});
  }else{
    const mode=field(area,'Embeddings','select');mode.append(option('local',L('MiniLM multilingue · CPU','Multilingual MiniLM · CPU')),option('api','OpenAI v1 API'));mode.value=config.RagMode;
    const provider=field(area,L('Fournisseur embeddings','Embeddings provider'),'select');snapshot.providers.filter(x=>x.kind!=='opencode'&&x.kind!=='composite').forEach(x=>provider.append(option(x.id,x.name)));provider.value=config.RagProviderId||provider.value;
    const model=field(area,L('Modèle API','API model'),'text',config.RagModel);
    const files=field(area,L('Fichiers maximum (1–2000)','Maximum files (1–2000)'),'number',config.RagMaxFiles);
    const hits=field(area,L('Résultats (1–20)','Results (1–20)'),'number',config.RagTopK);
    area.append(el('p',L('Activez RAG dans Skills puis demandez une indexation. MiniLM multilingue embarqué (~118 Mo) fonctionne hors ligne en français et en anglais, avec recherche entre les langues. Les textes sont transmis à l’API après autorisation. Index stocké dans SQLite ; réindexez après le passage de l’ancien modèle anglais au modèle multilingue, ou après modification des sources.','Enable RAG in Skills and ask the agent to index. Bundled multilingual MiniLM (~118 MB) works offline in French and English, including cross-language search. API transmission requires approval. Index lives in SQLite; re-index after upgrading from the English model or editing sources.')));
    const save=()=>{Object.assign(config,{RagMode:mode.value,RagProviderId:Number(provider.value),RagModel:model.value,RagMaxFiles:Number(files.value),RagTopK:Number(hits.value)});return JSON.stringify(config);};if(embedded)return save;button(area,L('Enregistrer','Save'),async()=>{await call('state.save',{featuresJson:save()});await refresh();renderSettings();});
  }
}
function renderChildRows(chat){
  for(const child of subagents.values())if(child.chatId===chat.id&&child.status==='running'){
    const button=el('button','↳ '+child.name+' · '+child.activity,'child-row');button.onclick=()=>guard(async()=>{if(chatId!==chat.id)await selectChat(chat.id);selectedChild=child.id;renderChild();updateControls();});$('chats').append(button);
  }
}
function renderChildBubbles(){
  for(const child of subagents.values())if(child.chatId===chatId){
    let button=$('messages').querySelector(`[data-child="${child.id}"]`);
    if(!button){button=el('button',null,'message child-bubble');button.dataset.child=child.id;button.onclick=()=>{selectedChild=child.id;renderChild();updateControls();};$('messages').append(button);}
    button.textContent='↳ '+child.name+' · '+child.status+'\n'+child.activity+'\n'+child.task.slice(0,180);
  }
}
function renderChild(){
  const child=subagents.get(selectedChild);if(!child)return;
  const scroll=$('messages'),offset=scroll.scrollTop;scroll.replaceChildren();
  const back=el('button',L('← Conversation parente','← Parent conversation'));back.onclick=()=>{selectedChild=null;renderMessages(true);updateControls();};scroll.append(back,el('h2',child.name+' · '+child.status),el('p',child.activity),el('p',child.task));
  for(const message of JSON.parse(child.transcriptJson||'[]')){
    const block=el('article',null,'message');block.append(el('strong',message.role),el('div',message.content||'','body'));
    if(message.tool_calls)block.append(el('pre',JSON.stringify(message.tool_calls,null,2)));scroll.append(block);
  }
  scroll.scrollTop=offset;followMessages();
}
