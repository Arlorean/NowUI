#version 300 es
// ===========================================================================
// nowui-colorpicker.vert
//
// GLSL ES 3.00 port of the `vert` function of
// Assets/NowUI/Assets/Shaders/UIColorPicker.shader (shader "NowUI/Color
// Picker", 128 lines, single pass). HLSL line references below are into that
// file. The *UGUI variant is deliberately not ported: the standalone build
// never resolves it.
//
// COMPOSITION: GLSL has no #include. The line
//     //#include "nowui-colorspace.glsl"
// is a marker the loader in nowui-glsl-include.js replaces with that file's
// text before calling gl.shaderSource. See that file for the contract.
//
// ---------------------------------------------------------------------------
// THE ONE TRAP IN THIS FILE: THE VERTEX STAGE DOES NOT CONVERT THE COLOUR.
// ---------------------------------------------------------------------------
// Every other NowUI vertex stage runs NowUIColorToWorkingSpace over TEXCOORD3.
// UIColorPicker.shader:86 does NOT -- it passes v.color through untouched --
// and the difference is not an oversight in the HLSL. On this shader TEXCOORD3
// is a PARAMETER, not a colour:
//   * mode 0 (SaturationValue): .r is the HUE, a 0..1 angle around the wheel.
//   * mode 2 (Alpha):           .rgb is the colour being previewed, which the
//                               checker lerp treats as data and which the
//                               fragment stage converts AFTER compositing.
// Converting it here would be the identity under Gamma -- and silently wrong
// the day a linear slice lands, in a way that would show up as a hue shift
// rather than as an error. The conversion belongs where the HLSL puts it, on
// the FINAL rgb at :120, and nowui-colorpicker.frag does exactly that.
//
// The colorspace include is still spliced in below, because the fragment stage
// needs it and the loader's include-at-most-once rule means naming it here as
// well would be harmless -- but it is NOT named here, precisely so that this
// file cannot grow a call to it by accident. This vertex stage needs no
// include at all.
//
// PRECISION: highp everywhere. The vertex language already defaults float and
// int to highp, but say it, for the same reason the other ports do.
// ===========================================================================

precision highp float;
precision highp int;

// ---------------------------------------------------------------------------
// Vertex attributes. The SAME nine locations as every other NowUI program, so
// one VAO layout serves all of them (M2-ShaderPort.md section 1.4).
//
// UIColorPicker.shader:39-51 (struct appdata) declares all nine and reads
// four: vertex, rect, color, mask, rawUV. uv, radius, outlineColor and extras
// go unread -- there is no texture, no shape SDF and no outline on this
// shader. All nine are declared here anyway, deliberately: the backend enables
// all nine for every mesh from one fixed table, and an `in` that is never read
// is removed by the compiler.
//
// The instancing macro on :50 is dropped: multi_compile_instancing is Unity
// variant bookkeeping and no NowUI call site in scope enables it.
// ---------------------------------------------------------------------------
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;
layout(location = 3) in vec4 aRadius;
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;
layout(location = 6) in vec4 aExtras;
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;

// ---------------------------------------------------------------------------
// Uniforms. The vertex stage needs exactly one: the MVP.
//
// nowui_MatrixMVP replaces UnityObjectToClipPos. The backend uploads
// projection * view * model with transpose = false; no flip anywhere
// (M2-ShaderPort.md sections 8.2 and 8.4).
//
// There is no _MainTex on this shader and therefore no _MainTex_ST and no
// TRANSFORM_TEX -- the same structural difference UIRipple has.
// ---------------------------------------------------------------------------
uniform highp mat4 nowui_MatrixMVP;

// ---------------------------------------------------------------------------
// Varyings. UIColorPicker.shader:53-61 (struct v2f) -- only four, the fewest
// of any NowUI program. nowui-colorpicker.frag declares the identical set as
// `in`.
// ---------------------------------------------------------------------------
out highp vec4 vRect;
out highp vec4 vColor;
out highp vec4 vMask;
out highp vec4 vRawUV;

void main()
{
    // UIColorPicker.shader:84 -- UnityObjectToClipPos(v.vertex).
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);

    // UIColorPicker.shader:85-88 -- four straight pass-throughs. See this
    // file's header for why :86 is NOT NowUIColorToWorkingSpace(v.color).
    vRect = aRect;
    vColor = aColor;
    vMask = aMask;
    vRawUV = aRawUV;
}
