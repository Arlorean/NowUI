// The browser host's entry point: fetch the resources, install the host and the WebGL2 backend, then run one NowUI
// frame per requestAnimationFrame.
//
// The frame is the whole contract, and it is three lines (NowRuntime.cs, "A browser frame is"):
//
//     NowRuntime.BeginFrame();
//     using (Now.StartUI(1f)) DrawScene();
//     NowRuntime.EndFrame();
//
// Everything else in this file is getting to the point where those three lines can run: the resources have to be in
// memory before the first Load (INowResourceProvider is synchronous), and the GL context has to exist before the
// first texture upload. Both are asynchronous in a browser, which is why Main is async and the frame loop only starts
// after it.
//
// What this file draws is chosen by `?area=NAME` and lives in FeatureGallery.cs: one NowUI feature area at a time,
// so a headless capture can ask for exactly one and an unported program's exception takes down only the area that
// asked for it. The default area is the interactive demo (DemoScene.cs), so the page's behaviour with no query
// string is unchanged.
//
// This replaced a `?scene=` path that compiled Assets/NowUIHarness/NowParityScenes.cs by file path. That file does
// not exist in the repository, so this project did not build at all; the frozen Unity tree was not touched to fix
// it, the dead reference was removed instead.
//
// Design: Docs/Standalone/M2-Scouting.md (what a real frame asks the backend for), Docs/Standalone/M2-ShaderPort.md
// (the shader programs behind it), Docs/Standalone/M2-InputContract.md (what the provider expects).
using System;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using NowUI;
using NowUI.Engine;
using NowUI.Web;
using UnityEngine;

await WebApp.RunAsync();

/// <summary>The page's NowUI application: start-up, the frame loop, and the one scene it draws.</summary>
internal static partial class WebApp
{
    /// <summary>
    /// The canvas in <c>wwwroot/index.html</c>. The backend takes the same selector and gets its WebGL2 context from
    /// that element; <c>main.js</c> owns the element's size (see <c>WebHostServices.Poll</c>).
    /// </summary>
    private const string k_CanvasSelector = "#nowui-canvas";

    /// <summary>Where the build put the exported fixtures, relative to the document base URL.</summary>
    private const string k_FixturePath = "Fixtures";

    /// <summary>
    /// The canvas ground the scene is drawn over.
    /// </summary>
    /// <remarks>
    /// Nothing else clears: NowUI itself issues no clear (M2-Scouting.md section 3 records the whole frame, and
    /// there is no ClearRenderTarget in it), and the WebGL2 context is created with <c>alpha: false</c>, so an
    /// uncleared drawing buffer is opaque black. The quick-start panel is <c>Color(0, 0, 0, 0.8f)</c>, which over
    /// black composites to black - the panel draws correctly and is invisible. Verified rather than reasoned:
    /// with a non-black ground injected at runtime, the panel's interior read back as exactly
    /// <c>ground * 0.2</c>, which is the blend working.
    /// <para>The value is the mid tone of the Unity reference render's own background gradient
    /// (Docs/media/readme/quick-start-score.png samples (26,36,61) at the left edge and (14,17,29) at the right;
    /// this is (20,27,46), what that gradient reads under the panel). Flat rather than a gradient, because a
    /// gradient would need the unported <c>NowUI/UI Gradient</c> program. Gamma, like every other colour here, so
    /// the component values go to <c>gl.clearColor</c> unconverted.</para>
    /// </remarks>
    private static readonly Color k_Ground = new Color(20f / 255f, 27f / 255f, 46f / 255f, 1f);

    private static WebHostServices s_Host;
    private static INowRenderBackend s_Backend;
    private static WebInput s_Input;
    private static bool s_Failed;

    /// <summary>
    /// The one feature area this page is showing. Never null once start-up succeeded.
    /// </summary>
    /// <remarks>
    /// <c>?area=NAME</c>, defaulting to the interactive demo. One area at a time is the whole point: an unported
    /// shader throws by name inside <c>WebGL2Backend.DrawMesh</c> and latches the frame loop off, so a combined
    /// scene would let one unsupported primitive erase the evidence for every other feature in the frame.
    /// </remarks>
    private static NowGalleryArea s_Area;

