const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('harness', {
  call: (method, parameters = {}) => ipcRenderer.invoke('harness:call', method, parameters),
  host: (method, parameters = {}) => ipcRenderer.invoke('harness:host', method, parameters),
  onEvent: callback => { const listener = (_, event) => callback(event); ipcRenderer.on('harness:event', listener); return () => ipcRenderer.removeListener('harness:event', listener); }
});
