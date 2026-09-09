// Showing the page to somebody who is not looking at it: one still, or one short animation, written to a file in
// the user's Unity project by the Editor's own preview server.
//
// WHY THIS FILE EXISTS. An agent working in somebody's Unity project can write an application into
// <ProjectRoot>/NowUI/apps/NAME.js and compose a URL for it, but it cannot look at the result, and neither can a
// user reading a conversation on a phone. A link is the richest thing NowUI can hand over and the cheapest to
// produce, but it is also the only one that requires the reader to leave what they were doing. A picture does not.
//
// WHY THE BROWSER RECORDS ITSELF. Every other route needs a tool the package cannot ship: headless Chrome, ffmpeg,
// Python with Pillow, a Node muxer. Only Assets/NowUI reaches a consumer, so a capture recipe that needs any of
// those helps this repository and nobody else. canvas.toBlob, canvas.captureStream and MediaRecorder are already
// in the browser that the Editor opened, and a ~40-line RIFF muxer turns a run of stills into one animated WebP
// with no dependency at all.
//
// WHY WEBP AND NOT MediaRecorder BY DEFAULT. Measured on Chromium 152 rather than recalled:
// MediaRecorder.isTypeSupported returns true for video/webm and its vp8/vp9/h264/av01 spellings, for video/mp4 and
// for x-matroska - and FALSE for 'image/webp' and 'image/gif'. So MediaRecorder structurally cannot produce a
// format that renders inline in a chat client; it produces a video file the reader has to open. Animated WebP
// does render inline, everywhere WebP does, so it is the default and MediaRecorder is ?format=webm for anyone who
// wants a sixth of the bytes and does not need it inline.
//
// THE TRAP THIS FILE IS SHAPED AROUND. A hidden, minimised or occluded tab runs ZERO requestAnimationFrame
// callbacks, and neither of the two capture APIs says so. Measured in one: zero rAF callbacks in three seconds, a
// canvas.toBlob still of 87 bytes in a single colour, and a three-second MediaRecorder run of 110 bytes in one
// data chunk - neither call throwing, neither reporting an error. Worse, document.visibilityState read 'visible'
// through a separate run that delivered zero rAF ticks in three seconds, so visibility is not a liveness test
// either. The only honest test is whether frames were actually drawn, so that is what is counted, and a run that
// did not draw uploads a SENTENCE explaining itself instead of an image. A missing picture with a reason beside it
// is worth more than a black one.

const MAX_CLIP_SECONDS = 15;
const MAX_FRAMES = 180;
const MAX_UPLOAD_BYTES = 8 * 1024 * 1024;

// Above this width the still is scaled down whatever ?scale= said. A 2x display doubles the drawing buffer in each
// axis and quadruples every byte count below, and an agent composing a URL cannot know the reader's display.
const MAX_STILL_WIDTH = 1600;
const MAX_CLIP_WIDTH = 960;

const UPLOAD_PATH = './__nowui/capture';
const TOKEN_PATH = './__nowui/token';

/// Where the answer is left for a driver, and for anyone reading the console. Always assigned exactly once, even
/// when the run failed, so "there is no result yet" and "the result is a failure" are different states.
const RESULT_KEY = 'nowuiCaptureResult';

// ------------------------------------------------------------------------------------------------------ options

