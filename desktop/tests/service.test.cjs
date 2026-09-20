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
  let requests=0,active=0,peak=0;
  const server=http.createServer(async(req,res)=>{
    if(req.url.endsWith('/models')){res.setHeader('Content-Type','application/json');res.end('{"data":[{"id":"test-model"}]}');return;}
    const buffers=[];for await(const chunk of req)buffers.push(chunk);const body=JSON.parse(Buffer.concat(buffers));requests++;active++;peak=Math.max(peak,active);
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
});
