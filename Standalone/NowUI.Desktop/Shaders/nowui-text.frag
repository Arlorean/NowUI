#version 300 es
// ===========================================================================
// nowui-text.frag
//
// GLSL ES 3.00 port of median() + `frag` from
// Assets/NowUI/Assets/Shaders/TxtRenderer.shader (shader "NowUI/Text
// Renderer"). Every block cites the HLSL line it came from.
//
// COMPOSITION: `//#include "..."` lines are substituted by
// nowui-glsl-include.js before gl.shaderSource.
//
// NOTHING IS STUBBED. The gradient branch (HLSL :142-146) is now ported: its
// maths lives in nowui-text-gradient.glsl, included below, and the sampler it
// needs -- _NowGradientRampTexture, a SHADER GLOBAL published by
// Shader.SetGlobalTexture (NowGradient.cs:580), so it arrives through
// NowRuntime.globals and never through a material bag -- is declared in that
// file alongside its only reader. M2-ShaderPort.md section 4.5 permitted slice
// 1 to omit it on condition that a text vertex with extras.w != 0 be reported;
// the report fired, and this is the fix it asked for. WebGL2Backend keeps a
// narrower guard in its place: it now reports a gradient vertex that arrives
// when the ramp global is MISSING, which is the only way this branch can still
// render plausible flat text.
//
// TWO INCLUDES, AND THE ORDER MATTERS. nowui-text-gradient.glsl calls
// NowUIColorToWorkingSpace, so nowui-colorspace.glsl has to precede it. That
// makes this the second fragment stage in the port (after nowui-gradient.frag)
// to need the colour-space include, and for the same reason: it converts a
// colour that did not exist at vertex time.
//
// PRECISION -- the single most load-bearing decision in this file. The packed
// SDF16 reconstruction on HLSL :122 is
//     (msd.r * 256.0 + msd.b) / 257.0
// which rebuilds a 16-bit distance from two 8-bit channels of an RGBA8 texture
// read through LINEAR filtering. mediump has a 10-bit mantissa and destroys it
// outright. The fragment language has NO default float precision and defaults
// sampler2D to LOWP, so BOTH declarations below are load-bearing, not
// decoration. The atlas must also be a genuine RGBA8 normalised texture with
// LINEAR min and mag filters and no mip chain.
// ===========================================================================

precision highp float;
precision highp int;
precision highp sampler2D;

//#include "nowui-colorspace.glsl"
//#include "nowui-text-gradient.glsl"
//#include "nowui-mask.glsl"

// ---------------------------------------------------------------------------
// Uniforms. TxtRenderer.shader:71-73.
//   :71 sampler2D _MainTex              -- the font atlas, texture unit 0
//   :72 float4    _MainTex_ST           -- vertex stage only, see the .vert
//   :73 float     _NowUITextSdfEncoding -- ported below
//
// _NowUITextSdfEncoding IS 1 IN THIS BUILD, not the 0 that materials.json
// records as the asset's authored value. NowFont.cs:3555-3558 sets
// usePackedManagedSdf16 from material.HasProperty(_NowUITextSdfEncoding), which
// TxtMaterial declares, so the managed baker packs SDF16 and NowFont.cs:3647
// writes 1 before the first text draw. The packed branch is the live one;
// taking the median branch renders text that is blurry and subtly wrong, which
// is the failure mode that looks like "close enough". If the backend ever
// observes 0 here, that is a finding to chase, not something to paper over.
//
// _NowUITextOutlineOnlyPass appears in the Properties block and in
// shaders.json with default 1, but THE SHADER NEVER READS IT. It is a CPU-side
// capability flag (NowFont.supportsOutlineOnlyPass) that decides whether to
// emit a second draw. Bind nothing for it. Likewise _ZTest: render state, not
// a uniform, and always 8 (Always) in this build.
// ---------------------------------------------------------------------------
uniform highp sampler2D _MainTex;
uniform highp float _NowUITextSdfEncoding;

