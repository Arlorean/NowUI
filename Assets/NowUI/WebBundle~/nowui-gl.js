// The GL half of the NowUI WebGL2 render backend. C# (WebGL2Backend.cs) owns every decision; this file owns
// nothing but GL calls and the two ported shader programs.
//
// Contract with WebGL2Backend.cs — keep the two in step:
//   * Attribute locations 0..8 are fixed here and in NowMeshLayout on the C# side.
//   * The uniform block is a flat Float32Array of UNIFORM_FLOATS entries whose slot map is duplicated as
//     WebGL2Backend.UniformSlots. Change one, change both.
//   * Every Span<byte>/Span<int> argument arrives as a .NET MemoryView, valid only for the duration of the call.
//     Copy it (`asBytes`/`asInts`) before doing anything asynchronous with it. Nothing here is asynchronous.
//
// Colour space is Gamma (Docs/Standalone/M2-ShaderPort.md §6): no SRGB8_ALPHA8, no sRGB framebuffer, no
// UNPACK_COLORSPACE_CONVERSION. `NowUIColorToWorkingSpace` below is deliberately the identity function.
//
// Ported from Assets/NowUI/Assets/Shaders/{UIRectangle,TxtRenderer}.shader and NowUIMask.cginc. Those files are
// frozen; this is a translation of them, not a reinterpretation.

'use strict';

// ---------------------------------------------------------------------------------------------- shared GLSL

// Vertex attribute locations. Identical for both programs so one VAO layout serves both (§1.4).
const ATTR = [
    { name: 'aPosition', size: 3 },  // 0  POSITION   quad corner, mesh space (z always 0)
    { name: 'aUv', size: 2 },        // 1  TEXCOORD0  texture UV, Unity bottom-up
    { name: 'aRect', size: 4 },      // 2  TEXCOORD1  shape rect (x, y, w, h) in mesh space
    { name: 'aRadius', size: 4 },    // 3  TEXCOORD2  corner radii (TR, BR, TL, BL) / text gradient payload
    { name: 'aColor', size: 4 },     // 4  TEXCOORD3  fill RGBA, display encoded, not premultiplied
    { name: 'aOutline', size: 4 },   // 5  TEXCOORD4  outline RGBA
    { name: 'aExtras', size: 4 },    // 6  TEXCOORD5  per-shader scalars
    { name: 'aMask', size: 4 },      // 7  TEXCOORD6  legacy clip rect, UI coords (y down)
    { name: 'aRawUV', size: 4 },     // 8  TEXCOORD7  un-atlased quad coordinate
];

const VERTEX_FLOATS_PER_VERTEX = ATTR.reduce((n, a) => n + a.size, 0); // 33 floats = 132 bytes

// NowUIColorSpace.cginc under UNITY_COLORSPACE_GAMMA. Kept as a named function rather than inlined so the linear
// branch is one edit away for a later slice (§6.3).
// Both overloads, because UIGradient converts in the FRAGMENT stage: the ramp is sampled and the tint unpacked
// there, so it calls the vec3 form on each (UIGradient.shader:212-215). The other three programs convert vertex
// colours only and use the vec4 form. Alpha is never converted, in either branch of the cginc.
const GLSL_COLOR_SPACE = `
vec3 NowUIColorToWorkingSpace(vec3 c) { return c; }
vec4 NowUIColorToWorkingSpace(vec4 c) { return vec4(NowUIColorToWorkingSpace(c.rgb), c.a); }
`;

// NowUIMask.cginc, verbatim in behaviour. Array capacities are fixed at 8 and 2 and must never be sized from the
// count uniform (§5.2).
const GLSL_MASK = `
#define NOW_UI_ANALYTIC_MASK_CAPACITY 8
#define NOW_UI_TEXTURE_MASK_CAPACITY 2

uniform float _NowUIMaskCount;
uniform vec4  _NowUIMaskRects[NOW_UI_ANALYTIC_MASK_CAPACITY];
uniform vec4  _NowUIMaskData[NOW_UI_ANALYTIC_MASK_CAPACITY];
uniform vec4  _NowUIMaskParams[NOW_UI_ANALYTIC_MASK_CAPACITY];
uniform vec4  _NowUIMaskTransforms[NOW_UI_ANALYTIC_MASK_CAPACITY];

uniform float _NowUITextureMaskCount;
uniform highp sampler2D _NowUITextureMask0;
uniform highp sampler2D _NowUITextureMask1;
uniform vec4  _NowUITextureMaskRects[NOW_UI_TEXTURE_MASK_CAPACITY];
uniform vec4  _NowUITextureMaskParams[NOW_UI_TEXTURE_MASK_CAPACITY];
uniform vec4  _NowUITextureMaskTransforms[NOW_UI_TEXTURE_MASK_CAPACITY];

float NowUILegacyRectDistance(vec2 position, vec4 rect)
{
    return min(
        min(position.x - rect.x, rect.x + rect.z - position.x),
        min(position.y - rect.y, rect.y + rect.w - position.y));
}

float NowUIRectMaskDistance(vec2 position, vec4 rect)
{
    vec2 halfSize = max(abs(rect.zw) * 0.5, vec2(0.00001));
    vec2 center = rect.xy + rect.zw * 0.5;
    vec2 q = abs(position - center) - halfSize;
    return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0);
}

// Radii are (TR, BR, TL, BL). This works in UI coordinates (y down), so local.y < 0.0 is the TOP half — the
// opposite y sense from sdRoundedBox in the rectangle shader, which works in y-up SDF space. Both end up
// selecting the top-right radius from .x.
float NowUIRoundedRectMaskDistance(vec2 position, vec4 rect, vec4 radii)
{
    vec2 halfSize = max(abs(rect.zw) * 0.5, vec2(0.00001));
    vec2 local = position - (rect.xy + rect.zw * 0.5);
    float radius;

    if (local.x < 0.0)
        radius = local.y < 0.0 ? radii.z : radii.w;
    else
        radius = local.y < 0.0 ? radii.x : radii.y;

    radius = clamp(radius, 0.0, min(halfSize.x, halfSize.y));
    vec2 q = abs(local) - halfSize + radius;
    return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - radius;
}

float NowUIEllipseMaskDistance(vec2 position, vec4 rect)
{
    vec2 halfSize = max(abs(rect.zw) * 0.5, vec2(0.00001));
    vec2 local = position - (rect.xy + rect.zw * 0.5);
    return (length(local / halfSize) - 1.0) * min(halfSize.x, halfSize.y);
}

float NowUICapsuleMaskDistance(vec2 position, vec4 endpoints, float radius)
{
    vec2 fromPoint = endpoints.xy;
    vec2 toPoint = endpoints.zw;
    vec2 segment = toPoint - fromPoint;
    float segmentLengthSquared = max(dot(segment, segment), 0.00001);
    float t = clamp(dot(position - fromPoint, segment) / segmentLengthSquared, 0.0, 1.0);
    return length(position - (fromPoint + segment * t)) - max(radius, 0.0);
}

float NowUIAnalyticMaskDistance(vec2 position, vec4 rect, vec4 data, vec4 parameters)
{
    float shapeKind = parameters.x;
    if (shapeKind < 0.5) return NowUIRectMaskDistance(position, rect);
    if (shapeKind < 1.5) return NowUIRoundedRectMaskDistance(position, rect, data);
    if (shapeKind < 2.5) return NowUIEllipseMaskDistance(position, rect);
    return NowUICapsuleMaskDistance(position, data, parameters.z);
}

// The sign of the scale must survive: a mirrored scope has to select the mirrored rounded-rect corner.
vec2 NowUIMaskLocalPosition(vec2 position, vec4 maskTransform)
{
    vec2 signedScale = maskTransform.zw;
    vec2 safeScale = vec2(
        signedScale.x < 0.0 ? min(signedScale.x, -0.00001) : max(signedScale.x, 0.00001),
        signedScale.y < 0.0 ? min(signedScale.y, -0.00001) : max(signedScale.y, 0.00001));
    return (position - maskTransform.xy) / safeScale;
}

float NowUIAnalyticMaskEdgeCoverage(float signedDistance, float featherPixels)
{
    float distancePerPixel = max(fwidth(signedDistance), 0.00001);
    float transitionPixels = 1.0 + max(featherPixels, 0.0);
    float halfBand = 0.5 * transitionPixels * distancePerPixel;
    return 1.0 - smoothstep(-halfBand, halfBand, signedDistance);
}

float NowUIAnalyticMaskCoverage(vec2 position)
{
    float coverage = 1.0;
    int maskCount = int(clamp(floor(_NowUIMaskCount + 0.5), 0.0, float(NOW_UI_ANALYTIC_MASK_CAPACITY)));

    for (int maskIndex = 0; maskIndex < NOW_UI_ANALYTIC_MASK_CAPACITY; ++maskIndex)
    {
        if (maskIndex >= maskCount)
            break;

        vec4 parameters = _NowUIMaskParams[maskIndex];
        vec2 localPosition = NowUIMaskLocalPosition(position, _NowUIMaskTransforms[maskIndex]);
        float signedDistance = NowUIAnalyticMaskDistance(
            localPosition, _NowUIMaskRects[maskIndex], _NowUIMaskData[maskIndex], parameters);
        coverage = min(coverage, NowUIAnalyticMaskEdgeCoverage(signedDistance, parameters.y));
    }

    return coverage;
}

// Validity is checked BEFORE inversion so an empty source always stays empty.
float NowUITextureMaskSampleCoverage(
    vec2 position, vec4 rect, vec4 parameters, vec4 maskTransform, highp sampler2D coverageTexture)
{
    if (parameters.z < 0.5 || rect.z <= 0.0 || rect.w <= 0.0)
        return 0.0;

    vec2 localPosition = NowUIMaskLocalPosition(position, maskTransform);
    vec2 normalized = (localPosition - rect.xy) / max(rect.zw, vec2(0.00001));
    float inside =
        step(0.0, normalized.x) * step(normalized.x, 1.0) *
        step(0.0, normalized.y) * step(normalized.y, 1.0);

    // NowUI is top-left/y-down while texture UVs are bottom-left/y-up.
    vec2 uv = clamp(vec2(normalized.x, 1.0 - normalized.y), 0.0, 1.0);
    vec4 sampleValue = texture(coverageTexture, uv);
    float channelCoverage = parameters.x < 0.5 ? sampleValue.a : sampleValue.r;
    channelCoverage = parameters.y > 0.5 ? 1.0 - channelCoverage : channelCoverage;
    return clamp(channelCoverage, 0.0, 1.0) * inside;
}

// Samplers are explicitly unrolled: GLSL ES 3.00 only permits constant-expression sampler indexing, which is
// also why the HLSL does it this way.
float NowUITextureMaskCoverage(vec2 position)
{
    int maskCount = int(clamp(floor(_NowUITextureMaskCount + 0.5), 0.0, float(NOW_UI_TEXTURE_MASK_CAPACITY)));
    if (maskCount <= 0)
        return 1.0;

    float coverage = NowUITextureMaskSampleCoverage(
        position, _NowUITextureMaskRects[0], _NowUITextureMaskParams[0],
        _NowUITextureMaskTransforms[0], _NowUITextureMask0);

    if (maskCount > 1)
    {
        coverage = min(coverage, NowUITextureMaskSampleCoverage(
            position, _NowUITextureMaskRects[1], _NowUITextureMaskParams[1],
            _NowUITextureMaskTransforms[1], _NowUITextureMask1));
    }

    return coverage;
}

// This early-out is what makes the all-zero uniform arrays of a mask-free frame harmless. Keep it first.
float NowUIMaskCoverage(vec2 position)
{
    if (_NowUIMaskCount < 0.5 && _NowUITextureMaskCount < 0.5)
        return 1.0;

    return min(NowUIAnalyticMaskCoverage(position), NowUITextureMaskCoverage(position));
}
`;

// One vertex shader serves both programs: UIRectangle.vert and TxtRenderer.vert are line-for-line identical.
const GLSL_VERTEX = `#version 300 es
precision highp float;
precision highp int;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;
layout(location = 3) in vec4 aRadius;
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;
layout(location = 6) in vec4 aExtras;
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;

uniform mat4 nowui_MatrixMVP;
uniform vec4 _MainTex_ST;

out vec2 vUv;
out vec4 vRect;
out vec4 vRadius;
out vec4 vColor;
out vec4 vOutline;
out vec4 vExtras;
out vec4 vMask;
out vec4 vRawUV;
${GLSL_COLOR_SPACE}
void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);
    vUv = aUv * _MainTex_ST.xy + _MainTex_ST.zw;   // TRANSFORM_TEX
    vRect = aRect;
    vRadius = aRadius;
    vColor = NowUIColorToWorkingSpace(aColor);
    vOutline = NowUIColorToWorkingSpace(aOutline);
    vExtras = aExtras;
    vMask = aMask;
    vRawUV = aRawUV;
}
`;

const GLSL_VARYINGS_IN = `
in vec2 vUv;
in vec4 vRect;
in vec4 vRadius;
in vec4 vColor;
in vec4 vOutline;
in vec4 vExtras;
in vec4 vMask;
in vec4 vRawUV;
`;

// NowUI/UI Rectangle — Assets/NowUI/Assets/Shaders/UIRectangle.shader, one pass.
//
// Samplers are declared `highp` on purpose. GLSL ES gives sampler2D a default precision of lowp, and a lowp
// texture result would propagate lowp through the arithmetic below.
const GLSL_FRAGMENT_RECTANGLE = `#version 300 es
precision highp float;
precision highp int;

uniform highp sampler2D _MainTex;
uniform float _NowPremultipliedTexture;
${GLSL_VARYINGS_IN}
${GLSL_MASK}
out vec4 fragColor;

float sdRoundedBox(vec2 p, vec2 b, vec4 r)
{
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    vec2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, vec2(0.0))) - r.x;
}

void main()
{
    vec4 rect = vRect;
    vec4 mask = vMask;

    vec2 size = rect.zw;
    // The HLSL writes \`i.rawUV * rect.zw\`, a float4*float2 that HLSL truncates to .xy. GLSL will not, so the
    // swizzle is explicit.
    vec2 pos = rect.xy + vRawUV.xy * rect.zw;
    vec2 uiPosition = vec2(pos.x, -pos.y);

    // NowUIClipLegacyRect. HLSL clip() discards on strictly negative, so an exact 0.0 survives.
    if (NowUILegacyRectDistance(uiPosition, mask) < 0.0)
        discard;

    vec4 rad = vRadius;
    vec4 color = vColor;
    vec4 data = vExtras;
    float blur = data.x;
    float outline = data.y;

    vec4 textureSample = texture(_MainTex, vUv);

    // Texture UVs can point into an atlas; the shape SDF stays in full-quad space so sprites and custom UVs keep
    // the same corners.
    vec2 position = (vRawUV.xy - 0.5) * size;
    vec2 halfSize = size * 0.5;

    float dist = sdRoundedBox(position, halfSize, rad);
    float delta = max(length(vec2(dFdx(dist), dFdy(dist))), 0.0001);

    float aa = 0.5 * delta;
    float graphicAlpha = 1.0 - smoothstep(-aa, aa + max(blur, 0.0), dist);

    float outlineWidth = max(outline, delta);
    float outlineAlpha = outline == 0.0 ? 0.0 : smoothstep(-outlineWidth - aa, -outlineWidth + aa, dist);

    float outlineCoverage = vOutline.a * outlineAlpha * graphicAlpha;
    float fillCoverage = textureSample.a * color.a * graphicAlpha;
    vec3 fillColor = _NowPremultipliedTexture > 0.5
        ? textureSample.rgb * color.rgb * color.a * graphicAlpha
        : textureSample.rgb * color.rgb * fillCoverage;

    vec4 col;
    col.rgb = vOutline.rgb * outlineCoverage + fillColor * (1.0 - outlineCoverage);
    col.a = outlineCoverage + fillCoverage * (1.0 - outlineCoverage);

    // \`col *= x\` on a float4 by a float scales rgb and a alike, which is correct for premultiplied output.
    col *= NowUIMaskCoverage(uiPosition);

    if (col.a - 0.001 < 0.0)
        discard;

    fragColor = col;
}
`;

// NowUITextGradient.cginc — the text shader's gradient fill, and the only part of either slice-1 program that is
// GENERATED rather than hand-copied. It declares _NowGradientRampTexture, which is a shader GLOBAL
// (Shader.SetGlobalTexture, NowGradient.cs:580) and therefore reaches draw() through NowRuntime.globals rather
// than any material bag. Requires GLSL_COLOR_SPACE ahead of it.
// GENERATED from wwwroot/shaders/nowui-text-gradient.glsl by tools/embed-shader.py -- edit that file, not this.
const GLSL_TEXT_GRADIENT = `#ifndef NOWUI_TEXT_GRADIENT_INCLUDED
#define NOWUI_TEXT_GRADIENT_INCLUDED

uniform highp sampler2D _NowGradientRampTexture;

highp float hlslFmod(highp float x, highp float y)
{
    return x - y * trunc(x / y);
}

highp float NowUITextGradientApplySpread(highp float t, highp float spread)
{
    if (spread < 0.5)
        return clamp(t, 0.0, 1.0);

    if (spread < 1.5)
        return fract(t);

    return 1.0 - abs(fract(t * 0.5) * 2.0 - 1.0);
}

highp float NowUITextGradientFlags(highp float encodedRamp)
{
    return floor(fract(encodedRamp) * 256.0);
}

highp float NowUITextGradientPosition(
    highp vec2 uiPosition,
    highp vec4 payload,
    highp float flags)
{
    highp float kind = hlslFmod(flags, 4.0);

    if (kind < 0.5)
        return dot(uiPosition, payload.xy) + payload.z;

    if (kind < 1.5)
    {
        highp float circle = hlslFmod(floor(flags / 16.0), 2.0);
        highp vec2 radii = circle > 0.5 ? payload.zz : payload.zw;
        return length((uiPosition - payload.xy) / max(abs(radii), vec2(0.0001)));
    }

    highp vec2 delta = uiPosition - payload.xy;
    highp float turns = atan(delta.x, -delta.y) / 6.28318530718;
    return fract(turns - payload.z) * payload.w;
}

highp vec4 NowUITextGradientSample(
    highp vec2 uiPosition,
    highp vec4 payload,
    highp float encodedRamp)
{
    highp float row = floor(encodedRamp);
    highp float flags = NowUITextGradientFlags(encodedRamp);
    highp float spread = hlslFmod(floor(flags / 4.0), 4.0);
    highp float fixedMode = hlslFmod(floor(flags / 32.0), 2.0);
    highp float t = NowUITextGradientApplySpread(
        NowUITextGradientPosition(uiPosition, payload, flags),
        spread);

    highp float rampIndex = fixedMode > 0.5
        ? floor(t * 255.0 + 0.5)
        : t * 255.0;
    highp vec2 rampUV = vec2(
        (rampIndex + 0.5) / 256.0,
        (row + 0.5) / 256.0);
    highp vec4 ramp = texture(_NowGradientRampTexture, rampUV);

    ramp.rgb = NowUIColorToWorkingSpace(ramp.rgb);
    return ramp;
}

#endif
`;

// NowUI/Text Renderer — Assets/NowUI/Assets/Shaders/TxtRenderer.shader, one pass.
//
// The gradient branch (extras.w > 0.0) is PORTED. It needs the colour-space include as well as the mask one,
// because it converts a ramp texel that did not exist at vertex time — the same reason nowui-gradient.frag
// needs it. Order matters: GLSL_COLOR_SPACE before GLSL_TEXT_GRADIENT.
const GLSL_FRAGMENT_TEXT = `#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform highp sampler2D _MainTex;
uniform float _NowUITextSdfEncoding;
${GLSL_VARYINGS_IN}
${GLSL_COLOR_SPACE}
${GLSL_TEXT_GRADIENT}
${GLSL_MASK}
out vec4 fragColor;

float median(float r, float g, float b)
{
    return max(min(r, g), min(max(r, g), b));
}

void main()
{
    vec4 rect = vRect;
    vec4 mask = vMask;

    vec2 pos = rect.xy + vRawUV.xy * rect.zw;
    vec2 uiPosition = vec2(pos.x, -pos.y);

    if (NowUILegacyRectDistance(uiPosition, mask) < 0.0)
        discard;

    float outline = vExtras.x;
    vec4 msd = texture(_MainTex, vUv);

    // extras.y is the distance-field range in local units; convert to screen pixels so canvas scale and
    // transform scale keep text crisp.
    vec2 gradX = vec2(dFdx(pos.x), dFdy(pos.x));
    vec2 gradY = vec2(dFdx(pos.y), dFdy(pos.y));
    float unitsPerPixel = max(0.5 * (length(gradX) + length(gradY)), 1e-5);
    bool outlineOnly = vExtras.y < 0.0;
    float screenPxRange = max(abs(vExtras.y) / unitsPerPixel, 1.0);

    // Packed SDF16 in R+B is the branch this build takes; the managed baker sets the uniform to 1 before the
    // first text draw. The median branch renders text, but blurry and subtly wrong.
    bool packedSdf16 = _NowUITextSdfEncoding > 0.5;
    float sd = packedSdf16
        ? (msd.r * 256.0 + msd.b) / 257.0
        : median(msd.r, msd.g, msd.b);

    float screenPxDistance = screenPxRange * (sd - 0.5);
    float outlineSd = (outline == 0.0 || packedSdf16) ? sd : msd.a;
    float screenPxDistanceOutline = screenPxRange * (outlineSd - 0.5) + outline / unitsPerPixel;

    float distanceCodeCount = packedSdf16 ? 65535.0 : 255.0;
    float aaWidth = max(1.0, screenPxRange / distanceCodeCount);
    float opacity = clamp(screenPxDistance / aaWidth + 0.5, 0.0, 1.0);
    float outlineOp = clamp(screenPxDistanceOutline / aaWidth + 0.5, 0.0, 1.0);

    // extras.w is the encoded ramp; 0 means "no gradient" and must not enter, because row 0 of the atlas is a
    // magenta/black checker rather than a usable ramp. The compound multiply is a multiply, not a replace: the
    // glyph's flat colour still tints the sampled ramp and its alpha still scales the ramp's.
    vec4 fillColor = vColor;

    if (vExtras.w > 0.0)
    {
        vec4 gradientPayload = vec4(vRadius.xyz, vExtras.z);
        fillColor *= NowUITextGradientSample(uiPosition, gradientPayload, vExtras.w);
    }

    vec4 color;

    if (outlineOnly)
    {
        float remainingFill = max(1.0 - opacity, 1e-5);
        float ringCoverage = clamp((outlineOp - opacity) / remainingFill, 0.0, 1.0);
        color = vOutline;
        color.a *= ringCoverage;
    }
    else
    {
        color = outline == 0.0
            ? fillColor
            : mix(vOutline, fillColor, outline < 0.0 ? outlineOp : opacity);
        color.a *= max(opacity, outlineOp);
    }

    // Note the asymmetry with UIRectangle: this shader applies mask coverage to ALPHA ONLY and premultiplies
    // afterwards. Both are correct for their own shader; do not unify them.
    color.a *= NowUIMaskCoverage(uiPosition);
    color.rgb *= color.a;

    // TxtRenderer has no clip(col.a - 0.001). It returns fully transparent fragments and relies on the blend.
    fragColor = color;
}
`;

// ---------------------------------------------------------------------------------------------- gradient
//
// NowUI/UI Gradient — Assets/NowUI/Assets/Shaders/UIGradient.shader, one pass. The annotated port, with the
// full derivation and the HLSL line citations, lives in wwwroot/shaders/nowui-gradient.{vert,frag}; this is the
// same GLSL, kept here because this file is what actually reaches gl.shaderSource.
//
// This program needs its OWN vertex shader, and the reason is a trap rather than a preference:
//   * TEXCOORD0 is `packedTint` (two 16-bit integers), NOT a texture UV. Running TRANSFORM_TEX over it — which
//     GLSL_VERTEX does — is the identity only while _MainTex_ST stays (1,1,0,0). So no _MainTex_ST here.
//   * TEXCOORD3 is the gradient PARAMETER payload, not a colour, and must not go through
//     NowUIColorToWorkingSpace. That is the identity under Gamma and wrong the day a linear slice lands.
//   * rawUV is narrowed to a vec2 by the HLSL v2f.
const GLSL_VERTEX_GRADIENT = `#version 300 es
precision highp float;
precision highp int;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;
layout(location = 3) in vec4 aRadius;
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;
layout(location = 6) in vec4 aExtras;
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;

uniform mat4 nowui_MatrixMVP;

out vec2 vPackedTint;
out vec4 vRect;
out vec4 vRadius;
out vec4 vGradient;
out vec4 vOutlineColor;
out vec4 vExtras;
out vec4 vMask;
out vec2 vRawUV;
${GLSL_COLOR_SPACE}
void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);
    vPackedTint = aUv;          // RAW. No TRANSFORM_TEX: these are packed integers, not UVs.
    vRect = aRect;
    vRadius = aRadius;
    vGradient = aColor;         // data, not colour — deliberately NOT converted
    vOutlineColor = NowUIColorToWorkingSpace(aOutline);
    vExtras = aExtras;
    vMask = aMask;
    vRawUV = aRawUV.xy;
}
`;

const GLSL_VARYINGS_IN_GRADIENT = `
in vec2 vPackedTint;
in vec4 vRect;
in vec4 vRadius;
in vec4 vGradient;
in vec4 vOutlineColor;
in vec4 vExtras;
in vec4 vMask;
in vec2 vRawUV;
`;

