// The browser half of INowFetchProvider, and the pre-decode that makes INowImageDecoder possible.
//
// INowFetch.cs says its shape "maps 1:1 onto fetch() + ReadableStream in a browser". It does, and this file is that
// mapping and nothing else: C# (WebFetchProvider.cs) owns every decision about what a status, a header or an
// outcome MEANS; this file owns the fetch call, the reader loop and the browser's image codec.
//
// Contract with WebFetchProvider.cs — keep the two in step:
//   * The OUTCOME_* and REDIRECT_* numbers below are duplicated as constants on the C# side. Change one, change both.
//   * Bytes never cross as arguments. JS parks a chunk on the entry and tells C# how many bytes there are; C# calls
//     back into readChunk/readImage with a .NET MemoryView over its own buffer and JS writes into it. Every value
//     that crosses is a number or a string, which is the one marshalling shape this project has already proven
//     (nowui-gl.js) and the one that needs no assumptions about how byte[] marshals.
//   * A MemoryView is valid only for the duration of the call it arrived on, which is exactly when it is written.
//
// The ONE thing here that is not a straight mapping, and the reason this file is more than fifty lines:
//
//   INowImageDecoder.TryDecode is SYNCHRONOUS and every browser decode API is asynchronous. It is resolved by
//   decoding AHEAD of the call rather than by blocking one: while a response whose content type is an image
//   streams, its chunks are also retained here; when the body ends they are handed to createImageBitmap, drawn
//   into an OffscreenCanvas and read back as RGBA, and only THEN is the fetch reported complete. So by the time
//   the consumer sees the transfer finish — NowMarkdownImages polls handle.isDone — the pixels are already in the
//   decoder's cache and TryDecode is a lookup. The ordering is the whole trick: onImage strictly precedes
//   onComplete, and the C# side depends on that.
//
//   Retaining the chunks does NOT defeat the byte cap. The cap is enforced by C# returning false from onData, and
//   that still happens per chunk, before the retention: a refused chunk cancels the reader and nothing is retained
//   or decoded. Retention is additionally capped at MAX_DECODE_BYTES and only ever happens for a response the
//   server labelled (or whose first bytes sniff as) an image.
//
// Design: Docs/Standalone/StandaloneCoreDesign.md §4.4; Docs/Standalone/M2-FeatureMatrix.md §8.1.

'use strict';

// NowFetchOutcome, verbatim.
const OUTCOME_SUCCESS = 0;
const OUTCOME_CONNECTION_ERROR = 1;
const OUTCOME_PROTOCOL_ERROR = 2;
const OUTCOME_ABORTED = 3;
const OUTCOME_TIMEOUT = 4;
const OUTCOME_LIMIT_EXCEEDED = 5;

// WebFetchProvider.RedirectMode.
const REDIRECT_MANUAL = 0;
const REDIRECT_FOLLOW = 1;
const REDIRECT_ERROR = 2;

// The most this file will hold on to for the sake of a pre-decode. A response larger than this still transfers
// normally and still reaches the sink chunk by chunk; it simply is not pre-decoded, so TryDecode falls back to the
// managed PNG decoder and says so for anything else. 32 MB is far above any UI image and far below a tab's budget.
const MAX_DECODE_BYTES = 32 * 1024 * 1024;

// The managed callbacks, installed by main.js once getAssemblyExports has run. Null until then, which is why
// probe() exists: the C# side asks whether the wiring is live BEFORE it installs itself as the host's provider,
// so a stale cached main.js produces one named error at start-up instead of a fetch that silently never completes.
let managed = null;

/** Entries in flight, by the id the C# side minted. */
const entries = new Map();

// ------------------------------------------------------------------------------------------------ installation

/** Called by main.js with the assembly exports, before runMain(). */
export function setExports(exports) {
    managed = exports.NowUI.Web.WebFetchBridge;
}

// ------------------------------------------------------------------------ the imports the C# side calls (JSImport)

/**
 * Registered by main.js as setModuleImports('nowui-fetch.js', imports), so the managed [JSImport]s resolve without
 * a JSHost.ImportAsync — the same arrangement main.js already uses for the host.* functions. It also means there is
 * exactly ONE instance of this module: main.js imports it, and nothing else loads it by URL.
 */
export const imports = { probe, start, abort, release, readChunk, readImage };

/** Answers whether this module is wired to the managed side. Returns '' when it is, or a reason when it is not. */
function probe() {
    if (managed === null) return 'main.js did not call setExports on nowui-fetch.js.';
    if (typeof fetch !== 'function') return 'this browser has no fetch().';
    return '';
}

