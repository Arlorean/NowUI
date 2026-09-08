// Gallery areas: the paint primitives that are not rectangles or text. Gradients, ripple, glass, masks and effects.
//
// Three of these are predicted to work and two are the interesting ones: glass is predicted to fail on both of its
// halves at once (an unported program AND a render target that cannot be allocated), and masks are predicted to work
// but are the place where a uniform-array mistake would be least visible - the mask include is shared verbatim by
// both ported programs, so a bug there is a bug everywhere and would be easy to read as "the shape is slightly off".
using System.Text;
using NowUI;
using UnityEngine;

namespace NowUI.Web
{
    internal static partial class FeatureGallery
    {
        // --------------------------------------------------------------------------------------- gradients

        /// <summary>Linear, radial and conic geometry, spread modes, repetitions, ramps, and gradient shape styling.</summary>
        private static void DrawGradients(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            Color from = new Color(0.20f, 0.70f, 0.98f, 1f);
            Color to = new Color(0.95f, 0.35f, 0.55f, 1f);

            NowRect inner = Panel(new NowRect(body.x + 20f, body.y + 16f, body.width - 40f, body.height - 36f),
                                  "NowGradient - geometry, spread, ramps and shape styling");

            float cell = 168f;
            float cellH = 110f;
            float gap = 18f;
            float x = inner.x;
            float y = inner.y + 8f;

            // ---- linear, at four angles. Four rather than one because a gradient whose ANGLE is ignored still
            // renders as a gradient, and one sample cannot tell.
            float[] angles = { 0f, 45f, 90f, 135f };

            for (int i = 0; i < angles.Length; ++i)
            {
                NowRect at = new NowRect(x, y, cell, cellH);
                Now.Gradient(at, from, to).SetLinear(angles[i]).SetRadius(10f).Draw();
                CaptionUnder(at, "linear " + angles[i].ToString("0") + " degrees");
                x += cell + gap;
            }

            // ---- radial and conic
            NowRect radial = new NowRect(x, y, cell, cellH);
            Now.Gradient(radial, from, to).SetRadial().SetRadius(10f).Draw();
            CaptionUnder(radial, "radial (ellipse)");
            x += cell + gap;

            NowRect radialCircle = new NowRect(x, y, cell, cellH);
            Now.Gradient(radialCircle, from, to).SetRadial(NowGradientShape.Circle).SetRadius(10f).Draw();
            CaptionUnder(radialCircle, "radial (circle)");

            x = inner.x;
            y += cellH + 34f;

            NowRect offCentre = new NowRect(x, y, cell, cellH);
            Now.Gradient(offCentre, from, to).SetRadial(new Vector2(0.25f, 0.3f), 0.7f).SetRadius(10f).Draw();
            CaptionUnder(offCentre, "radial, centre 0.25/0.3");
            x += cell + gap;

            NowRect conic = new NowRect(x, y, cell, cellH);
            Now.Gradient(conic, from, to).SetConic().SetRadius(10f).Draw();
            CaptionUnder(conic, "conic");
            x += cell + gap;

            NowRect conicStart = new NowRect(x, y, cell, cellH);
            Now.Gradient(conicStart, from, to).SetConic(new Vector2(0.5f, 0.5f), 120f).SetRadius(10f).Draw();
            CaptionUnder(conicStart, "conic, start 120 degrees");
            x += cell + gap;

            // ---- spread modes, all with repetitions, because spread is only observable outside [0,1].
            NowRect repeat = new NowRect(x, y, cell, cellH);
            Now.Gradient(repeat, from, to).SetLinear(0f).SetRepetitions(3f)
                .SetSpread(NowGradientSpread.Repeat).SetRadius(10f).Draw();
            CaptionUnder(repeat, "spread Repeat, x3");
            x += cell + gap;

            NowRect mirror = new NowRect(x, y, cell, cellH);
            Now.Gradient(mirror, from, to).SetLinear(0f).SetRepetitions(3f)
                .SetSpread(NowGradientSpread.Mirror).SetRadius(10f).Draw();
            CaptionUnder(mirror, "spread Mirror, x3");
            x += cell + gap;

            NowRect clamp = new NowRect(x, y, cell, cellH);
            Now.Gradient(clamp, from, to).SetLinear(0f).SetRepetitions(3f)
                .SetSpread(NowGradientSpread.Clamp).SetRadius(10f).Draw();
            CaptionUnder(clamp, "spread Clamp, x3");

            x = inner.x;
            y += cellH + 34f;

            // ---- ramps. Multi-key, which is the form that needs the 256x256 ramp atlas as a shader global.
            NowRect rampRect = new NowRect(x, y, cell * 2f + gap, cellH);
            Now.Gradient(rampRect, Ramp()).SetLinear(0f).SetRadius(10f).Draw();
            CaptionUnder(rampRect, "Unity Gradient ramp, four colour keys");
            x += cell * 2f + gap * 2f;

            NowRect rampRadial = new NowRect(x, y, cell, cellH);
            Now.Gradient(rampRadial, Ramp()).SetRadial().SetRadius(10f).Draw();
            CaptionUnder(rampRadial, "the same ramp, radial");
            x += cell + gap;

            // ---- shape styling: a gradient is a full rectangle primitive, not just a fill.
            NowRect styled = new NowRect(x, y, cell, cellH);
            Now.Gradient(styled, from, to).SetLinear(45f)
                .SetRadius(34f, 6f, 34f, 6f)
                .SetOutline(2f, theme.GetColor(NowColorToken.Text))
                .Draw();
            CaptionUnder(styled, "per-corner radius + outline");
            x += cell + gap;

            NowRect blurred = new NowRect(x, y, cell, cellH);
            Now.Gradient(blurred, from, to).SetLinear(90f).SetRadius(16f).SetBlur(10f).Draw();
            CaptionUnder(blurred, "blur 10");
            x += cell + gap;

            NowRect tinted = new NowRect(x, y, cell, cellH);
            Now.Gradient(tinted, from, to).SetLinear(0f).SetRadius(16f)
                .SetTint(new Color(1f, 1f, 1f, 0.4f)).Draw();
            CaptionUnder(tinted, "tint alpha 0.4");

            y += cellH + 40f;

            Caption(new NowRect(inner.x, y, inner.width, 16f),
                "All of these draw through 'NowUI/UI Gradient', one of the four ported programs. The two ramp cells " +
                "additionally need _NowGradientRampTexture, which arrives as a shader global rather than in a " +
                "material bag.");
        }