const GLSL_FRAGMENT_GRADIENT = `#version 300 es
precision highp float;
precision highp int;

// _MainTex is the 256x256 RAMP ATLAS, not a fill texture, and _NowGradientRampTexelSize is (1/w, 1/h, w, h) of
// it. The backend must never leave the texel size at GL's all-zero default: y = (row + 0.5) * texelSize.y
// becomes 0, so every gradient samples ROW 0 of the atlas instead of its own -- a smooth, plausible sweep of
// the wrong ramp. (The fixed-step branch also collapses to column 0; the smooth branch's x survives by
// accident, which is what makes the row error easy to miss.)
uniform highp sampler2D _MainTex;
uniform vec4 _NowGradientRampTexelSize;
${GLSL_VARYINGS_IN_GRADIENT}
${GLSL_COLOR_SPACE}
${GLSL_MASK}
out vec4 fragColor;

// HLSL fmod truncates toward zero and keeps the sign of the dividend; GLSL mod floors. Every operand here is
// non-negative, where the two agree — but the next port will have a negative one, so the helper is written out.
float hlslFmod(float x, float y)
{
    return x - y * trunc(x / y);
}

float sdRoundedBox(vec2 p, vec2 b, vec4 r)
{
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    vec2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, vec2(0.0))) - r.x;
}

// Two 8-bit channels packed as high * 256 + low. Divided by 255, not 256, so a stored 255 lands on exactly 1.0.
vec2 decodePair8(float packed)
{
    packed = floor(packed + 0.5);
    float first = floor(packed / 256.0);
    float second = packed - first * 256.0;
    return vec2(first, second) / 255.0;
}

// 0 clamp, 1 repeat, 2 mirror.
float applySpread(float t, float spread)
{
    if (spread < 0.5)
        return clamp(t, 0.0, 1.0);

    if (spread < 1.5)
        return fract(t);

    return 1.0 - abs(fract(t * 0.5) * 2.0 - 1.0);
}

// uv is uiUV (y DOWN), size is rect.zw. kind: 0 linear, 1 radial, 2 angular.
float gradientPosition(vec2 uv, vec2 size, vec4 data, float kind, float circle)
{
    if (kind < 0.5)
    {
        vec2 direction = data.xy;
        float directionLength = max(length(direction), 0.0001);
        direction /= directionLength;
        vec2 local = (uv - 0.5) * size;
        // The half-width of the rect measured ALONG the direction, so t reaches 0 and 1 on the silhouette.
        float extent = max(
            abs(direction.x) * size.x * 0.5 + abs(direction.y) * size.y * 0.5,
            0.0001);
        return (0.5 + dot(local, direction) / (2.0 * extent)) * max(abs(data.z), 0.0001);
    }

    if (kind < 1.5)
    {
        vec2 delta = uv - data.xy;

        if (circle > 0.5)
        {
            float radius = max(abs(data.z) * min(size.x, size.y), 0.0001);
            return length(delta * size) / radius;
        }

        // HLSL broadcasts the scalar into a float2 max; GLSL will not. This is the one line in the shader that
        // changes meaning if translated literally.
        return length(delta / max(abs(data.zw), vec2(0.0001)));
    }

    vec2 delta = uv - data.xy;
    // HLSL atan2(0, 0) is defined to be 0; GLSL atan(0, 0) is UNDEFINED and may be NaN, which would paint one
    // garbage pixel at the exact centre of every conic gradient. Guarding it moves this CLOSER to Unity.
    vec2 d = vec2(delta.x, -delta.y);
    float turns = (d.x == 0.0 && d.y == 0.0) ? 0.0 : atan(d.x, d.y) / 6.28318530718;
    return fract(turns - data.z) * max(abs(data.w), 0.0001);
}

// encodedRamp packs the atlas ROW in its integer part and the mode flags in its fraction. Note the second
// divide shifts the ALREADY-SHIFTED value: spread is bits 2..3 of the byte and fixedMode is bit 5. The
// kind/circle decode in main() reads the SAME byte unshifted. Both are reproduced where the HLSL puts them
// rather than unified, because unifying them is exactly how the shift order gets lost.
vec4 sampleRamp(float t, float encodedRamp)
{
    float row = floor(encodedRamp);
    float flags = floor(fract(encodedRamp) * 256.0);
    flags = floor(flags / 4.0);
    float spread = hlslFmod(flags, 4.0);
    flags = floor(flags / 8.0);
    float fixedMode = hlslFmod(flags, 2.0);

    t = applySpread(t, spread);
    float x;

    if (fixedMode > 0.5)
    {
        float index = floor(t * (_NowGradientRampTexelSize.z - 1.0) + 0.5);
        x = (index + 0.5) * _NowGradientRampTexelSize.x;
    }
    else
    {
        // First texel centre to last texel centre, so LINEAR filtering never bleeds the neighbouring row in.
        x = mix(
            0.5 * _NowGradientRampTexelSize.x,
            1.0 - 0.5 * _NowGradientRampTexelSize.x,
            t);
    }

    float y = (row + 0.5) * _NowGradientRampTexelSize.y;
    return texture(_MainTex, vec2(x, y));
}

void main()
{
    vec2 pos = vRect.xy + vRawUV * vRect.zw;
    vec2 uiPosition = vec2(pos.x, -pos.y);

    // NowUIClipLegacyRect. HLSL clip() discards on strictly negative, so an exact 0.0 survives.
    if (NowUILegacyRectDistance(uiPosition, vMask) < 0.0)
        discard;

    // The gradient's own coordinate is y-DOWN, because its parameters are authored in UI space. \`position\`
    // below is the y-UP SDF space. Two different spaces; both correct.
    vec2 uiUV = vec2(vRawUV.x, 1.0 - vRawUV.y);

    vec2 position = (vRawUV - 0.5) * vRect.zw;
    float dist = sdRoundedBox(position, vRect.zw * 0.5, vRadius);
    float delta = max(length(vec2(dFdx(dist), dFdy(dist))), 0.0001);
    float aa = 0.5 * delta;
    float graphicAlpha = 1.0 - smoothstep(-aa, aa + max(vExtras.x, 0.0), dist);

    float outlineWidth = max(vExtras.y, delta);
    float outlineAlpha = vExtras.y == 0.0
        ? 0.0
        : smoothstep(-outlineWidth - aa, -outlineWidth + aa, dist);

    float flags = floor(fract(vExtras.w) * 256.0);
    float kind = hlslFmod(flags, 4.0);
    float circle = hlslFmod(floor(flags / 16.0), 2.0);
    float t = gradientPosition(uiUV, vRect.zw, vGradient, kind, circle);
    vec4 ramp = sampleRamp(t, vExtras.w);

    vec2 rg = decodePair8(vPackedTint.x);
    vec2 ba = decodePair8(vPackedTint.y);
    vec4 tint = vec4(rg.x, rg.y, ba.x, ba.y);

    // The colour-space conversion happens HERE, not in the vertex stage: neither the sampled ramp nor the
    // unpacked tint existed there.
    float outlineCoverage = vOutlineColor.a * outlineAlpha * graphicAlpha;
    float fillCoverage = ramp.a * tint.a * graphicAlpha;
    vec3 fillColor =
        NowUIColorToWorkingSpace(ramp.rgb) *
        NowUIColorToWorkingSpace(tint.rgb) *
        fillCoverage;

    vec4 col;
    col.rgb = vOutlineColor.rgb * outlineCoverage + fillColor * (1.0 - outlineCoverage);
    col.a = outlineCoverage + fillCoverage * (1.0 - outlineCoverage);

    col *= NowUIMaskCoverage(uiPosition);

    if (col.a - 0.001 < 0.0)
        discard;

    fragColor = col;
}
`;

// ---------------------------------------------------------------------------------------------- ripple
//
// NowUI/UI Ripple — Assets/NowUI/Assets/Shaders/UIRipple.shader, one pass. Annotated port in
// wwwroot/shaders/nowui-ripple.{vert,frag}.
//
// A ripple is the INTERSECTION of two coverages: the host control's rounded rect and an expanding circle centred
// on the pointer. It declares no sampler and no _MainTex at all, so its vertex stage has no _MainTex_ST and its
// fragment stage's only uniforms are the mask include's.
const GLSL_VERTEX_RIPPLE = `#version 300 es
precision highp float;
precision highp int;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;
layout(location = 3) in vec4 aRadius;
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;
layout(location = 6) in vec4 aExtras;
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;

uniform mat4 nowui_MatrixMVP;

out vec4 vRect;
out vec4 vRadius;
out vec4 vColor;
out vec4 vExtras;
out vec4 vMask;
out vec4 vRawUV;
${GLSL_COLOR_SPACE}
void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);
    vRect = aRect;
    vRadius = aRadius;
    vColor = NowUIColorToWorkingSpace(aColor);
    // extras is the ripple CIRCLE, not (blur, outline, ...): .xy is its centre in UI coordinates and .w its
    // radius, written by NowRipple as Vector4(origin.x, origin.y, 0, circleRadius).
    vExtras = aExtras;
    vMask = aMask;
    vRawUV = aRawUV;
}
`;

const GLSL_VARYINGS_IN_RIPPLE = `
in vec4 vRect;
in vec4 vRadius;
in vec4 vColor;
in vec4 vExtras;
in vec4 vMask;
in vec4 vRawUV;
`;

const GLSL_FRAGMENT_RIPPLE = `#version 300 es
precision highp float;
precision highp int;
${GLSL_VARYINGS_IN_RIPPLE}
${GLSL_MASK}
out vec4 fragColor;

float sdRoundedBox(vec2 p, vec2 b, vec4 r)
{
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    vec2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, vec2(0.0))) - r.x;
}

void main()
{
    vec4 rect = vRect;
    vec4 mask = vMask;
    vec2 rawUV = vRawUV.xy;
    vec2 pos = rect.xy + rawUV * rect.zw;
    vec2 uiPosition = vec2(pos.x, -pos.y);

    if (NowUILegacyRectDistance(uiPosition, mask) < 0.0)
        discard;

    // Coverage 1: the control's rounded rect, in full-quad space. No blur term and no outline — extras means
    // something else in this shader.
    vec2 centered = (rawUV - 0.5) * rect.zw;
    float shapeDist = sdRoundedBox(centered, rect.zw * 0.5, vRadius);
    float shapeDelta = max(length(vec2(dFdx(shapeDist), dFdy(shapeDist))), 0.0001);
    float shapeAlpha = 1.0 - smoothstep(-0.5 * shapeDelta, 0.5 * shapeDelta, shapeDist);

    // Coverage 2: the expanding circle. extras.xy is its centre in UI coordinates — which is why it is compared
    // against uiPosition and not against \`centered\` — and extras.w its radius in UI units.
    float circleDist = length(uiPosition - vExtras.xy) - vExtras.w;
    float circleDelta = max(length(vec2(dFdx(circleDist), dFdy(circleDist))), 0.0001);
    float circleAlpha = 1.0 - smoothstep(-0.5 * circleDelta, 0.5 * circleDelta, circleDist);

    // They MULTIPLY: a circle that has grown past the control's rounded corner is trimmed by the corner.
    float alpha = vColor.a * shapeAlpha * circleAlpha;
    vec4 col;
    col.rgb = vColor.rgb * alpha;
    col.a = alpha;

    col *= NowUIMaskCoverage(uiPosition);

    if (col.a - 0.001 < 0.0)
        discard;

    fragColor = col;
}
`;

// ---------------------------------------------------------------------------------------------- glass
//
// NowUI/UI Glass — Assets/NowUI/Assets/Shaders/UIGlass.shader, one pass. Annotated port, with the reachability
// analysis for every branch, in wwwroot/shaders/nowui-glass.{vert,frag}.
//
// THIS IS THE ONLY PROGRAM WITH ITS OWN BLEND STATE. UIGlass.shader:35 declares
//     Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
// — separate colour and alpha, with the colour half NON-premultiplied, because this is the one NowUI fragment
// stage that divides its accumulated colour back out by coverage (:257). Drawing it with the shared premultiplied
// blend darkens the panel by exactly its own alpha: a plausible panel, and the wrong one. See BLEND_STATES.
//
// WHAT IS REACHABLE. Now.StartUI drives the immediate path, and Now.cs:1155 calls
// NowGlassRenderer.DisableBackdropGlobal() for every NowMeshKind.Glass mesh on that path — so
// _NowGlassUseBackdrop is 0 and glass in the browser is a translucent tinted rounded rect with an outline,
// exactly as Unity's own immediate path draws it. The flat-2D backdrop branch is ported and waiting for the
// retained NowRenderer path; the stereo/texture-array branch and the scene-depth variant are not, and the
// fragment stage paints magenta if either is ever selected rather than silently drawing the wrong half.
const GLSL_VERTEX_GLASS = `#version 300 es
precision highp float;
precision highp int;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;
layout(location = 3) in vec4 aRadius;
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;
layout(location = 6) in vec4 aExtras;
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;

uniform mat4 nowui_MatrixMVP;

out vec4 vScreenPos;
out vec4 vRect;
out vec4 vRadius;
out vec4 vColor;
out vec4 vOutlineColor;
out vec4 vExtras;
out vec4 vMask;
out vec4 vRawUV;
${GLSL_COLOR_SPACE}
void main()
{
    vec4 clipPos = nowui_MatrixMVP * vec4(aPosition, 1.0);
    gl_Position = clipPos;

    // ComputeScreenPos: o.xy = 0.5*pos.xy + 0.5*pos.w, o.zw = pos.zw, so screenUV = o.xy/o.w = 0.5*ndc + 0.5.
    // _ProjectionParams.x is +1 and is hard-coded as such: Unity sets it to -1 only for a FLIPPED projection,
    // which happens on the D3D-style platforms whose render textures have a top-left origin. WebGL2 is OpenGL
    // and the projection is never flipped.
    vScreenPos = vec4(clipPos.xy * 0.5 + clipPos.w * 0.5, clipPos.zw);

    vRect = aRect;
    vRadius = aRadius;
    vColor = NowUIColorToWorkingSpace(aColor);
    vOutlineColor = NowUIColorToWorkingSpace(aOutline);
    // extras is (blurRadius, outlineWidth, saturation, brightness). .x is a CPU-side batch-key input only.
    vExtras = aExtras;
    vMask = aMask;
    vRawUV = aRawUV;
}
`;

const GLSL_VARYINGS_IN_GLASS = `
in vec4 vScreenPos;
in vec4 vRect;
in vec4 vRadius;
in vec4 vColor;
in vec4 vOutlineColor;
in vec4 vExtras;
in vec4 vMask;
in vec4 vRawUV;
`;

const GLSL_FRAGMENT_GLASS = `#version 300 es
precision highp float;
precision highp int;
${GLSL_VARYINGS_IN_GLASS}
${GLSL_MASK}
uniform highp sampler2D _NowBackdropTex;
uniform float _NowGlassUseBackdrop;
uniform float _NowGlassUseStereoBackdrop;
uniform vec4 _NowBackdropUVTransform;
uniform float _NowMaterialGlassMode;

out vec4 fragColor;

float sdRoundedBox(vec2 p, vec2 b, vec4 r)
{
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    vec2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, vec2(0.0))) - r.x;
}

void main()
{
    vec4 rect = vRect;
    vec4 mask = vMask;
    vec2 rawUV = vRawUV.xy;
    vec2 pos = rect.xy + rawUV * rect.zw;
    vec2 uiPosition = vec2(pos.x, -pos.y);

    // NowUIClipLegacyRect. HLSL clip() discards on strictly negative, so an exact 0.0 survives. This file's
    // GLSL_MASK exposes only the DISTANCE function, unlike wwwroot/shaders/nowui-mask.glsl which also wraps it
    // as NowUIClipLegacyRect; calling the wrapper here is a compile error, which is how the divergence was found.
    if (NowUILegacyRectDistance(uiPosition, mask) < 0.0)
        discard;

    // NOTE: no max(..., 1e-4) floor on delta, unlike UIRectangle. That is faithful — UIGlass does not clamp it,
    // and clamping here would make the browser and Unity disagree on the one input where they currently agree
    // to be undefined together.
    vec2 size = rect.zw;
    vec2 position = (rawUV - 0.5) * size;
    float dist = sdRoundedBox(position, size * 0.5, vRadius);
    float delta = length(vec2(dFdx(dist), dFdy(dist)));
    float aa = 0.5 * delta;
    float graphicAlpha = 1.0 - smoothstep(-aa, aa, dist);

    float outline = vExtras.y;
    float outlineWidth = max(outline, delta);
    float outlineAlpha = (outline == 0.0) ? 0.0 : smoothstep(-outlineWidth - aa, -outlineWidth + aa, dist);

    float saturation = vExtras.z;
    float brightness = vExtras.w;
    vec4 tint = vColor;
    vec3 fillRgb = tint.rgb;
    float fillCoverage = tint.a;
    vec2 screenUV = vScreenPos.xy / vScreenPos.w;

    bool useMaterialBackdrop = _NowMaterialGlassMode > 0.5;
    bool useBackdrop = _NowGlassUseBackdrop > 0.5;

    if (useBackdrop)
    {
        vec4 uvTransform = _NowBackdropUVTransform;
        vec2 clampedBackdropUV = clamp(screenUV * uvTransform.xy + uvTransform.zw, 0.0, 1.0);
        vec4 backdrop = texture(_NowBackdropTex, clampedBackdropUV);
        float luminance = dot(backdrop.rgb, vec3(0.299, 0.587, 0.114));
        backdrop.rgb = mix(vec3(luminance), backdrop.rgb, saturation) * brightness;
        fillRgb = mix(backdrop.rgb, tint.rgb, tint.a);
        fillCoverage = 1.0;
    }

    // The two unported branches, marked UNCONDITIONALLY. Both flags are the constant 0 in this build — their
    // only writer, NowWorldGraphic.cs, is on NowUI.Runtime.csproj's exclude list, and _NowGlassUseStereoBackdrop
    // needs an XR eye texture — so this costs two comparisons and never changes a pixel. It is NOT behind a
    // #ifdef, because guarding it makes both uniforms write-only and the compiler then eliminates them: the
    // assertion would vanish in exactly the configuration it guards. Delete this block as part of porting
    // either branch.
    if (useMaterialBackdrop || _NowGlassUseStereoBackdrop > 0.5)
    {
        fragColor = vec4(1.0, 0.0, 1.0, 1.0);
        return;
    }

    float outlineCoverage = vOutlineColor.a * outlineAlpha;
    float coverage = outlineCoverage + fillCoverage * (1.0 - outlineCoverage);
    vec3 rgb = fillRgb;

    if (coverage > 0.0001)
        rgb = (vOutlineColor.rgb * outlineCoverage + fillRgb * fillCoverage * (1.0 - outlineCoverage)) / coverage;

    // STRAIGHT rgb, not premultiplied — see the blend note above. Mask coverage multiplies ALPHA ONLY here,
    // unlike UIRectangle/UIGradient/UIRipple which scale an already-premultiplied vec4. Do not unify them.
    vec4 col = vec4(rgb, coverage * graphicAlpha);
    col.a *= NowUIMaskCoverage(uiPosition);

    if (col.a - 0.001 < 0.0)
        discard;

    fragColor = col;
}
`;

// ---------------------------------------------------------------------------------------------- colour picker
//
// NowUI/Color Picker — Assets/NowUI/Assets/Shaders/UIColorPicker.shader, one pass. Annotated port in
// wwwroot/shaders/nowui-colorpicker.{vert,frag}.
//
// Three unrelated pickers behind one `_Mode` uniform, one material per mode. NowValueControls.cs:935-954 builds
// them lazily with `new Material(Shader.Find("NowUI/Color Picker"))` plus a single SetFloat(_Mode, mode), so
// _Mode arrives through the MATERIAL bag and there is no ColorPickerMaterial asset in the fixtures.
//
// THE TRAP: the vertex stage does NOT run NowUIColorToWorkingSpace over TEXCOORD3, and that is deliberate in the
// HLSL (:86). On this shader TEXCOORD3 is a PARAMETER — mode 0 reads .r as a HUE ANGLE — so converting it would
// be the identity under Gamma and a hue shift under linear. The conversion happens once on the final rgb (:120).
const GLSL_VERTEX_COLOR_PICKER = `#version 300 es
precision highp float;
precision highp int;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;
layout(location = 3) in vec4 aRadius;
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;
layout(location = 6) in vec4 aExtras;
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;

uniform mat4 nowui_MatrixMVP;

out vec4 vRect;
out vec4 vColor;
out vec4 vMask;
out vec4 vRawUV;

void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);
    vRect = aRect;
    // NOT NowUIColorToWorkingSpace — see the note above this program.
    vColor = aColor;
    vMask = aMask;
    vRawUV = aRawUV;
}
`;

const GLSL_VARYINGS_IN_COLOR_PICKER = `
in vec4 vRect;
in vec4 vColor;
in vec4 vMask;
in vec4 vRawUV;
`;

const GLSL_FRAGMENT_COLOR_PICKER = `#version 300 es
precision highp float;
precision highp int;
${GLSL_VARYINGS_IN_COLOR_PICKER}
${GLSL_COLOR_SPACE}
${GLSL_MASK}
uniform float _Mode;

out vec4 fragColor;

// HLSL fmod truncates toward zero and keeps the dividend's sign; GLSL mod floors. They agree only for
// non-negative operands, which every call here has — but the next port will not, and reaching for mod there is
// a bug that produces a plausible picture.
float hlslFmod(float x, float y) { return x - y * trunc(x / y); }

// HLSL's k.xxx is a SCALAR swizzle; GLSL has none, so it must be vec3(k.x).
vec3 HsvToRgb(float h, float s, float v)
{
    vec4 k = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(vec3(h) + k.xyz) * 6.0 - vec3(k.w));
    return v * mix(vec3(k.x), clamp(p - vec3(k.x), 0.0, 1.0), s);
}

vec3 Checker(vec2 rawUV, vec2 size)
{
    vec2 pixel = rawUV * size;
    float checker = hlslFmod(floor(pixel.x / 5.0) + floor(pixel.y / 5.0), 2.0);
    return mix(vec3(0.88, 0.90, 0.93), vec3(0.68, 0.72, 0.78), checker);
}

void main()
{
    vec4 rect = vRect;
    vec4 mask = vMask;
    // The saturate on :97 is unique to this shader and must be kept: the output is a FUNCTION of rawUV rather
    // than a shape SDF, so geometry padding pushing it outside [0,1] would wrap the hue past the end of the
    // wheel. Do not copy this line into the other ports, and do not remove it from this one.
    vec2 rawUV = clamp(vRawUV.xy, 0.0, 1.0);
    vec2 pos = rect.xy + rawUV * rect.zw;
    vec2 uiPosition = vec2(pos.x, -pos.y);

    // NowUIClipLegacyRect. HLSL clip() discards on strictly negative, so an exact 0.0 survives. This file's
    // GLSL_MASK exposes only the DISTANCE function, unlike wwwroot/shaders/nowui-mask.glsl which also wraps it
    // as NowUIClipLegacyRect; calling the wrapper here is a compile error, which is how the divergence was found.
    if (NowUILegacyRectDistance(uiPosition, mask) < 0.0)
        discard;

    int mode = int(_Mode + 0.5);
    vec3 rgb;

    if (mode == 1)
        rgb = HsvToRgb(1.0 - rawUV.y, 1.0, 1.0);      // hue strip; rawUV.y == 1 is the UI top, so red is on top
    else if (mode == 2)
        rgb = mix(Checker(rawUV, rect.zw), vColor.rgb, rawUV.x);
    else
        rgb = HsvToRgb(vColor.r, rawUV.x, rawUV.y);   // color.r is the HUE

    vec4 col = vec4(NowUIColorToWorkingSpace(rgb), 1.0);
    // Alpha is seeded to 1, so this scalar multiply turns straight rgb into correctly premultiplied output.
    col *= NowUIMaskCoverage(uiPosition);

    if (col.a - 0.001 < 0.0)
        discard;

    fragColor = col;
}
`;

// ---------------------------------------------------------------------------------------------- bezier
//
// NowUI/UI Bezier — Assets/NowUI/Assets/Shaders/UIBezier.shader, one pass. Annotated port in
// wwwroot/shaders/nowui-bezier.{vert,frag}.
//
// The one NowUI program that keeps the TRUE cubic: four Newton steps per fragment on f(t) = dot(B(t)-p, B'(t))
// find the nearest point on the curve itself, so the stroke's edge is analytically antialiased and the CPU-side
// flattening only has to COVER the stroke rather than BE it.
//
// IT REUSES THE NINE STREAMS FOR DIFFERENT DATA (NowLine.cs:530-551):
//   TEXCOORD1 cp01 = (p0.xy, p1.xy)        TEXCOORD2 cp23 = (p2.xy, p3.xy) — NOT radii
//   TEXCOORD5 params = (halfWidth, aaWidth, t, 0)
//   TEXCOORD7 pixel  = (uiPos.x, uiPos.y)  — ALREADY UI space, y down, WITHOUT the usual negation
//   TEXCOORD0 and TEXCOORD4 are written as zero and never read
// That last one is the one to hold on to: there is no `uiPosition = vec2(pos.x, -pos.y)` in this shader, and
// adding one would mirror every curve about y = 0, off screen.
const GLSL_VERTEX_BEZIER = `#version 300 es
precision highp float;
precision highp int;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;      // cp01
layout(location = 3) in vec4 aRadius;    // cp23
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;   // unused1
layout(location = 6) in vec4 aExtras;    // params
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;     // pixel, in UI space

uniform mat4 nowui_MatrixMVP;

out vec4 vCp01;
out vec4 vCp23;
out vec4 vColor;
out vec4 vParams;
out vec4 vMask;
out vec2 vPixel;
${GLSL_COLOR_SPACE}
void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);
    vCp01 = aRect;
    vCp23 = aRadius;
    // TEXCOORD3 really is a colour here, unlike UIColorPicker, so the conversion belongs in the vertex stage.
    vColor = NowUIColorToWorkingSpace(aColor);
    vParams = aExtras;
    vMask = aMask;
    vPixel = aRawUV.xy;
}
`;

const GLSL_VARYINGS_IN_BEZIER = `
in vec4 vCp01;
in vec4 vCp23;
in vec4 vColor;
in vec4 vParams;
in vec4 vMask;
in vec2 vPixel;
`;

