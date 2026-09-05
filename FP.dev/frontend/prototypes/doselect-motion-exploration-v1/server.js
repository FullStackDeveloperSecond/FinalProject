/* 純靜態比較伺服器：僅供本機檢視 GSAP 動態方案 A／B／C，不屬於正式應用程式。
   用法： node FP.dev/frontend/prototypes/doselect-motion-exploration-v1/server.js

   ROOT 設在 frontend/，因此頁面可以直接以相對路徑載入
   /customer-web/node_modules/gsap/index.js —— 用的是 npm 安裝、版本固定為 3.15.0
   的同一份 GSAP，不走任何 CDN，也不需要授權 token。 */
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '..', '..');
const PORT = Number(process.env.PORT || 8937);
const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.md': 'text/markdown; charset=utf-8'
};

http.createServer((req, res) => {
  let rel = decodeURIComponent(req.url.split('?')[0]);
  if (rel === '/') { rel = '/prototypes/doselect-motion-exploration-v1/index.html'; }
  const file = path.join(ROOT, rel);
  if (!file.startsWith(ROOT)) { res.writeHead(403).end('forbidden'); return; }
  fs.readFile(file, (err, buf) => {
    if (err) { res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' }).end('not found'); return; }
    res.writeHead(200, { 'content-type': TYPES[path.extname(file)] || 'application/octet-stream' }).end(buf);
  });
}).listen(PORT, () => {
  console.log('motion exploration: http://localhost:' + PORT + '/prototypes/doselect-motion-exploration-v1/index.html');
});
