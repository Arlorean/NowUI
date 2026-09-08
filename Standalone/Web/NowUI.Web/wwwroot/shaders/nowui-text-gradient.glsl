#ifndef NOWUI_TEXT_GRADIENT_INCLUDED
#define NOWUI_TEXT_GRADIENT_INCLUDED
// ===========================================================================
// nowui-text-gradient.glsl
//
// GLSL ES 3.00 port of Assets/NowUI/Assets/Shaders/NowUITextGradient.cginc
// (71 lines), the gradient fill for NowUI/Text Renderer. Line references below
// are into that file.
//
// This closes the omission M2-ShaderPort.md section 4.5 permitted slice 1 to
// make. Nothing here is stubbed: all three gradient kinds, all three spread
// modes, both ramp modes and the circle-vs-ellipse radial variant are ported.
//
// REQUIRES nowui-colorspace.glsl (for NowUIColorToWorkingSpace), and it must be
// included BEFORE this file. It is a fragment-stage include: it samples a
// texture, so never include it from a vertex stage.
//
// -------------------------------------------------------------- the payload
//
// Two vertex streams and one shader global carry a text gradient. Nothing
// arrives through a material property, which is what makes this path different
// from NowUI/UI Gradient's:
//
//   vRadius.xyz + vExtras.z  the payload, assembled by the caller as
//                            float4(i.radius.xyz, i.extras.z). On the text
//                            shader TEXCOORD2 is NOT corner radii.
//   vExtras.w                the encoded ramp: `row + (flags + 0.5) / 256`,
//                            written by NowGradient.TryResolveTextGradient
//                            (NowGradient.cs:864). Zero means "no gradient" and
//                            the caller must not enter this code at all -- row
//                            0 of the atlas is a magenta/black checker, not a
//                            usable ramp (NowGradient.cs:573-576).
//   _NowGradientRampTexture  the 256x256 ramp atlas, ONE ROW PER RAMP. It is a
//                            SHADER GLOBAL -- Shader.SetGlobalTexture at
//                            NowGradient.cs:580, from EnsureTexture, i.e. on
//                            the first ramp ALLOCATION -- so it reaches the
//                            backend through NowRuntime.globals and never
//                            through a material bag or a property block. It is
//                            declared here rather than in nowui-text.frag so
//                            that the sampler and its only reader live in one
//                            file.
//
// The payload is in UI space: absolute, not per-glyph, so one gradient runs
// continuously across every glyph and every font page of a string
// (NowGradient.cs:763-767). The caller passes `uiPosition`, which the text
// fragment stage has already rebuilt as (pos.x, -pos.y).
//
// ------------------------------------------------ the four HLSL->GLSL traps
//
// M2-ShaderPort.md section 4.5 named three; porting turned up a fourth.
//
//   1. frac  -> fract. Both are x - floor(x), so this one is a rename.
//   2. fmod  -> hlslFmod below, NOT mod. HLSL's fmod truncates toward zero and
//      keeps the sign of the dividend; GLSL's mod floors. Every operand the
//      cginc passes is non-negative, where the two agree -- but the helper is
//      written out anyway, because the next shader ported will have a negative
//      one and `mod` would then be quietly wrong.
//   3. atan2(y, x) -> atan(y, x). The argument ORDER is the same; only the
//      name differs. HLSL :42 is atan2(delta.x, -delta.y), so the GLSL is
//      atan(delta.x, -delta.y) -- y = delta.x, x = -delta.y. Writing
//      atan(-delta.y, delta.x) would rotate the conic sweep by 90 degrees,
//      which still looks like a conic gradient.
//   4. tex2D -> texture, and `sampler2D` must be declared highp. GLSL ES gives
//      a sampler2D default precision of LOWP in the fragment stage, which
//      would quantise the ramp on the way in.
// ===========================================================================

uniform highp sampler2D _NowGradientRampTexture;

// HLSL fmod, spelled out. See trap 2 above.
highp float hlslFmod(highp float x, highp float y)
{
    return x - y * trunc(x / y);
}

