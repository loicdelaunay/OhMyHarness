// Run only through the Electron executable; uses a disposable database and local fixture server.
const {app,BrowserWindow,webContents}=require('electron');
const fs=require('node:fs/promises'),path=require('node:path'),http=require('node:http'),assert=require('node:assert/strict');
const directory=path.resolve(__dirname,'../.smoke');
process.env.OHMYHARNESS_TEST_DATA=path.join(directory,'run-'+Date.now());
require('../main.cjs');
let server;
async function waitFor(fn){const until=Date.now()+25000;while(Date.now()<until){const result=await fn();if(result)return result;await new Promise(r=>setTimeout(r,80));}throw new Error('Smoke test timed out');}
(async()=>{
  await fs.mkdir(directory,{recursive:true});
  server=http.createServer(async(req,res)=>{
    if(req.url==='/page'){res.setHeader('Content-Type','text/html');res.end('<!doctype html><h1>Browser fixture</h1><input id="name"><button id="click" onclick="this.textContent=\'Clicked\'">Click me</button>');return;}
    const chunks=[];for await(const chunk of req)chunks.push(chunk);const body=JSON.parse(Buffer.concat(chunks));
    res.writeHead(200,{'Content-Type':'text/event-stream'});
    res.write('data: '+JSON.stringify({choices:[{delta:{reasoning_content:'Checking the UI',content:'# Hello\n\nA streamed response.'}}]})+'\n\n');
    setTimeout(()=>res.end('data: '+JSON.stringify({choices:[],usage:{prompt_tokens:120,completion_tokens:18}})+'\n\ndata: [DONE]\n\n'),1600);
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  await app.whenReady();
  const win=await waitFor(()=>BrowserWindow.getAllWindows()[0]);
  const evaluate=source=>win.webContents.executeJavaScript(source,true);
  await waitFor(()=>evaluate('!!window.harness && document.getElementById("platform")?.textContent.length > 0').catch(()=>false));
  const base=`http://127.0.0.1:${server.address().port}`;
  const model=await evaluate(`window.harness.call('provider.save',${JSON.stringify({name:'Smoke provider',baseUrl:base+'/v1',kind:'openai',model:'smoke',key:'fake-test-key',supportsImages:true})})`);
  const snapshot=await evaluate(`window.harness.call('snapshot')`),project=snapshot.projects[0];
  const chat=await evaluate(`window.harness.call('chat.save',{projectId:${project.id},title:'Smoke conversation'})`);
  await evaluate(`(async()=>{await refresh();providerId=${model.id};projectId=${project.id};await selectChat(${chat.id});document.getElementById('composer').value='Hello';document.getElementById('send').click();})()`);
  await waitFor(()=>evaluate(`document.querySelector('.chat-row progress') !== null`));
  assert.equal(await evaluate(`document.getElementById('new-chat').disabled`),false);
  assert.equal(await evaluate(`document.getElementById('composer').disabled`),false);
  await evaluate(`document.getElementById('new-chat').click()`);
  await waitFor(()=>evaluate(`chatId!==${chat.id} && !document.getElementById('send').disabled`));
  await waitFor(()=>evaluate(`!running.has(${chat.id})`));
  await evaluate(`selectChat(${chat.id})`);
  assert.ok((await evaluate(`document.getElementById('messages').textContent`)).includes('streamed response'));
  await new Promise(r=>setTimeout(r,250));
  await fs.writeFile(path.join(directory,'desktop-chat.png'),(await win.webContents.capturePage()).toPNG());
  await evaluate(`(async()=>{showTools(true);selectTab('web');await window.harness.host('browser.navigate',{url:${JSON.stringify(base+'/page')}})})()`);
  const remote=webContents.getAllWebContents().find(x=>x.getURL()===base+'/page');assert.ok(remote);
  assert.equal(await remote.executeJavaScript('typeof window.harness'), 'undefined');
  assert.equal(await remote.executeJavaScript('typeof require'), 'undefined');
  await new Promise(r=>setTimeout(r,250));
  await fs.writeFile(path.join(directory,'desktop-browser.png'),(await win.webContents.capturePage()).toPNG());
  console.log('SMOKE OK: rendering, streaming, navigation during generation, isolated browser.');
  server.closeAllConnections();server.close();win.close();
})().catch(error=>{console.error(error);server?.closeAllConnections();server?.close();app.exit(1);});
setTimeout(()=>{console.error('Smoke timeout');app.exit(1);},45000).unref();
