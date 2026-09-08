// ===========================================================================
// nowui-glsl-include.js
//
// GLSL has no #include, so the NowUI WebGL2 shaders are composed by STRING
// CONCATENATION AT LOAD TIME, immediately before gl.shaderSource. This file is
// the canonical description of that composition, and a working implementation
// of it. The host is free to reimplement it -- the contract, not this code, is
// what the .vert/.frag files depend on.
//
// THE CONTRACT
//
// 1. A line whose entire content (after optional leading whitespace) is
//        //#include "name.glsl"
//    is replaced by the full text of that file. Nothing else on the line.
//    Because the marker is a `//` comment, an unresolved .frag is still valid
//    GLSL that an editor or linter can parse -- it is merely incomplete.
//
// 2. Includes are resolved recursively and AT MOST ONCE per compiled shader.
//    A second marker for an already-included file becomes a comment recording
//    the skip. The .glsl files also carry #ifndef/#define guards, so a loader
//    that includes twice anyway still compiles.
//
// 3. Include fragments (*.glsl) carry NO `#version` line. Only the .vert/.frag
//    entry files do, and it is their first line -- as GLSL ES requires, since
//    only comments and whitespace may precede it. Never splice an include
//    before the #version line.
//
// 4. Extra `#define`s are injected AFTER the #version line and BEFORE
//    everything else, one per line. One exists today:
//        NOWUI_COLORSPACE_LINEAR    switch NowUIColorToWorkingSpace to the
//                                   linear branch (do NOT set it: this build
//                                   is Gamma)
//
//    NOWUI_TEXT_GRADIENT_MARKER was the second, and it is gone because what it
//    guarded is gone: the text shader's gradient branch is ported, so there is
//    no unported path left to paint magenta. Its replacement is a C#-side
//    report of the one thing that can still flatten a gradient — the ramp
//    atlas never reaching the backend (WebGL2Backend.WarnOnMissingTextGradientRamp).
//
//    nowui-glass.frag marks ITS two unported branches unconditionally rather
//    than behind a third define, and the reason is worth knowing before adding
//    a fourth: a marker inside `#ifdef` makes the uniforms it tests write-only
//    in the default build, so the compiler eliminates them and
//    getUniformLocation returns null. An assertion that vanishes in exactly
//    the configuration it guards is worse than none. Prefer an unconditional
//    marker whenever the branch is genuinely unreachable and therefore free.
//
// 5. compose() returns { source, map }. `map` is a 0-based array with one
//    entry per line of `source`, each { file, line } naming where that line
//    came from. Compile errors from WebGL name a line in the CONCATENATED
//    source; translateLog() turns those back into file:line so a reviewer is
//    not counting lines by hand. (A `#line` directive would be the other way
//    to do this, but GLSL ES 3.00's `#line` renumbers only within one source
//    string and would make the include's own lines unreportable.)
//
// THE FILES
//
//   nowui-rectangle.vert  includes nowui-colorspace.glsl
//   nowui-rectangle.frag  includes nowui-mask.glsl
//   nowui-text.vert       includes nowui-colorspace.glsl
//   nowui-text.frag       includes nowui-colorspace.glsl, nowui-text-gradient.glsl
//                                  AND nowui-mask.glsl, in that order
//   nowui-gradient.vert   includes nowui-colorspace.glsl
//   nowui-gradient.frag   includes nowui-colorspace.glsl AND nowui-mask.glsl
//   nowui-ripple.vert     includes nowui-colorspace.glsl
//   nowui-ripple.frag     includes nowui-mask.glsl
//   nowui-glass.vert      includes nowui-colorspace.glsl
//   nowui-glass.frag      includes nowui-mask.glsl
//   nowui-colorpicker.vert includes NOTHING (see below)
//   nowui-colorpicker.frag includes nowui-colorspace.glsl AND nowui-mask.glsl
//   nowui-bezier.vert     includes nowui-colorspace.glsl
//   nowui-bezier.frag     includes nowui-mask.glsl
//   nowui-glassblur.vert  includes NOTHING
//   nowui-glassblur.frag  includes NOTHING
//   nowui-sdf.vert        includes nowui-colorspace.glsl
//   nowui-sdf.frag        includes nowui-mask.glsl
//
// nowui-colorpicker.vert is the only vertex stage with no include, and that is
// load-bearing rather than incidental: UIColorPicker's vertex stage passes
// TEXCOORD3 through WITHOUT NowUIColorToWorkingSpace, because on that shader
// TEXCOORD3 is a PARAMETER (a hue angle, or a colour the checker composite
// treats as data) rather than a colour. The conversion happens once on the
// final rgb in the fragment stage instead. Not naming the include here is what
// stops a future edit from "restoring" a call that would be the identity under
// Gamma and a hue shift under linear.
//
// The two nowui-glassblur stages include nothing because that program has no
// colours to convert and no mask -- it moves image data.
//
// nowui-gradient.frag and nowui-text.frag are the two fragment stages that
// need the colour space include, for the same reason: each converts a ramp
// texel that did not exist at vertex time (UIGradient.shader:212-215;
// NowUITextGradient.cginc:69). Including it there is safe --
// nowui-colorspace.glsl uses no derivatives and no samplers -- and the
// include-at-most-once rule means the mask include that follows it is
// unaffected.
//
// nowui-text-gradient.glsl is FRAGMENT-ONLY (it samples _NowGradientRampTexture)
// and must follow nowui-colorspace.glsl, whose NowUIColorToWorkingSpace it
// calls. It is included by nowui-text.frag alone; NowUI/UI Gradient reaches the
// same 256x256 atlas through its own _MainTex and its own sampleRamp, so the
// two ramp readers deliberately share no code.
//
// nowui-mask.glsl is FRAGMENT-ONLY (fwidth, texture with implicit
// derivatives). Do not include it from a vertex shader.
// ===========================================================================

