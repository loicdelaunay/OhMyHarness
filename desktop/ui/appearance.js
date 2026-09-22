function applyAppearance(){
  const config=featureConfig(),themes=snapshot.appearanceThemes||[],theme=themes.find(x=>x.id===config.Theme)||themes[0];
  if(theme){
    const root=document.documentElement;root.dataset.theme=theme.id;root.classList.toggle('theme-light',!theme.dark);
    root.style.colorScheme=theme.dark?'dark':'light';
    for(const [key,value] of Object.entries({background:theme.background,surface:theme.surface,text:theme.text,muted:theme.muted,accent:theme.accent}))root.style.setProperty('--'+key,value);
  }
  const expanded=config.ComposerInfoExpanded!==false;
  $('info-toggle').setAttribute('aria-expanded',String(expanded));
  $('info-toggle').textContent=(expanded?'⌄  ':'›  ')+L('Modèle, débit et contexte','Model, speed and context');
  document.querySelector('.metrics').hidden=!expanded;
}
const composerShell=document.createElement('div');composerShell.className='composer-shell';
const metricsPanel=document.querySelector('.metrics'),composerPanel=document.querySelector('.composer');
metricsPanel.before(composerShell);
const infoToggle=document.createElement('button');infoToggle.id='info-toggle';infoToggle.type='button';
composerShell.append(infoToggle,metricsPanel,$('assets'),composerPanel);
composerShell.before($('pinned-tasks'),$('inbox'));
infoToggle.onclick=()=>guard(async()=>{
  const config=featureConfig();config.ComposerInfoExpanded=config.ComposerInfoExpanded===false;
  await call('state.save',{featuresJson:JSON.stringify(config)});await refresh();
});

function providerActions(parent,provider){
  if(!provider.id)return;
  const actions=el('div',null,'form-actions');parent.append(actions);
  button(actions,L('Dupliquer','Duplicate'),()=>providerForm({...provider,id:0,name:provider.name+' '+L('copie','copy'),hasKey:false}));
  button(actions,L('Supprimer le fournisseur','Delete provider'),async()=>{
    if(!confirm(L('Supprimer ce fournisseur ?','Delete provider?')))return;
    await call('provider.delete',{id:provider.id});await refresh();renderSettings();
  });
}

async function mcpJsonEditor(){
  const file=await call('mcp.json.get');if(settingsTab!=='mcp')return;
  const area=$('settings-content');area.replaceChildren();area.append(el('h3','MCP.json'),el('p',file.path,'muted'));
  area.append(el('p',L('Format mcpServers : command, args, env, url, headers. Les secrets saisis ici restent en clair dans le fichier. Les secrets déjà enregistrés restent dans SQLite.','mcpServers format: command, args, env, url, headers. Secrets entered here remain as plain text in the file. Previously saved secrets remain in SQLite.'),'muted'));
  const editor=field(area,'JSON','textarea',file.content);editor.id='mcp-json';editor.rows=18;editor.spellcheck=false;
  const error=el('p',null,'error');area.append(error);
  button(area,L('Enregistrer et appliquer','Save and apply'),async()=>{
    try{JSON.parse(editor.value);await call('mcp.json.save',{content:editor.value,expected:file.content});await refresh();renderSettings();}
    catch(ex){error.textContent=ex.message;}
  });
  button(area,L('Recharger le fichier','Reload file'),mcpJsonEditor);
  button(area,L('Retour','Back'),renderSettings);
}
