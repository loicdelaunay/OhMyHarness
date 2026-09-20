const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),http=require('node:http'),readline=require('node:readline');
const {spawn}=require('node:child_process');

test('agent modes: Plan enforcement, delegation, project instructions and lazy custom skills',{timeout:60000},async t=>{
  const directory=await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(),'omh-agents-'))),skillRoot=path.join(directory,'skills');
  await fs.writeFile(path.join(directory,'AGENTS.md'),'PROJECT-CONVENTION-TEST');
  const child=spawn('dotnet',[path.resolve(__dirname,'../../src/OhMyHarness.Service/bin/Release/net10.0/OhMyHarness.Service.dll'),'--database',path.join(directory,'database.sqlite')],{stdio:['pipe','pipe','pipe'],windowsHide:true,env:{...process.env,OHMYHARNESS_SKILLS_DIR:skillRoot}});
  let sequence=0,readyResolve,readyReject,stderr='',calls=0,hostActions=0,childCalls=0,rootCalls=0,scenario='plan';
  const pending=new Map(),events=[],payloads=[];const ready=new Promise((resolve,reject)=>{readyResolve=resolve;readyReject=reject;});
  const write=value=>child.stdin.write(JSON.stringify(value)+'\n');
  child.stderr.on('data',data=>stderr+=data);child.on('error',readyReject);child.on('exit',()=>{readyReject(new Error(stderr||'exited'));for(const p of pending.values())p.reject(new Error(stderr||'exited'));});
  readline.createInterface({input:child.stdout}).on('line',line=>{
    const item=JSON.parse(line);if(item.ready)return readyResolve();if(item.event){events.push(item);return;}
    if(item.hostRequest){hostActions++;let result;if(item.method==='key.encrypt')result=Buffer.from(item.parameters.text).toString('base64');else if(item.method==='key.decrypt')result=Buffer.from(item.parameters.data,'base64').toString();else return write({hostResponse:item.hostRequest,error:'Unexpected privileged host action'});return write({hostResponse:item.hostRequest,result});}
    const p=pending.get(item.id);if(p){pending.delete(item.id);item.error?p.reject(new Error(item.error)):p.resolve(item.result);}
  });
  const rpc=(method,parameters={})=>new Promise((resolve,reject)=>{const id=String(++sequence);pending.set(id,{resolve,reject});write({id,method,parameters});});
  const tool=(name,args)=>({tool_calls:[{index:0,id:'call'+calls,type:'function',function:{name,arguments:JSON.stringify(args)}}]});
  const server=http.createServer(async(req,res)=>{
    const parts=[];for await(const part of req)parts.push(part);const body=JSON.parse(Buffer.concat(parts));payloads.push(body);calls++;
    const isChild=body.messages[0].content.includes('You are a bounded subagent');if(isChild)childCalls++;else rootCalls++;
    const previous=body.messages.filter(x=>x.role==='tool');let delta={content:'Done'};
    if(scenario==='plan'){
      if(previous.length===0)delta=tool('write_source',{path:'blocked.md',content:'must not write'});
      else if(previous.length===1)delta=tool('run_terminal',{command:'echo blocked'});
      else if(previous.length===2)delta=tool('mcp_forged',{});
    }else if(scenario==='skill'){
      if(previous.length===0)delta=tool('load_skill',{name:'exemple-revue'});
      else if(previous.length===1)delta=tool('read_skill_resource',{name:'exemple-revue',path:'resources/checklist.md'});
    }else if(scenario==='auto'){
      if(isChild)delta=toolOrFinish();
      else if(!previous.length)delta=tool('delegate_tasks',{tasks:[{name:'Review',prompt:'Inspect conventions'}]});
    }else if(scenario==='disabled'&&!previous.length)delta=tool('delegate_tasks',{tasks:[{name:'Forbidden',prompt:'Should never start'}]});
    else if(scenario==='forced'&&isChild)delta=toolOrFinish();
    else if(scenario==='execute'&&!previous.length)delta=tool('write_source',{path:'allowed.md',content:'written'});
    function toolOrFinish(){return previous.length?{content:'Child verified sources'}:tool('read_source',{path:'AGENTS.md'});}
    res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: [DONE]\n\n');
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  t.after(async()=>{child.stdin.end();await new Promise(resolve=>{if(child.exitCode!=null)return resolve();child.once('exit',resolve);setTimeout(()=>child.kill(),3000).unref();});server.closeAllConnections();await new Promise(resolve=>server.close(resolve));await fs.rm(directory,{recursive:true,force:true});});
  await ready;
  const project=await rpc('project.save',{name:'Agent tests',folders:[directory]});
  const provider=await rpc('provider.save',{name:'Fixture',baseUrl:`http://127.0.0.1:${server.address().port}/v1`,kind:'openai',model:'test',key:'fake'});
  await rpc('state.save',{enabledSkills:'sources,write_sources,terminal,custom:exemple-revue',permissionMode:'allow'});
  async function run(mode,orchestration){calls=childCalls=rootCalls=0;payloads.length=0;const chat=await rpc('chat.save',{projectId:project.id,title:scenario});await rpc('chat.modes',{id:chat.id,executionMode:mode,orchestrationMode:orchestration});await rpc('send',{chatId:chat.id,providerId:provider.id,text:'Inspect this project'});return rpc('history',{chatId:chat.id});}
  const plan=await run('plan','disabled');
  assert.ok(plan.filter(x=>x.role==='tool').every(x=>x.content.includes('Mode Plan')));await assert.rejects(fs.stat(path.join(directory,'blocked.md')));
  assert.ok(payloads[0].messages[0].content.includes('PROJECT-CONVENTION-TEST'));
  assert.ok(!payloads[0].tools.some(x=>['write_source','run_terminal','delegate_tasks'].includes(x.function.name)));
  assert.ok(!payloads[0].messages[0].content.includes('# Exemple de skill personnalisable'));
  scenario='skill';const skill=await run('plan','disabled');assert.ok(skill.some(x=>x.role==='tool'&&x.content.includes('# Exemple de skill personnalisable')));assert.ok(skill.some(x=>x.role==='tool'&&x.content.includes('# Checklist')));
  scenario='auto';const auto=await run('plan','auto');assert.equal(childCalls,2);assert.ok(auto.some(x=>x.role==='tool'&&x.content.includes('Child verified sources')));
  scenario='disabled';await run('execute','disabled');assert.equal(childCalls,0);
  scenario='forced';const forced=await run('plan','forced');assert.equal(childCalls,4);assert.equal(rootCalls,1);assert.ok(forced.some(x=>x.content.includes('Sous-agents / Subagents')&&x.content.includes('Exploration')));
  scenario='execute';await run('execute','disabled');assert.equal(await fs.readFile(path.join(directory,'allowed.md'),'utf8'),'written');
  assert.ok(events.some(x=>x.event==='status'&&x.text.includes('Subagent')));
  const snap=await rpc('snapshot');assert.ok(snap.skills.some(x=>x.id==='custom:exemple-revue'));assert.equal(snap.skillsDirectory,skillRoot);assert.equal(snap.running.length,0);
});
