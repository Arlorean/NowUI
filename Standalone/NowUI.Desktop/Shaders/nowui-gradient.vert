#version 300 es
// ===========================================================================
// nowui-gradient.vert
//
// GLSL ES 3.00 port of the `vert` function of
// Assets/NowUI/Assets/Shaders/UIGradient.shader (shader "NowUI/UI Gradient",
// 223 lines, single pass, render queue 3000). HLSL line references below are
// into that file.
//
// COMPOSITION: GLSL has no #include. The line
//     //#include "nowui-colorspace.glsl"
// is a marker the loader in nowui-glsl-include.js replaces with that file's
// text before calling gl.shaderSource. See that file for the contract.
//
// THIS IS *NOT* nowui-rectangle.vert WITH A DIFFERENT NAME. Three of the nine
// channels mean something else in this program, and two of the operations
// nowui-rectangle.vert performs would be actively wrong here:
//
//   1. TEXCOORD0 is `packedTint`, not `uv`. It carries two 16-bit integers
//      (values up to 65535) that the fragment stage unpacks into an RGBA tint,
//      and it is passed through RAW. Running TRANSFORM_TEX over it -- which is
//      exactly what nowui-rectangle.vert does to TEXCOORD0 -- would multiply
//      those integers by _MainTex_ST.xy and add _MainTex_ST.zw. That is the
//      identity today only because NowUI never writes _MainTex_ST; the moment
//      anything did, the tint would silently become garbage. So this program
//      declares no _MainTex_ST at all.
//   2. TEXCOORD3 is the gradient's `parameters` payload (direction, centre,
//      radii, repetitions), NOT a colour. NowUIColorToWorkingSpace must NOT be
//      applied to it. Under Gamma that function is the identity, so getting
//      this wrong is invisible today and wrong the day a linear slice lands --
//      which is the definition of a trap. Only outlineColor is converted here;
//      the ramp and the tint are converted in the FRAGMENT stage instead
//      (UIGradient.shader:212-215), because both are unpacked there.
//   3. rawUV is narrowed to a float2 by the HLSL v2f (UIGradient.shader:62),
//      unlike UIRipple and UIRectangle which carry all four components.
//
// PRECISION: highp, and here it is load-bearing beyond the usual SDF argument.
// decodePair8 in the fragment stage does `floor(packed / 256.0)` on values up
// to 65535 and then subtracts the reconstructed high byte back off. mediump's
// 10-bit mantissa cannot even represent 65535 exactly, so the tint would come
// out visibly wrong rather than subtly wrong.
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-colorspace.glsl"

// ---------------------------------------------------------------------------
// Vertex attributes. The SAME nine locations as every other NowUI program, so
// one VAO layout serves all of them (M2-ShaderPort.md section 1.4). The names
// keep the neutral aN form of the shared layout; the comment column says what
// UIGradient.shader:43-54 calls each one.
//
//   loc  shared name  UIGradient appdata        meaning here
//   ---  -----------  -----------------------   -----------------------------
//    0   aPosition    vertex   : POSITION       quad corner, mesh space
//    1   aUv          packedTint : TEXCOORD0    two packed 16-bit tint pairs
//    2   aRect        rect     : TEXCOORD1      shape rect (x, y, w, h)
//    3   aRadius      radius   : TEXCOORD2      corner radii (TR, BR, TL, BL)
//    4   aColor       gradient : TEXCOORD3      gradient parameters, NOT colour
//    5   aOutline     outlineColor : TEXCOORD4  outline RGBA
//    6   aExtras      extras   : TEXCOORD5      (blur, outline, -, encodedRamp)
//    7   aMask        mask     : TEXCOORD6      legacy clip rect, UI coords
//    8   aRawUV       rawUV    : TEXCOORD7      un-atlased quad coordinate
//
// The TEXCOORD0 stream is a Vector2 (elementSize 8) in every NowUI mesh, which
// is why the shared layout gives location 1 two components and why packedTint
// fits it exactly.
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
// Uniforms. Just the MVP: UIGradient's vertex stage calls no TRANSFORM_TEX, so
// _MainTex_ST is absent by design (see note 1 in the header). _MainTex and
// _NowGradientRampTexelSize are fragment-stage uniforms and are declared there.
//
// The backend uploads projection * view * model with transpose = false; no
// flip anywhere (M2-ShaderPort.md sections 8.2 and 8.4).
// ---------------------------------------------------------------------------
uniform highp mat4 nowui_MatrixMVP;

// ---------------------------------------------------------------------------
// Varyings. UIGradient.shader:56-68 (struct v2f). nowui-gradient.frag declares
// the identical set as `in`, including vRawUV's vec2 narrowing.
// ---------------------------------------------------------------------------
out highp vec2 vPackedTint;
out highp vec4 vRect;
out highp vec4 vRadius;
out highp vec4 vGradient;
out highp vec4 vOutlineColor;
out highp vec4 vExtras;
out highp vec4 vMask;
out highp vec2 vRawUV;

void main()
{
    // UIGradient.shader:163 -- UnityObjectToClipPos(v.vertex).
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);

    // UIGradient.shader:164 -- RAW pass-through. No TRANSFORM_TEX. See note 1.
    vPackedTint = aUv;

    // UIGradient.shader:165-167 -- pass-through. vGradient is data, not colour;
    // it is deliberately NOT run through NowUIColorToWorkingSpace. See note 2.
    vRect = aRect;
    vRadius = aRadius;
    vGradient = aColor;

    // UIGradient.shader:168 -- the ONLY colour this stage converts. Identity
    // under Gamma; alpha untouched in either branch.
    vOutlineColor = NowUIColorToWorkingSpace(aOutline);

    // UIGradient.shader:169-171. extras is (blur, outlineWidth, unused,
    // encodedRamp): .w packs the ramp atlas row in its integer part and the
    // gradient flags in its fraction, which the fragment stage decodes twice
    // -- once for kind/circle and once, differently shifted, for spread and
    // fixed-step mode.
    vExtras = aExtras;
    vMask = aMask;

    // UIGradient.shader:171 -- `o.rawUV = v.rawUV.xy;`. The narrowing to two
    // components is the HLSL's, not ours.
    vRawUV = aRawUV.xy;
}
