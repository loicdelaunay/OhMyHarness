const { desktopCapturer, screen } = require('electron');

// Captures a window source, never a desktop crop. Metadata comes from the trusted service.
async function captureApplication(win, p) {
  const w = p.application_window;
  if (!w || w.id !== p.window_id || !Number.isSafeInteger(w.nativeId) || !w.visible || w.minimized)
    throw new Error('Application window unavailable. Call desktop_applications.');
  if (![w.x,w.y,w.width,w.height].every(Number.isFinite) || w.width <= 0 || w.height <= 0 || w.width*w.height > 40000000)
    throw new Error('Invalid window dimensions.');
  for (const key of ['max_width','max_height','quality'])
    if (p[key] != null && (!Number.isInteger(p[key]) || p[key] <= 0)) throw new Error('Invalid '+key);
  const maxWidth = Math.min(p.max_width || 1920,4096), maxHeight = Math.min(p.max_height || 1440,4096);
  const ratio = Math.min(1,maxWidth/w.width,maxHeight/w.height);
  const sources = await desktopCapturer.getSources({types:['window'],thumbnailSize:{width:Math.max(1,Math.round(w.width*ratio)),height:Math.max(1,Math.round(w.height*ratio))}});
  const source = sources.find(s => s.id.split(':')[0] === 'window' && s.id.split(':')[1] === String(w.nativeId));
  if (!source || source.thumbnail.isEmpty()) throw new Error('Window capture unavailable or protected. No desktop fallback.');
  let pointer = screen.getCursorScreenPoint();
  if (process.platform === 'win32') pointer = screen.dipToScreenPoint(pointer);
  const region = {x:w.x,y:w.y,width:w.width,height:w.height};
  const result = await win.webContents.executeJavaScript(`(${(async (url, region, pointer, quality) => {
    const image = new Image(); image.src = url; await image.decode();
    const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
    const ctx = canvas.getContext('2d'); ctx.drawImage(image,0,0);
    if (pointer.x >= region.x && pointer.y >= region.y && pointer.x < region.x+region.width && pointer.y < region.y+region.height) {
      ctx.save(); ctx.translate((pointer.x-region.x)*canvas.width/region.width,(pointer.y-region.y)*canvas.height/region.height);
      ctx.beginPath(); ctx.moveTo(0,0); ctx.lineTo(0,20); ctx.lineTo(5,15); ctx.lineTo(10,24); ctx.lineTo(14,22); ctx.lineTo(9,13); ctx.lineTo(17,13); ctx.closePath(); ctx.fillStyle='white'; ctx.strokeStyle='#111'; ctx.lineWidth=1.5; ctx.fill(); ctx.stroke(); ctx.restore();
    }
    const mime = quality && quality < 100 ? 'image/jpeg' : 'image/png';
    return {mime,data:canvas.toDataURL(mime,Math.min(1,(quality || 90)/100)).split(',')[1],width:canvas.width,height:canvas.height};
  }).toString()})(${JSON.stringify(source.thumbnail.toDataURL())},${JSON.stringify(region)},${JSON.stringify(pointer)},${JSON.stringify(p.quality || 100)})`);
  if (Buffer.byteLength(result.data,'base64') > 8*1024*1024) throw new Error('Screenshot exceeds 8 MB. Reduce max_width/max_height.');
  return {...result,window_id:w.id,window:region,captured_region:region,cursor:'synthetic pointer at current position',warning:'Protected windows may return blank images.'};
}
module.exports = { captureApplication };
