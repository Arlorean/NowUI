#version 300 es
// ===========================================================================
// nowui-glassblur.frag
//
// GLSL ES 3.00 port of the fragment stage of PASS 0 of
// Assets/NowUI/Assets/Shaders/UIGlassBlur.shader (shader
// "Hidden/NowUI/GlassBlur"). Every block cites the HLSL line it came from.
// See nowui-glassblur.vert's header for which of the four passes are ported,
// which are unreachable, and which are impossible on WebGL2.
//
// COMPOSITION: no includes. This pass has no colours to convert and no mask.
//
// ---------------------------------------------------------------------------
// RENDER STATE. Different from every UI program, and easy to get wrong.
// ---------------------------------------------------------------------------
// The SubShader block (:7-9) is
//     Cull Off
//     ZWrite Off
//     ZTest Always
// and -- the part that matters -- there is NO `Blend` line at all. A pass with
// no Blend statement has blending DISABLED, i.e. it REPLACES the destination.
// Every other NowUI shader declares `Blend One OneMinusSrcAlpha` (or, for
// UIGlass, a separate variant). Running this pass with blending left enabled
// would composite each blur iteration over the previous contents of the
// scratch target instead of overwriting it, and since the ping-pong reuses two
// targets across iterations (NowGlassRenderer.cs:238-250) the result would
// accumulate rather than converge -- a blur that gets brighter with radius.
// nowui-gl.js carries `blend: null` for this program so the state and the
// shader travel together.
//
// ALPHA IS BLURRED TOO. The kernel multiplies float4s, so the source's alpha
// goes through the same Gaussian as its rgb. That is correct for a backdrop
// captured as premultiplied colour and must not be "fixed" by forcing alpha
// to 1: NowGlassRenderer.ClearCopyDestination (:952-959) clears the copy
// destination to Color.clear precisely so that the transparent margin around a
// cropped capture bleeds inward as transparency rather than as black.
//
// ---------------------------------------------------------------------------
// THE KERNEL. 17 taps in 9 samples, and the numbers are not adjustable.
// ---------------------------------------------------------------------------
// HLSL :29 calls it a "bilinear-optimized 17-tap Gaussian, applied separably
// by C#". Each of the eight off-centre samples sits at a FRACTIONAL texel
// offset so that the GPU's own bilinear filter blends two adjacent taps into
// one fetch; the weight is then the sum of that pair's Gaussian weights. The
// offsets (1.4765796511, 3.4455295350, 5.4148988458, 7.3849121445) and the
// weights (0.1031526189, 0.1910108131, 0.1404289078, 0.0807154625,
// 0.0362685072) are a matched set -- rounding one, or reordering them, or
// "simplifying" the doubled weights into a loop with a computed offset,
// produces a different filter. They are transcribed here digit for digit.
//
// The weights sum to 0.1031526189 + 2*(0.1910108131 + 0.1404289078 +
// 0.0807154625 + 0.0362685072) = 1.0000000001, i.e. unity to within float
// precision, so the pass is energy preserving and repeated iterations do not
// drift in brightness.
//
// THE BILINEAR REQUIREMENT IS A REQUIREMENT. The whole point of the fractional
// offsets is that the sampler interpolates. The source render texture MUST be
// bound with TEXTURE_MIN_FILTER and TEXTURE_MAG_FILTER = LINEAR and
// CLAMP_TO_EDGE wrapping (NowGlassBackdropSurface.CreateTexture authors it
// bilinear and clamped). With NEAREST every fractional offset collapses onto
// one of its two texels and the kernel silently becomes a different, lumpier
// filter -- a blur that still looks like a blur, which is the failure mode
// worth naming.
//
// PRECISION: highp float and highp sampler2D. The offsets are up to 7.38
// texels of a target that can be 2048 wide; at mediump the products
// `direction * texelSize * 7.3849121445` quantise onto texel boundaries and
// the bilinear optimisation above evaporates. The fragment language defaults
// sampler2D to LOWP, so declaring it is what keeps the taps at full precision.
// ===========================================================================

precision highp float;
precision highp int;
precision highp sampler2D;