        // --------------------------------------------------------------------------------------- ripple

        /// <summary>The ripple program: an expanding disc clipped to a rounded rect, driven by caller-passed geometry.</summary>
        private static void DrawRipple(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect inner = Panel(new NowRect(body.x + 20f, body.y + 16f, body.width - 40f, body.height - 36f),
                                  "NowRipple");

            float cell = 210f;
            float cellH = 110f;
            float gap = 20f;
            float y = inner.y + 10f;

            // The radius sweeps with the clock so a live page shows the effect animating, and a still capture
            // catches a definite phase rather than an ambiguous one - four cells at four offsets means at least one
            // is mid-expansion at any moment.
            float t = clock;

            for (int i = 0; i < 4; ++i)
            {
                NowRect at = new NowRect(inner.x + i * (cell + gap), y, cell, cellH);
                float phase = Mathf.Repeat(t * 0.45f + i * 0.25f, 1f);

                Now.Rectangle(at)
                    .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                    .SetRadius(12f)
                    .Draw();

                Now.Ripple(at)
                    .SetRadius(12f)
                    .SetOrigin(new Vector2(at.x + at.width * 0.5f, at.y + at.height * 0.5f))
                    .SetCircleRadius(phase * cell * 0.75f)
                    .SetColor(new Color(0.35f, 0.75f, 1f, 0.55f * (1f - phase)))
                    .Draw();

                CaptionUnder(at, "centre origin, phase " + phase.ToString("0.00"));
            }

            y += cellH + 42f;

            // Off-centre origins, which is how the control library uses it: the ripple starts where the pointer
            // pressed, and the rounded-rect clip is the control's own corner radius.
            for (int i = 0; i < 4; ++i)
            {
                NowRect at = new NowRect(inner.x + i * (cell + gap), y, cell, cellH);
                float phase = Mathf.Repeat(t * 0.45f + i * 0.25f, 1f);

                Now.Rectangle(at)
                    .SetColor(theme.GetColor(NowColorToken.Accent))
                    .SetRadius(cellH * 0.5f)
                    .Draw();

                Now.Ripple(at)
                    .SetRadius(cellH * 0.5f)
                    .SetOrigin(new Vector2(at.x + 24f, at.y + at.height - 18f))
                    .SetCircleRadius(phase * cell)
                    .SetColor(new Color(1f, 1f, 1f, 0.5f * (1f - phase)))
                    .Draw();

                CaptionUnder(at, "bottom-left origin, pill radius");
            }

            y += cellH + 42f;

            Caption(new NowRect(inner.x, y, inner.width, 16f),
                "'NowUI/UI Ripple' is the fourth ported program. The origin and the circle radius are caller-owned - " +
                "the builder has no clock of its own, which is what makes a capture of it reproducible.");
        }

