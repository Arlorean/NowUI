// Gallery area: SDF COMPOSITION AND THE REST OF THE ADVERTISED SURFACE, ?area=sdf-compose.
//
// WHY THIS EXISTS. ?area=sdf and ?area=sdf-image between them prove that the two SDF programs run: nine
// primitives, six operators over ONE pair of shapes, one morph, six effects, and a baked image field. That is
// most of the system, and it is not all of what README.md advertises. The verification pass that added this file
// went through the README's SDF paragraphs line by line and found five claims that no capture on either page
// touched:
//
//   1. "composable"                      - every algebra cell is exactly TWO operands. A two-operand tree does
//                                          not show that a THIRD operand keeps composing, and the shader's node
//                                          walk is where that would break.
//   2. "scoped rotation for primitives"  - RotateNext / PushRotation / PopRotation. The SetMaterial docstring
//                                          says "nonidentity node rotation require[s] MaterialAbiVersion", so
//                                          rotation is exactly the kind of thing a port can silently drop.
//   3. "and warp"                        - SetWarp is the only part of the scene program that reads _Time.y.
//                                          ?area=sdf's own note says the warp is "off by default", so nothing
//                                          on that page proves the uniform arrives or that the warp deforms.
//   4. "texture fills" / sprites         - NowSdf.Sprite is a separate entry point from NowSdf.Image, and
//                                          ?area=sdf-image only ever calls Image.
//   5. "the scene used as a mask"        - the README's x-ray and metamorphosis loops both clip ordinary NowUI
//                                          content with BeginMask(). That path rasterises the scene into a
//                                          render target and installs it as a texture mask, so it exercises the
//                                          scene program, the render-target path and the mask include TOGETHER,
//                                          and none of the three pages before this one ran it.
//
// Each cell below is one of those, plus a control wherever a cell could be right by accident.
//
// WHAT IS DELIBERATELY NOT HERE: SetMaterial(customMaterial), the "custom final-shading" the README's x-ray and
// custom-shader loops are built on. It is absent because it cannot work rather than because it was skipped, and
// the reason is structural: a custom final shader is a user-authored Unity Material whose Shader would have to
// be compiled, and this host has no shader compiler. WebGL2Backend.IsPortedShader names ten hand-ported GLSL
// programs and DrawMesh throws on anything else - which, per the note at the head of GalleryAreas.Extensions.cs,
// takes the whole frame down and not just its own cell. Putting it on this page would erase the evidence for
// everything else here. It is reported as a named limitation in M2-FeatureMatrix.md 7.2 instead.
using NowUI;
using NowUI.Sdf;
using UnityEngine;

