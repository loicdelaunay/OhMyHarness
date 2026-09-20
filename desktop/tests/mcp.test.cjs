const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),http=require('node:http'),readline=require('node:readline');
const {spawn}=require('node:child_process');

test('MCP transports, permissions, live toggles and automatic continuation', {timeout:90000}, async t=>{
  const directory=await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(),'omh-mcp-'))),marker=path.join(directory,'fixture.log');
  const child=spawn('dotnet',[path.resolve(__dirname,'../../src/OhMyHarness.Service/bin/Release/net10.0/OhMyHarness.Service.dll'),'--database',path.join(directory,'database.sqlite')],{stdio:['pipe','pipe','pipe'],windowsHide:true});
  let sequence=0,readyResolve,readyReject,stderr='',onPermission,apiCalls=0,httpMcpCalls=0,authHeader;
  const pending=new Map(),events=[],httpMethods=[];const ready=new Promise((resolve,reject)=>{readyResolve=resolve;readyReject=reject;});
  const write=value=>child.stdin.write(JSON.stringify(value)+'\n');
  child.stderr.on('data',chunk=>stderr+=chunk);child.on('error',readyReject);
  child.on('exit',()=>{readyReject(new Error(stderr||'Service exited'));for(const item of pending.values())item.reject(new Error(stderr||'Service exited'));});
  readline.createInterface({input:child.stdout}).on('line',async line=>{
    const message=JSON.parse(line);if(message.ready)return readyResolve();if(message.event){events.push(message);return;}
    if(message.hostRequest){let result;
      if(message.method==='permission'){if(onPermission)await onPermission(message.parameters);result='always';}
      // The macOS test double only emulates the host contract, not actual Keychain encryption.
      else if(message.method==='key.encrypt')result=Buffer.from(message.parameters.text).toString('base64');
      else if(message.method==='key.decrypt')result=Buffer.from(message.parameters.data,'base64').toString();
      else return write({hostResponse:message.hostRequest,error:'Unexpected host method'});
      return write({hostResponse:message.hostRequest,result});
    }
    const item=pending.get(message.id);if(item){pending.delete(message.id);message.error?item.reject(new Error(message.error)):item.resolve(message.result);}
  });
  const rpc=(method,parameters={})=>new Promise((resolve,reject)=>{const id=String(++sequence);pending.set(id,{resolve,reject});write({id,method,parameters});});
  const waitFor=async fn=>{const until=Date.now()+10000;while(Date.now()<until){if(await fn())return;await new Promise(r=>setTimeout(r,20));}throw new Error('Timed out');};
  const server=http.createServer(async(req,res)=>{
    if(req.method!=='POST'){res.writeHead(405);res.end();return;}
    const chunks=[];for await(const c of req)chunks.push(c);const body=JSON.parse(Buffer.concat(chunks));
    if(req.url==='/mcp'){
      httpMcpCalls++;httpMethods.push(body.method);authHeader=req.headers.authorization;res.setHeader('Content-Type','application/json');
      if(body.id===undefined){res.writeHead(202);res.end();return;}
      if(!['initialize','tools/list','tools/call'].includes(body.method)){res.end(JSON.stringify({jsonrpc:'2.0',id:body.id,error:{code:-32601,message:'Method not found'}}));return;}
      const result=body.method==='initialize'?{protocolVersion:body.params.protocolVersion,serverInfo:{name:'http-fixture',version:'1'},capabilities:{tools:{}}}:body.method==='tools/list'?{tools:[{name:'echo',description:'HTTP echo',inputSchema:{type:'object',properties:{}}}]}:{content:[{type:'text',text:'http result'}]};
      res.end(JSON.stringify({jsonrpc:'2.0',id:body.id,result}));return;
    }
    apiCalls++;const results=body.messages.filter(x=>x.role==='tool');
    const toolName=(body.tools||[]).find(x=>x.function.description.includes(body.model==='slow'?' / slow:':' / echo:'))?.function.name;
    const shouldCall=toolName&&(body.model==='chain'?results.length<13:results.length===0);
    const delta=shouldCall?{tool_calls:[{index:0,id:'call_'+apiCalls,type:'function',function:{name:toolName,arguments:JSON.stringify({text:'fixture '+results.length})}}]}:{content:'MCP finished'};
    res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: [DONE]\n\n');
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  t.after(async()=>{child.stdin.end();await new Promise(resolve=>{if(child.exitCode!=null)return resolve();child.once('exit',resolve);setTimeout(()=>child.kill(),5000).unref();});server.closeAllConnections();await new Promise(resolve=>server.close(resolve));await fs.rm(directory,{recursive:true,force:true});});
  await ready;const initial=await rpc('snapshot');assert.equal(initial.state.autoContinue,false);assert.deepEqual(initial.mcpServers,[]);
  const configuration={name:'Fixture stdio',transport:'stdio',command:process.execPath,argumentsJson:JSON.stringify([path.join(__dirname,'fixture.mcp.cjs'),marker]),enabled:true,secrets:JSON.stringify({environment:{MCP_FIXTURE_TOKEN:'fixture-secret'}})};
  await assert.rejects(rpc('mcp.save',{...configuration,argumentsJson:'invalid'}));
  const local=await rpc('mcp.save',configuration);assert.equal(local.hasSecrets,true);assert.equal(local.protectedSecrets,undefined);
  await rpc('state.save',{permissionMode:'deny'});assert.equal((await rpc('mcp.test',{id:local.id})).error,'Access denied.');await assert.rejects(fs.stat(marker));
  await rpc('state.save',{permissionMode:'ask'});assert.deepEqual((await rpc('mcp.test',{id:local.id})).tools,['echo','slow']);
  const remote=await rpc('mcp.save',{name:'Fixture HTTP',transport:'http',url:`http://127.0.0.1:${server.address().port}/mcp`,enabled:false,secrets:JSON.stringify({headers:{Authorization:'Bearer fixture-header'}})});
  const remoteTest=await rpc('mcp.test',{id:remote.id});assert.deepEqual(remoteTest.tools,['echo'],JSON.stringify({remoteTest,httpMethods}));assert.equal(authHeader,'Bearer fixture-header');assert.ok(httpMcpCalls>=2);
  const provider=await rpc('provider.save',{name:'fixture',kind:'openai',baseUrl:`http://127.0.0.1:${server.address().port}/v1`,model:'chain',contextLimit:128000,key:'not-real'});
  const createChat=async title=>rpc('chat.save',{projectId:initial.projects[0].id,title});
  const send=chat=>rpc('send',{chatId:chat.id,providerId:provider.id,text:'Run fixture'});
  const limited=await createChat('limited');let before=apiCalls;await send(limited);assert.equal(apiCalls-before,12);
  assert.match(events.findLast(x=>x.event==='done'&&x.chatId===limited.id).status,/12 steps/);
  const result=await rpc('history',{chatId:limited.id});assert.ok(result.some(x=>x.role==='tool'&&x.content.includes('fixture-secret')));
  await rpc('state.save',{autoContinue:true});assert.equal((await rpc('snapshot')).state.autoContinue,true);
  const unlimited=await createChat('auto');before=apiCalls;await send(unlimited);assert.equal(apiCalls-before,14);assert.equal((await rpc('history',{chatId:unlimited.id})).at(-1).content,'MCP finished');
  assert.ok(events.some(x=>x.event==='status'&&x.chatId===unlimited.id&&x.text.includes('Auto-continue')));
  await rpc('mcp.toggle',{id:local.id,enabled:false});await rpc('mcp.toggle',{id:remote.id,enabled:true});
  await rpc('provider.save',{...provider,model:'single'});
  const httpChat=await createChat('http tool');await send(httpChat);assert.ok((await rpc('history',{chatId:httpChat.id})).some(x=>x.role==='tool'&&x.content.includes('http result')));
  await rpc('mcp.toggle',{id:local.id,enabled:true});await rpc('mcp.toggle',{id:remote.id,enabled:false});
  // Revoke the remembered tool approval, then disable the server while its next call awaits approval.
  const grants=(await rpc('snapshot')).permissions;for(const grant of grants.filter(x=>x.scope.startsWith('mcp-tool|')))await rpc('permission.revoke',{id:grant.id});
  let disabled=false;onPermission=async p=>{if(p.title.endsWith(' · echo')){await rpc('mcp.toggle',{id:local.id,enabled:false});disabled=true;}};
  const toggled=await createChat('disable');const logBefore=(await fs.readFile(marker,'utf8')).split('\n').filter(x=>x.includes('called')).length;await send(toggled);assert.equal(disabled,true);
  assert.equal((await fs.readFile(marker,'utf8')).split('\n').filter(x=>x.includes('called')).length,logBefore);onPermission=null;
  await rpc('mcp.toggle',{id:local.id,enabled:true});await rpc('provider.save',{...provider,model:'slow'});
  const slow=await createChat('cancel');const generating=send(slow);generating.catch(()=>{});
  await waitFor(async()=> (await fs.readFile(marker,'utf8')).includes('"called":"slow"'));
  await rpc('stop',{chatId:slow.id});await assert.rejects(generating);assert.equal((await rpc('snapshot')).running.includes(slow.id),false);
  await rpc('mcp.delete',{id:local.id});await rpc('mcp.delete',{id:remote.id});assert.deepEqual((await rpc('snapshot')).mcpServers,[]);
});
