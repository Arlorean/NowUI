#version 300 es
// ===========================================================================
// nowui-sdf-image-dilate.frag  --  PASS 4, "Dilate"
//
// GLSL ES 3.00 port of DilateFragment,
// Assets/NowUI/Extensions/Sdf/NowSdfImageField.shader:204-235.
//
// Dilated sprite colour for the sprite rect (NO padding -- the blit target is
// sprite-sized, NowSdfImageField.cs:474 into field.color, an ARGB32 target of
// texelRect.width x texelRect.height). Texels inside the silhouette keep their
// own pixels. Texels outside take the colour just inside the nearest contour
// point at full alpha, so smooth fillets and morph bridges that reach past the
// pixels inherit the edge colour instead of sampling transparency.
//
// TWO SPACES IN ONE FUNCTION, which is the thing to hold on to while reading:
//   * spriteTexel is in SPRITE texels, from vUv over the sprite-sized target;
//   * fieldTexel  is spriteTexel + padding, in FIELD texels, because _MainTex
//     is the padded flood result and its coordinates are field-space.
// Mixing them produces an image offset by exactly the padding -- a plausible
// picture, shifted, which is why the two names are never abbreviated here.
//
// _MainTex is the FLOOD result (segments), not the resolved field. This pass
// runs from the same `ping` the resolve pass reads (NowSdfImageField.cs:473-474
// blit the same source into two different destinations), so it has the segment
// endpoints available and reconstructs the nearest point itself rather than
// reading a distance.
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-sdf-image.glsl"

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
        // No contour anywhere near: an interior texel keeps its own alpha, an
        // exterior one is forced opaque so a fillet reaching it is coloured
        // rather than transparent.
        fragColor = vec4(own.rgb, IsInside(own.a) ? own.a : 1.0);
        return;
    }

    highp vec2 a = segment.xy;
    highp vec2 ab = segment.zw - a;
    highp float lengthSquared = dot(ab, ab);
    highp float t = lengthSquared > 0.0 ? clamp(dot(fieldTexel - a, ab) / lengthSquared, 0.0, 1.0) : 0.0;
    // HAZARD 1 again: the HLSL calls this local `nearest`, not `distance`, so
    // there is nothing to rename here -- but `toward` below is a VECTOR from
    // the texel to the contour, not a length, and length(toward) is taken twice
    // for two different purposes.
    highp vec2 nearest = a + ab * t;
    highp vec2 toward = nearest - fieldTexel;

    if (IsInside(own.a))
    {
        // The field owns the edge: antialiased texels within a texel of the
        // contour become opaque so their alpha ramp cannot ghost through
        // fillets, while interior translucency is preserved.
        highp float edgeAlpha = mix(1.0, own.a, clamp(length(toward) - 1.0, 0.0, 1.0));
        fragColor = vec4(own.rgb, edgeAlpha);
        return;
    }

    highp float towardLength = max(length(toward), 0.0001);
    highp vec2 insidePoint = nearest + toward / towardLength * 0.75;
    // HAZARD 3: the HLSL is clamp(insidePoint - padding, 0.5, spriteSize - 0.5),
    // a scalar low bound against a vector high bound. GLSL ES 3.00 offers
    // clamp(genType,genType,genType) and clamp(genType,float,float) and nothing
    // mixed, so the scalar is widened. Getting this wrong is a compile error
    // rather than a wrong picture, which is the good outcome.
    highp vec2 insideSprite = clamp(insidePoint - _FieldParams.z, vec2(0.5), spriteSize - 0.5);
    highp vec4 edge = textureLod(_SourceTex, _SourceUv.xy + insideSprite / spriteSize * _SourceUv.zw, 0.0);
    fragColor = vec4(edge.rgb, 1.0);
}
