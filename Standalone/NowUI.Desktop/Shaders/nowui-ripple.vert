#version 300 es
// ===========================================================================
// nowui-ripple.vert
//
// GLSL ES 3.00 port of the `vert` function of
// Assets/NowUI/Assets/Shaders/UIRipple.shader (shader "NowUI/UI Ripple",
// 117 lines, single pass, render queue 3000). HLSL line references below are
// into that file.
//
// COMPOSITION: GLSL has no #include. The line
//     //#include "nowui-colorspace.glsl"
// is a marker the loader in nowui-glsl-include.js replaces with that file's
// text before calling gl.shaderSource. See that file for the contract.
//
// PRECISION: highp everywhere, for the same reason as the rectangle port --
// the SDF derivative arithmetic in the fragment stage is a difference of two
// nearly equal numbers and mediump turns the AA band into noise.
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-colorspace.glsl"

// ---------------------------------------------------------------------------
// Vertex attributes. The SAME nine locations as every other NowUI program, so
// one VAO layout serves all of them (M2-ShaderPort.md section 1.4).
//
// UIRipple.shader:41-52 declares only seven of the nine in its `appdata`:
// TEXCOORD4 (outlineColor) is absent, and TEXCOORD0 (uv) is declared but never
// read. All nine are declared here anyway, deliberately:
//   * the backend builds its vertexAttribPointer table from ONE fixed list and
//     enables all nine for every mesh, so a program that declares fewer is not
//     a program that is fed fewer;
//   * an `in` that is never read is removed by the compiler, costs nothing,
//     and keeps this file diffable line-for-line against the other three.
// aUv and aOutline are the two that go unused below. That is not an oversight.
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
// Uniforms. UIRipple declares NO sampler and NO _MainTex, so there is no
// _MainTex_ST here and TRANSFORM_TEX is never called -- the one structural
// difference from nowui-rectangle.vert. The ripple's own material
// (Resources/NowUI/RippleMaterial, shader "NowUI/UI Ripple") lists exactly
// _NowUIMaskCount, _NowUITextureMask{0,1}, _NowUITextureMaskCount and _ZTest,
// which confirms it: no main texture is ever bound to this program.
//
// nowui_MatrixMVP replaces UnityObjectToClipPos. The backend uploads
// projection * view * model with transpose = false; no flip anywhere
// (M2-ShaderPort.md sections 8.2 and 8.4).
// ---------------------------------------------------------------------------
uniform highp mat4 nowui_MatrixMVP;

// ---------------------------------------------------------------------------
// Varyings. UIRipple.shader:54-64 (struct v2f) -- six of them, and note that
// it carries rawUV as a full float4 (unlike UIGradient, which narrows it to
// float2). nowui-ripple.frag declares the identical set as `in`.
// ---------------------------------------------------------------------------
out highp vec4 vRect;
out highp vec4 vRadius;
out highp vec4 vColor;
out highp vec4 vExtras;
out highp vec4 vMask;
out highp vec4 vRawUV;

void main()
{
    // UIRipple.shader:78 -- UnityObjectToClipPos(v.vertex).
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);

    // UIRipple.shader:79-80 -- straight pass-through.
    vRect = aRect;
    vRadius = aRadius;

    // UIRipple.shader:81 -- the only transformation the vertex stage applies.
    // Identity under Gamma; kept so the linear branch is one #define away.
    // Alpha is never converted, in either branch.
    vColor = NowUIColorToWorkingSpace(aColor);

    // UIRipple.shader:82-84 -- pass-through. extras carries the ripple circle:
    // .xy is its centre in UI coordinates and .w its radius, written by
    // NowRipple's AddRect call as Vector4(origin.x, origin.y, 0, circleRadius).
    // .z is unused.
    vExtras = aExtras;
    vMask = aMask;
    vRawUV = aRawUV;
}
