// A transparent proxy in front of the WasmAppHost that injects a console
// forwarder into index.html. Everything else passes through byte for byte, so
// the .NET loader, the wasm, the shaders and the fixtures are the REAL ones —
// only the page gains a way to say what went wrong.
//
// Why this exists: a blank canvas with no visible log is the failure mode this
// milestone keeps hitting, headless Chrome does not surface console output on
// stderr, and its --remote-debugging-port refused to open here. Reading the log
// beats guessing at it.
import { createServer } from 'node:http';
import { request } from 'node:http';

const UPSTREAM = { host: '127.0.0.1', port: 5103 };
const PORT = 5199;

const INJECT = `<script>
(function () {
  const post = (t) => { try { fetch('/__log', { method: 'POST', body: t }); } catch (e) {} };
  for (const k of ['log', 'info', 'warn', 'error']) {
    const original = console[k].bind(console);
    console[k] = (...a) => { original(...a); post('[' + k + '] ' + a.map((x) => (x && x.stack) || String(x)).join(' ')); };
  }
  window.addEventListener('error', (e) => post('[onerror] ' + ((e.error && e.error.stack) || e.message)));
  window.addEventListener('unhandledrejection', (e) => post('[rejection] ' + ((e.reason && e.reason.stack) || e.reason)));
})();
</script>`;

createServer(async (req, res) => {
    if (req.method === 'POST' && req.url === '/__log') {
        const chunks = [];
        for await (const c of req) chunks.push(c);
        console.log(Buffer.concat(chunks).toString('utf8'));
        res.writeHead(204); res.end();
        return;
    }

    const upstream = request({ ...UPSTREAM, path: req.url, method: req.method, headers: req.headers }, (up) => {
        const isHtml = (up.headers['content-type'] || '').includes('text/html');
        const headers = { ...up.headers, 'cache-control': 'no-store' };

        if (!isHtml) {
            res.writeHead(up.statusCode, headers);
            up.pipe(res);
            return;
        }

        const chunks = [];
        up.on('data', (c) => chunks.push(c));
        up.on('end', () => {
            let body = Buffer.concat(chunks).toString('utf8');
            body = body.replace(/<head[^>]*>/i, (m) => m + INJECT);
            delete headers['content-length'];
            res.writeHead(up.statusCode, headers);
            res.end(body);
        });
    });

    upstream.on('error', (e) => { res.writeHead(502); res.end('upstream: ' + e.message); });
    req.pipe(upstream);
}).listen(PORT, '127.0.0.1', () => console.log('PROXY=' + PORT));