/**
 * Starts one request. Returns immediately; everything else arrives on the managed callbacks.
 *
 * headerText is the request headers as "Name: value" lines. The browser silently drops forbidden header names
 * (Host, Referer, and the rest of the fetch spec's forbidden list); that is the browser's rule, not this file's,
 * and it is reported in the port's notes rather than worked around.
 */
function start(id, url, method, headerText, timeoutSeconds, redirectMode) {
    const entry = {
        id: id,
        controller: new AbortController(),
        timer: 0,
        chunk: null,          // the chunk readChunk will copy out, parked for the duration of onData
        image: null,          // the decoded RGBA, parked for the duration of onImage
        retain: false,        // whether this response is a candidate for the image pre-decode
        retained: [],
        retainedBytes: 0,
        mime: '',
        timedOut: false,
        abortedByCaller: false,
        finished: false,
    };

    entries.set(id, entry);

    // Not awaited: start() is a synchronous managed call and the transfer is not.
    run(entry, url, method, headerText, timeoutSeconds, redirectMode);
}

/** Aborts a transfer. The reader loop's catch turns it into onComplete(Aborted). */
function abort(id) {
    const entry = entries.get(id);
    if (!entry || entry.finished) return;

    entry.abortedByCaller = true;
    try { entry.controller.abort(); } catch (e) { /* already aborted */ }
}

/** Drops every trace of a transfer. Called from the handle's Dispose; safe on an id that already completed. */
function release(id) {
    const entry = entries.get(id);
    if (!entry) return;

    clearTimer(entry);
    entry.finished = true;
    entry.retained = null;
    entry.chunk = null;
    entry.image = null;

    try { if (!entry.settled) entry.controller.abort(); } catch (e) { /* already aborted */ }

    entries.delete(id);
}

/** Copies the parked chunk into the managed buffer. Only legal from inside an onData call. */
function readChunk(id, view) {
    const entry = entries.get(id);
    const chunk = entry ? entry.chunk : null;
    if (!chunk || !view) return;

    // A MemoryView's set() demands the exact typed-array constructor; a stream chunk is already a Uint8Array, but
    // it may be a view with a byteOffset, which set() handles.
    view.set(chunk, 0);
}

/** Copies the pre-decoded RGBA into the managed buffer. Only legal from inside an onImage call. */
function readImage(id, view) {
    const entry = entries.get(id);
    const image = entry ? entry.image : null;
    if (!image || !view) return;

    view.set(image, 0);
}

// -------------------------------------------------------------------------------------------------- the transfer

async function run(entry, url, method, headerText, timeoutSeconds, redirectMode) {
    try {
        const init = {
            method: (method || 'GET').toUpperCase(),
            signal: entry.controller.signal,
            // Same-origin only. A cross-origin request still goes out — CORS decides whether the response is
            // readable — but no cookie or credential rides along with it unless the page's own origin is the target.
            credentials: 'same-origin',
            redirect: redirectMode === REDIRECT_FOLLOW ? 'follow'
                : (redirectMode === REDIRECT_ERROR ? 'error' : 'manual'),
        };

        const headers = parseHeaders(headerText);
        if (headers !== null) init.headers = headers;

        if (timeoutSeconds > 0) {
            entry.timer = setTimeout(() => {
                entry.timedOut = true;
                try { entry.controller.abort(); } catch (e) { /* already aborted */ }
            }, Math.round(timeoutSeconds * 1000));
        }

        const response = await fetch(url, init);

        if (entry.finished) return;

        // The redirect the browser will not show us.
        //
        // With redirect:'manual' the Fetch standard hands script an "opaque redirect" filtered response: type
        // 'opaqueredirect', status 0, no headers, no body — the Location is deliberately invisible. NowUI asks for
        // manual redirects precisely so it can apply its own per-hop URL policy to the Location, and in a browser
        // it cannot have it. Reported as a protocol error naming the cause rather than silently following the hop,
        // because following it is the one thing the caller asked not to happen.
        if (response.type === 'opaqueredirect') {
            managed.OnResponse(entry.id, 0, '', response.url || '', response.type || '');
            finish(entry, OUTCOME_PROTOCOL_ERROR,
                'The response is a redirect, and a browser hides the redirect target from script when the caller ' +
                'asks to follow redirects itself (fetch redirect:"manual" yields an opaque-redirect response with ' +
                'no status, no headers and no Location). NowUI\'s per-hop URL policy therefore cannot be applied. ' +
                'Start the page with ?redirects=follow to let the browser follow redirects instead, which trades ' +
                'that policy for reachability.');
            return;
        }

        const headerLines = [];
        try {
            response.headers.forEach((value, name) => headerLines.push(name + ': ' + value));
        } catch (e) { /* an opaque response has no iterable headers */ }

        const headerText2 = headerLines.join('\n');
        managed.OnResponse(entry.id, response.status, headerText2, response.url || '', response.type || '');

        if (entry.finished) return;

        entry.mime = (response.headers.get('content-type') || '').split(';')[0].trim().toLowerCase();
        entry.retain = isDecodableImageType(entry.mime);

        const ok = await readBody(entry, response);

        if (entry.finished) return;

        if (!ok) {
            // The sink refused a chunk. That is the byte cap doing its job, and it is an outcome, not an error.
            finish(entry, OUTCOME_LIMIT_EXCEEDED, 'The sink refused a chunk after accepting ' +
                entry.deliveredBytes + ' bytes; the reader was cancelled and the rest of the body was never read.');
            return;
        }

        await decodeIfImage(entry);

        if (entry.finished) return;

        finish(entry, OUTCOME_SUCCESS, '');
    } catch (error) {
        if (entry.finished) return;
        finish(entry, classify(entry, error), describe(error));
    }
}