const INCLUDE_PATTERN = /^[ \t]*\/\/#include[ \t]+"([^"]+)"[ \t]*$/;

/**
 * Splice `//#include "..."` markers, injecting optional defines after #version.
 *
 * @param {string} entryName   name of the entry file, for the source map
 * @param {Object<string,string>} files  name -> text, for every file that may
 *                                       be included (and the entry itself)
 * @param {{defines?: string[]}} [options]
 * @returns {{source: string, map: {file: string, line: number}[]}}
 */
export function compose(entryName, files, options = {}) {
    const defines = options.defines || [];
    const included = new Set();
    const outLines = [];
    const map = [];

    const emit = (text, file, line) => {
        outLines.push(text);
        map.push({ file, line });
    };

    const read = (name) => {
        const text = files[name];
        if (text === undefined) {
            throw new Error(`nowui-glsl-include: no such shader file "${name}"`);
        }
        return text.split('\n');
    };

    // Emit `lines` from `name`, starting at index `from`, splicing includes.
    const expandLines = (lines, name, from) => {
        for (let i = from; i < lines.length; i++) {
            const match = INCLUDE_PATTERN.exec(lines[i]);
            if (!match) {
                emit(lines[i], name, i + 1);
                continue;
            }
            const target = match[1];
            if (included.has(target)) {
                emit(`// (already included: ${target})`, name, i + 1);
                continue;
            }
            included.add(target);
            emit(`// >>> begin ${target} (included from ${name}:${i + 1})`, name, i + 1);
            expandLines(read(target), target, 0);
            emit(`// <<< end ${target}`, name, i + 1);
        }
    };

    // The #version line must stay first, so peel it off, inject the defines,
    // then expand the remainder.
    const entryLines = read(entryName);
    if (!/^\s*#version\b/.test(entryLines[0])) {
        throw new Error(
            `nowui-glsl-include: "${entryName}" must open with a #version line`);
    }
    emit(entryLines[0], entryName, 1);
    for (const define of defines) {
        emit(`#define ${define}`, '<defines>', 0);
    }

    included.add(entryName);
    expandLines(entryLines, entryName, 1);

    return { source: outLines.join('\n'), map };
}

/**
 * Rewrite the "ERROR: 0:123:" line references in a gl.getShaderInfoLog() string
 * into "file:line" against the original sources.
 */
