const test=require('node:test'),assert=require('node:assert/strict'),path=require('node:path');
const {allowedNavigation,previewPath}=require('../policy.cjs');
test('remote browser navigation stays separate from privileged UI',()=>{
  for(const url of ['https://example.com','http://127.0.0.1:8080','http://localhost:5000','omh-preview://approved/index.html'])assert.equal(allowedNavigation(url),true);
  for(const url of ['file:///etc/passwd','javascript:alert(1)','data:text/html,hello','http://example.com','https://user:pass@example.com','ftp://example.com'])assert.equal(allowedNavigation(url),false);
});
test('preview paths reject traversal and secret files',()=>{
  const root=path.resolve('approved');
  assert.equal(previewPath(root,'/sub/app.js'),path.join(root,'sub/app.js'));
  for(const value of ['/../outside','/%2e%2e/outside','/.env','/sub/.env.local','/.ENV.local','/.git/config','/secrets.json','/appsettings.Production.json','/index.html:secret'])assert.throws(()=>previewPath(root,value));
});
