const test = require('node:test'), assert = require('node:assert/strict'), Module = require('node:module');

test('application capture selects exact window, preserves mapping and never falls back to desktop', async () => {
  let query, calls = 0, available = true;
  const original = Module._load;
  Module._load = function(name, ...args) {
    if (name === 'electron') return {
      screen: {getCursorScreenPoint: () => ({x:10,y:20}), dipToScreenPoint: p => p},
      desktopCapturer: {getSources: async options => {
        query = options;
        return available ? [
          {id:'window:124:0',thumbnail:{isEmpty:()=>false,toDataURL:()=>{throw new Error('Wrong window');}}},
          {id:'window:123:0',thumbnail:{isEmpty:()=>false,toDataURL:()=> 'data:image/png;base64,AAAA'}}
        ] : [];
      }}
    };
    return original.call(this,name,...args);
  };
  let captureApplication;
  try { ({captureApplication} = require('../application-capture.cjs')); } finally { Module._load = original; }
  const win = {webContents:{executeJavaScript: async code => {
    calls++; assert.ok(code.includes('data:image/png;base64,AAAA'));
    return {data:'AAAA',mime:'image/png',width:600,height:400};
  }}};
  const p = {window_id:'win:123:456',application_window:{id:'win:123:456',nativeId:123,visible:true,minimized:false,x:-1200,y:-100,width:1200,height:800},max_width:600};
  const result = await captureApplication(win,p);
  assert.deepEqual(query.types,['window']); assert.deepEqual(query.thumbnailSize,{width:600,height:400});
  assert.deepEqual(result.captured_region,{x:-1200,y:-100,width:1200,height:800});
  assert.equal(result.window_id,p.window_id); assert.equal(result.width,600); assert.equal(calls,1);
  available = false;
  await assert.rejects(captureApplication(win,p),/No desktop fallback/);
  assert.equal(calls,1);
  await assert.rejects(captureApplication(win,{...p,application_window:{...p.application_window,minimized:true}}),/unavailable/);
  await assert.rejects(captureApplication(win,{...p,window_id:'other'}),/unavailable/);
  await assert.rejects(captureApplication(win,{...p,max_width:-1}),/Invalid/);
});
