// ===========================================================================
// nowui-sdf-image.glsl
//
// GLSL ES 3.00 port of the CGINCLUDE block shared by all five passes of
// Assets/NowUI/Extensions/Sdf/NowSdfImageField.shader:30-255, shader
// "Hidden/NowUI/SDF Image Field".
//
// WHAT THE PROGRAM IS. A jump-flood bake that turns a sprite's alpha
// silhouette into a signed distance field, driven pass by pass from
// NowSdfImageFields.Bake (NowSdfImageField.cs:376-482) through Graphics.Blit:
//
//   pass 0 Seed     marching squares over each texel cell -> the cell's
//                   contour SEGMENT as (x0,y0,x1,y1) in FIELD TEXEL space,
//                   or (-1,-1,-1,-1) for "this cell has no contour".
//   pass 1 Flood    propagate the nearest segment from the eight neighbours
//                   at a halving jump step, ping-ponging between two pooled
//                   targets, then one extra unit-step pass.
//   pass 2 Resolve  measure the texel against its nearest segment and sign it
//                   by the source alpha -> the field itself, in the red
//                   channel.
//   pass 3 Stamp    copy a source rect into a texel rect of an atlas,
//                   discarding outside, so existing entries survive.
//   pass 4 Dilate   sprite-sized colour copy whose exterior texels inherit
//                   the nearest interior colour at full alpha.
//
// This file is the CGINCLUDE half: the uniforms and the seven helpers every
// pass shares. The five fragment entry points live in
// nowui-sdf-image-{seed,flood,resolve,stamp,dilate}.frag and the vertex stage
// in nowui-sdf-image.vert. tools/embed-shader.py splices this file into each
// of them as the constant GLSL_SDF_IMAGE_COMMON, so the text below exists
// ONCE in nowui-gl.js however many passes reference it.
//
// EVERY PASS IS REACHED THROUGH Blit, so the vertex stage follows the BLIT
// GEOMETRY CONTRACT in nowui-gl.js (attributes 0 and 1, the unit-quad ortho in
// nowui_MatrixMVP) rather than the nine-stream UI layout. Attributes 2..8 are
// not enabled for a blit and this program declares none of them.
//
// PRECISION IS NOT COSMETIC HERE. Unity's v2f_img.uv is half2 and its
// SourceAlpha / FieldTexelCenter arithmetic inherits that; on desktop D3D and
// GL half IS float, which is the profile these passes were authored and
// measured against. floor(uv * _FieldTexels.xy) at a 1024-texel field needs
// better than 1e-3 relative accuracy to land on the intended texel at all, so
// mediump would not merely blur the result, it would pick the wrong cell.
// Everything here is highp, per M2-ShaderPort.md section 2.0.
//
// THE FOUR HLSL-TO-GLSL HAZARDS IN THIS PROGRAM, each marked at its site:
//   1. A local named `distance` shadows GLSL's built-in distance(). Legal,
//      but renamed rather than relied on.
//   2. float2(x, y) over LOOP COUNTERS is an implicit int-to-float conversion
//      HLSL performs and GLSL refuses. Written vec2(float(x), float(y)).
//   3. clamp(v, 0.5, spriteSize - 0.5) mixes a scalar and a vector bound.
//      GLSL has clamp(genType,genType,genType) and clamp(genType,float,float)
//      and nothing in between, so the scalar is spelled vec2(0.5).
//   4. tex2Dlod(t, float4(uv, 0, 0)) becomes textureLod(t, uv, 0.0). The
//      explicit LOD is not decoration: these passes sample at computed
//      coordinates inside branches, where an implicit-LOD texture() would have
//      undefined derivatives.
// ===========================================================================

// _MainTex is the FLOOD INPUT (the previous ping-pong target), which is also
// what Graphics.Blit binds its `source` argument to. Seed and Stamp never
// sample it; their locations resolve to null and the backend pushes nothing.
uniform highp sampler2D _MainTex;

// The sprite being baked. A material property, NOT the blit source, so the
// backend routes it to its own texture unit -- see UNIT_SOURCE_TEX in
// nowui-gl.js. Its Properties default is "white" (NowSdfImageField.shader:16),
// which is the fallback the backend binds when the property is unset.
uniform highp sampler2D _SourceTex;

// (uvX, uvY, uvW, uvH): the sub-rect of _SourceTex being baked, normalised.
// Bake writes texelRect / sourceSize (NowSdfImageField.cs:428-437).
uniform highp vec4 _SourceUv;

