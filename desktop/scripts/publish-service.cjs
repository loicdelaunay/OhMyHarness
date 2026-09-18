const { spawnSync } = require('node:child_process');
const path = require('node:path');
const root=path.resolve(__dirname,'../..');
const rid=process.argv[2] || `${process.platform==='darwin'?'osx':'win'}-${process.arch}`;
if(!/^(osx|win)-(x64|arm64)$/.test(rid))throw new Error('Use osx-arm64, osx-x64, win-x64 or win-arm64.');
const output=path.join(root,'desktop/sidecar',rid.replace('osx-','mac-'));
const result=spawnSync('dotnet',['publish',path.join(root,'src/OhMyHarness.Service'),'-c','Release','-r',rid,'--self-contained','true',
  '-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:DebugType=None','-o',output],{stdio:'inherit',windowsHide:true});
if(result.error)throw result.error;
process.exitCode=result.status;