function parseOptions(search) {
    const shot = search.get('shot');
    const clip = search.get('clip');

    const wantShot = shot === '1' || shot === 'true';
    const clipSeconds = clip === null ? NaN : Number(clip);
    const wantClip = Number.isFinite(clipSeconds) && clipSeconds > 0;

    if (!wantShot && !wantClip) return null;

    const number = (name, fallback, min, max) => {
        const raw = search.get(name);
        const parsed = raw === null ? NaN : Number(raw);
        if (!Number.isFinite(parsed)) return fallback;
        return Math.min(max, Math.max(min, parsed));
    };

    // A single path segment, and the only part of the destination the page gets to choose. The server sanitises it
    // again and computes the folder itself; this is here so a typo produces a sensible filename rather than a 400.
    const rawName = search.get('name') || (wantClip ? 'nowui-clip' : 'nowui-shot');
    const name = rawName.replace(/[^A-Za-z0-9_-]/g, '-').replace(/^-+|-+$/g, '').slice(0, 64) || 'nowui-capture';

    let format = (search.get('format') || '').toLowerCase();
    if (format !== 'png' && format !== 'webp' && format !== 'webm') format = wantClip ? 'webp' : 'png';
    // A clip cannot be a PNG and a still cannot be a WebM; asking for either is a typo, not a request.
    if (wantClip && format === 'png') format = 'webp';
    if (!wantClip && format === 'webm') format = 'png';

    return {
        clip: wantClip,
        seconds: wantClip ? Math.min(MAX_CLIP_SECONDS, clipSeconds) : 0,
        fps: Math.round(number('fps', 12, 1, 30)),
        scale: number('scale', wantClip ? 0.5 : 1, 0.05, 1),
        quality: number('quality', 0.75, 0.1, 1),
        // Seconds of running page before the capture window opens, on top of the warm-up. For a user who wants to
        // click something first, or an app whose interesting second is not its first.
        delay: number('delay', 0, 0, 30),
        format,
        name,
    };
}

// -------------------------------------------------------------------------------------------------- the RIFF mux
//
// Animated WebP by hand, because nothing has to be downloaded to do it. Every frame is already a complete WebP
// still that the browser's own encoder produced; an animation is those same compressed bitstreams re-filed inside
// ANMF chunks under one VP8X/ANIM header. No pixels are re-encoded and no library is involved.
//
// The cost of doing it this way is real and worth stating: each frame is encoded independently, so there is no
// inter-frame prediction and the file is several times larger per pixel than the equivalent WebM. Measured on a
// NowUI application: 6 s at 12 fps and 632x352 muxed to 292,576 B of WebP, against 188,419 B for 3 s at 15 fps of
// VP9 WebM at the full 1264x704 - about a seventh of the bytes per pixel-second. That is the price of a format
// that renders inline in a conversation, and ?format=webm is there for anyone who would rather not pay it.

function u32(value) {
    return [value & 0xff, (value >> 8) & 0xff, (value >> 16) & 0xff, (value >>> 24) & 0xff];
}

function u24(value) {
    return [value & 0xff, (value >> 8) & 0xff, (value >> 16) & 0xff];
}

/// Splits a WebP file into its chunks. A still from toBlob is either a bare 'VP8 ' (lossy), a bare 'VP8L'
/// (lossless), or - when the source canvas had alpha - an extended file whose payload is 'VP8X' + 'ALPH' + 'VP8 '.
/// All three are handled, because which one arrives depends on the browser and on whether the scratch canvas was
/// opaque, and guessing wrong produces a file that decodes to nothing.
function splitWebp(bytes) {
    if (bytes.length < 12) return null;
    if (String.fromCharCode(bytes[0], bytes[1], bytes[2], bytes[3]) !== 'RIFF') return null;
    if (String.fromCharCode(bytes[8], bytes[9], bytes[10], bytes[11]) !== 'WEBP') return null;

    const chunks = [];
    let at = 12;
    while (at + 8 <= bytes.length) {
        const fourcc = String.fromCharCode(bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3]);
        const size = bytes[at + 4] | (bytes[at + 5] << 8) | (bytes[at + 6] << 16) | (bytes[at + 7] * 0x1000000);
        const start = at + 8;
        if (start + size > bytes.length) break;
        chunks.push({ fourcc, payload: bytes.subarray(start, start + size) });
        at = start + size + (size & 1);   // chunks are padded to an even length; the pad byte is not counted.
    }
    return chunks;
}

