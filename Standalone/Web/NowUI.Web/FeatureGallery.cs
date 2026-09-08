// The feature gallery: one NowUI feature area on screen at a time, chosen by `?area=NAME`.
//
// Why this shape. Milestone 2's question stopped being "can a NowUI frame reach a browser at all" - slice 1 answered
// that, and the answer is in M2-Scouting.md - and became "WHICH of NowUI's features work here". That question is
// only answerable one feature at a time, by something that can be pointed at exactly one of them and captured. A
// single scene cannot do it: an unported shader throws by name inside DrawMesh (WebGL2Backend.DrawMesh), which stops
// the frame loop, so one unsupported primitive in a combined scene takes every other feature down with it and the
// capture shows nothing about any of them. Isolation is not tidiness here, it is the measuring instrument.
//
// So: an area is a name, a size, a stated expectation, and one draw method. `?area=shapes` draws that method and
// nothing else. If it renders, that feature works in the browser. If the page reports a failure naming the area,
// that feature does not, and the message says which shader or backend call refused. Both are results.
//
// The expectation string is deliberately part of the data rather than a comment. Each area records what this unit
// predicted BEFORE anything was captured, so the capture unit is confirming or refuting a written prediction rather
// than describing whatever it happens to see. Docs/Standalone/M2-FeatureMatrix.md carries the same predictions in
// table form and has a column for the answers.
//
// What replaced what. This file supersedes the `?scene=` parity-scene path that Program.cs used to carry. That path
// compiled `Assets/NowUIHarness/NowParityScenes.cs` by file path - and that file does not exist in the repository,
// so the web project did not build at all before this change. See the report accompanying this slice; the frozen
// Unity tree was not touched to fix it.
using System;
using System.Collections.Generic;
using System.Text;
using NowUI;
using UnityEngine;

namespace NowUI.Web
{
    /// <summary>One feature area: what it is called, how big it wants to be, what was predicted of it, and how it draws.</summary>
    internal sealed class NowGalleryArea
    {
        internal NowGalleryArea(string id, string title, string summary, string expectation,
                                int width, int height, Action<NowRect> draw)
        {
            this.id = id;
            this.title = title;
            this.summary = summary;
            this.expectation = expectation;
            this.width = width;
            this.height = height;
            this.draw = draw;
        }

        /// <summary>The value of <c>?area=</c> that selects this one. Lower case, no spaces, stable.</summary>
        internal readonly string id;

        /// <summary>Human title, drawn in the header strip.</summary>
        internal readonly string title;

        /// <summary>One line saying what the area puts on screen.</summary>
        internal readonly string summary;

        /// <summary>
        /// What this unit predicted the browser would do with this area, and why, written before any capture.
        /// A refuted prediction is a finding; a vague one is not.
        /// </summary>
        internal readonly string expectation;

        /// <summary>Canvas size in CSS pixels for a capture of this area. Both zero means "follow the window".</summary>
        internal readonly int width;
        internal readonly int height;

        /// <summary>Draws the area into the body rect - the view minus the header strip.</summary>
        internal readonly Action<NowRect> draw;
    }

    /// <summary>The gallery: the area list, the lookup, and the chrome drawn around whichever one is showing.</summary>
    internal static partial class FeatureGallery
    {
        /// <summary>The area shown when no <c>?area=</c> is given. The interactive demo, so the page's default is unchanged.</summary>
        internal const string defaultAreaId = "controls";

        /// <summary>Header strip height in UI units. Zero when chrome is off.</summary>
        private const float k_HeaderHeight = 46f;

        private static bool s_ThemeSelected;

        /// <summary>
        /// The clock every animated area reads, in seconds.
        /// </summary>
        /// <remarks>
        /// FROZEN by default, and that is the point. A headless capture lands on whatever phase the wall clock
        /// happened to be at, so an animation captured at its own zero renders as nothing at all - which is
        /// indistinguishable, in a PNG, from an animation that does not work. Pinning it makes every capture of an
        /// area byte-comparable with the last one, and picks a phase where every built-in animation is visibly
        /// part-way through rather than at either end.
        /// <para><c>?animate=1</c> hands the real clock back, for a person looking at the live page.</para>
        /// </remarks>
        internal static float clock
        {
            get { return animate ? Time.unscaledTime : k_FrozenClock; }
        }