// NowUITextGradient.cginc:9-19. The spread mode, applied to the raw gradient
// coordinate. Matches NowGradientSpread's order exactly:
//   0 Clamp   saturate  -- the ramp holds at both ends
//   1 Repeat  frac      -- sawtooth, period 1
//   2 Mirror            -- triangle wave of period 2, so the ramp reflects
// Anything >= 1.5 takes the mirror branch, as in the HLSL: there is no fourth
// mode and no default case.
highp float NowUITextGradientApplySpread(highp float t, highp float spread)
{
    if (spread < 0.5)
        return clamp(t, 0.0, 1.0);

    if (spread < 1.5)
        return fract(t);

    return 1.0 - abs(fract(t * 0.5) * 2.0 - 1.0);
}

// NowUITextGradient.cginc:21-24. The whole flag byte, recovered from the
// fractional part of the encoded ramp. The +0.5 the CPU added
// (NowGradient.cs:864) centres the byte inside its 1/256 slot so a float
// round-trip cannot land it one below.
highp float NowUITextGradientFlags(highp float encodedRamp)
{
    return floor(fract(encodedRamp) * 256.0);
}

// NowUITextGradient.cginc:26-46. The raw gradient coordinate, before the
// spread. `flags` bit layout, from NowGradient.cs:
//   bits 0-1  kind    0 Linear, 1 Radial, 2 Conic
//   bits 2-3  spread  read by the caller, not here
//   bit  4    circle  radial only: one radius rather than two
//   bit  5    fixed   ramp mode; read by the caller, not here
highp float NowUITextGradientPosition(
    highp vec2 uiPosition,
    highp vec4 payload,
    highp float flags)
{
    highp float kind = hlslFmod(flags, 4.0);

    // Linear. The CPU has already baked direction, extent AND repetitions into
    // the affine coefficients (NowGradient.cs:800-820), so there is no
    // repetition multiply here -- unlike the conic branch below, which does
    // carry one. Do not "restore" symmetry between the two.
    if (kind < 0.5)
        return dot(uiPosition, payload.xy) + payload.z;

    // Radial. payload.xy is the centre in UI space; .zw the radii. The circle
    // flag makes it .zz, i.e. one radius on both axes, so the field is a true
    // circle rather than an ellipse fitted to the bounds.
    if (kind < 1.5)
    {
        highp float circle = hlslFmod(floor(flags / 16.0), 2.0);
        highp vec2 radii = circle > 0.5 ? payload.zz : payload.zw;
        return length((uiPosition - payload.xy) / max(abs(radii), vec2(0.0001)));
    }

    // Conic. CSS convention: zero points UP and positive turns rotate
    // CLOCKWISE in NowUI's positive-y-down coordinate system, which is what
    // the (delta.x, -delta.y) argument pair buys. payload.z is the start angle
    // in turns and payload.w the repetition count.
    highp vec2 delta = uiPosition - payload.xy;
    highp float turns = atan(delta.x, -delta.y) / 6.28318530718;
    return fract(turns - payload.z) * payload.w;
}

// NowUITextGradient.cginc:48-70. One texel out of the shared 256x256 atlas.
//
// The two 256.0 constants are LITERAL in the cginc, where NowUI/UI Gradient
// reads its own atlas dimensions from _NowGradientRampTexelSize. That is not
// an oversight to be tidied up: the text path has no such uniform to resolve,
// and the atlas is fixed at 256x256 by NowGradientRampCache.TextureWidth and
// .TextureHeight (NowGradient.cs:399-403). Porting the literals keeps this
// program independent of a uniform it would otherwise have to bridge.
//
// `fixed` mode is Unity's GradientMode.Fixed: snap to the nearest of the 256
// texel CENTRES so the ramp reads as hard bands. Blend mode leaves the index
// continuous and lets the atlas's own bilinear filter interpolate.
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

    // 255.0, not 256.0: t = 1 must land on the LAST texel, not one past it.
    highp float rampIndex = fixedMode > 0.5
        ? floor(t * 255.0 + 0.5)
        : t * 255.0;
    highp vec2 rampUV = vec2(
        (rampIndex + 0.5) / 256.0,
        (row + 0.5) / 256.0);
    highp vec4 ramp = texture(_NowGradientRampTexture, rampUV);

    // The atlas stores authored colours, so it converts on the way in exactly
    // as a vertex colour does. Identity under Gamma, which is this build.
    ramp.rgb = NowUIColorToWorkingSpace(ramp.rgb);
    return ramp;
}

#endif // NOWUI_TEXT_GRADIENT_INCLUDED
