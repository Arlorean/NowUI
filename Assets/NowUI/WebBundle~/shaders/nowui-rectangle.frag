#version 300 es
// ===========================================================================
// nowui-rectangle.frag
//
// GLSL ES 3.00 port of sdRoundedBox + `frag` from
// Assets/NowUI/Assets/Shaders/UIRectangle.shader (shader "NowUI/UI Rectangle").
// Every block cites the HLSL line it came from.
//
// COMPOSITION: see nowui-rectangle.vert's header. `//#include "..."` lines are
// substituted by nowui-glsl-include.js before gl.shaderSource.
//
// Nothing in this shader is stubbed. All of it runs for the quick-start panel:
// non-zero corner radii, translucent fill, a 1x1 white _MainTex, blur == 0,
// outline == 0, both mask counts 0 (M2-ShaderPort.md section 3.6).
//
// PRECISION -- read this before "tidying" it. Unity's fixed4/half4/half3 are
// legacy precision aliases that would map to lowp/mediump. Do NOT translate
// them that way:
//   * `delta = length(vec2(dFdx(dist), dFdy(dist)))` is a difference of two
//     nearly equal numbers; at mediump the AA band width becomes noise.
//   * the SDF works in local UI units, which for a full-screen mask rect run
//     to the hundreds -- mediump's 10-bit mantissa cannot resolve a sub-pixel
//     edge at that magnitude.
// The fragment language has NO default float precision (it must be declared)
// and defaults sampler2D to LOWP, so `precision highp sampler2D` is not
// decoration either -- it is what keeps texture() returning full precision.
// ===========================================================================

precision highp float;
precision highp int;
precision highp sampler2D;

//#include "nowui-mask.glsl"

// ---------------------------------------------------------------------------
// Uniforms. UIRectangle.shader:69-72 declares four; only three are real:
//   :69 sampler2D _MainTex               -- ported
//   :70 float4    _MainTex_ST            -- vertex stage only, see the .vert
//   :71 float4    _Color                 -- DECLARED AND NEVER USED. It appears
//                                           nowhere in vert or frag and is not
//                                           in the Properties block, so it is
//                                           not in shaders.json either.
//                                           Deliberately NOT ported; do not
//                                           bind it (M2-ShaderPort.md 3.2).
//   :72 float     _NowPremultipliedTexture -- ported
//
// _ZTest is in the Properties block but is RENDER STATE, not a uniform. It is
// always 8 (CompareFunction.Always) in this build. Never bind it to a location.
//
// _MainTex fallback when the material has no texture: a 1x1 OPAQUE WHITE
// texture, so textureSample is (1,1,1,1) and the fill reduces to vColor.
// Texture unit 0 (M2-ShaderPort.md section 7.4).
// ---------------------------------------------------------------------------
uniform highp sampler2D _MainTex;
uniform highp float _NowPremultipliedTexture;

// Varyings -- must match nowui-rectangle.vert's `out` block exactly.
in highp vec2 vUv;
in highp vec4 vRect;
in highp vec4 vRadius;
in highp vec4 vColor;
in highp vec4 vOutlineColor;
in highp vec4 vExtras;
in highp vec4 vMask;
in highp vec4 vRawUV;

// UIRectangle.shader:99 `fixed4 frag(...) : SV_Target` becomes an explicit out.
out highp vec4 fragColor;