const GLSL_FRAGMENT_BEZIER = `#version 300 es
precision highp float;
precision highp int;
${GLSL_VARYINGS_IN_BEZIER}
${GLSL_MASK}
out vec4 fragColor;

void main()
{
    vec2 p0 = vCp01.xy;
    vec2 p1 = vCp01.zw;
    vec2 p2 = vCp23.xy;
    vec2 p3 = vCp23.zw;

    // Bernstein to power basis: B(t) = a*t^3 + b*t^2 + c*t + d. highp matters: the coefficient a is a four-term alternating
    // sum of control points that can be hundreds of UI units apart.
    vec2 d = p0;
    vec2 c = 3.0 * (p1 - p0);
    vec2 b = 3.0 * (p0 - 2.0 * p1 + p2);
    vec2 a = p3 - 3.0 * p2 + 3.0 * p1 - p0;

    float halfWidth = vParams.x;
    float aaWidth = vParams.y;
    float t = vParams.z;
    vec2 pixel = vPixel;   // already UI space — no negation, see the note above this program

    for (int k = 0; k < 4; ++k)
    {
        float t2 = t * t;
        vec2 Bt = a * t2 * t + b * t2 + c * t + d;
        vec2 B1 = 3.0 * a * t2 + 2.0 * b * t + c;
        vec2 B2 = 6.0 * a * t + 2.0 * b;
        vec2 diff = Bt - pixel;
        float f = dot(diff, B1);
        float fp = dot(B1, B1) + dot(diff, B2);
        t -= f / (fp + 1e-5);
        t = clamp(t, 0.0, 1.0);
    }

    vec2 closest = ((a * t + b) * t + c) * t + d;
    float dist = length(closest - pixel);
    float coverage = clamp((halfWidth + aaWidth - dist) / (2.0 * aaWidth), 0.0, 1.0);

    // The legacy clip sits AFTER the Newton loop here, not before it as in every other NowUI fragment stage.
    // That is what the HLSL does (:124), it changes no pixel, and reordering it would be an optimisation
    // dressed as a port.
    vec4 mask = vMask;
    // NowUIClipLegacyRect, with vPixel used as UI space directly -- see the note above this program.
    if (NowUILegacyRectDistance(pixel, mask) < 0.0)
        discard;

    if (coverage - 0.001 < 0.0)
        discard;

    float alpha = vColor.a * coverage;
    vec4 col;
    col.rgb = vColor.rgb * alpha;
    col.a = alpha;
    col *= NowUIMaskCoverage(pixel);
    fragColor = col;
}
`;

// ---------------------------------------------------------------------------------------------- glass blur
//
// Hidden/NowUI/GlassBlur PASS 0 — Assets/NowUI/Assets/Shaders/UIGlassBlur.shader. Annotated port, with the
// reachability analysis for all four passes, in wwwroot/shaders/nowui-glassblur.{vert,frag}.
//
// ONLY PASS 0 IS DECLARED, and the `passes` array below is length one on purpose: selectPass then fails loudly
// for 1..3 rather than quietly drawing pass 0 instead.
//   pass 1  the texture-array blur — expressible on WebGL2 (sampler2DArray exists) but UNREACHABLE: BlitBlur
//           picks it only when the capture descriptor says Tex2DArray, i.e. an XR eye texture.
//   pass 2  the MSAA texture-array resolve, and
//   pass 3  the MSAA 2D resolve — NOT PORTABLE AT ALL. Both read Texture2DMS/Texture2DMSArray with .Load(pixel,
//           sample). GLSL ES 3.00 has no sampler2DMS; multisampled samplers arrive in ES 3.10, which WebGL2 does
//           not expose and no extension adds. A WebGL2 multisampled renderbuffer can only be resolved with
//           blitFramebuffer, never sampled.
//
// It is drawn through blit(), so its vertex stage follows the BLIT GEOMETRY CONTRACT (attributes 0 and 1, the
// unit-quad ortho in nowui_MatrixMVP, the blit's scale/offset in _MainTex_ST) rather than the nine-stream UI
// layout. The source text is a copy of GLSL_VERTEX_BLIT rather than a reference to it, because PROGRAM_SOURCES
// is evaluated far above that constant's declaration and a reference would hit the temporal dead zone.
//
// BLENDING IS OFF for this pass: UIGlassBlur declares no Blend directive at all (Unity's opaque default), and
// the blur ping-pongs between two pooled targets across iterations, so a composited pass would accumulate.
// blit() already disables BLEND for its own reasons, and BLEND_STATES records the requirement independently.
const GLSL_VERTEX_GLASS_BLUR = `#version 300 es
precision highp float;
precision highp int;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;

uniform mat4 nowui_MatrixMVP;
uniform vec4 _MainTex_ST;

out vec2 vUv;

void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);
    vUv = aUv * _MainTex_ST.xy + _MainTex_ST.zw;
}
`;

const GLSL_FRAGMENT_GLASS_BLUR = `#version 300 es
precision highp float;
precision highp int;

// All four arrive as SHADER GLOBALS (SetGlobalTexture / SetGlobalVector from NowGlassRenderer), never from the
// blur material's bag. NONE has a safe zero default: a zero texel size or direction makes all nine taps land on
// the same texel, so the "blur" becomes a pixel-perfect COPY that reads as "the tint did not take"; a zero
// scale/offset collapses the whole image onto one texel.
uniform highp sampler2D _NowBlurSourceTex;
uniform vec4 _NowBlurTexelSize;
uniform vec4 _NowBlurSourceScaleOffset;
uniform vec2 _NowBlurDirection;

in vec2 vUv;
out vec4 fragColor;

void main()
{
    vec2 uv = vUv * _NowBlurSourceScaleOffset.xy + _NowBlurSourceScaleOffset.zw;
    // The HLSL calls this variable "step", which is a GLSL BUILT-IN function name. Renaming it is the only
    // identifier in this program that does not match the source.
    vec2 stepUV = _NowBlurDirection * _NowBlurTexelSize.xy;

    // Bilinear-optimised 17-tap Gaussian in 9 samples. The fractional offsets and their weights are a MATCHED
    // SET — rounding one, reordering them, or folding the doubled weights into a loop produces a different
    // filter. They also require the source sampler to be LINEAR: with NEAREST every fractional offset collapses
    // onto one of its two texels and the kernel silently becomes a lumpier one that still looks like a blur.
    vec4 col = texture(_NowBlurSourceTex, uv) * 0.1031526189;

    col += texture(_NowBlurSourceTex, uv + stepUV * 1.4765796511) * 0.1910108131;
    col += texture(_NowBlurSourceTex, uv - stepUV * 1.4765796511) * 0.1910108131;
    col += texture(_NowBlurSourceTex, uv + stepUV * 3.4455295350) * 0.1404289078;
    col += texture(_NowBlurSourceTex, uv - stepUV * 3.4455295350) * 0.1404289078;
    col += texture(_NowBlurSourceTex, uv + stepUV * 5.4148988458) * 0.0807154625;
    col += texture(_NowBlurSourceTex, uv - stepUV * 5.4148988458) * 0.0807154625;
    col += texture(_NowBlurSourceTex, uv + stepUV * 7.3849121445) * 0.0362685072;
    col += texture(_NowBlurSourceTex, uv - stepUV * 7.3849121445) * 0.0362685072;

    fragColor = col;
}
`;

// ------------------------------------------------------------------------------------------- NowUI/SDF Scene
//
// The shape-algebra program: Assets/NowUI/Extensions/Sdf/NowSdf.shader over NowSdfShaderV2.cginc (1725 lines,
// the largest single shader in NowUI). Unlike every other program here it is an INTERPRETER -- the C# side packs
// up to 64 shape nodes and 16 layers into nine uniform vec4 arrays and this fragment stage walks them per pixel.
//
// The two constants below are GENERATED from wwwroot/shaders/nowui-sdf.{vert,frag} by tools/embed-shader.py,
// which strips the annotation and splices the GLSL_MASK / GLSL_COLOR_SPACE interpolations in place of the
// //#include markers. EDIT THOSE FILES, NOT THESE STRINGS, and re-run the tool. Every earlier program's embedded
// copy was made by hand; at 1500 lines that stops being viable, and a divergence between the file a reviewer
// reads and the source the browser compiles is exactly the failure class this port exists to avoid.
//
// THREE THINGS THAT ARE NOT LIKE THE OTHER PROGRAMS:
//
//   * Its blend is `SrcAlpha OneMinusSrcAlpha` (NowSdf.shader:58) and its fragment stage returns STRAIGHT alpha.
//     BLEND_STATES below carries it. Under the shared premultiplied blend the whole scene darkens by its own
//     coverage -- a plausible picture, which is the worst kind of wrong.
//   * It reads its OWN uniform block, not the shared one. 480 vec4 of array plus scalars is ~2000 floats, and
//     pushing that through the per-draw block would cost every rectangle draw in the app 8 KB of marshalling for
//     uniforms it does not declare. setSdfUniforms() below is called only before an SDF draw.
//   * It declares two samplers of its own, _SdfImageField and _SdfImageColor, on units 5 and 6.

// GENERATED from wwwroot/shaders/nowui-sdf.vert by tools/embed-shader.py -- edit that file, not this.
const GLSL_VERTEX_SDF = `#version 300 es

precision highp float;
precision highp int;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;
layout(location = 3) in vec4 aRadius;
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;
layout(location = 6) in vec4 aExtras;
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;

${GLSL_COLOR_SPACE}

uniform mat4 nowui_MatrixMVP;

uniform float _NowCanvasLayout;

out highp vec2 vRawUV;
out highp vec4 vRect;
out highp vec4 vMask;
out highp vec4 vTint;
out highp vec4 vSceneMapping;

const vec4 NOWUI_SDF_CANVAS_COLOR_MARKER = vec4(1.0, 0.0, 1.0, 1.0);

void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);

    float isCanvas = step(0.5, _NowCanvasLayout);

    vRawUV = mix(aRawUV.xy, aUv.xy, isCanvas);

    vRect = aRect;

    vMask = mix(aMask, aRadius, isCanvas);

    vTint = mix(aColor, NOWUI_SDF_CANVAS_COLOR_MARKER, isCanvas);

    vSceneMapping = mix(aExtras, aColor, isCanvas);

}
`;

