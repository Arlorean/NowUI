// The three controls the `fields` area deliberately left out, plus the render-to-texture path the `effects` area
// left out: everything in this gallery whose exclusion rested on a claim that is no longer true.
//
// Why this file exists. `GalleryAreas.Interface.cs` ends the `fields` area with "Absent on purpose: ColorPicker
// needs 'NowUI/Color Picker' and AnimationCurveField needs 'NowUI/UI Bezier'. Neither is ported, and drawing either
// would stop the frame loop before anything above could be seen." `GalleryAreas.Paint.cs` says the same of
// SetRenderToTexture and NowEffects.Snapshot: "it needs a render target, and this backend's CreateRenderTexture
// throws." Neither statement is true any more - `WebGL2Backend.IsPortedShader` lists eight programs including
// 'NowUI/Color Picker' and 'NowUI/UI Bezier', and CreateRenderTexture, SetRenderTarget, Blit, DrawProcedural and
// CopyTexture are all implemented. Four features were therefore being reported as blocked by a blocker that had
// already been removed, which is exactly the kind of stale "not ported" the matrix exists to catch.
//
// Isolation still applies: if any of these four does throw, it takes down this area only.
//
// ColorPicker and GradientField are CLOSED FIELDS with deferred popups. The saturation/value square that needs
// 'NowUI/Color Picker' lives in the popup, so a still capture of this area does NOT exercise that program - the
// popup has to be opened with a pointer first. That is why the area is designed to be driven, and why the popup
// state is left to the pointer rather than forced open here.
using System.Text;
using NowUI;
using UnityEngine;

namespace NowUI.Web
{
    internal static partial class FeatureGallery
    {
        // Caller-owned state, as everywhere in this gallery: an immediate-mode UI keeps none of its own.
        private static Color s_PickColor = new Color(0.36f, 0.42f, 0.95f, 0.85f);
        private static Gradient s_PickGradient;
        private static AnimationCurve s_PickCurve;