        // --------------------------------------------------------------------------------------- glass

        /// <summary>
        /// A frosted pane over patterned content. Predicted to fail on the shader before it can fail on the blur.
        /// </summary>
        /// <remarks>
        /// The backdrop is drawn first and captioned first, deliberately: if the page dies on the pane, the last
        /// complete frame still shows the content the glass was meant to blur, which makes the failure readable as
        /// "the glass did not draw" rather than as "the page is blank".
        /// </remarks>
        private static void DrawGlass(NowRect body)
        {
            // Diagnostics on, and reserved before the pane is drawn, so the readout under the stage is NowUI's own
            // account of what the blur pipeline did this frame rather than an inference from the picture.
            NowGlassSettings.diagnosticsEnabled = true;
            NowGlassSettings.ReserveDiagnostics(4);

            NowRect inner = Panel(new NowRect(body.x + 20f, body.y + 16f, body.width - 40f, body.height - 36f),
                                  "NowGlass - measured, with the pipeline's own diagnostics underneath");

            Caption(new NowRect(inner.x, inner.y + 6f, inner.width, 32f),
                "'NowUI/UI Glass' and 'NowUI/UI Glass Blur' are BOTH ported now, and CreateRenderTexture no longer " +
                "throws - so the earlier prediction of a shader rejection is refuted. What to look for instead is " +
                "whether the stripes actually soften under the pane; the readout below says what NowGlass thinks " +
                "it did.");

            NowRect stage = new NowRect(inner.x, inner.y + 46f, inner.width, inner.height - 96f);

            // A backdrop with high-frequency detail, because a blur that runs but does nothing is invisible over a
            // flat colour and obvious over stripes.
            Now.Rectangle(stage).SetColor(new Color(0.10f, 0.12f, 0.20f, 1f)).SetRadius(12f).Draw();

            using (Now.Mask(NowMaskShape.RoundedRect(stage, 12f)))
            {
                for (int i = 0; i < 26; ++i)
                {
                    float t = i / 25f;

                    Now.Rectangle(new NowRect(stage.x + i * 46f, stage.y, 22f, stage.height))
                        .SetColor(new Color(0.2f + t * 0.6f, 0.35f, 0.9f - t * 0.5f, 1f))
                        .Draw();
                }
            }

            Now.Text(new NowRect(stage.x + 20f, stage.y + 16f, stage.width - 40f, 26f))
                .SetFontSize(18f)
                .SetColor(Color.white)
                .Draw("Backdrop content - the glass pane below should blur this");

            Now.Glass(stage.Inset(90f, 70f))
                .SetBlurRadius(18f)
                .SetBlurQuality(NowGlassBlurQuality.Balanced)
                .SetTint(new Color(1f, 1f, 1f, 0.22f))
                .SetRadius(18f)
                .SetOutline(1f, new Color(1f, 1f, 1f, 0.35f))
                .Draw();

            Now.Text(stage.Inset(90f, 70f).Inset(20f, 16f))
                .SetFontSize(20f)
                .SetColor(Color.white)
                .Draw("Frosted panel");

            // The readout. `lastFrameDiagnostics` is the previous frame's, which is exactly what is wanted: by the
            // time this text is laid out the current frame's panes have not been flushed yet.
            NowGlassFrameDiagnostics frame = NowGlassSettings.lastFrameDiagnostics;
            StringBuilder report = new StringBuilder(256);

            report.Append("panes=").Append(frame.paneCount)
                  .Append("  entries=").Append(frame.entryCount)
                  .Append("  fallbacks=").Append(frame.fallbackCount)
                  .Append("  copiedPixels=").Append(frame.copiedPixels)
                  .Append("  blurredPixels=").Append(frame.blurredPixels)
                  .Append("  blurPasses=").Append(frame.blurPasses);

            NowGlassDiagnosticEntry entry;
            for (int i = 0; i < frame.entryCount && i < 3; ++i)
            {
                if (!NowGlassSettings.TryGetLastFrameDiagnostic(i, out entry))
                    break;

                report.Append("   |   [").Append(i).Append("] host=").Append(entry.host ?? "(null)")
                      .Append(" reason=").Append(entry.fallbackReason.ToString())
                      .Append(" quality=").Append(entry.quality.ToString())
                      .Append(" radius=").Append(entry.blurRadius.ToString("0.##"))
                      .Append(" src=").Append(entry.sourceWidth).Append('x').Append(entry.sourceHeight)
                      .Append(" blurred=").Append(entry.blurredWidth).Append('x').Append(entry.blurredHeight)
                      .Append(" downsample=").Append(entry.blurDownsample)
                      .Append(" iterations=").Append(entry.blurIterations)
                      .Append(" passes=").Append(entry.blurPasses);
            }

            Caption(new NowRect(inner.x, stage.y + stage.height + 6f, inner.width, 40f), report.ToString());
        }