    /// <summary>Whether the gallery draws its header strip. Off with <c>?chrome=0</c>, for a clean comparison capture.</summary>
    private static bool s_Chrome = true;

    // The areas, and with them every piece of state the page has, live in FeatureGallery.cs and its GalleryAreas.*
    // partials. This file's job ends at "a NowUI frame runs in the browser"; what that frame draws is a separate
    // question with a separate answer.

    /// <summary>
    /// Fetches the fixtures, brings up the runtime, and hands the frame loop to the browser. Returns as soon as the
    /// first frame has been requested; the .NET runtime stays alive because the wasm host keeps it alive between
    /// callbacks.
    /// </summary>
    internal static async Task RunAsync()
    {
        try
        {
            // `?spike=1` runs the M3 W1 transport spike INSTEAD of the gallery, and returns without starting a
            // frame loop. It is here rather than in its own page because a [JSImport] needs a .NET runtime that has
            // already booted, and this is the one place in the repository where one has; nothing below this line
            // runs in that mode, so the gallery's behaviour with any other query string is unchanged.
            // See Docs/Standalone/M3-Spec.md section 9 (W1) and Standalone/NowUI.Bridge/BridgeInterop.cs.
            // `?spike=verify` skips the timing loops, because that is the mode a headless capture can reach:
            // --virtual-time-budget, the only wait --dump-dom and --screenshot have, freezes the clock during
            // synchronous JavaScript. See TransportSpike.RunAsync's remarks.
            string spike = BrowserInterop.QueryParam("spike");
            if (spike == "1" || spike == "verify")
            {
                await NowUI.Bridge.TransportSpike.RunAsync(measure: spike == "1");
                return;
            }

            string baseUri = BrowserInterop.BaseUri();

            // A wasm HttpClient has no ambient base address, so the fixture URLs are absolutised against the
            // document's own base. GetByteArrayAsync goes through the browser's fetch().
            using HttpClient http = new HttpClient();
            string fixtureRoot = new Uri(new Uri(baseUri), k_FixturePath).ToString();

            WebResourceProvider resources = await WebResourceProvider.CreateAsync(http, fixtureRoot);

            s_Host = new WebHostServices(resources, baseUri);

            // Remote loading. Installed before the backend and before the first frame, because NowMarkdownImages
            // and NowLottieAsset read NowRuntime.host.fetch at the moment a document first asks for an image and
            // cache the failure if it is null.
            //
            // The decoder is installed even when the fetch bridge does not come up: its managed PNG decoder works
            // with no browser and no network at all, so ImageConversion.LoadImage still decodes a local PNG. What a
            // missing bridge costs is the fetch provider and, with it, every format the managed decoder does not
            // read - see WebImageDecoder's header for the exact list.
            var imageDecoder = new WebImageDecoder();
            s_Host.imageDecoder = imageDecoder;

            // ?redirects=follow hands redirect following to the BROWSER. Off by default and that default is a
            // security position, not an omission: NowUI passes followRedirects:false so it can apply its own
            // per-hop URL policy to each Location, and a browser will not show script a Location at all
            // (fetch redirect:'manual' yields an opaque redirect). Following hops silently would quietly delete
            // that policy; refusing them keeps it, at the cost of any URL that redirects.
            NowWebRedirectMode redirects =
                string.Equals(BrowserInterop.QueryParam("redirects"), "follow", StringComparison.OrdinalIgnoreCase)
                    ? NowWebRedirectMode.Follow
                    : NowWebRedirectMode.Manual;

            string fetchError;
            WebFetchProvider fetchProvider = WebFetchProvider.TryCreate(imageDecoder, redirects, out fetchError);

            if (fetchProvider != null)
            {
                s_Host.fetch = fetchProvider;
            }
            else
            {
                BrowserInterop.Log(2, "[NowUI] the fetch bridge is not available, so remote loading is off: " +
                    fetchError + " Local PNG decoding still works.");
            }

            // Which area, and at what size. Both are resolved BEFORE the backend and before the first Poll,
            // because the canvas has to be its final size when the projection and the viewport are first built or
            // frame one describes a different rectangle from every frame after it.
            //
            // Resolved here rather than in JavaScript so that a mistyped name is an error naming every area that
            // does exist, instead of a blank page.
            string requestedArea = BrowserInterop.QueryParam("area");

            s_Area = string.IsNullOrEmpty(requestedArea)
                ? FeatureGallery.Find(FeatureGallery.defaultAreaId)
                : FeatureGallery.Find(requestedArea);

            if (s_Area == null)
                throw new ArgumentException("Unknown area '" + requestedArea + "'. Known: " + FeatureGallery.Names() + ".");

            s_Chrome = BrowserInterop.QueryParam("chrome") != "0";

            // What the remote areas need to build a same-origin URL and to report which host service answered.
            // Same-origin is the point: a test that depends on the public internet measures the internet.
            FeatureGallery.baseUri = baseUri;
            FeatureGallery.fetchProvider = fetchProvider;
            FeatureGallery.imageDecoder = imageDecoder;
            FeatureGallery.crossOriginProbe = BrowserInterop.QueryParam("xorigin") == "1";

            // Animated areas read a frozen clock unless asked otherwise, so a headless capture of the same area
            // twice is the same image. See FeatureGallery.clock for why the frozen value is what it is.
            FeatureGallery.animate = BrowserInterop.QueryParam("animate") == "1";

            // ?trace=N prints the backend's call sequence for the next N frames. Added for a failure that raises
            // nothing: an area whose canvas comes back empty with a clean console. The order of SetRenderTarget,
            // SetViewport, DrawMesh and EndFrame is the evidence that separates "nothing was drawn" from "it was
            // drawn somewhere else".
            int traceFrames;
            if (int.TryParse(BrowserInterop.QueryParam("trace"), out traceFrames) && traceFrames > 0)
                WebGL2Backend.traceFrames = traceFrames;

            // `?sdftext=1` adds a GLYPH node to the SDF area. Off by default, and the flag survives the port
            // that made every other cell on that page draw: an unported program throws out of the FRAME FLUSH
            // rather than at the call, so one failing cell erases the evidence for all the others. The glyph
            // path is the only one on that page that samples a font atlas as its distance field, so it is the
            // only one that could still reach a program this backend does not have. (It replaces `?sdfprobe=1`,
            // which existed to photograph the refusal that no longer happens.)
            FeatureGallery.sdfText = BrowserInterop.QueryParam("sdftext") == "1";

            // Render-target modes. `?rt=1` draws the frame through a full-canvas render texture and blits it
            // back, which must produce an image identical to `?rt=0`; `?rtcheck=1` runs the backend's
            // render-target contract suite once and logs it. Both off by default. See RenderTargetSelfTest.cs.
            RenderTargetSelfTest.indirect = BrowserInterop.QueryParam("rt") == "1";
            RenderTargetSelfTest.contract = BrowserInterop.QueryParam("rtcheck") == "1";

            ApplyCanvasSize(s_Area);

            // Before Initialize, because the backend creates its GL context and compiles both programs, and a compile
            // failure should be reported as a start-up failure rather than as a blank canvas on frame one.
            INowRenderBackend backend = await WebGL2Backend.CreateAsync(k_CanvasSelector);
            s_Backend = backend;

            // Gamma, matching the Unity project (ProjectSettings m_ActiveColorSpace: 0) and the shim's own default.
            // Set explicitly rather than relied on: the whole colour path downstream is written for it, and
            // M2-ShaderPort.md section 5 is a list of five WebGL settings that must be left alone to keep it.
            NowRuntime.colorSpace = ColorSpace.Gamma;

            // True is the default and is what a browser wants: the core's play-mode paths (deferred destroy, the
            // runtime rather than edit-time branches) are the ones a running page should take.
            NowRuntime.isPlaying = true;

            // ------------------------------------------------------------------------------------- glyph baker
            //
            // The browser is the one host where the DEFAULT choice is wrong. Everywhere else NowUI prefers the
            // managed baker for TrueType faces, and rightly: it carries no binary dependency and, with Burst
            // compiling its IJobParallelFor, it is fast. Here Burst is an attribute-only shim
            // (Standalone/NowUI.Engine/Burst/Burst.cs), so that same job runs scalar, single-threaded and
            // interpreted - about 19 ms per distinct glyph, paid on the frame that first shows it. The native
            // msdf plugin this host links (see NowUI.Web.csproj) does the same work at about 4 ms per glyph.
            //
            // This is a PREFERENCE, not a requirement. NowFontCompiler.DynamicSession.TryCreate falls back to the
            // managed baker when the plugin is missing or refuses, so a build without the native object still
            // draws every glyph - just slowly. Fail-open is the point: a host that cannot render text is worse
            // than a host that renders it late.
            //
            // `?baker=managed` forces the managed path back on, in the SAME binary, which is what makes the two
            // rasterizers comparable: same fonts, same sizes, same atlas, one flag apart.
            string baker = BrowserInterop.QueryParam("baker");
            NowFontCompiler.forceManagedCompiler = baker == "managed";
            NowFontCompiler.forceNativeCompiler = baker != "managed";
            BrowserInterop.Log(0, "[NowUI] glyph baker: " + (baker == "managed" ? "managed (forced)" : "native (preferred, managed fallback)"));

            NowRuntime.Initialize(s_Host, backend);

            // After Initialize, because Install writes NowUI statics, and before the first frame, because
            // Now.StartUI reaches the provider only through NowInput.defaultProvider - a frame drawn before this
            // would sample NowScreenInputProvider, read Unity input that is compiled out, and be inert with no
            // error to show for it (M2-InputContract.md section 0).
            s_Input = await WebInput.CreateAsync(k_CanvasSelector, s_Host.devicePixelRatio);
            s_Host.clipboard = s_Input.clipboard;
            s_Input.Install();

            BrowserInterop.Log(0, "[NowUI] input bridge up: " +
                (s_Input.isMacPlatform ? "macOS conventions (Command)" : "PC conventions (Ctrl)") + ".");

            BrowserInterop.Log(0, "[NowUI] remote loading: fetch=" +
                (s_Host.fetch != null ? "WebFetchProvider, redirects=" + redirects : "OFF") +
                ", decoder=WebImageDecoder (managed PNG here, everything else pre-decoded by the browser " +
                "during a fetch).");

            BrowserInterop.Log(0, "[NowUI] area '" + s_Area.id + "' - " + s_Area.title + ". Expected: " +
                s_Area.expectation);

            // `?bridge=MODE` hands the frame to the JavaScript surface instead of the C# gallery. Everything above
            // this line is unchanged and still runs: the resources, the backend, the input bridge and the clear
            // are the same for a JavaScript-authored UI as for a C# one, which is the point.
            // See BridgeHost.cs and Docs/Standalone/M3-Spec.md sections 5.7 and 9 (W2, W3).
            //
            // `?app=NAME` is W6's: it hands the frame to an author's own file, wwwroot/NAME.js, which imports
            // nowui.js and calls start() at module scope. `?app=app` is the application of section 1. The two
            // selectors sit beside `?area=` and follow its shape - resolved here rather than in JavaScript, so
            // that a mistyped name is an error with a message rather than a blank canvas.
            string appName = BrowserInterop.QueryParam("app");
            string bridgeMode = BrowserInterop.QueryParam("bridge");

            if (!string.IsNullOrEmpty(appName))
                await BridgeHost.StartAppAsync(appName);
            else if (!string.IsNullOrEmpty(bridgeMode))
                await BridgeHost.StartAsync(bridgeMode);

            BrowserInterop.Log(0, "[NowUI] runtime up: " +
                WebResourceProvider.Format(resources.fetchedFileCount) + " fixture files, " +
                WebResourceProvider.Format(resources.fetchedByteCount) + " bytes, canvas " +
                WebResourceProvider.Format(s_Host.screen.width) + "x" +
                WebResourceProvider.Format(s_Host.screen.height) + ".");

            StartFrameLoop();
        }
        catch (Exception e)
        {
            s_Failed = true;
            BrowserInterop.Log(2, "[NowUI] start-up failed, no frame will run." + Environment.NewLine + e);
        }
    }