export function translateLog(log, map) {
    return String(log).replace(/(\d+):(\d+)/g, (whole, str, line) => {
        const entry = map[Number(line) - 1];
        return entry ? `${entry.file}:${entry.line} (composed ${whole})` : whole;
    });
}

/**
 * Fetch every shader file this slice needs, relative to `baseUrl`.
 * Returns the name -> text table `compose` expects.
 */
export async function fetchShaderFiles(baseUrl = './shaders/') {
    const names = [
        'nowui-colorspace.glsl',
        'nowui-mask.glsl',
        'nowui-text-gradient.glsl',
        'nowui-rectangle.vert',
        'nowui-rectangle.frag',
        'nowui-text.vert',
        'nowui-text.frag',
        'nowui-gradient.vert',
        'nowui-gradient.frag',
        'nowui-ripple.vert',
        'nowui-ripple.frag',
        'nowui-glass.vert',
        'nowui-glass.frag',
        'nowui-colorpicker.vert',
        'nowui-colorpicker.frag',
        'nowui-bezier.vert',
        'nowui-bezier.frag',
        'nowui-glassblur.vert',
        'nowui-glassblur.frag',
        'nowui-sdf.vert',
        'nowui-sdf.frag',
    ];
    const texts = await Promise.all(names.map(async (name) => {
        const response = await fetch(baseUrl + name);
        if (!response.ok) {
            throw new Error(`nowui-glsl-include: fetch ${name} -> ${response.status}`);
        }
        return response.text();
    }));
    const files = {};
    names.forEach((name, i) => { files[name] = texts[i]; });
    return files;
}

/**
 * The ported programs, and the entry files each is built from. The `shader`
 * value is the Unity shader name the backend resolves through Shader.Find and
 * the key nowui-gl.js registers the compiled program under; keep the two in
 * step, because a mismatch shows up only as "shader not ported" at draw time.
 *
 * Slice 1 ported `rectangle` and `text`. `gradient` and `ripple` are the two
 * the interactive demo needs: gradients appear throughout NowUI's controls and
 * themes, and the ripple is what makes a button feel pressed.
 *
 * `glass`, `colorpicker`, `bezier` and `glassblur` complete the CORE set --
 * every non-UGUI, non-SDF shader under Assets/NowUI/Assets/Shaders now has a
 * program here. Three notes that are not obvious from the table:
 *
 *   * `glassblur` is `blitGeometry: true`. It is drawn through nowui-gl.js's
 *     blit() path, not through draw(), so it declares only attributes 0
 *     (aPosition) and 1 (aUv) -- the BLIT GEOMETRY CONTRACT documented above
 *     GLSL_VERTEX_BLIT in that file -- and NOT the nine-stream UI layout. A
 *     checker that asserts the nine-attribute table against every program must
 *     skip this one, and a checker that draws it must bind the blit quad
 *     rather than a UI mesh.
 *
 *   * `glassblur` corresponds to PASS 0 of a four-pass shader. Passes 1-3 are
 *     the texture-array and MSAA-resolve variants; pass 1 is unreachable
 *     (XR-only) and passes 2-3 are impossible on WebGL2 (no sampler2DMS in
 *     GLSL ES 3.00). nowui-glassblur.vert's header carries the full reasoning.
 *
 *   * `glass` is the ONLY program whose blend state differs from the shared
 *     premultiplied one. See BLEND_MODES below.
 */
