const test=require('node:test'),assert=require('node:assert/strict');
const {spawn}=require('node:child_process'),fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path'),http=require('node:http'),readline=require('node:readline');
test('desktop service: SQLite, providers, skills, permissions and simultaneous conversations', {timeout:60000},async t=>{
  const directory=await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(),'omh-service-test-')));
  const executable=path.resolve(__dirname,'../../src/OhMyHarness.Service/bin/Release/net10.0/OhMyHarness.Service.dll');
  const child=spawn('dotnet',[executable,'--database',path.join(directory,'database.sqlite')],{stdio:['pipe','pipe','pipe'],windowsHide:true});
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
  let requests=0,active=0,peak=0,embeddings=0;const requestLog=[];
  const server=http.createServer(async(req,res)=>{
    if(req.url.endsWith('/models')){res.setHeader('Content-Type','application/json');res.end('{"data":[{"id":"test-model"}]}');return;}
    const buffers=[];for await(const chunk of req)buffers.push(chunk);const body=JSON.parse(Buffer.concat(buffers));
    if(req.url==='/v1/embeddings'){
      embeddings++;assert.equal(body.model,'fixture-embedding');assert.equal(req.headers.authorization,'Bearer fixture-not-a-real-key');
      res.setHeader('Content-Type','application/json');res.end(JSON.stringify({data:[{embedding:[1,0,0]}]}));return;
    }
    requests++;active++;peak=Math.max(peak,active);requestLog.push({body,authorization:req.headers.authorization});
    if(body.model==='rag-agent'){
      const last=body.messages.at(-1),name=last.content==='index'?'rag_index':'rag_search';
      const delta=last.role==='tool'?{content:'RAG done'}:{tool_calls:[{index:0,id:'rag-call',type:'function',function:{name,arguments:name==='rag_index'?'{}':'{"query":"database query"}'}}]};
      active--;res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: [DONE]\n\n');return;
    }
    const last=body.messages.at(-1),tool=body.model==='tool-model'&&last.role!=='tool',isSummary=body.messages[0].content.startsWith('Summarize');
    const delta=body.model==='browser-model'&&last.role!=='tool'?{tool_calls:[{index:0,id:'browser-call',type:'function',function:{name:'read_page',arguments:'{"chatId":999999}'}}]}:tool?{tool_calls:[{index:0,id:'test-call',type:'function',function:{name:'run_terminal',arguments:JSON.stringify({command:'echo terminal-ok'})}}]}:{content:isSummary?'Résumé conservant la demande.':`Reply: ${last.content}`};
    res.writeHead(200,{'Content-Type':'text/event-stream'});res.write('data: '+JSON.stringify({choices:[{delta}]})+'\n\n');
    setTimeout(()=>{active--;res.end('data: '+JSON.stringify({choices:[],usage:{prompt_tokens:50,completion_tokens:8}})+'\n\ndata: [DONE]\n\n');},250);
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  t.after(async()=>{child.stdin.end();await new Promise(resolve=>{if(child.exitCode!=null)return resolve();child.once('exit',resolve);setTimeout(()=>child.kill(),5000).unref();});server.closeAllConnections();await new Promise(resolve=>server.close(resolve));await fs.rm(directory,{recursive:true,force:true});});
  await ready;
  const initial=await rpc('snapshot');assert.equal(initial.state.permissionMode,'ask');assert.equal(initial.providers.length,2);assert.ok(!('protectedKey' in initial.providers[0]));
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
  await rpc('browser.access',{enabled:true,dom:true});await rpc('state.save',{enabledSkills:'web'});
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
  await rpc('inbox.add',{chatId:queueChat.id,providerId:provider.id,text:'use French now',mode:'steering'});
  assert.equal((await rpc('inbox.list',{chatId:queueChat.id})).length,2);
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
});
