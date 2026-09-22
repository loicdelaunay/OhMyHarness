function featureConfig(){return {BrowserMode:'embedded',RagMode:'local',RagModel:'text-embedding-3-small',RagMaxFiles:500,RagTopK:5,...JSON.parse(snapshot?.state.featuresJson||'{}')};}
function renderVisionSettings(area){
  const config=featureConfig();
  const provider=field(area,L('Fournisseur vision','Vision provider'),'select');provider.append(option(0,L('Choisir un fournisseur','Choose a provider')));
  snapshot.providers.filter(x=>x.kind!=='opencode'&&x.kind!=='composite').forEach(x=>provider.append(option(x.id,x.name)));provider.value=config.VisionProviderId||0;
  const model=field(area,L('Modèle vision','Vision model'),'text',config.VisionModel||'');model.setAttribute('list','vision-models');
  const choices=document.createElement('datalist');choices.id='vision-models';area.append(choices);
  provider.onchange=()=>{choices.replaceChildren();model.value=snapshot.providers.find(x=>x.id===Number(provider.value))?.model||'';};
  button(area,L('Charger les modèles','Load models'),async()=>{choices.replaceChildren();for(const name of await call('provider.models',{id:Number(provider.value)}))choices.append(option(name,name));});
  area.append(el('p',L('Choisissez un modèle acceptant les images. Les images et captures sont décrites automatiquement pour un modèle principal sans vision. L’IA peut aussi poser une question précise avec analyze_image. Envoi au fournisseur choisi après autorisation ; des frais API peuvent s’appliquer. Les descriptions peuvent être inexactes. OpenCode conserve ses propres outils : seul le relais des images jointes y est disponible.','Choose an image-capable model. Images and screenshots are described automatically for a non-vision main model. The AI can also ask focused questions with analyze_image. Transmission requires approval and may incur API charges. Descriptions can be inaccurate. OpenCode retains its own tools: only attachment relay is available there.'),'muted'));
  return ()=>({VisionProviderId:Number(provider.value),VisionModel:model.value.trim()});
}
const inboxes=new Map();
async function refreshInbox(){const id=chatId;if(!id){$('inbox').replaceChildren();return;}inboxes.set(id,await call('inbox.list',{chatId:id}));if(chatId===id)renderInbox();}
function renderInbox(){
  const area=$('inbox');area.replaceChildren();const id=chatId,items=inboxes.get(id)||[];
  area.hidden=items.length===0;
  if(items.length)area.append(el('div',L('Messages en attente','Queued messages')+' · '+items.length,'inbox-header'));
  for(const item of items){
    const row=el('div',null,'inbox-row');row.append(el('span',item.mode==='steering'?'↳':String(items.indexOf(item)+1),'queue-index'),el('span',item.text.slice(0,220)+(item.text.length>220?'…':''),'queue-text'));
    const actions=el('div',null,'inbox-actions');
    button(actions,L('Supprimer','Delete'),async()=>{await call('inbox.delete',{id:item.id,chatId:id});});
    button(actions,L('Modifier','Edit'),async()=>{
      const dialog=document.createElement('dialog'),editor=el('textarea');editor.value=item.text;editor.rows=5;editor.style.width='100%';
      dialog.append(el('h3',L('Modifier le message en attente','Edit queued message')),editor);
      button(dialog,L('Enregistrer','Save'),async()=>{await call('inbox.update',{id:item.id,chatId:id,expectedText:item.text,text:editor.value});dialog.close();});
      button(dialog,L('Annuler','Cancel'),async()=>dialog.close());dialog.onclose=()=>dialog.remove();document.body.append(dialog);dialog.showModal();
    });
    const steer=el('button','Steer');steer.disabled=item.mode!=='queued'||!running.has(id);steer.title=L('Transmettre à la prochaine étape','Send at the next step');
    steer.onclick=()=>guard(async()=>{await call('inbox.update',{id:item.id,chatId:id,expectedText:item.text,text:item.text,steer:true});});
    actions.append(steer);row.append(actions);area.append(row);
  }
  if(items.length&&!running.has(id))button(area,L('Reprendre la file','Resume queue'),async()=>{await call('inbox.resume',{chatId:id});await refreshInbox();});
}
function compositeForm(provider){
  const area=$('settings-content');area.replaceChildren();const form=el('form');area.append(form);
  const available=snapshot.providers.filter(x=>x.kind!=='composite');
  providerActions(form,provider);
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
function childCard(child,compact){
  const states={running:L('En cours','Running'),completed:L('Terminé','Completed'),failed:L('Échec','Failed'),limited:L('Limite atteinte','Limit reached'),cancelled:L('Annulé','Cancelled'),interrupted:L('Interrompu','Interrupted')};
  let activity=child.activity||'';
  for(const [both,fr,en] of [['Réflexion / Thinking','Réflexion','Thinking'],['Démarrage / Starting','Démarrage','Starting'],['Réponse reçue / Response received','Réponse reçue','Response received'],['Outil / Tool','Outil','Tool']])activity=activity.replace(both,L(fr,en));
  const step=activity.match(/^Étape (\d+\/\d+) · /);if(step)activity=activity.slice(step[0].length);
  const card=el('span',null,'agent-card');card.dataset.status=child.status;
  card.append(el('span',child.status==='completed'?'✓':child.status==='failed'?'!':'◈','agent-icon'),el('span',child.name,'agent-name'),el('span',compact?(step?.[1]||'›'):(states[child.status]||child.status),'agent-badge'),el('span',child.status==='running'?activity:states[child.status],'agent-activity'));
  if(!compact)card.append(el('span',child.task,'agent-task'));
  card.title=child.name+'\n'+activity+'\n'+child.task;return card;
}
function renderChildRows(chat){
  for(const child of subagents.values())if(child.chatId===chat.id&&child.status==='running'){
    const button=el('button',null,'child-row');button.append(childCard(child,true));button.setAttribute('aria-label',child.name+' · '+child.activity);button.onclick=()=>guard(async()=>{if(chatId!==chat.id)await selectChat(chat.id);selectedChild=child.id;renderChild();updateControls();});$('chats').append(button);
  }
}
function renderChildBubbles(){
  for(const child of subagents.values())if(child.chatId===chatId){
    let button=$('messages').querySelector(`[data-child="${child.id}"]`);
    if(!button){button=el('button',null,'message child-bubble');button.dataset.child=child.id;button.onclick=()=>{selectedChild=child.id;renderChild();updateControls();};$('messages').append(button);}
    button.replaceChildren(childCard(child,false));
  }
}
function renderChild(){
  renderPinnedTasks();
  const child=subagents.get(selectedChild);if(!child)return;
  const scroll=$('messages'),offset=scroll.scrollTop;scroll.replaceChildren();
  const back=el('button',L('← Conversation parente','← Parent conversation'));back.onclick=()=>{selectedChild=null;renderMessages(true);updateControls();};scroll.append(back,el('h2',child.name+' · '+child.status),el('p',child.activity),el('p',child.task));
  for(const message of JSON.parse(child.transcriptJson||'[]')){
    const block=el('article',null,'message');block.append(el('strong',message.role),el('div',message.content||'','body'));
    if(message.tool_calls)block.append(el('pre',JSON.stringify(message.tool_calls,null,2)));scroll.append(block);
  }
  scroll.scrollTop=offset;followMessages();
}