export const PROGRAMS = {
    rectangle: {
        shader: 'NowUI/UI Rectangle',
        vert: 'nowui-rectangle.vert',
        frag: 'nowui-rectangle.frag',
    },
    text: {
        shader: 'NowUI/Text Renderer',
        vert: 'nowui-text.vert',
        frag: 'nowui-text.frag',
    },
    gradient: {
        shader: 'NowUI/UI Gradient',
        vert: 'nowui-gradient.vert',
        frag: 'nowui-gradient.frag',
    },
    ripple: {
        shader: 'NowUI/UI Ripple',
        vert: 'nowui-ripple.vert',
        frag: 'nowui-ripple.frag',
    },
    glass: {
        shader: 'NowUI/UI Glass',
        vert: 'nowui-glass.vert',
        frag: 'nowui-glass.frag',
    },
    colorpicker: {
        shader: 'NowUI/Color Picker',
        vert: 'nowui-colorpicker.vert',
        frag: 'nowui-colorpicker.frag',
    },
    bezier: {
        shader: 'NowUI/UI Bezier',
        vert: 'nowui-bezier.vert',
        frag: 'nowui-bezier.frag',
    },
    // The shape-algebra program, and the only INTERPRETER among these: its fragment stage walks nine uniform
    // vec4 arrays holding up to 64 shape nodes and 16 layers. Its embedded copy in nowui-gl.js is GENERATED from
    // these two files by tools/embed-shader.py rather than hand-copied -- at 1500 lines a hand copy is a
    // divergence waiting to happen.
    sdf: {
        shader: 'NowUI/SDF Scene',
        vert: 'nowui-sdf.vert',
        frag: 'nowui-sdf.frag',
    },
    glassblur: {
        shader: 'Hidden/NowUI/GlassBlur',
        vert: 'nowui-glassblur.vert',
        frag: 'nowui-glassblur.frag',
        blitGeometry: true,
        // PASS 0 of four. nowui-gl.js declares a `passes` array of length one,
        // so selectPass(name, 1..3) fails loudly with the pass count in the
        // message rather than quietly drawing pass 0 instead.
        passes: 1,
    },
};

/**
 * Per-program blend state, as each shader's own SubShader block declares it.
 * A program absent from this table uses the shared premultiplied source-over
 * that nowui-gl.js sets once at init:
 *     gl.enable(BLEND); blendEquation(FUNC_ADD);
 *     gl.blendFunc(ONE, ONE_MINUS_SRC_ALPHA)
 *
 * Two programs are NOT absent, and both would fail quietly rather than loudly
 * if their state were left at the shared default:
 *
 *   'NowUI/UI Glass'          UIGlass.shader:35
 *       Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
 *     Separate colour and alpha functions, and the colour half is the
 *     NON-premultiplied source-over -- because this is the one NowUI fragment
 *     stage that divides its accumulated colour back out by coverage
 *     (UIGlass.shader:257) to keep the tint and the outline separable over an
 *     opaque backdrop. Drawing it with the shared premultiplied blend darkens
 *     the panel by exactly a factor of its own alpha: still a plausible
 *     translucent panel, just the wrong one.
 *
 *   'Hidden/NowUI/GlassBlur'  UIGlassBlur.shader:7-9
 *       (no Blend statement at all)
 *     A pass with no Blend line has blending DISABLED and replaces the
 *     destination. The blur ping-pongs between two targets across iterations
 *     (NowGlassRenderer.cs:238-250), so leaving blending on would accumulate
 *     each pass over the last -- a blur that brightens with radius.
 *
 * `blend: null` means "disable BLEND for this program". A `{ src, dst }` pair
 * means blendFunc; a `{ srcRGB, dstRGB, srcAlpha, dstAlpha }` quadruple means
 * blendFuncSeparate. Values are the GL enum NAMES, so this table stays a plain
 * data file with no dependency on a live context.
 */
export const BLEND_MODES = {
    // NowSdf.shader:58  Blend SrcAlpha OneMinusSrcAlpha
    //   The SDF fragment stage composites its shadow, glow, outline, fill, inner shadow and contour layers with
    //   a STRAIGHT-alpha source-over (NowSdfShaderV2.cginc:1447) and returns the result unpremultiplied
    //   (:1721). Under the shared premultiplied blend every soft edge has its coverage applied twice: the scene
    //   keeps its silhouette and merely darkens, which reads as a colour choice rather than as a bug. Unity's
    //   statement applies one function to colour and alpha alike, so this is blendFunc spelled through the
    //   separate form.
    'NowUI/SDF Scene': {
        srcRGB: 'SRC_ALPHA', dstRGB: 'ONE_MINUS_SRC_ALPHA',
        srcAlpha: 'SRC_ALPHA', dstAlpha: 'ONE_MINUS_SRC_ALPHA',
    },
    'NowUI/UI Glass': {
        srcRGB: 'SRC_ALPHA', dstRGB: 'ONE_MINUS_SRC_ALPHA',
        srcAlpha: 'ONE', dstAlpha: 'ONE_MINUS_SRC_ALPHA',
    },
    'Hidden/NowUI/GlassBlur': null,
};