// Varyings -- must match nowui-text.vert's `out` block exactly.
in highp vec2 vUv;
in highp vec4 vRect;
// vRadius.xyz is the gradient payload, not corner radii. Read only by the
// gradient branch, where it becomes the first three components of the vec4
// NowUITextGradientSample takes.
in highp vec4 vRadius;
in highp vec4 vColor;
in highp vec4 vOutlineColor;
in highp vec4 vExtras;
in highp vec4 vMask;
in highp vec4 vRawUV;

// TxtRenderer.shader:96 `float4 frag(...) : SV_Target` becomes an explicit out.
out highp vec4 fragColor;

// TxtRenderer.shader:92-94. The MSDF median: the middle of the three channels.
// Unused while _NowUITextSdfEncoding is 1, but kept because the HLSL keeps it
// and because it is the branch a non-packed atlas would take.
highp float median(highp float r, highp float g, highp float b)
{
    return max(min(r, g), min(max(r, g), b));
}

void main()
{
    // TxtRenderer.shader:99-100
    highp vec4 rect = vRect;
    highp vec4 mask = vMask;

    // TxtRenderer.shader:102-103. Same float4*float2 truncation as in
    // UIRectangle: HLSL writes `i.rawUV * rect.zw`, GLSL needs the explicit
    // .xy. rect.zw is used ONLY to rebuild the position here -- unlike
    // UIRectangle there is no shape SDF, so it is never a `size`.
    highp vec2 pos = rect.xy + vRawUV.xy * rect.zw;
    highp vec2 uiPosition = vec2(pos.x, -pos.y);

    // TxtRenderer.shader:105-106 -- the legacy hard clip. May discard.
    NowUIClipLegacyRect(uiPosition, mask);

    // TxtRenderer.shader:109-110. tex2D -> texture. `msd` is the raw atlas
    // sample, whose meaning depends on the encoding branch below.
    highp float outline = vExtras.x;
    highp vec4 msd = texture(_MainTex, vUv);

    // TxtRenderer.shader:112-118. extras.y is the distance-field range in
    // LOCAL units; convert it to actual screen pixels so canvas scale and
    // transform scale keep text crisp.
    //
    // unitsPerPixel is the average of the local-unit lengths of the x and y
    // screen-derivative vectors -- i.e. how many local units one screen pixel
    // spans. Note gradX packs the x-derivatives OF pos.x (dFdx and dFdy), so
    // it measures how pos.x moves per pixel in both screen directions; gradY
    // does the same for pos.y. Averaging the two lengths gives an isotropic
    // scale that survives rotation.
    //
    // A NEGATIVE extras.y is not a negative range -- it is the flag for the
    // outline-only pass. Take abs() for the magnitude, and the sign separately.
    highp vec2 gradX = vec2(dFdx(pos.x), dFdy(pos.x));
    highp vec2 gradY = vec2(dFdx(pos.y), dFdy(pos.y));
    highp float unitsPerPixel = max(0.5 * (length(gradX) + length(gradY)), 1e-5);
    bool outlineOnly = vExtras.y < 0.0;
    highp float screenPxRange = max(abs(vExtras.y) / unitsPerPixel, 1.0);

    // TxtRenderer.shader:120-123. THE ENCODING BRANCH.
    //   packed SDF16: the distance is stored across two 8-bit channels, high
    //     byte in R and low byte in B, and (r*256 + b)/257 rebuilds it into
    //     [0,1]. The divisor is 257, not 256: with r and b both at 255 the
    //     numerator is 255*256 + 255 = 65535 = 255*257, so 257 is exactly what
    //     maps full-scale to 1.0.
    //   MSDF: the median of R, G and B, the standard multi-channel decode.
    // This build takes the packed branch.
    bool packedSdf16 = _NowUITextSdfEncoding > 0.5;
    highp float sd = packedSdf16
        ? (msd.r * 256.0 + msd.b) / 257.0
        : median(msd.r, msd.g, msd.b);

    // TxtRenderer.shader:125-130. The field's 0.5 iso-line is the glyph edge,
    // so (sd - 0.5) is a signed distance in field units and multiplying by
    // screenPxRange converts it to screen pixels.
    //
    // MTSDF alpha is the TRUE signed distance and stays stable far from
    // corners, where median-RGB is optimised for the fill edge instead -- hence
    // the switch to msd.a for the outline, but only when there IS an outline
    // and only on a non-packed atlas (a packed page has no alpha channel to
    // spare). `outline == 0` is an exact test; || short-circuits the same way
    // in both languages, and || binds tighter than ?: in both.
    highp float screenPxDistance = screenPxRange * (sd - 0.5);
    highp float outlineSd = (outline == 0.0 || packedSdf16) ? sd : msd.a;
    highp float screenPxDistanceOutline =
        screenPxRange * (outlineSd - 0.5) + outline / unitsPerPixel;

    // TxtRenderer.shader:132-138. A large RGBA8 field can represent more than
    // one screen pixel per stored distance code; widen its coverage ramp to at
    // least one code step so the edge does not quantise into visible steps.
    // Packed managed pages have 65535 codes, so the ratio is tiny and the
    // max(1.0, ...) keeps the normal one-pixel ramp.
    // saturate -> clamp(x, 0.0, 1.0).
    highp float distanceCodeCount = packedSdf16 ? 65535.0 : 255.0;
    highp float aaWidth = max(1.0, screenPxRange / distanceCodeCount);
    highp float opacity = clamp(screenPxDistance / aaWidth + 0.5, 0.0, 1.0);
    highp float outlineOp = clamp(screenPxDistanceOutline / aaWidth + 0.5, 0.0, 1.0);

    // TxtRenderer.shader:140-146. The gradient branch, now ported.
    //
    // extras.w is the encoded ramp and 0 means "no gradient", so the test is
    // the whole gate -- row 0 of the atlas is a magenta/black checker rather
    // than a usable ramp, and entering with 0 would draw it.
    //
    // `fillColor *=` is a MULTIPLY, not a replace: the glyph's flat colour
    // still tints the sampled ramp, and its alpha still scales the ramp's. A
    // white glyph shows the ramp unmodified, which is what the gallery draws;
    // a tinted one modulates it. Assigning instead would look right in the
    // common case and be wrong everywhere else.
    highp vec4 fillColor = vColor;

    if (vExtras.w > 0.0)
    {
        highp vec4 gradientPayload = vec4(vRadius.xyz, vExtras.z);
        fillColor *= NowUITextGradientSample(uiPosition, gradientPayload, vExtras.w);
    }

    // TxtRenderer.shader:148-163
    highp vec4 color;

    if (outlineOnly)
    {
        // TxtRenderer.shader:150-158. This is the second, outline-only draw
        // NowFont emits when the material advertises the capability. It must
        // produce the coverage which, AFTER the fill pass composites
        // source-over on top, leaves the opaque union equal to outlineOp
        // without painting the glyph face. Solving
        //     opacity + ring * (1 - opacity) = outlineOp
        // for ring gives (outlineOp - opacity) / (1 - opacity); the 1e-5 floor
        // keeps a fully opaque face from dividing by zero.
        highp float remainingFill = max(1.0 - opacity, 1e-5);
        highp float ringCoverage = clamp((outlineOp - opacity) / remainingFill, 0.0, 1.0);
        color = vOutlineColor;
        color.a *= ringCoverage;
    }
    else
    {
        // TxtRenderer.shader:161-162. lerp -> mix (identical semantics).
        // A NEGATIVE outline is an INNER outline, so it interpolates on
        // outlineOp rather than opacity. Alpha is the union of the two
        // coverages.
        color = outline == 0.0
            ? fillColor
            : mix(vOutlineColor, fillColor, outline < 0.0 ? outlineOp : opacity);
        color.a *= max(opacity, outlineOp);
    }

    // TxtRenderer.shader:165-166. NOTE THE ASYMMETRY WITH UIRectangle: this
    // shader applies mask coverage to ALPHA ONLY and premultiplies afterwards,
    // while UIRectangle multiplies an already-premultiplied col by it. Both are
    // correct for their own shader; do not "unify" them.
    color.a *= NowUIMaskCoverage(uiPosition);
    color.rgb *= color.a;

    // TxtRenderer.shader:168. There is deliberately NO clip(col.a - 0.001)
    // here, unlike UIRectangle:156 -- this shader returns fully transparent
    // fragments and relies on the blend. Do not add the discard.
    fragColor = color;
}