        /// <summary>Whether animated areas follow the wall clock. Set from <c>?animate=1</c> before the first frame.</summary>
        internal static bool animate;

        /// <summary>
        /// 0.45 seconds in.
        /// </summary>
        /// <remarks>
        /// Chosen against the built-in animations' own defaults rather than picked for looks: Typewriter runs at 24
        /// characters a second, FadeIn is a 0.3s per-glyph fade with 0.025s of stagger, ScaleIn and FadeUp are the
        /// same shape. At 0.45s a ten-character string is part-typed, the early glyphs have finished fading and the
        /// late ones have not, and Wave is off its zero crossing. Every animation is therefore mid-flight, which is
        /// the only phase that distinguishes "running" from "absent".
        /// </remarks>
        private const float k_FrozenClock = 0.45f;

        /// <summary>
        /// Every area, in the order a reader should walk them: the application first, then the drawing primitives
        /// that everything else is built from, then text, then paint, then the interface layer, then the extensions.
        /// </summary>
        /// <remarks>
        /// Built once and cached. The delegates are static methods, so the array holds no per-frame allocation.
        /// </remarks>
        private static readonly NowGalleryArea[] k_Areas =
        {
            // ---------------------------------------------------------------- the application
            new NowGalleryArea(
                "controls", "Control library (interactive demo)",
                "The slice-2 task list: text fields, button, slider, switch, checkbox, progress bar, scroll view.",
                "Works. This is what slice 2 shipped and drove with synthetic DOM events; every control in it draws " +
                "through UI Rectangle or Text Renderer.",
                0, 0, DrawControls),

            // ---------------------------------------------------------------- drawing primitives
            new NowGalleryArea(
                "rectangles", "Rectangles",
                "Fill, per-corner radius, outline, blur, padding, and a textured quad.",
                "Works. UIRectangle is ported whole (M2-ShaderPort.md section 3.6 says there is nothing in it to " +
                "stub), and blur/outline/padding are all vertex-carried scalars that shader already reads.",
                1180, 720, DrawRectangles),

            new NowGalleryArea(
                "shapes", "Shapes",
                "Circles, ellipses, triangles and polygons, filled and outlined.",
                "Works, and this contradicts the note at the foot of DemoScene.cs. NowShape draws through " +
                "_defaultMaterial with NowMeshKind.Rectangle (NowShape.cs:476), i.e. the ported UI Rectangle " +
                "program - it is the Sdf EXTENSION's shape algebra that goes through 'NowUI/SDF Scene' (also " +
                "ported now; see ?area=sdf), not Now.Circle/Triangle/Polygon.",
                1180, 720, DrawShapes),

            new NowGalleryArea(
                "lines", "Lines (straight and polyline)",
                "Widths, caps, dashes, arrow heads and gradient strokes on straight segments and polylines.",
                "Works. Straight lines and polylines also take the _defaultMaterial / NowMeshKind.Rectangle path " +
                "(NowLine.cs:359 and :485), so they are UI Rectangle geometry, not the Bezier program.",
                1180, 720, DrawLines),

            new NowGalleryArea(
                "bezier", "Bezier curves",
                "Now.Bezier only, isolated so its failure cannot take the rest of the line work with it.",
                "FAILS. Now.Bezier alone takes NowMeshKind.Bezier with the 'NowUI/UI Bezier' material " +
                "(NowLine.cs:620); that program is not ported, and WebGL2Backend.DrawMesh throws by name on it.",
                1180, 480, DrawBezier),

            // ---------------------------------------------------------------- text
            new NowGalleryArea(
                "text", "Text",
                "Sizes, bold/italic faces, outlines, colours, tabs and newlines, non-ASCII, and wrapped paragraphs.",
                "Works. Text was slice 1's second half and matched Unity's own render to a median of 2/255. Faces " +
                "beyond Regular depend on the exported family carrying them - Bold, Italic and BoldItalic are all " +
                "in wwwroot/Fixtures/NowUI - and non-ASCII depends on the glyph being in NotoSans rather than on " +
                "the browser.",
                1180, 720, DrawText),

            new NowGalleryArea(
                "textfx", "Text gradients and animations",
                "Linear, radial and conic gradient fills on glyphs, and the built-in glyph animations.",
                "UNCERTAIN, and this is the area most worth capturing early. M2-ShaderPort.md section 4.5 permitted " +
                "slice 1 to omit the text shader's gradient branch on condition that a text vertex with " +
                "extras.w != 0 be reported rather than silently flattened. If the branch was omitted, gradient text " +
                "renders as plausible FLAT text - the exact failure that looks like success. The animations are " +
                "CPU-side glyph transforms with no shader of their own and should work regardless.",
                1180, 620, DrawTextEffects),

            // ---------------------------------------------------------------- paint
            new NowGalleryArea(
                "gradients", "Gradients",
                "Linear, radial and conic fills, spread modes, repetitions and Unity ramp keys.",
                "Works. UIGradient is one of the four ported programs. The ramp-keyed forms additionally need the " +
                "256x256 ramp atlas to reach the backend as a shader GLOBAL rather than through a material bag " +
                "(NowGradient.cs:580), which is a resolution path worth confirming separately from the geometry.",
                1180, 720, DrawGradients),

            new NowGalleryArea(
                "ripple", "Ripple",
                "The ripple program's expanding disc, driven by caller-passed time.",
                "Works. UI Ripple is the fourth ported program.",
                1180, 480, DrawRipple),

            new NowGalleryArea(
                "glass", "Glass",
                "A frosted pane over patterned content.",
                "FAILS, twice over. 'NowUI/UI Glass' is unported, and the blur behind it is four passes of " +
                "'NowUI/UI Glass Blur' into render textures that this backend cannot allocate " +
                "(CreateRenderTexture throws). Expect the shader rejection first.",
                1180, 480, DrawGlass),

            new NowGalleryArea(
                "masks", "Masks",
                "Rect clip, rounded/circle/ellipse/capsule analytic masks, feathering, and nesting.",
                "Works. NowUIMask.cginc is shared by both ported programs and was ported with them; the uniform " +
                "arrays arrive at fixed capacity 8 (M2-ShaderPort.md section 5.2). Texture masks are drawn here too " +
                "and are the part most likely to differ, because they need a second and third sampler bound per draw.",
                1180, 720, DrawMasks),

            new NowGalleryArea(
                "effects", "Effects",
                "NowEffects.Modifier wave and genie deformers over ordinary draw calls.",
                "Mesh-capture deformers should work: they subdivide and move vertices that still draw through UI " +
                "Rectangle and Text Renderer, adding no program. SetRenderToTexture and NowEffects.Snapshot should " +
                "NOT, because both need a render target and CreateRenderTexture throws. Only the mesh path is drawn " +
                "here; the texture path is a named omission, not an untested claim.",
                1180, 620, DrawEffects),

            // ---------------------------------------------------------------- the interface layer
            new NowGalleryArea(
                "layout", "Layout",
                "Rows, columns, gaps, padding, alignment, stretch, spacers and nesting, with every reserved rect outlined.",
                "Works. NowLayout is arithmetic over rects with no rendering of its own. The one browser-specific " +
                "risk is the measure pass: DemoScene.cs found that plain NowLayout.Column resolved main-axis " +
                "stretch from the PREVIOUS frame and reported width 0 on frame one, which a one-shot capture would " +
                "photograph. RunMeasured is used here for the same reason.",
                1180, 720, DrawLayout),

            new NowGalleryArea(
                "theme", "Themes",
                "Every NowColorToken as a swatch, in the light and the dark built-in theme side by side.",
                "Works. Themes are ScriptableObject-backed data resolved through the shim's CreateInstance path, " +
                "which DemoScene already exercises by asking for the dark theme.",
                1180, 720, DrawTheme),

            new NowGalleryArea(
                "fields", "Value controls",
                "The controls the demo does not reach: dropdown, combo box, tabs, foldout, radio, badge, chip, " +
                "numeric and vector fields, text area, tree view.",
                "MOSTLY works, with two named exceptions inside it. ColorPicker needs 'NowUI/Color Picker' and " +
                "AnimationCurveField needs 'NowUI/UI Bezier' (NowValueControls.cs), neither ported, so both are " +
                "deliberately absent here and listed in the matrix as unported rather than untested. Dropdown and " +
                "combo box popups are overlays, which is a separate question from their closed field.",
                1180, 720, DrawFields),

            new NowGalleryArea(
                "pickers", "Pickers and curves",
                "ColorPicker, GradientField and CurveField as closed fields, plus a render-to-texture deformer " +
                "and a NowEffects.Snapshot drawn back as a texture.",
                "MEASUREMENT, not prediction: this area exists because the `fields` and `effects` areas excluded " +
                "these four on the grounds that 'NowUI/Color Picker' and 'NowUI/UI Bezier' were unported and " +
                "CreateRenderTexture threw. All three of those grounds are gone - IsPortedShader lists eight " +
                "programs and every render-target method is implemented - so the exclusions were stale rather " +
                "than true. Note that the picker's saturation/value square is in a POPUP: a still capture proves " +
                "the closed field only, and the popup has to be opened with a pointer.",
                1180, 720, DrawPickers),

            new NowGalleryArea(
                "rendertexture", "Render to texture",
                "A deformer with SetRenderToTexture(true), the same deformer in place as a control, and a " +
                "NowEffects.Snapshot drawn back through SetTexture.",
                "MEASURED, and the answer is a failure with no error: CreateRenderTexture, SetRenderTarget, Blit, " +
                "DrawProcedural and CopyTexture are all implemented now, so the matrix's 'not ported' is stale - " +
                "but the frame this area draws comes back EMPTY, clear colour only, with nothing on the console. " +
                "That is what a render target bound and never restored looks like from the outside.",
                1180, 720, DrawRenderTexture),

            new NowGalleryArea(
                "remote", "Remote loading (fetch and image decode)",
                "The two host services that were hard-coded null: INowFetchProvider driven directly with five " +
                "probes, and INowImageDecoder answering from both of its paths.",
                "MEASUREMENT, not prediction: this area was written with the port it tests, so nothing here is a " +
                "guess about what a browser will do. What it is FOR is separating the claims a single 'the image " +
                "loaded' capture would fuse together - that a status and headers arrive, that the body arrives in " +
                "chunks, that a sink returning false actually stops a transfer mid-flight, that a 404 is a " +
                "transport success carrying a failure status, that a PNG decodes in managed code with no network, " +
                "and that a JPEG decodes only because the browser decoded it BEFORE the synchronous TryDecode ran. " +
                "Everything is same-origin out of this app's own wwwroot, so it proves the transport and not the " +
                "internet; ?xorigin=1 adds a cross-origin probe whose result is reported as observed.",
                1180, 900, DrawRemote),

            // ---------------------------------------------------------------- extensions
            //
            // All seven are now REFERENCED by NowUI.Web.csproj. Before this slice none of them was, so the only
            // honest statement about any of them was "compiles, never linked". All seven are now exercised with
            // real content, Sdf included: it was a report page while "NowUI/SDF Scene" was unported, and the port
            // that landed turned it into the shape system actually drawing. What survives of the report is one
            // paragraph at the foot of that page, naming the one thing still missing.
            new NowGalleryArea(
                "extensions", "Extensions (index)",
                "Where each of the seven extension assemblies stands, and which area exercises it.",
                "This page itself only draws rectangles and text, so it works. What it claims about the other " +
                "seven areas is written from their captures, not predicted.",
                1180, 720, DrawExtensions),

            new NowGalleryArea(
                "markdown", "Markdown",
                "Headings, emphasis, lists, tasks, code fences, a table, a blockquote, links - plus one injected " +
                "image and one remote image.",
                "Works, remote images INCLUDED - which is a change, and the prediction it replaces is worth keeping " +
                "next to it. That prediction read: 'Works, except remote images ... NowRuntime.host.fetch and " +
                "host.imageDecoder are null, so NowMarkdownImages reaches Failed with a message naming the " +
                "provider.' Both services now exist (WebFetchProvider over fetch() + ReadableStream, " +
                "WebImageDecoder over a browser pre-decode with a managed PNG decoder under it), so the remote row " +
                "should reach Loaded and draw. The image is SAME-ORIGIN, out of this app's wwwroot: this row " +
                "proves the transport and the decoder, not the public internet. The injected texture beside it is " +
                "unchanged and still separates the transport from the renderer.",
                1180, 1120, DrawMarkdown),

            new NowGalleryArea(
                "markup", "Markup",
                "An XML-like document with layout, headings, controls, state binding, a visibility expression and " +
                "events, beside the state it drives and its own source.",
                "Works. NowMarkup is a tag parser over the same primitives the core uses and declares no shader of " +
                "its own; its controls are the core controls, which slice 2 already drove with synthetic DOM " +
                "events. NowMarkup.File(path) is the part that cannot work - it resolves against " +
                "Directory.GetCurrentDirectory() and polls timestamps - and is deliberately not used.",
                1180, 760, DrawMarkup),

            new NowGalleryArea(
                "markdown-markup", "Markdown + Markup",
                "The same document twice - with and without a NowMarkupEmbeds set - and, at the foot, the same " +
                "markup drawn with no markdown around it at all.",
                "PREDICTED: works if both halves do, since the bridge adds no primitive of its own. MEASURED: it " +
                "RENDERS but does not COMMIT. The embedded controls draw, highlight on hover and show a pressed " +
                "state, and then no value reaches the shared NowMarkupState and no event reaches " +
                "Clicked/Changed/Action. The control block at the foot is the same markup, the same state object " +
                "and the same NowLayout.Area + Draw(state) call shape the bridge uses, with markdown removed - and " +
                "it commits normally. So the fault is in the markdown host of the embed, not in Markup and not in " +
                "the layout-flowing draw. Whether it is browser-specific was not determined; it needs the same " +
                "document run under Unity.",
                1180, 800, DrawMarkdownMarkup),

            new NowGalleryArea(
                "codeeditor", "Code editor",
                "Two editors: C# and JSON, the JSON buffer carrying one deliberate syntax error for the validator.",
                "Works. Syntax colouring is per-run vertex colour rather than a shader, and the validator is a " +
                "parser. Typing and clipboard need the host text-input source and INowClipboard, both of which " +
                "WebInput installs; the browser's clipboard API is async and permission-gated, which is the one " +
                "part that could differ from Unity.",
                1180, 760, DrawCodeEditor),

            new NowGalleryArea(
                "docking", "Docking",
                "Four panels in a seeded split layout: tab bars, splitters, a closable tab, and live controls " +
                "inside one pane.",
                "Works. Panes, tabs and splitters are rectangles and text; dragging needs the pointer bridge, " +
                "which exists. The layout is seeded through Dock() so a capture lands on a split tree rather than " +
                "on the four-tabs-in-one-pane state an unseeded dock space starts in.",
                1180, 760, DrawDocking),

            // The expectation below keeps the PREDICTION and both corrections, because a prediction quietly
            // rewritten to match its result is worth nothing - and because the first correction was itself wrong,
            // which is the more useful half. See the node-graph note at the head of GalleryAreas.Extensions.cs.
            new NowGalleryArea(
                "nodegraph", "Node graph",
                "A four-node graph with three links, drawn by the extension's own default renderer, plus the " +
                "evaluator's output for the wired-up chain.",
                "PREDICTED (wrongly): fails at the first link, because NowNodeGraphDefaultRenderer.DrawLink ends " +
                "in Now.Bezier (NowNodeGraph.cs:3172) and 'NowUI/UI Bezier' is unported. MEASURED: it works. " +
                "FIRST EXPLANATION (also wrong): Now.Bezier takes the analytic program only when solidCubic && " +
                "!hasTransform (NowLine.cs:317-324) and the canvas draws inside a transform. TRUE CAUSE: " +
                "'NowUI/UI Bezier' had been ported by a parallel unit before the capture was taken - " +
                "WebGL2Backend.IsPortedShader lists it - and ?area=bezier, which uses no transform, renders too. " +
                "Which of the two paths carried these links was not measured and is not claimed.",
                1180, 780, DrawNodeGraph),

            new NowGalleryArea(
                "nodegraph-custom", "Node graph (custom renderer)",
                "The same graph through a subclass of the default renderer with one virtual, DrawLink, replaced.",
                "Works. Written as a route around an unported program, kept after that turned out not to be " +
                "needed, because it measures something the shader port does not touch: that " +
                "INowNodeGraphRenderer can be subclassed and one virtual replaced from another assembly under " +
                "WebAssembly, and that the canvas dispatches to it.",
                1180, 780, DrawNodeGraphCustom),

            new NowGalleryArea(
                "sdf", "SDF shape system",
                "Nine primitives, six boolean operators over one pair of shapes, a morph, and the six distance " +
                "effects - all through the ported 'NowUI/SDF Scene' program.",
                "SUPERSEDES the report-only page that stood here while the program was unported. PREDICTED: the " +
                "analytic half of the system renders in full. NowSdfShaderV2.cginc needs nothing WebGL2 lacks - " +
                "its 480 vec4 of uniform array sit far inside the 4096-vector limit this context reports, its " +
                "loops already carry constant bounds with a dynamic break, and its only Unity built-in is " +
                "_Time.y, read by a domain warp that is off by default. Two things could still refuse: the " +
                "GLYPH node, which is the one path here whose field is a texture and is therefore behind " +
                "?sdftext=1 rather than on the default capture; and IMAGE and SPRITE nodes, which need " +
                "'Hidden/NowUI/SDF Image Field' and are deliberately absent because an unported program takes " +
                "the whole frame down, not just its own cell.",
                1180, 840, DrawSdf),

            new NowGalleryArea(
                "sdf-image", "SDF image field",
                "An image-shaped distance field: the artwork's silhouette, its contour bands, and the outline, " +
                "glow, shadow, emboss and morph that only exist because the field does.",
                "SEPARATE from ?area=sdf on purpose, because the two fail differently. That page fails visibly " +
                "when the scene program is wrong. This one fails INVISIBLY when the bake is wrong: a jump flood " +
                "that loses a seed, or a sign that inverts, produces a smooth plausible field that the scene " +
                "program then renders faithfully, and the result is a silhouette that is merely the wrong shape. " +
                "So the source art is a five-pointed star with a punched hole - convex tips, concave notches, an " +
                "interior hole and an antialiased rim, none of which a wrong implementation reproduces by " +
                "accident - and one cell draws the field's own contour bands rather than its zero contour. " +
                "PREDICTED: it draws. The five passes need nothing WebGL2 lacks once render targets exist, and " +
                "the one real dependency is a FLOAT or HALF-FLOAT colour attachment for the seed and flood " +
                "targets, which needs EXT_color_buffer_float / EXT_color_buffer_half_float. The backend already " +
                "probes for both and answers SystemInfo.SupportsRenderTextureFormat from the probe, so if the " +
                "extension is absent the bake refuses rather than rendering something wrong - and the strip at " +
                "the foot prints which format the device actually granted, plus the backend's per-pass blit " +
                "tally, so a capture records whether the GPU ran the bake at all.",
                1180, 560, DrawSdfImage),

            // Added by the VERIFICATION pass rather than by either porting unit, and that is why it exists: the
            // two pages above prove the two programs run, and a line-by-line read of README.md's SDF paragraphs
            // against them turned up five advertised behaviours that neither page touches. A capture that shows
            // most of a system is not evidence for the rest of it.
            new NowGalleryArea(
                "sdf-compose", "SDF composition, rotation, warp, sprites and scene-as-mask",
                "A boolean tree grown one operand at a time, scoped and per-node rotation, a domain warp that " +
                "reads the clock, a sprite node, and the scene used to clip ordinary NowUI content.",
                "MEASUREMENT, not prediction - written during verification, after both programs were already " +
                "ported. What it is FOR is the gap between 'the SDF pages render' and README.md's actual " +
                "claims. ?area=sdf draws six operators over exactly TWO operands, never a third; it never " +
                "rotates a node; its own note says the warp is off; it never calls Sprite; and nothing on any " +
                "page uses BeginMask, which is the path both README metamorphosis loops clip their gloss with " +
                "and the only one that runs the scene program, a render target and the mask include together. " +
                "Each row here carries a CONTROL beside it - the unrotated box, the unmasked content, the mask " +
                "shape drawn normally - because every one of these can fail by quietly doing nothing, and " +
                "'quietly did nothing' and 'worked' are the same picture without something to compare against.",
                1180, 660, DrawSdfCompose),
        };

