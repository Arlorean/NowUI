#version 300 es
// ===========================================================================
// nowui-colorpicker.frag
//
// GLSL ES 3.00 port of HsvToRgb + Checker + `frag` from
// Assets/NowUI/Assets/Shaders/UIColorPicker.shader (shader "NowUI/Color
// Picker"). Every block cites the HLSL line it came from.
//
// COMPOSITION: see nowui-colorpicker.vert's header.
//
// WHAT THIS SHADER IS. Three unrelated pickers behind one `_Mode` uniform,
// one material per mode. NowValueControls.cs:935-954 creates them lazily with
// `new Material(Shader.Find("NowUI/Color Picker"))` and a single
// material.SetFloat(_Mode, (float)mode), then draws each as an ordinary
// Now.Rectangle with SetMaterial. So `_Mode` arrives through the MATERIAL bag,
// never through a MaterialPropertyBlock, and there is no ColorPickerMaterial
// asset in the exported fixtures -- the material is built at runtime from the
// shader's declared defaults. Modes:
//     0  SaturationValue   the square. color.r is the HUE.
//     1  Hue               the vertical rainbow strip.
//     2  Alpha             the horizontal transparency strip over a checker.
//
// Nothing here is stubbed and nothing is optional. All three modes, the
// checkerboard, the legacy clip and the mask coverage run as written.
//
// PRECISION -- read before "tidying". Unity's fixed4 is a legacy precision
// alias that would map to lowp. Do NOT translate it that way: the hue ramp is
// `abs(fract(h + k) * 6 - 3)`, which at lowp's ~8 bits bands visibly across a
// 360-degree sweep -- and banding in a colour PICKER is the one place a user
// will notice it. The fragment language has no default float precision, so
// declaring it is mandatory rather than decorative; `highp sampler2D` matters
// because the fragment default for a sampler is LOWP and the mask include
// samples two of them.
// ===========================================================================

precision highp float;
precision highp int;
precision highp sampler2D;

//#include "nowui-colorspace.glsl"
//#include "nowui-mask.glsl"

// ---------------------------------------------------------------------------
// Uniforms. UIColorPicker.shader:63 declares exactly one of its own.
//
//   :63  float _Mode   0 SaturationValue, 1 Hue, 2 Alpha. Set once per
//                      material at creation (NowValueControls.cs:953).
//                      **GL's zero default is a VALID mode**, which makes this
//                      the one uniform on this shader whose absence is
//                      invisible: an unbridged _Mode renders all three pickers
//                      as a saturation/value square -- three plausible
//                      gradients, one correct. The backend must resolve it
//                      from the material bag, and this file's port is
//                      worthless without that.
//
// _ZTest is in the Properties block (:10) but is RENDER STATE, not a uniform.
// It is 8 (CompareFunction.Always). Never bind it to a location.
//
// There is no sampler on this shader beyond the two the mask include brings.
// Texture units are still fixed: 1 _NowUITextureMask0, 2 _NowUITextureMask1
// (M2-ShaderPort.md section 7.4), both of which must be bound to the 1x1
// opaque black fallback when unused.
// ---------------------------------------------------------------------------
uniform highp float _Mode;

// Varyings -- must match nowui-colorpicker.vert's `out` block exactly.
in highp vec4 vRect;
in highp vec4 vColor;
in highp vec4 vMask;
in highp vec4 vRawUV;

// UIColorPicker.shader:92 `fixed4 frag(...) : SV_Target` becomes an explicit out.
out highp vec4 fragColor;

// ---------------------------------------------------------------------------
// HLSL fmod is NOT GLSL mod.
//
// HLSL's fmod truncates toward zero and keeps the sign of the DIVIDEND; GLSL's
// mod floors and keeps the sign of the DIVISOR. They agree only for
// non-negative operands. Checker()'s argument below is non-negative by
// construction -- rawUV is saturated into [0,1] and rect.zw is positive, so
// `pixel` is non-negative and so are both floors -- which means `mod` would
// give the same answer today. It is still written out, because reaching for
// `mod` in the next shader ported is a bug that produces a plausible picture
// (M2-ShaderPort.md section 4.5). Same helper, same name, as in
// nowui-gradient.frag.
// ---------------------------------------------------------------------------
highp float hlslFmod(highp float x, highp float y)
{
    return x - y * trunc(x / y);
}

// ---------------------------------------------------------------------------
// UIColorPicker.shader:65-70. The standard branchless HSV-to-RGB.
//
//   k = (1, 2/3, 1/3, 3)
//   p = abs(fract(vec3(h) + k.xyz) * 6 - k.www)     // three phase-shifted ramps
//   return v * mix(k.xxx, clamp(p - k.xxx, 0, 1), s)
//
// Two HLSL->GLSL substitutions, both mechanical: frac -> fract and
// saturate(x) -> clamp(x, 0.0, 1.0). The third is not mechanical and is the
// one to watch: HLSL's `k.xxx` is a SCALAR swizzle producing a float3 of
// k.x repeated. GLSL has no scalar swizzle, so it must be written vec3(k.x) --
// same value, and the only legal spelling. `float3(h, h, h)` becomes vec3(h).
// ---------------------------------------------------------------------------
highp vec3 HsvToRgb(highp float h, highp float s, highp float v)
{
    highp vec4 k = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    highp vec3 p = abs(fract(vec3(h) + k.xyz) * 6.0 - vec3(k.w));
    return v * mix(vec3(k.x), clamp(p - vec3(k.x), 0.0, 1.0), s);
}