// GENERATED from wwwroot/shaders/nowui-sdf.frag by tools/embed-shader.py -- edit that file, not this.
const GLSL_FRAGMENT_SDF = `#version 300 es

precision highp float;
precision highp int;
precision highp sampler2D;

${GLSL_MASK}

float saturate(float x) { return clamp(x, 0.0, 1.0); }
vec2 saturate(vec2 x) { return clamp(x, vec2(0.0), vec2(1.0)); }
vec3 saturate(vec3 x) { return clamp(x, vec3(0.0), vec3(1.0)); }
vec4 saturate(vec4 x) { return clamp(x, vec4(0.0), vec4(1.0)); }

#define NOW_SDF_MAX_SHAPES 64
#define NOW_SDF_MAX_LAYERS 16

in highp vec2 vRawUV;
in highp vec4 vRect;
in highp vec4 vMask;
in highp vec4 vTint;
in highp vec4 vSceneMapping;

out highp vec4 fragColor;

uniform highp sampler2D _MainTex;
uniform highp sampler2D _SdfImageField;
uniform highp sampler2D _SdfImageColor;
uniform highp vec4 _SdfImageAtlasSize;

uniform highp float _SdfShapeCount;
uniform highp float _SdfLayerCount;
uniform highp float _SdfFeather;

uniform highp float _SdfTextEffectLimit;

uniform highp vec4 _SdfOutline;
uniform highp vec4 _SdfOutlineColor;
uniform highp vec4 _SdfGlow;
uniform highp vec4 _SdfGlowColor;
uniform highp vec4 _SdfShadow;
uniform highp vec4 _SdfShadowColor;
uniform highp vec4 _SdfInnerShadow;
uniform highp vec4 _SdfInnerShadowColor;
uniform highp vec4 _SdfEmboss;
uniform highp vec4 _SdfContour;
uniform highp vec4 _SdfContourColor;
uniform highp vec4 _SdfContourMask;
uniform highp vec4 _SdfWarp;
uniform highp float _SdfMaskOutput;

uniform highp float nowui_Time;

uniform highp vec4 _SdfData0[NOW_SDF_MAX_SHAPES];
uniform highp vec4 _SdfData1[NOW_SDF_MAX_SHAPES];
uniform highp vec4 _SdfData2[NOW_SDF_MAX_SHAPES];
uniform highp vec4 _SdfShapeMeta[NOW_SDF_MAX_SHAPES];
uniform highp vec4 _SdfColors[NOW_SDF_MAX_SHAPES];
uniform highp vec4 _SdfUvs[NOW_SDF_MAX_SHAPES];
uniform highp vec4 _SdfImageUvs[NOW_SDF_MAX_SHAPES];
uniform highp vec4 _SdfLayerData0[NOW_SDF_MAX_LAYERS];
uniform highp vec4 _SdfLayerData1[NOW_SDF_MAX_LAYERS];

float sdBox(vec2 p, vec2 b)
{
    vec2 q = abs(p) - b;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
}

float sdRoundBox(vec2 p, vec2 b, vec4 r)
{
    float radius;

    if (p.x < 0.0)
        radius = p.y < 0.0 ? r.z : r.w;
    else
        radius = p.y < 0.0 ? r.x : r.y;

    radius = min(radius, min(b.x, b.y));
    vec2 q = abs(p) - b + radius;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
}

float sdEllipse(vec2 p, vec2 radius)
{
    radius = max(radius, 0.0001);
    return (length(p / radius) - 1.0) * min(radius.x, radius.y);
}

float sdCapsule(vec2 p, vec2 a, vec2 b, float r)
{
    vec2 pa = p - a;
    vec2 ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.0001));
    return length(pa - ba * h) - r;
}

float NowSdfChamferedBoxDistanceV2(vec2 p, vec2 halfSize, float chamfer)
{
    halfSize = max(halfSize, 0.0001);
    chamfer = clamp(chamfer, 0.0, min(halfSize.x, halfSize.y));
    vec2 q = abs(p) - halfSize;

    if (q.y > q.x)
        q = q.yx;

    q.y += chamfer;
    const float diagonalScale = 0.70710678118;
    const float diagonalBias = 1.0 - 1.41421356237;

    if (q.y < 0.0 && q.y + q.x * diagonalBias < 0.0)
        return q.x;

    if (q.x < q.y)
        return (q.x + q.y) * diagonalScale;

    return length(q);
}

vec2 NowSdfNodePivotV2(float type, vec4 data1, vec4 data2)
{
    if (type > 3.5 && type < 4.5)
        return data1.xy * 0.5 + data1.zw * 0.5;

    if (type > 8.5 && type < 9.5)
    {
        float scale = max(data2.w, 1.17549435e-38);
        vec2 a = data1.xy;
        vec2 b = a + data1.zw * scale;
        vec2 c = a + data2.xy * scale;
        vec2 minPoint = min(a, min(b, c));
        vec2 maxPoint = max(a, max(b, c));
        return minPoint + (maxPoint - minPoint) * 0.5;
    }

    return data1.xy;
}

vec2 NowSdfInverseRotateRelativeV2(
    vec2 scenePos,
    vec2 pivot,
    vec2 rotation,
    float rotationLengthSquared)
{
    vec2 p = scenePos - pivot;

    return vec2(
        p.x * rotation.x + p.y * rotation.y,
        -p.x * rotation.y + p.y * rotation.x) / rotationLengthSquared;
}

float NowSdfSegmentDistanceSquaredV2(vec2 p, vec2 a, vec2 b)
{
    vec2 edge = b - a;
    float denominator = dot(edge, edge);

    if (denominator <= 0.0)
        return dot(p - a, p - a);

    float t = saturate(dot(p - a, edge) / denominator);
    vec2 nearest = p - a - edge * t;
    return dot(nearest, nearest);
}

float NowSdfTriangleDistanceV2(vec2 p, vec2 b, vec2 c, float orientationSign)
{
    const vec2 a = vec2(0.0, 0.0);
    float distanceSquared = min(
        NowSdfSegmentDistanceSquaredV2(p, a, b),
        min(
            NowSdfSegmentDistanceSquaredV2(p, b, c),
            NowSdfSegmentDistanceSquaredV2(p, c, a)));
    if (abs(orientationSign) < 0.5)
        return sqrt(max(distanceSquared, 0.0));

    float ab = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
    float bc = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
    float ca = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
    bool inside = orientationSign > 0.0
        ? (ab >= 0.0 && bc >= 0.0 && ca >= 0.0)
        : (ab <= 0.0 && bc <= 0.0 && ca <= 0.0);
    float signedDist = sqrt(max(distanceSquared, 0.0));
    return inside ? -signedDist : signedDist;
}

vec2 NowSdfRotateRadialV2(vec2 p, vec2 rotation)
{
    return vec2(
        p.x * rotation.x - p.y * rotation.y,
        p.x * rotation.y + p.y * rotation.x);
}

float NowSdfArcDistanceV2(vec2 p, vec2 sc, float ra, float rb)
{
    p.x = abs(p.x);
    return ((sc.y * p.x > sc.x * p.y) ? length(p - sc * ra) : abs(length(p) - ra)) - rb;
}

float NowSdfPieDistanceV2(vec2 p, vec2 sc, float r)
{
    p.x = abs(p.x);
    float l = length(p) - r;
    float m = length(p - sc * clamp(dot(p, sc), 0.0, r));
    return max(l, m * sign(sc.y * p.x - sc.x * p.y));
}

float median(float r, float g, float b)
{
    return max(min(r, g), min(max(r, g), b));
}

vec2 NowSdfGlyphSamplesV2(vec4 texel, float encoding)
{
    if (encoding > 0.5)
    {
        float packedSample = (texel.r * 256.0 + texel.b) / 257.0;
        return vec2(packedSample, packedSample);
    }

    return vec2(median(texel.r, texel.g, texel.b), texel.a);
}

float NowSdfGlyphSampleV2(vec4 texel, float encoding)
{
    return NowSdfGlyphSamplesV2(texel, encoding).x;
}

#define NOW_SDF_IMAGE_CODE_STEP 0.002

float NowSdfShapeCodeStepV2(float type, vec4 data2)
{
    if (type > 4.5 && type < 5.5)
        return max(data2.z, 0.0);

    if (type > 9.5 && type < 10.5)
        return max(min(data2.x, data2.y), 0.0001) * NOW_SDF_IMAGE_CODE_STEP;

    return 0.0;
}

vec2 NowSdfAtlasUvV2(vec2 uv, vec4 texelRect, vec2 atlasSize)
{
    vec2 texel = clamp(uv * texelRect.zw, vec2(0.5), max(texelRect.zw - 0.5, 0.5));
    return (texelRect.xy + texel) / max(atlasSize, 1.0);
}

float NowSdfImageLocalDistanceV2(vec2 local, vec2 size, vec4 data2, vec4 fieldRect)
{
    vec2 texelScale = max(data2.xy, 0.0001);
    float pad = max(data2.z, 0.0);
    vec2 fieldSize = max(size + 2.0 * pad * texelScale, 0.0001);
    vec2 uv = saturate(local / fieldSize + 0.5);
    vec2 atlasUv = NowSdfAtlasUvV2(vec2(uv.x, 1.0 - uv.y), fieldRect, _SdfImageAtlasSize.xy);
    float texelDistance = textureLod(_SdfImageField, atlasUv, 0.0).r;
    float signedDist = texelDistance * min(texelScale.x, texelScale.y);
    float boundsDist = sdBox(local, fieldSize * 0.5);
    return boundsDist > 0.0 ? max(signedDist, 0.0) + boundsDist : signedDist;
}

vec2 NowSdfGlyphLocalDistancesV2(
    vec2 local,
    vec2 size,
    vec4 data2,
    vec4 uvRect)
{
    size = max(size, 0.0001);
    vec2 halfSize = size * 0.5;
    vec2 glyphUv = local / size + 0.5;
    float boundsDist = sdBox(local, halfSize);

    if (glyphUv.x < 0.0 || glyphUv.y < 0.0 || glyphUv.x > 1.0 || glyphUv.y > 1.0)
    {
        float outsideDistance = 0.5 * max(data2.x, 0.0001) + max(boundsDist, 0.0);
        return vec2(outsideDistance, outsideDistance);
    }

    vec2 atlasUv = uvRect.xy + vec2(glyphUv.x, 1.0 - glyphUv.y) * uvRect.zw;
    vec4 msd = texture(_MainTex, atlasUv);
    return (0.5 - NowSdfGlyphSamplesV2(msd, data2.y)) * max(data2.x, 0.0001);
}

vec2 sdGlyphDistances(vec2 scenePos, vec4 data1, vec4 data2, vec4 uvRect)
{
    return NowSdfGlyphLocalDistancesV2(scenePos - data1.xy, data1.zw, data2, uvRect);
}

float sdGlyph(vec2 scenePos, vec4 data1, vec4 data2, vec4 uvRect)
{
    return sdGlyphDistances(scenePos, data1, data2, uvRect).x;
}

float NowSdfUnrotatedShapeDistanceV2(
    int index,
    float type,
    vec4 data1,
    vec4 data2,
    vec2 scenePos)
{
    if (type < 0.5)
        return length(scenePos - data1.xy) - data1.z;

    if (type < 1.5)
        return sdBox(scenePos - data1.xy, max(data1.zw * 0.5, 0.0001));

    if (type < 2.5)
        return sdRoundBox(scenePos - data1.xy, max(data1.zw * 0.5, 0.0001), data2);

    if (type < 3.5)
        return sdEllipse(scenePos - data1.xy, max(data1.zw * 0.5, 0.0001));

    if (type < 4.5)
        return sdCapsule(scenePos, data1.xy, data1.zw, data2.x);

    if (type < 5.5)
        return sdGlyph(scenePos, data1, data2, _SdfUvs[index]);

    if (type < 7.5)
    {
        vec2 radial = scenePos - data1.xy;

        if (dot(data2.zw, data2.zw) < 0.5)
        {
            if (type < 6.5)
                return abs(length(radial) - data1.z) - data1.w;

            return length(radial) - data1.z;
        }

        vec2 q = NowSdfRotateRadialV2(radial, data2.zw);

        if (type < 6.5)
            return NowSdfArcDistanceV2(q, data2.xy, data1.z, data1.w);

        return NowSdfPieDistanceV2(q, data2.xy, data1.z);
    }

    if (type < 8.5)
    {
        return NowSdfChamferedBoxDistanceV2(
            scenePos - data1.xy,
            max(data1.zw * 0.5, 0.0001),
            data2.x);
    }

    if (type < 9.5)
    {
        float scale = max(data2.w, 1.17549435e-38);
        vec2 normalizedPosition = (scenePos - data1.xy) / scale;
        return NowSdfTriangleDistanceV2(
            normalizedPosition,
            data1.zw,
            data2.xy,
            data2.z) * scale;
    }

    if (type < 10.5)
        return NowSdfImageLocalDistanceV2(scenePos - data1.xy, data1.zw, data2, _SdfImageUvs[index]);

    return 100000.0;
}

float NowSdfRotatedShapeDistanceV2(
    int index,
    float type,
    vec4 data1,
    vec4 data2,
    vec2 relativeScenePos,
    vec2 pivot)
{
    if (type < 0.5)
        return length(relativeScenePos) - data1.z;

    if (type < 1.5)
        return sdBox(relativeScenePos, max(data1.zw * 0.5, 0.0001));

    if (type < 2.5)
        return sdRoundBox(relativeScenePos, max(data1.zw * 0.5, 0.0001), data2);

    if (type < 3.5)
        return sdEllipse(relativeScenePos, max(data1.zw * 0.5, 0.0001));

    if (type < 4.5)
    {
        return sdCapsule(
            relativeScenePos,
            data1.xy - pivot,
            data1.zw - pivot,
            data2.x);
    }

    if (type < 5.5)
        return NowSdfGlyphLocalDistancesV2(
            relativeScenePos,
            data1.zw,
            data2,
            _SdfUvs[index]).x;

    if (type < 7.5)
    {
        if (dot(data2.zw, data2.zw) < 0.5)
        {
            if (type < 6.5)
                return abs(length(relativeScenePos) - data1.z) - data1.w;

            return length(relativeScenePos) - data1.z;
        }

        vec2 q = NowSdfRotateRadialV2(relativeScenePos, data2.zw);

        if (type < 6.5)
            return NowSdfArcDistanceV2(q, data2.xy, data1.z, data1.w);

        return NowSdfPieDistanceV2(q, data2.xy, data1.z);
    }

    if (type < 8.5)
    {
        return NowSdfChamferedBoxDistanceV2(
            relativeScenePos,
            max(data1.zw * 0.5, 0.0001),
            data2.x);
    }

    if (type < 9.5)
    {
        float scale = max(data2.w, 1.17549435e-38);
        vec2 normalizedPosition = (relativeScenePos + (pivot - data1.xy)) / scale;
        return NowSdfTriangleDistanceV2(
            normalizedPosition,
            data1.zw,
            data2.xy,
            data2.z) * scale;
    }

    if (type < 10.5)
        return NowSdfImageLocalDistanceV2(relativeScenePos, data1.zw, data2, _SdfImageUvs[index]);

    return 100000.0;
}

vec2 NowSdfUnrotatedShapeDistancesV2(
    int index,
    float type,
    vec4 data1,
    vec4 data2,
    vec2 scenePos)
{
    if (type > 4.5 && type < 5.5)
        return sdGlyphDistances(scenePos, data1, data2, _SdfUvs[index]);

    float signedDist = NowSdfUnrotatedShapeDistanceV2(index, type, data1, data2, scenePos);
    return vec2(signedDist, signedDist);
}

vec2 NowSdfRotatedShapeDistancesV2(
    int index,
    float type,
    vec4 data1,
    vec4 data2,
    vec2 relativeScenePos,
    vec2 pivot)
{
    if (type > 4.5 && type < 5.5)
    {
        return NowSdfGlyphLocalDistancesV2(
            relativeScenePos,
            data1.zw,
            data2,
            _SdfUvs[index]);
    }

    float signedDist = NowSdfRotatedShapeDistanceV2(
        index,
        type,
        data1,
        data2,
        relativeScenePos,
        pivot);
    return vec2(signedDist, signedDist);
}

vec2 shapeDistances(int index, float type, vec4 data1, vec4 data2, vec2 scenePos)
{
    vec2 rotation = _SdfShapeMeta[index].zw;
    float rotationLengthSquared = dot(rotation, rotation);

    if (rotationLengthSquared == 0.0)
        return NowSdfUnrotatedShapeDistancesV2(index, type, data1, data2, scenePos);

    vec2 pivot = NowSdfNodePivotV2(type, data1, data2);
    vec2 relativeScenePos = NowSdfInverseRotateRelativeV2(
        scenePos,
        pivot,
        rotation,
        rotationLengthSquared);
    return NowSdfRotatedShapeDistancesV2(
        index,
        type,
        data1,
        data2,
        relativeScenePos,
        pivot) *
        sqrt(rotationLengthSquared);
}

float shapeDistance(int index, float type, vec4 data1, vec4 data2, vec2 scenePos)
{
    return shapeDistances(index, type, data1, data2, scenePos).x;
}

float NowSdfTransformedShapeCodeStepV2(int index, float type, vec4 data2)
{
    float codeStep = NowSdfShapeCodeStepV2(type, data2);
    vec2 rotation = _SdfShapeMeta[index].zw;
    float rotationLengthSquared = dot(rotation, rotation);
    return rotationLengthSquared == 0.0
        ? codeStep
        : codeStep * sqrt(rotationLengthSquared);
}

vec2 NowSdfRotatedShapeUvV2(
    float type,
    vec4 data1,
    vec4 data2,
    vec2 relativeScenePos,
    vec2 pivot)
{
    vec2 minPoint;
    vec2 maxPoint;

    if (type < 0.5)
    {
        minPoint = -data1.zz;
        maxPoint = data1.zz;
    }
    else if (type < 3.5)
    {
        vec2 halfSize = data1.zw * 0.5;
        minPoint = -halfSize;
        maxPoint = halfSize;
    }
    else if (type < 4.5)
    {
        vec2 a = data1.xy - pivot;
        vec2 b = data1.zw - pivot;
        minPoint = min(a, b) - data2.xx;
        maxPoint = max(a, b) + data2.xx;
    }
    else if (type < 7.5)
    {
        float extent = type < 6.5 ? data1.z + data1.w : data1.z;
        minPoint = -vec2(extent, extent);
        maxPoint = vec2(extent, extent);
    }
    else if (type < 8.5)
    {
        vec2 halfSize = data1.zw * 0.5;
        minPoint = -halfSize;
        maxPoint = halfSize;
    }
    else if (type < 9.5)
    {
        float scale = max(data2.w, 1.17549435e-38);
        vec2 normalizedPosition = (relativeScenePos + (pivot - data1.xy)) / scale;
        vec2 minNormalized = min(vec2(0.0, 0.0), min(data1.zw, data2.xy));
        vec2 maxNormalized = max(vec2(0.0, 0.0), max(data1.zw, data2.xy));
        vec2 span = maxNormalized - minNormalized;
        vec2 uv;
        uv.x = span.x > 0.0
            ? saturate((normalizedPosition.x - minNormalized.x) / span.x)
            : 0.5;
        uv.y = span.y > 0.0
            ? saturate((normalizedPosition.y - minNormalized.y) / span.y)
            : 0.5;
        return vec2(uv.x, 1.0 - uv.y);
    }
    else if (type < 10.5)
    {
        vec2 halfSize = data1.zw * 0.5;
        minPoint = -halfSize;
        maxPoint = halfSize;
    }
    else
    {
        return vec2(0.5, 0.5);
    }

    vec2 uv = saturate(
        (relativeScenePos - minPoint) /
        max(maxPoint - minPoint, 0.0001));
    return vec2(uv.x, 1.0 - uv.y);
}

vec2 shapeUv(int index, float type, vec4 data1, vec4 data2, vec2 scenePos)
{
    vec2 rotation = _SdfShapeMeta[index].zw;
    float rotationLengthSquared = dot(rotation, rotation);

    if (rotationLengthSquared != 0.0)
    {
        vec2 pivot = NowSdfNodePivotV2(type, data1, data2);
        vec2 relativeScenePos = NowSdfInverseRotateRelativeV2(
            scenePos,
            pivot,
            rotation,
            rotationLengthSquared);
        return NowSdfRotatedShapeUvV2(
            type,
            data1,
            data2,
            relativeScenePos,
            pivot);
    }

    vec2 minPoint;
    vec2 maxPoint;

    if (type < 0.5)
    {
        minPoint = data1.xy - data1.zz;
        maxPoint = data1.xy + data1.zz;
    }
    else if (type < 3.5)
    {
        vec2 halfSize = data1.zw * 0.5;
        minPoint = data1.xy - halfSize;
        maxPoint = data1.xy + halfSize;
    }
    else if (type < 4.5)
    {
        minPoint = min(data1.xy, data1.zw) - data2.xx;
        maxPoint = max(data1.xy, data1.zw) + data2.xx;
    }
    else if (type < 7.5)
    {
        float extent = type < 6.5 ? data1.z + data1.w : data1.z;
        minPoint = data1.xy - vec2(extent, extent);
        maxPoint = data1.xy + vec2(extent, extent);
    }
    else if (type < 8.5)
    {
        vec2 halfSize = data1.zw * 0.5;
        scenePos -= data1.xy;
        minPoint = -halfSize;
        maxPoint = halfSize;
    }
    else if (type < 9.5)
    {
        float scale = max(data2.w, 1.17549435e-38);
        vec2 normalizedPosition = (scenePos - data1.xy) / scale;
        vec2 minNormalized = min(vec2(0.0, 0.0), min(data1.zw, data2.xy));
        vec2 maxNormalized = max(vec2(0.0, 0.0), max(data1.zw, data2.xy));
        vec2 span = maxNormalized - minNormalized;
        vec2 uv;
        uv.x = span.x > 0.0
            ? saturate((normalizedPosition.x - minNormalized.x) / span.x)
            : 0.5;
        uv.y = span.y > 0.0
            ? saturate((normalizedPosition.y - minNormalized.y) / span.y)
            : 0.5;
        return vec2(uv.x, 1.0 - uv.y);
    }
    else if (type < 10.5)
    {
        vec2 halfSize = data1.zw * 0.5;
        minPoint = data1.xy - halfSize;
        maxPoint = data1.xy + halfSize;
    }
    else
    {
        return vec2(0.5, 0.5);
    }

    vec2 uv = saturate((scenePos - minPoint) / max(maxPoint - minPoint, 0.0001));
    return vec2(uv.x, 1.0 - uv.y);
}

vec4 shapeFill(int index, float type, vec4 data1, vec4 data2, vec2 scenePos, vec4 tint)
{
    vec4 color = _SdfColors[index] * tint;

    if (type > 9.5 && type < 10.5)
    {
        vec2 imageUv = shapeUv(index, type, data1, data2, scenePos);
        vec2 atlasUv = NowSdfAtlasUvV2(imageUv, _SdfUvs[index], _SdfImageAtlasSize.zw);
        return textureLod(_SdfImageColor, atlasUv, 0.0) * color;
    }

    if ((type > 4.5 && type < 5.5) || _SdfShapeMeta[index].y < 0.5)
        return color;

    vec2 uv = shapeUv(index, type, data1, data2, scenePos);
    vec4 uvRect = _SdfUvs[index];
    uv = uvRect.xy + uv * uvRect.zw;
    return texture(_MainTex, uv) * color;
}

vec4 NowSdfBlendFillV2(vec4 a, vec4 b, float h)
{
    float weightA = h * a.a;
    float weightB = (1.0 - h) * b.a;
    float weightSum = weightA + weightB;
    vec3 rgb = weightSum > 0.0
        ? (a.rgb * weightA + b.rgb * weightB) / weightSum
        : mix(b.rgb, a.rgb, h);
    float alpha = max(weightA, weightB) / max(max(h, 1.0 - h), 0.0001);
    return vec4(rgb, alpha);
}

void combine(
    inout float dist,
    inout vec4 fill,
    inout float codeStep,
    float shapeDist,
    vec4 nextFill,
    float shapeCodeStep,
    float operation,
    float smoothing)
{
    if (operation < 0.5)
    {
        if (shapeDist < dist)
        {
            dist = shapeDist;
            fill = nextFill;
            codeStep = shapeCodeStep;
        }
        else if (shapeDist == dist)
        {
            codeStep = max(codeStep, shapeCodeStep);
        }

        return;
    }

    if (operation < 1.5)
    {
        if (-shapeDist > dist)
        {
            dist = -shapeDist;
            codeStep = shapeCodeStep;
        }
        else if (-shapeDist == dist)
        {
            codeStep = max(codeStep, shapeCodeStep);
        }
        return;
    }

    if (operation < 2.5)
    {
        if (shapeDist > dist)
        {
            fill = nextFill;
            codeStep = shapeCodeStep;
        }
        else if (shapeDist == dist)
        {
            codeStep = max(codeStep, shapeCodeStep);
        }

        dist = max(dist, shapeDist);
        return;
    }

    smoothing = max(smoothing, 0.0001);

    if (operation < 3.5)
    {
        float h = saturate(0.5 + 0.5 * (shapeDist - dist) / smoothing);
        dist = mix(shapeDist, dist, h) - smoothing * h * (1.0 - h);
        fill = NowSdfBlendFillV2(fill, nextFill, h);
        codeStep = mix(shapeCodeStep, codeStep, h);
        return;
    }

    if (operation < 4.5)
    {
        float h = saturate(0.5 - 0.5 * (shapeDist + dist) / smoothing);
        dist = mix(dist, -shapeDist, h) + smoothing * h * (1.0 - h);
        codeStep = mix(codeStep, shapeCodeStep, h);
        return;
    }

    {
        float h = saturate(0.5 - 0.5 * (shapeDist - dist) / smoothing);
        dist = mix(shapeDist, dist, h) + smoothing * h * (1.0 - h);
        fill = NowSdfBlendFillV2(fill, nextFill, h);
        codeStep = mix(shapeCodeStep, codeStep, h);
    }
}

void combineDistance(
    inout float dist,
    inout float codeStep,
    float shapeDist,
    float shapeCodeStep,
    float operation,
    float smoothing)
{
    if (operation < 0.5)
    {
        if (shapeDist < dist)
        {
            dist = shapeDist;
            codeStep = shapeCodeStep;
        }
        else if (shapeDist == dist)
        {
            codeStep = max(codeStep, shapeCodeStep);
        }
        return;
    }

    if (operation < 1.5)
    {
        if (-shapeDist > dist)
        {
            dist = -shapeDist;
            codeStep = shapeCodeStep;
        }
        else if (-shapeDist == dist)
        {
            codeStep = max(codeStep, shapeCodeStep);
        }
        return;
    }

    if (operation < 2.5)
    {
        if (shapeDist > dist)
        {
            dist = shapeDist;
            codeStep = shapeCodeStep;
        }
        else if (shapeDist == dist)
        {
            codeStep = max(codeStep, shapeCodeStep);
        }
        return;
    }

    smoothing = max(smoothing, 0.0001);

    if (operation < 3.5)
    {
        float h = saturate(0.5 + 0.5 * (shapeDist - dist) / smoothing);
        dist = mix(shapeDist, dist, h) - smoothing * h * (1.0 - h);
        codeStep = mix(shapeCodeStep, codeStep, h);
        return;
    }

    if (operation < 4.5)
    {
        float h = saturate(0.5 - 0.5 * (shapeDist + dist) / smoothing);
        dist = mix(dist, -shapeDist, h) + smoothing * h * (1.0 - h);
        codeStep = mix(codeStep, shapeCodeStep, h);
        return;
    }

    {
        float h = saturate(0.5 - 0.5 * (shapeDist - dist) / smoothing);
        dist = mix(shapeDist, dist, h) + smoothing * h * (1.0 - h);
        codeStep = mix(shapeCodeStep, codeStep, h);
    }
}

void decodeGraphRange(float packedRange, out int start, out int count)
{
    int total = min(max(int(_SdfShapeCount), 0), NOW_SDF_MAX_SHAPES);
    float packed = max(packedRange, 0.0);
    float startValue = floor(packed * (1.0 / 128.0));
    start = min(max(int(startValue), 0), total);
    count = min(max(int(packed - startValue * 128.0 + 0.5), 0), total - start);
}

void evalGraphFields(
    float packedRange,
    vec2 scenePos,
    vec4 tint,
    bool useDistinctEffectField,
    out float dist,
    out float effectDist,
    out vec4 fill,
    out float codeStep,
    out float effectCodeStep)
{
    int start;
    int count;
    decodeGraphRange(packedRange, start, count);
    dist = 100000.0;
    effectDist = 100000.0;
    fill = vec4(0.0);
    codeStep = 0.0;
    effectCodeStep = 0.0;

    if (count <= 0)
        return;

    int first = start;
    vec4 data0 = _SdfData0[first];
    vec4 data1 = _SdfData1[first];
    vec4 data2 = _SdfData2[first];
    vec2 firstDistances = shapeDistances(first, data0.x, data1, data2, scenePos);
    dist = firstDistances.x;
    effectDist = useDistinctEffectField ? firstDistances.y : firstDistances.x;
    fill = shapeFill(first, data0.x, data1, data2, scenePos, tint);
    codeStep = NowSdfTransformedShapeCodeStepV2(first, data0.x, data2);
    effectCodeStep = codeStep;

    for (int localIndex = 1; localIndex < NOW_SDF_MAX_SHAPES; ++localIndex)
    {
        if (localIndex >= count)
            break;

        int index = start + localIndex;
        data0 = _SdfData0[index];
        data1 = _SdfData1[index];
        data2 = _SdfData2[index];
        vec2 shapeFieldDistances = shapeDistances(index, data0.x, data1, data2, scenePos);
        vec4 nextFill = shapeFill(index, data0.x, data1, data2, scenePos, tint);
        float shapeCodeStep = NowSdfTransformedShapeCodeStepV2(index, data0.x, data2);
        combine(
            dist,
            fill,
            codeStep,
            shapeFieldDistances.x,
            nextFill,
            shapeCodeStep,
            data0.y,
            data0.z);

        if (useDistinctEffectField)
        {
            combineDistance(
                effectDist,
                effectCodeStep,
                shapeFieldDistances.y,
                shapeCodeStep,
                data0.y,
                data0.z);
        }
    }

    if (!useDistinctEffectField)
    {
        effectDist = dist;
        effectCodeStep = codeStep;
    }
}

void evalGraph(
    float packedRange,
    vec2 scenePos,
    vec4 tint,
    out float dist,
    out vec4 fill,
    out float codeStep)
{
    float effectDist;
    float effectCodeStep;
    evalGraphFields(
        packedRange,
        scenePos,
        tint,
        false,
        dist,
        effectDist,
        fill,
        codeStep,
        effectCodeStep);
}

void evalLayerFields(
    int index,
    vec2 scenePos,
    vec4 tint,
    bool useDistinctEffectField,
    out float dist,
    out float effectDist,
    out vec4 fill,
    out float codeStep,
    out float effectCodeStep)
{
    dist = 100000.0;
    effectDist = 100000.0;
    fill = vec4(0.0);
    codeStep = 0.0;
    effectCodeStep = 0.0;
    vec4 layer0 = _SdfLayerData0[index];
    vec4 layer1 = _SdfLayerData1[index];

    if (layer0.w < 0.5)
    {
        evalGraphFields(
            layer1.z,
            scenePos,
            tint,
            useDistinctEffectField,
            dist,
            effectDist,
            fill,
            codeStep,
            effectCodeStep);
        return;
    }

    float aDist = 0.0;
    float bDist = 0.0;
    float aEffectDist = 0.0;
    float bEffectDist = 0.0;
    vec4 aFill = vec4(0.0);
    vec4 bFill = vec4(0.0);
    float aCodeStep = 0.0;
    float bCodeStep = 0.0;
    float aEffectCodeStep = 0.0;
    float bEffectCodeStep = 0.0;
    evalGraphFields(
        layer1.z,
        scenePos,
        tint,
        useDistinctEffectField,
        aDist,
        aEffectDist,
        aFill,
        aCodeStep,
        aEffectCodeStep);
    evalGraphFields(
        layer1.w,
        scenePos,
        tint,
        useDistinctEffectField,
        bDist,
        bEffectDist,
        bFill,
        bCodeStep,
        bEffectCodeStep);
    float t = saturate(layer1.y);
    dist = mix(aDist, bDist, t);
    fill = NowSdfBlendFillV2(aFill, bFill, 1.0 - t);
    codeStep = mix(aCodeStep, bCodeStep, t);
    if (useDistinctEffectField)
    {
        effectDist = mix(aEffectDist, bEffectDist, t);
        effectCodeStep = mix(aEffectCodeStep, bEffectCodeStep, t);
    }
    else
    {
        effectDist = dist;
        effectCodeStep = codeStep;
    }
}

void evalLayer(
    int index,
    vec2 scenePos,
    vec4 tint,
    out float dist,
    out vec4 fill,
    out float codeStep)
{
    float effectDist;
    float effectCodeStep;
    evalLayerFields(
        index,
        scenePos,
        tint,
        false,
        dist,
        effectDist,
        fill,
        codeStep,
        effectCodeStep);
}

void evalGraphDistanceField(
    float packedRange,
    vec2 scenePos,
    float effectField,
    out float dist,
    out float codeStep)
{
    int start;
    int count;
    decodeGraphRange(packedRange, start, count);
    dist = 100000.0;
    codeStep = 0.0;

    if (count <= 0)
        return;

    int first = start;
    vec4 data0 = _SdfData0[first];
    vec4 firstData2 = _SdfData2[first];
    vec2 firstDistances = shapeDistances(first, data0.x, _SdfData1[first], firstData2, scenePos);
    dist = mix(firstDistances.x, firstDistances.y, effectField);
    codeStep = NowSdfTransformedShapeCodeStepV2(first, data0.x, firstData2);

    for (int localIndex = 1; localIndex < NOW_SDF_MAX_SHAPES; ++localIndex)
    {
        if (localIndex >= count)
            break;

        int index = start + localIndex;
        data0 = _SdfData0[index];
        vec4 data2 = _SdfData2[index];
        vec2 shapeFieldDistances = shapeDistances(index, data0.x, _SdfData1[index], data2, scenePos);
        float shapeDist = mix(shapeFieldDistances.x, shapeFieldDistances.y, effectField);
        float shapeCodeStep = NowSdfTransformedShapeCodeStepV2(index, data0.x, data2);
        combineDistance(dist, codeStep, shapeDist, shapeCodeStep, data0.y, data0.z);
    }
}

void evalGraphDistance(
    float packedRange,
    vec2 scenePos,
    out float dist,
    out float codeStep)
{
    evalGraphDistanceField(packedRange, scenePos, 0.0, dist, codeStep);
}

void evalGraphEffectDistance(
    float packedRange,
    vec2 scenePos,
    out float dist,
    out float codeStep)
{
    evalGraphDistanceField(packedRange, scenePos, 1.0, dist, codeStep);
}

void evalLayerDistanceField(
    int index,
    vec2 scenePos,
    float effectField,
    out float dist,
    out float codeStep)
{
    dist = 100000.0;
    codeStep = 0.0;
    vec4 layer0 = _SdfLayerData0[index];
    vec4 layer1 = _SdfLayerData1[index];

    if (layer0.w < 0.5)
    {
        evalGraphDistanceField(layer1.z, scenePos, effectField, dist, codeStep);
        return;
    }

    float aDist = 100000.0;
    float bDist = 100000.0;
    float aCodeStep = 0.0;
    float bCodeStep = 0.0;
    evalGraphDistanceField(layer1.z, scenePos, effectField, aDist, aCodeStep);
    evalGraphDistanceField(layer1.w, scenePos, effectField, bDist, bCodeStep);
    float t = saturate(layer1.y);
    dist = mix(aDist, bDist, t);
    codeStep = mix(aCodeStep, bCodeStep, t);
}

void evalLayerDistance(int index, vec2 scenePos, out float dist, out float codeStep)
{
    evalLayerDistanceField(index, scenePos, 0.0, dist, codeStep);
}

void evalLayerEffectDistance(int index, vec2 scenePos, out float dist, out float codeStep)
{
    evalLayerDistanceField(index, scenePos, 1.0, dist, codeStep);
}

void evalSceneFields(
    vec2 scenePos,
    vec4 tint,
    bool useDistinctEffectField,
    out float dist,
    out float effectDist,
    out vec4 fill,
    out float codeStep,
    out float effectCodeStep)
{
    int layerCount = min(int(_SdfLayerCount), NOW_SDF_MAX_LAYERS);
    bool found = false;
    dist = 100000.0;
    effectDist = 100000.0;
    fill = vec4(0.0);
    codeStep = 0.0;
    effectCodeStep = 0.0;

    for (int layer = 0; layer < NOW_SDF_MAX_LAYERS; ++layer)
    {
        if (layer >= layerCount)
            break;

        float layerDist;
        float layerEffectDist;
        vec4 layerFill;
        float layerCodeStep;
        float layerEffectCodeStep;
        evalLayerFields(
            layer,
            scenePos,
            tint,
            useDistinctEffectField,
            layerDist,
            layerEffectDist,
            layerFill,
            layerCodeStep,
            layerEffectCodeStep);

        if (!found)
        {
            dist = layerDist;
            effectDist = layerEffectDist;
            fill = layerFill;
            codeStep = layerCodeStep;
            effectCodeStep = layerEffectCodeStep;
            found = true;
        }
        else
        {
            combine(
                dist,
                fill,
                codeStep,
                layerDist,
                layerFill,
                layerCodeStep,
                _SdfLayerData0[layer].y,
                _SdfLayerData0[layer].z);

            if (useDistinctEffectField)
            {
                combineDistance(
                    effectDist,
                    effectCodeStep,
                    layerEffectDist,
                    layerEffectCodeStep,
                    _SdfLayerData0[layer].y,
                    _SdfLayerData0[layer].z);
            }
        }
    }

    if (!useDistinctEffectField)
    {
        effectDist = dist;
        effectCodeStep = codeStep;
    }
}

void evalScene(
    vec2 scenePos,
    vec4 tint,
    out float dist,
    out vec4 fill,
    out float codeStep)
{
    float effectDist;
    float effectCodeStep;
    evalSceneFields(
        scenePos,
        tint,
        false,
        dist,
        effectDist,
        fill,
        codeStep,
        effectCodeStep);
}

void evalSceneDistanceAndCodeStepField(
    vec2 scenePos,
    float effectField,
    out float dist,
    out float codeStep)
{
    int layerCount = min(int(_SdfLayerCount), NOW_SDF_MAX_LAYERS);
    bool found = false;
    dist = 100000.0;
    codeStep = 0.0;

    for (int layer = 0; layer < NOW_SDF_MAX_LAYERS; ++layer)
    {
        if (layer >= layerCount)
            break;

        float layerDist;
        float layerCodeStep;
        evalLayerDistanceField(layer, scenePos, effectField, layerDist, layerCodeStep);

        if (!found)
        {
            dist = layerDist;
            codeStep = layerCodeStep;
            found = true;
        }
        else
        {
            combineDistance(
                dist,
                codeStep,
                layerDist,
                layerCodeStep,
                _SdfLayerData0[layer].y,
                _SdfLayerData0[layer].z);
        }
    }
}

void evalSceneDistanceAndCodeStep(vec2 scenePos, out float dist, out float codeStep)
{
    evalSceneDistanceAndCodeStepField(scenePos, 0.0, dist, codeStep);
}

void evalSceneEffectDistanceAndCodeStep(vec2 scenePos, out float dist, out float codeStep)
{
    evalSceneDistanceAndCodeStepField(scenePos, 1.0, dist, codeStep);
}

void evalSceneDistance(vec2 scenePos, out float dist)
{
    float codeStep;
    evalSceneDistanceAndCodeStep(scenePos, dist, codeStep);
}

float hash21(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float noise21(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

vec2 warpScenePos(vec2 scenePos)
{
    if (_SdfWarp.x <= 0.0)
        return scenePos;

    float scale = max(_SdfWarp.y, 0.0001);
    float t = nowui_Time * _SdfWarp.z + _SdfWarp.w;
    vec2 p = scenePos / scale;
    vec2 n = vec2(noise21(p + t), noise21(p + t + 37.23)) * 2.0 - 1.0;
    return scenePos + n * _SdfWarp.x;
}

vec4 effectColor(vec4 color, vec4 tint)
{
    return color * tint;
}

float exteriorEffectValidity(float fieldDistance, float codeStep, float edge)
{
    float isGlyphDistance = sign(max(codeStep, 0.0));
    float glyphValidity = 1.0 - smoothstep(
        max(_SdfTextEffectLimit, 0.0) - edge,
        max(_SdfTextEffectLimit, 0.0) + edge,
        fieldDistance);
    return mix(1.0, glyphValidity, isGlyphDistance);
}

float exclusiveEffectCoverage(float effectCoverage, float fillCoverage, float fillOpacity)
{
    float remainingFill = max(1.0 - fillCoverage * saturate(fillOpacity), 0.0001);
    return saturate((effectCoverage - fillCoverage) / remainingFill);
}

vec4 alphaOver(vec4 baseColor, vec4 topColor)
{
    float a = topColor.a + baseColor.a * (1.0 - topColor.a);
    vec3 rgb = (topColor.rgb * topColor.a + baseColor.rgb * baseColor.a * (1.0 - topColor.a)) / max(a, 0.0001);
    return vec4(rgb, a);
}

void main()
{
    vec2 quadPos = vRawUV * vRect.zw;

    float hasSceneMapping = step(0.0001, abs(vSceneMapping.x) + abs(vSceneMapping.y));
    vec2 sceneSize = max(mix(vRect.zw, abs(vSceneMapping.xy), hasSceneMapping), 0.0001);
    vec2 sceneDirection = mix(vec2(1.0, 1.0), vSceneMapping.zw, hasSceneMapping);
    vec2 sourceUv = 0.5 + (vRawUV - 0.5) * sceneDirection;
    vec2 sceneQuadPos = sourceUv * sceneSize;

    vec2 scenePosBase = vec2(sceneQuadPos.x, sceneSize.y - sceneQuadPos.y);
    vec2 scenePos = warpScenePos(scenePosBase);
    vec2 meshPos = vRect.xy + quadPos;
    vec2 uiPosition = vec2(meshPos.x, -meshPos.y);
    vec4 mask = vMask;

    if (NowUILegacyRectDistance(uiPosition, mask) < 0.0)
        discard;

    bool hasFiniteTextEffectLimit = _SdfTextEffectLimit < 100000.0;
    bool hasStockDistanceEffect =
        (_SdfOutlineColor.a > 0.0 && _SdfOutline.x > 0.0) ||
        (_SdfGlowColor.a > 0.0 && _SdfGlow.x > 0.0) ||
        _SdfShadowColor.a > 0.0 ||
        _SdfInnerShadowColor.a > 0.0 ||
        (_SdfContourColor.a > 0.0 && _SdfContour.x > 0.0 && _SdfContour.y > 0.0);
    bool useDistinctEffectField = hasFiniteTextEffectLimit && hasStockDistanceEffect;

    float dist = 100000.0;
    float effectDist = 100000.0;
    vec4 fill = vec4(0.0);
    float distanceCodeStep = 0.0;
    float effectCodeStep = 0.0;
    evalSceneFields(
        scenePos,
        vTint,
        useDistinctEffectField,
        dist,
        effectDist,
        fill,
        distanceCodeStep,
        effectCodeStep);

    float pixelWidth = max(
        max(length(vec2(dFdx(dist), dFdy(dist))), distanceCodeStep),
        0.0001);
    float edge = pixelWidth * max(0.5 + _SdfFeather * 0.5, 0.5);
    float effectPixelWidth = pixelWidth;
    float effectEdge = edge;

    if (useDistinctEffectField)
    {
        effectPixelWidth = max(
            max(length(vec2(dFdx(effectDist), dFdy(effectDist))), effectCodeStep),
            0.0001);
        effectEdge = effectPixelWidth * max(0.5 + _SdfFeather * 0.5, 0.5);
    }

    float coverage = smoothstep(edge, -edge, dist);
    float exteriorValidity = 1.0;

    if (useDistinctEffectField)
        exteriorValidity = exteriorEffectValidity(effectDist, effectCodeStep, effectEdge);

    vec4 col = vec4(0.0);

    if (_SdfShadowColor.a > 0.0)
    {
        float shadowDist;
        float shadowCodeStep;
        evalSceneEffectDistanceAndCodeStep(
            warpScenePos(scenePosBase - _SdfShadow.xy),
            shadowDist,
            shadowCodeStep);
        float shadowPixelWidth = max(
            max(length(vec2(dFdx(shadowDist), dFdy(shadowDist))), shadowCodeStep),
            0.0001);
        float shadowEdge = shadowPixelWidth * max(0.5 + _SdfFeather * 0.5, 0.5);
        float shadowEffectDist = shadowDist - _SdfShadow.w;
        float shadowCoverage = smoothstep(max(_SdfShadow.z, shadowPixelWidth) + shadowEdge, -shadowEdge, shadowEffectDist);
        float shadowAlpha = exclusiveEffectCoverage(shadowCoverage, coverage, fill.a);
        shadowAlpha *= exteriorEffectValidity(shadowDist, shadowCodeStep, shadowEdge);
        vec4 shadowColor = effectColor(_SdfShadowColor, vTint);
        shadowColor.a *= shadowAlpha;
        col = alphaOver(col, shadowColor);
    }

    if (_SdfGlowColor.a > 0.0 && _SdfGlow.x > 0.0)
    {
        float glowT = saturate(1.0 - max(effectDist, 0.0) / max(_SdfGlow.x, 0.0001));
        float glowCoverage = pow(glowT, max(_SdfGlow.y, 0.0001));
        float glowAlpha = exclusiveEffectCoverage(glowCoverage, coverage, fill.a) * exteriorValidity;
        vec4 glowColor = effectColor(_SdfGlowColor, vTint);
        glowColor.a *= glowAlpha;
        col = alphaOver(col, glowColor);
    }

    if (_SdfOutlineColor.a > 0.0 && _SdfOutline.x > 0.0)
    {
        float outlineCoverage = smoothstep(_SdfOutline.x + _SdfOutline.y + effectEdge, _SdfOutline.x - effectEdge, effectDist);
        float outlineAlpha = exclusiveEffectCoverage(outlineCoverage, coverage, fill.a) * exteriorValidity;
        vec4 outlineColor = effectColor(_SdfOutlineColor, vTint);
        outlineColor.a *= outlineAlpha;
        col = alphaOver(col, outlineColor);
    }

    vec4 fillColor = fill;

    if (_SdfEmboss.w > 0.0)
    {
        vec2 grad = vec2(dFdx(dist), dFdy(dist));
        vec2 normal2 = normalize(grad + 0.0001);
        vec2 light = normalize(_SdfEmboss.xy + 0.0001);
        float band = 1.0 - smoothstep(0.0, max(_SdfEmboss.z, pixelWidth), abs(dist));
        float shade = dot(normal2, light) * _SdfEmboss.w * band;
        fillColor.rgb = saturate(fillColor.rgb + shade);
    }

    fillColor.a *= coverage;
    col = alphaOver(col, fillColor);

    if (_SdfInnerShadowColor.a > 0.0)
    {
        float innerDist;
        float innerCodeStep;
        evalSceneEffectDistanceAndCodeStep(
            warpScenePos(scenePosBase - _SdfInnerShadow.xy),
            innerDist,
            innerCodeStep);
        float innerPixelWidth = max(
            max(length(vec2(dFdx(innerDist), dFdy(innerDist))), innerCodeStep),
            0.0001);
        float innerEdge = innerPixelWidth * max(0.5 + _SdfFeather * 0.5, 0.5);
        float innerEffectDist = innerDist + _SdfInnerShadow.w;
        float innerShape = smoothstep(max(_SdfInnerShadow.z, innerPixelWidth) + innerEdge, -innerEdge, innerEffectDist);
        float innerAlpha = coverage * (1.0 - innerShape);
        vec4 innerShadowColor = effectColor(_SdfInnerShadowColor, vTint);
        innerShadowColor.a *= innerAlpha;
        col = alphaOver(col, innerShadowColor);
    }

    if (_SdfContourColor.a > 0.0 && _SdfContour.x > 0.0 && _SdfContour.y > 0.0)
    {
        float spacing = max(_SdfContour.x, 0.0001);
        float halfWidth = _SdfContour.y * 0.5;
        float contourDistance = effectDist + _SdfContour.z;
        float nearest = abs(fract(contourDistance / spacing + 0.5) - 0.5) * spacing;
        float contourAlpha = smoothstep(halfWidth + effectEdge, halfWidth - effectEdge, nearest);
        if (_SdfContour.w > 0.0)
        {
            float bandIndex = floor(abs(contourDistance / spacing) + 0.5);
            contourAlpha *= 1.0 - step(_SdfContour.w, bandIndex);
        }
        if (_SdfContourMask.z > 0.0)
        {
            float maskDist = length(scenePosBase - _SdfContourMask.xy);
            float maskSoftness = max(_SdfContourMask.w, edge);
            contourAlpha *= smoothstep(_SdfContourMask.z + maskSoftness, _SdfContourMask.z - edge, maskDist);
        }
        contourAlpha *= exteriorValidity;
        vec4 contourColor = effectColor(_SdfContourColor, vTint);
        contourColor.a *= contourAlpha;
        col = alphaOver(col, contourColor);
    }

    col.a *= NowUIMaskCoverage(uiPosition);

    if (_SdfMaskOutput > 0.5)
    {
        fragColor = vec4(col.a, col.a, col.a, 1.0);
        return;
    }

    if (col.a - 0.001 < 0.0)
        discard;

    fragColor = col;
}
`;

