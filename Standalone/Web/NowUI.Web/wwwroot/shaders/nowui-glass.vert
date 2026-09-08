#version 300 es
// ===========================================================================
// nowui-glass.vert
//
// GLSL ES 3.00 port of the `vert` function of
// Assets/NowUI/Assets/Shaders/UIGlass.shader (shader "NowUI/UI Glass",
// 267 lines, single pass, render queue 3000). HLSL line references below are
// into that file.
//
// COMPOSITION: GLSL has no #include. The line
//     //#include "nowui-colorspace.glsl"
// is a marker the loader in nowui-glsl-include.js replaces with that file's
// text before calling gl.shaderSource. See that file for the contract.
//
// PRECISION: highp everywhere, for the same reason as every other NowUI port
// -- the fragment stage's `length(vec2(dFdx(d), dFdy(d)))` is a difference of
// two nearly equal numbers and mediump turns the AA band into noise.
//
// TWO THINGS FROM THE HLSL VERTEX STAGE ARE DELIBERATELY ABSENT. Both are
// consequences of the fragment stage's unported branches, and both are
// documented at their use site in nowui-glass.frag:
//
//   1. COMPUTE_EYEDEPTH(o.eyeDepth) (HLSL :133) and the `eyeDepth` varying
//      (:75). Its ONLY consumer is the scene-depth branch, which lives behind
//      `#if defined(NOWUI_GLASS_SCENE_DEPTH)`. That keyword is enabled from
//      exactly one place, NowWorldGraphic.cs:1543, and NowWorldGraphic.cs is
//      on NowUI.Runtime.csproj's exclude list -- so the standalone build only
//      ever compiles the variant WITHOUT the keyword. COMPUTE_EYEDEPTH also
//      expands to -UnityObjectToViewPos(v.vertex).z, which needs UNITY_MATRIX_MV,
//      a matrix the backend does not upload (it uploads one combined MVP).
//      Carrying a varying nothing reads, computed from a matrix nothing sends,
//      would be worse than not carrying it.
//
//   2. `uv` / TEXCOORD0. UIGlass declares it in appdata (:53) and never reads
//      it: there is no _MainTex on this shader and therefore no TRANSFORM_TEX.
//      The attribute is still DECLARED below, because the backend enables all
//      nine for every mesh from one fixed table; an `in` that is never read
//      costs nothing and the compiler removes it.
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-colorspace.glsl"

// ---------------------------------------------------------------------------
// Vertex attributes. The SAME nine locations as every other NowUI program, so
// one VAO layout serves all of them (M2-ShaderPort.md section 1.4).
//
// UIGlass.shader:50-62 (struct appdata) declares all nine. The instancing
// macro on :61 is dropped: multi_compile_instancing is Unity variant
// bookkeeping and no NowUI call site in scope enables it.
//
// The per-vertex meanings that differ from UIRectangle are all in `extras`
// (TEXCOORD5), written by NowGlass.cs:310-321:
//   .x = blur radius in UI units -- CPU-side only. It selects the blur plan
//        and the batch key; this shader never reads it.
//   .y = outline width in local units (same meaning as UIRectangle).
//   .z = saturation, .w = brightness -- applied to the BACKDROP only.
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
// Uniforms. UIGlass's vertex stage needs exactly one: the MVP.
//
// nowui_MatrixMVP replaces UnityObjectToClipPos. The backend uploads
// projection * view * model with transpose = false; no flip anywhere
// (M2-ShaderPort.md sections 8.2 and 8.4).
// ---------------------------------------------------------------------------
uniform highp mat4 nowui_MatrixMVP;

// ---------------------------------------------------------------------------
// Varyings. UIGlass.shader:64-77 (struct v2f), minus `eyeDepth` (see the
// header). nowui-glass.frag declares the identical set as `in`.
// ---------------------------------------------------------------------------
out highp vec4 vScreenPos;
out highp vec4 vRect;
out highp vec4 vRadius;
out highp vec4 vColor;
out highp vec4 vOutlineColor;
out highp vec4 vExtras;
out highp vec4 vMask;
out highp vec4 vRawUV;

void main()
{
    // UIGlass.shader:124 -- UnityObjectToClipPos(v.vertex).
    highp vec4 clipPos = nowui_MatrixMVP * vec4(aPosition, 1.0);
    gl_Position = clipPos;

    // UIGlass.shader:125 -- ComputeScreenPos(o.vertex).
    //
    // UnityCG's ComputeNonStereoScreenPos is
    //     o    = pos * 0.5;
    //     o.xy = float2(o.x, o.y * _ProjectionParams.x) + o.w;   // o.w == 0.5*pos.w
    //     o.zw = pos.zw;                                          // NOT halved
    // so o.xy = 0.5*pos.xy + 0.5*pos.w and screenUV = o.xy / o.w = 0.5*ndc + 0.5,
    // i.e. the ordinary [0,1] screen coordinate with y increasing UPWARD from
    // the bottom-left. That matches gl_FragCoord and matches the backdrop
    // texture's own bottom-up storage, so nothing is flipped here or later.
    //
    // _ProjectionParams.x is HARD-CODED to +1 rather than uploaded, and that
    // is a claim worth stating rather than hiding: Unity sets it to -1 only
    // when rendering with a FLIPPED projection, which happens on the D3D-style
    // platforms whose render textures have a top-left origin. WebGL2 is
    // OpenGL: the default drawing buffer and an FBO colour attachment share
    // one bottom-left origin, so the projection is never flipped and the sign
    // is never -1. If a later slice ever finds it needs -1 here, something
    // else has gone wrong (M2-ShaderPort.md section 8.4).
    vScreenPos = vec4(clipPos.xy * 0.5 + clipPos.w * 0.5, clipPos.zw);

    // UIGlass.shader:126-127 -- straight pass-through.
    vRect = aRect;
    vRadius = aRadius;

    // UIGlass.shader:128-129 -- the only transformation the vertex stage
    // applies. Identity under Gamma; kept so the linear branch is one #define
    // away. Alpha is never converted, in either branch.
    vColor = NowUIColorToWorkingSpace(aColor);
    vOutlineColor = NowUIColorToWorkingSpace(aOutline);

    // UIGlass.shader:130-132 -- pass-through.
    vExtras = aExtras;
    vMask = aMask;
    vRawUV = aRawUV;
}
