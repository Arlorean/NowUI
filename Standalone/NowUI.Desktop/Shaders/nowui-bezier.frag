#version 300 es
// ===========================================================================
// nowui-bezier.frag
//
// GLSL ES 3.00 port of `frag` from
// Assets/NowUI/Assets/Shaders/UIBezier.shader (shader "NowUI/UI Bezier").
// Every block cites the HLSL line it came from.
//
// COMPOSITION: see nowui-bezier.vert's header.
//
// WHAT THIS SHADER IS. Every other NowUI curve is flattened into line segments
// on the CPU and drawn as rectangles. This one keeps the TRUE cubic: the
// vertex stage hands each fragment the four control points and a rough
// parameter t, and the fragment stage runs four Newton steps on
//     f(t) = dot(B(t) - pixel, B'(t)) = 0
// to find the nearest point on the curve itself, then shades by that distance.
// So the stroke's edge is analytically antialiased against the real curve, and
// the flattening (NowLine.cs:576-612) only has to be good enough to cover the
// stroke -- not good enough to BE the stroke. Nothing here is stubbed and
// nothing is optional.
//
// TWO ORDERING FACTS THAT LOOK LIKE MISTAKES AND ARE NOT. Both are faithful to
// the HLSL and both are called out at their line below:
//   1. the legacy clip happens AFTER the Newton loop and the coverage, not
//      before it as in every other NowUI fragment stage;
//   2. `pixel` is used as UI space with NO y negation, because TEXCOORD7
//      already carries UI coordinates on this shader (see the .vert header).
//
// PRECISION -- read before "tidying". This is the port where mediump would do
// the most damage, and the damage would be silent:
//   * the power-basis coefficients are differences of control points that can
//     be hundreds of UI units apart, and `a = p3 - 3*p2 + 3*p1 - p0` is a
//     four-term alternating sum -- catastrophic cancellation at 10 bits;
//   * Newton's `t -= f / (fp + 1e-5)` divides one nearly-cancelled quantity by
//     another, and the 1e-5 guard is not even representable as distinct from
//     zero at mediump;
//   * the result would not be an error, it would be a curve whose edge wobbles
//     by a pixel in a way that reads as "the AA is a bit rough".
// The fragment language has no default float precision, so declaring it is
// mandatory rather than decorative; `highp sampler2D` matters because the
// fragment default for a sampler is LOWP and the mask include samples two.
// ===========================================================================

precision highp float;
precision highp int;
precision highp sampler2D;

//#include "nowui-mask.glsl"

// ---------------------------------------------------------------------------
// Uniforms. UIBezier's CGPROGRAM declares NONE of its own: everything this
// stage reads beyond the varyings comes from the mask include above. There is
// no _MainTex, no _MainTex_ST and no _Color. BezierMaterial.mat agrees -- its
// property list is exactly _NowUIMaskCount, _NowUITextureMask{0,1},
// _NowUITextureMaskCount and _ZTest.
//
// _ZTest is in the Properties block (:9) but is RENDER STATE, not a uniform.
// It is 8 (CompareFunction.Always). Never bind it to a location.
//
// Texture units are still fixed: 1 _NowUITextureMask0, 2 _NowUITextureMask1
// (M2-ShaderPort.md section 7.4), both bound to the 1x1 opaque black fallback
// when unused.
// ---------------------------------------------------------------------------

// Varyings -- must match nowui-bezier.vert's `out` block exactly.
in highp vec4 vCp01;
in highp vec4 vCp23;
in highp vec4 vColor;
in highp vec4 vParams;
in highp vec4 vMask;
in highp vec2 vPixel;

// UIBezier.shader:80 `fixed4 frag(...) : SV_Target` becomes an explicit out.
out highp vec4 fragColor;

