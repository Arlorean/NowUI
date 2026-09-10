#version 300 es
// ===========================================================================
// nowui-sdf-image-flood.frag  --  PASS 1, "Flood"
//
// GLSL ES 3.00 port of FloodFragment,
// Assets/NowUI/Extensions/Sdf/NowSdfImageField.shader:155-186.
//
// One jump-flood step. Keeps whichever of nine candidates -- this texel's own
// current segment and the eight neighbours at +/- _Step -- is nearest to this
// texel's center. Run log2(size) + 1 times with _Step halving to 1 and then
// once more at 1 (NowSdfImageField.cs:458-471), ping-ponging between two
// pooled targets.
//
// _MainTex is the PREVIOUS ping-pong target, which is also what Graphics.Blit
// binds as the blit source, so the two agree by construction and no separate
// binding is needed.
//
// THE NEIGHBOUR SAMPLE IS POINT-FILTERED BY CONTRACT, not by luck.
// NowSdfImageField.cs:448-449 sets FilterMode.Point on both pooled targets.
// A bilinear tap here would average two segments' endpoint coordinates and
// produce a segment that lies between them and matches neither -- a smooth
// wrong field rather than a visibly broken one. The backend's applySampler
// also clamps a non-filterable float target to point, so the two cannot
// disagree.
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-sdf-image.glsl"

in highp vec2 vUv;
out highp vec4 fragColor;

void main()
{
    highp vec2 texel = FieldTexelCenter(vUv);
    highp vec4 current = textureLod(_MainTex, vUv, 0.0);
    highp vec4 best = NOW_SDF_FIELD_NO_SEGMENT;
    highp float bestDistance = NOW_SDF_FIELD_FAR;
    Consider(texel, current, best, bestDistance);

    // [unroll] in the HLSL. Constant bounds are kept because they cost nothing
    // and keep this a line-for-line match; GLSL ES 3.00 would permit dynamic
    // ones.
    for (int y = -1; y <= 1; ++y)
    {
        for (int x = -1; x <= 1; ++x)
        {
            if (x == 0 && y == 0)
                continue;

            // HAZARD 2: HLSL writes float2(x, y) over int loop counters and
            // converts implicitly. GLSL refuses the implicit conversion, so
            // the casts are explicit. This is the one line in the program that
            // will not compile if transcribed verbatim.
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