/// The image chunks of one still, in the order an ANMF wants them: an optional ALPH, then the bitstream.
function frameChunks(bytes) {
    const chunks = splitWebp(bytes);
    if (!chunks) return null;

    const wanted = [];
    for (const chunk of chunks) {
        if (chunk.fourcc === 'ALPH') wanted.push(chunk);
    }
    for (const chunk of chunks) {
        if (chunk.fourcc === 'VP8 ' || chunk.fourcc === 'VP8L') { wanted.push(chunk); break; }
    }

    return wanted.length === 0 ? null : wanted;
}

function chunkBytes(fourcc, payload) {
    const out = [];
    for (let i = 0; i < 4; i++) out.push(fourcc.charCodeAt(i));
    out.push(...u32(payload.length));
    return { head: new Uint8Array(out), payload, padded: payload.length & 1 };
}

/// stills: an array of Uint8Array, each a complete WebP file of the same size. Returns one animated WebP.
export function muxAnimatedWebp(stills, width, height, frameDurationMs) {
    const body = [];

    // VP8X. Byte 0 is the feature flag byte; bit 1 (0x02) is ANIMATION. The alpha bit is set when any frame
    // carried an ALPH chunk, because a decoder that is told there is no alpha may skip one that is there.
    let anyAlpha = false;
    const perFrame = [];
    for (const still of stills) {
        const chunks = frameChunks(still);
        if (!chunks) return null;
        if (chunks.some(c => c.fourcc === 'ALPH')) anyAlpha = true;
        perFrame.push(chunks);
    }

    const vp8x = new Uint8Array([
        0x02 | (anyAlpha ? 0x10 : 0x00), 0, 0, 0,
        ...u24(width - 1),
        ...u24(height - 1),
    ]);
    body.push(chunkBytes('VP8X', vp8x));

    // ANIM: background colour BGRA (transparent, which every decoder that honours it renders as nothing behind
    // full-canvas opaque frames), then a uint16 loop count. 0 means forever, which is what an inline animation in
    // a conversation should do.
    body.push(chunkBytes('ANIM', new Uint8Array([0, 0, 0, 0, 0, 0])));

    for (const chunks of perFrame) {
        const inner = [];
        for (const chunk of chunks) inner.push(chunkBytes(chunk.fourcc, chunk.payload));

        let innerLength = 0;
        for (const piece of inner) innerLength += piece.head.length + piece.payload.length + piece.padded;

        // ANMF header: x/2, y/2, w-1, h-1, duration, flags. Offsets are in units of two pixels, so a full-canvas
        // frame at the origin is the only offset this writes. Flags bit 1 (0x02) is "do not blend": every frame
        // here is a complete opaque canvas, so blending one over the last would be work with no visible effect,
        // and it is the setting that cannot ghost.
        const header = new Uint8Array([
            ...u24(0), ...u24(0),
            ...u24(width - 1), ...u24(height - 1),
            ...u24(frameDurationMs),
            0x02,
        ]);

        const payload = new Uint8Array(header.length + innerLength);
        payload.set(header, 0);
        let at = header.length;
        for (const piece of inner) {
            payload.set(piece.head, at); at += piece.head.length;
            payload.set(piece.payload, at); at += piece.payload.length;
            if (piece.padded) { payload[at] = 0; at += 1; }
        }

        body.push(chunkBytes('ANMF', payload));
    }

    let bodyLength = 0;
    for (const piece of body) bodyLength += piece.head.length + piece.payload.length + piece.padded;

    const file = new Uint8Array(12 + bodyLength);
    file.set([0x52, 0x49, 0x46, 0x46], 0);                 // 'RIFF'
    file.set(u32(4 + bodyLength), 4);                       // size of everything after this field
    file.set([0x57, 0x45, 0x42, 0x50], 8);                  // 'WEBP'

    let at = 12;
    for (const piece of body) {
        file.set(piece.head, at); at += piece.head.length;
        file.set(piece.payload, at); at += piece.payload.length;
        if (piece.padded) { file[at] = 0; at += 1; }
    }

    return file;
}

