#ifndef NOWUI_MASK_INCLUDED
#define NOWUI_MASK_INCLUDED

// ===========================================================================
// nowui-mask.glsl
//
// GLSL ES 3.00 port of Assets/NowUI/Assets/Shaders/NowUIMask.cginc (236 lines,
// read in full). Shared verbatim by both in-scope programs, exactly as the
// .cginc is. Each block cites its HLSL line range.
//
// This file is an INCLUDE FRAGMENT, not a standalone shader: no "#version"
// line. The loader (nowui-glsl-include.js) splices it in where it finds
//     //#include "nowui-mask.glsl"
//
// FRAGMENT STAGE ONLY. NowUIAnalyticMaskEdgeCoverage calls fwidth(), and
// NowUITextureMaskSampleCoverage calls texture() with implicit derivatives;
// neither is available in the vertex stage. Do not include this from a .vert.
//
// Three independent things live here (M2-ShaderPort.md section 5):
//   1. the legacy axis-aligned hard clip, ALWAYS active, no AA, no uniform;
//   2. up to 8 analytic masks, driven by four vec4[8] uniform arrays;
//   3. up to 2 texture masks, with explicitly unrolled samplers.
//
// For the slice-1 quick-start scene both counts are 0, NowMaskShader.Apply
// never calls SetVectorArray, and every array uniform stays at GL's all-zero
// default -- which the early-out in NowUIMaskCoverage makes harmless. Keep that
// early-out first.
// ===========================================================================

// NowUIMask.cginc:7-8. FIXED capacities. Never size these from the count
// uniform: NowMaskShader passes the WHOLE static Vector4[8] / Vector4[2]
// scratch array, so what reaches the backend is always full capacity or
// absent entirely (M2-ShaderPort.md section 5.2).
#define NOW_UI_ANALYTIC_MASK_CAPACITY 8
#define NOW_UI_TEXTURE_MASK_CAPACITY 2

// NowUIMask.cginc:17-21. Per-entry packing:
//   Rects      = local-space (x, y, width, height)
//   Data       = packed radii (TR, BR, TL, BL) for rounded rectangles, or
//                capsule endpoints (start.x, start.y, end.x, end.y)
//   Params     = (kind, additional feather pixels, capsule radius, unused)
//   Transforms = (screen origin.x, screen origin.y, signed scale.x, signed scale.y)
// Kinds: 0 = rectangle, 1 = rounded rectangle, 2 = ellipse, 3 = capsule.
// Upload each array with ONE gl.uniform4fv against the location of the "[0]"
// element name, e.g. getUniformLocation(prog, "_NowUIMaskRects[0]").
uniform highp float _NowUIMaskCount;
uniform highp vec4  _NowUIMaskRects[NOW_UI_ANALYTIC_MASK_CAPACITY];
uniform highp vec4  _NowUIMaskData[NOW_UI_ANALYTIC_MASK_CAPACITY];
uniform highp vec4  _NowUIMaskParams[NOW_UI_ANALYTIC_MASK_CAPACITY];
uniform highp vec4  _NowUIMaskTransforms[NOW_UI_ANALYTIC_MASK_CAPACITY];

// NowUIMask.cginc:28-33. Texture mask packing:
//   Rects      = authored local-space (x, y, width, height)
//   Params     = (channel, inverted, valid texture, unused); channel 0 = alpha, 1 = red
//   Transforms = (screen origin.x, screen origin.y, signed scale.x, signed scale.y)
// The samplers are explicit rather than dynamically indexed. The HLSL did that
// for SM3 compatibility (cginc:27); GLSL ES 3.00 REQUIRES it, since a sampler
// array may only be indexed by a constant expression. Keep the unrolled form.
// Fixed texture units, per M2-ShaderPort.md section 7.4:
//   0 _MainTex   1 _NowUITextureMask0   2 _NowUITextureMask1   3 (ramp, unused)
// Both mask samplers must ALWAYS be bound to something -- NowMaskShader.Apply
// binds Texture2D.blackTexture (1x1 opaque black) when unused; the backend
// should do the same rather than leave a stale unit binding from a prior draw.
uniform highp float     _NowUITextureMaskCount;
uniform highp sampler2D _NowUITextureMask0;
uniform highp sampler2D _NowUITextureMask1;
uniform highp vec4      _NowUITextureMaskRects[NOW_UI_TEXTURE_MASK_CAPACITY];
uniform highp vec4      _NowUITextureMaskParams[NOW_UI_TEXTURE_MASK_CAPACITY];
uniform highp vec4      _NowUITextureMaskTransforms[NOW_UI_TEXTURE_MASK_CAPACITY];