// -------------------------------------------------------------------------------- Hidden/NowUI/SDF Image Field
//
// The jump-flood bake that turns a sprite's alpha silhouette into a signed distance field, so an SDF scene can
// have IMAGE and SPRITE nodes. Five passes, all five ported, all five reached through blit():
//
//   0 Seed     marching squares -> the contour segment of each texel cell, in field texel space
//   1 Flood    propagate the nearest segment at a halving jump step, ping-ponging two pooled targets
//   2 Resolve  measure and sign the distance -> the field (red channel, NEGATIVE inside)
//   3 Stamp    copy one field into a texel rect of an atlas, discarding outside so neighbours survive
//   4 Dilate   sprite-sized colour copy whose exterior texels inherit the nearest interior colour
//
// GENERATED from wwwroot/shaders/nowui-sdf-image{,-seed,-flood,-resolve,-stamp,-dilate}.{glsl,vert,frag} by
// tools/embed-shader.py. Edit those files, not these constants.
//
// THE CAPABILITY QUESTION, ANSWERED RATHER THAN ASSUMED. This program is the first thing in the port whose
// correctness depends on a FLOAT COLOUR ATTACHMENT, and the dependency is not cosmetic:
//
//   * The seed and flood targets hold texel-space COORDINATES (up to the field's width, and a negative sentinel
//     for "no segment"). NowSdfImageFields.FloodFormat asks for ARGBFloat and falls back to ARGBHalf
//     (NowSdfImageField.cs:372-380). RGBA8 is not a fallback: it cannot store the -1 sentinel at all, so every
//     cell would read as having a segment, and it would clamp every coordinate past texel 1 to white.
//   * The field target holds signed distances up to +/-30000. FieldFormat asks for RHalf, then RFloat, then
//     ARGBHalf (NowSdfImageField.cs:283-292).
//
// WebGL2 makes both RENDERABLE only through an extension -- EXT_color_buffer_float for the 32-bit forms,
// EXT_color_buffer_half_float (or EXT_color_buffer_float, which implies it) for the 16-bit ones. getCaps()
// already probes for both and reports them through NowRenderCaps.supportsARGBFloat / supportsARGBHalf /
// supportsRHalf, so SystemInfo.SupportsRenderTextureFormat answers honestly and NowSdfImageFields picks a format
// this device can actually allocate. resolveTargetFormat() refuses an unrenderable one out loud rather than
// substituting RGBA8. On a context with neither extension the bake cannot run and CreateRenderTexture returns 0,
// which surfaces as RenderTexture.Create() == false and NowSdfImageFields.Bake returning false -- an honest
// failure, not a wrong field.
//
// FILTERING is a separate question from renderability and fails separately. The pooled flood targets are
// FilterMode.Point by construction (NowSdfImageField.cs:448-449), so they need no float filtering at all. The
// resolved field is FilterMode.Bilinear; in WebGL2 the 16-bit float formats are texture-filterable in core, and
// only the 32-bit ones need OES_texture_float_linear -- which applySampler() already clamps to point, with a
// warning, rather than letting an incomplete texture read black.
//
// TEXTURE UNITS. This is the only program with a sampler that is NOT the blit source: `_SourceTex` is a material
// property carrying the sprite, while `_MainTex` is the previous ping-pong target that Graphics.Blit binds. It
// gets UNIT_SOURCE_TEX of its own and blit() binds it from the blit info, falling back to 1x1 white to match the
// shader's own Properties default.

// GENERATED from wwwroot/shaders/nowui-sdf-image.glsl by tools/embed-shader.py -- edit that file, not this.
const GLSL_SDF_IMAGE_COMMON = `
uniform highp sampler2D _MainTex;

uniform highp sampler2D _SourceTex;

uniform highp vec4 _SourceUv;

uniform highp vec4 _FieldParams;

uniform highp vec4 _FieldTexels;

uniform highp float _Step;

uniform highp vec4 _StampRect;

const highp vec4 NOW_SDF_FIELD_NO_SEGMENT = vec4(-1.0, -1.0, -1.0, -1.0);
const highp float NOW_SDF_FIELD_FAR = 1000000.0;
const highp float NOW_SDF_FIELD_MAX_DISTANCE = 30000.0;

highp vec2 FieldTexelCenter(highp vec2 uv)
{
    return floor(uv * _FieldTexels.xy) + 0.5;
}

highp float Threshold()
{
    return clamp(_FieldParams.w, 0.0001, 0.9999);
}

bool IsInside(highp float alpha)
{
    return alpha >= Threshold();
}

highp float SourceAlpha(highp vec2 texelCenter)
{
    highp vec2 spriteTexel = texelCenter - _FieldParams.z;
    highp vec2 spriteSize = max(_FieldParams.xy, 1.0);

    if (spriteTexel.x < 0.0 || spriteTexel.y < 0.0 ||
        spriteTexel.x > spriteSize.x || spriteTexel.y > spriteSize.y)
    {
        return 0.0;
    }

    highp vec2 sourceUv = _SourceUv.xy + spriteTexel / spriteSize * _SourceUv.zw;
    return textureLod(_SourceTex, sourceUv, 0.0).a;
}

highp vec2 Crossing(highp vec2 a, highp float alphaA, highp vec2 b, highp float alphaB)
{
    highp float t = clamp((Threshold() - alphaA) / (alphaB - alphaA), 0.0, 1.0);
    return mix(a, b, t);
}

highp float SegmentDistance(highp vec2 p, highp vec4 segment)
{
    highp vec2 a = segment.xy;
    highp vec2 ab = segment.zw - a;
    highp float lengthSquared = dot(ab, ab);
    highp float t = lengthSquared > 0.0 ? clamp(dot(p - a, ab) / lengthSquared, 0.0, 1.0) : 0.0;
    return length(p - (a + ab * t));
}

void Push(highp vec2 crossing, inout highp vec2 p0, inout highp vec2 p1, inout int count)
{
    if (count == 0)
        p0 = crossing;
    else if (count == 1)
        p1 = crossing;

    ++count;
}

void Consider(highp vec2 texel, highp vec4 candidate, inout highp vec4 best, inout highp float bestDistance)
{
    if (candidate.x < 0.0)
        return;

    highp float d = SegmentDistance(texel, candidate);

    if (d < bestDistance)
    {
        bestDistance = d;
        best = candidate;
    }
}
`;

// GENERATED from wwwroot/shaders/nowui-sdf-image.vert by tools/embed-shader.py -- edit that file, not this.
const GLSL_VERTEX_SDF_IMAGE = `#version 300 es

precision highp float;
precision highp int;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;

uniform mat4 nowui_MatrixMVP;

out highp vec2 vUv;

void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);
    vUv = aUv;
}
`;

// GENERATED from wwwroot/shaders/nowui-sdf-image-seed.frag by tools/embed-shader.py -- edit that file, not this.
const GLSL_FRAGMENT_SDF_IMAGE_SEED = `#version 300 es

precision highp float;
precision highp int;

${GLSL_SDF_IMAGE_COMMON}

in highp vec2 vUv;
out highp vec4 fragColor;

void main()
{
    highp vec2 c00 = FieldTexelCenter(vUv);
    highp vec2 c10 = c00 + vec2(1.0, 0.0);
    highp vec2 c01 = c00 + vec2(0.0, 1.0);
    highp vec2 c11 = c00 + vec2(1.0, 1.0);
    highp float a00 = SourceAlpha(c00);
    highp float a10 = SourceAlpha(c10);
    highp float a01 = SourceAlpha(c01);
    highp float a11 = SourceAlpha(c11);
    bool b00 = IsInside(a00);
    bool b10 = IsInside(a10);
    bool b01 = IsInside(a01);
    bool b11 = IsInside(a11);

    if (b00 == b10 && b00 == b01 && b00 == b11)
    {
        fragColor = NOW_SDF_FIELD_NO_SEGMENT;
        return;
    }

    highp vec2 p0 = vec2(0.0);
    highp vec2 p1 = vec2(0.0);
    int count = 0;

    if (b00 != b10)
        Push(Crossing(c00, a00, c10, a10), p0, p1, count);
    if (b10 != b11)
        Push(Crossing(c10, a10, c11, a11), p0, p1, count);
    if (b01 != b11)
        Push(Crossing(c01, a01, c11, a11), p0, p1, count);
    if (b00 != b01)
        Push(Crossing(c00, a00, c01, a01), p0, p1, count);

    fragColor = vec4(p0, p1);
}
`;

// GENERATED from wwwroot/shaders/nowui-sdf-image-flood.frag by tools/embed-shader.py -- edit that file, not this.
const GLSL_FRAGMENT_SDF_IMAGE_FLOOD = `#version 300 es

precision highp float;
precision highp int;

${GLSL_SDF_IMAGE_COMMON}

in highp vec2 vUv;
out highp vec4 fragColor;

void main()
{
    highp vec2 texel = FieldTexelCenter(vUv);
    highp vec4 current = textureLod(_MainTex, vUv, 0.0);
    highp vec4 best = NOW_SDF_FIELD_NO_SEGMENT;
    highp float bestDistance = NOW_SDF_FIELD_FAR;
    Consider(texel, current, best, bestDistance);

    for (int y = -1; y <= 1; ++y)
    {
        for (int x = -1; x <= 1; ++x)
        {
            if (x == 0 && y == 0)
                continue;

            highp vec2 neighbor = texel + vec2(float(x), float(y)) * _Step;

            if (neighbor.x < 0.0 || neighbor.y < 0.0 ||
                neighbor.x > _FieldTexels.x || neighbor.y > _FieldTexels.y)
            {
                continue;
            }

            highp vec4 candidate = textureLod(_MainTex, neighbor * _FieldTexels.zw, 0.0);
            Consider(texel, candidate, best, bestDistance);
        }
    }

    fragColor = best;
}
`;

// GENERATED from wwwroot/shaders/nowui-sdf-image-resolve.frag by tools/embed-shader.py -- edit that file, not this.
const GLSL_FRAGMENT_SDF_IMAGE_RESOLVE = `#version 300 es

precision highp float;
precision highp int;

${GLSL_SDF_IMAGE_COMMON}

in highp vec2 vUv;
out highp vec4 fragColor;

void main()
{
    highp vec2 texel = FieldTexelCenter(vUv);
    highp vec4 segment = textureLod(_MainTex, vUv, 0.0);
    bool inside = IsInside(SourceAlpha(texel));
    highp float dist = segment.x < 0.0
        ? NOW_SDF_FIELD_MAX_DISTANCE
        : min(SegmentDistance(texel, segment), NOW_SDF_FIELD_MAX_DISTANCE);
    fragColor = vec4(inside ? -dist : dist, 0.0, 0.0, 1.0);
}
`;

// GENERATED from wwwroot/shaders/nowui-sdf-image-stamp.frag by tools/embed-shader.py -- edit that file, not this.
const GLSL_FRAGMENT_SDF_IMAGE_STAMP = `#version 300 es

precision highp float;
precision highp int;

${GLSL_SDF_IMAGE_COMMON}

in highp vec2 vUv;
out highp vec4 fragColor;

void main()
{
    highp vec2 texel = floor(vUv * _FieldTexels.xy);
    highp vec2 local = texel - _StampRect.xy;

    if (local.x < 0.0 || local.y < 0.0 ||
        local.x >= _StampRect.z || local.y >= _StampRect.w)
    {
        discard;
    }

    highp vec2 sourceUv = _SourceUv.xy + (local + 0.5) / max(_StampRect.zw, 1.0) * _SourceUv.zw;
    fragColor = textureLod(_SourceTex, sourceUv, 0.0);
}
`;

// GENERATED from wwwroot/shaders/nowui-sdf-image-dilate.frag by tools/embed-shader.py -- edit that file, not this.
const GLSL_FRAGMENT_SDF_IMAGE_DILATE = `#version 300 es

precision highp float;
precision highp int;

${GLSL_SDF_IMAGE_COMMON}

in highp vec2 vUv;
out highp vec4 fragColor;

void main()
{
    highp vec2 spriteSize = max(_FieldParams.xy, 1.0);
    highp vec2 spriteTexel = floor(vUv * spriteSize) + 0.5;
    highp vec2 fieldTexel = spriteTexel + _FieldParams.z;
    highp vec4 own = textureLod(_SourceTex, _SourceUv.xy + spriteTexel / spriteSize * _SourceUv.zw, 0.0);
    highp vec4 segment = textureLod(_MainTex, fieldTexel * _FieldTexels.zw, 0.0);

    if (segment.x < 0.0)
    {
        fragColor = vec4(own.rgb, IsInside(own.a) ? own.a : 1.0);
        return;
    }

    highp vec2 a = segment.xy;
    highp vec2 ab = segment.zw - a;
    highp float lengthSquared = dot(ab, ab);
    highp float t = lengthSquared > 0.0 ? clamp(dot(fieldTexel - a, ab) / lengthSquared, 0.0, 1.0) : 0.0;
    highp vec2 nearest = a + ab * t;
    highp vec2 toward = nearest - fieldTexel;

    if (IsInside(own.a))
    {
        highp float edgeAlpha = mix(1.0, own.a, clamp(length(toward) - 1.0, 0.0, 1.0));
        fragColor = vec4(own.rgb, edgeAlpha);
        return;
    }

    highp float towardLength = max(length(toward), 0.0001);
    highp vec2 insidePoint = nearest + toward / towardLength * 0.75;
    highp vec2 insideSprite = clamp(insidePoint - _FieldParams.z, vec2(0.5), spriteSize - 0.5);
    highp vec4 edge = textureLod(_SourceTex, _SourceUv.xy + insideSprite / spriteSize * _SourceUv.zw, 0.0);
    fragColor = vec4(edge.rgb, 1.0);
}
`;


// Every program this backend can draw with, keyed by the Unity shader name Shader.Find resolves. A name that is
// not here makes resolveShader return 0, which is what makes Shader.Find return null upstream — the behaviour
// NowUI's own resolvers already handle.
//
// `vertex` is per-program on purpose: rectangle and text share one vertex stage because their HLSL vert bodies
// are line-for-line identical, and gradient and ripple do not because theirs are not.
export const PROGRAM_SOURCES = [
    { key: 'NowUI/UI Rectangle', vertex: GLSL_VERTEX, fragment: GLSL_FRAGMENT_RECTANGLE },
    { key: 'NowUI/Text Renderer', vertex: GLSL_VERTEX, fragment: GLSL_FRAGMENT_TEXT },
    { key: 'NowUI/UI Gradient', vertex: GLSL_VERTEX_GRADIENT, fragment: GLSL_FRAGMENT_GRADIENT },
    { key: 'NowUI/UI Ripple', vertex: GLSL_VERTEX_RIPPLE, fragment: GLSL_FRAGMENT_RIPPLE },
    { key: 'NowUI/UI Glass', vertex: GLSL_VERTEX_GLASS, fragment: GLSL_FRAGMENT_GLASS },
    { key: 'NowUI/Color Picker', vertex: GLSL_VERTEX_COLOR_PICKER, fragment: GLSL_FRAGMENT_COLOR_PICKER },
    { key: 'NowUI/UI Bezier', vertex: GLSL_VERTEX_BEZIER, fragment: GLSL_FRAGMENT_BEZIER },
    // One pass, as NowSdf.shader declares. NowSdfShaderV1.cginc is NOT a second pass and not a second entry:
    // the .shader includes V2 only, and a material declaring ABI 1 is refused by the backend rather than drawn
    // with V2 semantics that would silently differ.
    { key: 'NowUI/SDF Scene', vertex: GLSL_VERTEX_SDF, fragment: GLSL_FRAGMENT_SDF },
    // Four passes in the .shader file, ONE declared here. selectPass therefore fails loudly for 1..3 with the
    // pass count in the message, which is what is wanted: pass 1 is XR-only and unreachable, and passes 2-3
    // read Texture2DMS, which GLSL ES 3.00 has no type for. See the block above GLSL_VERTEX_GLASS_BLUR.
    { key: 'Hidden/NowUI/GlassBlur', passes: [
        { vertex: GLSL_VERTEX_GLASS_BLUR, fragment: GLSL_FRAGMENT_GLASS_BLUR },
    ] },
    // Five passes in NowSdfImageField.shader, FIVE declared here, in the SubShader's order -- Seed, Flood,
    // Resolve, Stamp, Dilate (:258-306). The order is the ABI: NowSdfImageFields.Bake and .Stamp address them by
    // literal index (0 at NowSdfImageField.cs:456, 1 at :462 and :470, 2 at :473, 4 at :474, 3 at :362), so a
    // reordering here would run the wrong pass with no error at all. All five share one vertex stage because
    // every Pass block declares the same `#pragma vertex vert_img`.
    { key: 'Hidden/NowUI/SDF Image Field', passes: [
        { vertex: GLSL_VERTEX_SDF_IMAGE, fragment: GLSL_FRAGMENT_SDF_IMAGE_SEED },
        { vertex: GLSL_VERTEX_SDF_IMAGE, fragment: GLSL_FRAGMENT_SDF_IMAGE_FLOOD },
        { vertex: GLSL_VERTEX_SDF_IMAGE, fragment: GLSL_FRAGMENT_SDF_IMAGE_RESOLVE },
        { vertex: GLSL_VERTEX_SDF_IMAGE, fragment: GLSL_FRAGMENT_SDF_IMAGE_STAMP },
        { vertex: GLSL_VERTEX_SDF_IMAGE, fragment: GLSL_FRAGMENT_SDF_IMAGE_DILATE },
    ] },
];

// Per-program render state, as each shader's own SubShader block declares it. A program ABSENT from this table
// uses the shared premultiplied source-over that init() sets: blendFunc(ONE, ONE_MINUS_SRC_ALPHA).
//
// Only two programs are present, and both would fail QUIETLY at the shared default rather than loudly:
//
//   NowUI/UI Glass          UIGlass.shader:35  Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
//     Separate colour and alpha, with the colour half NON-premultiplied — because UIGlass divides its
//     accumulated colour back out by coverage (:257) to keep tint and outline separable over an opaque
//     backdrop. Under the shared blend the panel comes out darkened by exactly its own alpha.
//
//   Hidden/NowUI/GlassBlur  UIGlassBlur.shader:7-9  (no Blend statement at all ⇒ blending DISABLED)
//     The blur ping-pongs between two pooled targets, so a composited pass would accumulate rather than
//     converge — a blur that brightens with radius. blit() already disables BLEND for its own reasons; this
//     entry records the requirement so the two cannot drift apart.
//
// `null` means disable BLEND. A { srcRGB, dstRGB, srcAlpha, dstAlpha } object means blendFuncSeparate.
// Mirrored as BLEND_MODES in wwwroot/shaders/nowui-glsl-include.js.
const BLEND_STATES = {
    'NowUI/UI Glass': { srcRGB: 'SRC_ALPHA', dstRGB: 'ONE_MINUS_SRC_ALPHA',
                        srcAlpha: 'ONE', dstAlpha: 'ONE_MINUS_SRC_ALPHA' },
    'Hidden/NowUI/GlassBlur': null,
    // NowSdf.shader:58  Blend SrcAlpha OneMinusSrcAlpha
    //   The third program that is not premultiplied, and the one whose wrongness under the shared blend is
    //   hardest to see: NowSdfShaderV2.cginc:1721 returns `col` with no `col.rgb *= col.a`, because the whole
    //   fragment stage composites its shadow, glow, outline, fill, inner shadow and contour layers with
    //   `alphaOver` (:1447), a STRAIGHT-alpha source-over. Drawn with ONE / ONE_MINUS_SRC_ALPHA every soft edge
    //   gets its coverage applied twice -- shapes keep their silhouette and merely darken, which reads as a
    //   colour choice. Unity's own statement applies the same function to colour and alpha, so this is
    //   blendFunc rather than blendFuncSeparate, spelled through the separate form the table already supports.
    'NowUI/SDF Scene': { srcRGB: 'SRC_ALPHA', dstRGB: 'ONE_MINUS_SRC_ALPHA',
                         srcAlpha: 'SRC_ALPHA', dstAlpha: 'ONE_MINUS_SRC_ALPHA' },
    // NowSdfImageField.shader:28  Blend Off
    //   Recorded even though blit() disables BLEND for every blit anyway, for the same reason GlassBlur's entry
    //   is: the requirement belongs to the shader, not to the code path that happens to satisfy it today. Every
    //   pass writes a value rather than a colour -- segment coordinates, a signed distance, a dilated texel --
    //   and compositing any of them against a pooled target's previous contents would be nonsense that still
    //   produced a picture.
    'Hidden/NowUI/SDF Image Field': null,
};

