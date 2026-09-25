const test=require('node:test'),assert=require('node:assert/strict');
const {spawn}=require('node:child_process'),fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path'),http=require('node:http'),readline=require('node:readline');
test('desktop service: SQLite, providers, skills, permissions and simultaneous conversations', {timeout:60000},async t=>{
  const directory=await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(),'omh-service-test-')));
  const executable=path.resolve(__dirname,'../../src/OhMyHarness.App/bin/Release/net10.0-desktop/OhMyHarness.App.dll');
  const child=spawn('dotnet',[executable,'--service','--database',path.join(directory,'database.sqlite')],{stdio:['pipe','pipe','pipe'],windowsHide:true});
  let index=0,stderr='',readyResolve,readyReject;const replies=new Map(),events=[],hosts=[];
  const ready=new Promise((resolve,reject)=>{readyResolve=resolve;readyReject=reject;});
  child.stderr.on('data',data=>stderr+=data);
  child.on('error',readyReject);child.on('exit',code=>{readyReject(new Error(stderr||`Exit ${code}`));for(const p of replies.values())p.reject(new Error(stderr||'Service exited'));});
  let choice='deny';const browserCalls=[];
  function write(value){child.stdin.write(JSON.stringify(value)+'\n');}
  readline.createInterface({input:child.stdout}).on('line',line=>{
    const item=JSON.parse(line);if(item.ready)return readyResolve();
    if(item.event){events.push(item);return;}
    if(item.hostRequest){hosts.push(item.method);let result;
      if(item.method==='permission')result=choice;
      else if(item.method==='key.encrypt')result=Buffer.from(item.parameters.text).toString('base64');
      else if(item.method==='key.decrypt')result=Buffer.from(item.parameters.data,'base64').toString();
      else if(item.method==='read_page'){browserCalls.push(item.parameters);result={text:'Page for chat '+item.parameters.chatId};}
      else return write({hostResponse:item.hostRequest,error:'Unexpected host action '+item.method});
      return write({hostResponse:item.hostRequest,result});
    }
    const pending=replies.get(item.id);if(pending){replies.delete(item.id);item.error?pending.reject(new Error(item.error)):pending.resolve(item.result);}
  });
  function rpc(method,parameters={}){const id=String(++index);return new Promise((resolve,reject)=>{replies.set(id,{resolve,reject});write({id,method,parameters});});}
  let requests=0,active=0,peak=0,embeddings=0,visionRequests=0,releaseQueueStream;const requestLog=[];
  let holdInitialStreams=true;const initialStreams=[];
  const server=http.createServer(async(req,res)=>{
    if(req.url.endsWith('/models')){res.setHeader('Content-Type','application/json');res.end('{"data":[{"id":"test-model"}]}');return;}
    const buffers=[];for await(const chunk of req)buffers.push(chunk);const body=JSON.parse(Buffer.concat(buffers));
    if(req.url==='/v1/embeddings'){
      embeddings++;assert.equal(body.model,'fixture-embedding');assert.equal(req.headers.authorization,'Bearer fixture-not-a-real-key');
      res.setHeader('Content-Type','application/json');res.end(JSON.stringify({data:[{embedding:[1,0,0]}]}));return;
    }
    requests++;active++;peak=Math.max(peak,active);requestLog.push({body,authorization:req.headers.authorization});
    if(body.model==='deepseek-v4-fixture'){
      active--;
      if(body.thinking?.type!=='disabled'){res.writeHead(400,{'Content-Type':'application/json'});res.end('{"error":{"message":"Missing reasoning_content in thinking mode"}}');return;}
      res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta:{content:'Recovered from DeepSeek compatibility error.'}}]})+'\n\ndata: [DONE]\n\n');return;
    }
    if(body.model==='vision-model'){
      visionRequests++;active--;assert.equal(req.headers.authorization,'Bearer vision-fixture-key');assert.ok(body.messages.at(-1).content.some(x=>x.type==='image_url'));assert.ok(!body.tools);
      res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta:{content:'Visible: red circle, label START.'}}]})+'\n\ndata: [DONE]\n\n');return;
    }
    if(body.model==='blind-agent'){
      active--;assert.ok(!JSON.stringify(body.messages).includes('image_url'),'Non-vision main model receives no raw image');
      assert.ok(body.messages.some(x=>typeof x.content==='string'&&x.content.includes('red circle')));
      const last=body.messages.at(-1),previousTool=body.messages.at(-2)?.tool_calls?.[0]?.function?.name;
      const delta=last.role==='tool'&&previousTool==='analyze_image'?{content:'Vision delegated successfully.'}:{tool_calls:[{index:0,id:previousTool==='list_images'?'analyze':'list',type:'function',function:previousTool==='list_images'?{name:'analyze_image',arguments:JSON.stringify({image_id:JSON.parse(last.content)[0].image_id,question:'Read the label exactly'})}:{name:'list_images',arguments:'{}'}}]};
      res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: [DONE]\n\n');return;
    }
    if(body.model==='handoff-model'){
      const last=body.messages.at(-1),isChild=body.messages[0].content.includes('You are a bounded subagent');
      active--;
      if(last.role==='assistant'){res.writeHead(400,{'Content-Type':'application/json'});res.end('{"error":{"message":"last assistant is not a valid continuation"}}');return;}
      const delta=isChild?{content:'Analysis completed in Plan mode. No edits.'}:last.role==='tool'?{content:'Parent resumed and finished.'}:{tool_calls:[{index:0,id:'parent-read',type:'function',function:{name:'list_sources',arguments:'{"path":"."}'}}]};
      res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: [DONE]\n\n');return;
    }
    if(body.model==='python-agent') {
      active--;
      const last=body.messages.at(-1),names=(body.tools||[]).map(x=>x.function.name);
      assert.ok(['python_info','write_python_script','run_python_script'].every(n=>names.includes(n)));
      let name,args,id;
      if(last.role!=='tool'){name='python_info';args={};id='py-info';}
      else if(last.tool_call_id==='py-info'){assert.equal(JSON.parse(last.content).Version,'3.13.15');name='write_python_script';args={path:'api-test.py',code:"print('python-api-ok')"};id='py-write';}
      else if(last.tool_call_id==='py-write'){assert.ok(JSON.parse(last.content).written);name='run_python_script';args={path:'api-test.py'};id='py-run';}
      else {const result=JSON.parse(last.content);assert.equal(result.exit_code,0);assert.ok(result.stdout.includes('python-api-ok'));}
      const delta=name?{tool_calls:[{index:0,id,type:'function',function:{name,arguments:JSON.stringify(args)}}]}:{content:'Bundled Python completed.'};
      res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: [DONE]\n\n');return;
    }
    if(body.model==='application-agent') {
      active--;
      const last=body.messages.at(-1);
      const tools=(body.tools||[]).map(x=>x.function);
      assert.ok(tools.some(x=>x.name==='desktop_applications'));
      assert.ok(tools.find(x=>x.name==='desktop_mouse').parameters.properties.window_id);
      assert.ok(tools.find(x=>x.name==='desktop_screenshot').parameters.properties.window_id);
      const delta=last.role==='tool'?{content:'Applications checked.'}:{tool_calls:[{index:0,id:'applications-list',type:'function',function:{name:'desktop_applications',arguments:'{}'}}]};
      res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: [DONE]\n\n');return;
    }
    if(body.model==='failure-model'){active--;res.writeHead(400);res.end('{}');return;}
    if(body.model==='rag-agent'){
      const last=body.messages.at(-1),name=last.content==='index'?'rag_index':'rag_search';
      const delta=last.role==='tool'?{content:'RAG done'}:{tool_calls:[{index:0,id:'rag-call',type:'function',function:{name,arguments:name==='rag_index'?'{}':'{"query":"database query"}'}}]};
      active--;res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: [DONE]\n\n');return;
    }
    const last=body.messages.at(-1),tool=body.model==='tool-model'&&last.role!=='tool',isSummary=body.messages[0].content.startsWith('Summarize');
    const delta=body.model==='browser-model'&&last.role!=='tool'?{tool_calls:[{index:0,id:'browser-call',type:'function',function:{name:'read_page',arguments:'{"chatId":999999}'}}]}:tool?{tool_calls:[{index:0,id:'test-call',type:'function',function:{name:'run_terminal',arguments:JSON.stringify({command:'echo terminal-ok'})}}]}:{content:isSummary?'Résumé conservant la demande.':`Reply: ${last.content}`};
    res.writeHead(200,{'Content-Type':'text/event-stream'});res.write('data: '+JSON.stringify({choices:[{delta}]})+'\n\n');
    const finish=()=>{active--;res.end('data: '+JSON.stringify({choices:[],usage:{prompt_tokens:50,completion_tokens:8}})+'\n\ndata: [DONE]\n\n');};
    // Prove overlap without requiring startup/JIT on a loaded runner to finish within 250 ms.
    if(holdInitialStreams&&body.model==='test-model'&&last.role==='user'&&['alpha','beta'].includes(last.content)){
      initialStreams.push(finish);
      if(initialStreams.length===2){holdInitialStreams=false;for(const complete of initialStreams.splice(0))complete();}
    }else if(body.model==='queue-model'&&last.role==='user'&&last.content==='first')releaseQueueStream=finish;
    else if(body.model==='queue-model'&&last.role==='user'&&last.content==='stop with queue')res.once('close',()=>{active--;});
    else setTimeout(finish,250);
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  t.after(async()=>{child.stdin.end();await new Promise(resolve=>{if(child.exitCode!=null)return resolve();child.once('exit',resolve);setTimeout(()=>child.kill(),5000).unref();});server.closeAllConnections();await new Promise(resolve=>server.close(resolve));await fs.rm(directory,{recursive:true,force:true});});
  await ready;
  const initial=await rpc('snapshot');assert.equal(initial.state.permissionMode,'ask');assert.equal(initial.providers.length,2);assert.ok(!('protectedKey' in initial.providers[0]));
  assert.equal(initial.appearanceThemes.length,10);assert.equal(initial.appearanceThemes.filter(x=>x.dark).length,5);
  const mcpFile=await rpc('mcp.json.get');assert.equal(mcpFile.path,path.join(directory,'MCP.json'));
  const godot=JSON.stringify({mcpServers:{godot:{command:'npx',args:['@coding-solo/godot-mcp'],env:{GODOT_PATH:'/path/to/godot',DEBUG:'true'},enabled:false}}});
  await rpc('mcp.json.save',{content:godot,expected:mcpFile.content});
  const imported=(await rpc('snapshot')).mcpServers.find(x=>x.name==='godot');assert.ok(imported.hasSecrets);assert.equal(imported.enabled,false);
  await rpc('mcp.toggle',{id:imported.id,enabled:true});assert.equal(JSON.parse(await fs.readFile(mcpFile.path,'utf8')).mcpServers.godot.enabled,true);
  const current=await rpc('mcp.json.get');await assert.rejects(rpc('mcp.json.save',{content:'{',expected:current.content}));assert.equal(await fs.readFile(mcpFile.path,'utf8'),current.content);
  await rpc('mcp.json.save',{content:'{"mcpServers":{}}',expected:current.content});
  await rpc('state.save',{featuresJson:JSON.stringify({Theme:'ivory',ComposerInfoExpanded:false})});
  const appearance=JSON.parse((await rpc('snapshot')).state.featuresJson);assert.equal(appearance.Theme,'ivory');assert.equal(appearance.ComposerInfoExpanded,false);

  const provider=await rpc('provider.save',{name:'local',kind:'openai',baseUrl:`http://127.0.0.1:${server.address().port}/v1`,model:'test-model',key:'fixture-not-a-real-key',contextLimit:4096});
  assert.equal(provider.hasKey,true);assert.deepEqual(await rpc('provider.models',{id:provider.id}),['test-model']);
  const project=await rpc('project.save',{name:'Sources',folders:[directory]});
  const chatA=await rpc('chat.save',{projectId:project.id,title:'A'}),chatB=await rpc('chat.save',{projectId:project.id,title:'B'});
  await Promise.all([rpc('send',{chatId:chatA.id,providerId:provider.id,text:'alpha'}),rpc('send',{chatId:chatB.id,providerId:provider.id,text:'beta'})]);
  assert.ok(peak>=2,'Both HTTP streams overlap');
  const historyA=await rpc('history',{chatId:chatA.id}),historyB=await rpc('history',{chatId:chatB.id});
  assert.equal(historyA.at(-1).content,'Reply: alpha');assert.equal(historyB.at(-1).content,'Reply: beta');
  const exported=await rpc('chat.export',{chatId:chatA.id,providerId:provider.id});
  assert.equal(exported.useClipboard,true);assert.ok(exported.fileName.endsWith('.md'));
  assert.ok(exported.markdown.includes('Reply: alpha'));assert.ok(!exported.markdown.includes('Reply: beta'));
  const fork=await rpc('chat.branch',{chatId:chatA.id,messageId:historyA[0].id});
  assert.equal((await rpc('history',{chatId:fork.id})).length,1);
  assert.equal((await rpc('history',{chatId:chatA.id})).length,2);
  await rpc('chat.tasks',{id:fork.id,dismissed:true});assert.equal((await rpc('snapshot')).chats.find(x=>x.id===fork.id).todoDismissed,true);
  const resourceFile=path.join(directory,'attached.txt');await fs.writeFile(resourceFile,'specific file');
  await rpc('chat.resources',{id:fork.id,paths:[resourceFile]});
  assert.equal(await rpc('files.read',{projectId:project.id,chatId:fork.id,path:resourceFile}),'specific file');
  await assert.rejects(rpc('files.read',{projectId:project.id,chatId:fork.id,path:path.join(directory,'database.sqlite')}));
  await fs.writeFile(path.join(directory,'permission.json'),'{"permissions":{"desktop":"deny","terminal":"ask"}}');
  const reviewed=await rpc('project.permissions.preview',{id:project.id});
  await rpc('project.permissions.apply',{id:project.id,reviewed});
  await fs.writeFile(path.join(directory,'permission.json'),'{"permissions":{"desktop":"allow"}}');
  await assert.rejects(rpc('project.permissions.apply',{id:project.id,reviewed}));
  assert.equal(JSON.parse((await rpc('snapshot')).projects.find(x=>x.id===project.id).permissionProfileJson).desktop,'deny');
  await rpc('project.permissions.apply',{id:project.id,clear:true});
  const recovering=await rpc('provider.save',{...provider,id:0,name:'DeepSeek fallback',model:'deepseek-v4-fixture',kind:'deepseek',key:'fixture-not-a-real-key'});
  const recoveryChat=await rpc('chat.save',{projectId:project.id,title:'Recovery'});
  await rpc('send',{chatId:recoveryChat.id,providerId:recovering.id,text:'continue'});
  const recovered=(await rpc('history',{chatId:recoveryChat.id})).at(-1);
  assert.equal(recovered.state,'complete');assert.ok(recovered.content.includes('Recovered'));assert.ok(recovered.compatibilityNotice.includes('raisonnement'));
  await rpc('chat.branch',{chatId:chatA.id,messageId:historyA[0].id,resume:true});
  assert.equal((await rpc('history',{chatId:chatA.id})).length,1);
  const stopped=rpc('send',{chatId:chatA.id,providerId:provider.id,text:'cancel me'});stopped.catch(()=>{});
  while(!(await rpc('snapshot')).running.includes(chatA.id))await new Promise(r=>setTimeout(r,5));
  const other=rpc('send',{chatId:chatB.id,providerId:provider.id,text:'keep going'});
  await rpc('stop',{chatId:chatA.id});await assert.rejects(stopped);await other;
  assert.equal((await rpc('history',{chatId:chatB.id})).at(-1).content,'Reply: keep going');
  await rpc('state.save',{enabledSkills:'terminal',permissionMode:'deny'});
  await rpc('provider.save',{...provider,model:'tool-model'});
  const before=hosts.filter(x=>x==='permission').length;
  await rpc('send',{chatId:chatB.id,providerId:provider.id,text:'tool please'});
  assert.equal(hosts.filter(x=>x==='permission').length,before,'Deny-all occurs before permission dialogs');
  assert.ok((await rpc('history',{chatId:chatB.id})).some(x=>x.role==='tool'&&x.content.includes('Access denied')));
  await rpc('state.save',{permissionMode:'ask'});choice='always';
  await rpc('send',{chatId:chatB.id,providerId:provider.id,text:'approved tool'});
  const approved=await rpc('snapshot');assert.equal(approved.permissions.length,1);assert.ok(approved.permissions[0].scope.startsWith('terminal|'));
  await rpc('permission.revoke',{id:approved.permissions[0].id});assert.equal((await rpc('snapshot')).permissions.length,0);
  const template=await rpc('template.save',{name:'Custom',content:'Create an HTML app'});assert.ok(template.id>0);
  await fs.writeFile(path.join(directory,'hello.swift'),'print("hello")');assert.equal(await rpc('files.read',{projectId:project.id,path:'hello.swift'}),'print("hello")');
  await fs.writeFile(path.join(directory,'.env'),'sensitive');await assert.rejects(rpc('files.read',{projectId:project.id,path:'.env'}));
  assert.ok(events.some(x=>x.event==='stream')&&events.some(x=>x.event==='done'));assert.ok(requests>=6);assert.equal((await rpc('snapshot')).running.length,0);
  await rpc('browser.access',{enabled:true,dom:true});
  const browserState=await rpc('snapshot');
  assert.ok(browserState.browserAccess&&browserState.domAccess&&browserState.state.enabledSkills.includes('browser_access')&&browserState.state.enabledSkills.includes('browser_dom_access'),'Browser skills persist in shared state');
  await rpc('state.save',{enabledSkills:'web,browser_access,browser_dom_access'});
  await rpc('provider.save',{...provider,model:'browser-model'});
  await Promise.all([rpc('send',{chatId:chatA.id,providerId:provider.id,text:'browser A'}),rpc('send',{chatId:chatB.id,providerId:provider.id,text:'browser B'})]);
  assert.deepEqual(browserCalls.map(x=>x.chatId).sort((a,b)=>a-b),[chatA.id,chatB.id].sort((a,b)=>a-b),'Host browser routing uses run identity, ignoring a forged chatId');
  const ragRoot=path.join(directory,'rag-sources');await fs.mkdir(ragRoot);await fs.writeFile(path.join(ragRoot,'database.custom'),'SQL database index speeds up database queries.');
  const ragProject=await rpc('project.save',{name:'RAG test',folders:[ragRoot]});
  const ragChat=await rpc('chat.save',{projectId:ragProject.id,title:'RAG'});
  await rpc('provider.save',{...provider,model:'rag-agent'});
  await rpc('state.save',{enabledSkills:'rag',permissionMode:'deny',featuresJson:JSON.stringify({RagMode:'api',RagProviderId:provider.id,RagModel:'fixture-embedding'})});
  await rpc('send',{chatId:ragChat.id,providerId:provider.id,text:'index'});assert.equal(embeddings,0,'No file transmission when embeddings permission is denied');
  await rpc('state.save',{permissionMode:'allow'});
  await rpc('send',{chatId:ragChat.id,providerId:provider.id,text:'index'});assert.equal(embeddings,1);
  await rpc('send',{chatId:ragChat.id,providerId:provider.id,text:'search'});assert.equal(embeddings,2);
  assert.ok((await rpc('history',{chatId:ragChat.id})).some(x=>x.role==='tool'&&x.content.includes('database.custom')),'Remote embeddings return scoped file and line citations');
  await rpc('state.save',{enabledSkills:'sources',autoContinue:false});
  await rpc('provider.save',{...provider,model:'queue-model'});
  const queueChat=await rpc('chat.save',{projectId:project.id,title:'Queue'});
  const queuedRun=rpc('send',{chatId:queueChat.id,providerId:provider.id,text:'first'});
  while(!events.some(x=>x.event==='stream'&&x.chatId===queueChat.id))await new Promise(r=>setTimeout(r,5));
  await rpc('inbox.add',{chatId:queueChat.id,providerId:provider.id,text:'next turn',mode:'queued'});
  await rpc('inbox.add',{chatId:queueChat.id,providerId:provider.id,text:'use English',mode:'queued'});
  const editable=(await rpc('inbox.list',{chatId:queueChat.id})).find(x=>x.text==='use English');
  await rpc('inbox.update',{chatId:queueChat.id,id:editable.id,expectedText:'use English',text:'use French now'});
  await rpc('inbox.update',{chatId:queueChat.id,id:editable.id,expectedText:'use French now',text:'use French now',steer:true});
  assert.equal((await rpc('inbox.list',{chatId:queueChat.id})).length,2);
  assert.equal(typeof releaseQueueStream,'function','First queued stream stays active until edits finish');
  releaseQueueStream();
  await queuedRun;
  assert.deepEqual((await rpc('history',{chatId:queueChat.id})).filter(x=>x.role==='user').map(x=>x.content),['first','use French now','next turn']);
  assert.equal((await rpc('inbox.list',{chatId:queueChat.id})).length,0);
  const interrupted=rpc('send',{chatId:queueChat.id,providerId:provider.id,text:'stop with queue'});interrupted.catch(()=>{});
  while(!(await rpc('snapshot')).running.includes(queueChat.id))await new Promise(r=>setTimeout(r,5));
  await rpc('inbox.add',{chatId:queueChat.id,providerId:provider.id,text:'retained after stop',mode:'queued'});
  await rpc('stop',{chatId:queueChat.id});await assert.rejects(interrupted);
  assert.equal((await rpc('inbox.list',{chatId:queueChat.id})).length,1,'Stop preserves queued messages');
  await rpc('inbox.resume',{chatId:queueChat.id});assert.equal((await rpc('history',{chatId:queueChat.id})).at(-1).content,'Reply: retained after stop');
  await rpc('inbox.add',{chatId:queueChat.id,providerId:provider.id,text:'delete pending',mode:'queued'});
  const pending=(await rpc('inbox.list',{chatId:queueChat.id}))[0];await rpc('inbox.delete',{chatId:chatA.id,id:pending.id});
  assert.equal((await rpc('inbox.list',{chatId:queueChat.id})).length,1,'Cannot remove another conversation input');
  await rpc('inbox.delete',{chatId:queueChat.id,id:pending.id});
  await rpc('inbox.add',{chatId:queueChat.id,providerId:provider.id,text:' single queued turn ',mode:'queued'});
  await Promise.all([rpc('inbox.resume',{chatId:queueChat.id}),rpc('inbox.resume',{chatId:queueChat.id})]);
  assert.equal((await rpc('history',{chatId:queueChat.id})).filter(x=>x.role==='user'&&x.content==='single queued turn').length,1,'Concurrent queue resumes consume only once');
  const childProvider=await rpc('provider.save',{name:'Child provider',kind:'openai',baseUrl:provider.baseUrl,model:'child-default',key:'child-fixture-key',contextLimit:4096});
  const config={Orchestrator:{ProviderId:provider.id,Model:'orchestrator-model'},Agents:[{ProviderId:childProvider.id,Model:'review-model',Name:'Review',Task:'Review the code without edits.'},{ProviderId:provider.id,Model:'test-model',Name:'Tests',Task:'Identify tests to run without edits.'}]};
  const composite=await rpc('provider.save',{kind:'composite',name:'Team',compositeJson:JSON.stringify(config)});
  assert.equal(composite.hasKey,false);await assert.rejects(rpc('provider.delete',{id:childProvider.id}),/composé/);
  const teamChat=await rpc('chat.save',{projectId:project.id,title:'Team'});await rpc('chat.modes',{id:teamChat.id,executionMode:'plan'});
  await rpc('send',{chatId:teamChat.id,providerId:composite.id,text:'Inspect this project'});
  const agents=await rpc('subagents',{chatId:teamChat.id});assert.equal(agents.length,2);assert.ok(agents.every(x=>x.status==='completed'));
  const review=requestLog.find(x=>x.body.model==='review-model');assert.equal(review.authorization,'Bearer child-fixture-key');assert.ok(review.body.messages[1].content.includes('Review the code without edits.'));
  assert.ok(!(review.body.tools||[]).some(x=>x.function.name==='write_source'),'Plan applies to configured child model');
  const orchestrator=requestLog.find(x=>x.body.model==='orchestrator-model');assert.equal(orchestrator.authorization,'Bearer fixture-not-a-real-key');assert.ok(orchestrator.body.messages.some(x=>x.content?.includes('[Review]')),'Orchestrator receives child reports');
  await assert.rejects(rpc('provider.save',{kind:'composite',name:'Invalid nesting',compositeJson:JSON.stringify({...config,Orchestrator:{ProviderId:composite.id,Model:'anything'}})}));
  await rpc('state.save',{enabledSkills:'sources,write_sources',autoContinue:true});
  const forcedProvider=await rpc('provider.save',{name:'Handoff regression',kind:'openai',baseUrl:provider.baseUrl,model:'handoff-model',contextLimit:32000});
  const forcedChat=await rpc('chat.save',{projectId:project.id,title:'Forced continuation'});
  await rpc('chat.modes',{id:forcedChat.id,executionMode:'execute',orchestrationMode:'forced'});
  await rpc('send',{chatId:forcedChat.id,providerId:forcedProvider.id,text:'Create the requested project'});
  const forcedHistory=await rpc('history',{chatId:forcedChat.id});
  assert.equal(forcedHistory.at(-1).content,'Parent resumed and finished.');assert.equal(forcedHistory.at(-1).state,'complete');
  assert.ok(forcedHistory.some(x=>x.role==='tool'&&x.content.startsWith('list_sources')));
  assert.equal((await rpc('subagents',{chatId:forcedChat.id})).length,2,'Forced children run once, then the parent continues');
  const handoffRequests=requestLog.filter(x=>x.body.model==='handoff-model');
  const parentRequest=handoffRequests.find(x=>!x.body.messages[0].content.includes('You are a bounded subagent'));
  assert.equal(parentRequest.body.messages.at(-1).role,'user');assert.ok(parentRequest.body.tools.some(x=>x.function.name==='write_source'),'Parent retains Execute tools');
  assert.ok(handoffRequests.filter(x=>x.body.messages[0].content.includes('You are a bounded subagent')).every(x=>!x.body.tools.some(t=>t.function.name==='write_source')),'Analysis children remain read-only');
  const failureProvider=await rpc('provider.save',{name:'Error regression',kind:'openai',baseUrl:provider.baseUrl,model:'failure-model'});
  const failureChat=await rpc('chat.save',{projectId:project.id,title:'Failure detail'});
  await assert.rejects(rpc('send',{chatId:failureChat.id,providerId:failureProvider.id,text:'Test error'}),/HTTP 400/);
  const failedHistory=await rpc('history',{chatId:failureChat.id});assert.match(failedHistory.at(-1).content,/HTTP 400/);assert.equal(failedHistory.at(-1).state,'interrupted');
  const visionProvider=await rpc('provider.save',{name:'Dedicated vision',kind:'openai',baseUrl:provider.baseUrl,model:'provider-default',key:'vision-fixture-key'});
  const blindProvider=await rpc('provider.save',{name:'No vision',kind:'openai',baseUrl:provider.baseUrl,model:'blind-agent',supportsImages:false,contextLimit:32000});
  await rpc('state.save',{enabledSkills:'vision_bridge',permissionMode:'allow',featuresJson:JSON.stringify({VisionProviderId:visionProvider.id,VisionModel:'vision-model'})});
  const visionChat=await rpc('chat.save',{projectId:project.id,title:'Vision relay'});
  const testImage={name:'test.png',mime:'image/png',data:'AQID'};
  await rpc('send',{chatId:visionChat.id,providerId:blindProvider.id,text:'Read this image',images:[testImage]});
  const visionHistory=await rpc('history',{chatId:visionChat.id});assert.equal(visionHistory.at(-1).content,'Vision delegated successfully.');
  assert.equal(visionHistory[0].attachments[0].data,'AQID','Original image retained in chat history');assert.equal(visionRequests,2,'One automatic description plus one focused question, cached across tool rounds');
  await rpc('state.save',{permissionMode:'deny'});
  const deniedVision=await rpc('chat.save',{projectId:project.id,title:'Denied vision relay'});
  await assert.rejects(rpc('send',{chatId:deniedVision.id,providerId:blindProvider.id,text:'Read',images:[testImage]}),/refusée|denied/);
  assert.equal(visionRequests,2,'Deny all blocks transmission to vision API');
  const appProvider=await rpc('provider.save',{...provider,model:'application-agent'});
  await rpc('state.save',{enabledSkills:'applications,mouse_control,screenshots',permissionMode:'deny'});
  const appChat=await rpc('chat.save',{projectId:project.id,title:'Application tools'});
  const dialogsBefore=hosts.filter(x=>x==='permission').length;
  await rpc('send',{chatId:appChat.id,providerId:appProvider.id,text:'List applications'});
  assert.equal(hosts.filter(x=>x==='permission').length,dialogsBefore,'Application inventory respects deny before dialogs');
  assert.ok((await rpc('history',{chatId:appChat.id})).some(x=>x.role==='tool'&&x.content==='desktop_applications\nAccess denied.'));
  await rpc('state.save',{permissionMode:'ask'});choice='always';
  await rpc('send',{chatId:appChat.id,providerId:appProvider.id,text:'List applications after approval'});
  const appHistory=await rpc('history',{chatId:appChat.id});
  const inventoryText=appHistory.filter(x=>x.role==='tool').at(-1).content;
  if(process.platform==='linux'){
    assert.match(inventoryText,/desktop_applications\n/);
    assert.match(inventoryText,/not supported|non pris en charge|Erreur outil/i,'Linux reports its unavailable desktop inventory');
  }else{
    const inventory=JSON.parse(inventoryText.slice(inventoryText.indexOf('\n')+1));
    assert.ok(Array.isArray(inventory.windows));assert.ok(inventory.coordinate_system);
  }
  assert.ok((await rpc('snapshot')).permissions.some(x=>x.scope==='desktop|applications'),'Always-allow inventory permission is persisted');
  const pythonProvider=await rpc('provider.save',{...provider,model:'python-agent'});
  await rpc('state.save',{enabledSkills:'python',permissionMode:'allow'});
  const pythonChat=await rpc('chat.save',{projectId:project.id,title:'Python tools'});
  await rpc('send',{chatId:pythonChat.id,providerId:pythonProvider.id,text:'Create and execute a script'});
  assert.equal((await rpc('history',{chatId:pythonChat.id})).at(-1).content,'Bundled Python completed.');
  assert.equal(await fs.readFile(path.join(directory,'scripts','python','chat-'+pythonChat.id,'api-test.py'),'utf8'),"print('python-api-ok')");
});