// ---------------------------------------------------------------------------
// 1. Legacy rect clip -- always active, deliberately un-anti-aliased.
// ---------------------------------------------------------------------------

// NowUIMask.cginc:38-43. Returns POSITIVE inside the rect. `rect` is the
// per-vertex `mask` attribute (TEXCOORD6), in UI coordinates (y down,
// positive), and `position` is uiPosition.
highp float NowUILegacyRectDistance(highp vec2 position, highp vec4 rect)
{
    return min(
        min(position.x - rect.x, rect.x + rect.z - position.x),
        min(position.y - rect.y, rect.y + rect.w - position.y));
}

// NowUIMask.cginc:45-48. HLSL clip(x) discards on STRICTLY negative x, so a
// fragment exactly on the boundary (x == 0) survives: the test is `< 0.0`,
// not `<= 0.0`. Hard, not feathered, on purpose -- it preserves the existing
// NowRect mask contract exactly (cginc:35-37).
void NowUIClipLegacyRect(highp vec2 position, highp vec4 rect)
{
    if (NowUILegacyRectDistance(position, rect) < 0.0)
        discard;
}

// ---------------------------------------------------------------------------
// 2. Analytic masks. Signed-distance convention: NEGATIVE IS INSIDE
//    (cginc:50), the same convention sdRoundedBox uses in UIRectangle.
// ---------------------------------------------------------------------------

// NowUIMask.cginc:51-57
highp float NowUIRectMaskDistance(highp vec2 position, highp vec4 rect)
{
    highp vec2 halfSize = max(abs(rect.zw) * 0.5, 0.00001);
    highp vec2 center = rect.xy + rect.zw * 0.5;
    highp vec2 q = abs(position - center) - halfSize;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
}