        private static Gradient PickGradient()
        {
            if (s_PickGradient != null)
                return s_PickGradient;

            s_PickGradient = new Gradient();
            s_PickGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.20f, 0.45f, 0.95f), 0f),
                    new GradientColorKey(new Color(0.15f, 0.80f, 0.55f), 0.4f),
                    new GradientColorKey(new Color(0.98f, 0.75f, 0.15f), 0.7f),
                    new GradientColorKey(new Color(0.95f, 0.25f, 0.35f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.15f, 0f),
                    new GradientAlphaKey(1f, 0.5f),
                    new GradientAlphaKey(0.6f, 1f),
                });

            return s_PickGradient;
        }

        private static AnimationCurve PickCurve()
        {
            if (s_PickCurve == null)
            {
                s_PickCurve = new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.3f, 0.85f),
                    new Keyframe(0.65f, 0.25f),
                    new Keyframe(1f, 1f));
            }

            return s_PickCurve;
        }

        /// <summary>ColorPicker, GradientField and CurveField as closed fields.</summary>
        private static void DrawPickers(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color muted = theme.GetColor(NowColorToken.TextMuted);

            NowRect left = new NowRect(body.x + 20f, body.y + 16f, (body.width - 60f) * 0.48f, body.height - 36f);

            NowRect innerLeft = Panel(left, "Color picker, gradient field, curve field");

            // ------------------------------------------------------------------ the three excluded controls
            float y = innerLeft.y + 2f;

            Caption(new NowRect(innerLeft.x, y, innerLeft.width, 40f),
                "All three are CLOSED fields here. Clicking one opens its popup, and only the popup reaches " +
                "'NowUI/Color Picker' - so a still capture of this panel proves the fields draw, not that the " +
                "picker program runs. Drive it to prove that.");
            y += 44f;

            Now.Text(new NowRect(innerLeft.x, y, innerLeft.width, 16f))
                .SetFontSize(12f).SetColor(muted).Draw("ColorPicker - closed field");
            y += 18f;

            NowRect pickerRect = new NowRect(innerLeft.x, y, innerLeft.width, 30f);
            s_PickerFieldRect = pickerRect;
            Now.ColorPicker(pickerRect).Draw(ref s_PickColor);
            y += 34f;

            Caption(new NowRect(innerLeft.x, y, innerLeft.width, 16f), "value " + FormatColor(s_PickColor));
            y += 22f;

            Now.Text(new NowRect(innerLeft.x, y, innerLeft.width, 16f))
                .SetFontSize(12f).SetColor(muted).Draw("GradientField - closed field, texture-backed preview");
            y += 18f;

            NowRect gradientRect = new NowRect(innerLeft.x, y, innerLeft.width, 30f);
            s_GradientFieldRect = gradientRect;
            Gradient gradient = PickGradient();
            Now.GradientField(gradientRect).Draw(ref gradient);
            y += 38f;

            Now.Text(new NowRect(innerLeft.x, y, innerLeft.width, 16f))
                .SetFontSize(12f).SetColor(muted).Draw("CurveField - draws its curve with Now.Bezier");
            y += 18f;

            NowRect curveRect = new NowRect(innerLeft.x, y, innerLeft.width, 84f);
            s_CurveFieldRect = curveRect;
            AnimationCurve animationCurve = PickCurve();
            Now.CurveField(curveRect).Draw(ref animationCurve);
            y += 90f;

            Caption(new NowRect(innerLeft.x, y, innerLeft.width, 40f),
                "Four keys at 0.00, 0.30, 0.65, 1.00. If the curve line is missing while the box and its grid are " +
                "drawn, 'NowUI/UI Bezier' is the part that failed.");

        }

        /// <summary>
        /// The render-target path, on its own, because it can take the whole frame down without raising anything.
        /// </summary>
        /// <remarks>
        /// Separated from <see cref="DrawPickers"/> after a measurement: with both halves in one area the canvas
        /// came back EMPTY - clear colour only, no panels, no text - and with NO console error at all. That is the
        /// signature of a render target that was bound and never unbound: every draw after it lands in the
        /// offscreen texture instead of the back buffer, so nothing raises and nothing appears. Keeping it alone
        /// means the finding is attributable to this path rather than to whatever else shared its area.
        /// </remarks>
        private static void DrawRenderTexture(NowRect body)
        {
            NowRect right = new NowRect(body.x + 20f, body.y + 16f, body.width - 40f, body.height - 36f);
            NowRect innerRight = Panel(right, "Render to texture, and a snapshot");

            float ry = innerRight.y + 2f;

            Caption(new NowRect(innerRight.x, ry, innerRight.width, 54f),
                "SetRenderToTexture(true) makes NowEffects capture the block into a RenderTexture and draw the " +
                "texture back, rather than deforming vertices in place. It needs CreateRenderTexture, " +
                "SetRenderTarget, a clear and a textured draw - four backend calls the matrix recorded as throwing.");
            ry += 58f;

            NowRect deformed = new NowRect(innerRight.x, ry, innerRight.width, 118f);

            using (NowEffects.Modifier(NowDeformers.Wave(clock, 6f, 40f))
                       .SetRenderToTexture()
                       .SetSubdivision(6)
                       .Begin())
            {
                Card(deformed);
            }

            CaptionUnder(deformed, "Wave, subdivision 6, SetRenderToTexture(true)");
            ry += deformed.height + 30f;

            NowRect plain = new NowRect(innerRight.x, ry, innerRight.width, 118f);

            using (NowEffects.Modifier(NowDeformers.Wave(clock, 6f, 40f))
                       .SetSubdivision(6)
                       .Begin())
            {
                Card(plain);
            }

            CaptionUnder(plain, "The same wave in place, as the control. The two should look the same.");
            ry += plain.height + 30f;

            // A snapshot: draw into an offscreen target, then draw that target back as an ordinary texture.
            NowRect source = new NowRect(innerRight.x, ry, innerRight.width * 0.47f, 96f);
            NowRect echo = new NowRect(source.x + source.width + 14f, ry, innerRight.width * 0.47f, 96f);

            Texture captured = null;

            using (var snapshot = NowEffects.Snapshot(source).Begin())
            {
                Card(source);
                captured = snapshot.Texture;
            }

            if (captured != null)
            {
                Now.Rectangle(echo).SetTexture(captured).SetRadius(10f).Draw();
            }
            else
            {
                Now.Rectangle(echo).SetColor(new Color(0.55f, 0.12f, 0.14f, 1f)).SetRadius(10f).Draw();
                Now.Text(echo.Inset(10f)).SetFontSize(12f).SetColor(Color.white).Draw("snapshot.Texture was null");
            }

            ry += 100f;

            StringBuilder note = new StringBuilder(220);
            note.Append("Left: NowEffects.Snapshot drew the card into a RenderTexture. Right: that texture drawn " +
                        "back with SetTexture. Target: ");
            note.Append(captured == null
                ? "null"
                : captured.width.ToString() + "x" + captured.height.ToString());
            Caption(new NowRect(innerRight.x, ry, innerRight.width, 40f), note.ToString());
        }

        /// <summary>Where each closed field landed this frame, so a driver can click it without hard-coding a rect.</summary>
        private static NowRect s_PickerFieldRect;
        private static NowRect s_GradientFieldRect;
        private static NowRect s_CurveFieldRect;

        internal static string PickerRects()
        {
            StringBuilder builder = new StringBuilder(160);
            AppendRect(builder, "colorPicker", s_PickerFieldRect);
            AppendRect(builder, "gradientField", s_GradientFieldRect);
            AppendRect(builder, "curveField", s_CurveFieldRect);
            return builder.ToString();
        }

        private static void AppendRect(StringBuilder builder, string name, NowRect rect)
        {
            if (builder.Length > 0)
                builder.Append(';');

            builder.Append(name).Append('=')
                .Append(rect.x.ToString("0.##")).Append(',')
                .Append(rect.y.ToString("0.##")).Append(',')
                .Append(rect.width.ToString("0.##")).Append(',')
                .Append(rect.height.ToString("0.##"));
        }

        /// <summary>A small card with rows: the subject of every deformer and snapshot in this area.</summary>
        private static void Card(NowRect at)
        {
            Now.Rectangle(at)
                .SetColor(new Color(0.36f, 0.42f, 0.92f, 1f))
                .SetRadius(10f)
                .Draw();

            for (int i = 0; i < 4; ++i)
            {
                Now.Rectangle(new NowRect(at.x + 14f, at.y + 18f + i * 21f, at.width - 28f, 10f))
                    .SetColor(new Color(1f, 1f, 1f, 0.55f))
                    .SetRadius(5f)
                    .Draw();
            }
        }

        private static string FormatColor(Color c)
        {
            return "rgba(" + c.r.ToString("0.00") + ", " + c.g.ToString("0.00") + ", " +
                   c.b.ToString("0.00") + ", " + c.a.ToString("0.00") + ")";
        }
    }
}
