// Gallery areas: the interface layer. Layout, themes, the value controls the demo does not reach, and the extension
// status page.
//
// These are the areas where "works in the browser" is least about shaders and most about the shim: layout is
// arithmetic, themes are ScriptableObject-backed data resolved through the engine-free CreateInstance path, and the
// extensions are a question about what the csproj references rather than about what WebGL2 can draw. They are worth
// capturing for exactly that reason - a rendering slice that only ever tests rendering will report a green board
// while half the library is unreachable.
using System;
using System.Collections.Generic;
using System.Globalization;
using NowUI;
using UnityEngine;

namespace NowUI.Web
{
    internal static partial class FeatureGallery
    {
        // --------------------------------------------------------------------------------------- layout

        /// <summary>
        /// NowLayout: rows, columns, gaps, padding, alignment, stretch, spacers and nesting.
        /// </summary>
        /// <remarks>
        /// Every reserved rect is outlined, because layout is invisible when it works - a column of labels looks the
        /// same whether the column computed the positions or the labels happened to be drawn there. The outlines are
        /// the only way a still capture can show that the boxes are where the layout says they are.
        /// <para>Drawn through <c>RunMeasured</c> for the reason DemoScene.cs records: plain <c>NowLayout.Column</c>
        /// resolves main-axis stretch from the PREVIOUS frame's measurement, so on frame one every stretched rect
        /// reports width 0 - which is precisely the frame a one-shot headless capture would photograph.</para>
        /// </remarks>
        private static void DrawLayout(NowRect body)
        {
            NowRect left = new NowRect(body.x + 20f, body.y + 16f, (body.width - 60f) * 0.5f, body.height - 36f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, left.width, left.height);

            NowRect innerLeft = Panel(left, "Columns, rows, gaps, padding, alignment");
            NowRect innerRight = Panel(right, "Stretch, spacers, nesting, scrolling");

            NowLayout.RunMeasured(innerLeft, s_DrawLayoutLeft, spacing: 10f, padding: 0f);
            NowLayout.RunMeasured(innerRight, s_DrawLayoutRight, spacing: 10f, padding: 0f);
        }

        private static readonly Action s_DrawLayoutLeft = DrawLayoutLeft;
        private static readonly Action s_DrawLayoutRight = DrawLayoutRight;

        private static void DrawLayoutLeft()
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color muted = theme.GetColor(NowColorToken.TextMuted);
            Color text = theme.GetColor(NowColorToken.Text);

            NowLayout.Label("A row with three fixed children and gap 12", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().Gap(12f).Begin())
            {
                Box(NowLayout.ReserveRect(width: 90f, height: 40f), "90");
                Box(NowLayout.ReserveRect(width: 130f, height: 40f), "130");
                Box(NowLayout.ReserveRect(width: 70f, height: 40f), "70");
            }

