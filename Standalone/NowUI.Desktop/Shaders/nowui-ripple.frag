#version 300 es
// ===========================================================================
// nowui-ripple.frag
//
// GLSL ES 3.00 port of sdRoundedBox + `frag` from
// Assets/NowUI/Assets/Shaders/UIRipple.shader (shader "NowUI/UI Ripple").
// Every block cites the HLSL line it came from.
//
// COMPOSITION: see nowui-ripple.vert's header.
//
// WHAT THIS SHADER IS. A ripple is the intersection of two coverages: the
// host control's rounded rectangle, and an expanding circle centred on the
// pointer. Nothing is stubbed and nothing is optional -- both SDFs, the
// legacy clip and the mask coverage all run on every fragment.
//
// PRECISION -- read before "tidying". Unity's fixed4 is a legacy precision
// alias that would map to lowp. Do NOT translate it that way: both
// `length(vec2(dFdx(d), dFdy(d)))` terms are differences of nearly equal
// numbers, and the ripple's circle distance is computed in UI units that run
// to the hundreds, which mediump's 10-bit mantissa cannot resolve to a
// sub-pixel edge. The fragment language has no default float precision, so
// declaring it is mandatory rather than decorative; `highp sampler2D` matters
// because the fragment default for a sampler is LOWP and the mask include
// samples two of them.
// ===========================================================================

precision highp float;
precision highp int;
precision highp sampler2D;

//#include "nowui-mask.glsl"

// ---------------------------------------------------------------------------
// Uniforms. UIRipple's CGPROGRAM declares NONE of its own: everything this
// stage reads beyond the varyings comes from the mask include above. There is
// no _MainTex, no _MainTex_ST and no _Color.
//
// _ZTest is in the Properties block but is RENDER STATE, not a uniform. It is
// 8 (CompareFunction.Always) in RippleMaterial.mat and nothing in the
// standalone build writes it. Never bind it to a location.
// ---------------------------------------------------------------------------

// Varyings -- must match nowui-ripple.vert's `out` block exactly.
in highp vec4 vRect;
in highp vec4 vRadius;
in highp vec4 vColor;
in highp vec4 vExtras;
in highp vec4 vMask;
in highp vec4 vRawUV;

// UIRipple.shader:86 `fixed4 frag(...) : SV_Target` becomes an explicit out.
out highp vec4 fragColor;

// ---------------------------------------------------------------------------
// UIRipple.shader:66-73. Byte-identical to the copy in UIRectangle.shader and
// UIGradient.shader -- the three files each carry their own, so the ports do
// too rather than inventing a fourth shared include the HLSL does not have.
//
//   p = fragment position relative to the box CENTRE, in y-UP space
//   b = half size
//   r = corner radii packed (TR, BR, TL, BL)
//
// p.y > 0 is the TOP because p is built from rawUV, and rawUV.y == 1 is the UI
// top edge (M2-ShaderPort.md section 1.3). The two ternaries narrow the four
// radii to the one for p's quadrant, and :77 reads r.x/r.y AFTER :76 has
// overwritten them -- the order matters, do not reorder.
// ---------------------------------------------------------------------------
highp float sdRoundedBox(highp vec2 p, highp vec2 b, highp vec4 r)
{
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    highp vec2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
}

void main()
{
    // UIRipple.shader:89-94.
    //
    // The HLSL takes `float2 rawUV = i.rawUV.xy;` explicitly here, so unlike
    // UIRectangle there is no float4*float2 truncation to translate -- but the
    // same rule applies: rect.xy is the UI BOTTOM-LEFT corner expressed in
    // negated-y mesh space and rect.zw is always positive, so negating pos.y
    // recovers UI space (y down, positive).
    highp vec4 rect = vRect;
    highp vec4 mask = vMask;
    highp vec2 rawUV = vRawUV.xy;
    highp vec2 pos = rect.xy + rawUV * rect.zw;
    highp vec2 uiPosition = vec2(pos.x, -pos.y);

    // UIRipple.shader:96 -- the legacy hard clip. Unconditional, no AA, before
    // any other work. May discard.
    NowUIClipLegacyRect(uiPosition, mask);

    // UIRipple.shader:98-101. Coverage 1: the host control's rounded rect, in
    // FULL-QUAD space. Do NOT clamp rawUV -- geometry padding legitimately
    // pushes it outside [0,1] and the SDF handles that by construction.
    // Note this shader uses a plain half-pixel band with NO `blur` term and no
    // outline: extras.x/.y mean something else here (see below), so there is
    // nothing to widen the band with.
    highp vec2 centered = (rawUV - 0.5) * rect.zw;
    highp float shapeDist = sdRoundedBox(centered, rect.zw * 0.5, vRadius);
    highp float shapeDelta = max(length(vec2(dFdx(shapeDist), dFdy(shapeDist))), 0.0001);
    highp float shapeAlpha = 1.0 - smoothstep(-0.5 * shapeDelta, 0.5 * shapeDelta, shapeDist);

    // UIRipple.shader:103-105. Coverage 2: the expanding circle.
    //
    // extras is NOT the (blur, outline, ...) packing the rectangle shader uses.
    // NowRipple.cs:154 writes it as Vector4(origin.x, origin.y, 0, radius):
    //   .xy = the ripple's centre, already in UI coordinates (y down), which is
    //         why it is compared against uiPosition and NOT against `centered`;
    //   .z  = unused;
    //   .w  = the circle's radius in UI units, grown by the animation and
    //         scaled by the ambient transform.
    // A negative or zero radius never reaches here: NowRipple.Draw returns
    // early when circleRadius <= 0.
    highp float circleDist = length(uiPosition - vExtras.xy) - vExtras.w;
    highp float circleDelta = max(length(vec2(dFdx(circleDist), dFdy(circleDist))), 0.0001);
    highp float circleAlpha = 1.0 - smoothstep(-0.5 * circleDelta, 0.5 * circleDelta, circleDist);

    // UIRipple.shader:107-111. The two coverages MULTIPLY: the ripple is the
    // circle clipped to the control, so a circle that has grown past the
    // control's rounded corner is trimmed by the corner rather than squared
    // off. Output is PREMULTIPLIED -- the pipeline blends with
    // gl.blendFunc(ONE, ONE_MINUS_SRC_ALPHA).
    highp float alpha = vColor.a * shapeAlpha * circleAlpha;
    highp vec4 col;
    col.rgb = vColor.rgb * alpha;
    col.a = alpha;

    // UIRipple.shader:112. HLSL `col *= x` on a float4 by a scalar scales rgb
    // AND a alike, which is correct for a premultiplied colour. This matches
    // UIRectangle and UIGradient; TxtRenderer deliberately does it differently.
    col *= NowUIMaskCoverage(uiPosition);

    // UIRipple.shader:113 -- clip(col.a - 0.001). HLSL clip discards on
    // strictly negative, so alpha exactly 0.001 survives. For a ripple this
    // rejects the whole quad outside the circle, which is most of it.
    if (col.a - 0.001 < 0.0)
        discard;

    // UIRipple.shader:114
    fragColor = col;
}