    /// <summary>
    /// One frame. Called from the page's <c>requestAnimationFrame</c> pump, which is the browser's own frame clock -
    /// there is no other correct source of one, and a timer would tear against compositing.
    /// </summary>
    [JSExport]
    internal static void Frame()
    {
        if (s_Failed)
            return;

        try
        {
            // The canvas size for this frame, read once (see WebHostServices.Poll). Before BeginFrame, so the
            // projection Now.StartUI builds and the viewport the backend sets describe the same rectangle.
            s_Host.Poll();

            NowRuntime.BeginFrame();

            // Establish the back buffer as the bound target, every frame, before anything else can bind one.
            //
            // MEASURED, and the bug it fixes has no symptom other than an empty canvas. NowImmediate.activeTarget
            // starts as `default(NowRenderTarget)` - texture null, WIDTH AND HEIGHT ZERO - and nothing populates
            // it, because this host never binds the back buffer: nowui-gl.js sets the viewport itself inside
            // beginFrame. The first code that binds a render texture (NowEffects' render-to-texture capture, and
            // NowGlass's backdrop) captures that default as "the target to restore", and
            // Graphics.ExecuteCommandBuffer / NowImmediate.Bind then restore it faithfully:
            //
            //     SetRenderTarget(backbuffer)
            //     SetViewport(0, 0, 0x0)          <- a DEGENERATE viewport
            //
            // From there every remaining draw in the frame rasterises nothing. No GL error, no exception, no
            // console output - just a canvas with the clear colour on it. `?area=rendertexture&trace=2` prints
            // exactly that sequence.
            //
            // Assigning null here goes through NowImmediate.SetActive -> ResolveTarget(null) -> BackBuffer(),
            // which reads INowHostServices.screen and therefore carries the real canvas size, so the target that
            // later gets restored is the right one. Cost: one SetRenderTarget and one SetViewport per frame.
            //
            // Fixed HERE rather than in the shim deliberately. NowImmediate.Bind could clamp a zero-sized back
            // buffer to the host's screen and arguably should - a back buffer with no size is not a thing that
            // exists - but that is a change under Standalone/NowUI.Engine, and this host can express the same
            // guarantee without one. The shim's latent defect is reported in Docs/Standalone/M2-FeatureMatrix.md.
            RenderTexture.active = null;

            // Every DOM event the page buffered since the last frame, replayed into the provider in one batch.
            //
            // The position in this method is the contract, not a preference. BeginFrame is what increments
            // Time.frameCount, and NowUIToolkitInputProvider folds its whole event buffer into a single snapshot at
            // the FIRST read of a new frame and clears the one-shot state immediately. So a drain before BeginFrame
            // would land in the previous frame's buffer and be discarded, and a drain after StartUI would land in
            // the next frame's. Between the two is the only place it belongs. Feeding is not reading: nothing here
            // touches NowInput.
            s_Input.Drain(s_Host.devicePixelRatio);

            // Before the clear, so anything the suite scribbles on the back buffer is wiped by it.
            RenderTargetSelfTest.RunContractChecksOnce();

            // Before the clear too, for the opposite reason: with ?rt=1 the clear and the whole scene have to
            // land in the render texture, not on the back buffer.
            bool indirect = RenderTargetSelfTest.BeginIndirect(s_Host.screen.width, s_Host.screen.height);

            // After BeginFrame so the backend is inside a frame, and before StartUI so the scene composites over
            // it. Colour only: no depth buffer is allocated (nowui-gl.js creates the context with depth: false),
            // and the clear ignores the viewport because scissor is disabled.
            s_Backend.ClearRenderTarget(
                clearDepth: false,
                clearColor: true,
                // The gallery paints its own ground over this immediately, so the clear only decides what any
                // pixel neither the gallery nor the area draws over looks like. Kept because an uncleared drawing
                // buffer is undefined, not black.
                //
                // A `?bridge=` page paints nothing of its own and owns the whole surface, so it takes the theme's
                // Background token instead - see BridgeHost.ground.
                color: BridgeHost.active ? BridgeHost.ground : k_Ground,
                depth: 1f);

            // Scale the UI by the device pixel ratio, so a 2x buffer renders the same layout at twice the density
            // rather than at half the size. NowUI measures in drawing-buffer pixels, so the two must agree.
            // The one line `?bridge=` replaces. BridgeHost opens its own Now.StartUI, because section 5.7 puts the
            // Record call OUTSIDE it - the author's draw function runs before NowUI's frame is entered, not
            // inside it.
            if (BridgeHost.active)
                BridgeHost.Frame(s_Host.devicePixelRatio);
            else
                using (Now.StartUI(s_Host.devicePixelRatio))
                    FeatureGallery.Draw(s_Area, Now.screenMask, s_Chrome);

            // The whole of ?rt=1: one blit of the frame's render texture onto the back buffer.
            if (indirect)
                RenderTargetSelfTest.EndIndirect();

            NowRuntime.EndFrame();
        }
        catch (Exception e)
        {
            // Latched: a frame that throws will throw every frame, and sixty identical stack traces a second would
            // bury the first one, which is the only useful one.
            s_Failed = true;
            BrowserInterop.Log(2, "[NowUI] frame failed; the loop is stopped." + Environment.NewLine + e);
        }
    }