// ---------------------------------------------------------------------------
// UIColorPicker.shader:72-77. The transparency checkerboard behind the alpha
// strip: 5-unit squares alternating between two near-white greys.
//
// `size` is rect.zw, so `pixel` is in the quad's own LOCAL units -- which are
// UI units, which are drawing-buffer pixels scaled by the device pixel ratio.
// That means the squares are 5 PHYSICAL pixels at dpr 1 and 5 physical pixels
// at dpr 2 as well, i.e. they halve in apparent size on a retina canvas. That
// is what Unity does too (the same rect.zw arrives there), so it is faithful;
// it is called out because it looks like a dpr bug and is not one.
//
// The two greys are authored display/sRGB values and are converted along with
// everything else at :120, not here.
// ---------------------------------------------------------------------------
highp vec3 Checker(highp vec2 rawUV, highp vec2 size)
{
    highp vec2 pixel = rawUV * size;
    highp float checker = hlslFmod(floor(pixel.x / 5.0) + floor(pixel.y / 5.0), 2.0);
    return mix(vec3(0.88, 0.90, 0.93), vec3(0.68, 0.72, 0.78), checker);
}

void main()
{
    // UIColorPicker.shader:95-99.
    //
    // NOTE the saturate on :97, which no other NowUI shader applies to rawUV.
    // It is deliberate here and must be kept: the picker's output is a
    // FUNCTION of rawUV rather than a shape SDF, so a value outside [0,1]
    // (which geometry padding can produce) would run the hue past the end of
    // the wheel and wrap. Clamping pins the last row of texels instead. Do not
    // copy this line into the other ports, and do not remove it from this one.
    highp vec4 rect = vRect;
    highp vec4 mask = vMask;
    highp vec2 rawUV = clamp(vRawUV.xy, 0.0, 1.0);
    highp vec2 pos = rect.xy + rawUV * rect.zw;
    highp vec2 uiPosition = vec2(pos.x, -pos.y);

    // UIColorPicker.shader:101 -- the legacy hard clip. Unconditional, no AA,
    // before any other work. May discard.
    NowUIClipLegacyRect(uiPosition, mask);

    // UIColorPicker.shader:103. HLSL's float-to-int cast truncates toward
    // zero, and so does GLSL's int() constructor, so `int(_Mode + 0.5)` is the
    // same rounding in both languages for the non-negative values this
    // uniform ever holds (0, 1 or 2 -- NowValueControls.cs:953 casts an enum).
    int mode = int(_Mode + 0.5);
    highp vec3 rgb;

    if (mode == 1)
    {
        // UIColorPicker.shader:106-109. The hue strip. `1.0 - rawUV.y` puts
        // red at the TOP: rawUV.y == 1 is the UI top edge
        // (M2-ShaderPort.md section 1.3), so hue 0 lands there.
        rgb = HsvToRgb(1.0 - rawUV.y, 1.0, 1.0);
    }
    else if (mode == 2)
    {
        // UIColorPicker.shader:110-115. The alpha strip: the previewed colour
        // composited over the checkerboard by hand, left (transparent) to
        // right (opaque).
        highp float alpha = rawUV.x;
        rgb = mix(Checker(rawUV, rect.zw), vColor.rgb, alpha);
    }
    else
    {
        // UIColorPicker.shader:116-118. The saturation/value square for the
        // hue carried in color.r. x is saturation, y is value, and y is
        // measured UP -- rawUV.y == 1 is the UI top, so the bright end is at
        // the top, which is the convention every colour picker uses.
        rgb = HsvToRgb(vColor.r, rawUV.x, rawUV.y);
    }

    // UIColorPicker.shader:120. THE colour-space conversion for this shader,
    // applied once to the final rgb rather than to the vertex colour -- see
    // nowui-colorpicker.vert's header for why that distinction is load-bearing.
    // Identity under Gamma; kept so the linear branch is one #define away.
    highp vec4 col = vec4(NowUIColorToWorkingSpace(rgb), 1.0);

    // UIColorPicker.shader:121. HLSL `col *= x` on a float4 by a scalar scales
    // rgb AND a alike. With alpha seeded to 1 that turns straight rgb into
    // correctly PREMULTIPLIED output, which is what
    // gl.blendFunc(ONE, ONE_MINUS_SRC_ALPHA) expects -- this shader uses the
    // shared premultiplied blend, unlike UIGlass.
    col *= NowUIMaskCoverage(uiPosition);

    // UIColorPicker.shader:122 -- clip(col.a - 0.001). HLSL clip discards on
    // strictly negative, so alpha exactly 0.001 survives. With no mask this
    // never fires (alpha is 1); with one it rejects the masked-out region.
    if (col.a - 0.001 < 0.0)
        discard;

    // UIColorPicker.shader:123
    fragColor = col;
}