/** Streams the body into the managed sink. Returns false when the sink refused a chunk. */
async function readBody(entry, response) {
    entry.deliveredBytes = 0;

    // 204/304 and HEAD have no body at all; and a browser without streaming bodies (none that runs this page, but
    // the fallback costs three lines) hands the whole thing over at once. Both go through the same delivery path,
    // so the sink sees the same callbacks either way.
    if (!response.body || typeof response.body.getReader !== 'function') {
        const whole = new Uint8Array(await response.arrayBuffer());
        return whole.length === 0 ? true : deliver(entry, whole);
    }

    const reader = response.body.getReader();

    for (;;) {
        const step = await reader.read();

        if (entry.finished) {
            try { await reader.cancel(); } catch (e) { /* the stream is already gone */ }
            return false;
        }

        if (step.done) return true;

        const chunk = step.value;
        if (!chunk || chunk.length === 0) continue;

        if (!deliver(entry, chunk)) {
            // The abort the contract is built around: INowFetchSink.OnData returning false stops the transfer
            // mid-flight, so an over-large response is never fully received. cancel() releases the connection.
            try { await reader.cancel(); } catch (e) { /* the stream is already gone */ }
            return false;
        }
    }
}

/**
 * Hands one chunk to the managed sink and, when this response is an image candidate, keeps a copy for the decode.
 * Returns whatever the sink returned.
 */
function deliver(entry, chunk) {
    entry.chunk = chunk;

    let keep;
    try {
        keep = managed.OnData(entry.id, chunk.length);
    } finally {
        entry.chunk = null;
    }

    if (!keep) return false;

    entry.deliveredBytes += chunk.length;

    // Sniff, once, when the server did not say. A .png served as application/octet-stream is common enough that
    // refusing to decode it would be a self-inflicted limitation.
    if (!entry.retain && entry.deliveredBytes === chunk.length && entry.mime === '')
        entry.retain = sniffsAsImage(chunk);

    if (entry.retain && entry.retained !== null) {
        entry.retainedBytes += chunk.length;

        if (entry.retainedBytes > MAX_DECODE_BYTES) {
            // Too big to pre-decode. Let the transfer finish normally and drop the copies.
            entry.retain = false;
            entry.retained = [];
            entry.retainedBytes = 0;
        } else {
            // slice(), not the chunk itself: a stream reader may reuse its buffer.
            entry.retained.push(chunk.slice());
        }
    }

    return true;
}

// ------------------------------------------------------------------------------------------------- the pre-decode

/**
 * Decodes a retained image body into RGBA and hands it to the managed decoder cache, BEFORE the transfer is
 * reported complete. A failure here is not a transfer failure: the bytes still reached the sink, so the fetch
 * completes normally and TryDecode falls back to the managed PNG decoder.
 */
async function decodeIfImage(entry) {
    if (!entry.retain || entry.retained === null || entry.retained.length === 0) return;
    if (typeof createImageBitmap !== 'function') return;

    let bitmap = null;

    try {
        const blob = new Blob(entry.retained, entry.mime ? { type: entry.mime } : undefined);

        // premultiplyAlpha:'none' and colorSpaceConversion:'none' ask the browser NOT to touch the pixels. The
        // 2D canvas below still stores premultiplied internally, so a partially transparent source loses a little
        // precision on the round trip; that is stated in the port's notes and is why a PNG this host can decode
        // itself is decoded managed-side instead (see WebImageDecoder.TryDecode's order).
        bitmap = await createImageBitmap(blob, { premultiplyAlpha: 'none', colorSpaceConversion: 'none' });

        if (entry.finished) return;

        const width = bitmap.width;
        const height = bitmap.height;

        if (width <= 0 || height <= 0) return;

        const canvas = typeof OffscreenCanvas === 'function'
            ? new OffscreenCanvas(width, height)
            : Object.assign(document.createElement('canvas'), { width: width, height: height });

        const context = canvas.getContext('2d', { alpha: true, willReadFrequently: true });
        if (!context) return;

        context.clearRect(0, 0, width, height);
        context.drawImage(bitmap, 0, 0);

        const data = context.getImageData(0, 0, width, height).data;

        // getImageData returns a Uint8ClampedArray, and a MemoryView's set() demands the EXACT constructor. Re-view
        // the same memory as a Uint8Array rather than copying it.
        entry.image = new Uint8Array(data.buffer, data.byteOffset, data.byteLength);

        try {
            managed.OnImage(entry.id, width, height, entry.image.length);
        } finally {
            entry.image = null;
        }
    } catch (error) {
        if (!entry.finished)
            managed.OnImageFailed(entry.id, describe(error));
    } finally {
        if (bitmap && typeof bitmap.close === 'function') bitmap.close();
        entry.retained = [];
        entry.retainedBytes = 0;
    }
}

