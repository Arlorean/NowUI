// Gallery areas: the drawing primitives. Rectangles, shapes, straight lines and polylines, and - on its own, so its
// predicted failure cannot take the others with it - the bezier.
//
// Every example here is drawn at an explicit rect rather than through NowLayout, on purpose: an area whose job is to
// show what the RENDERER does should not also be exercising the layout engine, or a layout bug and a shader bug
// would arrive looking the same. Layout has its own area.
using System.Collections.Generic;
using NowUI;
using UnityEngine;

namespace NowUI.Web
{
    internal static partial class FeatureGallery
    {
        /// <summary>The interactive demo, unchanged: the whole control-library area is DemoScene.cs.</summary>
        private static void DrawControls(NowRect body)
        {
            DemoScene.Draw(body);
        }

        // --------------------------------------------------------------------------------------- rectangles

        /// <summary>
        /// UIRectangle's whole surface: fill, per-corner radius, outline, blur, padding, textures and sprites.
        /// </summary>
        /// <remarks>
        /// The order is the shader's own order of operations (M2-ShaderPort.md section 3.4): the shape SDF, then the
        /// AA band, then the outline ring, then the premultiplied composite. An example that renders wrong tells you
        /// which step, which a single busy panel would not.
        /// </remarks>
        private static void DrawRectangles(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color accent = theme.GetColor(NowColorToken.Accent);
            Color surface = theme.GetColor(NowColorToken.SurfaceMuted);

            NowRect left = new NowRect(body.x + 20f, body.y + 16f, (body.width - 60f) * 0.5f, body.height - 36f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, left.width, left.height);

            // ---- radius and outline
            NowRect inner = Panel(left, "Radius, outline, padding");

            float cell = 118f;
            float gap = 18f;
            float x = inner.x;
            float y = inner.y + 6f;

            Now.Rectangle(new NowRect(x, y, cell, 64f)).SetColor(accent).Draw();
            CaptionUnder(new NowRect(x, y, cell, 64f), "flat, radius 0");

            x += cell + gap;
            Now.Rectangle(new NowRect(x, y, cell, 64f)).SetColor(accent).SetRadius(16f).Draw();
            CaptionUnder(new NowRect(x, y, cell, 64f), "radius 16");

            x += cell + gap;
            // Per-corner, in the shader's own (TL, TR, BR, BL) argument order. Asymmetric on purpose: a symmetric
            // radius cannot show a corner-pair swap, which is exactly the bug the packing invites.
            Now.Rectangle(new NowRect(x, y, cell, 64f)).SetColor(accent).SetRadius(28f, 4f, 28f, 4f).Draw();
            CaptionUnder(new NowRect(x, y, cell, 64f), "radius 28/4/28/4");

            x = inner.x;
            y += 100f;

            Now.Rectangle(new NowRect(x, y, cell, 64f))
                .SetColor(surface)
                .SetRadius(10f)
                .SetOutline(2f, accent)
                .Draw();
            CaptionUnder(new NowRect(x, y, cell, 64f), "outline 2");

            x += cell + gap;
            // A hairline: thinner than the AA band, which the shader widens to `max(outline, delta)` so it cannot
            // wash out entirely (M2-ShaderPort.md section 3.4 step 7). If this one vanishes, that clamp is missing.
            Now.Rectangle(new NowRect(x, y, cell, 64f))
                .SetColor(new Color(0f, 0f, 0f, 0f))
                .SetRadius(10f)
                .SetOutline(0.5f, theme.GetColor(NowColorToken.BorderStrong))
                .Draw();
            CaptionUnder(new NowRect(x, y, cell, 64f), "hairline outline 0.5");

            x += cell + gap;
            Now.Rectangle(new NowRect(x, y, cell, 64f))
                .SetColor(accent)
                .SetRadius(10f)
                .SetPadding(new Vector4(16f, 8f, 16f, 8f))
                .Draw();
            CaptionUnder(new NowRect(x, y, cell, 64f), "padding 16/8");

            x = inner.x;
            y += 100f;

            // Blur widens the outer edge of the AA band, and the quad grows to make room (geometryPadding), which
            // pushes rawUV outside [0,1]. Three widths, because a blur that is present but not scaling with the
            // parameter looks correct in a single sample.
            for (int i = 0; i < 3; ++i)
            {
                float blur = 2f + i * 6f;
                NowRect at = new NowRect(x, y, cell, 64f);

                Now.Rectangle(at).SetColor(accent).SetRadius(12f).SetBlur(blur).Draw();
                CaptionUnder(at, "blur " + blur.ToString("0"));
                x += cell + gap;
            }

            x = inner.x;
            y += 100f;

            // Translucency over a drawn ground, which is what makes the premultiplied blend observable: the wrong
            // blend func produces a panel that is merely a slightly different wrong colour.
            NowRect groundRect = new NowRect(x, y, cell * 3f + gap * 2f, 64f);
            Now.Rectangle(groundRect).SetColor(new Color(0.95f, 0.72f, 0.2f, 1f)).SetRadius(8f).Draw();

            for (int i = 0; i < 3; ++i)
            {
                float alpha = 0.25f + i * 0.25f;

                Now.Rectangle(new NowRect(groundRect.x + 12f + i * (cell + gap), groundRect.y + 12f, cell - 24f, 40f))
                    .SetColor(new Color(0f, 0f, 0f, alpha))
                    .SetRadius(8f)
                    .Draw();
            }

            CaptionUnder(groundRect, "alpha 0.25 / 0.50 / 0.75 over an opaque ground");

            // ---- textures
            NowRect innerRight = Panel(right, "Textures and UVs");

            Texture2D checker = Checkerboard();

            NowRect tex = new NowRect(innerRight.x, innerRight.y + 6f, 150f, 150f);
            Now.Rectangle(tex).SetTexture(checker).Draw();
            CaptionUnder(tex, "SetTexture, default UV");

            NowRect tinted = new NowRect(tex.x + 172f, tex.y, 150f, 150f);
            Now.Rectangle(tinted).SetTexture(checker).SetColor(accent).SetRadius(20f).Draw();
            CaptionUnder(tinted, "texture x colour, radius 20");

            NowRect sub = new NowRect(tinted.x + 172f, tex.y, 150f, 150f);
            // A sub-rect of the atlas. The shape SDF stays in full-quad space (section 3.4 step 5), so the corner
            // radius must NOT follow the UV crop - if the corners move with the crop, that separation is broken.
            Now.Rectangle(sub).SetTexture(checker).SetUV(new Vector4(0.25f, 0.25f, 0.5f, 0.5f)).SetRadius(20f).Draw();
            CaptionUnder(sub, "SetUV quarter crop, radius 20");

            NowRect big = new NowRect(innerRight.x, tex.y + 196f, innerRight.width, 150f);
            Now.Rectangle(big).SetTexture(checker).SetRadius(14f).SetOutline(2f, accent).Draw();
            CaptionUnder(big, "texture + radius + outline, stretched wide");
        }

