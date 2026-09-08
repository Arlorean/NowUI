// INowHostServices over the browser: performance.now() for the clock, the canvas for the screen, console for the log.
//
// It is DefaultHostServices with seven substitutions, and every one of them is a place where "the browser" and "a
// desktop process" genuinely differ:
//
//   clock      -> performance.now() / 1000. Monotonic by specification, which is what INowClock requires; the
//                 Stopwatch the default host uses does exist under wasm, but the browser's own timeline is the one
//                 requestAnimationFrame schedules against, so the frame delta should be measured on it.
//   screen     -> the canvas' drawing-buffer size, polled once per frame (see `Poll`).
//   logger     -> console.log / warn / error, so a page's diagnostics land where a browser developer looks.
//   resources  -> the fetched fixtures (WebResourceProvider). Without this the material and font loads fail and
//                 nothing draws at all - the first finding in Docs/Standalone/M2-Scouting.md section 6.
//   fetch      -> WebFetchProvider, over fetch() + a ReadableStream reader.
//   decoder    -> WebImageDecoder: the browser's codec, run AHEAD of the synchronous TryDecode by the fetch
//                 provider, with a managed PNG decoder underneath for bytes that never went through a fetch.
//   platform   -> RuntimePlatform.WebGLPlayer, which is what a browser build reports and what DefaultHostServices
//                 deliberately does not (its comment says a browser host sets it explicitly).
//
// Everything else - the layer table, and the two path properties, for whatever they are worth here - is delegated to
// DefaultHostServices, exactly as the test host does.
//
// Design: Docs/Standalone/StandaloneCoreDesign.md section 4.4; Docs/Standalone/M2-ShaderPort.md section 8.6 (device
// pixel ratio) and section 6 (colour space).
using System;
using System.Runtime.InteropServices.JavaScript;
using NowUI.Engine;
using UnityEngine;

namespace NowUI.Web
{
    /// <summary>
    /// The JS functions the page exposes to the runtime. Registered by <c>setModuleImports('main.js', ...)</c> in
    /// <c>wwwroot/main.js</c>, so no <c>JSHost.ImportAsync</c> is needed for them.
    /// </summary>
    internal static partial class BrowserInterop
    {
        /// <summary>Milliseconds since the page started, monotonic. <c>performance.now()</c>.</summary>
        [JSImport("host.now", "main.js")]
        internal static partial double Now();

        /// <summary>The canvas' drawing-buffer width in device pixels (<c>canvas.width</c>, not the CSS width).</summary>
        [JSImport("host.canvasWidth", "main.js")]
        internal static partial int CanvasWidth();

        /// <summary>The canvas' drawing-buffer height in device pixels.</summary>
        [JSImport("host.canvasHeight", "main.js")]
        internal static partial int CanvasHeight();

        /// <summary><c>window.devicePixelRatio</c>. Reported, not applied - see <see cref="WebHostServices.Poll"/>.</summary>
        [JSImport("host.devicePixelRatio", "main.js")]
        internal static partial double DevicePixelRatio();

        /// <summary>The document's base URL, used to build the absolute fixture URLs a wasm HttpClient needs.</summary>
        [JSImport("host.baseUri", "main.js")]
        internal static partial string BaseUri();

        /// <summary>0 log, 1 warning, 2 error. Split in JS rather than here so the browser's own grouping applies.</summary>
        [JSImport("host.log", "main.js")]
        internal static partial void Log(int level, string message);

        /// <summary>
        /// One query parameter of the page URL, by name; empty when absent.
        /// </summary>
        /// <remarks>
        /// A lookup rather than a whole parsed URL, so the managed side owns which flags exist and the JS side
        /// stays a single <c>URLSearchParams.get</c>. Used for <c>?scene=</c>, which selects a parity scene.
        /// </remarks>
        [JSImport("host.queryParam", "main.js")]
        internal static partial string QueryParam(string name);

        /// <summary>
        /// Pins the canvas to an exact CSS size, so a parity capture is the size Unity's reference render was.
        /// </summary>
        /// <remarks>
        /// The drawing buffer becomes this times the effective device pixel ratio, which at <c>?dpr=2</c> is
        /// exactly the Unity harness' 2x capture. Without it the canvas fills the window, and a comparison would
        /// be measuring whatever size the window happened to be.
        /// </remarks>
        [JSImport("host.setCanvasSize", "main.js")]
        internal static partial void SetCanvasSize(int width, int height);
    }

    /// <summary>Monotonic clock over <c>performance.now()</c>.</summary>
    public sealed class WebClock : INowClock
    {
        /// <inheritdoc />
        public double realtimeSeconds
        {
            get { return BrowserInterop.Now() / 1000d; }
        }
    }