// ------------------------------------------------------------------------------------------------------ plumbing

function finish(entry, outcome, error) {
    if (entry.finished) return;

    entry.finished = true;
    entry.settled = true;
    clearTimer(entry);
    entry.retained = null;
    entry.chunk = null;
    entry.image = null;

    managed.OnComplete(entry.id, outcome, error || '');
}

function clearTimer(entry) {
    if (entry.timer !== 0) {
        clearTimeout(entry.timer);
        entry.timer = 0;
    }
}

function classify(entry, error) {
    if (entry.timedOut) return OUTCOME_TIMEOUT;
    if (entry.abortedByCaller) return OUTCOME_ABORTED;

    const name = error && error.name ? error.name : '';

    if (name === 'TimeoutError') return OUTCOME_TIMEOUT;
    if (name === 'AbortError') return OUTCOME_ABORTED;

    // A redirect refused by redirect:'error', or a malformed URL, is a protocol fault rather than a connection one.
    if (name === 'TypeError' && /redirect/i.test(String(error && error.message))) return OUTCOME_PROTOCOL_ERROR;

    // Everything else fetch can throw is a TypeError with the message "Failed to fetch" — the single opaque error
    // the Fetch standard requires so that script cannot probe the network. A CORS refusal, DNS failure, a refused
    // connection and a blocked mixed-content request are indistinguishable here, which is why the message this
    // sends back says so rather than guessing.
    return OUTCOME_CONNECTION_ERROR;
}

function describe(error) {
    if (!error) return 'The request failed.';

    const name = error.name ? error.name + ': ' : '';
    const message = error.message ? String(error.message) : String(error);

    if (message === 'Failed to fetch' || message === 'NetworkError when attempting to fetch resource.') {
        return name + message + ' — the browser reports network failures, CORS refusals, DNS failures and blocked ' +
            'mixed content with this one message and gives script no way to tell them apart.';
    }

    return name + message;
}

function parseHeaders(headerText) {
    if (!headerText) return null;

    const headers = new Headers();
    let any = false;

    for (const line of headerText.split('\n')) {
        const colon = line.indexOf(':');
        if (colon <= 0) continue;

        const name = line.slice(0, colon).trim();
        const value = line.slice(colon + 1).trim();
        if (name === '') continue;

        try {
            headers.append(name, value);
            any = true;
        } catch (e) {
            // An invalid or forbidden header name. Dropped rather than failing the request, which is what the
            // browser would do with it anyway.
        }
    }

    return any ? headers : null;
}

/** Whether createImageBitmap is worth trying on this content type. */
function isDecodableImageType(mime) {
    if (!mime || mime.indexOf('image/') !== 0) return false;

    // SVG is excluded on purpose: createImageBitmap on an SVG blob is inconsistent across browsers when the
    // document has no intrinsic size, and a vector rasterised at an arbitrary size is not what the caller asked
    // for. An SVG therefore reaches TryDecode undecoded and is refused there by name.
    return mime !== 'image/svg+xml';
}

/** The magic numbers of the raster formats a browser can decode. Used only when the server declared no type. */
function sniffsAsImage(bytes) {
    if (!bytes || bytes.length < 12) return false;

    // PNG
    if (bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47) return true;
    // JPEG
    if (bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) return true;
    // GIF
    if (bytes[0] === 0x47 && bytes[1] === 0x49 && bytes[2] === 0x46) return true;
    // BMP
    if (bytes[0] === 0x42 && bytes[1] === 0x4d) return true;
    // RIFF ....WEBP
    if (bytes[0] === 0x52 && bytes[1] === 0x49 && bytes[2] === 0x46 && bytes[3] === 0x46 &&
        bytes[8] === 0x57 && bytes[9] === 0x45 && bytes[10] === 0x42 && bytes[11] === 0x50) return true;

    return false;
}