// (spriteWidth, spriteHeight, padding, threshold), all in SOURCE TEXELS except
// the threshold (NowSdfImageField.cs:438-442).
uniform highp vec4 _FieldParams;

// (fieldWidth, fieldHeight, 1/fieldWidth, 1/fieldHeight) of the target being
// written (NowSdfImageField.cs:443). Stamp reuses it for the ATLAS size
// instead (:353-357) -- the same uniform, a different target, which is why
// nothing here may assume it describes a padded field.
uniform highp vec4 _FieldTexels;

// The jump-flood step, in field texels. Halves from NextPowerOfTwo(max(w,h))/2
// down to 1, then one more pass at 1 (NowSdfImageField.cs:458-471).
uniform highp float _Step;

// Stamp only: (x, y, width, height) of the destination texel rect inside the
// atlas (NowSdfImageField.cs:352).
uniform highp vec4 _StampRect;

// NowSdfImageField.shader:40-42. Spelled const rather than as a #define so the
// types are declared once; the values are identical.
const highp vec4 NOW_SDF_FIELD_NO_SEGMENT = vec4(-1.0, -1.0, -1.0, -1.0);
const highp float NOW_SDF_FIELD_FAR = 1000000.0;
const highp float NOW_SDF_FIELD_MAX_DISTANCE = 30000.0;

// Texel centers sit at integer + 0.5 in field texel space.
// NowSdfImageField.shader:45-48.
highp vec2 FieldTexelCenter(highp vec2 uv)
{
    return floor(uv * _FieldTexels.xy) + 0.5;
}

// NowSdfImageField.shader:65-68. The clamp keeps IsInside from degenerating
// into "always" or "never" for a threshold authored at 0 or 1.
highp float Threshold()
{
    return clamp(_FieldParams.w, 0.0001, 0.9999);
}

bool IsInside(highp float alpha)
{
    return alpha >= Threshold();
}

// NowSdfImageField.shader:50-63. Corners beyond the sprite rect read as fully
// transparent, which is what makes the silhouette closed at the sprite border
// instead of running off the edge of the padded field.
//
// The bounds are > spriteSize rather than >= spriteSize: a texel center at
// exactly spriteSize.x is the padding's first texel and its sample lands on
// the clamped last column of the source, which is deliberate.
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
    // HAZARD 4: explicit LOD. This call sits after a branch, so an
    // implicit-LOD texture() would have undefined derivatives here.
    return textureLod(_SourceTex, sourceUv, 0.0).a;
}

// Linear crossing of the threshold between two texel centers.
// NowSdfImageField.shader:76-80. alphaB - alphaA cannot be zero at any call
// site: every caller has already established that IsInside differs across the
// pair, so one sample is at or above Threshold() and the other is below.
highp vec2 Crossing(highp vec2 a, highp float alphaA, highp vec2 b, highp float alphaB)
{
    highp float t = clamp((Threshold() - alphaA) / (alphaB - alphaA), 0.0, 1.0);
    return mix(a, b, t);
}

// Distance from p to the segment segment.xy -> segment.zw.
// NowSdfImageField.shader:82-89.
highp float SegmentDistance(highp vec2 p, highp vec4 segment)
{
    highp vec2 a = segment.xy;
    highp vec2 ab = segment.zw - a;
    highp float lengthSquared = dot(ab, ab);
    highp float t = lengthSquared > 0.0 ? clamp(dot(p - a, ab) / lengthSquared, 0.0, 1.0) : 0.0;
    return length(p - (a + ab * t));
}

// NowSdfImageField.shader:91-99. Collects the first two crossings of a cell;
// count keeps rising past two so a saddle is still detectable, and the third
// and fourth crossings are dropped exactly as the HLSL drops them.
void Push(highp vec2 crossing, inout highp vec2 p0, inout highp vec2 p1, inout int count)
{
    if (count == 0)
        p0 = crossing;
    else if (count == 1)
        p1 = crossing;

    ++count;
}

// NowSdfImageField.shader:141-153. candidate.x < 0.0 is the
// NOW_SDF_FIELD_NO_SEGMENT test: seeds are texel centers and therefore never
// negative, so a negative x is unambiguously the sentinel.
//
// HAZARD 1: the HLSL names its local `distance`, which in GLSL is a built-in
// function. Shadowing one with a variable is legal GLSL ES 3.00 and most
// drivers accept it, but the built-in is not needed here and the rename costs
// nothing.
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