namespace NowUI.Web
{
    internal static partial class FeatureGallery
    {
        /// <summary>
        /// The composition, rotation, warp, sprite and scene-as-mask cells.
        /// </summary>
        /// <remarks>
        /// Every cell here is drawn at the SAME scale and in the same cell furniture as <c>?area=sdf</c>, so a
        /// reader can put the two captures side by side. The clock is the gallery's pinned one, so the warp cell
        /// photographs the same deformation on every run.
        /// </remarks>
        private static void DrawSdfCompose(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            Color warm = new Color(0.98f, 0.55f, 0.22f, 1f);
            Color cool = new Color(0.30f, 0.68f, 0.98f, 1f);
            Color mint = new Color(0.36f, 0.86f, 0.62f, 1f);

            float x = body.x + 20f;
            float width = body.width - 40f;

            // ------------------------------------------------------------------ composition depth
            //
            // The claim under test is "composable", and the thing that would refute it is a tree that stops
            // agreeing with its own operators once it is more than two deep. So each cell adds one operand to
            // the one before it, left to right, and the first two are the shapes ?area=sdf's algebra panel
            // already draws. Reading the row left to right IS the test: if the four-operand cell is not the
            // three-operand cell plus one capsule, the walk lost a node.
            NowRect depth = Panel(new NowRect(x, body.y + 10f, width, 178f),
                                  "Composition depth - the same tree, one operand at a time");
            NowRect cell = SdfFirstCell(depth, 5);

            NowRect s = SdfCellFrame(cell, "1: circle");
            ComposeChain(NowSdf.Scene(s, new NowId(601)), s, warm, cool, mint, 1);

            s = SdfCellFrame(cell = SdfNextCell(cell), "2: + SmoothUnion box");
            ComposeChain(NowSdf.Scene(s, new NowId(602)), s, warm, cool, mint, 2);

            s = SdfCellFrame(cell = SdfNextCell(cell), "3: - Subtract circle");
            ComposeChain(NowSdf.Scene(s, new NowId(603)), s, warm, cool, mint, 3);

            s = SdfCellFrame(cell = SdfNextCell(cell), "4: + Union capsule");
            ComposeChain(NowSdf.Scene(s, new NowId(604)), s, warm, cool, mint, 4);

            // The effects read the COMPOSED field, not the last primitive in it. ?area=sdf only ever puts an
            // effect on a single shape, so this is the cell that says an outline traces a boolean result -
            // including the concave fillet a SmoothUnion makes and the notch the Subtract cuts.
            s = SdfCellFrame(cell = SdfNextCell(cell), "4 + outline and glow");
            ComposeChain(NowSdf.Scene(s, new NowId(605))
                             .SetOutline(3f, new Color(0.95f, 0.95f, 1f, 1f))
                             .SetGlow(14f, new Color(0.30f, 0.68f, 0.98f, 0.8f), 1.4f),
                         s, warm, cool, mint, 4);

            // ------------------------------------------------------------------ rotation, warp, sprite
            NowRect more = Panel(new NowRect(x, depth.yMax + 26f, width, 178f),
                                 "Scoped rotation, domain warp, and a sprite node");
            cell = SdfFirstCell(more, 5);

            // CONTROL for the two rotation cells. Same box, no rotation. A rotation that silently does nothing
            // renders as this, so this cell is what "did nothing" looks like.
            s = SdfCellFrame(cell, "Box (no rotation)");
            NowSdf.Scene(s, new NowId(611)).SetColor(cool)
                .RoundedBox(SdfInner(s, 26f, 34f), 6f).Draw();

            // RotateNext applies to the NEXT primitive only. Drawn over the unrotated box in a second colour so
            // the picture shows both the rotation AND its scope: if RotateNext leaked, the pale box would be
            // rotated too.
            s = SdfCellFrame(cell = SdfNextCell(cell), "RotateNext(30) - one node");
            NowSdf.Scene(s, new NowId(612))
                .SetColor(new Color(0.30f, 0.68f, 0.98f, 0.45f))
                .RoundedBox(SdfInner(s, 26f, 34f), 6f)
                .SetColor(warm)
                .Union()
                .RotateNext(30f)
                .RoundedBox(SdfInner(s, 26f, 34f), 6f)
                .Draw();

            // PushRotation is scoped: BOTH primitives inside it turn, and by the same angle, so the two stay
            // rigid relative to each other. A per-node rotation applied twice would not keep them rigid.
            s = SdfCellFrame(cell = SdfNextCell(cell), "PushRotation(25) - two nodes");
            NowSdf.Scene(s, new NowId(613)).SetColor(mint)
                .PushRotation(25f)
                .RoundedBox(new NowRect(s.width * 0.5f - 34f, s.height * 0.5f - 8f, 68f, 16f), 4f)
                .Union()
                .RoundedBox(new NowRect(s.width * 0.5f - 8f, s.height * 0.5f - 30f, 16f, 60f), 4f)
                .PopRotation()
                .Draw();

            // SetWarp is the one path in the scene program that reads _Time.y. speed is NON-ZERO on purpose:
            // a warp with speed 0 would prove the amplitude arrived but not that the clock did, and the clock
            // is the uniform a port is most likely to leave at zero. The gallery pins that clock, so this
            // photographs identically every run while still being a live read of it.
            //
            // SCALE IS A WAVELENGTH, NOT A FREQUENCY. `warpScenePos` computes `scenePos / scale`
            // (NowSdfShaderV2.cginc:1421), so a SMALL scale is a FAST noise. The first capture of this cell
            // asked for 0.06 and got a rounded box whose edge was a band of per-pixel dither - noise running at
            // roughly seventeen cycles per scene unit, aliasing against the pixel grid. That was read as a
            // possible port defect for as long as it took to open the include: the GLSL is a line-for-line
            // match of the HLSL, hash and all, and the picture was a correct rendering of a wrong request.
            // 22 units is a wavelength wide enough to see, which is what makes this cell evidence.
            s = SdfCellFrame(cell = SdfNextCell(cell), "SetWarp(6, 22, 0.6)");
            NowSdf.Scene(s, new NowId(614)).SetColor(warm)
                .SetWarp(6f, 22f, 0.6f)
                .RoundedBox(SdfInner(s, 22f, 30f), 14f).Draw();

            // A Sprite node over the same star art the image page bakes, through the sprite entry point rather
            // than the texture one. Same five passes underneath; a different call into them.
            s = SdfCellFrame(cell = SdfNextCell(cell), "Sprite node");
            Texture2D star = StarTexture();
            Sprite sprite = Sprite.Create(star, new Rect(0f, 0f, star.width, star.height),
                                          new Vector2(0.5f, 0.5f));
            NowSdf.Scene(s, new NowId(615)).SetColor(mint)
                .Sprite(SdfImageRect(s), sprite)
                .Draw();

            // ------------------------------------------------------------------ the scene as a mask
            //
            // BeginMask() rasterises the composited ALPHA of a scene into a coverage target and installs it as
            // an ambient texture mask, so ordinary NowUI content drawn inside the scope is clipped by the SDF.
            // This is the path both README metamorphosis loops use for their gloss and shading, and it is the
            // only cell on any of the three SDF pages that runs the scene program, the render-target path and
            // NowUIMask.cginc's texture-mask branch in one draw.
            NowRect masked = Panel(new NowRect(x, more.yMax + 26f, width, 190f),
                                   "The scene used as a mask - BeginMask() clipping ordinary NowUI content");
            cell = SdfFirstCell(masked, 3);

            // CONTROL 1: the content, unclipped. Whatever the masked cell shows, it is a subset of this.
            s = SdfCellFrame(cell, "Content, unmasked");
            MaskContent(s);

            // CONTROL 2: the mask shape, drawn normally. This is the silhouette the third cell should cut.
            s = SdfCellFrame(cell = SdfNextCell(cell), "Mask shape, drawn normally");
            MaskShape(NowSdf.Scene(s, new NowId(621)), s).SetColor(cool).Draw();

            // THE TEST. If the mask never installs, this cell equals the first one; if the coverage target
            // comes back empty, an empty scene "clips all content in its scope" (SDF.md) and this cell is
            // blank. Both failure modes are distinguishable from a pass, which is why both controls are here.
            s = SdfCellFrame(cell = SdfNextCell(cell), "Content INSIDE BeginMask()");
            using (MaskShape(NowSdf.Scene(s, new NowId(622)), s).SetFeather(1f).BeginMask())
            {
                MaskContent(s);
            }

            NowRect notes = new NowRect(x, masked.yMax + 22f, width, 62f);
            Now.Rectangle(notes)
                .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                .SetRadius(8f)
                .SetOutline(1f, theme.GetColor(NowColorToken.Border))
                .Draw();
            Now.Text(new NowRect(notes.x + 14f, notes.y + 8f, notes.width - 28f, 18f))
                .SetFontSize(12f).SetBold().SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("What this page still does not reach");
            Caption(new NowRect(notes.x + 14f, notes.y + 28f, notes.width - 28f, 30f),
                    "SetMaterial(custom) - the README's 'custom final-shading' x-ray and aurora loops. A custom " +
                    "final shader is a user-authored Material whose Shader this host cannot compile: " +
                    "WebGL2Backend.IsPortedShader names ten hand-ported GLSL programs and DrawMesh throws on " +
                    "anything else. It is absent from this page rather than failing on it because an unported " +
                    "program takes the whole frame down, not one cell.");

            areaState = "sdf-compose: composition 1-4 operands, RotateNext, PushRotation, SetWarp, Sprite, BeginMask.";
        }

