// draw.js - the W10 acceptance application, and the first thing to read if you want to see what the JavaScript
// surface can now do. Served as `?app=draw`.
//
// Unlike w9-draw.js, which talked to the recorder directly to prove the WIRE, everything below is the public
// surface: `import { start, ui } from './nowui/nowui.js'` and nothing else. Controls and drawings are in the same
// frame, in the same layout, and the controls DRIVE the drawings - which is the claim the picture has to back up.
//
// What it exercises, on purpose, so the capture can be read as a checklist:
//   ui.split       two panes and a draggable divider  (implemented in W10; it used to throw)
//   ui.theme       a dark scope beside a light one    (implemented in W10; it used to throw)
//   ui.canvas      one layout box, a coordinate origin, and a clip
//   ui.mask        a circular and a rounded-rect clip over a subset of it
//   ui.rect / circle / line / bezier / triangle / polygon / gradient
//   the colour forms: a theme token, a hex literal, and the array ui.colorField returns
//   the controls:  tabs, slider, intSlider, dropdown, colorField, switch, checkbox, button, caption

import { start, ui } from './nowui/nowui.js';

const TINTS = ['accent', 'success', 'warning', 'danger'];

const state = {
    ratio: 0.34,
    tab: 0,
    points: 14,
    thickness: 3,
    tint: 'accent',
    custom: [0.20, 0.60, 0.95, 1],
    filled: true,
    dark: true,
    seed: 7,
};

/// A deterministic series, so the capture is the same picture every run. `seed` is what the Randomise button
/// moves, which is the whole of the button's job.
function series(count, seed) {
    const out = [];
    let x = seed * 9301 + 49297;

    for (let i = 0; i < count; i++) {
        x = (x * 9301 + 49297) % 233280;
        out.push(0.15 + 0.7 * (x / 233280));
    }

    return out;
}

// ------------------------------------------------------------------------------------------------ the drawings
//
// Each of these takes the canvas box it was handed and draws inside it. Coordinates are CANVAS-LOCAL: (0, 0) is
// the canvas's own top-left corner, whatever the layout did with it, so none of this code knows or cares where
// on the page the canvas ended up.

/// A line chart: a gradient ground, an axis, a filled area, a bezier trend line and a dot per sample.
function chart(box) {
    const w = box.width;
    const h = box.height;
    const pad = 24;
    const values = series(state.points, state.seed);

    // The ground, and the axis it sits on.
    ui.gradient([0, 0, w, h], 'surface', 'surfaceMuted', { kind: 'linear', angle: 160 });
    ui.line([pad, h - pad], [w - pad, h - pad], { color: 'border', stroke: 1 });
    ui.line([pad, pad], [pad, h - pad], { color: 'border', stroke: 1 });

    // Four gridlines, dashed, drawn under everything else.
    for (let i = 1; i <= 4; i++) {
        const y = pad + (h - pad * 2) * (i / 5);
        ui.line([pad, y], [w - pad, y], { color: 'border', stroke: 1, dash: [5, 6] });
    }

    const at = (i) => [
        pad + (w - pad * 2) * (values.length === 1 ? 0.5 : i / (values.length - 1)),
        h - pad - (h - pad * 2) * values[i],
    ];

    // The filled area under the curve, as one polygon: the samples, then back along the axis. The flat form,
    // because this is exactly the case the flat form exists for.
    const area = [];
    for (let i = 0; i < values.length; i++) { const p = at(i); area.push(p[0], p[1]); }
    area.push(pad + (w - pad * 2), h - pad);
    area.push(pad, h - pad);

    ui.polygon(area, { color: 'accentMuted', fill: state.filled, stroke: 1, strokeColor: state.tint });

    // The trend, as one bezier through the first and last samples - the curve is deliberately not the data, it
    // is the shape of it, which is what a bezier is good for.
    const first = at(0);
    const last = at(values.length - 1);
    ui.bezier(first,
        [first[0] + (last[0] - first[0]) * 0.35, first[1]],
        [last[0] - (last[0] - first[0]) * 0.35, last[1]],
        last,
        { color: state.tint, stroke: state.thickness, cap: 'round' });

    // A dot per sample, in the colour the colour picker is holding - the same [r, g, b, a] array ui.colorField
    // returns, passed straight into `color` with no conversion anywhere. That composition is the reason the
    // literal paint tag packs exactly the way the COLOR_FIELD op does.
    for (let i = 0; i < values.length; i++) {
        const p = at(i);
        ui.circle(p, 4.5, { color: state.custom });
        ui.circle(p, 4.5, { color: 'background', fill: false, stroke: 1.5 });
    }

    // A legend, drawn rather than laid out, because at this point it is one line of code.
    ui.rect([w - 150, 14, 136, 26], { style: 'elevated', radius: 6 });
    ui.circle([w - 136, 27], 5, { color: state.custom });
    ui.rect([w - 124, 24, 40, 6], { color: state.tint, radius: 3 });
}

