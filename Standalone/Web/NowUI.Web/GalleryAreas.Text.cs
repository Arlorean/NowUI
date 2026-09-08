// Gallery areas: text. The plain text area, and the gradient/animation area kept separate from it.
//
// They are separate because they test different things. Plain text tests the ported TxtRenderer program and the
// managed SDF16 font baker behind it, which slice 1 already measured against Unity. Gradient text tests a branch of
// that program which M2-ShaderPort.md section 4.5 explicitly permitted slice 1 to OMIT, on condition the omission be
// reported rather than silently flattened - and a silently flattened gradient renders as perfectly plausible flat
// text. Putting the two in one area would let the working half vouch for the half that may not be there.
using System.Collections.Generic;
using NowUI;
using UnityEngine;

namespace NowUI.Web
{
    internal static partial class FeatureGallery
    {
        // --------------------------------------------------------------------------------------- text

        /// <summary>Sizes, faces, outlines, whitespace handling, non-ASCII, measurement and wrapping.</summary>
        private static void DrawText(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color text = theme.GetColor(NowColorToken.Text);
            Color accent = theme.GetColor(NowColorToken.Accent);

            NowRect left = new NowRect(body.x + 20f, body.y + 16f, (body.width - 60f) * 0.55f, body.height - 36f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, body.width - 60f - left.width, left.height);

            // ---- sizes and faces
            NowRect inner = Panel(left, "Sizes and faces");
            float y = inner.y + 8f;

            float[] sizes = { 11f, 14f, 18f, 24f, 34f, 48f };

            for (int i = 0; i < sizes.Length; ++i)
            {
                Now.Text(new NowRect(inner.x, y, inner.width, sizes[i] * 1.4f))
                    .SetFontSize(sizes[i])
                    .SetColor(text)
                    .Draw("Score: 1200  -  " + sizes[i].ToString("0") + "px");

                y += sizes[i] * 1.5f + 2f;
            }

            y += 6f;

            // The three faces beyond Regular. They are separate .ttf files in the exported family
            // (wwwroot/Fixtures/NowUI/NotoSans.family.json), so a missing one is a resource-provider result, not a
            // shader result - which is why they are drawn together and captioned as a set.
            Now.Text(new NowRect(inner.x, y, inner.width, 26f)).SetFontSize(20f).SetColor(text)
                .Draw("Regular face");
            y += 30f;

            Now.Text(new NowRect(inner.x, y, inner.width, 26f)).SetFontSize(20f).SetBold().SetColor(text)
                .Draw("Bold face");
            y += 30f;

            Now.Text(new NowRect(inner.x, y, inner.width, 26f)).SetFontSize(20f).SetItalic().SetColor(text)
                .Draw("Italic face");
            y += 30f;

            Now.Text(new NowRect(inner.x, y, inner.width, 26f)).SetFontSize(20f).SetBold().SetItalic().SetColor(text)
                .Draw("Bold italic face");
            y += 34f;

            Caption(new NowRect(inner.x, y, inner.width, 16f),
                "Four separate TTFs in the exported family. A face that falls back to Regular is a resource result, " +
                "not a renderer one.");
            y += 26f;

            // ---- outlines. Adaptive width against a light and a dark ground, because an outline that is present
            // but the wrong colour is invisible on exactly one of the two.
            NowRect outlineGround = new NowRect(inner.x, y, inner.width, 56f);
            Now.Rectangle(outlineGround).SetColor(new Color(0.95f, 0.72f, 0.2f, 1f)).SetRadius(8f).Draw();

            Now.Text(new NowRect(outlineGround.x + 12f, outlineGround.y + 12f, outlineGround.width - 24f, 34f))
                .SetFontSize(26f)
                .SetColor(Color.white)
                .SetOutlinePixels(2f)
                .SetOutlineColor(new Vector4(0f, 0f, 0f, 1f))
                .Draw("Outlined, 2px");

            CaptionUnder(outlineGround, "SetOutlinePixels(2) - a screen-pixel outline, so it holds at any density");

            // ---- whitespace, unicode, measurement
            NowRect innerRight = Panel(right, "Whitespace, Unicode, measurement, wrapping");
            float ry = innerRight.y + 8f;

            Now.Text(new NowRect(innerRight.x, ry, innerRight.width, 60f))
                .SetFontSize(15f)
                .SetColor(text)
                .Draw("tabs:\tone\ttwo\nnewline handled by the same call");
            ry += 66f;

            Caption(new NowRect(innerRight.x, ry, innerRight.width, 16f), "tabs and newlines in one Draw(string)");
            ry += 26f;

            // Latin-1, Greek, Cyrillic and typographic punctuation are all in NotoSans; emoji are not, and their
            // absence here is a FONT fact rather than a browser one. Said in the caption so the capture is not read
            // as "the browser cannot do Unicode".
            Now.Text(new NowRect(innerRight.x, ry, innerRight.width, 30f))
                .SetFontSize(20f)
                .SetColor(text)
                .Draw("aeiou / AEIOU  -  Greek and Cyrillic  -  quotes, dashes");
            ry += 34f;

            Now.Text(new NowRect(innerRight.x, ry, innerRight.width, 30f))
                .SetFontSize(20f)
                .SetColor(text)
                .Draw("Äéîõü ßçø Γαμμα Да — “quoted” …");
            ry += 34f;

            Caption(new NowRect(innerRight.x, ry, innerRight.width, 16f),
                "Glyphs bake into the atlas on first use. A missing glyph means NotoSans lacks it (emoji, CJK), " +
                "not that the browser does.");
            ry += 28f;

            // ---- measurement, drawn as a box around the measured advance so a wrong measurement is visible rather
            // than merely wrong. This is the API layout code depends on to reserve space.
            NowFontAsset font = Now.font;

            if (font != null)
            {
                const string measured = "MeasureText";
                const float measuredSize = 26f;

                Vector2 advance = font.MeasureText(measured, measuredSize);

                NowRect box = new NowRect(innerRight.x, ry, advance.x, measuredSize * 1.35f);

                Now.Rectangle(box).SetColor(new Color(0f, 0f, 0f, 0f)).SetOutline(1f, accent).Draw();

                Now.Text(box).SetFontSize(measuredSize).SetColor(text).Draw(measured);

                CaptionUnder(box, "the box is font.MeasureText's advance - the text should just fill it");
                ry += box.height + 28f;
            }

            // ---- wrapping
            const string paragraph =
                "NowUI wraps through NowTextWrap.Layout, which returns runs and a measured size; the same runs are " +
                "then drawn by NowTextWrap.Draw at an origin. Wrapping is therefore a two-step, caller-owned " +
                "operation with no hidden clock and no hidden allocation - the runs list belongs to the caller and " +
                "is reused across frames.";

            NowText style = Now.Text(default).SetFontSize(14f).SetColor(text);
            float wrapWidth = innerRight.width;

            Vector2 size = NowTextWrap.Layout(in style, paragraph, wrapWidth, s_WrapRuns);
            NowTextWrap.Draw(in style, paragraph, s_WrapRuns, new Vector2(innerRight.x, ry));

            Caption(new NowRect(innerRight.x, ry + size.y + 6f, innerRight.width, 16f),
                "wrapped to " + wrapWidth.ToString("0") + "px, measured " +
                size.x.ToString("0") + "x" + size.y.ToString("0"));
        }

