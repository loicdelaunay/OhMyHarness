// Local MCP fixture, never included in the packaged application.
const readline=require('node:readline'),fs=require('node:fs');
const marker=process.argv[2],timers=new Map();
if(marker)fs.appendFileSync(marker,JSON.stringify({started:process.pid})+'\n');
const write=value=>process.stdout.write(JSON.stringify({jsonrpc:'2.0',...value})+'\n');
readline.createInterface({input:process.stdin}).on('line',line=>{
  const message=JSON.parse(line),{id,method,params}=message;
  if(method==='initialize')return write({id,result:{protocolVersion:params.protocolVersion,serverInfo:{name:'fixture',version:'1.0'},capabilities:{tools:{}}}});
  if(method==='tools/list')return write({id,result:{tools:['echo','slow'].map(name=>({name,description:name,inputSchema:{type:'object',properties:{text:{type:'string'}}}}))}});
  if(method==='tools/call'){
    const respond=()=>write({id,result:{content:[{type:'text',text:`${params.arguments?.text||''}|${process.env.MCP_FIXTURE_TOKEN||'none'}`}],isError:false}});
    if(marker)fs.appendFileSync(marker,JSON.stringify({called:params.name})+'\n');
    if(params.name==='slow')timers.set(id,setTimeout(respond,30000));else respond();return;
  }
  if(method==='notifications/cancelled'){clearTimeout(timers.get(params.requestId));return;}
  if(id!==undefined)write({id,error:{code:-32601,message:'Method not found'}});
}).on('close',()=>process.exit(0));