/// One of every primitive, labelled by position rather than by text, so a reader can name what is missing.
function shapes(box) {
    const w = box.width;
    const h = box.height;

    ui.rect([0, 0, w, h], { style: 'surface' });

    // Row one: a styled rect, a coloured rect with four different corners, and a blurred one.
    ui.rect([20, 20, 120, 64], { style: 'accent', radius: 10 });
    ui.rect([156, 20, 120, 64], { color: '#F59E0B', radius: [18, 4, 18, 4] });
    ui.rect([292, 20, 120, 64], { color: state.tint, radius: 10, blur: 6 });

    // Row two: a filled circle, a ring, and an ellipse.
    ui.circle([60, 150], 36, { color: state.custom });
    ui.circle([160, 150], 36, { color: 'danger', fill: false, stroke: state.thickness + 2, segments: 64 });
    ui.circle([270, 150], [56, 30], { color: 'success', fill: state.filled });

    // Row three: lines and a bezier.
    ui.line([20, 220], [w - 20, 220], { color: 'text', stroke: state.thickness, cap: 'round' });
    ui.line([20, 244], [w - 20, 244], { color: 'warning', stroke: state.thickness, dash: [14, 8] });
    ui.bezier([20, 300], [w * 0.3, 260], [w * 0.6, 340], [w - 20, 296],
        { color: 'success', stroke: state.thickness + 1, cap: 'round' });

    // Row four: a triangle, a polygon, a gradient.
    ui.triangle([30, 400], [130, 350], [120, 440], { color: 'accentPressed' });
    ui.polygon([[170, 350], [260, 372], [278, 440], [200, 456], [156, 400]],
        { color: state.tint, fill: state.filled, stroke: 2, strokeColor: state.tint });
    ui.gradient([300, 350, Math.max(60, w - 320), 106], '#EC4899', state.tint,
        { kind: 'linear', angle: 45, radius: 12 });

    // Row five, if there is room: two masks, so that this one view is a complete witness - every primitive,
    // both colour forms, a gradient AND a clip in one frame.
    if (h < 620) return;

    const y = 500;

    ui.mask({ circle: [86, y + 62], radius: 58, feather: 2 }, () => {
        ui.rect([0, y, 200, 130], { color: state.tint });
        for (let i = 0; i < 9; i++)
            ui.line([i * 22 - 20, y], [i * 22 + 40, y + 130], { color: 'background', stroke: 6 });
    });

    ui.mask({ rect: [186, y + 4, 190, 116], radius: [30, 6, 30, 6] }, () => {
        ui.gradient([160, y - 40, 260, 200], 'success', '#EC4899', { kind: 'linear', angle: 120 });
        ui.triangle([200, y + 130], [280, y + 10], [366, y + 130], { color: 'background' });
    });

    ui.mask({ capsule: [[420, y + 40], [Math.max(470, w - 40), y + 86]], radius: 42 }, () => {
        ui.gradient([380, y - 20, w, 190], state.custom, 'warning', { kind: 'linear', angle: 90 });
    });
}

/// The same drawings, clipped. A canvas already clips to its own box; ui.mask is for a SUBSET of it.
function masks(box) {
    const w = box.width;
    const h = box.height;

    ui.gradient([0, 0, w, h], 'background', 'surfaceMuted', { kind: 'radial' });

    // A circular clip over a band that is deliberately far larger than it - if the mask did nothing, the band
    // would cross the whole canvas and the picture would say so immediately.
    ui.mask({ circle: [w * 0.28, 130], radius: 78, feather: 2 }, () => {
        ui.rect([0, 60, w, 140], { color: state.tint });
        for (let i = 0; i < 14; i++)
            ui.line([i * 26 - 40, 60], [i * 26, 200], { color: 'background', stroke: 5 });
    });

    // A rounded-rect clip over the same idea, with a triangle and a circle escaping into it.
    ui.mask({ rect: [w * 0.52, 60, Math.max(80, w * 0.4), 140], radius: [28, 6, 28, 6] }, () => {
        ui.rect([0, 0, w, h], { color: 'success' });
        ui.triangle([w * 0.5, 220], [w * 0.75, 30], [w * 0.98, 220], { color: 'background' });
        ui.circle([w * 0.72, 130], 40, { color: state.custom });
    });

    // A capsule, and an ellipse mask, on the lower half.
    ui.mask({ capsule: [[60, 300], [w - 60, 380]], radius: 46 }, () => {
        ui.gradient([0, 240, w, 200], state.tint, '#EC4899', { kind: 'linear', angle: 90 });
    });

    ui.mask({ ellipse: [w * 0.3, 420, w * 0.4, 110] }, () => {
        ui.rect([0, 400, w, 160], { color: 'warning' });
        ui.polygon([[w * 0.3, 540], [w * 0.5, 400], [w * 0.7, 540]], { color: 'background' });
    });
}