/**
 * Attribute locations, fixed so ONE VAO layout serves both programs. These
 * match `layout(location = N)` in both .vert files; they are exported so the
 * backend can build its vertexAttribPointer table from one source of truth
 * rather than a second hand-written copy.
 * The `stream` value is the NowUI VertexAttribute enum value whose
 * NowMeshData stream feeds the attribute; `size` is elementSize / 4.
 */
export const ATTRIBUTES = [
    { location: 0, name: 'aPosition', stream: 0,  size: 3 },
    { location: 1, name: 'aUv',       stream: 4,  size: 2 },
    { location: 2, name: 'aRect',     stream: 5,  size: 4 },
    { location: 3, name: 'aRadius',   stream: 6,  size: 4 },
    { location: 4, name: 'aColor',    stream: 7,  size: 4 },
    { location: 5, name: 'aOutline',  stream: 8,  size: 4 },
    { location: 6, name: 'aExtras',   stream: 9,  size: 4 },
    { location: 7, name: 'aMask',     stream: 10, size: 4 },
    { location: 8, name: 'aRawUV',    stream: 11, size: 4 },
];

/**
 * Fixed sampler units (M2-ShaderPort.md section 7.4). Unit 3 is reserved for
 * _NowGradientRampTexture and is unused while the text gradient branch is
 * not ported.
 *
 * Unit 4 is _NowBackdropTex, UIGlass's blurred backdrop. It must be bound to
 * SOMETHING complete on every glass draw -- the 1x1 opaque black fallback,
 * matching UIGlass.shader:11's "black" default and NowMaskShader's own habit --
 * even though _NowGlassUseBackdrop is 0 on every path the browser host can
 * reach today. WebGL2 invalidates a draw whose sampler points at an incomplete
 * texture regardless of whether dynamic control flow reaches the sample.
 *
 * _NowBlurSourceTex takes unit 0. That is deliberate reuse rather than an
 * oversight: 'Hidden/NowUI/GlassBlur' is drawn on its own, never in the same
 * draw as a UI program, and it has exactly one sampler.
 */
export const SAMPLER_UNITS = {
    _MainTex: 0,
    _NowBlurSourceTex: 0,
    _NowUITextureMask0: 1,
    _NowUITextureMask1: 2,
    _NowBackdropTex: 4,
    // NowUI/SDF Scene's own pair. Bound on every SDF draw -- to the scene's atlases when it has them and to the
    // 1x1 opaque black fallback when it does not, which is every scene this host can build today because
    // 'Hidden/NowUI/SDF Image Field' is not ported.
    _SdfImageField: 5,
    _SdfImageColor: 6,
};

/**
 * Uniforms each program declares beyond the shared mask set, for the backend's
 * benefit and for the checker's. `NowUI/UI Ripple` declares none of its own --
 * it has no sampler and no _MainTex at all -- which is why its .vert omits
 * _MainTex_ST as well.
 */
