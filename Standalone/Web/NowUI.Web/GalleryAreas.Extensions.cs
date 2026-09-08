// Gallery areas: the seven extension assemblies.
//
// Why this file exists. Milestone 1 established that all seven extensions COMPILE without Unity. Nothing had ever
// linked them into a browser payload, so "they compile" was the entire body of knowledge about them: no extension
// type had been constructed, no extension had drawn a vertex, and the feature matrix carried predictions rather than
// results. NowUI.Web.csproj now references all seven and this file exercises six of them with real content, which is
// what turns those predictions into measurements.
//
// One area per extension, for the reason the gallery exists at all: an unported shader throws by name inside
// WebGL2Backend.DrawMesh and latches the frame loop off, so an extension that fails takes down every other extension
// sharing its frame. Isolation is the measuring instrument.
//
// Two areas need their split explained, because in both cases the split IS the finding:
//
//   nodegraph / nodegraph-custom  These two exist because a prediction was wrong, and because the FIRST
//                                 explanation of why it was wrong was also wrong. Both corrections are recorded,
//                                 because the second is the one with the lesson in it.
//
//                                 Predicted: ?area=nodegraph fails at the first link, because
//                                 NowNodeGraphDefaultRenderer.DrawLink ends in Now.Bezier (NowNodeGraph.cs:3172)
//                                 and 'NowUI/UI Bezier' is not ported. Captured: every link renders, no frame
//                                 failure.
//
//                                 First explanation, from reading NowLine.cs: Now.Bezier takes the analytic
//                                 program only when `solidCubic && !hasTransform` (NowLine.cs:317-324), and the
//                                 canvas draws its links inside Now.Transform (NowNodeGraph.cs:3982), so the cubic
//                                 is flattened and stroked through the ported UI Rectangle program. Plausible, and
//                                 it is a true reading of that code - but it is not why this capture looks the way
//                                 it does.
//
//                                 What was actually true: 'NowUI/UI Bezier' HAD BEEN PORTED, by a parallel unit,
//                                 while this file was being written. WebGL2Backend.IsPortedShader and
//                                 nowui-gl.js's PROGRAM_SOURCES list it (both files stamped 18:41-18:45 on
//                                 2026-09-07, ahead of the 19:22 capture). The counter-example that exposed it is
//                                 ?area=bezier, which draws a solid cubic with NO transform - the case the first
//                                 explanation says must still fail - and which renders. One shader name, checked
//                                 against the backend that was on disk at capture time, would have settled it
//                                 before a mechanism was invented for it.
//
//                                 So: NodeGraph works, links included, and this file cannot say whether the
//                                 transform path or the newly-ported program carried them, because it never
//                                 captured the graph while the program was still absent.
//
//                                 nodegraph-custom stayed because it measures something the port does not touch:
//                                 that INowNodeGraphRenderer can be subclassed and one virtual replaced from
//                                 another assembly under WebAssembly.
//
//   sdf                           A report page that deliberately does NOT call the extension. Its programs
//                                 ('NowUI/SDF Scene', 'Hidden/NowUI/SDF Image Field') are unported and its image
//                                 field additionally needs render targets, so a draw would produce one exception and
//                                 no information. The page states what is missing, at the granularity of shader
//                                 program and backend method, which is the deliverable asked for.
//
// What is DELIBERATELY not stubbed here, and named instead as a missing host capability:
//
//   fetch-backed images           NO LONGER MISSING, and the note is kept rather than deleted because what it
//                                 described is the shape of the fix. It read: "NowMarkdownImages' standalone half
//                                 needs NowRuntime.host.fetch (INowFetchProvider) to start a download and
//                                 NowRuntime.host.imageDecoder (INowImageDecoder) to turn the bytes into a
//                                 texture. WebHostServices returns null for both." Both are now installed
//                                 (WebFetchProvider, WebImageDecoder), so the markdown area's remote row draws a
//                                 real download beside the locally-injected texture instead of photographing a
//                                 failure. The image it asks for is served by THIS app out of its own wwwroot: the
//                                 row proves the transport, not the internet. ?area=remote takes the same two
//                                 services apart probe by probe.
//
//   a filesystem                  NowMarkup.File(path) resolves against Directory.GetCurrentDirectory() and polls
//                                 timestamps. The markup area uses NowMarkup.Document(string) instead and says so;
//                                 hot-reload from disk is not a browser feature and is not faked here.
using System;
using System.Collections.Generic;
using NowUI;
using NowUI.CodeEditor;
using NowUI.Docking;
using NowUI.Markdown;
using NowUI.Markup;
using NowUI.NodeGraph;
using NowUI.Sdf;
using UnityEngine;

namespace NowUI.Web
{
    internal static partial class FeatureGallery
    {
        // =============================================================================================== Markdown