        /// <summary>
        /// What the area drawn this frame is holding, as one line, or null.
        /// </summary>
        /// <remarks>
        /// The oracle for "does it INTERACT", which a screenshot cannot answer. Program.cs already published
        /// DemoScene's state for exactly this reason (its own comment: "the click counter went from 3 to 4" is a
        /// fact, where "these pixels got lighter" is an inference), but that hook only knew about the demo. An area
        /// sets this at the end of its draw; <see cref="DebugState"/> hands it to the page.
        /// <para>Set unconditionally rather than behind the ?debug=1 flag, because a string concatenation per frame
        /// is not worth a branch and because an area whose readout only exists in one mode is an area whose readout
        /// is wrong in the other.</para>
        /// </remarks>
        internal static string areaState;

        /// <summary>The active area's state line, falling back to the demo's when the area publishes none.</summary>
        internal static string DebugState()
        {
            return areaState ?? DemoScene.DebugState();
        }

        /// <summary>Every area, in gallery order.</summary>
        internal static IReadOnlyList<NowGalleryArea> all
        {
            get { return k_Areas; }
        }

        /// <summary>The area with this id, or null. Case-insensitive, because a URL is typed by a human.</summary>
        internal static NowGalleryArea Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < k_Areas.Length; ++i)
            {
                if (string.Equals(k_Areas[i].id, id, StringComparison.OrdinalIgnoreCase))
                    return k_Areas[i];
            }

