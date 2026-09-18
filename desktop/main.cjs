const { app, BrowserWindow, ipcMain, dialog, safeStorage, protocol, session, systemPreferences } = require('electron');
const { spawn } = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs/promises');
const readline = require('node:readline');
const { pathToFileURL } = require('node:url');
const { createBrowser } = require('./browser.cjs');
protocol.registerSchemesAsPrivileged([{ scheme: 'omh-preview', privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true } }]);
let win, service, browser, readyResolve, readyReject, closing = false;
const ready = new Promise((resolve, reject) => { readyResolve = resolve; readyReject = reject; });
const requests = new Map(); let sequence = 0, dialogs = Promise.resolve();
const uiUrl = pathToFileURL(path.join(__dirname, 'ui/index.html')).href;
const serviceMethods = new Set(['snapshot','history','project.save','project.delete','chat.save','chat.delete','provider.save','provider.delete','provider.models',
  'state.save','template.save','template.delete','permission.revoke','browser.access','files.list','files.read','git','terminal','preview','send','stop']);
const uiHostMethods = new Set(['pick.folders','pick.images','pick.file','browser.navigate','browser.bounds','browser.back','browser.reload','system.permissions']);
function trusted(event) {
  if (event.sender !== win.webContents || event.senderFrame !== win.webContents.mainFrame || event.senderFrame.url !== uiUrl) throw new Error('Untrusted IPC sender.');
}
function write(message) { if (!service?.stdin.writable) throw new Error('Service unavailable.'); service.stdin.write(JSON.stringify(message) + '\n'); }
async function rpc(method, parameters) {
  await ready;
  const id = String(++sequence);
  return new Promise((resolve, reject) => { requests.set(id, { resolve, reject }); try { write({ id, method, parameters }); } catch (error) { requests.delete(id); reject(error); } });
}
async function host(method, p) {
  if (method === 'permission') {
    const show = async () => {
      const answer = await dialog.showMessageBox(win, { type: 'question', title: 'OhMyHarness · Autorisation / Permission', message: p.title,
        detail: String(p.details).slice(0, 20000), buttons: ['Refuser / Deny', 'Autoriser / Allow once', 'Toujours autoriser / Always allow'], defaultId: 0, cancelId: 0, noLink: true });
      return ['deny','allow','always'][answer.response];
    };
    const answer = dialogs.then(show, show); dialogs = answer.catch(() => {}); return answer;
  }
  if (method === 'key.encrypt' || method === 'key.decrypt') {
    if (process.platform !== 'darwin' || !safeStorage.isEncryptionAvailable()) throw new Error('macOS Keychain unavailable. No plaintext fallback is allowed.');
    return method === 'key.encrypt' ? safeStorage.encryptString(p.text).toString('base64') : safeStorage.decryptString(Buffer.from(p.data, 'base64'));
  }
  if (method.startsWith('pick.')) {
    const properties = method === 'pick.folders' ? ['openDirectory', 'multiSelections'] : method === 'pick.images' ? ['openFile', 'multiSelections'] : ['openFile'];
    const result = await dialog.showOpenDialog(win, { properties, ...(method === 'pick.images' ? { filters: [{ name: 'Images', extensions: ['png','jpg','jpeg','webp'] }] } : {}) });
    if (result.canceled) return [];
    if (method !== 'pick.images') return result.filePaths;
    if (result.filePaths.length > 4) throw new Error('4 images maximum.');
    return Promise.all(result.filePaths.map(async file => {
      if ((await fs.stat(file)).size > 8 * 1024 * 1024) throw new Error('8 MB maximum per image.');
      return { name: path.basename(file), mime: /\.webp$/i.test(file) ? 'image/webp' : /\.png$/i.test(file) ? 'image/png' : 'image/jpeg', data: (await fs.readFile(file)).toString('base64') };
    }));
  }
  if (method === 'system.permissions') return process.platform === 'darwin'
    ? { accessibility: systemPreferences.isTrustedAccessibilityClient(true), screen: systemPreferences.getMediaAccessStatus('screen') }
    : { accessibility: true, screen: 'granted' };
  return browser.execute(method, p);
}
async function start() {
  if (!['darwin','win32'].includes(process.platform)) throw new Error('Windows and macOS are supported.');
  const profile = session.defaultSession;
  profile.setPermissionRequestHandler((_, __, callback) => callback(false));
  profile.setPermissionCheckHandler(() => false);
  profile.on('will-download', event => event.preventDefault());
  win = new BrowserWindow({ width: 1500, height: 960, minWidth: 760, minHeight: 580, show: !process.env.OHMYHARNESS_TEST_DATA, backgroundColor: '#11141c', title: 'OhMyHarness',
    webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, nodeIntegration: false, sandbox: true } });
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', event => event.preventDefault());
  browser = createBrowser(win);
  const executable = process.platform === 'win32' ? 'OhMyHarness.Service.exe' : 'OhMyHarness.Service';
  const serviceFile = app.isPackaged ? path.join(process.resourcesPath, 'service', executable)
    : path.join(__dirname, 'sidecar', `${process.platform === 'darwin' ? 'mac' : 'win'}-${process.arch}`, executable);
  const directory = app.isPackaged ? (process.platform === 'darwin' ? path.resolve(path.dirname(process.execPath), '../../..') : path.dirname(process.execPath)) : (process.env.OHMYHARNESS_TEST_DATA || path.join(__dirname, '.data'));
  await fs.mkdir(directory, { recursive: true });
  const dbFile = path.join(directory, 'database.sqlite');
  await fs.access(directory, require('node:fs').constants.W_OK);
  service = spawn(serviceFile, ['--database', dbFile], { stdio: ['pipe','pipe','pipe'], windowsHide: true,
    env: { ...process.env, PATH: process.platform === 'darwin' ? `/opt/homebrew/bin:/usr/local/bin:${process.env.PATH || '/usr/bin:/bin'}` : process.env.PATH } });
  service.on('error', error => readyReject(error));
  let startupError = '';
  service.stderr.on('data', chunk => { startupError = (startupError + chunk.toString()).slice(-4000); });
  service.on('exit', code => {
    const error = new Error(`Service exited (${code}). ${startupError}`);
    readyReject(error); for (const request of requests.values()) request.reject(error); requests.clear();
    if (!closing && win && !win.isDestroyed()) win.webContents.send('harness:event', { event: 'fatal', error: error.message });
  });
  readline.createInterface({ input: service.stdout }).on('line', line => {
    let item; try { item = JSON.parse(line); } catch { return; }
    if (item.ready) return readyResolve();
    if (item.hostRequest) {
      host(item.method, item.parameters).then(result => write({ hostResponse: item.hostRequest, result }), error => write({ hostResponse: item.hostRequest, error: error.message })).catch(() => {});
    } else if (item.event && !win.isDestroyed()) win.webContents.send('harness:event', item);
    else if (item.id && requests.has(item.id)) {
      const request = requests.get(item.id); requests.delete(item.id); item.error ? request.reject(new Error(item.error)) : request.resolve(item.result);
    }
  });
  ipcMain.handle('harness:call', (event, method, parameters) => { trusted(event); if (!serviceMethods.has(method)) throw new Error('Method not exposed.'); return rpc(method, parameters); });
  ipcMain.handle('harness:host', (event, method, parameters) => { trusted(event); if (!uiHostMethods.has(method)) throw new Error('Host method not exposed.'); return host(method, parameters); });
  win.on('closed', () => { closing = true; browser.close(); service?.stdin.end(); setTimeout(() => service?.kill(), 5000).unref(); app.quit(); });
  await win.loadFile(path.join(__dirname, 'ui/index.html'));
  await ready;
}
app.whenReady().then(start).catch(async error => { dialog.showErrorBox('OhMyHarness', `${error.message}\nPlace the application in a writable folder beside database.sqlite.`); closing = true; service?.kill(); app.quit(); });
app.on('window-all-closed', () => app.quit());
app.on('before-quit', () => { closing = true; service?.stdin.end(); });