// -------------------------------------------------------------------------------------------------- the readback

/// A scratch 2D canvas the drawing buffer is copied into once per grab, at the output size. It is what makes
/// ?scale= free - the browser does the resample - and it is where the blankness check can look at pixels without
/// a second GPU readback.
function makeScratch(width, height) {
    const scratch = document.createElement('canvas');
    scratch.width = width;
    scratch.height = height;
    // alpha:false so the encoder is handed an opaque image and emits a bare 'VP8 ' bitstream; willReadFrequently
    // because the blankness check calls getImageData on it.
    const context = scratch.getContext('2d', { alpha: false, willReadFrequently: true });
    return { scratch, context };
}

/// True when every sampled pixel is the same colour. That is the signature of both traps this file exists for: a
/// tab that is not drawing, and the drawImage-from-a-WebGL-canvas readback that this repository has already seen
/// come back solid black while gl.readPixels on the same buffer returned the right pixels.
function looksBlank(context, width, height) {
    let data;
    try {
        data = context.getImageData(0, 0, width, height).data;
    } catch (e) {
        return false;   // tainted or unavailable: not evidence of blankness, so do not claim it.
    }

    const step = Math.max(4, (Math.floor(data.length / 4 / 4096) | 0) * 4);
    const r = data[0], g = data[1], b = data[2];
    for (let i = 0; i < data.length; i += step) {
        if (data[i] !== r || data[i + 1] !== g || data[i + 2] !== b) return false;
    }
    return true;
}

/// The fallback readback, and the reason ?shot=/?clip= turn preserveDrawingBuffer on in nowui-gl.js. Bottom-up,
/// so the rows are reversed on the way in; putImageData rather than drawImage, because drawImage of a WebGL canvas
/// is the path that came back black. Slower and unscaled, so it is only used when the fast path proves blank.
function readPixelsInto(canvas, context, width, height) {
    const gl = canvas.getContext('webgl2');
    if (!gl) return false;

    const bufferWidth = canvas.width;
    const bufferHeight = canvas.height;
    const pixels = new Uint8ClampedArray(bufferWidth * bufferHeight * 4);
    gl.readPixels(0, 0, bufferWidth, bufferHeight, gl.RGBA, gl.UNSIGNED_BYTE, pixels);

    const flipped = new Uint8ClampedArray(pixels.length);
    const stride = bufferWidth * 4;
    for (let y = 0; y < bufferHeight; y++) {
        flipped.set(pixels.subarray(y * stride, y * stride + stride), (bufferHeight - 1 - y) * stride);
    }

    const full = document.createElement('canvas');
    full.width = bufferWidth;
    full.height = bufferHeight;
    full.getContext('2d').putImageData(new ImageData(flipped, bufferWidth, bufferHeight), 0, 0);

    context.drawImage(full, 0, 0, width, height);
    return true;
}

// ------------------------------------------------------------------------------------------------------ reporting

function banner(text, bad) {
    let node = document.getElementById('nowui-capture-banner');
    if (!node) {
        node = document.createElement('div');
        node.id = 'nowui-capture-banner';
        node.style.cssText =
            'position:fixed;left:0;right:0;bottom:0;z-index:2147483647;padding:10px 14px;' +
            'font:13px/1.5 ui-monospace,SFMono-Regular,Consolas,monospace;white-space:pre-wrap;' +
            'background:#101418;color:#d8dee4;border-top:2px solid #3d4650';
        document.body.appendChild(node);
    }
    node.style.borderTopColor = bad ? '#c8484c' : '#3f9a5a';
    node.textContent = text;
}

function publish(result) {
    window[RESULT_KEY] = result;
    if (result.status === 'ok') console.log('NowUI capture: ' + result.message);
    else console.error('NowUI capture: ' + result.message);
    banner('NowUI capture\n' + result.message, result.status !== 'ok');
}

