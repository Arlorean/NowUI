#version 300 es
// ===========================================================================
// nowui-text.vert
//
// GLSL ES 3.00 port of the `vert` function of
// Assets/NowUI/Assets/Shaders/TxtRenderer.shader (shader "NowUI/Text Renderer",
// 173 lines, single pass, render queue 3000). HLSL line references are into
// that file.
//
// This is byte-for-byte the same computation as nowui-rectangle.vert -- the
// two `vert` functions in the two .shader files are identical, and the only
// difference is what the FRAGMENT stages read the varyings as (radius carries
// a gradient payload here, extras carries different scalars). It is kept as a
// separate file rather than shared so that each program is one .vert + one
// .frag with no special cases in the loader, and so that a future divergence
// does not have to be untangled first.
//
// COMPOSITION: `//#include "..."` lines are substituted by
// nowui-glsl-include.js before gl.shaderSource. See the marker syntax note in
// nowui-rectangle.vert.
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-colorspace.glsl"

// ---------------------------------------------------------------------------
// Vertex attributes. SAME LOCATIONS as nowui-rectangle.vert, on purpose: one
// VAO layout then serves both programs (M2-ShaderPort.md section 1.4).
// TxtRenderer.shader:43-55 (struct appdata); the instancing macro on :54 is
// dropped as no-op variant bookkeeping.
//
// Three attributes mean something different here (M2-ShaderPort.md 4.1):
//   aRect    glyph quad (x,y,w,h) in mesh space. Used ONLY to rebuild
//            uiPosition -- there is no shape SDF in the text shader, so
//            rect.zw is never used as a `size`.
//   aRadius  NOT corner radii. .xyz is the first three components of the
//            gradient payload; unused when extras.w == 0.
//   aExtras  .x outline width (local units, signed)
//            .y distance-field range in local units; NEGATIVE signals the
//              outline-only pass
//            .z gradient payload .w
//            .w encoded gradient ramp; 0 means no gradient
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

// nowui_MatrixMVP = projection * view * model, uploaded with transpose=false.
// _MainTex_ST is TRANSFORM_TEX's (scaleX, scaleY, offsetX, offsetY); NowUI
// never writes it, so the backend must fall back to (1, 1, 0, 0).
// TxtRenderer.shader:71-73 declares _MainTex, _MainTex_ST and
// _NowUITextSdfEncoding; the first and last belong to the fragment stage.
uniform highp mat4 nowui_MatrixMVP;
uniform highp vec4 _MainTex_ST;

// Varyings -- TxtRenderer.shader:57-69 (struct v2f). nowui-text.frag declares
// the identical set as `in`.
out highp vec2 vUv;
out highp vec4 vRect;
out highp vec4 vRadius;
out highp vec4 vColor;
out highp vec4 vOutlineColor;
out highp vec4 vExtras;
out highp vec4 vMask;
out highp vec4 vRawUV;

void main()
{
    // TxtRenderer.shader:80 -- UnityObjectToClipPos(v.vertex).
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);

    // TxtRenderer.shader:81 -- TRANSFORM_TEX(v.uv, _MainTex). For text, aUv is
    // the glyph's sub-rect in the font atlas, in Unity's bottom-up convention.
    // Do not flip it and do not flip the atlas upload.
    vUv = aUv * _MainTex_ST.xy + _MainTex_ST.zw;

    // TxtRenderer.shader:82-83. The vertex stage does not care that vRadius is
    // a gradient payload rather than radii; it is a straight pass-through.
    vRect = aRect;
    vRadius = aRadius;

    // TxtRenderer.shader:84-85 -- identity under Gamma, alpha untouched.
    vColor = NowUIColorToWorkingSpace(aColor);
    vOutlineColor = NowUIColorToWorkingSpace(aOutline);

    // TxtRenderer.shader:86-88
    vExtras = aExtras;
    vMask = aMask;
    vRawUV = aRawUV;
}
