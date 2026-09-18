const {spawn}=require('node:child_process'),path=require('node:path');
const env={...process.env};delete env.ELECTRON_RUN_AS_NODE;
const child=spawn(require('electron'),[path.resolve(__dirname,'../tests/smoke.electron.cjs')],{env,stdio:'inherit',windowsHide:true});
child.on('error',error=>{console.error(error);process.exitCode=1;});
child.on('exit',code=>process.exitCode=code);
