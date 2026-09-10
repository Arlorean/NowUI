#version 300 es
// ===========================================================================
// nowui-sdf-image-seed.frag  --  PASS 0, "Seed"
//
// GLSL ES 3.00 port of SeedFragment,
// Assets/NowUI/Extensions/Sdf/NowSdfImageField.shader:104-139.
//
// Marching squares on the cell whose corners are this texel center and its
// right, top and top-right neighbours. Writes the cell's contour SEGMENT as
// (x0, y0, x1, y1) in field texel space, or NOW_SDF_FIELD_NO_SEGMENT when all
// four corners agree and the cell holds no contour.
//
// Driven by NowSdfImageField.cs:456 -- Graphics.Blit(source, ping, material, 0)
// -- into a pooled ARGBFloat (or ARGBHalf) target at FilterMode.Point.
//
// WHY THE TARGET FORMAT MATTERS, and what to check before believing an empty
// field. The seed is a pair of texel-space COORDINATES, not a colour. An
// RGBA8 target would quantise them to 1/255 of the field and clamp everything
// past texel 1 to white, so the flood would then propagate a uniform garbage
// segment and the resolve would produce a smooth, plausible, entirely wrong
// field. The C# side asks for ARGBFloat and falls back to ARGBHalf
// (NowSdfImageField.cs:372-380) precisely for this, and the backend refuses to
// allocate either when EXT_color_buffer_float / EXT_color_buffer_half_float is
// missing rather than substituting RGBA8 (nowui-gl.js resolveTargetFormat).
// The sentinel is NEGATIVE, which an unsigned-normalised target cannot even
// represent -- so a backend that ever did substitute one would make every cell
// read as "has a segment".
//
// This pass samples _SourceTex only. It does not read _MainTex, even though
// Blit binds the sprite there as well.
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-sdf-image.glsl"

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

    // The four cell edges, in the HLSL's order: bottom, right, top, left.
    // Order is not arbitrary -- it decides which two of a saddle's four
    // crossings survive, and therefore which of the two short segments the
    // field keeps.
    if (b00 != b10)
        Push(Crossing(c00, a00, c10, a10), p0, p1, count);
    if (b10 != b11)
        Push(Crossing(c10, a10, c11, a11), p0, p1, count);
    if (b01 != b11)
        Push(Crossing(c01, a01, c11, a11), p0, p1, count);
    if (b00 != b01)
        Push(Crossing(c00, a00, c01, a01), p0, p1, count);

    // Two crossings form the cell's contour segment. A saddle has four; its
    // second short segment lies within one texel of the first and is dropped,
    // which only perturbs the field locally. (The HLSL comment,
    // NowSdfImageField.shader:135-137, and the reason `count` is computed at
    // all even though nothing below reads it.)
    fragColor = vec4(p0, p1);
}