// The blend state currently programmed, as a shader name or '' for the shared default. draw() reapplies only on
// a change: blend state is global and sticky, so a glass draw would otherwise leave blendFuncSeparate in place
// for the next rectangle.
let appliedBlend = '';

function applyBlendState(shaderName) {
    const wanted = (shaderName in BLEND_STATES) ? shaderName : '';
    if (wanted === appliedBlend) return;
    appliedBlend = wanted;

    if (wanted === '') {
        gl.enable(gl.BLEND);
        gl.blendEquation(gl.FUNC_ADD);
        gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
        return;
    }

    const mode = BLEND_STATES[wanted];

    if (mode === null) {
        gl.disable(gl.BLEND);
        return;
    }

    gl.enable(gl.BLEND);
    gl.blendEquation(gl.FUNC_ADD);
    gl.blendFuncSeparate(gl[mode.srcRGB], gl[mode.dstRGB], gl[mode.srcAlpha], gl[mode.dstAlpha]);
}

// blit() and drawProcedural() bracket themselves with disable(BLEND)/enable(BLEND), which restores the ENABLE
// bit but not the function. Calling this afterwards, and at the top of every draw, is what keeps the two from
// interfering.
function invalidateBlendState() {
    appliedBlend = '\u0000';   // a name no shader can have, so the next applyBlendState always reprograms
}

// Slot map of the flat uniform block. Mirrored by WebGL2Backend.UniformSlots.
const U = {
    MVP: 0,                       // mat4, column major
    MAIN_TEX_ST: 16,              // vec4
    PREMULTIPLIED_TEXTURE: 20,    // float
    TEXT_SDF_ENCODING: 21,        // float
    MASK_COUNT: 22,               // float
    TEXTURE_MASK_COUNT: 23,       // float
    MASK_RECTS: 24,               // vec4[8]
    MASK_DATA: 56,                // vec4[8]
    MASK_PARAMS: 88,              // vec4[8]
    MASK_TRANSFORMS: 120,         // vec4[8]
    TEXTURE_MASK_RECTS: 152,      // vec4[2]
    TEXTURE_MASK_PARAMS: 160,     // vec4[2]
    TEXTURE_MASK_TRANSFORMS: 168, // vec4[2]
    GRADIENT_RAMP_TEXEL_SIZE: 176, // vec4 -- NowUI/UI Gradient only
    // Appended by the core-shader port. Everything at 180 and above is new; nothing below it moved, so a
    // mismatch between this table and WebGL2Backend.UniformSlots shows up as the block-length check in
    // toBlock() rather than as a silently wrong uniform.
    COLOR_PICKER_MODE: 180,        // float -- NowUI/Color Picker only. GL's zero default is a VALID mode
                                   //          (SaturationValue), so an unbridged _Mode renders all three
                                   //          pickers as three plausible squares. It must be resolved.
    GLASS_USE_BACKDROP: 181,       // float -- 0 on every path the browser host reaches today
    GLASS_USE_STEREO_BACKDROP: 182,// float -- always 0; feeds the unported-branch marker
    GLASS_MATERIAL_MODE: 183,      // float -- always 0 (its only writer is excluded from the standalone build)
    BACKDROP_UV_TRANSFORM: 184,    // vec4  -- MUST fall back to (1,1,0,0); zero collapses the backdrop to
                                   //          texel (0,0), which reads as a deliberate flat tint
    BLUR_TEXEL_SIZE: 188,          // vec4  -- (1/w, 1/h, w, h) of the blur SOURCE; zero makes the blur a copy
    BLUR_SOURCE_SCALE_OFFSET: 192, // vec4  -- MUST fall back to (1,1,0,0)
    BLUR_DIRECTION: 196,           // vec2  -- (step,0) or (0,step); zero makes the blur a copy
    // Appended by the SDF image-field port. Hidden/NowUI/SDF Image Field only; nothing below 198 moved.
    // NONE of these has a usable zero default, which is why every one is bridged rather than left at GL's:
    //   _SourceUv    zero .zw collapses the whole sprite onto its first texel
    //   _FieldParams zero .xy makes max(.xy, 1) a 1x1 sprite; zero .w clamps the threshold to 0.0001, i.e.
    //                "every texel is inside", and the field comes out uniformly negative
    //   _FieldTexels zero .xy makes FieldTexelCenter return 0.5 for EVERY fragment -- one texel's worth of
    //                field, smeared, which is a picture rather than a failure
    //   _Step        zero makes every jump-flood neighbour the texel itself, so the flood never propagates and
    //                the field is MAX_DISTANCE everywhere except on the contour
    //   _StampRect   zero .zw makes the stamp discard every fragment, leaving the atlas untouched
    SOURCE_UV: 198,               // vec4
    FIELD_PARAMS: 202,            // vec4
    FIELD_TEXELS: 206,            // vec4
    STAMP_RECT: 210,              // vec4
    STEP: 214,                    // float
    COUNT: 215,
};

// Texture units, fixed for the life of the backend (§7.4).
const UNIT_MAIN_TEX = 0;
const UNIT_TEXTURE_MASK0 = 1;
const UNIT_TEXTURE_MASK1 = 2;
// _NowGradientRampTexture: the 256x256 ramp atlas the TEXT shader's gradient branch samples. It is a shader
// GLOBAL, so it arrives in the draw info the C# side fills from NowRuntime.globals rather than from a material.
// Its fallback is the 1x1 opaque WHITE texture, which makes every ramp texel (1,1,1,1) and leaves fillColor at
// the glyph's flat colour — i.e. exactly the pre-port picture. That is why WebGL2Backend reports a text mesh
// that asks for a gradient while the global is unset, rather than letting the fallback speak for it.
const UNIT_GRADIENT_RAMP = 3;
// 4 is UIGlass's _NowBackdropTex. It is bound ONCE at init to the 1x1 opaque black fallback and never rebound,
// because no path the browser host can reach produces a backdrop: Now.StartUI drives the immediate path, which
// calls NowGlassRenderer.DisableBackdropGlobal() for every glass mesh. WebGL2 still invalidates a draw whose
// sampler points at an INCOMPLETE texture regardless of dynamic control flow, so the binding is not optional.
// When the retained NowRenderer path lands, this is where the backdrop render texture gets bound per draw.
const UNIT_BACKDROP_TEX = 4;
// NowUI/SDF Scene's own pair. Both are bound on every SDF draw -- to the scene's atlases when it has them and
// to the 1x1 opaque BLACK fallback when it does not, which matches NowSdf.shader:6-7's "black" defaults and
// NowSdfCache.Upload's own Texture2D.blackTexture substitution (NowSdf.cs:4881-4882). Binding is not optional
// even though no path reaches an atlas today: WebGL2 invalidates a draw whose sampler points at an incomplete
// texture regardless of whether control flow reaches the sample.
const UNIT_SDF_IMAGE_FIELD = 5;
const UNIT_SDF_IMAGE_COLOR = 6;
// Hidden/NowUI/SDF Image Field's _SourceTex: the sprite being baked. It is the only sampler in the port that is
// a MATERIAL PROPERTY on a blit rather than the blit's own source -- _MainTex there is the previous ping-pong
// target, which Graphics.Blit binds. blit() binds this one from the blit info, falling back to 1x1 white to
// match the shader's Properties default (NowSdfImageField.shader:16).
const UNIT_SOURCE_TEX = 7;

// ------------------------------------------------------------------------------------- the SDF uniform block
//
// A SECOND flat block, for NowUI/SDF Scene alone, mirrored by WebGL2Backend.SdfUniformSlots. It is separate
// from `U` rather than appended to it for one measured reason: the arrays are 480 vec4, so a combined block
// would be ~2180 floats and EVERY draw in the app -- a rectangle, a glyph run, a ripple -- would marshal 8.7 KB
// across the wasm boundary to fill uniforms its program does not declare. The C# side calls setSdfUniforms()
// immediately before an SDF draw and never otherwise.
//
// The capacities are STRUCTURAL. NowSdf.MaxShapes is 64 and NowSdf.MaxLayers is 16 (NowSdf.cs:2343-2344), the
// C# scratch arrays are exactly that long, and Material.SetVectorArray ships the whole array whatever the scene
// filled. Never size either from _SdfShapeCount or _SdfLayerCount.
const SDF_MAX_SHAPES = 64;
const SDF_MAX_LAYERS = 16;

const S = {
    DATA0: 0,                         // vec4[64]
    DATA1: 256,                       // vec4[64]
    DATA2: 512,                       // vec4[64]
    SHAPE_META: 768,                  // vec4[64]
    COLORS: 1024,                     // vec4[64]
    UVS: 1280,                        // vec4[64]
    IMAGE_UVS: 1536,                  // vec4[64]
    LAYER_DATA0: 1792,                // vec4[16]
    LAYER_DATA1: 1856,                // vec4[16]
    IMAGE_ATLAS_SIZE: 1920,           // vec4  -- (fieldW, fieldH, colorW, colorH); Upload writes Vector4.one
                                      //          when there is no atlas, and zero would divide the atlas uv
                                      //          by max(0,1) = 1 rather than fail, so the fallback matters
    OUTLINE: 1924,                    // vec4
    OUTLINE_COLOR: 1928,              // vec4
    GLOW: 1932,                       // vec4
    GLOW_COLOR: 1936,                 // vec4
    SHADOW: 1940,                     // vec4
    SHADOW_COLOR: 1944,               // vec4
    INNER_SHADOW: 1948,               // vec4
    INNER_SHADOW_COLOR: 1952,         // vec4
    EMBOSS: 1956,                     // vec4
    CONTOUR: 1960,                    // vec4
    CONTOUR_COLOR: 1964,              // vec4
    CONTOUR_MASK: 1968,               // vec4
    WARP: 1972,                       // vec4
    SHAPE_COUNT: 1976,                // float
    LAYER_COUNT: 1977,                // float
    FEATHER: 1978,                    // float
    TEXT_EFFECT_LIMIT: 1979,          // float -- zero is WRONG, not merely unset; see the C# fallback
    MASK_OUTPUT: 1980,                // float
    CANVAS_LAYOUT: 1981,              // float -- vertex stage; always 0 in this build and still bridged
    TIME: 1982,                       // float -- Unity's _Time.y, read only by the domain warp
    COUNT: 1983,
};

// The most recent setSdfUniforms() payload. Held rather than passed through draw() because the .NET MemoryView
// it arrives in is valid only for the duration of that call. `sdfBlockPending` is set by setSdfUniforms and
// consumed by draw, so a draw that forgot its upload fails loudly instead of reusing stale shapes.
const sdfBlock = new Float32Array(S.COUNT);
let sdfBlockPending = false;

// FilterMode / TextureWrapMode, as the shim's enums order them.
const FILTER_POINT = 0;
const WRAP_REPEAT = 0, WRAP_CLAMP = 1, WRAP_MIRROR = 2, WRAP_MIRROR_ONCE = 3;

// ---------------------------------------------------------------------------------------------- module state

let gl = null;
let canvas = null;
let whiteTexture = null;   // 1x1 opaque white — the _MainTex fallback
let blackTexture = null;   // 1x1 opaque black — the mask sampler fallback, matching NowMaskShader.Apply
const programs = new Map();  // shader name -> { program, uniforms }
const textures = new Map();  // instance id -> { tex, width, height, filter, wrapS, wrapT, mipCount }
const meshes = new Map();    // instance id -> { vao, vbo, ibo, offsets }
const warned = new Set();
let frameCount = 0;

// ---------------------------------------------------------------------------------------- render-target state
//
// instance id -> { fbo, tex, depthBuffer, width, height, levels, attachedLevel, autoGenerateMips, dirtyMips,
//                  filter, wrapS, wrapT, filterable, generation }
const renderTargets = new Map();

// Bumped whenever the GL objects underneath us die. Every render target carries the generation it was built
// under, so isRenderTextureLost() answers "your FBO is from a previous context" without needing a per-object
// query the API does not have.
let contextGeneration = 1;

// The target currently bound, as an instance id; 0 is the default framebuffer. Held so a blit can restore what
// it changed and so autoGenerateMips can fire on the way out of a target.
let boundTargetId = 0;

// Format capability, latched at init(). WebGL2 makes float and half-float textures sampleable in core but only
// RENDERABLE with an extension, and 32-bit float only LINEAR-filterable with another one, so a render target
// asking for RFloat can fail in two independent ways that both look like a black texture.
let colorBufferFloat = false;
let colorBufferHalfFloat = false;
let floatLinearFilter = false;

let blitQuad = null;      // VAO for the unit-square blit quad: attribute 0 position, attribute 1 uv
let emptyVao = null;      // VAO with nothing enabled, for DrawProcedural's gl_VertexID draws
let copyProgram = null;   // the internal texture-copy program used by a material-less blit
let scratchFbo = null;    // read framebuffer for CopyTexture

function warnOnce(key, message) {
    if (warned.has(key)) return;
    warned.add(key);
    console.warn('[NowUI.WebGL2] ' + message);
}

function fail(message) {
    const error = new Error('[NowUI.WebGL2] ' + message);
    console.error(error.message);
    throw error;
}

function requireGl() {
    if (gl === null) fail('init() has not run, or the WebGL2 context was lost.');
    return gl;
}

// A .NET MemoryView is valid only for the duration of the call. `slice()` copies it out; a plain typed array is
// accepted too so this file stays usable from a test harness that calls it directly.
function asBytes(view) {
    if (view === null || view === undefined) return new Uint8Array(0);
    if (typeof view.slice === 'function') return view.slice();
    return view;
}

function asInts(view) {
    if (view === null || view === undefined) return new Int32Array(0);
    if (typeof view.slice === 'function') return view.slice();
    return view;
}

// ---------------------------------------------------------------------------------------------- init

export function init(canvasSelector) {
    canvas = document.querySelector(canvasSelector);
    if (!canvas) fail(`no canvas matched the selector "${canvasSelector}".`);

    // §2.3 / §6.3. alpha and premultipliedAlpha move as a pair if a later slice wants a transparent page.
    gl = canvas.getContext('webgl2', {
        alpha: false,
        depth: false,               // neither shader writes or tests depth (§2.2)
        stencil: false,
        antialias: false,           // NowUI does its own analytic AA
        premultipliedAlpha: false,
        // Off by default because retaining the buffer costs a copy per frame. Opt in with ?capture=1 when something
        // needs to read the canvas back after the frame has been composited: canvas.toDataURL and gl.readPixels both
        // return an empty buffer otherwise, which looks exactly like "nothing rendered" rather than like a capture
        // problem. Golden-image comparison against Unity's harness renders will want this.
        preserveDrawingBuffer: new URLSearchParams(location.search).get('capture') === '1',
        powerPreference: 'high-performance',
    });

    if (!gl) fail('the browser did not give us a WebGL2 context.');

    // Unpack state, set once and never changed (§6.3). Only COLORSPACE_CONVERSION and ALIGNMENT differ from the
    // WebGL defaults, but all four are stated so a later reader does not have to know which.
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);

    // Render state for both programs, identical and constant (§2.1). Nothing below ever changes it.
    gl.disable(gl.DEPTH_TEST);
    gl.depthMask(false);
    gl.disable(gl.CULL_FACE);       // the projection negates Y, reversing apparent winding
    gl.disable(gl.STENCIL_TEST);
    gl.disable(gl.SCISSOR_TEST);
    gl.disable(gl.DITHER);
    gl.colorMask(true, true, true, true);
    gl.enable(gl.BLEND);
    gl.blendEquation(gl.FUNC_ADD);
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);   // premultiplied source-over

    whiteTexture = createSolidTexture(255, 255, 255, 255);
    blackTexture = createSolidTexture(0, 0, 0, 255);

    // UIGlass's _NowBackdropTex, bound ONCE and never rebound. No path the browser host can reach produces a
    // backdrop -- Now.StartUI drives the immediate path, which calls NowGlassRenderer.DisableBackdropGlobal()
    // for every glass mesh -- but WebGL2 invalidates a draw whose sampler points at an incomplete texture
    // regardless of dynamic control flow, so glass would not draw at all without it. 1x1 opaque black matches
    // UIGlass.shader:11's "black" Properties default. When the retained NowRenderer path lands, this becomes a
    // per-draw bindTextureUnit like the others.
    gl.activeTexture(gl.TEXTURE0 + UNIT_BACKDROP_TEX);
    gl.bindTexture(gl.TEXTURE_2D, blackTexture);
    gl.activeTexture(gl.TEXTURE0 + UNIT_MAIN_TEX);

    appliedBlend = '';   // matches the blendFunc programmed just above

    canvas.addEventListener('webglcontextlost', (e) => {
        e.preventDefault();

        // Every GL object created under the old context is gone. Forgetting them is what makes
        // isRenderTextureLost() tell the truth: RenderTexture.IsCreated() clears its created flag on a "lost"
        // answer, which is the signal NowSdf's device-loss recovery (NowSdf.cs:4731-4744) waits for. Textures,
        // meshes and programs are dropped for the same reason, so nothing hands a dead name back to the driver.
        contextGeneration++;
        renderTargets.clear();
        textures.clear();
        meshes.clear();
        programs.clear();
        blitQuad = null;
        emptyVao = null;
        copyProgram = null;
        scratchFbo = null;
        whiteTexture = null;
        blackTexture = null;
        boundTargetId = 0;

        console.error('[NowUI.WebGL2] WebGL context lost. Every GPU object has been forgotten; render targets ' +
                      'now report themselves lost. Re-creating the context itself belongs to the host.');
    });

    const debugInfo = gl.getExtension('WEBGL_debug_renderer_info');
    const renderer = debugInfo
        ? gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL)
        : gl.getParameter(gl.RENDERER);

    // Three separate questions, asked separately because they fail separately (§ resolveTargetFormat below).
    colorBufferFloat = !!gl.getExtension('EXT_color_buffer_float');
    colorBufferHalfFloat = colorBufferFloat || !!gl.getExtension('EXT_color_buffer_half_float');
    floatLinearFilter = !!gl.getExtension('OES_texture_float_linear');

    // Pipe-delimited rather than JSON so the C# side needs no serializer and stays trim-safe.
    return [
        gl.getParameter(gl.MAX_TEXTURE_SIZE),
        gl.getParameter(gl.MAX_SAMPLES),
        colorBufferFloat ? 1 : 0,
        String(renderer || 'WebGL2'),
        colorBufferHalfFloat ? 1 : 0,
        floatLinearFilter ? 1 : 0,
    ].join('|');
}

function createSolidTexture(r, g, b, a) {
    const tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    // RGBA8, never SRGB8_ALPHA8 (§6.3).
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([r, g, b, a]));
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_BASE_LEVEL, 0);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAX_LEVEL, 0);
    return tex;
}

// ---------------------------------------------------------------------------------------------- programs

function compile(type, source, label) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);

    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        const log = gl.getShaderInfoLog(shader);
        gl.deleteShader(shader);
        fail(`${label} failed to compile:\n${log}`);
    }

    return shader;
}

// Returns 1 when the program is known and linked, 0 when the name is not one this slice ports. 0 is what makes
// Shader.Find return null upstream, which NowUI's own resolvers already handle.
//
// A PROGRAM_SOURCES entry declares either `{ vertex, fragment }` for a single-pass shader or
// `{ passes: [{ vertex, fragment }, ...] }` for a multi-pass one, in the SubShader's pass order. Every ported
// program so far is single-pass; Hidden/NowUI/GlassBlur has four and Hidden/NowUI/SDF Image Field five, and both
// are addressed by pass index from Blit and DrawProcedural, so the index has to mean the same thing here as in
// the .shader file.
export function resolveShader(name) {
    requireGl();

    if (programs.has(name)) return 1;

    const source = PROGRAM_SOURCES.find((p) => p.key === name);
    if (!source) return 0;

    const declared = source.passes || [{ vertex: source.vertex, fragment: source.fragment }];
    const passes = declared.map((p, index) => linkPass(name, index, p.vertex, p.fragment));

    programs.set(name, { passes });
    return 1;
}

// One pass of one program: compile, link, resolve every uniform location the backend knows how to fill, and
// nail the sampler units down. Locations are resolved once because they are fixed for a linked program's life.
function linkPass(name, index, vertexSource, fragmentSource) {
    const label = index === 0 ? name : `${name} pass ${index}`;
    const vs = compile(gl.VERTEX_SHADER, vertexSource, `${label} vertex shader`);
    const fs = compile(gl.FRAGMENT_SHADER, fragmentSource, `${label} fragment shader`);
    const program = gl.createProgram();
    gl.attachShader(program, vs);
    gl.attachShader(program, fs);
    gl.linkProgram(program);

    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        const log = gl.getProgramInfoLog(program);
        gl.deleteProgram(program);
        fail(`${label} failed to link:\n${log}`);
    }

    gl.deleteShader(vs);
    gl.deleteShader(fs);

    const loc = (n) => gl.getUniformLocation(program, n);
    const uniforms = {
        mvp: loc('nowui_MatrixMVP'),
        mainTexST: loc('_MainTex_ST'),
        premultipliedTexture: loc('_NowPremultipliedTexture'),
        textSdfEncoding: loc('_NowUITextSdfEncoding'),
        maskCount: loc('_NowUIMaskCount'),
        textureMaskCount: loc('_NowUITextureMaskCount'),
        // The "[0]" suffix is how the base location of a uniform array is obtained.
        maskRects: loc('_NowUIMaskRects[0]'),
        maskData: loc('_NowUIMaskData[0]'),
        maskParams: loc('_NowUIMaskParams[0]'),
        maskTransforms: loc('_NowUIMaskTransforms[0]'),
        textureMaskRects: loc('_NowUITextureMaskRects[0]'),
        textureMaskParams: loc('_NowUITextureMaskParams[0]'),
        textureMaskTransforms: loc('_NowUITextureMaskTransforms[0]'),
        gradientRampTexelSize: loc('_NowGradientRampTexelSize'),
        colorPickerMode: loc('_Mode'),
        glassUseBackdrop: loc('_NowGlassUseBackdrop'),
        glassUseStereoBackdrop: loc('_NowGlassUseStereoBackdrop'),
        glassMaterialMode: loc('_NowMaterialGlassMode'),
        backdropUvTransform: loc('_NowBackdropUVTransform'),
        blurTexelSize: loc('_NowBlurTexelSize'),
        blurSourceScaleOffset: loc('_NowBlurSourceScaleOffset'),
        blurDirection: loc('_NowBlurDirection'),
        mainTex: loc('_MainTex'),
        backdropTex: loc('_NowBackdropTex'),
        blurSourceTex: loc('_NowBlurSourceTex'),
        textureMask0: loc('_NowUITextureMask0'),
        textureMask1: loc('_NowUITextureMask1'),
        // Declared only by NowUI/Text Renderer. It doubles as the flag that says this program has a gradient
        // branch at all, which is what gates the unit-3 bind in draw().
        gradientRampTexture: loc('_NowGradientRampTexture'),

        // NowUI/SDF Scene. `sdfData0` doubles as the flag that says this program reads the SDF block at all:
        // it is the first array the interpreter touches on every path, so a program that has it has all of
        // them, and a program that does not is not an SDF program.
        sdfData0: loc('_SdfData0[0]'),
        sdfData1: loc('_SdfData1[0]'),
        sdfData2: loc('_SdfData2[0]'),
        sdfShapeMeta: loc('_SdfShapeMeta[0]'),
        sdfColors: loc('_SdfColors[0]'),
        sdfUvs: loc('_SdfUvs[0]'),
        sdfImageUvs: loc('_SdfImageUvs[0]'),
        sdfLayerData0: loc('_SdfLayerData0[0]'),
        sdfLayerData1: loc('_SdfLayerData1[0]'),
        sdfImageAtlasSize: loc('_SdfImageAtlasSize'),
        sdfOutline: loc('_SdfOutline'),
        sdfOutlineColor: loc('_SdfOutlineColor'),
        sdfGlow: loc('_SdfGlow'),
        sdfGlowColor: loc('_SdfGlowColor'),
        sdfShadow: loc('_SdfShadow'),
        sdfShadowColor: loc('_SdfShadowColor'),
        sdfInnerShadow: loc('_SdfInnerShadow'),
        sdfInnerShadowColor: loc('_SdfInnerShadowColor'),
        sdfEmboss: loc('_SdfEmboss'),
        sdfContour: loc('_SdfContour'),
        sdfContourColor: loc('_SdfContourColor'),
        sdfContourMask: loc('_SdfContourMask'),
        sdfWarp: loc('_SdfWarp'),
        sdfShapeCount: loc('_SdfShapeCount'),
        sdfLayerCount: loc('_SdfLayerCount'),
        sdfFeather: loc('_SdfFeather'),
        sdfTextEffectLimit: loc('_SdfTextEffectLimit'),
        sdfMaskOutput: loc('_SdfMaskOutput'),
        sdfCanvasLayout: loc('_NowCanvasLayout'),
        sdfTime: loc('nowui_Time'),
        sdfImageField: loc('_SdfImageField'),
        sdfImageColor: loc('_SdfImageColor'),

        // Hidden/NowUI/SDF Image Field. Each of its five passes carries the whole shared CGINCLUDE block, so
        // every pass has every name in its source -- but a pass that never reads one has it optimised out and
        // its location comes back null, which the pushes below already guard on. Seed reads no _MainTex, Stamp
        // no _Step, Resolve no _StampRect; that is expected, not a symptom.
        sourceTex: loc('_SourceTex'),
        sourceUv: loc('_SourceUv'),
        fieldParams: loc('_FieldParams'),
        fieldTexels: loc('_FieldTexels'),
        stampRect: loc('_StampRect'),
        step: loc('_Step'),
    };

    // Sampler units are assigned once at link time and never shuffled.
    gl.useProgram(program);
    if (uniforms.mainTex) gl.uniform1i(uniforms.mainTex, UNIT_MAIN_TEX);
    // The blur's source IS the blit's source (NowGlassRenderer sets the global to the very texture it passes as
    // the Blit source), and blit() binds that to UNIT_MAIN_TEX — so the two share a unit by construction.
    if (uniforms.blurSourceTex) gl.uniform1i(uniforms.blurSourceTex, UNIT_MAIN_TEX);
    if (uniforms.textureMask0) gl.uniform1i(uniforms.textureMask0, UNIT_TEXTURE_MASK0);
    if (uniforms.textureMask1) gl.uniform1i(uniforms.textureMask1, UNIT_TEXTURE_MASK1);
    if (uniforms.gradientRampTexture) gl.uniform1i(uniforms.gradientRampTexture, UNIT_GRADIENT_RAMP);
    if (uniforms.backdropTex) gl.uniform1i(uniforms.backdropTex, UNIT_BACKDROP_TEX);
    if (uniforms.sdfImageField) gl.uniform1i(uniforms.sdfImageField, UNIT_SDF_IMAGE_FIELD);
    if (uniforms.sdfImageColor) gl.uniform1i(uniforms.sdfImageColor, UNIT_SDF_IMAGE_COLOR);
    if (uniforms.sourceTex) gl.uniform1i(uniforms.sourceTex, UNIT_SOURCE_TEX);
    gl.useProgram(null);

    return { program, uniforms };
}