        /// <summary>Reused across frames, which is the allocation contract NowTextWrap is built around.</summary>
        private static readonly List<NowTextRun> s_WrapRuns = new List<NowTextRun>();

        // --------------------------------------------------------------------------------------- text effects

        /// <summary>
        /// Gradient fills and the built-in glyph animations.
        /// </summary>
        /// <remarks>
        /// Read the flat control line at the top first. Every gradient below it is the SAME string in the same size,
        /// so if the gradients are being silently flattened, the whole panel reads as five copies of the control -
        /// which is a legible result. Without the control, "flat white text" and "a gradient that happens to start
        /// and end near white" are indistinguishable in a screenshot.
        /// </remarks>
        private static void DrawTextEffects(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color text = theme.GetColor(NowColorToken.Text);

            NowRect left = new NowRect(body.x + 20f, body.y + 16f, (body.width - 60f) * 0.5f, body.height - 36f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, left.width, left.height);

            NowRect inner = Panel(left, "Gradient fills");
            float y = inner.y + 8f;

            const string sample = "Gradient";
            const float size = 40f;

            Color from = new Color(0.25f, 0.86f, 0.98f, 1f);
            Color to = new Color(0.85f, 0.35f, 0.95f, 1f);

            Now.Text(new NowRect(inner.x, y, inner.width, size * 1.3f))
                .SetFontSize(size).SetColor(text).Draw(sample + " (control: flat)");
            y += size * 1.35f + 4f;

            Caption(new NowRect(inner.x, y, inner.width, 16f),
                "The control. Every line below is the same string at the same size with a gradient asked for.");
            y += 26f;

            Now.Text(new NowRect(inner.x, y, inner.width, size * 1.3f))
                .SetFontSize(size).SetGradient(from, to).SetGradientLinear(0f).Draw(sample + " linear 0deg");
            y += size * 1.35f + 6f;

            Now.Text(new NowRect(inner.x, y, inner.width, size * 1.3f))
                .SetFontSize(size).SetGradient(from, to).SetGradientLinear(90f).Draw(sample + " linear 90deg");
            y += size * 1.35f + 6f;

            Now.Text(new NowRect(inner.x, y, inner.width, size * 1.3f))
                .SetFontSize(size).SetGradient(from, to).SetGradientRadial().Draw(sample + " radial");
            y += size * 1.35f + 6f;

            Now.Text(new NowRect(inner.x, y, inner.width, size * 1.3f))
                .SetFontSize(size).SetGradient(from, to).SetGradientConic().Draw(sample + " conic");
            y += size * 1.35f + 6f;

            // The ramp form additionally needs the 256x256 gradient ramp atlas, which arrives as a shader GLOBAL
            // (Shader.SetGlobalTexture, NowGradient.cs:580) rather than through the material bag - a different
            // uniform-resolution path from everything else on this page (M2-ShaderPort.md section 7.3 step 3).
            Gradient ramp = Ramp();

            Now.Text(new NowRect(inner.x, y, inner.width, size * 1.3f))
                .SetFontSize(size).SetGradient(ramp).SetGradientLinear(0f).Draw(sample + " ramp keys");
            y += size * 1.35f + 6f;

            Caption(new NowRect(inner.x, y, inner.width, 32f),
                "The ramp line needs _NowGradientRampTexture, which reaches the backend as a shader global rather " +
                "than through a material - a resolution path nothing else here exercises.");

            // ---- animations. Driven by a caller-passed clock, so a capture at a fixed time is reproducible.
            NowRect innerRight = Panel(right, "Glyph animations");
            float ry = innerRight.y + 8f;

            float t = clock;

            Now.Text(new NowRect(innerRight.x, ry, innerRight.width, 40f))
                .SetFontSize(28f).SetColor(text)
                .SetAnimation(NowTextAnimations.Typewriter(14f)).SetTime(t)
                .Draw("Typewriter");
            ry += 46f;

            Now.Text(new NowRect(innerRight.x, ry, innerRight.width, 40f))
                .SetFontSize(28f).SetColor(text)
                .SetAnimation(NowTextAnimations.FadeIn()).SetTime(t)
                .Draw("FadeIn");
            ry += 46f;

            Now.Text(new NowRect(innerRight.x, ry, innerRight.width, 40f))
                .SetFontSize(28f).SetColor(text)
                .SetAnimation(NowTextAnimations.FadeUp()).SetTime(t)
                .Draw("FadeUp");
            ry += 46f;

            Now.Text(new NowRect(innerRight.x, ry, innerRight.width, 40f))
                .SetFontSize(28f).SetColor(text)
                .SetAnimation(NowTextAnimations.ScaleIn()).SetTime(t)
                .Draw("ScaleIn");
            ry += 46f;

            Now.Text(new NowRect(innerRight.x, ry, innerRight.width, 40f))
                .SetFontSize(28f).SetColor(text)
                .SetAnimation(NowTextAnimations.Wave()).SetTime(t)
                .Draw("Wave");
            ry += 52f;

            Caption(new NowRect(innerRight.x, ry, innerRight.width, 48f),
                "Animations move and fade glyph quads on the CPU; they add no shader program, so they are expected " +
                "to work whether or not the gradient branch does. The first four loop; a still capture catches one " +
                "phase of each.");
            ry += 58f;

            // Both at once, which is the combination TextStyling.md documents and the one most likely to expose an
            // ordering mistake between the gradient payload and the animated transform.
            Now.Text(new NowRect(innerRight.x, ry, innerRight.width, 46f))
                .SetFontSize(32f)
                .SetGradient(from, to)
                .SetGradientLinear(90f)
                .SetAnimation(NowTextAnimations.Wave())
                .SetTime(t)
                .Draw("Gradient + animation");

            CaptionUnder(new NowRect(innerRight.x, ry, innerRight.width, 46f), "the two combined");
        }

        /// <summary>A multi-key Unity ramp, built once - it is the input the ramp-atlas path is keyed on.</summary>
        private static Gradient Ramp()
        {
            if (s_Ramp != null)
                return s_Ramp;

            Gradient ramp = new Gradient();

            ramp.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.98f, 0.36f, 0.25f), 0f),
                    new GradientColorKey(new Color(0.98f, 0.80f, 0.25f), 0.4f),
                    new GradientColorKey(new Color(0.30f, 0.88f, 0.62f), 0.7f),
                    new GradientColorKey(new Color(0.35f, 0.55f, 0.98f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                });

            s_Ramp = ramp;
            return ramp;
        }

        private static Gradient s_Ramp;
    }
}