    /// <summary>
    /// Sizes the canvas for the selected area.
    /// </summary>
    /// <remarks>
    /// An area that declares a size gets exactly that size in CSS pixels, and the canvas stops following the
    /// window - which is what makes a headless capture reproducible: a screenshot crops the window, so a canvas
    /// still stretched to 100% would be captured at the window's size and rescaled, measuring the compositor
    /// rather than the renderer.
    /// <para><c>?size=WxH</c> overrides it, and <c>?size=window</c> puts the canvas back to filling the window -
    /// which is what the interactive demo wants and therefore what it declares (width and height both zero).</para>
    /// </remarks>
    private static void ApplyCanvasSize(NowGalleryArea area)
    {
        string requested = BrowserInterop.QueryParam("size");

        if (string.Equals(requested, "window", StringComparison.OrdinalIgnoreCase))
            return;

        int width = area.width;
        int height = area.height;

        if (!string.IsNullOrEmpty(requested))
        {
            int separator = requested.IndexOf('x');

            if (separator <= 0 ||
                !int.TryParse(requested.Substring(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out width) ||
                !int.TryParse(requested.Substring(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out height) ||
                width <= 0 || height <= 0)
            {
                throw new ArgumentException("?size must be WxH in CSS pixels, or the word 'window'. Got '" + requested + "'.");
            }
        }

        if (width > 0 && height > 0)
            BrowserInterop.SetCanvasSize(width, height);
    }

    /// <summary>
    /// The scene's state as one line, for driving the input tests from the page.
    /// </summary>
    /// <remarks>
    /// An oracle, not a feature. M2-Scouting.md establishes that headless screenshot capture here is not yet
    /// trustworthy and that <c>gl.readPixels</c> on a live page is; this is the third option, and for the things
    /// input changes it is the exact one - "the click counter went from 3 to 4" is a fact, where "these pixels got
    /// lighter" is an inference. Pixels remain the right oracle for hover and focus rings, which have no managed
    /// state to report.
    /// </remarks>
    [JSExport]
    internal static string DebugState()
    {
        // Through the gallery rather than straight to DemoScene: every area can publish its own line now, and the
        // demo is just the one that publishes none of its own (FeatureGallery.areaState).
        return FeatureGallery.DebugState();
    }

    /// <summary>Where each driveable control was laid out, so a test can address one by name. See DemoScene.DebugRects.</summary>
    [JSExport]
    internal static string DebugRects()
    {
        // The demo's rects, plus the pickers area's closed-field rects. Both are appended unconditionally: the
        // area that is not on screen reports zero-sized rects, which is unambiguous, and a driver that asks for a
        // name the current area does not own gets a rect it cannot click rather than a missing key.
        string demo = DemoScene.DebugRects();
        string pickers = FeatureGallery.PickerRects();

        if (string.IsNullOrEmpty(pickers))
            return demo;

        return string.IsNullOrEmpty(demo) ? pickers : demo + ";" + pickers;
    }

    /// <summary>Asks the page to begin calling <see cref="Frame"/> once per animation frame.</summary>
    [JSImport("host.startFrameLoop", "main.js")]
    private static partial void StartFrameLoop();
}