        /// <summary>
        /// The same boolean tree, truncated after <paramref name="operands"/> nodes.
        /// </summary>
        /// <remarks>
        /// One function for all four cells so that the only difference between them is the operand count. If
        /// each cell built its own tree, a difference between two cells could be a difference in the tree.
        /// </remarks>
        private static void ComposeChain(NowSdfBuilder scene, NowRect at, Color first, Color second, Color third,
                                         int operands)
        {
            scene.SetColor(first)
                 .Circle(new Vector2(at.width * 0.38f, at.height * 0.42f), 26f);

            if (operands >= 2)
            {
                scene.SetColor(second)
                     .SmoothUnion(16f)
                     .RoundedBox(new NowRect(at.width * 0.40f, at.height * 0.44f,
                                             at.width * 0.44f, at.height * 0.36f), 10f);
            }

            if (operands >= 3)
            {
                // Subtracted, so this operand REMOVES area. An operator the walk ignored would add it instead,
                // which is the difference this cell is here to show.
                scene.Subtract()
                     .Circle(new Vector2(at.width * 0.62f, at.height * 0.40f), 16f);
            }

            if (operands >= 4)
            {
                scene.SetColor(third)
                     .Union()
                     .Capsule(new Vector2(at.width * 0.22f, at.height * 0.78f),
                              new Vector2(at.width * 0.80f, at.height * 0.78f), 7f);
            }

            scene.Draw();
        }