// ---------------------------------------------------------------------------
// UIRectangle.shader:74-80. Inigo Quilez's rounded-box SDF, negative inside.
//
//   p = fragment position relative to the box CENTRE, in y-UP space
//   b = half size
//   r = corner radii packed (TR, BR, TL, BL)
//
// The two ternaries narrow four radii down to one, for the quadrant p is in:
//   :76  r.xy = (p.x > 0) ? r.xy : r.zw   -- right-hand pair (TR,BR), else left (TL,BL)
//   :77  r.x  = (p.y > 0) ? r.x  : r.y    -- then top of that pair, else bottom
// p.y > 0 is the TOP because the caller builds p from rawUV, and rawUV.y == 1
// is the UI top edge (M2-ShaderPort.md section 1.3). Cross-check: the mask
// include's rounded-rect selects the top from `local.y < 0.0` because it works
// in y-DOWN UI space. Both take the top-right radius from component .x.
//
// The rest (:78-79) is the standard box distance with the corner radius
// subtracted: inflate the half-size by r, take the distance to that box, then
// deflate by r.
//
// This is legal GLSL ES 3.00 verbatim: a ternary whose condition is a scalar
// bool may have vector branches, and writing to a swizzle of an `in` parameter
// (a writable local copy) is allowed. Note that :77 reads r.x and r.y AFTER
// :76 has overwritten them -- the order matters, do not reorder.
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
    // UIRectangle.shader:102-103
    highp vec4 rect = vRect;
    highp vec4 mask = vMask;

    // UIRectangle.shader:105-107.
    //
    // THE ONE LINE WHERE HLSL AND GLSL GENUINELY DIFFER: :106 is
    //     float2 pos = rect.xy + i.rawUV * rect.zw;
    // a float4 * float2, which HLSL silently truncates the float4 to .xy for.
    // GLSL refuses to compile that -- which is the good outcome. Write .xy.
    //
    // rect.xy is the UI BOTTOM-LEFT corner expressed in negated-y mesh space
    // and rect.zw is always positive, so negating pos.y recovers UI space
    // (y down, positive). See M2-ShaderPort.md section 1.3.
    highp vec2 size = rect.zw;
    highp vec2 pos = rect.xy + vRawUV.xy * rect.zw;
    highp vec2 uiPosition = vec2(pos.x, -pos.y);

    // UIRectangle.shader:109-110 -- the legacy hard clip. Unconditional, no AA,
    // before any other work. May discard.
    NowUIClipLegacyRect(uiPosition, mask);

    // UIRectangle.shader:112-116. extras.x = blur (extra outer softness in
    // local units), extras.y = outline width in local units. extras.zw unused
    // by this shader, as is rawUV.zw.
    highp vec4 rad = vRadius;
    highp vec4 color = vColor;
    highp vec4 data = vExtras;
    highp float blur = data.x;
    highp float outline = data.y;

    // UIRectangle.shader:118 -- tex2D -> texture.
    highp vec4 textureSample = texture(_MainTex, vUv);

    // UIRectangle.shader:120-123. The SDF runs in FULL-QUAD space, not the
    // (possibly atlased) texture UV space, so sprites and custom UVs keep the
    // same corners. Do NOT clamp or saturate rawUV here: with geometryPadding
    // > 0 the quad grows outward and rawUV legitimately leaves [0,1]; the SDF
    // handles that by construction.
    highp vec2 position = (vRawUV.xy - 0.5) * size;
    highp vec2 halfSize = size * 0.5;

    // UIRectangle.shader:125-127. ddx/ddy -> dFdx/dFdy, both core in GLSL ES
    // 3.00 (no OES_standard_derivatives -- that is the WebGL1 story).
    // `delta` is the local-unit length of one screen pixel measured along the
    // distance gradient; the 1e-4 floor keeps a degenerate quad finite.
    highp float dist = sdRoundedBox(position, halfSize, rad);
    highp float delta = max(length(vec2(dFdx(dist), dFdy(dist))), 0.0001);

    // UIRectangle.shader:129-132. Half-pixel AA band centred on the true edge,
    // so alpha crosses 0.5 exactly at dist == 0 and shapes neither grow nor
    // halo. `blur` widens only the OUTER side of the band.
    // smoothstep has identical semantics in HLSL and GLSL, including that
    // edge0 >= edge1 is undefined; aa > 0 and blur >= 0 make edge0 < edge1 here
    // by construction, so do not "helpfully" reorder the arguments.
    highp float aa = 0.5 * delta;
    highp float graphicAlpha = 1.0 - smoothstep(-aa, aa + max(blur, 0.0), dist);

    // UIRectangle.shader:134-140. An outline thinner than one AA width would
    // sit entirely inside the edge fade and render as a washed-out sliver, so
    // it is never drawn thinner than `delta`. The inner transition is centred
    // on -outlineWidth so the ring renders at its requested thickness rather
    // than one AA width fatter.
    // `outline == 0` is an EXACT equality test in the HLSL and stays one here:
    // it is a disable switch, not a threshold.
    highp float outlineWidth = max(outline, delta);
    highp float outlineAlpha = outline == 0.0
        ? 0.0
        : smoothstep(-outlineWidth - aa, -outlineWidth + aa, dist);

    // UIRectangle.shader:142-149. Premultiplied compositing of outline over
    // fill: it avoids colour leaking through partially transparent pixels while
    // keeping the existing inside-outline behaviour.
    highp float outlineCoverage = vOutlineColor.a * outlineAlpha * graphicAlpha;
    highp float fillCoverage = textureSample.a * color.a * graphicAlpha;
    highp vec3 fillColor = _NowPremultipliedTexture > 0.5
        ? textureSample.rgb * color.rgb * color.a * graphicAlpha
        : textureSample.rgb * color.rgb * fillCoverage;

    // UIRectangle.shader:151-154. Output is PREMULTIPLIED; the pipeline blends
    // with gl.blendFunc(ONE, ONE_MINUS_SRC_ALPHA).
    highp vec4 col;
    col.rgb = vOutlineColor.rgb * outlineCoverage
        + fillColor * (1.0 - outlineCoverage);
    col.a = outlineCoverage + fillCoverage * (1.0 - outlineCoverage);

    // UIRectangle.shader:155. HLSL `col *= x` on a float4 by a scalar scales
    // rgb AND a alike, which is the correct thing to do to a premultiplied
    // colour. Note TxtRenderer applies the same coverage to ALPHA ONLY and then
    // premultiplies; both are right for their own shader. Do not unify them.
    col *= NowUIMaskCoverage(uiPosition);

    // UIRectangle.shader:156 -- clip(col.a - 0.001). HLSL clip discards on
    // strictly negative, so alpha exactly 0.001 survives. A cheap reject of
    // fully transparent fragments. TxtRenderer deliberately has no equivalent.
    if (col.a - 0.001 < 0.0)
        discard;

    // UIRectangle.shader:158
    fragColor = col;
}