        /// <summary>
        /// A document using every block the parser has: headings, emphasis, lists, code fences, a table, links,
        /// blockquotes, rules - and two images, one remote and one injected locally.
        /// </summary>
        /// <remarks>
        /// The two images are the point of the second column. They differ in exactly one respect - where the texture
        /// comes from - so the capture attributes a failure to the transport rather than to the renderer.
        /// </remarks>
        private static void DrawMarkdown(NowRect body)
        {
            EnsureMarkdownImage();

            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect left = new NowRect(body.x + 20f, body.y + 14f, (body.width - 60f) * 0.62f, body.height - 30f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, body.width - 60f - left.width, left.height);

            NowRect inner = Panel(left, "NowMarkdown.Document(text).Draw(rect)");

            NowMarkdownResult result = NowMarkdown.Document(k_MarkdownBody).SetFontSize(14f).Draw(inner);

            s_MarkdownHeight = result.height;
            s_MarkdownHoveredLink = result.hoveredLink;

            if (!string.IsNullOrEmpty(result.clickedLink))
            {
                s_MarkdownClickedLink = result.clickedLink;
                ++s_MarkdownClickCount;
            }

            // ---- the right column: images, and what the document reported back.
            NowRect innerRight = Panel(right, "Images, and what the document reports");

            float y = innerRight.y + 6f;

            Caption(new NowRect(innerRight.x, y, innerRight.width, 30f),
                "Same markdown image syntax, two sources. The difference is the transport, not the renderer.");
            y += 34f;

            NowRect injected = new NowRect(innerRight.x, y, innerRight.width, 150f);
            NowMarkdown.Document(k_MarkdownInjectedImage).SetFontSize(13f).Draw(injected);
            y += 154f;

            // Taller than the injected block above it, because the remote image is 128x128 and the injected swatch
            // is 64x64 - and because a block too short for its image lets the caption below it draw over the
            // picture, which in a capture reads as a rendering fault rather than as a layout constant.
            NowRect remote = new NowRect(innerRight.x, y, innerRight.width, 200f);
            NowMarkdown.Document(MarkdownRemoteImage()).SetFontSize(13f).Draw(remote);
            y += 204f;

            NowMarkdownImageState remoteState = NowMarkdownImages.GetState(RemoteImageUrl(), out Texture2D _);

            Caption(new NowRect(innerRight.x, y, innerRight.width, 56f),
                "Remote state: " + remoteState + ". " + (fetchProvider == null
                    ? "NowRuntime.host.fetch is null, so NowMarkdownImages never starts a request."
                    : "NowMarkdownImages started a real request through NowRuntime.host.fetch (WebFetchProvider) " +
                      "and decoded the bytes through host.imageDecoder. The image is same-origin, out of this " +
                      "app's own wwwroot: what this row proves is the transport and the decoder, not the " +
                      "internet. See ?area=remote for each part of that contract separately."));
            y += 78f;

            Now.Rectangle(new NowRect(innerRight.x, y, innerRight.width, 1f))
                .SetColor(theme.GetColor(NowColorToken.Border))
                .Draw();
            y += 10f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f)
                .SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("laid-out height: " + s_MarkdownHeight.ToString("0.0"));
            y += 22f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f)
                .SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("hovered link: " + (s_MarkdownHoveredLink ?? "-"));
            y += 22f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f)
                .SetColor(theme.GetColor(NowColorToken.Accent))
                .Draw("clicked link: " + (s_MarkdownClickedLink ?? "-") + "  (" + s_MarkdownClickCount + ")");

            areaState =
                "height=" + s_MarkdownHeight.ToString("0.0") +
                ";hovered=" + (s_MarkdownHoveredLink ?? "-") +
                ";clicked=" + (s_MarkdownClickedLink ?? "-") +
                ";clicks=" + s_MarkdownClickCount +
                ";remote=" + remoteState +
                ";injected=" + (NowMarkdownImages.GetState(k_InjectedImageUrl, out Texture2D _) ==
                                NowMarkdownImageState.Loaded ? "Loaded" : "NotLoaded");
        }

        private static float s_MarkdownHeight;
        private static string s_MarkdownHoveredLink;
        private static string s_MarkdownClickedLink;
        private static int s_MarkdownClickCount;

        /// <summary>
        /// The URL the remote image is asked for: a PNG served by THIS app, out of its own wwwroot.
        /// </summary>
        /// <remarks>
        /// It used to be a raw.githubusercontent.com URL, and it was pointing at nothing that could work - the host
        /// had no fetch provider, and the whole purpose of the row was to photograph the failure. Now that both
        /// host services exist the row proves the opposite thing, and for that a SAME-ORIGIN image is the right
        /// subject: a test that reaches the public internet measures the internet's availability, its CORS headers
        /// and its redirect chain along with NowUI, and cannot tell you which of the four failed. The cross-origin
        /// question is a separate one and is asked separately, by the `remote` area under <c>?xorigin=1</c>.
        /// <para>Absolute, because <c>NowMarkdownImages.TryValidateRemoteUrl</c> accepts only absolute http/https
        /// URLs - so it is built from the document base rather than written down.</para>
        /// </remarks>
        private static string RemoteImageUrl()
        {
            if (s_RemoteImageUrl != null)
                return s_RemoteImageUrl;

            try
            {
                s_RemoteImageUrl = new Uri(new Uri(baseUri), "nowui-test-image.png").AbsoluteUri;
            }
            catch (Exception)
            {
                s_RemoteImageUrl = "nowui-test-image.png";
            }

            return s_RemoteImageUrl;
        }

        private static string s_RemoteImageUrl;

        /// <summary>The URL the locally-injected texture is registered under, so both go through the same code path.</summary>
        private const string k_InjectedImageUrl = "gallery://swatch";

        private static bool s_MarkdownImageInjected;

        /// <summary>
        /// Registers one procedurally-built texture with the markdown image cache, once.
        /// </summary>
        /// <remarks>
        /// <c>NowMarkdownImages.SetTexture</c> is the extension's own documented injection point ("tests, local
        /// art"). Using it rather than a fetch shim is what keeps the two image results comparable: the drawn image
        /// and the failed one differ only in where the texture came from, so a working swatch beside a missing
        /// remote is evidence about the transport specifically.
        /// </remarks>
        private static void EnsureMarkdownImage()
        {
            if (s_MarkdownImageInjected)
                return;

            s_MarkdownImageInjected = true;

            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    // A diagonal ramp with a ring, so a flipped or channel-swapped upload is visible rather than
                    // merely plausible - a flat swatch would pass every wrong pipeline as easily as the right one.
                    float dx = (x - size * 0.5f) / (size * 0.5f);
                    float dy = (y - size * 0.5f) / (size * 0.5f);
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    bool ring = radius > 0.62f && radius < 0.82f;

                    pixels[y * size + x] = ring
                        ? new Color32(250, 250, 250, 255)
                        : new Color32((byte)(40 + 200 * x / size), (byte)(60 + 160 * y / size), 210, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            NowMarkdownImages.SetTexture(k_InjectedImageUrl, texture);
        }

        private const string k_MarkdownInjectedImage =
            "**Injected texture** (`NowMarkdownImages.SetTexture`)\n\n" +
            "![swatch](" + k_InjectedImageUrl + ")\n";

        /// <summary>Built once: the URL is only known at run time, and this is redrawn every frame.</summary>
        private static string MarkdownRemoteImage()
        {
            return s_MarkdownRemoteImage ??= "**Remote image** (`INowFetchProvider` + `INowImageDecoder`)\n\n" +
                                             "![test](" + RemoteImageUrl() + ")\n";
        }

        private static string s_MarkdownRemoteImage;

        /// <summary>
        /// The document. Written to reach every block kind the parser has rather than to read well, because a block
        /// that is not in the source is a block this capture says nothing about.
        /// </summary>
        private const string k_MarkdownBody =
            "# Markdown in the browser\n" +
            "\n" +
            "This document is parsed and laid out by `NowUI.Extensions.Markdown` running under WebAssembly, and\n" +
            "drawn through the same two shader programs as everything else: **UI Rectangle** and *Text Renderer*.\n" +
            "\n" +
            "## Emphasis and inline spans\n" +
            "\n" +
            "**Bold**, *italic*, ***both***, ~~struck through~~, `inline code`, and a\n" +
            "[link to the repository](https://github.com/BlenMiner/NowUI) that reports back through\n" +
            "`NowMarkdownResult.clickedLink`.\n" +
            "\n" +
            "## Lists\n" +
            "\n" +
            "- unordered, first\n" +
            "- unordered, second\n" +
            "  - nested one level\n" +
            "  - and a sibling\n" +
            "- unordered, third\n" +
            "\n" +
            "1. ordered, first\n" +
            "2. ordered, second\n" +
            "3. ordered, third\n" +
            "\n" +
            "- [x] a checked task\n" +
            "- [ ] an unchecked task\n" +
            "\n" +
            "## Code\n" +
            "\n" +
            "```csharp\n" +
            "using (Now.StartUI(dpr))\n" +
            "{\n" +
            "    Now.Rectangle(panel).SetRadius(12f).Draw();\n" +
            "    Now.Text(label).SetFontSize(28f).Draw(\"Score: 1200\");\n" +
            "}\n" +
            "```\n" +
            "\n" +
            "## Table\n" +
            "\n" +
            "| Program | Ported | Used by |\n" +
            "|---|---|---|\n" +
            "| UI Rectangle | yes | every panel |\n" +
            "| Text Renderer | yes | every glyph |\n" +
            "| UI Gradient | yes | gradient fills |\n" +
            "| UI Bezier | no | node graph links |\n" +
            "\n" +
            "> A blockquote, to check the quote rule and its indent.\n" +
            ">\n" +
            "> Second paragraph inside the quote.\n" +
            "\n" +
            "---\n" +
            "\n" +
            "Trailing paragraph after a horizontal rule, so the rule has something below it to sit against.\n";

        // ================================================================================================= Markup

        /// <summary>
        /// A markup document exercising layout, headings, controls, state binding, visibility expressions and
        /// events, beside its own source and the state it is driving.
        /// </summary>
        /// <remarks>
        /// The state store and the event readout are what make this an interaction test rather than a rendering
        /// one. A markup document that draws but whose slider never moves would look identical in a still capture;
        /// the values printed on the right are what tell the two apart.
        /// </remarks>
        private static void DrawMarkup(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect left = new NowRect(body.x + 20f, body.y + 14f, (body.width - 60f) * 0.52f, body.height - 30f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, body.width - 60f - left.width, left.height);

            NowRect inner = Panel(left, "NowMarkup.Document(source).Draw(rect, state)");

            NowMarkupResult result = NowMarkup.Document(k_MarkupSource).Draw(inner, s_MarkupState);

            if (result.Clicked("save"))
            {
                ++s_MarkupSaveCount;
                s_MarkupLastEvent = "click save";
            }

            if (result.Action("reset"))
            {
                s_MarkupState.SetFloat("volume", 0.5f);
                s_MarkupState.SetString("name", "");
                s_MarkupLastEvent = "action reset";
            }

            if (result.Changed("volume"))
                s_MarkupLastEvent = "change volume";

            if (result.Changed("night"))
                s_MarkupLastEvent = "change night";

            // ---- the state the document is bound to, read straight back out of the store.
            NowRect innerRight = Panel(right, "State, events, and the source");

            float y = innerRight.y + 6f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f).SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("volume = " + s_MarkupState.GetFloat("volume", 0.5f).ToString("0.00"));
            y += 20f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f).SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("night = " + s_MarkupState.GetBool("night"));
            y += 20f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f).SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("details = " + s_MarkupState.GetBool("details"));
            y += 20f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f).SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("name = \"" + s_MarkupState.GetString("name") + "\"");
            y += 20f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f).SetColor(theme.GetColor(NowColorToken.Accent))
                .Draw("saves = " + s_MarkupSaveCount + "   last = " + (s_MarkupLastEvent ?? "-"));
            y += 24f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f).SetColor(theme.GetColor(NowColorToken.TextMuted))
                .Draw("events this frame: " + result.events.Count);
            y += 26f;

            Caption(new NowRect(innerRight.x, y, innerRight.width, 48f),
                "NowMarkup.File(path) is the other entry point and is NOT used: it resolves against " +
                "Directory.GetCurrentDirectory() and polls timestamps, which needs a filesystem this host does " +
                "not have.");
            y += 56f;

            // The source, in the extension's own code editor - which is also a second, independent draw of the
            // CodeEditor extension, on markup rather than on C#.
            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 18f))
                .SetFontSize(11f).SetBold().SetColor(theme.GetColor(NowColorToken.TextMuted))
                .Draw("SOURCE");
            y += 20f;

            NowRect source = new NowRect(innerRight.x, y, innerRight.width, innerRight.y + innerRight.height - y);

            if (source.height > 60f)
            {
                NowCode.Editor(source, NowMarkupCodeLanguage.instance)
                    .SetFontSize(11f)
                    .SetStatusBar(false)
                    .Draw(ref s_MarkupSourceBuffer);
            }

            areaState =
                "volume=" + s_MarkupState.GetFloat("volume", 0f).ToString("0.00") +
                ";night=" + s_MarkupState.GetBool("night") +
                ";details=" + s_MarkupState.GetBool("details") +
                ";pinned=" + s_MarkupState.GetBool("pinned") +
                ";name=" + s_MarkupState.GetString("name") +
                ";saves=" + s_MarkupSaveCount +
                ";last=" + (s_MarkupLastEvent ?? "-") +
                ";events=" + result.events.Count;
        }

        /// <summary>
        /// The state store, seeded once.
        /// </summary>
        /// <remarks>
        /// Seeded rather than left empty, because an unseeded store makes a still capture lie in two ways: a slider
        /// bound to an absent key sits at its minimum, which looks like a slider that does not read its binding,
        /// and <c>visible="details"</c> hides the textfield and the two buttons, which looks like markup that
        /// cannot draw them. Both are correct behaviour for an empty store and neither is what the capture is
        /// supposed to be evidence about. The toggle still works from the page.
        /// </remarks>
        private static readonly NowMarkupState s_MarkupState = SeedMarkupState();

        private static int s_MarkupSaveCount;
        private static string s_MarkupLastEvent;
        private static string s_MarkupSourceBuffer = k_MarkupSource;

        private static NowMarkupState SeedMarkupState()
        {
            NowMarkupState state = new NowMarkupState();
            state.SetFloat("volume", 0.65f);
            state.SetBool("night", true);
            state.SetBool("details", true);
            state.SetString("name", "browser");
            return state;
        }

        /// <summary>
        /// The markup document. Covers the four groups the language is organised into: layout containers, text and
        /// headings, controls bound to state, and structure with a visibility expression and an action.
        /// </summary>
        private const string k_MarkupSource =
            "<style>\n" +
            "  .card { padding: 14; gap: 8; rect-style: Surface; radius: 8; }\n" +
            "</style>\n" +
            "\n" +
            "<column class=\"card\" gap=\"10\">\n" +
            "  <h2>Display settings</h2>\n" +
            "  <text>Bound to a caller-owned <b>NowMarkupState</b>.</text>\n" +
            "  <hr />\n" +
            "\n" +
            "  <row gap=\"8\" align-items=\"center\">\n" +
            "    <text style=\"width: 90\">Volume</text>\n" +
            "    <slider id=\"volume\" state=\"volume\" min=\"0\" max=\"1\" step=\"0.05\" style=\"stretch: 1\" />\n" +
            "  </row>\n" +
            "\n" +
            "  <row gap=\"8\" align-items=\"center\">\n" +
            "    <switch id=\"night\" state=\"night\">Night mode</switch>\n" +
            "    <badge>beta</badge>\n" +
            "    <chip id=\"pinned\" state=\"pinned\">pinned</chip>\n" +
            "  </row>\n" +
            "\n" +
            "  <progress state=\"volume\" max=\"1\" />\n" +
            "\n" +
            "  <button id=\"toggle-details\" variant=\"Outline\" on-click=\"toggle(details)\">\n" +
            "    Toggle details\n" +
            "  </button>\n" +
            "\n" +
            "  <column visible=\"details\" gap=\"6\">\n" +
            "    <textfield id=\"display-name\" state=\"name\" placeholder=\"Display name\" />\n" +
            "    <row gap=\"6\">\n" +
            "      <button id=\"save\">Save</button>\n" +
            "      <button id=\"reset\" on-click=\"emit(reset)\">Reset</button>\n" +
            "    </row>\n" +
            "  </column>\n" +
            "</column>\n";

        // ======================================================================================= Markdown.Markup

        /// <summary>
        /// The bridge assembly: a <c>```markup</c> fence inside a markdown document rendered as live controls
        /// through <see cref="NowMarkupEmbeds"/>, beside the same document drawn WITHOUT the embed set.
        /// </summary>
        /// <remarks>
        /// Both halves draw the identical source string. That is the whole test: with the embed set the fence is a
        /// working slider and button, without it the fence is a highlighted code block. Drawing only the embedded
        /// half would leave "did the bridge do anything" unanswerable from the capture, because a markup block that
        /// silently fell back to code would look like a design choice.
        /// </remarks>
        private static void DrawMarkdownMarkup(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect left = new NowRect(body.x + 20f, body.y + 14f, (body.width - 60f) * 0.5f, body.height - 30f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, body.width - 60f - left.width, left.height);

            NowRect inner = Panel(left, "With SetEmbeds(NowMarkupEmbeds) - live controls");

            NowMarkdown.Document(k_EmbedDocument).SetFontSize(14f).SetEmbeds(s_Embeds).Draw(inner);

            if (s_Embeds.Clicked("apply"))
                ++s_EmbedApplyCount;

            NowRect innerRight = Panel(right, "Same document, no embed set - a code block");

            NowMarkdown.Document(k_EmbedDocumentPlain).SetFontSize(14f).Draw(innerRight);

            // The readout sits over the bottom of the right panel rather than in a third column, because the two
            // documents have to be the same width for the comparison to be about the embed and not about wrapping.
            NowRect readout = new NowRect(innerRight.x, innerRight.y + innerRight.height - 54f, innerRight.width, 50f);

            Now.Rectangle(readout.Outset(6f))
                .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                .SetRadius(6f)
                .Draw();

            Now.Text(new NowRect(readout.x, readout.y, readout.width, 20f))
                .SetFontSize(12f).SetColor(theme.GetColor(NowColorToken.Accent))
                .Draw("embed threshold = " + s_Embeds.state.GetFloat("threshold", 0.35f).ToString("0.00") +
                      "   applies = " + s_EmbedApplyCount);

            Now.Text(new NowRect(readout.x, readout.y + 22f, readout.width, 20f))
                .SetFontSize(11f).SetColor(theme.GetColor(NowColorToken.TextMuted))
                .Draw("Read from NowMarkupEmbeds.state, which every embedded block on this page shares.");

            // ---- the control.
            //
            // Driving the embedded controls found that they DRAW, HOVER and show a pressed state, and then commit
            // nothing: no value reaches the shared state and no event reaches Clicked/Changed. Two things could
            // produce that, and they need separating rather than guessing between:
            //
            //   (a) the markdown host - the fence, the document's own layout, its selection layer;
            //   (b) the call shape the bridge uses - NowMarkupEmbeds.Render opens a NowLayout.Area and draws with
            //       the layout-FLOWING overload, `Draw(state)`, where every other markup draw on this page uses the
            //       explicit-rect overload `Draw(rect, state)`.
            //
            // This block is (b) with markdown removed: the same markup source, the same shared state, the same
            // NowLayout.Area + Draw(state) shape, and no document anywhere near it. If its controls commit and the
            // embedded ones do not, the markdown host is responsible. If it fails the same way, the layout-flowing
            // draw is.
            NowRect control = new NowRect(left.x, left.y + left.height - 132f, left.width, 124f);

            Now.Rectangle(control)
                .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                .SetRadius(8f)
                .SetOutline(1f, theme.GetColor(NowColorToken.Border))
                .Draw();

            Now.Text(new NowRect(control.x + 12f, control.y + 8f, control.width - 24f, 18f))
                .SetFontSize(11f).SetBold().SetColor(theme.GetColor(NowColorToken.TextMuted))
                .Draw("CONTROL - same source and state, same NowLayout.Area + Draw(state), no markdown");

            using (NowLayout.Area(new NowRect(control.x + 12f, control.y + 28f, control.width - 24f, 88f)))
                NowMarkup.Document(k_EmbedMarkup).Draw(s_Embeds.state);

            areaState =
                "threshold=" + s_Embeds.state.GetFloat("threshold", 0f).ToString("0.00") +
                ";preview=" + s_Embeds.state.GetBool("preview") +
                ";applies=" + s_EmbedApplyCount +
                ";embedEvents=" + s_Embeds.events.Count;
        }

        private static readonly NowMarkupEmbeds s_Embeds = SeedEmbeds();
        private static int s_EmbedApplyCount;

        /// <summary>Seeded for the reason <see cref="SeedMarkupState"/> is: an unbound slider sits at its minimum.</summary>
        private static NowMarkupEmbeds SeedEmbeds()
        {
            NowMarkupEmbeds embeds = new NowMarkupEmbeds();
            embeds.state.SetFloat("threshold", 0.35f);
            embeds.state.SetBool("preview", true);
            return embeds;
        }

        /// <summary>The markup the fence carries, on its own, for the control block above.</summary>
        private const string k_EmbedMarkup =
            "<column gap=\"8\">\n" +
            "  <row gap=\"8\" align-items=\"center\">\n" +
            "    <text style=\"width: 80\">Threshold</text>\n" +
            "    <slider id=\"threshold\" state=\"threshold\" min=\"0\" max=\"1\" step=\"0.01\" style=\"stretch: 1\" />\n" +
            "  </row>\n" +
            "  <row gap=\"8\">\n" +
            "    <checkbox id=\"preview\" state=\"preview\">Preview</checkbox>\n" +
            "    <button id=\"apply\">Apply</button>\n" +
            "  </row>\n" +
            "</column>\n";

        private const string k_EmbedDocument =
            "### Live markup inside markdown\n" +
            "\n" +
            "The fence below is tagged `markup`. With a `NowMarkupEmbeds` set it renders as controls; without one\n" +
            "it stays a highlighted code block, which is how documents degrade where embeds are not wired up.\n" +
            "\n" +
            "```markup\n" +
            "<column gap=\"8\">\n" +
            "  <row gap=\"8\" align-items=\"center\">\n" +
            "    <text style=\"width: 80\">Threshold</text>\n" +
            "    <slider id=\"threshold\" state=\"threshold\" min=\"0\" max=\"1\" step=\"0.01\" style=\"stretch: 1\" />\n" +
            "  </row>\n" +
            "  <row gap=\"8\">\n" +
            "    <checkbox id=\"preview\" state=\"preview\">Preview</checkbox>\n" +
            "    <button id=\"apply\">Apply</button>\n" +
            "  </row>\n" +
            "</column>\n" +
            "```\n" +
            "\n" +
            "Ordinary markdown continues below the fence, so the embed has to give its height back correctly.\n";

        /// <summary>
        /// The same text again, with one trailing newline.
        /// </summary>
        /// <remarks>
        /// EXPERIMENT, not decoration. NowMarkdown.GetCached keys on (text, fontSize), so drawing the identical
        /// string twice in one frame hands both draws the SAME retained NowMarkdownDocument - and with it one
        /// cached layout and one set of embed slots, at two different widths. The trailing newline makes the two
        /// draws separate documents. The strings are otherwise identical, so the side-by-side comparison the area
        /// exists for is unaffected.
        /// </remarks>
        private const string k_EmbedDocumentPlain = k_EmbedDocument + "\n";

        // ============================================================================================= CodeEditor

        /// <summary>Two editors on the same page: C# and JSON, each with its own language profile and validator.</summary>
        /// <remarks>
        /// The JSON buffer carries a deliberate syntax error. The editor's validator is the half of this extension
        /// that has nothing to do with drawing - it is a parser that reports diagnostics - so a capture that shows
        /// the squiggle and the status bar is evidence that the language layer ran, not merely that text drew.
        /// </remarks>
        private static void DrawCodeEditor(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect left = new NowRect(body.x + 20f, body.y + 14f, (body.width - 60f) * 0.55f, body.height - 30f);
            NowRect right = new NowRect(left.x + left.width + 20f, left.y, body.width - 60f - left.width, left.height);

            NowRect inner = Panel(left, "C# - NowCSharpLanguage.instance");

            NowCodeEditorResult csharp = NowCode.Editor(inner, NowCSharpLanguage.instance)
                .SetFontSize(13f)
                .Draw(ref s_CSharpBuffer);

            NowRect innerRight = Panel(right, "JSON - NowJsonLanguage.instance (one deliberate error)");

            float editorHeight = innerRight.height - 96f;

            NowCodeEditorResult json = NowCode.Editor(
                    new NowRect(innerRight.x, innerRight.y, innerRight.width, editorHeight),
                    NowJsonLanguage.instance)
                .SetFontSize(13f)
                .Draw(ref s_JsonBuffer);

            float y = innerRight.y + editorHeight + 10f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f)
                .SetColor(json.isValid ? theme.GetColor(NowColorToken.Success) : theme.GetColor(NowColorToken.Danger))
                .Draw("JSON: isValid=" + json.isValid + "  diagnostics=" + json.diagnosticCount +
                      "  changed=" + json.changed);
            y += 22f;

            Now.Text(new NowRect(innerRight.x, y, innerRight.width, 20f))
                .SetFontSize(12f)
                .SetColor(csharp.isValid ? theme.GetColor(NowColorToken.Success) : theme.GetColor(NowColorToken.Danger))
                .Draw("C#: isValid=" + csharp.isValid + "  diagnostics=" + csharp.diagnosticCount +
                      "  changed=" + csharp.changed);
            y += 26f;

            Caption(new NowRect(innerRight.x, y, innerRight.width, 44f),
                "Typing, caret, selection, undo and Tab indent all need the host text-input source and the " +
                "clipboard bridge, both of which WebInput installs. Copy/paste goes through INowClipboard, which " +
                "is the browser's async clipboard API.");

            areaState =
                "jsonValid=" + json.isValid +
                ";jsonDiagnostics=" + json.diagnosticCount +
                ";jsonChanged=" + json.changed +
                ";jsonChars=" + s_JsonBuffer.Length +
                ";csharpValid=" + csharp.isValid +
                ";csharpDiagnostics=" + csharp.diagnosticCount +
                ";csharpChars=" + s_CSharpBuffer.Length;
        }

        private static string s_CSharpBuffer =
            "using NowUI;\n" +
            "using UnityEngine;\n" +
            "\n" +
            "// The quick-start panel, highlighted by NowCSharpLanguage.\n" +
            "public sealed class Hud\n" +
            "{\n" +
            "    const float Radius = 12f;\n" +
            "\n" +
            "    int _score = 1200;\n" +
            "\n" +
            "    public void Draw(NowRect panel)\n" +
            "    {\n" +
            "        Now.Rectangle(panel)\n" +
            "            .SetColor(new Color(0f, 0f, 0f, 0.8f))\n" +
            "            .SetRadius(Radius)\n" +
            "            .Draw();\n" +
            "\n" +
            "        Now.Text(panel.Inset(20f))\n" +
            "            .SetFontSize(28f)\n" +
            "            .Draw($\"Score: {_score}\");\n" +
            "    }\n" +
            "}\n";

        /// <summary>JSON with a trailing comma on the third line from the end - a real error for the validator to find.</summary>
        private static string s_JsonBuffer =
            "{\n" +
            "  \"schema\": \"nowui.standalone.shaders/1\",\n" +
            "  \"ported\": [\n" +
            "    \"NowUI/UI Rectangle\",\n" +
            "    \"NowUI/Text Renderer\",\n" +
            "    \"NowUI/UI Gradient\",\n" +
            "    \"NowUI/UI Ripple\"\n" +
            "  ],\n" +
            "  \"unported\": {\n" +
            "    \"bezier\": \"NowUI/UI Bezier\",\n" +
            "    \"glass\": [\"NowUI/UI Glass\", \"Hidden/NowUI/GlassBlur\"],\n" +
            "    \"sdf\": \"NowUI/SDF Scene\",\n" +
            "  },\n" +
            "  \"colorSpace\": \"Gamma\"\n" +
            "}\n";

        // ================================================================================================ Docking

        /// <summary>A dock space with four panels: a split layout, tab bars, splitters and a closable window.</summary>
        /// <remarks>
        /// The layout is seeded once through <c>Dock</c> so a headless capture lands on a split tree rather than on
        /// four tabs of one pane, which is what an unseeded dock space produces on frame one and which would show
        /// nothing about splitters.
        /// </remarks>
        private static void DrawDocking(NowRect body)
        {
            s_Dock.Window("Scene", DrawDockScene, id: "Scene");
            s_Dock.Window("Hierarchy", DrawDockHierarchy, id: "Hierarchy");
            s_Dock.Window("Inspector", DrawDockInspector, id: "Inspector");
            s_Dock.Window("Console", DrawDockConsole, id: "Console", canClose: true);

            if (!s_DockSeeded)
            {
                s_DockSeeded = true;
                s_Dock.Dock("Hierarchy", "Scene", NowDockSide.Left, 0.24f);
                s_Dock.Dock("Inspector", "Scene", NowDockSide.Right, 0.3f);
                s_Dock.Dock("Console", "Scene", NowDockSide.Bottom, 0.32f);
            }

            NowDock.Space(s_Dock, new NowRect(body.x + 16f, body.y + 12f, body.width - 32f, body.height - 26f))
                .SetMinPaneSize(120f)
                .SetPaneRadius(6f)
                .SetPaneOutline(true)
                .Draw();

            areaState =
                "grid=" + s_DockGrid +
                ";exposure=" + s_DockExposure.ToString("0.00") +
                ";consoleOpen=" + s_Dock.IsWindowOpen("Console") +
                ";sceneOpen=" + s_Dock.IsWindowOpen("Scene");
        }

        private static readonly NowDockSpace s_Dock = new NowDockSpace();
        private static bool s_DockSeeded;
        private static bool s_DockGrid = true;
        private static float s_DockExposure = 0.6f;
        private static int s_DockSelected = 1;

        private static void DrawDockScene(NowRect rect)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            // Something with actual geometry, so a pane that is laid out but clipped to nothing is distinguishable
            // from one that simply has no content.
            Now.Rectangle(rect.Inset(8f))
                .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                .SetRadius(6f)
                .Draw();

            NowRect stage = rect.Inset(8f);

            for (int i = 0; i < 5; ++i)
            {
                float t = i / 4f;

                Now.Rectangle(new NowRect(stage.x + 18f + t * (stage.width - 90f),
                                          stage.y + 22f + t * (stage.height - 90f), 56f, 44f))
                    .SetColor(Color.Lerp(theme.GetColor(NowColorToken.Accent),
                                         theme.GetColor(NowColorToken.Success), t))
                    .SetRadius(6f)
                    .Draw();
            }

            Now.Text(new NowRect(stage.x + 10f, stage.y + 6f, stage.width - 20f, 18f))
                .SetFontSize(12f)
                .SetColor(theme.GetColor(NowColorToken.TextMuted))
                .Draw("Scene pane - ordinary NowUI drawing inside a docked window");
        }

        private static void DrawDockHierarchy(NowRect rect)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            string[] rows = { "Root", "  Camera", "  Lighting", "  UI", "    HUD", "    Menu", "  Props" };

            for (int i = 0; i < rows.Length; ++i)
            {
                NowRect row = new NowRect(rect.x + 6f, rect.y + 8f + i * 22f, rect.width - 12f, 20f);

                if (i == s_DockSelected)
                {
                    Now.Rectangle(row)
                        .SetColor(theme.GetColor(NowColorToken.AccentMuted))
                        .SetRadius(4f)
                        .Draw();
                }

                Now.Text(new NowRect(row.x + 6f, row.y + 2f, row.width - 12f, 18f))
                    .SetFontSize(12f)
                    .SetColor(theme.GetColor(NowColorToken.Text))
                    .Draw(rows[i]);
            }
        }

        /// <summary>
        /// Live controls inside a docked pane, laid out with explicit rects.
        /// </summary>
        /// <remarks>
        /// Explicit rects rather than a NowLayout group, for the reason DemoScene records: a main-axis stretch
        /// inside a plain layout group resolves from the PREVIOUS frame and reports width 0 on frame one, which a
        /// one-shot capture would photograph as missing controls. The dock pane's rect is already known here, so
        /// the arithmetic is cheaper than a measure pass and cannot be wrong on frame one.
        /// </remarks>
        private static void DrawDockInspector(NowRect rect)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect inner = rect.Inset(10f);
            float y = inner.y + 4f;

            Now.Text(new NowRect(inner.x, y, inner.width, 20f))
                .SetFontSize(15f).SetBold().SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("Inspector");
            y += 26f;

            Now.Switch(new NowRect(inner.x, y, Mathf.Min(inner.width, 150f), 22f), "Show grid")
                .Draw(ref s_DockGrid);
            y += 30f;

            Now.Text(new NowRect(inner.x, y, inner.width, 16f))
                .SetFontSize(12f).SetColor(theme.GetColor(NowColorToken.TextMuted))
                .Draw("Exposure " + s_DockExposure.ToString("0.00"));
            y += 20f;

            Now.Slider(new NowRect(inner.x, y, inner.width, 22f), 0f, 2f).Draw(ref s_DockExposure);
            y += 30f;

            if (Now.Button(new NowRect(inner.x, y, Mathf.Min(inner.width, 96f), 28f), "Reset").Draw())
            {
                s_DockExposure = 0.6f;
                s_DockGrid = true;
            }
        }

        private static void DrawDockConsole(NowRect rect)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            string[] lines =
            {
                "[NowUI] runtime up: fixtures fetched, canvas sized.",
                "[NowUI] area 'docking' - Docking extension.",
                "[NowUI] dock space: 4 windows, 3 splits, 1 closable tab.",
                "Drag a tab onto another pane to see the dock guides.",
            };

            for (int i = 0; i < lines.Length; ++i)
            {
                Now.Text(new NowRect(rect.x + 8f, rect.y + 8f + i * 18f, rect.width - 16f, 16f))
                    .SetFontSize(11f)
                    .SetColor(i == lines.Length - 1
                        ? theme.GetColor(NowColorToken.TextMuted)
                        : theme.GetColor(NowColorToken.Text))
                    .Draw(lines[i]);
            }
        }

        // ============================================================================================== NodeGraph

        /// <summary>The graph through the extension's own default renderer, links included, plus the evaluator.</summary>
        /// <remarks>
        /// Predicted to FAIL on the links; it does not. See the note at the head of this file for both the wrong
        /// prediction and the wrong first explanation of it. The short version: <c>NowUI/UI Bezier</c> was ported
        /// by a parallel unit before this was ever captured.
        /// </remarks>
        private static void DrawNodeGraph(NowRect body)
        {
            DrawGraphArea(body, null,
                "NowNodeGraphDefaultRenderer, unmodified - links included. Predicted to fail on the first link, " +
                "because DrawLink ends in Now.Bezier and 'NowUI/UI Bezier' was unported. It is ported now " +
                "(WebGL2Backend.IsPortedShader), which is why this renders. A second path could also carry it - " +
                "Now.Bezier flattens to the rectangle program under a transform, and this canvas draws inside one " +
                "- but which of the two ran was not measured and is not claimed.");
        }

        /// <summary>The same graph with one renderer method replaced from outside the assembly.</summary>
        /// <remarks>
        /// Not a workaround for anything, since <see cref="DrawNodeGraph"/> works. What it measures is that
        /// <c>INowNodeGraphRenderer</c> can be subclassed and one virtual replaced from another assembly under
        /// WebAssembly - which is independent of which shader programs happen to be ported.
        /// </remarks>
        private static void DrawNodeGraphCustom(NowRect body)
        {
            DrawGraphArea(body, s_PolylineRenderer,
                "A subclass of NowNodeGraphDefaultRenderer with DrawLink replaced by an explicit Now.DrawPolyline " +
                "over the same cubic. Everything else on this canvas is the extension's own default renderer, so " +
                "what this measures is that the renderer interface is overridable and dispatches here - a fact " +
                "about the assembly boundary under WebAssembly, not about the shader port.");
        }

        /// <summary>The shared body of both node-graph areas: the canvas, then the evaluator's readout.</summary>
        private static void DrawGraphArea(NowRect body, INowNodeGraphRenderer renderer, string note)
        {
            EnsureGraph();

            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect canvas = new NowRect(body.x + 16f, body.y + 12f, body.width - 32f, body.height - 110f);

            NowNodeGraphCanvas graph = NowNodes.Canvas(s_Graph, canvas).SetSchema(s_GraphSchema);

            if (renderer != null)
                graph = graph.SetRenderer(renderer);

            graph.Draw();

            // ---- evaluation, which is pure C# and shares nothing with the renderer.
            float value = s_Evaluator.Evaluate(s_Graph, "output", 0f);

            NowRect readout = new NowRect(body.x + 16f, canvas.y + canvas.height + 10f, body.width - 32f, 80f);

            Now.Rectangle(readout)
                .SetColor(theme.GetColor(NowColorToken.Surface))
                .SetRadius(8f)
                .SetOutline(1f, theme.GetColor(NowColorToken.Border))
                .Draw();

            Now.Text(new NowRect(readout.x + 14f, readout.y + 8f, readout.width - 28f, 20f))
                .SetFontSize(14f).SetBold().SetColor(theme.GetColor(NowColorToken.Accent))
                .Draw("NowNodeGraphEvaluator<float>.Evaluate(graph, \"output\") = " + value.ToString("0.###") +
                      "   -   constant(" + s_GraphConstant.ToString("0.##") + ") x" +
                      s_GraphFactor.ToString("0.##") + " +" + s_GraphOffset.ToString("0.##"));

            Caption(new NowRect(readout.x + 14f, readout.y + 32f, readout.width - 28f, 44f), note);

            NowNode constant = s_Graph.FindNode("constant");

            areaState =
                "evaluated=" + value.ToString("0.###") +
                ";nodes=" + s_Graph.nodes.Count +
                ";links=" + s_Graph.links.Count +
                ";constantPos=" + (constant == null
                    ? "-"
                    : constant.position.x.ToString("0") + "," + constant.position.y.ToString("0"));
        }

        private static readonly NowNodeGraph s_Graph = new NowNodeGraph();
        private static readonly NowNodeGraphSchema s_GraphSchema = new NowNodeGraphSchema();
        private static readonly NowNodeGraphEvaluator<float> s_Evaluator = new NowNodeGraphEvaluator<float>();
        private static readonly PolylineLinkRenderer s_PolylineRenderer = new PolylineLinkRenderer();
        private static bool s_GraphBuilt;

        private const float s_GraphConstant = 6f;
        private const float s_GraphFactor = 1.5f;
        private const float s_GraphOffset = 2f;

        // Node kinds and port ids. Integers by the extension's own convention (NodeGraph.md); the names exist so
        // the wiring below reads as arithmetic rather than as a table of magic numbers.
        private const int k_KindConstant = 1;
        private const int k_KindMultiply = 2;
        private const int k_KindAdd = 3;
        private const int k_KindOutput = 4;

        private const int k_TypeFloat = 1;

        private const int k_PortValue = 10;
        private const int k_PortA = 11;
        private const int k_PortB = 12;
        private const int k_PortResult = 13;
        private const int k_PortIn = 14;

        /// <summary>Builds the schema, the four nodes, the three links and the evaluator, once.</summary>
        private static void EnsureGraph()
        {
            if (s_GraphBuilt)
                return;

            s_GraphBuilt = true;

            s_GraphSchema.Node(k_KindConstant, "Constant")
                .SetSize(150f, 74f)
                .Output(k_PortValue, "Value", k_TypeFloat);

            s_GraphSchema.Node(k_KindMultiply, "Multiply")
                .SetSize(160f, 96f)
                .Input(k_PortA, "A", k_TypeFloat)
                .Input(k_PortB, "B", k_TypeFloat)
                .Output(k_PortResult, "Result", k_TypeFloat);

            s_GraphSchema.Node(k_KindAdd, "Add")
                .SetSize(160f, 96f)
                .Input(k_PortA, "A", k_TypeFloat)
                .Input(k_PortB, "B", k_TypeFloat)
                .Output(k_PortResult, "Result", k_TypeFloat);

            s_GraphSchema.Node(k_KindOutput, "Output")
                .SetSize(150f, 74f)
                .Input(k_PortIn, "In", k_TypeFloat);

            s_GraphSchema.TypeColor(k_TypeFloat, new Color(0.45f, 0.78f, 1f, 1f));
            s_GraphSchema.AllowSameTypes();

            s_Graph.AddNode(s_GraphSchema, k_KindConstant, new Vector2(40f, 60f), id: "constant");
            s_Graph.AddNode(s_GraphSchema, k_KindMultiply, new Vector2(250f, 40f), id: "multiply");
            s_Graph.AddNode(s_GraphSchema, k_KindAdd, new Vector2(470f, 130f), id: "add");
            s_Graph.AddNode(s_GraphSchema, k_KindOutput, new Vector2(690f, 160f), id: "output");

            s_Graph.TryAddLink("constant", k_PortValue, "multiply", k_PortA);
            s_Graph.TryAddLink("multiply", k_PortResult, "add", k_PortA);
            s_Graph.TryAddLink("add", k_PortResult, "output", k_PortIn);

            // The evaluator: one handler per kind, reading upstream values through the context. Pure C# - no
            // drawing, no shader, nothing browser-specific - which is why it is reported separately from the canvas.
            s_Evaluator
                .Kind(k_KindConstant, context => s_GraphConstant)
                .Kind(k_KindMultiply, context => context.Input(k_PortA, 0f) * (context.HasInput(k_PortB)
                    ? context.Input(k_PortB, 1f)
                    : s_GraphFactor))
                .Kind(k_KindAdd, context => context.Input(k_PortA, 0f) + (context.HasInput(k_PortB)
                    ? context.Input(k_PortB, 0f)
                    : s_GraphOffset))
                .Kind(k_KindOutput, context => context.Input(k_PortIn, 0f));
        }

        /// <summary>
        /// The extension's default renderer with <see cref="DrawLink"/> replaced by a polyline.
        /// </summary>
        /// <remarks>
        /// Written as a workaround for an unported <c>NowUI/UI Bezier</c>, which turned out not to need working
        /// around (see the note at the head of this file). Kept because what it actually demonstrates is worth
        /// having on its own: that a caller in another assembly can subclass the extension's renderer and replace
        /// one virtual, under WebAssembly, and have the canvas dispatch to it. Everything except this single
        /// override is <c>NowNodeGraphDefaultRenderer</c>'s own code.
        /// <para>The cubic is the same one the extension uses (<c>ConnectionControlPoints</c> is internal, so the
        /// control points are rebuilt from the documented horizontal-tangent shape) sampled into a polyline, which
        /// draws through <c>NowMeshKind.Rectangle</c> - the ported UI Rectangle program.</para>
        /// </remarks>
        private sealed class PolylineLinkRenderer : NowNodeGraphDefaultRenderer
        {
            private const int k_Samples = 24;

            /// <summary>Reused across links and frames; <c>DrawPolyline</c> consumes the span immediately.</summary>
            private static readonly Vector2[] s_Points = new Vector2[k_Samples + 1];

            public override void DrawLink(in NowNodeGraphLinkContext context)
            {
                Vector2 from = context.from;
                Vector2 to = context.to;

                // Horizontal tangents proportional to the horizontal gap, which is the shape the extension's own
                // ConnectionControlPoints produces and what makes a link read as leaving a port sideways.
                float tangent = Mathf.Max(40f, Mathf.Abs(to.x - from.x) * 0.5f);
                Vector2 c1 = new Vector2(from.x + tangent, from.y);
                Vector2 c2 = new Vector2(to.x - tangent, to.y);

                for (int i = 0; i <= k_Samples; ++i)
                {
                    float t = i / (float)k_Samples;
                    float u = 1f - t;

                    s_Points[i] =
                        u * u * u * from +
                        3f * u * u * t * c1 +
                        3f * u * t * t * c2 +
                        t * t * t * to;
                }

                float width = Mathf.Max(1f, context.width) + (context.selected ? 1.5f : context.hovered ? 1f : 0f);

                // DrawPolyline takes _defaultMaterial with NowMeshKind.Rectangle (NowLine.cs:485), i.e. the
                // UI Rectangle program - one program rather than two, whatever else is ported.
                Now.DrawPolyline(System.MemoryExtensions.AsSpan(s_Points), width, NowLineCap.Round, context.color);
            }
        }

        // ==================================================================================================== Sdf

        /// <summary>
        /// The shape system, drawn: primitives, the boolean algebra, a morph, and the distance effects.
        /// </summary>
        /// <remarks>
        /// <para>This page REPLACES the report-only version that stood here while "NowUI/SDF Scene" was unported.
        /// Its rows are kept, in shortened form, at the foot - because two of the four things the report listed
        /// are still true, and a page that quietly stopped mentioning them would read as though the whole
        /// extension had landed.</para>
        /// <para><b>What is deliberately NOT on this page.</b> No Image or Sprite node, and no BeginMask. Image
        /// nodes need "Hidden/NowUI/SDF Image Field" - the jump-flood program that turns a texture into a
        /// distance field - which is not ported, so the first Image() would throw out of the frame flush and
        /// erase the evidence for every cell beside it. That is the same lesson the old ProbeSdf recorded: the
        /// granularity of an unported-shader failure is the FRAME, not the call.</para>
        /// <para>The one draw that could still take the page down is the glyph node, which is why it is behind
        /// <c>?area=sdf&amp;sdftext=1</c> rather than always on. Everything else here is analytic and reaches no
        /// program but this one.</para>
        /// <para><b>Scene coordinates are top-left, y-down</b> - NowSdfShaderV2.cginc says so at :1450 and the
        /// fragment stage builds them that way. So a cell's shapes are authored against (0,0) at the top-left of
        /// the scene rect, in the same sense as every other NowUI coordinate.</para>
        /// </remarks>
        private static void DrawSdf(NowRect body)
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color accent = theme.GetColor(NowColorToken.Accent);

            // Warm, cool and neutral, so a boolean result is readable as "which operand won" rather than as one
            // undifferentiated blob.
            Color warm = new Color(0.98f, 0.45f, 0.24f, 1f);
            Color cool = new Color(0.30f, 0.68f, 0.98f, 1f);
            Color mint = new Color(0.36f, 0.86f, 0.62f, 1f);

            float x = body.x + 20f;
            float width = body.width - 40f;

            // ------------------------------------------------------------------ primitives
            NowRect primitives = Panel(new NowRect(x, body.y + 10f, width, 178f),
                                       "Primitives - every analytic node type NowSdfShaderV2 dispatches");
            NowRect cell = SdfFirstCell(primitives, 9);

            NowRect s = SdfCellFrame(cell, "Circle");
            NowSdf.Scene(s, new NowId(101)).SetColor(warm)
                .Circle(SdfCentre(s), 34f).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "Box");
            NowSdf.Scene(s, new NowId(102)).SetColor(warm)
                .Box(SdfInner(s, 22f, 30f)).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "RoundedBox");
            NowSdf.Scene(s, new NowId(103)).SetColor(warm)
                .RoundedBox(SdfInner(s, 22f, 30f), 18f).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "Ellipse");
            NowSdf.Scene(s, new NowId(104)).SetColor(warm)
                .Ellipse(SdfInner(s, 16f, 30f)).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "Capsule");
            NowSdf.Scene(s, new NowId(105)).SetColor(warm)
                .Capsule(new Vector2(s.width * 0.30f, s.height * 0.68f),
                         new Vector2(s.width * 0.70f, s.height * 0.32f), 16f).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "ChamferedBox");
            NowSdf.Scene(s, new NowId(106)).SetColor(cool)
                .ChamferedBox(SdfInner(s, 22f, 30f), 18f).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "Triangle");
            NowSdf.Scene(s, new NowId(107)).SetColor(cool)
                .Triangle(new Vector2(s.width * 0.5f, s.height * 0.18f),
                          new Vector2(s.width * 0.86f, s.height * 0.80f),
                          new Vector2(s.width * 0.14f, s.height * 0.80f)).Draw();

            // ARC AND PIE TAKE RADIANS, not degrees. Both clamp `sweep` to +/- 2*PI (NowSdf.cs:590, :617), so a
            // sweep written in degrees is clamped to a full turn, and RadialData then returns its full-turn
            // SENTINEL (0, -1, 0, 0) — whose zero .zw the shader reads at NowSdfShaderV2.cginc:368 as "bypass
            // the aperture formula". The first capture of this page drew a complete ring and a complete disc for
            // exactly that reason, and the shader was right both times. Deg2Rad here rather than radian
            // literals so the intended angles stay legible: a 260-degree arc from 40 degrees leaves a gap, which
            // is the only thing that distinguishes an arc from a ring in a still image.
            s = SdfCellFrame(cell = SdfNextCell(cell), "Arc (260 deg)");
            NowSdf.Scene(s, new NowId(108)).SetColor(cool)
                .Arc(SdfCentre(s), 30f, 9f, 40f * Mathf.Deg2Rad, 260f * Mathf.Deg2Rad).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "Pie (250 deg)");
            NowSdf.Scene(s, new NowId(109)).SetColor(cool)
                .Pie(SdfCentre(s), 34f, 200f * Mathf.Deg2Rad, 250f * Mathf.Deg2Rad).Draw();

            // ------------------------------------------------------------------ boolean algebra
            //
            // Every cell is the SAME two shapes - a circle and an overlapping rounded box - so the picture
            // differs only by the operation. Anything else would let a shape difference be mistaken for an
            // operator difference.
            NowRect algebra = Panel(new NowRect(x, primitives.yMax + 26f, width, 178f),
                                    "Shape algebra - one circle and one rounded box, six operators");
            cell = SdfFirstCell(algebra, 6);

            s = SdfCellFrame(cell, "Union");
            SdfPair(NowSdf.Scene(s, new NowId(201)), s, warm, cool, NowSdfOperation.Union, 0f);

            s = SdfCellFrame(cell = SdfNextCell(cell), "Subtract");
            SdfPair(NowSdf.Scene(s, new NowId(202)), s, warm, cool, NowSdfOperation.Subtract, 0f);

            s = SdfCellFrame(cell = SdfNextCell(cell), "Intersect");
            SdfPair(NowSdf.Scene(s, new NowId(203)), s, warm, cool, NowSdfOperation.Intersect, 0f);

            s = SdfCellFrame(cell = SdfNextCell(cell), "SmoothUnion(18)");
            SdfPair(NowSdf.Scene(s, new NowId(204)), s, warm, cool, NowSdfOperation.SmoothUnion, 18f);

            s = SdfCellFrame(cell = SdfNextCell(cell), "SmoothSubtract(14)");
            SdfPair(NowSdf.Scene(s, new NowId(205)), s, warm, cool, NowSdfOperation.SmoothSubtract, 14f);

            s = SdfCellFrame(cell = SdfNextCell(cell), "SmoothIntersect(14)");
            SdfPair(NowSdf.Scene(s, new NowId(206)), s, warm, cool, NowSdfOperation.SmoothIntersect, 14f);

            // ------------------------------------------------------------------ morph and the distance effects
            NowRect effects = Panel(new NowRect(x, algebra.yMax + 26f, width, 178f),
                                    "Morph and the distance effects - all six read the same signed field");
            cell = SdfFirstCell(effects, 7);

            // A morph is a lerp of two FIELDS, not of two rasters: the square genuinely grows a curved
            // silhouette on its way to being a circle. t is driven by the gallery's pinned clock, so a capture
            // lands mid-flight rather than on either endpoint - at either end a morph is indistinguishable from
            // a shape that was simply drawn.
            s = SdfCellFrame(cell, "Morph (box -> circle)");
            // Two things are tuned here, both so that the still image can be read at all.
            //
            // The +0.85 phase: FeatureGallery pins the clock at 0.45s for a capture, and this lands t on
            // exactly 0.5 there. Without it t lands at 0.12 and the cell photographs as a rounded box.
            //
            // A BAR rather than a square: a rounded box of roughly the circle's size lerps into something that
            // still reads as a circle, because the two fields only disagree much at the corners. A wide flat
            // bar disagrees with a circle everywhere, so the half-way field is visibly neither of its ends -
            // which is the whole claim a morph cell has to support.
            float morphT = 0.5f - 0.5f * Mathf.Cos(FeatureGallery.clock * 1.6f + 0.85f);
            NowSdfGraph square = NowSdf.Graph().RoundedBox(SdfInner(s, 6f, 40f), 5f, mint);
            NowSdfGraph round = NowSdf.Graph().Circle(SdfCentre(s), 32f, warm);
            NowSdf.Scene(s, new NowId(301)).Morph(square, round, morphT).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "SetOutline(4)");
            NowSdf.Scene(s, new NowId(302)).SetColor(cool)
                .SetOutline(4f, accent)
                .RoundedBox(SdfInner(s, 20f, 28f), 16f).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "SetGlow(22)");
            NowSdf.Scene(s, new NowId(303)).SetColor(warm)
                .SetGlow(22f, new Color(1f, 0.62f, 0.20f, 0.9f), 1.6f)
                .Circle(SdfCentre(s), 24f).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "SetShadow(6,7)");
            NowSdf.Scene(s, new NowId(304)).SetColor(mint)
                .SetShadow(new Vector2(6f, 7f), 8f, new Color(0f, 0f, 0f, 0.75f))
                .RoundedBox(SdfInner(s, 20f, 28f), 14f).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "SetInnerShadow");
            NowSdf.Scene(s, new NowId(305)).SetColor(cool)
                // Offset is in SCENE coordinates, which are y-DOWN: a negative y samples the field ABOVE this
                // fragment, so the uncovered band - the shadow - falls along the BOTTOM inside edge.
                .SetInnerShadow(new Vector2(0f, -9f), 11f, new Color(0f, 0f, 0f, 0.9f))
                .RoundedBox(SdfInner(s, 20f, 28f), 14f).Draw();

            s = SdfCellFrame(cell = SdfNextCell(cell), "SetEmboss");
            NowSdf.Scene(s, new NowId(306)).SetColor(new Color(0.55f, 0.55f, 0.62f, 1f))
                .SetEmboss(new Vector2(-0.6f, -0.8f), 0.5f, 9f)
                .RoundedBox(SdfInner(s, 20f, 28f), 16f).Draw();

            // Contour bands are the clearest proof on the page that what is being drawn is a FIELD and not a
            // shape: the rings are level sets of the same distance every other cell thresholds at zero.
            s = SdfCellFrame(cell = SdfNextCell(cell), "SetContours(10,2)");
            NowSdf.Scene(s, new NowId(307)).SetColor(new Color(0.16f, 0.20f, 0.28f, 1f))
                .SetContours(10f, 2f, new Color(0.45f, 0.85f, 1f, 0.85f))
                .Circle(SdfCentre(s), 22f).Draw();

            // ------------------------------------------------------------------ what is still missing
            NowRect notes = new NowRect(x, effects.yMax + 26f, width, 76f);

            Now.Rectangle(notes)
                .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                .SetRadius(8f)
                .SetOutline(1f, theme.GetColor(NowColorToken.Border))
                .Draw();

            Now.Text(new NowRect(notes.x + 14f, notes.y + 8f, notes.width - 28f, 18f))
                .SetFontSize(12f).SetBold().SetColor(theme.GetColor(NowColorToken.Text))
                .Draw("Still missing from NowUI.Extensions.Sdf, after this port");

            Caption(new NowRect(notes.x + 14f, notes.y + 28f, notes.width - 28f, 44f), k_SdfRemaining);

            DrawSdfText(new NowRect(x, notes.yMax + 10f, width, 30f));
        }

        /// <summary>
        /// The glyph node, behind <c>?area=sdf&amp;sdftext=1</c>.
        /// </summary>
        /// <remarks>
        /// Behind a flag rather than always on, for the reason the old ProbeSdf established: an unported program
        /// throws out of the FRAME FLUSH, past any try/catch around the call, and takes every other cell on the
        /// page with it. Glyph nodes are the one path on this page that samples <c>_MainTex</c> as a font atlas
        /// and the one whose distance field is a texture rather than a formula - so they are the one thing here
        /// that could still fail. Keeping the flag means the default capture measures the analytic system and a
        /// second, deliberate capture measures the glyph one, instead of one capture measuring neither.
        /// </remarks>
        private static void DrawSdfText(NowRect at)
        {
            if (!sdfText)
            {
                Caption(at, "Glyph nodes (NowSdf.Text) are not drawn here by default - add ?sdftext=1. MEASURED " +
                            "with that flag: they render, outline included, sampling the font atlas through " +
                            "_MainTex. Kept behind the flag anyway, because an unported program takes the whole " +
                            "frame down rather than one cell, and the glyph path is the only one on this page " +
                            "whose distance field is a texture rather than a formula.");
                return;
            }

            NowSdf.Scene(at, new NowId(401))
                .SetColor(new Color(0.95f, 0.95f, 0.98f, 1f))
                .SetOutline(2f, new Color(0.30f, 0.68f, 0.98f, 1f))
                .Text(new Vector2(2f, 2f), "Glyph nodes: SDF text with an outline", 22f)
                .Draw();
        }

        /// <summary>Whether the glyph-node cell draws. Off by default; set from <c>?sdftext=1</c>.</summary>
        internal static bool sdfText;

        // ------------------------------------------------------------------------------- sdf cell furniture

        /// <summary>Cell size and gap, shared so the three panels line up column for column.</summary>
        private const float k_SdfCellGap = 10f;

        /// <summary>The first of <paramref name="count"/> equal cells across <paramref name="inner"/>.</summary>
        private static NowRect SdfFirstCell(NowRect inner, int count)
        {
            float cellWidth = (inner.width - k_SdfCellGap * (count - 1)) / count;
            return new NowRect(inner.x, inner.y + 4f, cellWidth, inner.height - 8f);
        }

        /// <summary>The cell to the right of <paramref name="cell"/>.</summary>
        private static NowRect SdfNextCell(NowRect cell)
        {
            return new NowRect(cell.x + cell.width + k_SdfCellGap, cell.y, cell.width, cell.height);
        }

        /// <summary>Draws a cell's ground and its caption, and returns the rect the SDF scene occupies.</summary>
        private static NowRect SdfCellFrame(NowRect cell, string label)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            Now.Rectangle(cell)
                .SetColor(theme.GetColor(NowColorToken.Surface))
                .SetRadius(8f)
                .SetOutline(1f, theme.GetColor(NowColorToken.Border))
                .Draw();

            // The label sits INSIDE the cell's bottom edge rather than under it, so the scene rect above it is
            // the whole of the remaining box and every cell's shapes are centred in the same amount of room.
            Now.Text(new NowRect(cell.x + 7f, cell.yMax - 18f, cell.width - 14f, 14f))
                .SetFontSize(10f)
                .SetColor(theme.GetColor(NowColorToken.TextMuted))
                .Draw(label);

            return new NowRect(cell.x + 5f, cell.y + 5f, cell.width - 10f, cell.height - 28f);
        }

        /// <summary>The centre of a scene rect, in SCENE coordinates (top-left origin, y down).</summary>
        private static Vector2 SdfCentre(NowRect scene)
        {
            return new Vector2(scene.width * 0.5f, scene.height * 0.5f);
        }

        /// <summary>A scene-local rect inset by <paramref name="padX"/> / <paramref name="padY"/>.</summary>
        private static NowRect SdfInner(NowRect scene, float padX, float padY)
        {
            return new NowRect(padX, padY, scene.width - padX * 2f, scene.height - padY * 2f);
        }

        /// <summary>
        /// The two operands every algebra cell is built from, folded with one operator.
        /// </summary>
        /// <remarks>
        /// The operation is set on the SECOND node, because an operator describes how a node joins what is
        /// already there - the first node in a graph is always a Union whatever it was given
        /// (NowSdfCache.Upload writes <c>i == 0 ? Union : layer.operation</c>, NowSdf.cs:4849).
        /// <para><paramref name="scene"/> is taken by value and its <c>Draw()</c> called here rather than
        /// returned to the caller, because <c>NowSdfBuilder</c> is a STRUCT: everything the shape calls do lands
        /// in the shared cache, but the tint and rect live in the struct, so handing a copy back and forth is a
        /// way to lose them.</para>
        /// </remarks>
        private static void SdfPair(NowSdfBuilder scene, NowRect at, Color first, Color second,
                                    NowSdfOperation operation, float smoothing)
        {
            scene
                .SetColor(first)
                .Circle(new Vector2(at.width * 0.40f, at.height * 0.42f), 28f)
                .SetColor(second)
                .SetOperation(operation, smoothing)
                .RoundedBox(new NowRect(at.width * 0.40f, at.height * 0.44f,
                                        at.width * 0.46f, at.height * 0.42f), 10f)
                .Draw();
        }

        /// <summary>
        /// What the Sdf extension still cannot do here, after the scene program landed.
        /// </summary>
        /// <remarks>
        /// Two of the report page's five rows survived the port and both are about the same missing program, so
        /// they are one sentence here rather than five rows. The three that did not survive - the scene shader
        /// itself, render targets and Blit - are gone because they are done, and saying so is the point of
        /// keeping the note at all.
        /// </remarks>
        private const string k_SdfRemaining =
            "Nothing, on this page. This note used to say Image and Sprite nodes were missing because " +
            "'Hidden/NowUI/SDF Image Field' was unported; all five of its passes are ported now and ?area=sdf-image " +
            "draws an image-shaped field with its contour bands, outline, glow, shadow, emboss and a morph. The " +
            "one doubt that note raised was real and is now answered by measurement rather than assumption: " +
            "NowSdfImageField asks SystemInfo.SupportsRenderTextureFormat for RHalf, then RFloat, then ARGBHalf " +
            "(:283-292), and the backend answers that from an EXT_color_buffer_float / " +
            "EXT_color_buffer_half_float probe rather than a blanket yes. This page still draws no image node, " +
            "deliberately - the two programs fail differently and are worth capturing separately.";
    }
}