        /// <summary>The shape the mask cells clip with: a composed scene, not a primitive.</summary>
        /// <remarks>
        /// Composed on purpose. A rectangular or circular mask would be reproduced by accident by any clip that
        /// happened to be in the right place; a smooth-union of a circle and a bar with a hole subtracted out of
        /// it is not.
        /// </remarks>
        private static NowSdfBuilder MaskShape(NowSdfBuilder scene, NowRect at)
        {
            return scene
                .Circle(new Vector2(at.width * 0.36f, at.height * 0.46f), 30f)
                .SmoothUnion(14f)
                .RoundedBox(new NowRect(at.width * 0.34f, at.height * 0.34f,
                                        at.width * 0.52f, at.height * 0.34f), 14f)
                .Subtract()
                .Circle(new Vector2(at.width * 0.70f, at.height * 0.50f), 12f);
        }

        /// <summary>Ordinary NowUI content - a gradient and text - for the mask cells to clip.</summary>
        /// <remarks>
        /// A GRADIENT rather than a flat rectangle, because a flat fill clipped to a shape is indistinguishable
        /// from the shape simply being drawn. The gradient's ramp has to survive the clip for the cell to mean
        /// "ordinary content was masked" rather than "an SDF was drawn".
        /// </remarks>
        private static void MaskContent(NowRect at)
        {
            Now.Gradient(at, new Color(0.20f, 0.90f, 0.75f, 1f), new Color(0.85f, 0.25f, 0.75f, 1f))
                .SetLinear(35f)
                .Draw();

            // Placed rather than aligned: NowText has no alignment setter, and the word only has to sit over
            // the middle of the cell for the clip to be readable.
            Now.Text(new NowRect(at.x + at.width * 0.5f - 34f, at.y + at.height * 0.5f - 12f, at.width, 24f))
                .SetFontSize(15f).SetBold()
                .SetColor(new Color(0.06f, 0.06f, 0.10f, 1f))
                .Draw("MASKED");
        }
    }
}