// Unity's "pass -1" means every pass; the C# side collapses it to 0 before it gets here, so a negative index is
// a bug rather than a convention.
function selectPass(shaderName, pass) {
    const resolved = programs.get(shaderName);
    if (!resolved) fail(`shader "${shaderName}" was never resolved.`);

    if (pass < 0 || pass >= resolved.passes.length) {
        fail(`shader "${shaderName}" has ${resolved.passes.length} pass(es); pass ${pass} was asked for. A ` +
             'multi-pass program has to declare every pass in PROGRAM_SOURCES, in the SubShader order.');
    }

    return resolved.passes[pass];
}

// ---------------------------------------------------------------------------------------------- frame

export function beginFrame(frame) {
    requireGl();
    frameCount = frame;
    if (gl.isContextLost()) fail('the WebGL2 context is lost; nothing can be drawn.');
}

export function endFrame() {
    requireGl();
    // A target with autoGenerateMips that is still bound at the end of the frame would otherwise keep a stale
    // chain until the next time something binds away from it. Normally the frame ends on the back buffer and
    // bindTarget has already done this.
    flushPendingMips();
    // WebGL presents when the task yields to the browser, so there is nothing to swap. flush() only makes the
    // frame's cost land inside this call rather than at an arbitrary later point.
    gl.flush();
}

export function setViewport(x, y, width, height) {
    requireGl();
    gl.viewport(x, y, width, height);
}

export function clearTarget(clearColor, r, g, b, a) {
    requireGl();
    if (!clearColor) return;   // depth is never allocated (§2.2), so a depth-only clear has nothing to clear
    gl.clearColor(r, g, b, a);
    gl.clear(gl.COLOR_BUFFER_BIT);
}

// ---------------------------------------------------------------------------------------------- textures

function samplerFilters(filter, mipCount) {
    const magFilter = filter === FILTER_POINT ? gl.NEAREST : gl.LINEAR;
    let minFilter = magFilter;

    if (mipCount > 1) {
        if (filter === FILTER_POINT) minFilter = gl.NEAREST_MIPMAP_NEAREST;
        else if (filter === 2 /* Trilinear */) minFilter = gl.LINEAR_MIPMAP_LINEAR;
        else minFilter = gl.LINEAR_MIPMAP_NEAREST;
    }

    return { minFilter, magFilter };
}

function glWrap(mode) {
    switch (mode) {
        case WRAP_REPEAT: return gl.REPEAT;
        case WRAP_CLAMP: return gl.CLAMP_TO_EDGE;
        case WRAP_MIRROR: return gl.MIRRORED_REPEAT;
        case WRAP_MIRROR_ONCE: return gl.CLAMP_TO_EDGE;  // WebGL2 has no GL_MIRROR_CLAMP_TO_EDGE
        default: return gl.CLAMP_TO_EDGE;
    }
}

function applySampler(entry) {
    // A 32-bit float target is only LINEAR-filterable with OES_texture_float_linear. Without it, LINEAR makes
    // the texture INCOMPLETE and every sample returns black — which reads as "the pass wrote nothing" rather
    // than as a filtering problem. Clamping to NEAREST keeps the data visible and says so once.
    if (entry.filterable === false && entry.filter !== FILTER_POINT) {
        warnOnce('float-linear', 'a float render target asked for bilinear filtering, but ' +
                                 'OES_texture_float_linear is missing. Using point filtering instead; expect ' +
                                 'blockier results, not black ones.');
        entry = { ...entry, filter: FILTER_POINT };
    }

    const { minFilter, magFilter } = samplerFilters(entry.filter, entry.mipCount);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, minFilter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, magFilter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, glWrap(entry.wrapS));
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, glWrap(entry.wrapT));
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_BASE_LEVEL, 0);
    // Not optional: it keeps the texture complete if MIN_FILTER is ever flipped to a mip filter.
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAX_LEVEL, Math.max(0, entry.mipCount - 1));
}

function getOrCreateTexture(id) {
    let entry = textures.get(id);
    if (entry) return entry;
    entry = { tex: gl.createTexture(), width: 0, height: 0, filter: 1, wrapS: 1, wrapT: 1, mipCount: 1 };
    textures.set(id, entry);
    return entry;
}

// info = [width, height, dirtyX, dirtyY, dirtyW, dirtyH, filter, wrapS, wrapT, mipCount, generateMips]
// pixels is the FULL CPU store, bottom-up (row 0 = bottom), RGBA8. The dirty rect is in the same bottom-up
// texel space, which is also GL's, so nothing is flipped anywhere.
export function uploadTexture(id, info, pixels) {
    requireGl();
    const i = asInts(info);
    const width = i[0], height = i[1];
    const dirtyX = i[2], dirtyY = i[3], dirtyW = i[4], dirtyH = i[5];
    const mipCount = i[9], generateMips = i[10] !== 0;
    const bytes = asBytes(pixels);

    if (width <= 0 || height <= 0) fail(`uploadTexture: texture ${id} is ${width}x${height}.`);
    if (bytes.length < width * height * 4)
        fail(`uploadTexture: texture ${id} is ${width}x${height} RGBA8 but only ${bytes.length} bytes arrived.`);

    const entry = getOrCreateTexture(id);
    entry.filter = i[6];
    entry.wrapS = i[7];
    entry.wrapT = i[8];
    entry.mipCount = Math.max(1, mipCount);

    gl.activeTexture(gl.TEXTURE0 + UNIT_MAIN_TEX);
    gl.bindTexture(gl.TEXTURE_2D, entry.tex);

    const reallocate = entry.width !== width || entry.height !== height;
    const fullRect = dirtyX === 0 && dirtyY === 0 && dirtyW === width && dirtyH === height;

    if (reallocate || fullRect) {
        // RGBA8 internal format. SRGB8_ALPHA8 would make the sampler linearise on read, which neither shader
        // expects and neither can undo.
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE,
                      bytes.subarray(0, width * height * 4));
        entry.width = width;
        entry.height = height;
    } else if (dirtyW > 0 && dirtyH > 0) {
        // Upload a sub-rect straight out of the full store. ROW_LENGTH/SKIP_* are what make that possible
        // without a CPU-side crop.
        gl.pixelStorei(gl.UNPACK_ROW_LENGTH, width);
        gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, dirtyX);
        gl.pixelStorei(gl.UNPACK_SKIP_ROWS, dirtyY);
        gl.texSubImage2D(gl.TEXTURE_2D, 0, dirtyX, dirtyY, dirtyW, dirtyH, gl.RGBA, gl.UNSIGNED_BYTE,
                         bytes.subarray(0, width * height * 4));
        gl.pixelStorei(gl.UNPACK_ROW_LENGTH, 0);
        gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, 0);
        gl.pixelStorei(gl.UNPACK_SKIP_ROWS, 0);
    }

    // A texture created with mipChain:false has no mip level to update, so Unity's own Apply(updateMipmaps:true)
    // is a no-op there. Only generate when the texture actually carries a chain.
    if (generateMips && entry.mipCount > 1) gl.generateMipmap(gl.TEXTURE_2D);

    applySampler(entry);
}

// info = [filter, wrapS, wrapT, aniso, mipCount]
//
// Reaches render targets as well as plain textures, and must: NowSdfImageField assigns filterMode and wrapMode
// on every acquire from the temporary pool (NowSdfImageField.cs:448-451), and those handles are RenderTextures.
// Creating a second, empty plain-texture entry for the same id would shadow the real one and hand every later
// sampler binding a texture with no storage.
export function updateSampler(id, info) {
    requireGl();
    const i = asInts(info);
    const target = renderTargets.get(id);
    const entry = target || getOrCreateTexture(id);
    entry.filter = i[0];
    entry.wrapS = i[1];
    entry.wrapT = i[2];

    // A render target's level count is fixed at allocation (texStorage2D is immutable), so the sampler update
    // must not move it: only a plain texture re-reads its mip count here.
    if (!target) entry.mipCount = Math.max(1, i[4]);

    gl.activeTexture(gl.TEXTURE0 + UNIT_MAIN_TEX);
    gl.bindTexture(gl.TEXTURE_2D, entry.tex);
    applySampler(entry);
}

export function releaseTexture(id) {
    if (gl === null) return;

    // A RenderTexture's GPU object belongs to releaseRenderTexture, which owns the FBO and the depth buffer
    // alongside the colour texture. Deleting the texture from under it here would leave a live FBO with a dead
    // attachment.
    if (renderTargets.has(id)) return;

    const entry = textures.get(id);
    if (!entry) return;
    gl.deleteTexture(entry.tex);
    textures.delete(id);
}

// A sampleable texture by instance id, from either map. A RenderTexture IS a Texture upstream — the glass path
// samples the blurred target, and NowSdfImageField samples its ping-pong buffers — so a sampler binding has to
// look in both places or a render target reads as "never uploaded" and silently falls back.
// RENDER TARGETS ARE CHECKED FIRST, and that ordering is load-bearing. Texture.filterMode's setter calls
// UpdateSampler, and a caller that sets filterMode in an object initialiser does so BEFORE Create() — so
// updateSampler sees an id it has no render target for yet and, without this ordering, would leave a plain
// entry with no storage sitting in front of the real target. The symptom is the fallback texture and a
// "never been uploaded" warning for a target that was in fact rendered into. createRenderTexture also deletes
// any such entry, so this ordering is the belt to that braces.
function lookupSampleable(id) {
    const target = renderTargets.get(id);
    if (target && target.generation === contextGeneration) return target;

    return textures.get(id) || null;
}

// id === 0 means "nothing is bound". The shim never issues 0: instance ids start at -1 and decrement, so every
// real id is strictly negative. Mirrored as WebGL2Backend.NoTexture.
function bindTextureUnit(unit, id, fallback, label) {
    gl.activeTexture(gl.TEXTURE0 + unit);

    if (id === 0) {
        gl.bindTexture(gl.TEXTURE_2D, fallback);
        return;
    }

    const entry = lookupSampleable(id);

    if (!entry || entry.width === 0) {
        // Bound but never uploaded. Falling back keeps the frame drawable; the warning is what stops it from
        // being mistaken for a working binding.
        warnOnce(`tex:${id}`, `${label} is bound to texture ${id}, which has never been uploaded. Using the ` +
                              `built-in fallback. Something upstream bound a texture without calling Apply().`);
        gl.bindTexture(gl.TEXTURE_2D, fallback);
        return;
    }

    // Sampling the target you are drawing into is a feedback loop: GL leaves the result undefined and a driver
    // may report INVALID_OPERATION at draw time instead, which surfaces as a blank frame with no obvious cause.
    // Saying so once beats either.
    if (id === boundTargetId) {
        warnOnce(`feedback:${id}`,
                 `${label} samples render target ${id}, which is also the current draw target. GL leaves that ` +
                 `undefined. Something upstream needs a ping-pong pair here.`);
    }

    gl.bindTexture(gl.TEXTURE_2D, entry.tex);
}

// ---------------------------------------------------------------------------------------------- meshes

// header = [vertexByteLength, indexCount, off0..off8]  (byte offsets into payload; -1 means the stream is absent)
// payload = the nine vertex streams back to back, then the index bytes as UNSIGNED_INT.
export function uploadMesh(id, vertexCount, header, payload) {
    requireGl();
    const h = asInts(header);
    const bytes = asBytes(payload);
    const vertexByteLength = h[0];
    const indexCount = h[1];

    let entry = meshes.get(id);
    if (!entry) {
        entry = { vao: gl.createVertexArray(), vbo: gl.createBuffer(), ibo: gl.createBuffer(), offsets: null };
        meshes.set(id, entry);
    }

    gl.bindVertexArray(entry.vao);

    gl.bindBuffer(gl.ARRAY_BUFFER, entry.vbo);
    gl.bufferData(gl.ARRAY_BUFFER, bytes.subarray(0, vertexByteLength), gl.DYNAMIC_DRAW);

    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, entry.ibo);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, bytes.subarray(vertexByteLength), gl.DYNAMIC_DRAW);

    for (let a = 0; a < ATTR.length; ++a) {
        const offset = h[2 + a];

        if (offset < 0) {
            // NowUI's render layout always writes all nine streams. A missing one means the mesh was built by
            // something else, and a disabled attribute would read as the generic constant (0,0,0,1) — which for
            // the mask channel clips every fragment away. Better to say so.
            fail(`uploadMesh: mesh ${id} has no ${ATTR[a].name} stream. NowUI's render layout writes all nine.`);
        }

        gl.enableVertexAttribArray(a);
        gl.vertexAttribPointer(a, ATTR[a].size, gl.FLOAT, false, 0, offset);
    }

    gl.bindVertexArray(null);
    entry.indexCount = indexCount;
    entry.vertexCount = vertexCount;
}

export function releaseMesh(id) {
    if (gl === null) return;
    const entry = meshes.get(id);
    if (!entry) return;
    gl.deleteVertexArray(entry.vao);
    gl.deleteBuffer(entry.vbo);
    gl.deleteBuffer(entry.ibo);
    meshes.delete(id);
}

// ---------------------------------------------------------------------------------------------- draw

// info = [indexCount, mainTextureId, textureMask0Id, textureMask1Id, pass, sdfImageFieldId, sdfImageColorId,
//         gradientRampId]
// uniforms = the flat block, U.COUNT floats, as bytes.
export function draw(meshId, shaderName, info, uniforms) {
    requireGl();

    const entry = meshes.get(meshId);
    if (!entry) fail(`draw: mesh ${meshId} was never uploaded.`);

    const i = asInts(info);
    const indexCount = i[0];
    if (indexCount <= 0) return;

    const resolved = selectPass(shaderName, i[4]);
    const block = toBlock(uniforms);

    // A program that declares the SDF arrays MUST have had setSdfUniforms() called for THIS draw. Left at the
    // zeros the block starts life with, _SdfShapeCount and _SdfLayerCount are both 0 and the interpreter walks
    // nothing: a completely EMPTY scene, indistinguishable from "the shapes are wrong" or "the material never
    // uploaded". `sdfBlockPending` makes the omission an error instead. It is consumed here rather than merely
    // read, so a second SDF draw that forgot its own upload cannot ride on the first one's data.
    const wantsSdfBlock = resolved.uniforms.sdfData0 !== null;

    if (wantsSdfBlock && !sdfBlockPending) {
        fail(`draw: "${shaderName}" reads the SDF uniform block, but setSdfUniforms was not called for this ` +
             'draw. The C# side must call it immediately before every SDF draw; see WebGL2Backend.DrawMesh.');
    }

    sdfBlockPending = false;

    // Blend state is per-PROGRAM, not global: NowUI/UI Glass declares a separate colour/alpha function because
    // its fragment stage emits straight rather than premultiplied rgb. Everything else uses the shared
    // premultiplied source-over, and applyBlendState reprograms only on a change.
    applyBlendState(shaderName);

    gl.useProgram(resolved.program);
    applyUniformBlock(resolved.uniforms, block);

    if (wantsSdfBlock) applySdfUniformBlock(resolved.uniforms);

    bindTextureUnit(UNIT_MAIN_TEX, i[1], whiteTexture, '_MainTex');
    bindTextureUnit(UNIT_TEXTURE_MASK0, i[2], blackTexture, '_NowUITextureMask0');
    bindTextureUnit(UNIT_TEXTURE_MASK1, i[3], blackTexture, '_NowUITextureMask1');

    // Unit 3 exists only for the text program, and only because its gradient branch samples the ramp atlas.
    // WebGL2 invalidates a draw whose sampler points at an INCOMPLETE texture regardless of whether control
    // flow reaches the sample, so binding is not optional even for a mesh with no gradient vertex in it.
    if (resolved.uniforms.gradientRampTexture) {
        bindTextureUnit(UNIT_GRADIENT_RAMP, i[7], whiteTexture, '_NowGradientRampTexture');
    }

    // Units 5 and 6 exist only for this program, so they are bound only when it is the one drawing. i[5] and
    // i[6] are 0 whenever the scene has no image atlas, which is every scene this backend can build today --
    // "Hidden/NowUI/SDF Image Field", the jump-flood program that makes one, is not ported.
    if (wantsSdfBlock) {
        bindTextureUnit(UNIT_SDF_IMAGE_FIELD, i[5], blackTexture, '_SdfImageField');
        bindTextureUnit(UNIT_SDF_IMAGE_COLOR, i[6], blackTexture, '_SdfImageColor');
    }

    gl.bindVertexArray(entry.vao);
    gl.drawElements(gl.TRIANGLES, indexCount, gl.UNSIGNED_INT, 0);
    gl.bindVertexArray(null);
}

// Pushes the whole flat block into whichever of its uniforms the program actually declares. Shared by the mesh
// draw, the blit and the procedural draw so all three resolve uniforms identically — a blit through a material
// reads the same bag a mesh draw does, and a slot that behaved differently between them would be a silent
// divergence rather than an error.
function applyUniformBlock(u, block) {
    // transpose = false: the shim's Matrix4x4 stores column-major, which is what GLSL wants. If you find
    // yourself transposing, something else is wrong.
    if (u.mvp) gl.uniformMatrix4fv(u.mvp, false, block.subarray(U.MVP, U.MVP + 16));
    if (u.mainTexST) gl.uniform4fv(u.mainTexST, block.subarray(U.MAIN_TEX_ST, U.MAIN_TEX_ST + 4));
    if (u.premultipliedTexture) gl.uniform1f(u.premultipliedTexture, block[U.PREMULTIPLIED_TEXTURE]);
    if (u.textSdfEncoding) gl.uniform1f(u.textSdfEncoding, block[U.TEXT_SDF_ENCODING]);
    if (u.maskCount) gl.uniform1f(u.maskCount, block[U.MASK_COUNT]);
    if (u.textureMaskCount) gl.uniform1f(u.textureMaskCount, block[U.TEXTURE_MASK_COUNT]);
    if (u.maskRects) gl.uniform4fv(u.maskRects, block.subarray(U.MASK_RECTS, U.MASK_RECTS + 32));
    if (u.maskData) gl.uniform4fv(u.maskData, block.subarray(U.MASK_DATA, U.MASK_DATA + 32));
    if (u.maskParams) gl.uniform4fv(u.maskParams, block.subarray(U.MASK_PARAMS, U.MASK_PARAMS + 32));
    if (u.maskTransforms) gl.uniform4fv(u.maskTransforms, block.subarray(U.MASK_TRANSFORMS, U.MASK_TRANSFORMS + 32));
    if (u.textureMaskRects)
        gl.uniform4fv(u.textureMaskRects, block.subarray(U.TEXTURE_MASK_RECTS, U.TEXTURE_MASK_RECTS + 8));
    if (u.textureMaskParams)
        gl.uniform4fv(u.textureMaskParams, block.subarray(U.TEXTURE_MASK_PARAMS, U.TEXTURE_MASK_PARAMS + 8));
    if (u.textureMaskTransforms)
        gl.uniform4fv(u.textureMaskTransforms,
                      block.subarray(U.TEXTURE_MASK_TRANSFORMS, U.TEXTURE_MASK_TRANSFORMS + 8));

    if (u.gradientRampTexelSize) {
        gl.uniform4fv(u.gradientRampTexelSize,
                      block.subarray(U.GRADIENT_RAMP_TEXEL_SIZE, U.GRADIENT_RAMP_TEXEL_SIZE + 4));
    }

    if (u.colorPickerMode) gl.uniform1f(u.colorPickerMode, block[U.COLOR_PICKER_MODE]);
    if (u.glassUseBackdrop) gl.uniform1f(u.glassUseBackdrop, block[U.GLASS_USE_BACKDROP]);
    if (u.glassUseStereoBackdrop) gl.uniform1f(u.glassUseStereoBackdrop, block[U.GLASS_USE_STEREO_BACKDROP]);
    if (u.glassMaterialMode) gl.uniform1f(u.glassMaterialMode, block[U.GLASS_MATERIAL_MODE]);

    if (u.backdropUvTransform) {
        gl.uniform4fv(u.backdropUvTransform,
                      block.subarray(U.BACKDROP_UV_TRANSFORM, U.BACKDROP_UV_TRANSFORM + 4));
    }

    if (u.blurTexelSize) gl.uniform4fv(u.blurTexelSize, block.subarray(U.BLUR_TEXEL_SIZE, U.BLUR_TEXEL_SIZE + 4));

    if (u.blurSourceScaleOffset) {
        gl.uniform4fv(u.blurSourceScaleOffset,
                      block.subarray(U.BLUR_SOURCE_SCALE_OFFSET, U.BLUR_SOURCE_SCALE_OFFSET + 4));
    }

    if (u.blurDirection) gl.uniform2fv(u.blurDirection, block.subarray(U.BLUR_DIRECTION, U.BLUR_DIRECTION + 2));

    if (u.sourceUv) gl.uniform4fv(u.sourceUv, block.subarray(U.SOURCE_UV, U.SOURCE_UV + 4));
    if (u.fieldParams) gl.uniform4fv(u.fieldParams, block.subarray(U.FIELD_PARAMS, U.FIELD_PARAMS + 4));
    if (u.fieldTexels) gl.uniform4fv(u.fieldTexels, block.subarray(U.FIELD_TEXELS, U.FIELD_TEXELS + 4));
    if (u.stampRect) gl.uniform4fv(u.stampRect, block.subarray(U.STAMP_RECT, U.STAMP_RECT + 4));
    if (u.step) gl.uniform1f(u.step, block[U.STEP]);
}

/**
 * Hands the SDF scene block over for the draw that follows. Called by the C# side immediately before a
 * NowUI/SDF Scene draw and at no other time; draw() refuses an SDF program that did not get one.
 *
 * The MemoryView is valid only for this call, so the payload is copied into the module's own Float32Array
 * rather than retained.
 */
export function setSdfUniforms(uniforms) {
    requireGl();

    const bytes = asBytes(uniforms);
    const floats = new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);

    if (floats.length !== S.COUNT) {
        fail(`setSdfUniforms: the block is ${floats.length} floats; ${S.COUNT} were expected. The slot map ` +
             'here and WebGL2Backend.SdfUniformSlots have to move together.');
    }

    sdfBlock.set(floats);
    sdfBlockPending = true;
}

/**
 * Pushes the SDF block into the program. Every uniform is uploaded unconditionally rather than behind a
 * "did it change" test: the whole point of the separate block is that this runs only for SDF draws, of which
 * a frame has a handful, and a dirty-tracking scheme that got it wrong would show up as one scene wearing
 * another scene's shapes.
 *
 * The `[0]`-suffixed array locations take the WHOLE array in one call. Never slice them to the live shape
 * count -- see the capacity note above S.
 */
function applySdfUniformBlock(u) {
    const shapes = SDF_MAX_SHAPES * 4;
    const layers = SDF_MAX_LAYERS * 4;

    gl.uniform4fv(u.sdfData0, sdfBlock.subarray(S.DATA0, S.DATA0 + shapes));
    gl.uniform4fv(u.sdfData1, sdfBlock.subarray(S.DATA1, S.DATA1 + shapes));
    gl.uniform4fv(u.sdfData2, sdfBlock.subarray(S.DATA2, S.DATA2 + shapes));
    gl.uniform4fv(u.sdfShapeMeta, sdfBlock.subarray(S.SHAPE_META, S.SHAPE_META + shapes));
    gl.uniform4fv(u.sdfColors, sdfBlock.subarray(S.COLORS, S.COLORS + shapes));
    gl.uniform4fv(u.sdfUvs, sdfBlock.subarray(S.UVS, S.UVS + shapes));
    gl.uniform4fv(u.sdfImageUvs, sdfBlock.subarray(S.IMAGE_UVS, S.IMAGE_UVS + shapes));
    gl.uniform4fv(u.sdfLayerData0, sdfBlock.subarray(S.LAYER_DATA0, S.LAYER_DATA0 + layers));
    gl.uniform4fv(u.sdfLayerData1, sdfBlock.subarray(S.LAYER_DATA1, S.LAYER_DATA1 + layers));

    const vec4 = (loc, slot) => { if (loc) gl.uniform4fv(loc, sdfBlock.subarray(slot, slot + 4)); };
    vec4(u.sdfImageAtlasSize, S.IMAGE_ATLAS_SIZE);
    vec4(u.sdfOutline, S.OUTLINE);
    vec4(u.sdfOutlineColor, S.OUTLINE_COLOR);
    vec4(u.sdfGlow, S.GLOW);
    vec4(u.sdfGlowColor, S.GLOW_COLOR);
    vec4(u.sdfShadow, S.SHADOW);
    vec4(u.sdfShadowColor, S.SHADOW_COLOR);
    vec4(u.sdfInnerShadow, S.INNER_SHADOW);
    vec4(u.sdfInnerShadowColor, S.INNER_SHADOW_COLOR);
    vec4(u.sdfEmboss, S.EMBOSS);
    vec4(u.sdfContour, S.CONTOUR);
    vec4(u.sdfContourColor, S.CONTOUR_COLOR);
    vec4(u.sdfContourMask, S.CONTOUR_MASK);
    vec4(u.sdfWarp, S.WARP);

    if (u.sdfShapeCount) gl.uniform1f(u.sdfShapeCount, sdfBlock[S.SHAPE_COUNT]);
    if (u.sdfLayerCount) gl.uniform1f(u.sdfLayerCount, sdfBlock[S.LAYER_COUNT]);
    if (u.sdfFeather) gl.uniform1f(u.sdfFeather, sdfBlock[S.FEATHER]);
    if (u.sdfTextEffectLimit) gl.uniform1f(u.sdfTextEffectLimit, sdfBlock[S.TEXT_EFFECT_LIMIT]);
    if (u.sdfMaskOutput) gl.uniform1f(u.sdfMaskOutput, sdfBlock[S.MASK_OUTPUT]);
    if (u.sdfCanvasLayout) gl.uniform1f(u.sdfCanvasLayout, sdfBlock[S.CANVAS_LAYOUT]);
    if (u.sdfTime) gl.uniform1f(u.sdfTime, sdfBlock[S.TIME]);
}

