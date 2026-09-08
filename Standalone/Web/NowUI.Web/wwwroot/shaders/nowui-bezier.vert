#version 300 es
// ===========================================================================
// nowui-bezier.vert
//
// GLSL ES 3.00 port of the `vert` function of
// Assets/NowUI/Assets/Shaders/UIBezier.shader (shader "NowUI/UI Bezier",
// 138 lines, single pass, render queue 3000). HLSL line references below are
// into that file.
//
// COMPOSITION: GLSL has no #include. The line
//     //#include "nowui-colorspace.glsl"
// is a marker the loader in nowui-glsl-include.js replaces with that file's
// text before calling gl.shaderSource. See that file for the contract.
//
// ---------------------------------------------------------------------------
// THIS PROGRAM REUSES THE NINE STREAMS FOR COMPLETELY DIFFERENT DATA.
// ---------------------------------------------------------------------------
// UIBezier is the one NowUI program whose attributes are not "a rectangle plus
// styling". NowLine.AddBezierVertex (NowLine.cs:530-551) writes each of the
// four vertices of each flattened segment quad as:
//
//   POSITION  (uiPos.x, -uiPos.y, 0)   the stroke outline, in negated-y mesh
//                                      space like everything else
//   TEXCOORD0 (0, 0)                   UNUSED. Written as Vector2.zero.
//   TEXCOORD1 (p0.x, p0.y, p1.x, p1.y) cp01 -- the cubic's first two control
//                                      points, in UI space
//   TEXCOORD2 (p2.x, p2.y, p3.x, p3.y) cp23 -- the last two. NOT corner radii.
//   TEXCOORD3 rgba                     the stroke colour, lerped along t when
//                                      the caller gave two ends
//   TEXCOORD4 (0,0,0,0)                UNUSED. Written as Vector4.zero, and the
//                                      HLSL appdata even names it `unused1`.
//   TEXCOORD5 (halfWidth, aaWidth, t, 0)  params -- half the stroke width and
//                                      the AA band width, both in UI units, and
//                                      the flattening parameter t for this
//                                      vertex, which is the Newton solver's
//                                      initial guess
//   TEXCOORD6 (x, y, w, h)             the legacy clip rect, UI space
//   TEXCOORD7 (uiPos.x, uiPos.y, 0, 0) `pixel` -- the SAME point as POSITION
//                                      but WITHOUT the y negation, i.e. already
//                                      in UI coordinates
//
// That last one is the one to hold on to. Every other NowUI fragment stage
// reconstructs UI space with `uiPosition = vec2(pos.x, -pos.y)`. This one does
// NOT, because the vertex data hands it UI space directly -- and everything
// the fragment stage compares against (the control points, the mask rect) is
// in UI space too. Adding the usual negation here would mirror every curve
// about y = 0, off screen. See nowui-bezier.frag.
//
// PRECISION: highp everywhere. The vertex language already defaults float and
// int to highp, but say it; the fragment stage's Newton iteration genuinely
// needs it (see that file's header).
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-colorspace.glsl"

// ---------------------------------------------------------------------------
// Vertex attributes. The SAME nine locations as every other NowUI program, so
// one VAO layout serves all of them (M2-ShaderPort.md section 1.4). The names
// below keep the GENERIC stream names rather than UIBezier's meanings, so that
// this block stays diffable line-for-line against the other four .vert files;
// the meanings are given in the header and again at each use in main().
//
// UIBezier.shader:39-51 (struct appdata). The instancing macro on :50 is
// dropped: multi_compile_instancing is Unity variant bookkeeping and no NowUI
// call site in scope enables it.
// ---------------------------------------------------------------------------
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;      // cp01 : p0.xy, p1.xy
layout(location = 3) in vec4 aRadius;    // cp23 : p2.xy, p3.xy
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;   // unused1 -- always (0,0,0,0)
layout(location = 6) in vec4 aExtras;    // params : halfWidth, aaWidth, t, 0
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;     // pixel : UI-space position in .xy

// ---------------------------------------------------------------------------
// Uniforms. UIBezier's vertex stage needs exactly one: the MVP.
//
// nowui_MatrixMVP replaces UnityObjectToClipPos. The backend uploads
// projection * view * model with transpose = false; no flip anywhere
// (M2-ShaderPort.md sections 8.2 and 8.4).
//
// There is no _MainTex on this shader and therefore no _MainTex_ST and no
// TRANSFORM_TEX -- the same structural difference UIRipple and UIColorPicker
// have. BezierMaterial.mat confirms it: its only properties are the four mask
// ones and _ZTest.
// ---------------------------------------------------------------------------
uniform highp mat4 nowui_MatrixMVP;

// ---------------------------------------------------------------------------
// Varyings. UIBezier.shader:53-63 (struct v2f) -- six, and note that `pixel`
// is narrowed to a float2 by the v2f (:61) even though the attribute is a
// float4. nowui-bezier.frag declares the identical set as `in`.
// ---------------------------------------------------------------------------
out highp vec4 vCp01;
out highp vec4 vCp23;
out highp vec4 vColor;
out highp vec4 vParams;
out highp vec4 vMask;
out highp vec2 vPixel;

void main()
{
    // UIBezier.shader:70 -- UnityObjectToClipPos(v.vertex).
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);

    // UIBezier.shader:71-72 -- the four control points, straight through.
    vCp01 = aRect;
    vCp23 = aRadius;

    // UIBezier.shader:73 -- the only transformation the vertex stage applies.
    // Identity under Gamma; kept so the linear branch is one #define away.
    // Alpha is never converted, in either branch. TEXCOORD3 really is a colour
    // on this shader (unlike UIColorPicker, where it is a parameter), so the
    // conversion belongs here.
    vColor = NowUIColorToWorkingSpace(aColor);

    // UIBezier.shader:74-76 -- pass-through. :76 takes `.xy` explicitly, which
    // is why vPixel is a vec2.
    vParams = aExtras;
    vMask = aMask;
    vPixel = aRawUV.xy;
}