        /// <summary>
        /// A 64x64 two-tone checkerboard, built once.
        /// </summary>
        /// <remarks>
        /// Generated rather than fetched: a texture example should fail for texture reasons, not because an HTTP
        /// request for a PNG did not come back, and nothing in this project decodes image files anyway (the font
        /// atlas is baked from TTF bytes, not loaded as an image). It also makes UV errors legible - a half-pixel
        /// offset on a hard checker edge is visible where it would not be on a photograph.
        /// </remarks>
        private static Texture2D Checkerboard()
        {
            if (s_Checker != null)
                return s_Checker;

            const int size = 64;
            const int square = 8;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[size * size];

            for (int py = 0; py < size; ++py)
            {
                for (int px = 0; px < size; ++px)
                {
                    bool on = ((px / square) + (py / square)) % 2 == 0;
                    pixels[py * size + px] = on
                        ? new Color(0.92f, 0.94f, 0.98f, 1f)
                        : new Color(0.20f, 0.24f, 0.34f, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            s_Checker = texture;
            return texture;
        }

        private static Texture2D s_Checker;

        // --------------------------------------------------------------------------------------- shapes

        /// <summary>
        /// Circles, ellipses, triangles and polygons.
        /// </summary>
        /// <remarks>
        /// The prediction being tested is that these need no shader beyond the ported rectangle one - NowShape
        /// tessellates on the CPU and submits the triangles through <c>_defaultMaterial</c> with
        /// <c>NowMeshKind.Rectangle</c> (NowShape.cs:476). If that is right, everything here draws; if it is wrong,
        /// the page reports an unported program by name and the prediction was wrong in a way that says so.
        /// </remarks>
        private static void DrawShapes(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color accent = theme.GetColor(NowColorToken.Accent);
            Color success = theme.GetColor(NowColorToken.Success);
            Color warning = theme.GetColor(NowColorToken.Warning);

            NowRect inner = Panel(new NowRect(body.x + 20f, body.y + 16f, body.width - 40f, body.height - 36f),
                                  "Circles, ellipses, triangles, polygons");

            float y = inner.y + 10f;

            // ---- circles
            Now.Circle(new Vector2(inner.x + 52f, y + 52f), 44f).SetColor(accent).Draw();
            CaptionUnder(new NowRect(inner.x, y, 104f, 104f), "filled circle");

            Now.Circle(new Vector2(inner.x + 176f, y + 52f), 44f)
                .SetFill(false)
                .SetOutline(4f, accent)
                .Draw();
            CaptionUnder(new NowRect(inner.x + 124f, y, 104f, 104f), "outline only, 4");

            Now.Circle(new Vector2(inner.x + 300f, y + 52f), 44f)
                .SetColor(new Color(accent.r, accent.g, accent.b, 0.35f))
                .SetOutline(2f, Color.white)
                .Draw();
            CaptionUnder(new NowRect(inner.x + 248f, y, 104f, 104f), "fill + outline");

            // Segment count is the tessellation dial. Eight segments should read as an octagon: if it does not,
            // the parameter is being ignored somewhere between the builder and the mesh.
            Now.Circle(new Vector2(inner.x + 424f, y + 52f), 44f)
                .SetColor(success)
                .SetSegments(8)
                .Draw();
            CaptionUnder(new NowRect(inner.x + 372f, y, 104f, 104f), "8 segments");

            Now.Ellipse(new NowRect(inner.x + 500f, y + 16f, 150f, 72f)).SetColor(warning).Draw();
            CaptionUnder(new NowRect(inner.x + 500f, y, 150f, 104f), "ellipse from a rect");

            y += 140f;

            // ---- triangles
            Now.Triangle(
                    new Vector2(inner.x + 12f, y + 96f),
                    new Vector2(inner.x + 100f, y + 96f),
                    new Vector2(inner.x + 56f, y + 12f))
                .SetColor(accent)
                .Draw();
            CaptionUnder(new NowRect(inner.x, y, 112f, 96f), "filled triangle");

            Now.Triangle(
                    new Vector2(inner.x + 136f, y + 96f),
                    new Vector2(inner.x + 224f, y + 96f),
                    new Vector2(inner.x + 180f, y + 12f))
                .SetFill(false)
                .SetOutline(3f, success)
                .Draw();
            CaptionUnder(new NowRect(inner.x + 124f, y, 112f, 96f), "outlined triangle");

            // ---- polygons
            Now.Polygon(Star(new Vector2(inner.x + 306f, y + 54f), 46f, 20f, 5)).SetColor(warning).Draw();
            CaptionUnder(new NowRect(inner.x + 248f, y, 116f, 96f), "concave (star)");

            Now.Polygon(Star(new Vector2(inner.x + 430f, y + 54f), 46f, 46f, 6))
                .SetFill(false)
                .SetOutline(3f, accent)
                .Draw();
            CaptionUnder(new NowRect(inner.x + 372f, y, 116f, 96f), "convex, outlined");

            // A polygon large enough that its tessellation is visible, and asymmetric enough that a winding-order
            // mistake would show as a hole rather than as a slightly different shape.
            Now.Polygon(Star(new Vector2(inner.x + 574f, y + 54f), 48f, 26f, 9))
                .SetColor(new Color(0.86f, 0.32f, 0.6f, 1f))
                .Draw();
            CaptionUnder(new NowRect(inner.x + 500f, y, 150f, 96f), "9-point star");

            y += 132f;

            Caption(new NowRect(inner.x, y, inner.width, 16f),
                "All of the above submit CPU-tessellated triangles through the default (UI Rectangle) material - " +
                "no shape shader is involved. The Sdf extension's shape algebra is a different feature, drawn by " +
                "the 'NowUI/SDF Scene' program rather than by tessellation; ?area=sdf is where it lives.");
        }

        /// <summary>A star polygon, as a caller-owned point array - which is the storage NowPolygon documents.</summary>
        private static Vector2[] Star(Vector2 center, float outerRadius, float innerRadius, int points)
        {
            Vector2[] result = new Vector2[points * 2];

            for (int i = 0; i < result.Length; ++i)
            {
                float radius = (i % 2 == 0) ? outerRadius : innerRadius;
                float angle = -Mathf.PI * 0.5f + i * Mathf.PI / points;

                result[i] = new Vector2(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius);
            }

            return result;
        }

        // --------------------------------------------------------------------------------------- lines

        /// <summary>Straight lines and polylines: width, caps, dashes, arrows and gradient strokes.</summary>
        private static void DrawLines(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color accent = theme.GetColor(NowColorToken.Accent);
            Color success = theme.GetColor(NowColorToken.Success);

            NowRect inner = Panel(new NowRect(body.x + 20f, body.y + 16f, body.width - 40f, body.height - 36f),
                                  "Straight lines and polylines");

            float y = inner.y + 16f;
            float x0 = inner.x + 10f;
            float x1 = inner.x + 330f;

            // ---- widths
            for (int i = 0; i < 4; ++i)
            {
                float width = 1f + i * 3f;

                Now.Line(new Vector2(x0, y), new Vector2(x1, y)).SetWidth(width).SetColor(accent).Draw();
                Caption(new NowRect(x1 + 16f, y - 8f, 200f, 14f), "width " + width.ToString("0"));
                y += 30f;
            }

            y += 6f;

            // ---- caps. Butt and round differ only at the ends, so the ends are what the caption points at.
            Now.Line(new Vector2(x0, y), new Vector2(x1, y))
                .SetWidth(12f).SetCap(NowLineCap.Butt).SetColor(success).Draw();
            Caption(new NowRect(x1 + 16f, y - 8f, 200f, 14f), "cap Butt");
            y += 34f;

            Now.Line(new Vector2(x0, y), new Vector2(x1, y))
                .SetWidth(12f).SetCap(NowLineCap.Round).SetColor(success).Draw();
            Caption(new NowRect(x1 + 16f, y - 8f, 200f, 14f), "cap Round");
            y += 34f;

            Now.Line(new Vector2(x0, y), new Vector2(x1, y))
                .SetWidth(12f).SetCap(NowLineCap.Square).SetColor(success).Draw();
            Caption(new NowRect(x1 + 16f, y - 8f, 200f, 14f), "cap Square");
            y += 40f;

            // ---- dashes and arrows
            Now.Line(new Vector2(x0, y), new Vector2(x1, y))
                .SetWidth(4f).SetDash(14f, 8f).SetColor(accent).Draw();
            Caption(new NowRect(x1 + 16f, y - 8f, 240f, 14f), "dash 14 / gap 8");
            y += 32f;

            Now.Line(new Vector2(x0, y), new Vector2(x1, y))
                .SetWidth(4f).SetDash(14f, 8f, 7f).SetColor(accent).Draw();
            Caption(new NowRect(x1 + 16f, y - 8f, 240f, 14f), "same dash, offset 7 (phase)");
            y += 32f;

            Now.Line(new Vector2(x0, y), new Vector2(x1, y))
                .SetWidth(4f).SetArrow(NowLineArrow.End).SetColor(success).Draw();
            Caption(new NowRect(x1 + 16f, y - 8f, 240f, 14f), "arrow End");
            y += 32f;

            Now.Line(new Vector2(x0, y), new Vector2(x1, y))
                .SetWidth(4f).SetArrow(NowLineArrow.Both, 18f, 14f).SetColor(success).Draw();
            Caption(new NowRect(x1 + 16f, y - 8f, 240f, 14f), "arrow Both, 18 x 14");
            y += 32f;

            // A stroke whose colour varies along its length, which is a vertex-colour feature rather than a shader
            // one - it should survive the port for the same reason the shapes do.
            Now.Line(new Vector2(x0, y), new Vector2(x1, y))
                .SetWidth(10f)
                .SetGradient(accent, new Color(0.95f, 0.72f, 0.2f, 1f))
                .SetCap(NowLineCap.Round)
                .Draw();
            Caption(new NowRect(x1 + 16f, y - 8f, 240f, 14f), "gradient stroke (vertex colours)");

            // ---- polyline, on the right-hand half
            float px = inner.x + 620f;
            float py = inner.y + 30f;

            List<Vector2> zig = new List<Vector2>();

            for (int i = 0; i <= 12; ++i)
            {
                zig.Add(new Vector2(px + i * 34f, py + ((i % 2 == 0) ? 0f : 70f)));
            }

            Now.DrawPolyline(System.MemoryExtensions.AsSpan(zig.ToArray()), 6f, NowLineCap.Round, accent);
            Caption(new NowRect(px, py + 92f, 420f, 14f), "DrawPolyline, width 6, round caps");

            List<Vector2> curve = new List<Vector2>();

            for (int i = 0; i <= 48; ++i)
            {
                float t = i / 48f;
                curve.Add(new Vector2(px + t * 408f, py + 190f + Mathf.Sin(t * Mathf.PI * 2f) * 60f));
            }

            Now.DrawPolyline(System.MemoryExtensions.AsSpan(curve.ToArray()), 4f, NowLineCap.Round, success);
            Caption(new NowRect(px, py + 270f, 420f, 14f),
                "A sampled sine as a 49-point polyline - the CPU way to draw a curve without the Bezier program.");
        }

        // --------------------------------------------------------------------------------------- bezier

        /// <summary>
        /// Now.Bezier, alone.
        /// </summary>
        /// <remarks>
        /// Its own area because it is predicted to throw, and a throw stops the frame loop for the whole page. Put
        /// beside the straight lines it would have taken them down with it, and the capture would have said nothing
        /// about either. The panel and the caption are drawn BEFORE the curve so that, if the page dies on the
        /// curve, the last complete frame still says what was being attempted.
        /// </remarks>
        private static void DrawBezier(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect inner = Panel(new NowRect(body.x + 20f, body.y + 16f, body.width - 40f, body.height - 36f),
                                  "Now.Bezier - predicted to fail");

            Caption(new NowRect(inner.x, inner.y + 6f, inner.width, 16f),
                "Now.Bezier is the only NowUI call that reaches NowMeshKind.Bezier and the 'NowUI/UI Bezier' " +
                "material (NowLine.cs:620). That program is not ported, so WebGL2Backend.DrawMesh is expected to " +
                "throw by name and stop the frame loop. If this curve is on screen, the prediction was wrong.");

            Now.Bezier(
                    new Vector2(inner.x + 40f, inner.y + 250f),
                    new Vector2(inner.x + 200f, inner.y + 60f),
                    new Vector2(inner.x + 420f, inner.y + 330f),
                    new Vector2(inner.x + 600f, inner.y + 150f))
                .SetWidth(6f)
                .SetCap(NowLineCap.Round)
                .SetColor(theme.GetColor(NowColorToken.Accent))
                .Draw();

            Now.Bezier(
                    new Vector2(inner.x + 40f, inner.y + 320f),
                    new Vector2(inner.x + 220f, inner.y + 140f),
                    new Vector2(inner.x + 440f, inner.y + 400f),
                    new Vector2(inner.x + 600f, inner.y + 220f))
                .SetWidth(4f)
                .SetDash(14f, 9f)
                .SetArrow(NowLineArrow.End)
                .SetColor(theme.GetColor(NowColorToken.Success))
                .Draw();
        }
    }
}