// ------------------------------------------------------------------------------------------------------- upload

async function fetchToken() {
    try {
        const response = await fetch(TOKEN_PATH, { cache: 'no-store' });
        if (!response.ok) return null;
        const text = (await response.text()).trim();
        return text.length > 0 ? text : null;
    } catch (e) {
        return null;
    }
}

/// POSTs the bytes to the Editor's preview server, which decides the folder, the extension and the size limit.
/// Returns the absolute path it wrote, or throws with the server's own sentence.
async function upload(name, mime, bytes) {
    const token = await fetchToken();
    if (token === null) {
        throw new Error('this page is not being served by the Unity Editor\'s Web Preview, so there is nowhere ' +
            'to write the file. Open it from Tools > NowUI > Web Preview.');
    }

    const response = await fetch(UPLOAD_PATH + '?name=' + encodeURIComponent(name), {
        method: 'POST',
        cache: 'no-store',
        headers: { 'Content-Type': mime, 'X-NowUI-Capture': token },
        body: bytes,
    });

    const text = (await response.text()).trim();
    if (!response.ok) throw new Error('the Editor refused the upload (' + response.status + '): ' + text);
    return text;
}

/// The one thing left to try when there is no server to write to: hand the blob to the browser's own download
/// machinery. It lands in the user's Downloads folder under a name nothing else can predict, which is why it is
/// the fallback and not the mechanism - but a file the user can find beats a file that does not exist.
function offerDownload(filename, blob, why) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.textContent = 'Download ' + filename;
    link.style.cssText = 'color:#7fb2ff;display:inline-block;margin-top:6px';

    banner('NowUI capture\ncould not be written into your project: ' + why, true);
    document.getElementById('nowui-capture-banner').appendChild(link);
    window[RESULT_KEY] = {
        status: 'error', file: null, bytes: blob.size, url,
        message: 'could not be written into your project: ' + why + ' The file is offered as a download instead.',
    };
}

async function deliver(name, extension, mime, bytes, summary) {
    try {
        const file = await upload(name, mime, bytes);
        publish({ status: 'ok', file, bytes: bytes.length || bytes.size, message: summary + ' -> ' + file });
    } catch (e) {
        offerDownload(name + '.' + extension, new Blob([bytes], { type: mime }), String(e.message || e));
    }
}

/// What lands instead of an image when the page was not drawing. Same folder, same name, ".txt" - so whoever went
/// looking for the picture finds the reason in its place rather than an empty folder or, worse, a black rectangle.
async function reportNotDrawing(name, ticks, planned, seconds) {
    const message =
        'NowUI could not capture this page: it was not drawing.\n\n' +
        'The browser ran ' + ticks + ' frame' + (ticks === 1 ? '' : 's') + ' during the ' + seconds +
        's capture window; ' + planned + ' were expected.\n\n' +
        'A browser suspends requestAnimationFrame entirely in a tab that is hidden, minimised, behind another\n' +
        'window, or on a background virtual desktop, and it throttles one that is merely occluded. A page there\n' +
        'runs zero frames and produces a blank image without reporting an error, so NowUI refuses to write one.\n\n' +
        'Keep the browser window visible and in front for the whole recording, then load the URL again.\n' +
        'document.visibilityState is not enough on its own: it read "visible" through a measured run that\n' +
        'delivered no frames at all, which is why this counts frames instead.\n';

    publish({
        status: 'not-drawing', file: null, ticks, planned,
        message: 'the tab was not drawing (' + ticks + ' frames in ' + seconds + 's, expected ' + planned +
            '). No image was written; keep the window visible and try again.',
    });

    try {
        const file = await upload(name, 'text/plain', new TextEncoder().encode(message));
        window[RESULT_KEY].file = file;
        banner('NowUI capture\n' + message.trim() + '\n\nWritten to ' + file, true);
    } catch (e) {
        banner('NowUI capture\n' + message.trim(), true);
    }
}