// ---------------------------------------------------------------------------------------------- render targets
//
// THE ORIGIN QUESTION, ANSWERED ONCE. GL's framebuffer origin is bottom-left and NowUI's UI space is top-left,
// and render-to-texture is where people expect that to stop cancelling. It does not, and the derivation is in
// Docs/Standalone/M2-ShaderPort.md §8.1-§8.4 rather than in anybody's eyes:
//
//   * NowUI's projection is Matrix4x4.Ortho(0, W, -H, 0, -1, 100), so clip.y = 2*meshY/H + 1. Mesh space is the
//     negation of UI space, which puts UI top (uiY = 0, meshY = 0) at clip.y = +1 and UI bottom at clip.y = -1.
//   * GL maps clip.y = +1 to the HIGHEST y of the viewport. Into an FBO that is the highest texel ROW, which is
//     texture coordinate t = 1.
//   * NowUI samples every texture with Unity's bottom-up convention — row 0 at the bottom, t = 1 at the top. Its
//     own UI-to-UV conversion says so out loud: NowUIMask.cginc writes `vec2(normalized.x, 1.0 - normalized.y)`
//     with the comment "NowUI is top-left/y-down while texture UVs are bottom-left/y-up".
//
// So UI top -> clip +1 -> the last texel row -> t = 1 -> sampled back as UI top. The same cancellation as the
// back buffer, for the same reason, and it holds for an FBO because the viewport is the whole target and the
// projection is the same one. THERE IS NO FLIP IN THIS FILE and there must not be one: not in the projection,
// not in the viewport, not in the blit's uv, not in UNPACK_FLIP_Y_WEBGL. If a render-to-texture result comes out
// upside down, the bug is that something used a DIFFERENT projection or a partial viewport, not that a flip is
// missing.
//
// The blit agrees by construction: its quad maps uv (0,0) to NDC (-1,-1), the bottom-left of the destination and
// the first texel row of the source, so a copy is the identity in both conventions at once. Unity's own GL blit
// is the same mapping, which is why UIGlassBlur's `#if UNITY_UV_STARTS_AT_TOP` flip is compiled OUT on OpenGL
// and must stay out here.

// RenderTextureFormat (Enums/GraphicsEnums.cs) -> a WebGL2 sized internal format, plus the two capability
// questions that format raises. They are asked separately because they fail separately: `renderable` decides
// whether the FBO can exist at all, `filterable` only decides whether LINEAR is legal, and a format can be
// sampleable in core WebGL2 while being neither.
function resolveTargetFormat(format) {
    switch (format) {
        case 0:   // ARGB32
        case 7:   // Default
            return { internal: gl.RGBA8, renderable: true, filterable: true, name: 'ARGB32' };

        case 20:  // BGRA32. GL has no renderable BGRA sized format; channel order is a CPU-side concern and
                  // nothing in this backend round-trips a render target through the CPU, so RGBA8 is the honest
                  // allocation rather than a silent swizzle.
            return { internal: gl.RGBA8, renderable: true, filterable: true, name: 'BGRA32 (as RGBA8)' };

        case 16:  // R8
            return { internal: gl.R8, renderable: true, filterable: true, name: 'R8' };

        case 4:   // RGB565
            return { internal: gl.RGB565, renderable: true, filterable: true, name: 'RGB565' };

        case 8:   // ARGB2101010
            return { internal: gl.RGB10_A2, renderable: true, filterable: true, name: 'ARGB2101010' };

        case 2:   // ARGBHalf
        case 9:   // DefaultHDR
            return { internal: gl.RGBA16F, renderable: colorBufferHalfFloat, filterable: true, name: 'ARGBHalf' };

        case 13:  // RGHalf
            return { internal: gl.RG16F, renderable: colorBufferHalfFloat, filterable: true, name: 'RGHalf' };

        case 15:  // RHalf
            return { internal: gl.R16F, renderable: colorBufferHalfFloat, filterable: true, name: 'RHalf' };

        // 32-bit float needs EXT_color_buffer_float to be a render target and OES_texture_float_linear to be
        // filtered. NowSdfImageField prefers RHalf and falls back to RFloat, so this branch is reachable.
        case 11:  // ARGBFloat
            return { internal: gl.RGBA32F, renderable: colorBufferFloat, filterable: floatLinearFilter, name: 'ARGBFloat' };

        case 12:  // RGFloat
            return { internal: gl.RG32F, renderable: colorBufferFloat, filterable: floatLinearFilter, name: 'RGFloat' };

        case 14:  // RFloat
            return { internal: gl.R32F, renderable: colorBufferFloat, filterable: floatLinearFilter, name: 'RFloat' };

        default:
            return null;
    }
}

function fullMipLevels(width, height) {
    let levels = 1;
    let size = Math.max(width, height);

    while (size > 1) {
        size >>= 1;
        ++levels;
    }

    return levels;
}

// info = [width, height, depthBits, volumeDepth, mipCount, msaaSamples, format, dimension, filter, wrapS,
//         wrapT, useMipMap, autoGenerateMips]
// Returns 1 when the target exists on the GPU, 0 when the request cannot be honoured — which is exactly what
// INowRenderBackend.CreateRenderTexture's bool means, and what RenderTexture.Create() turns into `false`.
export function createRenderTexture(id, info) {
    requireGl();

    const i = asInts(info);
    const width = i[0], height = i[1], depthBits = i[2];
    const volumeDepth = i[3], msaaSamples = i[5], format = i[6], dimension = i[7];
    const filter = i[8], wrapS = i[9], wrapT = i[10];
    const useMipMap = i[11] !== 0, autoGenerateMips = i[12] !== 0;

    // Idempotent: the shim's RenderTexture.Create() already returns early on a created target, but a host that
    // re-creates after a context loss reaches here with the same id and must not leak the old objects.
    releaseRenderTexture(id);

    // A sampler update that arrived BEFORE the target was created minted an empty plain-texture entry under
    // this id — Texture.filterMode's setter calls UpdateSampler, and an object initialiser runs before
    // Create(). Drop it here rather than leaving an orphan GL texture behind for the life of the page.
    const stale = textures.get(id);

    if (stale) {
        gl.deleteTexture(stale.tex);
        textures.delete(id);
    }

    if (width <= 0 || height <= 0) {
        console.error(`[NowUI.WebGL2] createRenderTexture: target ${id} is ${width}x${height}.`);
        return 0;
    }

    // TextureDimension.Tex2D === 2. WebGL2 has TEXTURE_2D_ARRAY and framebufferTextureLayer, so array targets
    // are implementable — they are simply not implemented, because the only NowUI code that asks for one is
    // NowGlassRenderer's single-pass-instanced STEREO path, which no browser host can reach. Returning 0 here
    // makes that an honest allocation failure rather than a wrong render.
    if (dimension !== 2) {
        console.error(`[NowUI.WebGL2] createRenderTexture: target ${id} asks for TextureDimension ${dimension}. ` +
                      'Only Tex2D (2) is implemented; array, 3D and cube targets are not ported.');
        return 0;
    }

    if (volumeDepth > 1) {
        console.error(`[NowUI.WebGL2] createRenderTexture: target ${id} asks for ${volumeDepth} slices on a ` +
                      'Tex2D target.');
        return 0;
    }

    const resolved = resolveTargetFormat(format);

    if (!resolved) {
        console.error(`[NowUI.WebGL2] createRenderTexture: target ${id} asks for RenderTextureFormat ${format}, ` +
                      'which this backend has no WebGL2 internal format for.');
        return 0;
    }

    if (!resolved.renderable) {
        console.error(`[NowUI.WebGL2] createRenderTexture: target ${id} asks for ${resolved.name}, which this ` +
                      'context cannot render into (EXT_color_buffer_float / EXT_color_buffer_half_float is ' +
                      'missing). NowRenderCaps reports this, so a caller that checked ' +
                      'SystemInfo.SupportsRenderTextureFormat should never have got here.');
        return 0;
    }

    // WebGL2 has no multisampled TEXTURE, only multisampled renderbuffers, and a render target exists to be
    // sampled. Flattening to one sample is the only thing that can be done; caps reports maxMsaaSamples = 1 so
    // that NowUI never asks in the first place, and this stays as the backstop.
    if (msaaSamples > 1) {
        warnOnce('msaa', `a render target asked for ${msaaSamples}x MSAA. WebGL2 has no sampleable multisampled ` +
                         'texture, so it is allocated single-sampled. Edges inside render-to-texture will be ' +
                         'aliased unless the shader anti-aliases them, which every NowUI shader does.');
    }

    const levels = useMipMap ? fullMipLevels(width, height) : 1;
    const tex = gl.createTexture();

    gl.activeTexture(gl.TEXTURE0 + UNIT_MAIN_TEX);
    gl.bindTexture(gl.TEXTURE_2D, tex);
    // Immutable storage. texStorage2D over texImage2D because the level count and the sized format are then
    // fixed for the object's life, which is what makes "the FBO attachment matches the sampler view" a property
    // of allocation rather than something to keep in sync.
    gl.texStorage2D(gl.TEXTURE_2D, levels, resolved.internal, width, height);

    const entry = {
        tex,
        fbo: gl.createFramebuffer(),
        depthBuffer: null,
        width,
        height,
        levels,
        mipCount: levels,
        attachedLevel: 0,
        autoGenerateMips: autoGenerateMips && levels > 1,
        dirtyMips: false,
        filter,
        wrapS,
        wrapT,
        filterable: resolved.filterable,
        generation: contextGeneration,
    };

    applySampler(entry);

    gl.bindFramebuffer(gl.FRAMEBUFFER, entry.fbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);

    if (depthBits > 0) {
        // 32 means "depth plus stencil" in Unity's vocabulary, not 32 depth bits.
        const depthFormat = depthBits >= 32 ? gl.DEPTH24_STENCIL8
            : depthBits > 16 ? gl.DEPTH_COMPONENT24
            : gl.DEPTH_COMPONENT16;
        const attachment = depthBits >= 32 ? gl.DEPTH_STENCIL_ATTACHMENT : gl.DEPTH_ATTACHMENT;

        entry.depthBuffer = gl.createRenderbuffer();
        gl.bindRenderbuffer(gl.RENDERBUFFER, entry.depthBuffer);
        gl.renderbufferStorage(gl.RENDERBUFFER, depthFormat, width, height);
        gl.framebufferRenderbuffer(gl.FRAMEBUFFER, attachment, gl.RENDERBUFFER, entry.depthBuffer);
        gl.bindRenderbuffer(gl.RENDERBUFFER, null);
    }

    const status = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    boundTargetId = 0;

    if (status !== gl.FRAMEBUFFER_COMPLETE) {
        console.error(`[NowUI.WebGL2] createRenderTexture: target ${id} (${width}x${height} ${resolved.name}) ` +
                      `made an incomplete framebuffer (status 0x${status.toString(16)}).`);
        gl.deleteFramebuffer(entry.fbo);
        if (entry.depthBuffer) gl.deleteRenderbuffer(entry.depthBuffer);
        gl.deleteTexture(tex);
        return 0;
    }

    renderTargets.set(id, entry);
    return 1;
}

// 1 when the GPU object is gone underneath us. Two ways for that to be true: the context died and took every
// object with it, or the target was released. RenderTexture.IsCreated() clears its own created flag on a 1, so
// this is what makes NowSdf's device-loss recovery re-create rather than draw into a dead handle.
export function isRenderTextureLost(id) {
    if (gl === null || gl.isContextLost()) return 1;

    const entry = renderTargets.get(id);
    if (!entry) return 1;

    return entry.generation === contextGeneration ? 0 : 1;
}

export function releaseRenderTexture(id) {
    if (gl === null) return;

    const entry = renderTargets.get(id);
    if (!entry) return;

    if (boundTargetId === id) {
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        boundTargetId = 0;
    }

    gl.deleteFramebuffer(entry.fbo);
    if (entry.depthBuffer) gl.deleteRenderbuffer(entry.depthBuffer);
    gl.deleteTexture(entry.tex);
    renderTargets.delete(id);
}

// id === 0 is the default framebuffer, mirroring NowRenderTarget.isBackBuffer. The shim always follows this with
// setViewport (backend invariant 5), so nothing here sets one — except bindTarget's blit caller, which has no
// shim-side viewport to follow it.
export function setRenderTarget(id, mipLevel, depthSlice) {
    requireGl();

    if (depthSlice > 0) {
        fail(`setRenderTarget: target ${id} asks for depth slice ${depthSlice}. Array targets are not ported; ` +
             'createRenderTexture refuses them, so this should be unreachable.');
    }

    bindTarget(id, mipLevel);
}

// The one place a framebuffer is bound. Also the one place autoGenerateMips fires: Unity regenerates a target's
// mip chain when rendering into it finishes, and "finishes" is observable here and nowhere else.
function bindTarget(id, mipLevel) {
    if (boundTargetId !== id) flushPendingMips();

    if (id === 0) {
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        boundTargetId = 0;
        return null;
    }

    const entry = renderTargets.get(id);

    if (!entry || entry.generation !== contextGeneration) {
        fail(`setRenderTarget: render target ${id} was never created, or was lost with the context. ` +
             'RenderTexture.Create() has to succeed before the target can be bound.');
    }

    gl.bindFramebuffer(gl.FRAMEBUFFER, entry.fbo);

    const level = Math.max(0, mipLevel | 0);

    if (level !== entry.attachedLevel) {
        if (level >= entry.levels) {
            fail(`setRenderTarget: target ${id} has ${entry.levels} mip level(s); level ${level} was asked for.`);
        }

        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, entry.tex, level);
        entry.attachedLevel = level;
    }

    boundTargetId = id;
    if (entry.autoGenerateMips) entry.dirtyMips = true;
    return entry;
}

function flushPendingMips() {
    const entry = renderTargets.get(boundTargetId);
    if (!entry || !entry.dirtyMips) return;

    entry.dirtyMips = false;
    gl.activeTexture(gl.TEXTURE0 + UNIT_MAIN_TEX);
    gl.bindTexture(gl.TEXTURE_2D, entry.tex);
    gl.generateMipmap(gl.TEXTURE_2D);
}

// ---------------------------------------------------------------------------------------------- blit, procedural
//
// THE BLIT GEOMETRY CONTRACT, which a ported full-screen program has to match:
//   * attribute 0 `aPosition` is a vec3 in the unit square, z = 0;
//   * attribute 1 `aUv` is the matching vec2 in [0,1], with uv (0,0) at NDC (-1,-1) — the FIRST texel row of the
//     source and the BOTTOM row of the destination, which is Unity's mapping on OpenGL and the reason
//     UIGlassBlur's `#if UNITY_UV_STARTS_AT_TOP` branch is the one that stays compiled out;
//   * `nowui_MatrixMVP` carries Ortho(0, 1, 0, 1, -1, 100), built on the C# side with the shim's own Matrix4x4;
//   * `_MainTex_ST` carries the blit's (scale, offset), so a program applies it exactly as TRANSFORM_TEX does;
//   * attributes 2..8 are NOT enabled, so a program that declares them reads the generic constant (0,0,0,1).
//     Both current callers (Hidden/NowUI/GlassBlur, Hidden/NowUI/SDF Image Field) declare only position and uv.
//
// Blending is OFF for a blit and for a procedural draw. That is not a guess: both shaders that reach these paths
// are opaque — UIGlassBlur declares no Blend directive (Unity's opaque default) and NowSdfImageField.shader says
// `Blend Off` outright — and a composited blit would accumulate the previous contents of a pooled target.
const GLSL_VERTEX_BLIT = `#version 300 es
precision highp float;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;

uniform mat4 nowui_MatrixMVP;
uniform vec4 _MainTex_ST;

out vec2 vUv;

void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);
    vUv = aUv * _MainTex_ST.xy + _MainTex_ST.zw;
}
`;

// The material-less blit: a straight copy. No colour-space function, because there is nothing to convert — this
// moves texels, it does not compose them.
const GLSL_FRAGMENT_BLIT = `#version 300 es
precision highp float;

uniform highp sampler2D _MainTex;
in vec2 vUv;
out vec4 fragColor;

void main()
{
    fragColor = texture(_MainTex, vUv);
}
`;

function ensureBlitResources() {
    if (blitQuad === null) {
        // A triangle strip over the unit square: (0,0) (1,0) (0,1) (1,1). Position and uv are the same numbers,
        // interleaved as five floats per vertex.
        const quad = new Float32Array([
            0, 0, 0, 0, 0,
            1, 0, 0, 1, 0,
            0, 1, 0, 0, 1,
            1, 1, 0, 1, 1,
        ]);

        const vao = gl.createVertexArray();
        const vbo = gl.createBuffer();
        gl.bindVertexArray(vao);
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        gl.bufferData(gl.ARRAY_BUFFER, quad, gl.STATIC_DRAW);
        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 20, 0);
        gl.enableVertexAttribArray(1);
        gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 20, 12);
        gl.bindVertexArray(null);
        blitQuad = { vao, vbo };
    }

    if (copyProgram === null) {
        const vs = compile(gl.VERTEX_SHADER, GLSL_VERTEX_BLIT, 'internal blit vertex shader');
        const fs = compile(gl.FRAGMENT_SHADER, GLSL_FRAGMENT_BLIT, 'internal blit fragment shader');
        const program = gl.createProgram();
        gl.attachShader(program, vs);
        gl.attachShader(program, fs);
        gl.linkProgram(program);

        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
            const log = gl.getProgramInfoLog(program);
            gl.deleteProgram(program);
            fail(`the internal blit program failed to link:\n${log}`);
        }

        gl.deleteShader(vs);
        gl.deleteShader(fs);

        const uniforms = {
            mvp: gl.getUniformLocation(program, 'nowui_MatrixMVP'),
            mainTexST: gl.getUniformLocation(program, '_MainTex_ST'),
            mainTex: gl.getUniformLocation(program, '_MainTex'),
        };

        gl.useProgram(program);
        gl.uniform1i(uniforms.mainTex, UNIT_MAIN_TEX);
        gl.useProgram(null);

        copyProgram = { program, uniforms };
    }
}

function ensureEmptyVao() {
    if (emptyVao === null) emptyVao = gl.createVertexArray();
    return emptyVao;
}

function toBlock(uniforms) {
    const raw = asBytes(uniforms);
    const block = new Float32Array(raw.buffer, raw.byteOffset, raw.byteLength >> 2);

    if (block.length < U.COUNT)
        fail(`the uniform block is ${block.length} floats; ${U.COUNT} were expected.`);

    return block;
}

// info = [sourceId, destinationId, destinationMip, destinationWidth, destinationHeight, mask0Id, mask1Id, pass,
//         sourceTexId]
// shaderName is '' for a material-less copy. uniforms is the same flat block a mesh draw uses; its MVP slot
// carries the unit-quad ortho and its _MainTex_ST slot the blit's scale/offset, both built on the C# side.
//
// The destination is left BOUND afterwards, which is backend invariant 7 and Unity's convention — NowSdfImageField
// saves and restores RenderTexture.active around every blit precisely because of it. The viewport is set to the
// whole destination here, because the shim issues no SetViewport of its own after a blit
// (NowImmediate.AdoptBlitDestination binds nothing).
export function blit(shaderName, info, uniforms) {
    requireGl();

    const i = asInts(info);
    const sourceId = i[0], destinationId = i[1], destinationMip = i[2];
    const destinationWidth = i[3], destinationHeight = i[4];

    ensureBlitResources();

    const resolved = shaderName ? selectPass(shaderName, i[7]) : copyProgram;

    bindTarget(destinationId, destinationMip);
    gl.viewport(0, 0, destinationWidth, destinationHeight);

    const block = toBlock(uniforms);
    gl.useProgram(resolved.program);

    if (resolved === copyProgram) {
        gl.uniformMatrix4fv(resolved.uniforms.mvp, false, block.subarray(U.MVP, U.MVP + 16));
        gl.uniform4fv(resolved.uniforms.mainTexST, block.subarray(U.MAIN_TEX_ST, U.MAIN_TEX_ST + 4));
    } else {
        applyUniformBlock(resolved.uniforms, block);
    }

    bindTextureUnit(UNIT_MAIN_TEX, sourceId, whiteTexture, '_MainTex');
    bindTextureUnit(UNIT_TEXTURE_MASK0, i[5], blackTexture, '_NowUITextureMask0');
    bindTextureUnit(UNIT_TEXTURE_MASK1, i[6], blackTexture, '_NowUITextureMask1');
    // Hidden/NowUI/SDF Image Field's sprite. Bound unconditionally, like the mask units: WebGL2 invalidates a
    // draw whose sampler points at an incomplete texture whether or not control flow reaches the sample, so a
    // program that merely DECLARES _SourceTex needs a binding. 1x1 white is the shader's own default.
    bindTextureUnit(UNIT_SOURCE_TEX, i[8], whiteTexture, '_SourceTex');

    gl.disable(gl.BLEND);
    gl.bindVertexArray(blitQuad.vao);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    gl.bindVertexArray(null);
    gl.enable(gl.BLEND);
    // The ENABLE bit is restored above but the FUNCTION is not tracked by that, and a preceding glass draw may
    // have left blendFuncSeparate programmed. Forcing the next draw to reprogram is one comparison.
    invalidateBlendState();
}

function glTopology(topology) {
    switch (topology) {
        case 0: return gl.TRIANGLES;
        case 3: return gl.LINES;
        case 4: return gl.LINE_STRIP;
        case 5: return gl.POINTS;
        // MeshTopology.Quads === 2. GLES 3.0 has no GL_QUADS at all, so there is nothing to map it to.
        default: return -1;
    }
}

// info = [topology, vertexCount, instanceCount, mainTextureId, mask0Id, mask1Id, pass]
//
// A vertex-less draw: no VAO attributes are enabled, so the vertex shader has nothing but gl_VertexID, which is
// what UIGlassBlur's full-screen passes are written against (`float2 positionUV = float2((vertexID << 1) & 2,
// vertexID & 2)` produces the standard oversized triangle). GLSL ES 3.00 spells SV_VertexID `gl_VertexID`.
export function drawProcedural(shaderName, info, uniforms) {
    requireGl();

    const i = asInts(info);
    const resolved = selectPass(shaderName, i[6]);
    const mode = glTopology(i[0]);
    const vertexCount = i[1];
    const instanceCount = i[2];

    if (mode < 0) fail(`drawProcedural: MeshTopology ${i[0]} has no WebGL2 equivalent.`);
    if (vertexCount <= 0 || instanceCount <= 0) return;

    const block = toBlock(uniforms);
    gl.useProgram(resolved.program);
    applyUniformBlock(resolved.uniforms, block);

    bindTextureUnit(UNIT_MAIN_TEX, i[3], whiteTexture, '_MainTex');
    bindTextureUnit(UNIT_TEXTURE_MASK0, i[4], blackTexture, '_NowUITextureMask0');
    bindTextureUnit(UNIT_TEXTURE_MASK1, i[5], blackTexture, '_NowUITextureMask1');

    gl.disable(gl.BLEND);
    gl.bindVertexArray(ensureEmptyVao());

    if (instanceCount > 1) gl.drawArraysInstanced(mode, 0, vertexCount, instanceCount);
    else gl.drawArrays(mode, 0, vertexCount);

    gl.bindVertexArray(null);
    gl.enable(gl.BLEND);
    invalidateBlendState();   // see the note in blit()
}

// ---------------------------------------------------------------------------------------------- copy
//
// A GPU-side copy with no shader: attach the source to a read framebuffer and copyTexSubImage2D out of it. That
// needs the SOURCE to be colour-renderable, which every format this backend allocates is, and it needs the two
// to be the same size — Unity's CopyTexture requires matching dimensions too.
export function copyTexture(sourceId, destinationId) {
    requireGl();

    const source = lookupSampleable(sourceId);
    if (!source || source.width === 0) fail(`copyTexture: source texture ${sourceId} has no GPU storage.`);

    const destination = lookupSampleable(destinationId);
    if (!destination) fail(`copyTexture: destination texture ${destinationId} has no GPU object.`);

    if (destination.width !== 0 && (destination.width !== source.width || destination.height !== source.height)) {
        fail(`copyTexture: source ${sourceId} is ${source.width}x${source.height} and destination ` +
             `${destinationId} is ${destination.width}x${destination.height}. A copy does not rescale.`);
    }

    if (scratchFbo === null) scratchFbo = gl.createFramebuffer();

    // Captured before the scratch framebuffer displaces them, and defensively: the previous target may have been
    // released between being bound and this call.
    const previousEntry = renderTargets.get(boundTargetId);
    const previousTarget = previousEntry ? boundTargetId : 0;
    const previousLevel = previousEntry ? previousEntry.attachedLevel : 0;

    gl.bindFramebuffer(gl.FRAMEBUFFER, scratchFbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, source.tex, 0);

    const status = gl.checkFramebufferStatus(gl.FRAMEBUFFER);

    if (status !== gl.FRAMEBUFFER_COMPLETE) {
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, null, 0);
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        boundTargetId = 0;
        fail(`copyTexture: source ${sourceId} cannot be attached to a framebuffer (status ` +
             `0x${status.toString(16)}), so there is nothing to copy out of.`);
    }

    gl.activeTexture(gl.TEXTURE0 + UNIT_MAIN_TEX);
    gl.bindTexture(gl.TEXTURE_2D, destination.tex);

    if (destination.width === 0) {
        // A plain texture that has never been uploaded has no storage. copyTexImage2D allocates and copies in
        // one call; RGBA8 because that is the only format this backend uploads plain textures as.
        gl.copyTexImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, 0, 0, source.width, source.height, 0);
        destination.width = source.width;
        destination.height = source.height;
        applySampler(destination);
    } else {
        gl.copyTexSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 0, 0, source.width, source.height);
    }

    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, null, 0);

    // Restore whatever was bound, because CopyTexture is not a target-binding operation: unlike Blit it leaves
    // the caller's render target alone, and the shim's activeTarget still names it.
    boundTargetId = -1;   // force bindTarget past its no-op check without firing a mip regeneration
    bindTarget(previousTarget, previousLevel);
}

// ---------------------------------------------------------------------------------------------- diagnostics

// Returns 0 when the GL error queue is clean. The C# side calls this only when diagnostics are switched on:
// getError() is a pipeline stall and must not sit in the steady-state path.
export function getError() {
    if (gl === null) return 0;
    return gl.getError();
}