export const PROGRAM_UNIFORMS = {
    'NowUI/UI Rectangle': ['nowui_MatrixMVP', '_MainTex_ST', '_MainTex', '_NowPremultipliedTexture'],
    'NowUI/Text Renderer': ['nowui_MatrixMVP', '_MainTex_ST', '_MainTex', '_NowUITextSdfEncoding'],
    'NowUI/UI Gradient': ['nowui_MatrixMVP', '_MainTex', '_NowGradientRampTexelSize'],
    'NowUI/UI Ripple': ['nowui_MatrixMVP'],
    // _NowMaterialGlassMode and _NowGlassUseStereoBackdrop are declared and
    // TESTED but never satisfied -- both are constants (0) in this build, and
    // both exist so that a non-zero value trips the NOWUI_GLASS_UNPORTED_MARKER
    // branch instead of silently selecting an unported half. See
    // nowui-glass.frag's header.
    'NowUI/UI Glass': [
        'nowui_MatrixMVP', '_NowBackdropTex', '_NowGlassUseBackdrop',
        '_NowGlassUseStereoBackdrop', '_NowBackdropUVTransform', '_NowMaterialGlassMode',
    ],
    // _Mode is the whole shader. GL's zero default is a VALID mode
    // (SaturationValue), so an unbridged _Mode renders all three pickers as
    // three plausible squares -- the one uniform here whose absence is
    // invisible.
    'NowUI/Color Picker': ['nowui_MatrixMVP', '_Mode'],
    // Nothing of its own beyond the MVP: no sampler, no _MainTex, no scalar.
    // Every uniform it reads comes from the shared mask block.
    'NowUI/UI Bezier': ['nowui_MatrixMVP'],
    // The SDF program's uniforms do NOT travel in the shared per-draw block: the nine arrays alone are 480
    // vec4, so appending them would make every rectangle draw in the app marshal ~8 KB it does not use. They go
    // through setSdfUniforms(), a separate call the backend makes immediately before an SDF draw and at no other
    // time. The capacities are structural -- 64 shapes and 16 layers, from NowSdf.MaxShapes / NowSdf.MaxLayers
    // -- and must never be sized from _SdfShapeCount or _SdfLayerCount.
    'NowUI/SDF Scene': [
        'nowui_MatrixMVP', '_NowCanvasLayout', 'nowui_Time',
        '_MainTex', '_SdfImageField', '_SdfImageColor', '_SdfImageAtlasSize',
        '_SdfShapeCount', '_SdfLayerCount', '_SdfFeather', '_SdfTextEffectLimit', '_SdfMaskOutput',
        '_SdfOutline', '_SdfOutlineColor', '_SdfGlow', '_SdfGlowColor',
        '_SdfShadow', '_SdfShadowColor', '_SdfInnerShadow', '_SdfInnerShadowColor',
        '_SdfEmboss', '_SdfContour', '_SdfContourColor', '_SdfContourMask', '_SdfWarp',
        '_SdfData0', '_SdfData1', '_SdfData2', '_SdfShapeMeta', '_SdfColors', '_SdfUvs', '_SdfImageUvs',
        '_SdfLayerData0', '_SdfLayerData1',
    ],
    // nowui_MatrixMVP and _MainTex_ST come from the BLIT contract, not from the
    // blur's own material: the unit-quad ortho and the blit's scale/offset.
    // Both are identity for every call NowGlassRenderer makes. The four that
    // follow are the shader's own, and ALL FOUR arrive as SHADER GLOBALS
    // (Shader.SetGlobalVector / SetGlobalTexture from NowGlassRenderer), never
    // from a material bag. NONE of them has a safe zero default: zeroing the
    // texel size or the direction turns the blur into a pixel-perfect copy, and
    // zeroing the scale/offset collapses the image to a single texel.
    'Hidden/NowUI/GlassBlur': [
        'nowui_MatrixMVP', '_MainTex_ST',
        '_NowBlurSourceTex', '_NowBlurTexelSize', '_NowBlurSourceScaleOffset', '_NowBlurDirection',
    ],
};

/**
 * Fallbacks for uniforms whose GL default of zero is WRONG rather than merely
 * unset (M2-ShaderPort.md section 7.3 step 5). A backend that leaves any of
 * these at zero produces a plausible picture, which is the failure class this
 * whole port exists to avoid, so they are listed as data rather than left in
 * prose. Uniforms not listed here are safe at zero.
 */
export const UNIFORM_FALLBACKS = {
    // TRANSFORM_TEX identity. Never written by NowUI.
    _MainTex_ST: [1, 1, 0, 0],
    // The Properties-block default and what NowGlassRenderer.DisableBackdropGlobal
    // writes (NowGlassRenderer.cs:632). Zero collapses the backdrop to texel (0,0).
    _NowBackdropUVTransform: [1, 1, 0, 0],
    // GradientMaterial.mat's 256x256 atlas. Zero makes every gradient sample row 0.
    _NowGradientRampTexelSize: [1 / 256, 1 / 256, 256, 256],
    // BlitBlur's own two-argument overload passes this (NowGlassRenderer.cs:826).
    _NowBlurSourceScaleOffset: [1, 1, 0, 0],
};