void main()
{
    // UIBezier.shader:83-86. The four control points, in UI space.
    highp vec2 p0 = vCp01.xy;
    highp vec2 p1 = vCp01.zw;
    highp vec2 p2 = vCp23.xy;
    highp vec2 p3 = vCp23.zw;

    // UIBezier.shader:88-92. Bernstein to power basis:
    //     B(t) = a*t^3 + b*t^2 + c*t + d
    // Note `b` and `d` shadow nothing here, but do not rename them to match
    // sdRoundedBox's b/r conventions -- these are the HLSL's own names and the
    // line-for-line correspondence is the point.
    highp vec2 d = p0;
    highp vec2 c = 3.0 * (p1 - p0);
    highp vec2 b = 3.0 * (p0 - 2.0 * p1 + p2);
    highp vec2 a = p3 - 3.0 * p2 + 3.0 * p1 - p0;

    // UIBezier.shader:94-97.
    //
    // params.x/.y are in UI units, produced by
    // NowLine.cs:564-569 as `width * 0.5` and
    // `ScreenPixelsToUiUnits(LineAaWidth)`. aaWidth is therefore strictly
    // positive for any live stroke, which is what keeps the division on the
    // coverage line below well defined. The HLSL does not guard it and neither
    // does this port: adding a guard would hide the day it stops being true.
    highp float halfWidth = vParams.x;
    highp float aaWidth   = vParams.y;
    highp float t         = vParams.z;

    // `pixel` is TEXCOORD7.xy, which NowLine.AddBezierVertex writes as
    // (uiPos.x, uiPos.y) -- ALREADY UI space, y down and positive, unlike
    // POSITION which carries the usual negated y. So there is NO
    //     uiPosition = vec2(pos.x, -pos.y)
    // line in this shader, and adding one would mirror every curve about
    // y = 0. This is the single biggest difference from the other four ports.
    highp vec2 pixel = vPixel;

    // UIBezier.shader:99-114. Four Newton steps on f(t) = dot(B(t) - pixel, B'(t)).
    //
    // The vertex-supplied t is a good initial guess: it is the flattening
    // parameter of this vertex, interpolated across the segment quad, so it
    // already lands within one flattening step of the true nearest point.
    //
    // HLSL's [unroll] is a hint; the loop bound is the literal 4 in both
    // languages and GLSL ES 3.00 unrolls a constant-bounded loop on its own.
    // The `+ 1e-5` on the denominator is the HLSL's guard against an inflection
    // point where f'(t) vanishes; keep it exactly, including the sign (it is
    // added, not max'd, so a negative fp near -1e-5 still explodes -- which the
    // clamp on the next line then contains).
    for (int k = 0; k < 4; ++k)
    {
        highp float t2 = t * t;
        highp vec2 Bt = a * t2 * t + b * t2 + c * t + d;
        highp vec2 B1 = 3.0 * a * t2 + 2.0 * b * t + c;
        highp vec2 B2 = 6.0 * a * t + 2.0 * b;
        highp vec2 diff = Bt - pixel;
        highp float f = dot(diff, B1);
        highp float fp = dot(B1, B1) + dot(diff, B2);
        t -= f / (fp + 1e-5);
        t = clamp(t, 0.0, 1.0);
    }

    // UIBezier.shader:116-117. Horner evaluation at the converged t.
    highp vec2 closest = ((a * t + b) * t + c) * t + d;
    highp float dist = length(closest - pixel);

    // UIBezier.shader:119-120. Solid core out to (halfWidth - aaWidth), then a
    // linear fade across a band of width 2*aaWidth centred on halfWidth.
    // saturate -> clamp(x, 0.0, 1.0).
    highp float coverage = clamp((halfWidth + aaWidth - dist) / (2.0 * aaWidth), 0.0, 1.0);

    // UIBezier.shader:122-124. THE LEGACY HARD CLIP, and note where it sits:
    // AFTER the Newton loop and the coverage, not before them as in every
    // other NowUI fragment stage. That ordering is observable -- a discard
    // this late still costs the sixteen Newton multiplies -- but it is what
    // the HLSL does, it changes no pixel, and reordering it would be an
    // optimisation dressed as a port. Left where it is.
    //
    // `pixel` is passed directly, with no negation, for the reason above.
    highp vec4 mask = vMask;
    NowUIClipLegacyRect(pixel, mask);

    // UIBezier.shader:126 -- clip(coverage - 0.001). HLSL clip discards on
    // strictly negative, so a coverage of exactly 0.001 survives. For a stroke
    // this rejects most of each segment quad, which is padded out to
    // halfWidth + aaWidth + 2px by NowLine.cs:571.
    if (coverage - 0.001 < 0.0)
        discard;

    // UIBezier.shader:128-133. PREMULTIPLIED output -- the pipeline blends
    // with gl.blendFunc(ONE, ONE_MINUS_SRC_ALPHA). This shader uses the shared
    // premultiplied blend, unlike UIGlass.
    highp float alpha = vColor.a * coverage;
    highp vec4 col;
    col.rgb = vColor.rgb * alpha;
    col.a = alpha;

    // UIBezier.shader:132. HLSL `col *= x` on a float4 by a scalar scales rgb
    // AND a alike, which is correct for a premultiplied colour. This matches
    // UIRectangle, UIGradient and UIRipple; TxtRenderer and UIGlass
    // deliberately do it differently.
    col *= NowUIMaskCoverage(pixel);

    // UIBezier.shader:133
    fragColor = col;
}