            NowLayout.Space(6f);
            NowLayout.Label("The same row, children aligned Center / Start / End on the cross axis", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(12f).Begin())
            {
                Box(NowLayout.ReserveRect(width: 90f, height: 24f), "24");
                Box(NowLayout.ReserveRect(width: 90f, height: 48f), "48");
                Box(NowLayout.ReserveRect(width: 90f, height: 36f), "36");
            }

            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Start).Gap(12f).Begin())
            {
                Box(NowLayout.ReserveRect(width: 90f, height: 24f), "24");
                Box(NowLayout.ReserveRect(width: 90f, height: 48f), "48");
                Box(NowLayout.ReserveRect(width: 90f, height: 36f), "36");
            }

            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.End).Gap(12f).Begin())
            {
                Box(NowLayout.ReserveRect(width: 90f, height: 24f), "24");
                Box(NowLayout.ReserveRect(width: 90f, height: 48f), "48");
                Box(NowLayout.ReserveRect(width: 90f, height: 36f), "36");
            }

            NowLayout.Space(6f);
            NowLayout.Label("A column, gap 6, inside a row - nesting", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().Gap(14f).Begin())
            {
                using (NowLayout.Column().Gap(6f).Begin())
                {
                    Box(NowLayout.ReserveRect(width: 120f, height: 26f), "a");
                    Box(NowLayout.ReserveRect(width: 120f, height: 26f), "b");
                    Box(NowLayout.ReserveRect(width: 120f, height: 26f), "c");
                }

                using (NowLayout.Column().Gap(6f).Begin())
                {
                    Box(NowLayout.ReserveRect(width: 160f, height: 40f), "d");
                    Box(NowLayout.ReserveRect(width: 160f, height: 46f), "e");
                }
            }

            NowLayout.Space(6f);
            NowLayout.Label("Padding: the same row inside a group padded by 16", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().Padding(16f).Gap(10f).Begin())
            {
                Box(NowLayout.ReserveRect(width: 100f, height: 34f), "padded");
                Box(NowLayout.ReserveRect(width: 100f, height: 34f), "padded");
            }

            NowLayout.Label(
                    "Every outlined box is a rect NowLayout reserved. Layout is invisible when it is right, so the " +
                    "outlines are the evidence.",
                    11, muted)
                .Draw();

            NowLayout.Label("Labels also flow: this one is drawn at the default themed size.", 13, text).Draw();
        }

        private static void DrawLayoutRight()
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color muted = theme.GetColor(NowColorToken.TextMuted);

            NowLayout.Label("Main-axis stretch: fixed / stretch / fixed", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().Gap(10f).Begin())
            {
                Box(NowLayout.ReserveRect(width: 80f, height: 34f), "80");
                Box(NowLayout.ReserveRect(height: 34f, stretchWidth: true), "stretch");
                Box(NowLayout.ReserveRect(width: 80f, height: 34f), "80");
            }

            NowLayout.Label("Two stretches share what is left, 1:1", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().Gap(10f).Begin())
            {
                Box(NowLayout.ReserveRect(width: 60f, height: 34f), "60");
                Box(NowLayout.ReserveRect(height: 34f, stretchWidth: true), "stretch");
                Box(NowLayout.ReserveRect(height: 34f, stretchWidth: true), "stretch");
            }

            NowLayout.Label("Spacer pushes the rest to the far edge", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().Gap(10f).Begin())
            {
                Box(NowLayout.ReserveRect(width: 90f, height: 34f), "left");
                NowLayout.Spacer();
                Box(NowLayout.ReserveRect(width: 90f, height: 34f), "right");
            }

            NowLayout.Space(6f);
            NowLayout.Label("A scroll view sized by its content, with reserved rows inside", 12, muted).Draw();

            NowRect viewport = NowLayout.ReserveRect(height: 150f, stretchWidth: true);

            Now.Rectangle(viewport)
                .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                .SetRadius(8f)
                .Draw();

            using (Now.ScrollView(viewport.Inset(6f)).Begin())
            {
                for (int i = 0; i < 18; ++i)
                {
                    using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(8f).Begin())
                    {
                        NowLayout.Label("row " + i.ToString(CultureInfo.InvariantCulture), 13, muted).Draw();
                        NowLayout.Spacer();
                        Box(NowLayout.ReserveRect(width: 60f, height: 18f), "");
                    }
                }
            }

            NowLayout.Space(6f);
            NowLayout.Label(
                    "Content taller than the viewport: the bar appears per axis, from the group's measured extent.",
                    11, muted)
                .Draw();
        }

        /// <summary>An outlined box with an optional label, so a reserved rect is visible as a rect.</summary>
        private static void Box(NowRect rect, string label)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            Now.Rectangle(rect)
                .SetColor(new Color(0f, 0f, 0f, 0f))
                .SetRadius(4f)
                .SetOutline(1f, theme.GetColor(NowColorToken.Accent))
                .Draw();

            if (string.IsNullOrEmpty(label))
                return;

            Now.Text(rect.Inset(4f, Mathf.Max(0f, (rect.height - 14f) * 0.5f)))
                .SetFontSize(11f)
                .SetColor(theme.GetColor(NowColorToken.TextMuted))
                .Draw(label);
        }

        // --------------------------------------------------------------------------------------- themes

        /// <summary>
        /// Every colour token, in both built-in palettes.
        /// </summary>
        /// <remarks>
        /// Two theme assets are minted here rather than toggling <c>NowTheme.preferDark</c> mid-frame: the ambient
        /// theme is what the rest of the gallery's chrome is drawn with, and flipping it in the middle of a frame
        /// would change the page around the panel that is trying to describe it. <c>ResetToDefaults(bool)</c> is
        /// public and does exactly what the built-in defaults do, so these two are the built-in palettes rather
        /// than a copy of them.
        /// </remarks>
        private static void DrawTheme(NowRect body)
        {
            NowThemeAsset light = ThemeAsset(dark: false);
            NowThemeAsset dark = ThemeAsset(dark: true);

            NowRect left = new NowRect(body.x + 20f, body.y + 16f, (body.width - 60f) * 0.5f, body.height - 36f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, left.width, left.height);

            DrawPalette(Panel(left, "Built-in light palette"), light);
            DrawPalette(Panel(right, "Built-in dark palette"), dark);
        }

        /// <summary>One palette as a grid of swatches, each labelled with its token name.</summary>
        private static void DrawPalette(NowRect inner, NowThemeAsset theme)
        {
            Array tokens = Enum.GetValues(typeof(NowColorToken));

            const float swatch = 34f;
            const float rowHeight = 42f;
            float columnWidth = inner.width * 0.5f - 8f;

            for (int i = 0; i < tokens.Length; ++i)
            {
                NowColorToken token = (NowColorToken)tokens.GetValue(i);

                int column = i / 14;
                int row = i % 14;

                float x = inner.x + column * (columnWidth + 16f);
                float y = inner.y + 8f + row * rowHeight;

                NowRect box = new NowRect(x, y, swatch, swatch);

                // Checkerboard behind the swatch, because Shadow and Scrim are translucent by design and would be
                // indistinguishable from each other, and from nothing, over a flat panel.
                Now.Rectangle(box).SetTexture(Checkerboard()).SetUV(new Vector4(0f, 0f, 0.25f, 0.25f)).SetRadius(6f).Draw();
                Now.Rectangle(box).SetColor(theme.GetColor(token)).SetRadius(6f).Draw();
                Now.Rectangle(box)
                    .SetColor(new Color(0f, 0f, 0f, 0f))
                    .SetRadius(6f)
                    .SetOutline(1f, new Color(0.5f, 0.5f, 0.5f, 0.5f))
                    .Draw();

                Now.Text(new NowRect(x + swatch + 10f, y + 4f, columnWidth - swatch - 12f, 16f))
                    .SetFontSize(12f)
                    .SetColor(NowTheme.themeAsset.GetColor(NowColorToken.Text))
                    .Draw(token.ToString());

                Color color = theme.GetColor(token);

                Now.Text(new NowRect(x + swatch + 10f, y + 19f, columnWidth - swatch - 12f, 14f))
                    .SetFontSize(10f)
                    .SetColor(NowTheme.themeAsset.GetColor(NowColorToken.TextMuted))
                    .Draw(Hex(color));
            }
        }

        /// <summary>The two built-in palettes as assets, minted once each.</summary>
        private static NowThemeAsset ThemeAsset(bool dark)
        {
            if (dark)
            {
                if (s_DarkTheme == null)
                {
                    s_DarkTheme = ScriptableObject.CreateInstance<NowThemeAsset>();
                    s_DarkTheme.name = "Gallery Dark";
                    s_DarkTheme.ResetToDefaults(true);
                }

                return s_DarkTheme;
            }

            if (s_LightTheme == null)
            {
                s_LightTheme = ScriptableObject.CreateInstance<NowThemeAsset>();
                s_LightTheme.name = "Gallery Light";
                s_LightTheme.ResetToDefaults(false);
            }

            return s_LightTheme;
        }

        private static NowThemeAsset s_LightTheme;
        private static NowThemeAsset s_DarkTheme;

        /// <summary>#RRGGBBAA, invariant - InvariantGlobalization is on for this project.</summary>
        private static string Hex(Color color)
        {
            return "#" +
                Channel(color.r) + Channel(color.g) + Channel(color.b) + Channel(color.a);
        }

        private static string Channel(float value)
        {
            int b = Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
            return b.ToString("X2", CultureInfo.InvariantCulture);
        }

        // --------------------------------------------------------------------------------------- value controls

        /// <summary>
        /// The controls the demo does not reach.
        /// </summary>
        /// <remarks>
        /// Two are deliberately absent, and their absence is a statement rather than an oversight: ColorPicker needs
        /// 'NowUI/Color Picker' and AnimationCurveField needs 'NowUI/UI Bezier', neither of which this backend has.
        /// Drawing them would stop the frame loop before anything else here could be seen, so they are recorded as
        /// unported in the matrix and named in the caption at the foot of this area.
        /// <para>Popup-bearing controls (Dropdown, ComboBox, MaskField) are drawn CLOSED. Their popups are deferred
        /// overlays, which is a different question from the closed field, and one that needs a pointer to answer.</para>
        /// </remarks>
        private static void DrawFields(NowRect body)
        {
            NowRect left = new NowRect(body.x + 20f, body.y + 16f, (body.width - 60f) * 0.5f, body.height - 36f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, left.width, left.height);

            NowRect innerLeft = Panel(left, "Selection, tabs, sections");
            NowRect innerRight = Panel(right, "Values, text, trees, indicators");

            NowLayout.RunMeasured(innerLeft, s_DrawFieldsLeft, spacing: 10f, padding: 0f);
            NowLayout.RunMeasured(innerRight, s_DrawFieldsRight, spacing: 10f, padding: 0f);

            // The popup-bearing controls' selections, as a line a driver can read through
            // window.nowui.debugState() instead of reading the field's pixels. Set unconditionally, for the reason
            // FeatureGallery.areaState gives: a readout that only exists under ?debug=1 is a readout that is wrong
            // in the other mode. These four are the ones whose value is committed by a DEFERRED OVERLAY - a popup
            // that draws after the screen's UI has closed - so "the popup changed the value" is the one thing
            // about this area a screenshot answers least well.
            areaState =
                "resolution=" + k_Resolutions[s_Resolution] +
                ";country=" + k_Countries[s_Country] +
                ";channels=" + s_Channels +
                ";due=" + s_Due.ToString("yyyy-MM-dd") +
                ";alarm=" + s_Alarm.ToString(@"hh\:mm");
        }

        private static readonly Action s_DrawFieldsLeft = DrawFieldsLeft;
        private static readonly Action s_DrawFieldsRight = DrawFieldsRight;

        // Caller-owned control state. An immediate-mode UI keeps none of its own, so this is all of it.
        private static readonly string[] k_Resolutions = { "1280 x 720", "1920 x 1080", "2560 x 1440", "3840 x 2160" };
        private static readonly string[] k_Countries = { "Argentina", "Belgium", "Canada", "Denmark", "Estonia", "Finland" };
        private static readonly string[] k_Pages = { "General", "Rendering", "Input", "About" };
        private static readonly string[] k_Channels = { "Music", "Effects", "Voice", "UI" };

        private static int s_Resolution = 1;
        private static int s_Country = 2;
        private static int s_Page;
        private static int s_Channels = 0b0101;
        private static int s_Quality = 1;
        private static bool s_Advanced = true;
        private static bool s_Chip = true;
        private static float s_Speed = 12.5f;
        private static int s_Lives = 3;
        private static Vector3 s_Spawn = new Vector3(1f, 2.5f, -3f);
        private static string s_Note = "A text area grows with its content and\nkeeps every character.";
        private static DateTime s_Due = new DateTime(2026, 9, 21);
        private static TimeSpan s_Alarm = new TimeSpan(7, 30, 0);
        private static readonly NowTreeViewState s_Tree = new NowTreeViewState();

        private static void DrawFieldsLeft()
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color muted = theme.GetColor(NowColorToken.TextMuted);

            NowLayout.Label("Dropdown (closed - its popup is a deferred overlay)", 12, muted).Draw();
            NowLayout.Dropdown(k_Resolutions).SetStretchWidth().Draw(ref s_Resolution);

            NowLayout.Label("ComboBox - a searchable dropdown", 12, muted).Draw();
            NowLayout.ComboBox(k_Countries).SetStretchWidth().Draw(ref s_Country);

            NowLayout.Label("MaskField - multi-select, bit per option", 12, muted).Draw();
            NowLayout.MaskField(k_Channels).SetStretchWidth().Draw(ref s_Channels);

            NowLayout.Space(4f);
            NowLayout.Label("TabBar", 12, muted).Draw();
            NowLayout.TabBar(k_Pages).SetStretchWidth().Draw(ref s_Page);

            NowLayout.Space(4f);
            NowLayout.Label("Radio group - caller owns the selection", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().Gap(16f).Begin())
            {
                if (NowLayout.Radio("Low", s_Quality == 0).Draw()) s_Quality = 0;
                if (NowLayout.Radio("Medium", s_Quality == 1).Draw()) s_Quality = 1;
                if (NowLayout.Radio("High", s_Quality == 2).Draw()) s_Quality = 2;
            }

            NowLayout.Space(4f);
            NowLayout.Foldout("Advanced").Draw(ref s_Advanced);

            if (s_Advanced)
            {
                using (NowLayout.Column().Padding(new Vector4(18f, 4f, 0f, 4f)).Gap(6f).Begin())
                {
                    NowLayout.Label("A section whose contents the caller draws conditionally.", 12, muted).Draw();
                    NowLayout.Checkbox("Nested checkbox").Draw(ref s_Chip);
                }
            }

            NowLayout.Space(4f);
            NowLayout.Label("Badges and chips", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(10f).Begin())
            {
                NowLayout.Badge("3").Draw();
                NowLayout.Badge("NEW").SetStyle(NowRectangleStyle.Accent).Draw();
                NowLayout.Badge("!").SetStyle(NowRectangleStyle.Danger).Draw();

                if (NowLayout.Chip("Filter: Active").SetSelected(s_Chip).Draw())
                    s_Chip = !s_Chip;
            }
        }

        private static void DrawFieldsRight()
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color muted = theme.GetColor(NowColorToken.TextMuted);

            NowLayout.Label("Numeric fields - arithmetic input, optional range", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(10f).Begin())
            {
                NowLayout.Label("Speed", 12, muted).SetWidth(56f).Draw();
                NowLayout.FloatField().SetRange(0f, 100f).SetStretchWidth().Draw(ref s_Speed);
                NowLayout.Label("Lives", 12, muted).SetWidth(46f).Draw();
                NowLayout.IntField().SetRange(0, 99).SetWidth(70f).Draw(ref s_Lives);
            }

            NowLayout.Label("Vector3Field - X/Y/Z components, scrubbable labels", 12, muted).Draw();
            NowLayout.Vector3Field().SetStretchWidth().Draw(ref s_Spawn);

            NowLayout.Space(4f);
            NowLayout.Label("TextArea - multi-line, wrapped, caret-aware", 12, muted).Draw();
            NowLayout.TextArea().SetLines(3, 5).SetStretchWidth().Draw(ref s_Note);

            NowLayout.Space(4f);
            NowLayout.Label("DatePicker and TimePicker (closed fields)", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().Gap(10f).Begin())
            {
                NowLayout.DatePicker().SetStretchWidth().Draw(ref s_Due);
                NowLayout.TimePicker().Set24Hour(false).SetStretchWidth().Draw(ref s_Alarm);
            }

            NowLayout.Space(4f);
            NowLayout.Label("TreeView - expansion and selection live in caller-owned state", 12, muted).Draw();

            using (var tree = NowLayout.TreeView(s_Tree).Begin())
            {
                if (tree.BeginNode("Assets"))
                {
                    if (tree.BeginNode("NowUI"))
                    {
                        tree.Node("Runtime");
                        tree.Node("Extensions");
                        tree.EndNode();
                    }

                    tree.Node("Fonts");
                    tree.EndNode();
                }

                if (tree.BeginNode("Standalone"))
                {
                    tree.Node("NowUI.Engine");
                    tree.Node("NowUI.Web");
                    tree.EndNode();
                }
            }

            NowLayout.Space(4f);
            NowLayout.Label("Indicators", 12, muted).Draw();

            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(12f).Begin())
            {
                NowLayout.ProgressBar(0.62f).SetStretchWidth().SetHeight(8f).Draw();
                NowLayout.ProgressBar().SetIndeterminate().SetTime(clock).SetStretchWidth().SetHeight(8f).Draw();
            }

            // A wrapped caption rather than a NowLayout.Label, because a Label is a single line and would run off
            // the panel - which reads, in a capture, as a rendering fault rather than as a long sentence.
            Caption(NowLayout.ReserveRect(height: 44f, stretchWidth: true),
                "Absent on purpose: ColorPicker needs 'NowUI/Color Picker' and AnimationCurveField needs " +
                "'NowUI/UI Bezier'. Neither is ported, and drawing either would stop the frame loop before " +
                "anything above could be seen.");
        }

        // --------------------------------------------------------------------------------------- extensions

        /// <summary>
        /// The index page for the seven extension assemblies: what each one is, which area exercises it, and what
        /// the capture of that area found.
        /// </summary>
        /// <remarks>
        /// This page used to say "not referenced by the build", and that was true when it was written: the web
        /// project referenced NowUI.Runtime and NowUI.Engine and nothing else, so no extension type was linked into
        /// the wasm payload and there was nothing to draw. All seven are now referenced (NowUI.Web.csproj), six of
        /// them are drawn with real content in their own areas, and the status column below reports what those
        /// captures showed rather than what this file predicted.
        /// </remarks>
        private static void DrawExtensions(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color text = theme.GetColor(NowColorToken.Text);
            Color muted = theme.GetColor(NowColorToken.TextMuted);
            Color success = theme.GetColor(NowColorToken.Success);
            Color warning = theme.GetColor(NowColorToken.Warning);
            Color danger = theme.GetColor(NowColorToken.Danger);

            NowRect inner = Panel(new NowRect(body.x + 20f, body.y + 12f, body.width - 40f, body.height - 26f),
                                  "The seven extensions");

            Now.Text(new NowRect(inner.x, inner.y + 4f, inner.width, 34f))
                .SetFontSize(12f)
                .SetColor(muted)
                .Draw("All seven are referenced by this build. Open the named area for each one; the status here " +
                      "is what that area's capture showed, not a prediction.");

            float y = inner.y + 42f;
            const float rowHeight = 72f;

            for (int i = 0; i < k_Extensions.Length; i += 4)
            {
                NowRect row = new NowRect(inner.x, y, inner.width, rowHeight - 6f);

                Now.Rectangle(row)
                    .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                    .SetRadius(8f)
                    .Draw();

                Now.Text(new NowRect(row.x + 14f, row.y + 7f, 200f, 20f))
                    .SetFontSize(14f)
                    .SetBold()
                    .SetColor(text)
                    .Draw(k_Extensions[i]);

                Now.Text(new NowRect(row.x + 220f, row.y + 9f, 200f, 18f))
                    .SetFontSize(11f)
                    .SetColor(muted)
                    .Draw("?area=" + k_Extensions[i + 1]);

                // Wrapped rather than drawn as one line. Now.Text does not wrap, so a description longer than the
                // panel is silently cut off by the canvas - which reads, in a capture, as a rendering fault rather
                // than as a sentence that did not fit.
                Caption(new NowRect(row.x + 14f, row.y + 26f, row.width - 28f, 30f), k_Extensions[i + 2]);

                string status = k_Extensions[i + 3];

                Now.Text(new NowRect(row.x + row.width - 250f, row.y + 7f, 236f, 20f))
                    .SetFontSize(12f)
                    .SetColor(status.StartsWith("works", StringComparison.Ordinal) ? success
                            : status.StartsWith("partial", StringComparison.Ordinal) ? warning
                            : danger)
                    .Draw(status);

                y += rowHeight;
            }
        }

        /// <summary>
        /// Name, area id, the one-line result, and the status word. Quadruples, because this array is data for one
        /// panel and never leaves this file.
        /// </summary>
        /// <remarks>
        /// Every entry is a statement about a capture that was taken, or - for Sdf - about source that was read.
        /// Nothing here is inferred from how hard an extension looks.
        /// </remarks>
        private static readonly string[] k_Extensions =
        {
            "Markdown", "markdown",
            "Every block kind parses, lays out and draws; links hover and click, and text selection drags. Remote " +
                "images do not load: NowRuntime.host.fetch and host.imageDecoder are both null, so the transport " +
                "does not exist. An injected texture draws through the same code path.",
            "partial: no remote images",

            "Markup", "markup",
            "Layout, headings, controls, state binding, visibility expressions and events all work, driven with " +
                "real DOM events: slider, switch, chip, on-click toggle, Clicked(save) and emit(reset). " +
                "NowMarkup.File(path) needs a filesystem and is not used.",
            "works",

            "Markdown.Markup", "markdown-markup",
            "A ```markup fence renders as live controls with the embed set and as a code block without it. The " +
                "controls hover and press but COMMIT NOTHING - no value, no event. The same markup outside a " +
                "document, same state and same call shape, commits normally.",
            "partial: renders, does not commit",

            "CodeEditor", "codeeditor",
            "C# and JSON highlight; the JSON validator finds the planted trailing comma. Driven: click-to-focus, " +
                "typing, arrows/Home/End, Backspace, Ctrl+A, Ctrl+Z, and the validator re-running on every edit.",
            "works",

            "Docking", "docking",
            "Four panels, a seeded split tree, tab bars and a closable tab. Driven: the splitter drags, the tab " +
                "closes, and the switch, slider and button inside a docked pane all take pointer input.",
            "works",

            "NodeGraph", "nodegraph",
            "Nodes, ports, labels, grid, the evaluator AND the bezier links all work with the unmodified default " +
                "renderer; node dragging works. Predicted to fail on the links - but 'NowUI/UI Bezier' was ported " +
                "by a parallel unit before this was captured.",
            "works",

            "Sdf", "sdf",
            "The shape system DRAWS, image nodes included. 'NowUI/SDF Scene' is ported, and ?area=sdf shows nine " +
                "primitives, all six boolean operators, a morph, and outline, glow, shadow, inner shadow, emboss " +
                "and contour bands. Glyph nodes draw too (?area=sdf&sdftext=1). All five passes of " +
                "'Hidden/NowUI/SDF Image Field' are ported as well, so Image and Sprite nodes bake and draw: " +
                "?area=sdf-image. That bake needs a float or half-float colour attachment, which this context " +
                "grants (measured: field RHalf, flood ARGBFloat). The render targets and blits this row used to " +
                "doubt were already available.",
             "works",
        };
    }
}