const TABS = [chart, shapes, masks];

// ------------------------------------------------------------------------------------------------- the frame

start(() => {
    ui.column({ padding: 20, gap: 12, grow: 1 }, () => {

        ui.heading('NowUI drawing, from JavaScript');
        ui.caption('Shapes and controls in one frame. The controls on the left drive the drawing on the right; ' +
            'every coordinate is canvas-local and the decoder adds the origin.');

        // The split needs a size, and this application owns the window, so it takes it from ui.frame - which is
        // the CURRENT frame's canvas, not one frame old (R6).
        const width = Math.max(560, ui.frame.width - 40);
        const height = Math.max(420, ui.frame.height - 132);

        state.ratio = ui.split('main', state.ratio, { width, height },

            // ---- the left pane: real controls, laid out normally -------------------------------------------
            () => {
                ui.column({ padding: 16, gap: 10, grow: 1 }, () => {
                    ui.subheading('Controls');

                    state.tab = ui.tabs('view', state.tab, ['Chart', 'Shapes', 'Masks']);
                    ui.rule();

                    state.points = ui.intSlider('points', state.points, 3, 40, { label: 'Samples' });
                    ui.caption(state.points + ' samples');

                    state.thickness = ui.slider('thickness', state.thickness, 1, 12, { step: 0.5 });
                    ui.caption('stroke ' + state.thickness.toFixed(1) + ' px');

                    state.tint = ui.dropdown('tint', state.tint, TINTS, { label: 'Tint' });
                    state.custom = ui.colorField('custom', state.custom);

                    state.filled = ui.switch('filled', state.filled, { label: 'Filled' });
                    state.dark = ui.checkbox('dark', state.dark, { label: 'Dark canvas' });

                    if (ui.button('Randomise', { style: 'accent' })) state.seed = (state.seed * 31 + 17) % 977;

                    ui.flexSpace();

                    // A drawing in the CONTROL pane, to make the point that the two are not separate worlds: a
                    // swatch strip, over the theme tokens, at whatever width this column ended up.
                    ui.canvas('swatches', { height: 34 }, (box) => {
                        const each = Math.max(8, (box.width - 12) / TINTS.length);
                        for (let i = 0; i < TINTS.length; i++)
                            ui.rect([6 + i * each, 6, each - 8, 22], { color: TINTS[i], radius: 5 });
                    });

                    ui.caption('swatch strip: one ui.canvas, four ui.rect');
                });
            },

            // ---- the right pane: one canvas, optionally under a dark theme ---------------------------------
            () => {
                ui.theme(state.dark ? 'dark' : 'light', () => {
                    ui.column({ padding: 12, gap: 8, grow: 1 }, () => {

                        // `grow: 1` rather than a declared height, so this exercises the stale-box path on
                        // purpose: the size comes back through the result table one frame late, the ORIGIN is
                        // always exact, and the caption below says which it got.
                        const box = ui.canvas('plate', { grow: 1, minHeight: 240 }, (plate) => {
                            if (plate.width > 0 && plate.height > 0) TABS[state.tab](plate);
                        });

                        ui.caption('canvas ' + Math.round(box.width) + ' x ' + Math.round(box.height) +
                            (box.stale ? ' - measured last frame' : ' - declared, exact'));
                    });
                });
            });
    });
});

// The state, for a headless driver, on the same terms app.js publishes its own: behind ?debug=1, because a page
// that always publishes its internals to the global scope has decided its internals are an API.
if (new URLSearchParams(location.search).get('debug') === '1') {
    window.nowuiDraw = { state: () => JSON.parse(JSON.stringify(state)) };
}