            return null;
        }

        /// <summary>Every area id, comma separated, for the error a mistyped <c>?area=</c> produces.</summary>
        internal static string Names()
        {
            StringBuilder names = new StringBuilder(256);

            for (int i = 0; i < k_Areas.Length; ++i)
            {
                if (i > 0)
                    names.Append(", ");

                names.Append(k_Areas[i].id);
            }

            return names.ToString();
        }

        /// <summary>
        /// Draws one area, with the header strip above it unless <paramref name="chrome"/> is false.
        /// </summary>
        /// <remarks>
        /// The header is on by default because a capture of an unlabelled scene is a picture whose subject has to be
        /// remembered rather than read. It is switchable off (<c>?chrome=0</c>) because a pixel comparison against a
        /// Unity render of the same content must not have to subtract it.
        /// </remarks>
        internal static void Draw(NowGalleryArea area, NowRect view, bool chrome)
        {
            SelectTheme();

            NowThemeAsset theme = NowTheme.themeAsset;

            // The gallery owns the ground, so every area starts from the same surface rather than each one having
            // an opinion. Areas that want their own backdrop still draw over it.
            Now.Rectangle(view)
                .SetColor(theme.GetColor(NowColorToken.Background))
                .Draw();

            NowRect body = view;

            if (chrome)
            {
                NowRect header = view.TakeTop(k_HeaderHeight, out body);
                DrawHeader(area, header, theme);
            }

            area.draw(body);
        }

        /// <summary>The header strip: which area this is, and where it sits in the list.</summary>
        private static void DrawHeader(NowGalleryArea area, NowRect header, NowThemeAsset theme)
        {
            Color text = theme.GetColor(NowColorToken.Text);
            Color muted = theme.GetColor(NowColorToken.TextMuted);

            Now.Rectangle(header)
                .SetColor(theme.GetColor(NowColorToken.Surface))
                .Draw();

            Now.Rectangle(new NowRect(header.x, header.y + header.height - 1f, header.width, 1f))
                .SetColor(theme.GetColor(NowColorToken.Border))
                .Draw();

            Now.Text(new NowRect(header.x + 16f, header.y + 8f, header.width - 32f, 20f))
                .SetFontSize(15f)
                .SetBold()
                .SetColor(text)
                .Draw(area.title);

            Now.Text(new NowRect(header.x + 16f, header.y + 26f, header.width - 32f, 16f))
                .SetFontSize(11f)
                .SetColor(muted)
                .Draw("?area=" + area.id + "   -   " + area.summary);
        }

        /// <summary>
        /// Chooses the dark built-in theme, once.
        /// </summary>
        /// <remarks>
        /// Moved here from DemoScene so every area inherits it rather than only the demo. The reason is unchanged:
        /// the default theme is light, the page's ground is dark, and light-theme muted text over a dark ground is
        /// unreadable grey. The theme area overrides this deliberately and puts it back.
        /// </remarks>
        private static void SelectTheme()
        {
            if (s_ThemeSelected)
                return;

            s_ThemeSelected = true;
            NowTheme.preferDark = true;
        }

        // ------------------------------------------------------------------------------- shared area furniture
        //
        // Small enough to be worth sharing, specific enough not to belong in the core. Every area is built from
        // these three so that fifteen scenes read as one gallery rather than as fifteen opinions about captions.

        /// <summary>A titled panel: the box an area's examples sit on, with its heading drawn inside the top edge.</summary>
        internal static NowRect Panel(NowRect rect, string title)
        {
            NowThemeAsset theme = NowTheme.themeAsset;

            Now.Rectangle(rect)
                .SetColor(theme.GetColor(NowColorToken.Surface))
                .SetRadius(10f)
                .SetOutline(1f, theme.GetColor(NowColorToken.Border))
                .Draw();

            if (!string.IsNullOrEmpty(title))
            {
                Now.Text(new NowRect(rect.x + 14f, rect.y + 10f, rect.width - 28f, 18f))
                    .SetFontSize(13f)
                    .SetBold()
                    .SetColor(theme.GetColor(NowColorToken.Text))
                    .Draw(title);
            }

            // The usable interior: inside the padding, below the heading.
            return rect.Inset(14f, 10f).Inset(0f, string.IsNullOrEmpty(title) ? 0f : 24f, 0f, 0f);
        }

        /// <summary>
        /// An 11px muted caption, wrapped to the width of <paramref name="at"/>.
        /// </summary>
        /// <remarks>
        /// Wrapped rather than drawn as one line, because <c>Now.Text</c> does not wrap on its own and a caption
        /// that runs off the edge of its panel is a caption that has been silently truncated by the canvas - which
        /// looks, in a capture, exactly like a rendering fault. <c>NowTextWrap</c> is the caller-owned two-step
        /// documented in the text area; the runs list is shared because only one caption is laid out at a time.
        /// </remarks>
        internal static void Caption(NowRect at, string text)
        {
            NowText style = Now.Text(default)
                .SetFontSize(11f)
                .SetColor(NowTheme.themeAsset.GetColor(NowColorToken.TextMuted));

            NowTextWrap.Layout(in style, text, at.width, s_CaptionRuns);
            NowTextWrap.Draw(in style, text, s_CaptionRuns, new Vector2(at.x, at.y));
        }

        private static readonly System.Collections.Generic.List<NowTextRun> s_CaptionRuns =
            new System.Collections.Generic.List<NowTextRun>();

        /// <summary>A caption under a rect, at the standard gap. Returns the rect it drew into.</summary>
        internal static NowRect CaptionUnder(NowRect subject, string text)
        {
            NowRect at = new NowRect(subject.x, subject.y + subject.height + 4f, subject.width, 28f);
            Caption(at, text);
            return at;
        }
    }
}
