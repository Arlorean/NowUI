#version 300 es
// ===========================================================================
// nowui-sdf-image-stamp.frag  --  PASS 3, "Stamp"
//
// GLSL ES 3.00 port of StampFragment,
// Assets/NowUI/Extensions/Sdf/NowSdfImageField.shader:242-255.
//
// Copies the _SourceUv region of _SourceTex into the _StampRect texel rect of
// the target atlas. The blit covers the WHOLE atlas; fragments outside the
// rect discard so existing entries are preserved. Driven from
// NowSdfImageFields.Stamp (NowSdfImageField.cs:343-368), which
// NowSdfImageAtlas calls once per field for the field atlas and once for the
// colour atlas (NowSdfImageAtlas.cs:172, :179).
//
// _FieldTexels MEANS THE ATLAS HERE, not a padded field: Stamp writes
// (atlas.width, atlas.height, 1/w, 1/h) into it (NowSdfImageField.cs:353-357).
// The shared helper FieldTexelCenter is therefore not used by this pass -- it
// floors to a texel center, and this pass wants the texel INDEX -- which is
// why the floor below is written out rather than shared.
//
// THE DISCARD IS LOAD-BEARING AND MUST NOT BECOME A CLEAR. An atlas holds many
// baked fields; a pass that wrote a transparent or zero fragment outside the
// rect instead of discarding would erase every neighbouring entry on every
// stamp, and the visible symptom is that only the most recently baked image
// has a field. Blending is off for a blit (NowSdfImageField.shader:28 says
// Blend Off, and the backend disables it for every blit regardless), so the
// discard is the only thing preserving the destination.
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-sdf-image.glsl"

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