// NowUIMask.cginc:62-76. Radii use NowCornerRadius.packed / the rectangle
// shader order: (TR, BR, TL, BL).
//
// CORNER SELECTION, THE THING MOST LIKELY TO BE GOT BACKWARDS: this function
// works in UI coordinates, where y points DOWN, so `local.y < 0.0` is the TOP
// half and selects radii.z (TL) / radii.x (TR). sdRoundedBox in
// nowui-rectangle.frag works in y-UP SDF space and selects the top from
// `p.y > 0.0`. Both end up taking the top-right radius from component .x.
// Invert either one and rounded masks come out mirrored in y -- a bug that
// survives casual inspection on a symmetric shape.
//
// The clamp exists so malformed data cannot invert the SDF (cginc:60-61).
highp float NowUIRoundedRectMaskDistance(highp vec2 position, highp vec4 rect, highp vec4 radii)
{
    highp vec2 halfSize = max(abs(rect.zw) * 0.5, 0.00001);
    highp vec2 local = position - (rect.xy + rect.zw * 0.5);
    highp float radius;

    if (local.x < 0.0)
        radius = local.y < 0.0 ? radii.z : radii.w;
    else
        radius = local.y < 0.0 ? radii.x : radii.y;

    radius = clamp(radius, 0.0, min(halfSize.x, halfSize.y));
    highp vec2 q = abs(local) - halfSize + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

// NowUIMask.cginc:81-86. Normalised ellipse distance: EXACT zero contour and
// sign, approximate magnitude away from the edge. That is sufficient because
// the derivative normalisation in NowUIAnalyticMaskEdgeCoverage rescales it
// into screen pixels anyway (cginc:78-80).
highp float NowUIEllipseMaskDistance(highp vec2 position, highp vec4 rect)
{
    highp vec2 halfSize = max(abs(rect.zw) * 0.5, 0.00001);
    highp vec2 local = position - (rect.xy + rect.zw * 0.5);
    return (length(local / halfSize) - 1.0) * min(halfSize.x, halfSize.y);
}

// NowUIMask.cginc:88-96. saturate(x) -> clamp(x, 0.0, 1.0).
highp float NowUICapsuleMaskDistance(highp vec2 position, highp vec4 endpoints, highp float radius)
{
    highp vec2 from = endpoints.xy;
    highp vec2 to = endpoints.zw;
    highp vec2 segment = to - from;
    highp float segmentLengthSquared = max(dot(segment, segment), 0.00001);
    highp float t = clamp(dot(position - from, segment) / segmentLengthSquared, 0.0, 1.0);
    return length(position - (from + segment * t)) - max(radius, 0.0);
}

// NowUIMask.cginc:98-116
highp float NowUIAnalyticMaskDistance(
    highp vec2 position,
    highp vec4 rect,
    highp vec4 data,
    highp vec4 parameters)
{
    highp float shapeKind = parameters.x;

    if (shapeKind < 0.5)
        return NowUIRectMaskDistance(position, rect);

    if (shapeKind < 1.5)
        return NowUIRoundedRectMaskDistance(position, rect, data);

    if (shapeKind < 2.5)
        return NowUIEllipseMaskDistance(position, rect);

    return NowUICapsuleMaskDistance(position, data, parameters.z);
}

// NowUIMask.cginc:118-129. maskTransform.xy is the translation and .zw the
// signed scale captured when the mask was pushed. THE SIGN MUST SURVIVE: a
// mirrored scope has to select the mirrored rounded-rectangle corner, so the
// magnitude is clamped away from zero without touching the sign.
highp vec2 NowUIMaskLocalPosition(highp vec2 position, highp vec4 maskTransform)
{
    highp vec2 signedScale = maskTransform.zw;
    highp vec2 safeScale = vec2(
        signedScale.x < 0.0 ? min(signedScale.x, -0.00001) : max(signedScale.x, 0.00001),
        signedScale.y < 0.0 ? min(signedScale.y, -0.00001) : max(signedScale.y, 0.00001));
    return (position - maskTransform.xy) / safeScale;
}

// NowUIMask.cginc:131-139. A zero feather still gets one screen pixel of
// derivative AA; feather is ADDITIONAL screen-pixel softness, matching the
// public SDF convention.
highp float NowUIAnalyticMaskEdgeCoverage(highp float signedDistance, highp float featherPixels)
{
    highp float distancePerPixel = max(fwidth(signedDistance), 0.00001);
    highp float transitionPixels = 1.0 + max(featherPixels, 0.0);
    highp float halfBand = 0.5 * transitionPixels * distancePerPixel;
    return 1.0 - smoothstep(-halfBand, halfBand, signedDistance);
}

// NowUIMask.cginc:141-166. Loop shape kept exactly: CONSTANT bound over the
// full capacity with an early break on the dynamic count. GLSL ES 3.00 would
// permit `maskIndex < maskCount` directly, but the constant bound costs nothing
// and keeps this a line-for-line match against the HLSL [unroll].
// (int)x -> int(x).
highp float NowUIAnalyticMaskCoverage(highp vec2 position)
{
    highp float coverage = 1.0;
    int maskCount = int(clamp(floor(_NowUIMaskCount + 0.5), 0.0, float(NOW_UI_ANALYTIC_MASK_CAPACITY)));

    for (int maskIndex = 0; maskIndex < NOW_UI_ANALYTIC_MASK_CAPACITY; ++maskIndex)
    {
        if (maskIndex >= maskCount)
            break;

        highp vec4 parameters = _NowUIMaskParams[maskIndex];
        highp vec2 localPosition = NowUIMaskLocalPosition(
            position,
            _NowUIMaskTransforms[maskIndex]);
        highp float signedDistance = NowUIAnalyticMaskDistance(
            localPosition,
            _NowUIMaskRects[maskIndex],
            _NowUIMaskData[maskIndex],
            parameters);
        highp float shapeCoverage = NowUIAnalyticMaskEdgeCoverage(signedDistance, parameters.y);
        coverage = min(coverage, shapeCoverage);
    }

    return coverage;
}

// ---------------------------------------------------------------------------
// 3. Texture masks.
// ---------------------------------------------------------------------------

// NowUIMask.cginc:168-192. Passing a sampler2D as a function parameter is
// legal in GLSL ES 3.00 (parameters must be `in`, and only sampler variables
// or sampler parameters may be passed), so the unrolled HLSL shape survives
// unchanged.
//
// Note the texture() call sits in non-uniform control flow. Its implicit
// derivatives are formally undefined there, exactly as in the HLSL; with a
// mip-less LINEAR texture there is no level to select, so nothing depends on
// them. Left faithful to the source rather than hoisted.
highp float NowUITextureMaskSampleCoverage(
    highp vec2 position,
    highp vec4 rect,
    highp vec4 parameters,
    highp vec4 maskTransform,
    highp sampler2D coverageTexture)
{
    // Validity is checked BEFORE inversion, so an empty source always stays
    // empty rather than inverting to fully opaque (cginc:175-176).
    if (parameters.z < 0.5 || rect.z <= 0.0 || rect.w <= 0.0)
        return 0.0;

    highp vec2 localPosition = NowUIMaskLocalPosition(position, maskTransform);
    highp vec2 normalized = (localPosition - rect.xy) / max(rect.zw, 0.00001);
    highp float inside =
        step(0.0, normalized.x) * step(normalized.x, 1.0) *
        step(0.0, normalized.y) * step(normalized.y, 1.0);

    // NowUI is top-left/y-down while texture UVs are bottom-left/y-up.
    highp vec2 uv = clamp(vec2(normalized.x, 1.0 - normalized.y), 0.0, 1.0);
    highp vec4 sampleValue = texture(coverageTexture, uv);
    highp float channelCoverage = parameters.x < 0.5 ? sampleValue.a : sampleValue.r;
    channelCoverage = parameters.y > 0.5 ? 1.0 - channelCoverage : channelCoverage;
    return clamp(channelCoverage, 0.0, 1.0) * inside;
}

// NowUIMask.cginc:194-224
highp float NowUITextureMaskCoverage(highp vec2 position)
{
    int maskCount = int(clamp(
        floor(_NowUITextureMaskCount + 0.5),
        0.0,
        float(NOW_UI_TEXTURE_MASK_CAPACITY)));

    if (maskCount <= 0)
        return 1.0;

    highp float coverage = NowUITextureMaskSampleCoverage(
        position,
        _NowUITextureMaskRects[0],
        _NowUITextureMaskParams[0],
        _NowUITextureMaskTransforms[0],
        _NowUITextureMask0);

    if (maskCount > 1)
    {
        coverage = min(
            coverage,
            NowUITextureMaskSampleCoverage(
                position,
                _NowUITextureMaskRects[1],
                _NowUITextureMaskParams[1],
                _NowUITextureMaskTransforms[1],
                _NowUITextureMask1));
    }

    return coverage;
}

// NowUIMask.cginc:226-234. THE EARLY-OUT. This is what makes the all-zero
// uniform arrays of the quick-start frame harmless: with both counts at 0
// nothing above is ever evaluated. Keep it first.
highp float NowUIMaskCoverage(highp vec2 position)
{
    if (_NowUIMaskCount < 0.5 && _NowUITextureMaskCount < 0.5)
        return 1.0;

    return min(
        NowUIAnalyticMaskCoverage(position),
        NowUITextureMaskCoverage(position));
}

#endif // NOWUI_MASK_INCLUDED