        // --------------------------------------------------------------------------------------- masks

        /// <summary>Rect clip, the four analytic shapes, feathering, nesting, and a texture mask.</summary>
        private static void DrawMasks(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect inner = Panel(new NowRect(body.x + 20f, body.y + 16f, body.width - 40f, body.height - 36f),
                                  "Now.Mask - rect, analytic shapes, feather, nesting, texture");

            float cell = 166f;
            float cellH = 132f;
            float gap = 14f;
            float x = inner.x;
            float y = inner.y + 8f;

            // Every cell clips the SAME content, so the only difference between cells is the mask itself.
            // Diagonal stripes, because a mask boundary that is one pixel off is visible on a diagonal edge and is
            // not visible on a flat fill.
            NowRect cellRect;

            cellRect = new NowRect(x, y, cell, cellH);
            using (Now.Mask(cellRect.Inset(16f)))
                Stripes(cellRect);
            CaptionUnder(cellRect, "Now.Mask(rect) - hard clip");
            x += cell + gap;

            cellRect = new NowRect(x, y, cell, cellH);
            using (Now.Mask(NowMaskShape.RoundedRect(cellRect.Inset(16f), 26f)))
                Stripes(cellRect);
            CaptionUnder(cellRect, "RoundedRect, radius 26");
            x += cell + gap;

            cellRect = new NowRect(x, y, cell, cellH);
            using (Now.Mask(NowMaskShape.Circle(new Vector2(cellRect.x + cell * 0.5f, cellRect.y + cellH * 0.5f), 52f)))
                Stripes(cellRect);
            CaptionUnder(cellRect, "Circle");
            x += cell + gap;

            cellRect = new NowRect(x, y, cell, cellH);
            using (Now.Mask(NowMaskShape.Ellipse(cellRect.Inset(16f, 26f))))
                Stripes(cellRect);
            CaptionUnder(cellRect, "Ellipse");
            x += cell + gap;

            cellRect = new NowRect(x, y, cell, cellH);
            using (Now.Mask(NowMaskShape.Capsule(
                       new Vector2(cellRect.x + 44f, cellRect.y + cellH * 0.5f),
                       new Vector2(cellRect.x + cell - 44f, cellRect.y + cellH * 0.5f),
                       34f)))
                Stripes(cellRect);
            CaptionUnder(cellRect, "Capsule");
            x += cell + gap;

            // Asymmetric corners on a mask. The mask include picks its corner in UI space (y down) while the
            // rectangle SDF picks its corner in the y-up SDF space (M2-ShaderPort.md section 5.2) - getting that
            // backwards rotates a rounded mask 180 degrees in y, which is invisible on a symmetric shape and
            // obvious on this one.
            cellRect = new NowRect(x, y, cell, cellH);
            using (Now.Mask(NowMaskShape.RoundedRect(cellRect.Inset(16f), new Vector4(44f, 4f, 44f, 4f))))
                Stripes(cellRect);
            CaptionUnder(cellRect, "RoundedRect 44/4/44/4 (asymmetric)");

            x = inner.x;
            y += cellH + 34f;

            // ---- feather. Zero still gets one pixel of AA; the rest is additive.
            float[] feathers = { 0f, 2f, 6f, 14f };

            for (int i = 0; i < feathers.Length; ++i)
            {
                cellRect = new NowRect(x, y, cell, cellH);

                using (Now.Mask(NowMaskShape.Circle(
                           new Vector2(cellRect.x + cell * 0.5f, cellRect.y + cellH * 0.5f), 52f)
                           .SetFeather(feathers[i])))
                    Stripes(cellRect);

                CaptionUnder(cellRect, "feather " + feathers[i].ToString("0") + " screen px");
                x += cell + gap;
            }

            // ---- nesting: the intersection of two shapes, which is what a nested scope means.
            cellRect = new NowRect(x, y, cell, cellH);
            using (Now.Mask(NowMaskShape.Circle(new Vector2(cellRect.x + cell * 0.42f, cellRect.y + cellH * 0.5f), 54f)))
            using (Now.Mask(NowMaskShape.Circle(new Vector2(cellRect.x + cell * 0.62f, cellRect.y + cellH * 0.5f), 54f)))
                Stripes(cellRect);
            CaptionUnder(cellRect, "two nested circles = intersection");
            x += cell + gap;

            // ---- texture mask. A different code path from the analytic ones: a second sampler bound per draw,
            // with its own rect/params/transform arrays at capacity 2.
            cellRect = new NowRect(x, y, cell, cellH);
            using (Now.Mask(NowMaskTexture.Alpha(SoftDisc(), cellRect.Inset(12f))))
                Stripes(cellRect);
            CaptionUnder(cellRect, "texture mask (alpha channel)");

            y += cellH + 40f;

            Caption(new NowRect(inner.x, y, inner.width, 32f),
                "Every cell clips identical diagonal stripes, so the only variable is the mask. NowUIMask.cginc is " +
                "shared verbatim by both ported programs, which is why a mask bug would be a bug in text and " +
                "rectangles alike rather than a bug in one area.");
        }

