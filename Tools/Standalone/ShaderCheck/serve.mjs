// ===========================================================================
// NowUI WebGL2 shader check.
//
//   node Tools/Standalone/ShaderCheck/serve.mjs          -> prints PORT=NNNN
//   chrome --headless=new --use-angle=swiftshader --enable-unsafe-swiftshader //          --user-data-dir=<THROWAWAY> --virtual-time-budget=25000 //          --window-size=600,400 --screenshot=out.png //          "http://127.0.0.1:NNNN/index.html"     (then /live.html)
//
// The page POSTs its result to /report and the server prints it, so the check
// is TEXT rather than a screenshot to be read by eye.
//
//   www/index.html  compiles and links every program in PROGRAMS from the
//                   annotated wwwroot/shaders tree, under every #define
//                   combination, checks the attribute and uniform tables, and
//                   then DRAWS each one and asserts pixel values against the
//                   HLSL's own arithmetic (79 checks).
//   www/live.html   compiles every PROGRAM_SOURCES entry out of nowui-gl.js --
//                   which is the copy that actually reaches gl.shaderSource --
//                   and renders the same scene through both trees, requiring
//                   the two readbacks to be byte-identical (17 checks). Two
//                   copies of the same GLSL is a divergence waiting to happen;
//                   this is what catches it.
//
// TWO THINGS THE APPARATUS ITSELF GETS WRONG, both measured here rather than
// assumed, and both worth knowing before trusting a capture:
//
//   * Use 127.0.0.1, NOT localhost. Capturing the browser host at
//     http://localhost:5103/?area=rectangles returned an all-black canvas
//     REPRODUCIBLY, while http://127.0.0.1:5103/ with the identical query
//     string, the identical build and an identical throwaway profile rendered
//     correctly. Same page, same fresh profile, different hostname spelling.
//   * Even on 127.0.0.1 a capture is intermittently blank. Two runs of one URL
//     gave 4480 bytes (a flat fill) and 44544 bytes (the real frame). A blank
//     capture is not evidence of a blank frame; re-run before believing it.
//
// proxy.mjs is the third tool: a pass-through proxy in front of the WasmAppHost
// that injects a console forwarder into index.html and prints everything the
// page logs. Headless Chrome does not put console output on stderr and its
// --remote-debugging-port refused to open here, so this is how a blank frame is
// told apart from a crashed one.
// ===========================================================================

// Tiny static server for the core-shader compile check.
//   /            -> scratchpad www/            (the harness page)
//   /live/...    -> the REAL NowUI.Web/wwwroot (shaders + nowui-gl.js under test)
// Serving the real tree rather than a copy is the point: a copy can pass while
// the file the browser host actually loads is broken.
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = fileURLToPath(new URL('.', import.meta.url));
const WWW = join(HERE, 'www');
const LIVE = 'D:/wkspaces/unity/Now-UI/Standalone/Web/NowUI.Web/wwwroot';

const TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8',
    '.glsl': 'text/plain; charset=utf-8',
    '.vert': 'text/plain; charset=utf-8',
    '.frag': 'text/plain; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
};

const server = createServer(async (req, res) => {
    let path = decodeURIComponent(req.url.split('?')[0]);

    // The page POSTs its result text here rather than leaving it in a <pre> for
    // a screenshot to be read by eye. Text out of the browser and into a file
    // is the whole point: a screenshot of a log is not a log.
    if (req.method === 'POST' && path === '/report') {
        const chunks = [];
        for await (const c of req) chunks.push(c);
        const body = Buffer.concat(chunks).toString('utf8');
        console.log('----- REPORT BEGIN -----');
        console.log(body);
        console.log('----- REPORT END -----');
        res.writeHead(204);
        res.end();
        return;
    }

    if (path === '/') path = '/index.html';
    let file;
    if (path.startsWith('/live/')) file = join(LIVE, normalize(path.slice(6)));
    else file = join(WWW, normalize(path));
    try {
        const body = await readFile(file);
        res.writeHead(200, {
            'content-type': TYPES[extname(file)] || 'application/octet-stream',
            'cache-control': 'no-store',
        });
        res.end(body);
    } catch (e) {
        res.writeHead(404, { 'content-type': 'text/plain' });
        res.end('not found: ' + file);
    }
});

server.listen(0, '127.0.0.1', () => {
    console.log('PORT=' + server.address().port);
});
