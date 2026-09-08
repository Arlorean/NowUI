// vscope.js - written by the verifier, against the published surface only.
//
// A signal desk: controls on the left, a drawn oscilloscope on the right, in one ui.split.
// Nothing in here reads a private member and nothing imports anything but nowui.js.

import { start, ui } from './nowui/nowui.js';

// A slider, a colour field and a dropdown have no label of their own - NowUI draws the text beside the control
// rather than inside it, and the surface now says so rather than ignoring { label } silently.
function labelled(text, body) {
  ui.row({ gap: 10, align: 'center' }, () => {
    ui.text(text, { width: 92 });
    body();
  });
}

const WAVES = ['sine', 'square', 'triangle'];

const state = {
    ratio: 0.32,
    wave: 0,
    freq: 3.0,
    amp: 0.72,
    phase: 0,
    samples: 96,
    grid: true,
    fill: false,
    trace: [0.30, 0.78, 0.95, 1],
    mode: 'dark',   // the OPTION, not its index: ui.dropdown takes and returns the option itself
    frames: 0,
};

// What the three absent functions actually do when called. Computed once, shown in the UI, so the
// claim "these three throw by name" is visible on screen rather than asserted in a report.
const absent = {};
for (const name of ['reset', 'overlay', 'contextMenu']) {
    try {
        ui[name]('probe', () => {});
        absent[name] = 'DID NOT THROW';
    } catch (e) {
        absent[name] = (e && e.name ? e.name : 'Error') + ': ' + String(e && e.message).slice(0, 64);
    }
}

function sample(t) {
    const x = t * state.freq * Math.PI * 2 + state.phase;
    if (state.wave === 1) return Math.sin(x) >= 0 ? 1 : -1;
    if (state.wave === 2) return Math.asin(Math.sin(x)) * (2 / Math.PI);
    return Math.sin(x);
}

function controls() {
    ui.heading('Signal desk');
    ui.caption('drawn with the browser surface, ' + state.frames + ' frames');

    ui.card({ padding: 12, gap: 8 }, () => {
        state.wave = ui.tabs('wave', state.wave, WAVES);
        labelled('frequency', () => { state.freq = ui.slider('freq', state.freq, 0.25, 8); });
        labelled('amplitude', () => { state.amp = ui.slider('amp', state.amp, 0.05, 1); });
        labelled('phase', () => { state.phase = ui.slider('phase', state.phase, 0, 6.28318); });
        labelled('samples', () => { state.samples = ui.intSlider('samples', state.samples, 8, 240); });
    });

    ui.card({ padding: 12, gap: 8 }, () => {
        labelled('trace colour', () => { state.trace = ui.colorField('trace', state.trace); });
        state.grid = ui.checkbox('grid', state.grid, { label: 'grid' });
        state.fill = ui.switch('fill', state.fill, { label: 'fill under curve' });
        labelled('theme', () => { state.mode = ui.dropdown('mode', state.mode, ['light', 'dark']); });
    });

    if (ui.button('Reset view')) {
        state.freq = 3; state.amp = 0.72; state.phase = 0; state.samples = 96;
    }

    ui.rule();
    ui.caption('not in this release:');
    for (const name of ['reset', 'overlay', 'contextMenu']) ui.caption(name + ' -> ' + absent[name]);
}

function scope() {
    const box = ui.canvas('scope', { grow: 1, minHeight: 240 }, (b) => {
        const w = b.width, h = b.height;
        if (w < 8 || h < 8) return;                       // frame one: nothing measured yet

        const mid = h / 2;
        const pad = 16;

        ui.gradient([0, 0, w, h], '#101826', '#1E293B', { kind: 'linear', angle: 160, radius: 8 });

        if (state.grid) {
            for (let i = 1; i < 8; i++) {
                const x = pad + (w - pad * 2) * (i / 8);
                ui.line([x, pad], [x, h - pad], { color: [1, 1, 1, 0.10], stroke: 1 });
            }
            for (let i = 1; i < 4; i++) {
                const y = pad + (h - pad * 2) * (i / 4);
                ui.line([pad, y], [w - pad, y], { color: [1, 1, 1, 0.10], stroke: 1 });
            }
        }

        ui.line([pad, mid], [w - pad, mid], { color: [1, 1, 1, 0.35], stroke: 1, dash: [6, 6] });

        // The trace: one flat point list, which is the form the surface documents for a long one.
        const n = state.samples;
        const flat = new Array(n * 2);
        for (let i = 0; i < n; i++) {
            const t = i / (n - 1);
            flat[i * 2] = pad + (w - pad * 2) * t;
            flat[i * 2 + 1] = mid - sample(t) * state.amp * (h / 2 - pad);
        }

        if (state.fill) {
            const poly = flat.slice();
            poly.push(w - pad, mid, pad, mid);
            ui.polygon(poly, { color: [state.trace[0], state.trace[1], state.trace[2], 0.25] });
        }

        for (let i = 0; i < n - 1; i++) {
            ui.line([flat[i * 2], flat[i * 2 + 1]], [flat[i * 2 + 2], flat[i * 2 + 3]],
                { color: state.trace, stroke: 2, cap: 'round' });
        }

        // Sample dots, thinned so a 240-sample trace does not become 240 circles.
        const stride = Math.max(1, Math.floor(n / 24));
        for (let i = 0; i < n; i += stride)
            ui.circle([flat[i * 2], flat[i * 2 + 1]], 3, { color: state.trace });

        // A masked badge in the corner: a rounded panel the canvas box cannot express.
        ui.mask({ rect: [w - 132, 12, 120, 34], radius: 17 }, () => {
            ui.rect([w - 132, 12, 120, 34], { color: '#0F172A' });
            ui.rect([w - 132, 12, 120 * (state.freq / 8), 34], { color: 'accent' });
        });

        // An envelope hint and a playhead marker.
        ui.bezier([pad, h - pad], [w * 0.35, h - pad - 40], [w * 0.65, pad + 40], [w - pad, pad],
            { color: [1, 1, 1, 0.18], stroke: 2, dash: [4, 6] });
        const px = pad + (w - pad * 2) * ((state.frames % 120) / 120);
        ui.triangle([px - 6, pad - 2], [px + 6, pad - 2], [px, pad + 9], { color: 'accent' });

        ui.rect([0, 0, w, h], { color: [0, 0, 0, 0], stroke: 1, strokeColor: [1, 1, 1, 0.25], radius: 8 });
    });

    ui.caption('canvas ' + Math.round(box.width) + ' x ' + Math.round(box.height) +
               (box.stale ? ' (measured last frame)' : ''));
    ui.progress(((state.frames % 120) / 120));
}

start(() => {
    state.frames++;
    window.__vstate = state;
    ui.theme(state.mode, () => {
        state.ratio = ui.split('desk', state.ratio, { grow: 1 },
            () => ui.column({ padding: 14, gap: 10 }, controls),
            () => ui.column({ padding: 14, gap: 8, grow: 1 }, scope));
    });
});