// ---------------------------------------------------------------------------
// Uniforms. UIGlassBlur pass 0 declares four (:20-23), and ALL FOUR arrive as
// SHADER GLOBALS, not as material properties: NowGlassRenderer writes them
// with commandBuffer.SetGlobalTexture / SetGlobalVector (:860-891), so they
// land in NowRuntime.globals and the backend must resolve them from there
// (M2-ShaderPort.md section 7.3 step 3). Reading them from the blur
// material's bag would find nothing.
//
//   :20  sampler2D _NowBlurSourceTex
//          the image being blurred. Written by BlitBlur (:889). It is also the
//          `source` argument of the Blit itself, so a backend implementing
//          Blit can bind it from either -- they are the same texture by
//          construction, and if they ever differ the global is the one the
//          shader means.
//
//   :21  float4 _NowBlurTexelSize   (1/width, 1/height, width, height)
//          of the SOURCE, written by BlitBlur (:861-867) from the source
//          dimensions, NOT from the destination's. **GL's all-zero default is
//          catastrophic here and quietly so**: a zero .xy makes every tap
//          offset zero, so all nine samples land on the same texel, the
//          weights sum to one, and the "blur" is a pixel-perfect COPY. The
//          glass then renders with a sharp backdrop and looks merely like a
//          tint that did not take. The backend must resolve it and must not
//          fall back to zero.
//
//   :22  float4 _NowBlurSourceScaleOffset  (scaleX, scaleY, offsetX, offsetY)
//          applied to the incoming uv (:27). It is how a CROPPED capture is
//          addressed inside a larger source: ComposeSourceScaleOffset
//          (NowGlassRenderer.cs:235) folds the capture's sub-rect into it, and
//          the plain ping-pong iterations pass (1, 1, 0, 0). **Zero is again
//          not a safe default** -- a zero .xy collapses every fragment onto the
//          single texel at .zw, giving a flat fill. Fall back to (1, 1, 0, 0),
//          which is what BlitBlur's own two-argument overload (:826) passes.
//
//   :23  float2 _NowBlurDirection
//          (step, 0) for the horizontal half of an iteration and (0, step) for
//          the vertical, set immediately before each Blit
//          (NowGlassRenderer.cs:245 and :247). Separability is done by the C#,
//          not by this shader: the shader blurs along ONE axis per invocation
//          and knows nothing about the pair. Zero here is the same failure as
//          a zero texel size -- a copy that passes for a blur.
//
// Declared as a vec4 rather than a vec2 would be wrong: HLSL says float2 and
// the backend uploads two floats. Keep the type.
//
// Texture unit: this program has exactly one sampler and it is the only thing
// it binds, so unit 0 is used -- the same unit _MainTex occupies on the UI
// programs (M2-ShaderPort.md section 7.4). There is no conflict because no
// draw uses both programs, and it is also what makes the blit path work
// unchanged: nowui-gl.js's blit() binds the blit's SOURCE texture to
// UNIT_MAIN_TEX, and for this pass the source and _NowBlurSourceTex are the
// same texture by construction (NowGlassRenderer.cs:889-891 sets the global to
// the very texture it then passes as the Blit source).
// ---------------------------------------------------------------------------
uniform highp sampler2D _NowBlurSourceTex;
uniform highp vec4 _NowBlurTexelSize;
uniform highp vec4 _NowBlurSourceScaleOffset;
uniform highp vec2 _NowBlurDirection;

// The varying -- must match nowui-glassblur.vert's `out` block exactly. Note
// that _MainTex_ST has ALREADY been applied to it there (the blit contract's
// scale/offset); _NowBlurSourceScaleOffset below is the blur's own, separate
// transform and is applied here, exactly where HLSL :27 applies it.
in highp vec2 vUv;

// UIGlassBlur.shader:25 `fixed4 frag(v2f_img i) : SV_Target` becomes an
// explicit out.
out highp vec4 fragColor;

void main()
{
    // UIGlassBlur.shader:27-28.
    //
    // NAMING TRAP: the HLSL calls the offset `step`. `step` is a BUILT-IN
    // FUNCTION in GLSL (step(edge, x)). Declaring a local variable with that
    // name is legal -- GLSL ES 3.00 lets a local declaration hide a built-in --
    // but it is exactly the kind of shadowing that some drivers warn about and
    // that makes a later edit calling step() fail with a baffling message. It
    // is renamed to `stepUV` here. That is the only identifier in this file
    // that does not match the HLSL, and this comment is why.
    highp vec2 uv = vUv * _NowBlurSourceScaleOffset.xy + _NowBlurSourceScaleOffset.zw;
    highp vec2 stepUV = _NowBlurDirection * _NowBlurTexelSize.xy;

    // UIGlassBlur.shader:30-39. tex2D -> texture. Transcribed digit for digit;
    // see this file's header on why the constants are a matched set.
    highp vec4 col = texture(_NowBlurSourceTex, uv) * 0.1031526189;

    col += texture(_NowBlurSourceTex, uv + stepUV * 1.4765796511) * 0.1910108131;
    col += texture(_NowBlurSourceTex, uv - stepUV * 1.4765796511) * 0.1910108131;
    col += texture(_NowBlurSourceTex, uv + stepUV * 3.4455295350) * 0.1404289078;
    col += texture(_NowBlurSourceTex, uv - stepUV * 3.4455295350) * 0.1404289078;
    col += texture(_NowBlurSourceTex, uv + stepUV * 5.4148988458) * 0.0807154625;
    col += texture(_NowBlurSourceTex, uv - stepUV * 5.4148988458) * 0.0807154625;
    col += texture(_NowBlurSourceTex, uv + stepUV * 7.3849121445) * 0.0362685072;
    col += texture(_NowBlurSourceTex, uv - stepUV * 7.3849121445) * 0.0362685072;

    // UIGlassBlur.shader:40. No clip, no premultiply, no mask -- this pass
    // moves image data and nothing else.
    fragColor = col;
}