    /// <summary>
    /// Writes NowUI's log messages to the browser console, mapping Unity's five <see cref="LogType"/> values onto the
    /// console's three levels the way a browser developer would expect (Error/Assert/Exception are errors).
    /// </summary>
    public sealed class WebConsoleLogger : INowLogger
    {
        /// <inheritdoc />
        public void Log(LogType type, string message, Exception exception, UnityEngine.Object context)
        {
            int level;
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    level = 2;
                    break;
                case LogType.Warning:
                    level = 1;
                    break;
                default:
                    level = 0;
                    break;
            }

            string line = "[NowUI] " + (message ?? "");
            if (context != null)
                line += " (context: " + context.name + ")";
            if (exception != null)
                line += Environment.NewLine + exception;

            BrowserInterop.Log(level, line);
        }
    }

    /// <summary>Host services for NowUI running on a browser canvas.</summary>
    public sealed class WebHostServices : INowHostServices
    {
        private readonly DefaultHostServices m_Defaults = new DefaultHostServices();
        private readonly INowClock m_Clock = new WebClock();
        private readonly INowLogger m_Logger = new WebConsoleLogger();
        private readonly INowResourceProvider m_Resources;
        private readonly string m_BaseUri;

        private NowScreenInfo m_Screen;
        private float m_DevicePixelRatio = 1f;

        /// <summary>Builds the host over an already-fetched resource provider and takes a first screen reading.</summary>
        public WebHostServices(INowResourceProvider resources, string baseUri)
        {
            if (resources == null)
                throw new ArgumentNullException(nameof(resources));

            m_Resources = resources;
            m_BaseUri = baseUri ?? "";
            Poll();
        }

        /// <inheritdoc />
        public INowClock clock
        {
            get { return m_Clock; }
        }

        /// <summary>
        /// The canvas geometry as of the last <see cref="Poll"/>.
        /// </summary>
        /// <remarks>
        /// A DEVIATION FROM THE CONTRACT, and a deliberate one. <see cref="INowHostServices.screen"/> is documented
        /// "re-read from the host on every access, so a browser resize is visible on the next frame without any
        /// notification plumbing" - and on a desktop host, where the read is a field, that is free. Here every read
        /// would be a managed-to-JS call, and <c>Now.StartUI</c> alone reads width and height four times before the
        /// first vertex exists. It is also not merely a cost: a canvas that resizes between two reads inside one frame
        /// would give the projection and the input surface different sizes. So the reading is taken once per frame by
        /// <see cref="Poll"/>, which the frame loop calls immediately before <c>NowRuntime.BeginFrame</c>. The
        /// contract's guarantee - a resize is visible on the next frame, with no notification plumbing - is kept
        /// exactly; what changes is that it becomes visible at a defined instant rather than an arbitrary one.
        /// </remarks>
        public NowScreenInfo screen
        {
            get { return m_Screen; }
        }

        /// <inheritdoc />
        public INowLogger logger
        {
            get { return m_Logger; }
        }

        /// <summary>
        /// The browser clipboard, installed by <see cref="WebInput"/> at start-up; null until then, which is a
        /// legal answer - the shim's <c>GUIUtility.systemCopyBuffer</c> is null-safe in both directions.
        /// </summary>
        /// <remarks>
        /// Settable, and this is the designated seam rather than replacing <c>NowClipboard.setText</c> /
        /// <c>getText</c> directly: it needs no change under <c>Assets/</c> and it keeps the IMGUI path consistent.
        /// <para>The shape cannot be satisfied faithfully. <see cref="INowClipboard.GetText"/> is synchronous with
        /// no way to say "not yet", and <c>navigator.clipboard.readText()</c> returns a Promise gated on a
        /// permission prompt and on transient user activation. <see cref="WebInput"/>'s implementation answers from
        /// a cache filled by the DOM <c>paste</c> event; see its remarks and M2-InputContract.md section 6.</para>
        /// </remarks>
        public INowClipboard clipboard { get; set; }

        /// <summary>
        /// Null, so <c>TouchScreenKeyboard.isSupported</c> is false - the correct answer for a desktop browser and
        /// the wrong one for a phone, where a soft keyboard would need the same hidden editable element that IME
        /// composition needs. Out of scope, and named rather than half-implemented.
        /// </summary>
        public INowTouchKeyboard touchKeyboard
        {
            get { return null; }
        }

        /// <inheritdoc />
        public INowResourceProvider resources
        {
            get { return m_Resources; }
        }

        /// <summary>
        /// The image decoder, installed by <c>Program.cs</c> at start-up; null until then, and null for good if the
        /// page's fetch bridge did not come up.
        /// </summary>
        /// <remarks>
        /// Settable for the same reason <see cref="clipboard"/> is: the service needs the runtime to exist before
        /// it can be built, and the host has to exist before the runtime. A null decoder is a legal answer - it
        /// makes <c>ImageConversion.LoadImage</c> return false rather than throw.
        /// <para>The shape of this one is the awkward part of the whole browser port and it is worth stating where
        /// a reader will find it. <see cref="INowImageDecoder.TryDecode"/> is SYNCHRONOUS; every browser decode API
        /// is asynchronous, and a wasm page has one thread, so blocking on a promise is not slow, it deadlocks. The
        /// resolution is to decode AHEAD of the call - <see cref="WebFetchProvider"/> pre-decodes an image response
        /// before it reports the transfer complete - with a managed PNG decoder underneath for bytes that never
        /// went through a fetch. <see cref="WebImageDecoder"/>'s header says exactly which inputs decode and which
        /// are refused.</para>
        /// </remarks>
        public INowImageDecoder imageDecoder { get; set; }

        /// <summary>
        /// The browser fetch provider, installed by <c>Program.cs</c> at start-up; null until then.
        /// </summary>
        /// <remarks>
        /// <see cref="INowFetchProvider"/> is already browser-shaped - its own header says the contract "maps 1:1
        /// onto fetch() + ReadableStream in a browser" - and <see cref="WebFetchProvider"/> is that mapping: one
        /// <c>fetch()</c>, one <c>ReadableStream</c> reader, and each chunk handed to the sink as it arrives so a
        /// byte cap can abort the transfer by returning false. A null provider is a legal answer and makes remote
        /// loads fail fast with a named error.
        /// <para>What a browser cannot do, and what the provider therefore reports rather than fakes: a redirect is
        /// invisible to script when the caller asks to follow it itself, and a cross-origin failure is one opaque
        /// message covering CORS, DNS, refused connections and mixed content alike. Both are in
        /// <see cref="WebFetchProvider"/>'s header.</para>
        /// </remarks>
        public INowFetchProvider fetch { get; set; }

        /// <summary>WebGLPlayer, which is what a Unity browser build reports.</summary>
        public RuntimePlatform platform
        {
            get { return RuntimePlatform.WebGLPlayer; }
        }

        /// <summary>
        /// The wasm in-memory filesystem, which is NOT persistent: it is discarded on reload. Reported honestly rather
        /// than mapped onto something that looks durable; a browser host that needs persistence has to mount IDBFS,
        /// and nothing in NowUI's core writes here during a frame.
        /// </summary>
        public string persistentDataPath
        {
            get { return "/nowui"; }
        }

        /// <summary>The document base URL. There is no data directory in a browser; this is the nearest true answer.</summary>
        public string dataPath
        {
            get { return m_BaseUri; }
        }

        /// <summary>Unity's default 32-entry layer table, delegated to <see cref="DefaultHostServices"/>.</summary>
        public string[] layerNames
        {
            get { return m_Defaults.layerNames; }
        }

        /// <summary>The device pixel ratio last observed. Reported for diagnostics; see <see cref="Poll"/>.</summary>
        public float devicePixelRatio
        {
            get { return m_DevicePixelRatio; }
        }

        /// <summary>
        /// Re-reads the canvas size and the device pixel ratio. Called once per frame, before
        /// <c>NowRuntime.BeginFrame</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Screen.width</c>/<c>Screen.height</c> report the canvas' DRAWING BUFFER size, which is what
        /// <c>gl.viewport(0, 0, drawingBufferWidth, drawingBufferHeight)</c> covers, so the ortho projection
        /// <c>Now.StartUI</c> builds maps one NowUI unit onto one drawing-buffer pixel. Per
        /// M2-ShaderPort.md section 8.6 slice 1 pins the ratio at 1 - <c>main.js</c> sets
        /// <c>canvas.width = cssWidth</c> - so the drawing buffer and the CSS box are the same size and there is one
        /// fewer variable while the first frame is being made to appear. Turning the ratio on later is a change in
        /// <c>main.js</c> alone: this code already reads whatever <c>canvas.width</c> says.
        /// </para>
        /// <para>
        /// dpi is reported as 96 x devicePixelRatio, the conventional CSS reference. It feeds
        /// <c>NowScreen.recommendedUIScale</c> only; the frame loop passes <c>Now.StartUI(1f)</c>, so nothing in slice
        /// 1 depends on it.
        /// </para>
        /// </remarks>
        public void Poll()
        {
            int width = BrowserInterop.CanvasWidth();
            int height = BrowserInterop.CanvasHeight();
            double ratio = BrowserInterop.DevicePixelRatio();

            // A zero-sized canvas (a hidden tab, a collapsed container) would make the ortho projection degenerate and
            // Now.StartUI divide by it. One pixel is the smallest lie that keeps a frame well-formed.
            if (width < 1)
                width = 1;
            if (height < 1)
                height = 1;

            m_DevicePixelRatio = ratio > 0d ? (float)ratio : 1f;
            m_Screen = new NowScreenInfo(width, height, 96f * m_DevicePixelRatio);
        }
    }
}
