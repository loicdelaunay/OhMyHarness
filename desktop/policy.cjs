const path = require('node:path');
function allowedNavigation(value) {
  try {
    const url = new URL(value);
    return !url.username && !url.password && (url.protocol === 'https:' || url.protocol === 'omh-preview:' ||
      (url.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)) || value === 'about:blank');
  } catch { return false; }
}
function contained(root, target) {
  const relative = path.relative(root, target);
  return relative === '' || (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative));
}
function previewPath(root, pathname) {
  const relative = decodeURIComponent(pathname).replace(/^[/\\]+/, '');
  if (relative.split(/[/\\]/).some(p => p.includes(':') || p.toLowerCase().startsWith('.env') || ['.git', '.vs', 'bin', 'obj', 'node_modules', 'secrets.json', 'appsettings.production.json'].includes(p.toLowerCase())))
    throw new Error('Protected resource.');
  const target = path.resolve(root, relative);
  if (!contained(root, target)) throw new Error('Resource outside the approved directory.');
  return target;
}
module.exports = { allowedNavigation, contained, previewPath };