        /// <summary>Diagonal stripes, the mask test content: a boundary error is visible on a diagonal edge.</summary>
        private static void Stripes(NowRect at)
        {
            for (int i = -6; i < 22; ++i)
            {
                Now.Line(
                        new Vector2(at.x + i * 16f, at.y),
                        new Vector2(at.x + i * 16f + at.height, at.y + at.height))
                    .SetWidth(9f)
                    .SetColor((i % 2 == 0)
                        ? new Color(0.30f, 0.70f, 0.98f, 1f)
                        : new Color(0.95f, 0.45f, 0.35f, 1f))
                    .Draw();
            }
        }

        /// <summary>
        /// A 64x64 radial alpha ramp, built once: the source for the texture-mask cell.
        /// </summary>
        /// <remarks>
        /// Its ALPHA varies and its RGB is constant white, so it can only produce a visible mask if the alpha
        /// channel is the one being read. A grey-scale disc would also work if the shader read red by mistake.
        /// </remarks>
        private static Texture2D SoftDisc()
        {
            if (s_SoftDisc != null)
                return s_SoftDisc;

            const int size = 64;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[size * size];

            for (int py = 0; py < size; ++py)
            {
                for (int px = 0; px < size; ++px)
                {
                    float dx = (px + 0.5f) / size - 0.5f;
                    float dy = (py + 0.5f) / size - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;

                    pixels[py * size + px] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            s_SoftDisc = texture;
            return texture;
        }

        private static Texture2D s_SoftDisc;

        // --------------------------------------------------------------------------------------- effects

        /// <summary>
        /// NowEffects.Modifier: the mesh-capture deformers.
        /// </summary>
        /// <remarks>
        /// Only the mesh path. SetRenderToTexture and NowEffects.Snapshot both need a render target, and
        /// CreateRenderTexture throws in this backend - so drawing them here would only prove that a documented
        /// unimplemented method is unimplemented, at the cost of killing the frame loop before the mesh path could
        /// be seen. They are recorded as unported in the matrix rather than demonstrated as broken here.
        /// </remarks>
        private static void DrawEffects(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color accent = theme.GetColor(NowColorToken.Accent);

            NowRect inner = Panel(new NowRect(body.x + 20f, body.y + 16f, body.width - 40f, body.height - 36f),
                                  "NowEffects.Modifier - mesh capture only");

            float t = clock;
            float cell = 260f;
            float cellH = 150f;
            float gap = 24f;
            float y = inner.y + 10f;

            // Undeformed control, so a deformer that silently does nothing is visible as "identical to the control"
            // rather than as "looks like a rectangle, presumably correct".
            NowRect control = new NowRect(inner.x, y, cell, cellH);
            DeformableContent(control, accent, "control");
            CaptionUnder(control, "no modifier (control)");

            NowRect wave = new NowRect(inner.x + cell + gap, y, cell, cellH);

            using (NowEffects.Modifier(NowDeformers.Wave(t, 6f, 40f)).SetSubdivision(6).Begin())
                DeformableContent(wave, accent, "wave");

            CaptionUnder(wave, "Wave, subdivision 6");

            NowRect coarse = new NowRect(inner.x + (cell + gap) * 2f, y, cell, cellH);

            // Subdivision 1 against subdivision 6: same deformer, different tessellation. If the two look the same,
            // the subdivision parameter is not reaching the mesh.
            using (NowEffects.Modifier(NowDeformers.Wave(t, 6f, 40f)).SetSubdivision(1).Begin())
                DeformableContent(coarse, accent, "wave");

            CaptionUnder(coarse, "the same Wave, subdivision 1");

            NowRect genie = new NowRect(inner.x + (cell + gap) * 3f, y, cell, cellH);

            using (NowEffects.Modifier(NowDeformers.Genie(
                       new NowRect(genie.x + genie.width * 0.5f - 20f, genie.y + genie.height + 60f, 40f, 10f),
                       Mathf.Repeat(t * 0.4f, 1f)))
                       .SetSubdivision(8).Begin())
                DeformableContent(genie, accent, "genie");

            CaptionUnder(genie, "Genie toward a target rect");

            y += cellH + 48f;

            // Text inside a modifier, which is the case that needs SetSubdivideText: glyph quads are small, so
            // without it a deformer moves each glyph rigidly instead of bending it.
            NowRect textWave = new NowRect(inner.x, y, cell * 2f + gap, cellH);

            using (NowEffects.Modifier(NowDeformers.Wave(t, 4f, 26f))
                       .SetSubdivision(6).SetSubdivideText().Begin())
            {
                Now.Rectangle(textWave).SetColor(theme.GetColor(NowColorToken.SurfaceMuted)).SetRadius(12f).Draw();

                Now.Text(textWave.Inset(18f, 40f))
                    .SetFontSize(30f)
                    .SetColor(theme.GetColor(NowColorToken.Text))
                    .Draw("Deformed text");
            }

            CaptionUnder(textWave, "SetSubdivideText - glyphs bend rather than move rigidly");

            y += cellH + 40f;

            Caption(new NowRect(inner.x, y, inner.width, 48f),
                "Mesh capture adds no shader program: it subdivides and moves vertices that still draw through UI " +
                "Rectangle and Text Renderer, which is why it is predicted to work. The texture path " +
                "(SetRenderToTexture, NowEffects.Snapshot) is deliberately absent - it needs a render target, and " +
                "this backend's CreateRenderTexture throws.");
        }

        /// <summary>A panel with a label and a few bars: enough internal structure that a deformation is legible.</summary>
        private static void DeformableContent(NowRect at, Color accent, string label)
        {
            Now.Rectangle(at)
                .SetColor(accent)
                .SetRadius(12f)
                .Draw();

            for (int i = 0; i < 5; ++i)
            {
                Now.Rectangle(new NowRect(at.x + 16f, at.y + 22f + i * 22f, at.width - 32f, 10f))
                    .SetColor(new Color(1f, 1f, 1f, 0.55f))
                    .SetRadius(5f)
                    .Draw();
            }
        }
    }
}
