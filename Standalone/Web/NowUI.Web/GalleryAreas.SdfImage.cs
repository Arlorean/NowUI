// Gallery area: the SDF IMAGE FIELD, ?area=sdf-image.
//
// Why this is a file of its own rather than three more cells on ?area=sdf. Two reasons, and the second is the
// one that matters.
//
//   1. ?area=sdf belongs to the unit that ported "NowUI/SDF Scene" and is being edited in parallel with this.
//      A separate file is a separate merge.
//
//   2. THE FAILURE MODES ARE DIFFERENT, so the pages have to be separately capturable. ?area=sdf fails if the
//      SCENE program is wrong: a shape comes out the wrong colour, or misplaced, or not at all. This page fails
//      if the BAKE is wrong, and a wrong bake does not throw -- it produces a smooth, plausible, incorrect
//      distance field, and the scene program then renders it faithfully. A silhouette that is subtly the wrong
//      shape, a glow that hugs a rectangle instead of the artwork, an outline that is there but a texel too fat:
//      those are what a broken jump flood looks like. So this page draws a source texture whose silhouette is
//      unmistakable (a five-pointed star with a hole punched through it), beside the field's own contour bands,
//      so that "the field is right" is something a still image can actually show.
//
// WHAT THIS PAGE PROVES, IF IT DRAWS AT ALL. An Image node reaches every one of these in order:
//
//   NowSdf.Image -> NowSdfGraph.PrepareImageFields -> NowSdfImageFields.Acquire -> Bake
//     -> RenderTexture.Create x3            (WebGL2Backend.CreateRenderTexture, float/half attachments)
//     -> Graphics.Blit pass 0, Seed         (Hidden/NowUI/SDF Image Field)
//     -> Graphics.Blit pass 1, Flood xN     (ping-pong between two pooled targets, N = log2(size) + 1)
//     -> Graphics.Blit pass 2, Resolve
//     -> Graphics.Blit pass 4, Dilate
//   NowSdfImageAtlas.Request -> ClearTarget (GL.Clear into a bound target)
//     -> Graphics.Blit pass 3, Stamp        (field atlas and colour atlas)
//   NowSdfCache.Upload -> DrawMesh          ("NowUI/SDF Scene", sampling _SdfImageField / _SdfImageColor)
//
// So a single drawn star is evidence for five shader passes, three render-target formats, a bound-target clear,
// a stack of ping-pong blits and the atlas plumbing at once. That is a lot of machinery behind one silhouette,
// which is why this page reports the backend's per-pass blit tally to the console rather than leaving the
// numbers to be inferred from the picture. MEASURED on SwiftShader, this page as it stands:
// seed/flood/resolve/stamp/dilate = 5/45/5/18/5 -- FIVE distinct fields, nine flood steps each, and eighteen
// atlas stamps because the atlas repacks as fields arrive.
//
// Five rather than one because NowSdfImageFields keys its cache on (texture, texel rect, padding, threshold),
// and three of those vary across this page: the threshold cell asks for 0.85, and the glow, shadow and contour
// cells each declare a different effect reach, which PaddingForReach quantizes to a different padding. Ten
// image nodes, five bakes: the cells that share a key share a field, which is itself something the tally shows
// and the picture cannot.
//
// THE ONE THING THAT COULD REFUSE, and where it would say so. The seed and flood targets need a FLOAT or
// HALF-FLOAT colour attachment, which WebGL2 grants only through EXT_color_buffer_float /
// EXT_color_buffer_half_float. The backend probes for both and reports them through NowRenderCaps, so
// SystemInfo.SupportsRenderTextureFormat answers honestly and NowSdfImageFields picks a format the device can
// allocate; if neither extension is present, RenderTexture.Create() returns false, Bake returns false, and the
// image node draws nothing rather than drawing a wrong silhouette. The strip at the foot prints what the caps
// actually said, so a capture records the answer instead of the page assuming one.
using NowUI;
using NowUI.Sdf;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Web
{
    internal static partial class FeatureGallery
    {
        // ------------------------------------------------------------------------------------- the source art
        //
        // Built in code rather than loaded, for the same reason the markdown area builds its swatch in code: a
        // fetch is a second thing that can fail, and this page is measuring the bake.
        //
        // THE SHAPE IS CHOSEN TO BREAK A BAD FLOOD, not to look nice. A five-pointed star with a circular hole
        // gives the field every case that separates a correct jump flood from a plausible one:
        //
        //   * five sharp CONVEX points, where a flood that loses a seed rounds the tip off;
        //   * five sharp CONCAVE notches, where the nearest contour is behind the fragment and a sign error
        //     turns the notch into a bridge;
        //   * a HOLE, whose interior is outside the silhouette while being surrounded by inside -- the one
        //     configuration that a flood-free "distance to the bounding box" fallback cannot fake;
        //   * an ANTIALIASED edge, so the alpha threshold is doing real work rather than picking between 0 and 1.
        //
        // A circle or a rounded box would have looked correct under any of at least three wrong implementations.
        private static Texture2D s_StarTexture;

        /// <summary>Texel size of the generated source art. Square, and a power of two out of habit only.</summary>
        private const int k_StarSize = 128;

        /// <summary>
        /// The source texture: a five-pointed star with a punched hole, antialiased, on transparent black.
        /// </summary>
        /// <remarks>
        /// RGB is a warm vertical ramp rather than a flat fill, because the DILATE pass (pass 4) claims to give
        /// exterior texels the colour of the nearest interior one. Against a flat fill that claim is untestable:
        /// every dilated texel would match every other. Against a ramp, a fillet reaching past the star's tip is
        /// visibly the colour of the tip.
        /// <para>Transparent texels are written with RGB = 0 as well as A = 0. That is deliberate: if the dilate
        /// pass were skipped, or the field were empty and every texel read as outside, the exterior would come
        /// out BLACK rather than warm, which is a difference a still image shows.</para>
        /// </remarks>
        private static Texture2D StarTexture()
        {
            if (s_StarTexture != null)
                return s_StarTexture;

            var texture = new Texture2D(k_StarSize, k_StarSize, TextureFormat.RGBA32, false)
            {
                name = "Now SDF Image Field source (star)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[k_StarSize * k_StarSize];
            const float outer = k_StarSize * 0.47f;
            const float hole = k_StarSize * 0.09f;
            const float centre = k_StarSize * 0.5f;

            for (int y = 0; y < k_StarSize; ++y)
            {
                for (int x = 0; x < k_StarSize; ++x)
                {
                    float dx = x + 0.5f - centre;
                    float dy = y + 0.5f - centre;

                    // The silhouette as a real signed distance, then a circle SUBTRACTED from it with
                    // max(d, -circle) -- the same boolean the SDF extension itself uses. The first draft folded
                    // the angle into a wedge and lerped between an inner and an outer RADIUS, which is a
                    // different shape: it gives round petals with sharp notches, not a star with straight edges
                    // and sharp tips. It rendered, and it looked fine, and it would have been a weaker test,
                    // because a flood that rounds a sharp tip off cannot be caught by art that has no sharp tips.
                    float distance = StarDistance(dx, dy, outer);
                    distance = Mathf.Max(distance, -(Mathf.Sqrt(dx * dx + dy * dy) - hole));

                    // One texel of analytic antialias, from the exact distance: coverage crosses 0.5 on the
                    // boundary. This is the ramp the bake's Crossing() interpolates across, so the source art
                    // being a real field rather than a jaggy mask is what makes the threshold meaningful.
                    float coverage = Mathf.Clamp01(0.5f - distance);

                    byte alpha = (byte)Mathf.RoundToInt(coverage * 255f);
                    float t = y / (float)(k_StarSize - 1);

                    pixels[y * k_StarSize + x] = alpha == 0
                        ? new Color32(0, 0, 0, 0)
                        : new Color32((byte)Mathf.RoundToInt(Mathf.Lerp(255f, 240f, t)),
                                      (byte)Mathf.RoundToInt(Mathf.Lerp(196f, 88f, t)),
                                      (byte)Mathf.RoundToInt(Mathf.Lerp(64f, 32f, t)),
                                      alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            s_StarTexture = texture;
            return texture;
        }

        /// <summary>
        /// Signed distance to a regular five-pointed star of circumradius <paramref name="outer"/>, centred at
        /// the origin and pointing up. Negative inside.
        /// </summary>
        /// <remarks>
        /// The standard reflect-fold construction: mirror about x, then reflect across the two wedge planes at
        /// +/-36 degrees so every one of the ten edges maps onto ONE edge, then measure against that edge's
        /// segment. <c>k1</c> is (cos 36, -sin 36); the ratio 0.42 between the inner and outer radii is what
        /// gives a star that reads as a star rather than as a pentagon or a set of spikes.
        /// <para>This is CPU-side art generation, not part of the port. It is written out rather than
        /// approximated because the point of the shape is its exactness: the tips and notches are what the
        /// baked field is being judged against, so they have to be right in the source before anything can be
        /// concluded about the bake.</para>
        /// </remarks>
        private static float StarDistance(float px, float py, float outer)
        {
            const float k1x = 0.809016994375f;      //  cos 36 degrees
            const float k1y = -0.587785252292f;     // -sin 36 degrees
            const float ratio = 0.42f;              // inner radius / outer radius

            float x = Mathf.Abs(px);
            float y = py;

            // Reflect across the plane whose normal is k1, then across its mirror k2 = (-k1.x, k1.y).
            float d1 = Mathf.Max(k1x * x + k1y * y, 0f);
            x -= 2f * d1 * k1x;
            y -= 2f * d1 * k1y;

            float d2 = Mathf.Max(-k1x * x + k1y * y, 0f);
            x -= 2f * d2 * -k1x;
            y -= 2f * d2 * k1y;

            x = Mathf.Abs(x);
            y -= outer;

            // The single edge every point has now been folded onto, from the top tip towards the next notch.
            float bax = ratio * -k1y - 0f;
            float bay = ratio * k1x - 1f;
            float h = Mathf.Clamp(((x * bax) + (y * bay)) / (bax * bax + bay * bay), 0f, outer);
            float ex = x - bax * h;
            float ey = y - bay * h;

            return Mathf.Sqrt(ex * ex + ey * ey) * Mathf.Sign(y * bax - x * bay);
        }

        // -------------------------------------------------------------------------------------------- the page

        /// <summary>
        /// The image distance field, drawn: the raw art, the baked silhouette, and the effects that only exist
        /// because the field does.
        /// </summary>
        /// <remarks>
        /// <para>Every cell but the first goes through <c>Hidden/NowUI/SDF Image Field</c>. The first draws the
        /// source texture through <c>Now.Rectangle</c> instead, deliberately: it is the control. A field cell
        /// that matched the control exactly would prove only that something copied the texture, so the control
        /// is there to be compared against, not to be reproduced.</para>
        /// <para><b>All the image nodes share ONE baked field.</b> <c>NowSdfImageFields.Acquire</c> keys its
        /// cache on (texture, texel rect, padding, threshold), and padding is quantized to 8 texels and derived
        /// from the scene's effect reach -- so a cell asking for a 22px glow bakes a DIFFERENT field from one
        /// asking for none, and the diagnostic strip reports how many distinct fields this page ended up
        /// with.</para>
        /// </remarks>
        private static void DrawSdfImage(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Texture2D star = StarTexture();

            Color warm = new Color(0.98f, 0.55f, 0.22f, 1f);
            Color cool = new Color(0.30f, 0.68f, 0.98f, 1f);
            Color mint = new Color(0.36f, 0.86f, 0.62f, 1f);

            float x = body.x + 20f;
            float width = body.width - 40f;

            // ------------------------------------------------------------------ the bake itself
            NowRect bake = Panel(new NowRect(x, body.y + 10f, width, 186f),
                                 "The bake - source art, its silhouette, and the field underneath it");
            NowRect cell = SdfFirstCell(bake, 5);

            // CONTROL. No SDF at all: the source texture drawn straight through NowUI/UI Rectangle. Whatever the
            // field cells show, this is what they were made from.
            NowRect s = SdfCellFrame(cell, "Source (no SDF)");
            Now.Rectangle(new NowRect(s.x + (s.width - 96f) * 0.5f, s.y + (s.height - 96f) * 0.5f, 96f, 96f))
                .SetTexture(star)
                .Draw();

            // The plain image node: alpha thresholded at 0.5, drawn as a solid tint. This is the cell that says
            // the seed, flood and resolve passes produced a field whose ZERO CONTOUR is the artwork's edge.
            // Flat colour rather than the art's own pixels, on purpose: a tinted silhouette can only come from
            // the field, where a textured one could have come from the texture.
            s = SdfCellFrame(cell = SdfNextCell(cell), "Image node (silhouette)");
            NowSdf.Scene(s, new NowId(501)).SetColor(warm)
                .Image(SdfImageRect(s), star)
                .Draw();

            // UseTexture() puts the DILATED colour back on the silhouette - pass 4's output, sampled through
            // _SdfImageColor. Compare with the control: same art, but the shape is now the field's.
            s = SdfCellFrame(cell = SdfNextCell(cell), "Image + UseTexture");
            NowSdf.Scene(s, new NowId(502))
                .Image(SdfImageRect(s), star)
                .UseTexture()
                .Draw();

            // THE CELL THAT PROVES IT IS A FIELD. Contour bands are level sets of the same distance every other
            // cell thresholds at zero, so concentric rings that follow the star's points and dive into its
            // notches are a picture OF the field rather than of the shape. A flood that failed would show rings
            // that are circles, or a bounding box, or nothing.
            //
            // THE BAND COUNT IS 3, NOT THE DEFAULT 0, and the reason is measured rather than stylistic. A band
            // count of 0 means REPEATING contours, and NowSdf deliberately excludes those from the effect budget
            // (NowSdf.cs:4101-4113: repeating contours cover the whole scene and so have no atlas-independent
            // bound). No budget means the field is baked at the minimum 8 texels of padding, which at this
            // cell's 0.69 scene units per texel is about 4.8 units of exact exterior field -- less than one
            // 9-unit contour spacing, so the first capture of this cell showed the zero contour and NOTHING
            // outside it. A finite count of 3 declares a reach of 23.5 units, PaddingForReach quantizes that up
            // to 40 texels, and the exterior rings appear. The cell was not wrong before; it was showing all the
            // exterior field that had been baked, which is a subtler thing to read than it looks.
            s = SdfCellFrame(cell = SdfNextCell(cell), "Contours (the field)");
            NowSdf.Scene(s, new NowId(503)).SetColor(new Color(0.16f, 0.20f, 0.28f, 1f))
                .SetContours(9f, 2f, new Color(0.45f, 0.85f, 1f, 0.9f), 0f, 3)
                .Image(SdfImageRect(s), star)
                .Draw();

            // The threshold is a real parameter of the bake, not a post-effect: raising it to 0.85 eats into the
            // antialiased rim, so the silhouette comes out slightly smaller AND the field is a different field
            // (the cache key includes the threshold). Two fields on one page is the point.
            s = SdfCellFrame(cell = SdfNextCell(cell), "threshold 0.85");
            NowSdf.Scene(s, new NowId(504)).SetColor(mint)
                .Image(SdfImageRect(s), star, 0.85f)
                .Draw();

            // ------------------------------------------------------------------ what the field buys
            NowRect effects = Panel(new NowRect(x, bake.yMax + 26f, width, 186f),
                                    "What an image field buys - effects that need distance, not pixels");
            cell = SdfFirstCell(effects, 5);

            // An outline that follows the artwork rather than its bounding box. Impossible from the texture
            // alone; trivial from the field.
            s = SdfCellFrame(cell, "SetOutline(3)");
            NowSdf.Scene(s, new NowId(511)).SetColor(new Color(0.20f, 0.24f, 0.32f, 1f))
                .SetOutline(3f, cool)
                .Image(SdfImageRect(s), star)
                .Draw();

            // The silhouette glow from NowUI's showcase. The glow reaches 18 scene units OUTSIDE the artwork,
            // which is exactly what the field's padding exists for: NowSdfImageFields.PaddingForReach quantizes
            // the reach up to a multiple of 8 texels and bakes that much exterior field.
            s = SdfCellFrame(cell = SdfNextCell(cell), "SetGlow(18)");
            NowSdf.Scene(s, new NowId(512))
                .SetGlow(18f, new Color(1f, 0.66f, 0.24f, 0.95f), 1.5f)
                .Image(SdfImageRect(s), star)
                .UseTexture()
                .Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "SetShadow(5,6)");
            NowSdf.Scene(s, new NowId(513)).SetColor(warm)
                .SetShadow(new Vector2(5f, 6f), 7f, new Color(0f, 0f, 0f, 0.8f))
                .Image(SdfImageRect(s), star)
                .Draw();

            // Emboss reads the field's GRADIENT, not its value, so it is the cell most sensitive to a flood that
            // is nearly right: a field built from texel centres rather than sub-texel contour segments has a
            // gradient that steps, and the emboss shows it as facets. NowSdfImageField measures against segments
            // for exactly this reason (its own header comment, :7-9).
            s = SdfCellFrame(cell = SdfNextCell(cell), "SetEmboss (gradient)");
            NowSdf.Scene(s, new NowId(514)).SetColor(new Color(0.62f, 0.62f, 0.68f, 1f))
                .SetEmboss(new Vector2(-0.6f, -0.8f), 0.55f, 8f)
                .Image(SdfImageRect(s), star)
                .Draw();

            // THE METAMORPHOSIS. A morph is a lerp of two FIELDS, so an image can genuinely melt into an
            // analytic shape - the star's points retract and its hole closes on the way to a circle. This is the
            // showcase effect the whole program exists for, and the one cell here that no amount of texture
            // sampling could imitate.
            //
            // The phase matches the ?area=sdf morph cell so both land at t = 0.5 on the pinned clock: at either
            // end a morph photographs as one of its two endpoints, which is indistinguishable from a morph that
            // does not work.
            s = SdfCellFrame(cell = SdfNextCell(cell), "Morph (image -> circle)");
            float morphT = 0.5f - 0.5f * Mathf.Cos(FeatureGallery.clock * 1.6f + 0.85f);
            NowSdfGraph fromArt = NowSdf.Graph().Image(SdfImageRect(s), star);
            NowSdfGraph toRound = NowSdf.Graph().Circle(SdfCentre(s), 30f, cool);
            NowSdf.Scene(s, new NowId(515)).SetColor(warm).Morph(fromArt, toRound, morphT).Draw();

            // ------------------------------------------------------------------ the numbers behind the picture
            NowRect notes = new NowRect(x, effects.yMax + 26f, width, 92f);

            Now.Rectangle(notes)
                .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                .SetRadius(8f)
                .SetOutline(1f, theme.GetColor(NowColorToken.Border))
                .Draw();

            Now.Text(new NowRect(notes.x + 14f, notes.y + 8f, notes.width - 28f, 18f))
                .SetFontSize(12f).SetBold().SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("What the device actually granted");

            Caption(new NowRect(notes.x + 14f, notes.y + 28f, notes.width - 28f, 60f), SdfImageCapsLine());

            areaState = SdfImageState();
            ReportSdfImageOnce();
        }

        /// <summary>Frames drawn of this area, so the report fires after the bake rather than before it.</summary>
        private static int s_SdfImageFrames;

        /// <summary>
        /// Prints the per-pass blit tally to the console, once, on the third frame.
        /// </summary>
        /// <remarks>
        /// <para>To the CONSOLE rather than only into <c>areaState</c>, because the console is what a headless
        /// capture keeps: the capture harness saves stderr beside the PNG, and <c>window.nowui.debugState</c> is
        /// not reachable on the <c>?capture=1</c> path. A number that only exists in a bridge the capture cannot
        /// call is not a measurement.</para>
        /// <para>On the THIRD frame, not the first. NowSdf prepares image fields during the draw that first
        /// needs them, so a report emitted at the end of frame one would race the bake it is reporting on and
        /// print zeros for a page that worked.</para>
        /// </remarks>
        private static void ReportSdfImageOnce()
        {
            if (++s_SdfImageFrames != 3)
                return;

            System.Console.WriteLine(
                "[NowUI] sdf-image: Hidden/NowUI/SDF Image Field blits by pass " +
                "(0 seed / 1 flood / 2 resolve / 3 stamp / 4 dilate) = " +
                WebGL2Backend.SdfImageFieldPassReport() + ". " + SdfImageCapsLine());
        }

        /// <summary>
        /// The scene-local rect an image node occupies, sized so the padded field has room in the cell.
        /// </summary>
        /// <remarks>
        /// Centred, and SQUARE, because the source art is square: an image node maps its texel rect onto this
        /// rect, so a non-square rect would stretch the silhouette and make a correct bake look like a wrong
        /// one. 88 units against a 128-texel source puts roughly 1.45 texels in a scene unit, which is what
        /// <c>SafeEffectReach</c> converts the 8-texel padding quantum into when it decides how far an exterior
        /// effect stays exact.
        /// </remarks>
        private static NowRect SdfImageRect(NowRect scene)
        {
            const float side = 88f;
            return new NowRect((scene.width - side) * 0.5f, (scene.height - side) * 0.5f, side, side);
        }

        /// <summary>
        /// What the caps and the bake actually reported, as one wrapped paragraph.
        /// </summary>
        /// <remarks>
        /// Read from <c>SystemInfo</c> live rather than asserted in prose, because the whole capability question
        /// this page exists to settle -- can WebGL2 render into a float colour attachment on this device --
        /// is answered by these three calls and by nothing a comment can say.
        /// </remarks>
        private static string SdfImageCapsLine()
        {
            bool rHalf = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf);
            bool rFloat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat);
            bool argbFloat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat);
            bool argbHalf = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);

            string field = rHalf ? "RHalf" : rFloat ? "RFloat" : argbHalf ? "ARGBHalf" : "NONE";
            string flood = argbFloat ? "ARGBFloat" : argbHalf ? "ARGBHalf" : "NONE";

            return
                "Field format " + field + ", flood format " + flood + " (" + SystemInfo.graphicsDeviceName + "). " +
                "NowSdfImageFields asks SystemInfo.SupportsRenderTextureFormat for RHalf, then RFloat, then " +
                "ARGBHalf for the field (NowSdfImageField.cs:283-292) and ARGBFloat then ARGBHalf for the flood " +
                "(:372-380); the backend answers from EXT_color_buffer_float / EXT_color_buffer_half_float " +
                "rather than guessing, and refuses to allocate an unrenderable format rather than substituting " +
                "RGBA8 - which could not hold the -1 no-segment sentinel or a texel coordinate past 1 and would " +
                "have produced a smooth, wrong field instead of a visible failure. NONE above would mean the " +
                "cells are blank because the bake could not run, not because the shader is wrong.";
        }

        /// <summary>
        /// The oracle line: how much baking a still image cannot show.
        /// </summary>
        /// <remarks>
        /// <c>NowSdfImageFields.bakeCount</c> and <c>.fieldCount</c> are the obvious things to report and are
        /// both <c>internal</c> to the package, which NowUI.Web is not a friend of -- and the package is frozen,
        /// so making them public is not this unit's to do. The backend's own per-pass blit tally answers the
        /// same question from the other end and is arguably the better oracle anyway: it counts what the GPU was
        /// actually asked to run, not what the cache believes it holds. A page whose cells all drew but whose
        /// seed count is zero would be drawing something that is not a baked field.
        /// </remarks>
        private static string SdfImageState()
        {
            return "sdf-image: passes(seed/flood/resolve/stamp/dilate)=" +
                   WebGL2Backend.SdfImageFieldPassReport() +
                   " source=" + k_StarSize + "x" + k_StarSize +
                   " imageNodes=10";
        }
    }
}
