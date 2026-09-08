#version 300 es
// ===========================================================================
// nowui-sdf-image-resolve.frag  --  PASS 2, "Resolve"
//
// GLSL ES 3.00 port of ResolveFragment,
// Assets/NowUI/Extensions/Sdf/NowSdfImageField.shader:188-197.
//
// Measures each texel against the segment the flood left for it and signs the
// result by the source alpha: NEGATIVE INSIDE the silhouette, positive
// outside, in SOURCE TEXELS. That sign convention is what NowSdfShaderV2's
// image node reads, and it is the opposite of "brighter means more".
//
// Written into field.texture (NowSdfImageField.cs:473), whose format is the
// first of RHalf, RFloat, ARGBHalf this device can render into
// (NowSdfImageField.cs:283-292). Only the red channel survives in the R
// formats; g and b are written 0 and a is written 1 so the ARGBHalf fallback
// stores something coherent rather than whatever the pool held.
//
// NOW_SDF_FIELD_MAX_DISTANCE is 30000, matching NowSdfImageFields.MaxDistance
// (NowSdfImageField.cs:104) exactly, and both the clamp and the
// no-segment sentinel use it. 30000 is representable in a 16-bit half (max
// 65504), which is why the RHalf preference is safe; its ulp up there is 32
// texels, which does not matter for a value that only ever means "far".
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-sdf-image.glsl"

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