// --------------------------------------------------------------------------------------------------------- jobs

/// The whole of the feature: a function main.js calls once per drawn frame, immediately after the frame is drawn.
/// Returns null when neither ?shot= nor ?clip= is present, which is every ordinary page load, and the cost there
/// is one URLSearchParams lookup at boot and nothing at all per frame.
export function installCapture(canvas) {
    let options;
    try {
        options = parseOptions(new URLSearchParams(location.search));
    } catch (e) {
        return null;
    }
    if (options === null) return null;

    // Warm-up, and not a nicety: the first NowUI frame in the browser takes 47-450 ms depending on how much text
    // is on the page (every glyph is rasterised into the atlas on first sight) and every frame after it takes
    // about 10. Grabbing frame one photographs a half-built page.
    const WARMUP_TICKS = 10;

    // Set on the FIRST DRAWN FRAME, not at install: everything before that is the .NET runtime coming up, and a
    // ?delay= that the wasm boot had already eaten would be a delay the caller did not get.
    let openAt = Infinity;

    let ticks = 0;                 // frames drawn since install
    let windowTicks = 0;           // frames drawn since the capture window opened
    let state = 'warmup';          // warmup -> running -> done
    let scratch = null;
    let context = null;
    let width = 0;
    let height = 0;
    let usePixelReadback = false;
    let checkedReadback = false;

    const stills = [];
    let pending = 0;               // toBlob calls that have not called back yet
    let windowOpenedAt = 0;
    let lastGrabAt = 0;
    let recorder = null;
    let recorderChunks = null;
    let recorderMime = null;

    // A long clip at a high frame rate slows DOWN rather than stopping early. Capping the frame count alone
    // would silently hand back the first six seconds of a fifteen-second request, which is the kind of quiet
    // truncation that gets reported as "the recording is wrong" a week later.
    if (options.clip && options.seconds * options.fps > MAX_FRAMES)
        options.fps = Math.max(1, Math.floor(MAX_FRAMES / options.seconds));

    const plannedFrames = options.clip
        ? Math.max(1, Math.min(MAX_FRAMES, Math.round(options.seconds * options.fps)))
        : 1;

    banner('NowUI capture\npreparing a ' + (options.clip
        ? options.seconds + 's ' + options.format + ' clip at ' + options.fps + ' fps'
        : options.format.toUpperCase() + ' still') +
        '. Keep this window visible and in front - a hidden tab draws nothing.', false);

    // The watchdog, on a wall clock rather than on the frame loop, because the failure it catches is the frame
    // loop having stopped. setTimeout is throttled to about 1 Hz in a background tab but it still fires, which is
    // exactly the difference between reporting "the tab was not drawing" and hanging forever.
    //
    // TWO budgets, because "the page has not drawn yet" means two different things. Before the first frame it can
    // still be the .NET runtime coming up - seconds of wasm instantiation on a cold load - so the wait is long.
    // After the first frame the page has proved it can draw, so a stall is a real failure and the wait is short.
    // One budget for both would either report a booting page as dead or leave a hidden one hanging.
    const RUNNING_BUDGET_MS = 300 + options.delay * 1000 + (options.clip ? options.seconds * 1000 : 0) + 4000;
    const BOOT_BUDGET_MS = 20000 + options.delay * 1000 + (options.clip ? options.seconds * 1000 : 0);
    let watchdog = setTimeout(() => { if (state !== 'done') finish('watchdog'); }, BOOT_BUDGET_MS);

    function sizeOutput() {
        const cap = options.clip ? MAX_CLIP_WIDTH : MAX_STILL_WIDTH;
        let w = Math.max(2, Math.round(canvas.width * options.scale));
        let h = Math.max(2, Math.round(canvas.height * options.scale));
        if (w > cap) { h = Math.max(2, Math.round(h * (cap / w))); w = cap; }
        // Even dimensions: a video encoder generally insists on them and the WebP muxer is happier with them.
        width = w - (w & 1);
        height = h - (h & 1);
        const made = makeScratch(width, height);
        scratch = made.scratch;
        context = made.context;
    }

    /// One copy of the drawing buffer into the scratch canvas, decided the first time and then repeated. Called
    /// only from inside main.js's frame callback, in the same task as the draw: without preserveDrawingBuffer the
    /// buffer is gone by the time the compositor is finished with it, and reading it from a timer or a driver is
    /// how this returns a blank image.
    function grabIntoScratch() {
        if (!usePixelReadback) {
            context.drawImage(canvas, 0, 0, width, height);

            if (!checkedReadback) {
                checkedReadback = true;
                if (looksBlank(context, width, height)) {
                    // Either the page really is one flat colour, or drawImage of a WebGL canvas came back black -
                    // a trap this repository has already been caught by once. Try the slow path; if that is blank
                    // too, believe the page.
                    usePixelReadback = true;
                    if (!readPixelsInto(canvas, context, width, height)) usePixelReadback = false;
                }
            }
            return;
        }

        if (!readPixelsInto(canvas, context, width, height)) {
            usePixelReadback = false;
            context.drawImage(canvas, 0, 0, width, height);
        }
    }

    function grabStill(type, quality) {
        pending++;
        const slot = stills.length;
        stills.push(null);
        scratch.toBlob(async (blob) => {
            if (blob) stills[slot] = new Uint8Array(await blob.arrayBuffer());
            pending--;
        }, type, quality);
    }

    function startRecorder() {
        const candidates = [
            'video/webm;codecs=vp9',
            'video/webm;codecs=vp8',
            'video/webm',
            'video/mp4;codecs=avc1.42E01E',
            'video/mp4',
        ];
        const supported = typeof MediaRecorder !== 'undefined' && MediaRecorder.isTypeSupported
            ? candidates.find(m => MediaRecorder.isTypeSupported(m))
            : null;

        if (!supported || typeof canvas.captureStream !== 'function') {
            // Not a failure worth abandoning the run for: the other format needs nothing this browser lacks.
            console.warn('NowUI capture: MediaRecorder cannot record this canvas here, so the clip will be an ' +
                'animated WebP instead.');
            options.format = 'webp';
            return false;
        }

        recorderMime = supported;
        recorderChunks = [];
        const stream = canvas.captureStream(options.fps);
        recorder = new MediaRecorder(stream, { mimeType: supported, videoBitsPerSecond: 2500000 });
        recorder.ondataavailable = (event) => { if (event.data && event.data.size > 0) recorderChunks.push(event.data); };
        recorder.start();
        return true;
    }

    async function finish(reason) {
        if (state === 'done') return;
        state = 'done';
        clearTimeout(watchdog);

        const seconds = options.clip ? options.seconds : 0;

        if (recorder && recorder.state !== 'inactive') {
            await new Promise(resolve => { recorder.onstop = resolve; recorder.stop(); });
        }

        // THE CHECK. Not visibilityState, not the byte count, not whether an API threw - how many frames the page
        // actually drew while it was being recorded. A hidden or throttled tab lands here with zero.
        const need = options.clip ? Math.max(4, Math.round(plannedFrames * 0.25)) : 1;
        if (windowTicks < need) {
            await reportNotDrawing(options.name, windowTicks, plannedFrames, seconds || 1);
            return;
        }

        if (options.clip && options.format === 'webm') {
            const blob = new Blob(recorderChunks, { type: recorderMime });
            const bytes = new Uint8Array(await blob.arrayBuffer());
            const extension = recorderMime.startsWith('video/mp4') ? 'mp4' : 'webm';
            await deliver(options.name, extension, recorderMime.split(';')[0], bytes,
                seconds + 's ' + extension + ', ' + width + 'x' + height + ', ' + bytes.length + ' B');
            return;
        }

        // Let the outstanding toBlob callbacks land. They are queued tasks, not frames, so they run whether or not
        // the page is still drawing; a bounded wait rather than an unbounded one all the same.
        for (let i = 0; i < 200 && pending > 0; i++) await new Promise(r => setTimeout(r, 10));

        let grabbed = stills.filter(Boolean);
        if (grabbed.length === 0) {
            publish({
                status: 'error', file: null,
                message: 'the browser encoded no frames (' + reason + '). Nothing was written.',
            });
            return;
        }

        if (!options.clip) {
            await deliver(options.name, 'png', 'image/png', grabbed[0],
                'still ' + width + 'x' + height + ', ' + grabbed[0].length + ' B');
            return;
        }

        // THE FRAME DURATION IS MEASURED, NOT REQUESTED, and the difference is visible. A browser that cannot
        // hold the asked-for rate simply delivers fewer frames - Firefox 155 returned 42 of 60 on a five second
        // clip where Chrome returned 60 - and stamping those 42 with 1000/fps produces 3.5 seconds of playback
        // for five seconds of recording: the whole clip runs 30% fast, and every animation in it lies about its
        // own speed. Dividing the real capture window by the frames that actually arrived makes the clip last as
        // long as the recording did, on whatever rate the browser managed.
        const windowMs = Math.max(1, performance.now() - windowOpenedAt);
        let durationMs = Math.min(10000, Math.max(10, Math.round(windowMs / grabbed.length)));
        let file = muxAnimatedWebp(grabbed, width, height, durationMs);

        // The size cap, paid for by frame rate rather than by quality: halving the frames halves the file and
        // still shows the motion, where dropping quality far enough to matter makes NowUI's gradients band.
        while (file && file.length > MAX_UPLOAD_BYTES && grabbed.length > 4) {
            grabbed = grabbed.filter((_, i) => i % 2 === 0);
            durationMs *= 2;
            file = muxAnimatedWebp(grabbed, width, height, durationMs);
        }

        if (!file) {
            publish({
                status: 'error', file: null,
                message: 'this browser\'s canvas.toBlob did not produce WebP stills, so no animation could be ' +
                    'assembled. Ask for ?format=webm, or take a still with ?shot=1.',
            });
            return;
        }

        await deliver(options.name, 'webp', 'image/webp', file,
            grabbed.length + ' frames, ' + width + 'x' + height + ', ' +
            Math.round(1000 / durationMs) + ' fps, ' + file.length + ' B');
    }

    return function captureTick() {
        if (state === 'done') return;

        // The first frame: the page is up, so the clocks start here rather than at install.
        if (ticks === 0) {
            openAt = performance.now() + 300 + options.delay * 1000;
            clearTimeout(watchdog);
            watchdog = setTimeout(() => { if (state !== 'done') finish('watchdog'); }, RUNNING_BUDGET_MS);
        }

        ticks++;

        if (state === 'warmup') {
            if (ticks < WARMUP_TICKS || performance.now() < openAt) return;

            state = 'running';
            windowOpenedAt = performance.now();
            lastGrabAt = 0;
            sizeOutput();

            if (options.clip && options.format === 'webm' && !startRecorder()) {
                // startRecorder rewrote the format; fall through into the WebP path on this same frame.
            }
        }

        windowTicks++;

        const now = performance.now();

        if (!options.clip) {
            grabIntoScratch();
            grabStill('image/png');
            finish('still');
            return;
        }

        if (options.format !== 'webm') {
            const interval = 1000 / options.fps;
            if (lastGrabAt === 0 || now - lastGrabAt >= interval - 1) {
                lastGrabAt = now;
                grabIntoScratch();
                grabStill('image/webp', options.quality);
            }
            if (stills.length >= plannedFrames) { finish('frames'); return; }
        }

        if (now - windowOpenedAt >= options.seconds * 1000) finish('elapsed');
    };
}
