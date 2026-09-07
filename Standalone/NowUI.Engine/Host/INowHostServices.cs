// The host-service contract for the NowUI standalone build: everything the shim cannot compute on its own and a host
// (a desktop test harness, a browser page) must supply.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§4.4), with NowScreenInfo from §3.10.
using System;

namespace NowUI.Engine
{
    /// <summary>
    /// Screen geometry, in pixels, with a bottom-left origin for <see cref="safeArea"/> exactly as
    /// <c>UnityEngine.Screen.safeArea</c> uses. Re-read from the host on every access, so a browser resize is visible
    /// on the next frame without any notification plumbing.
    /// </summary>
    public readonly struct NowScreenInfo
    {
        public readonly int width;
        public readonly int height;

        /// <summary>Dots per inch. 96 on a plain desktop host; the host reports the real value where it knows one.</summary>
        public readonly float dpi;

        /// <summary>The usable rectangle inside the screen, bottom-left origin. Full-rect when nothing is cut out.</summary>
        public readonly UnityEngine.Rect safeArea;

        public NowScreenInfo(int width, int height, float dpi, UnityEngine.Rect safeArea)
        {
            this.width = width;
            this.height = height;
            this.dpi = dpi;
            this.safeArea = safeArea;
        }

        /// <summary>Builds screen info whose safe area is the whole screen, which is the common case.</summary>
        public NowScreenInfo(int width, int height, float dpi)
            : this(width, height, dpi, new UnityEngine.Rect(0f, 0f, width, height))
        {
        }
    }

    /// <summary>
    /// Monotonic wall clock in seconds. A <c>Stopwatch</c> on desktop, <c>performance.now() / 1000</c> in a browser.
    /// Must never go backwards: <c>NowRuntime.BeginFrame</c> derives <c>Time.deltaTime</c> from consecutive reads.
    /// </summary>
    public interface INowClock
    {
        double realtimeSeconds { get; }
    }

    /// <summary>
    /// Where <c>UnityEngine.Debug</c> ends up. <paramref name="context"/> is the object the message is about and may be
    /// null; <paramref name="exception"/> is non-null only for <c>LogType.Exception</c>.
    /// </summary>
    public interface INowLogger
    {
        void Log(UnityEngine.LogType type, string message, Exception exception, UnityEngine.Object context);
    }

    /// <summary>System clipboard. A host with no clipboard reports null instead of implementing this.</summary>
    public interface INowClipboard
    {
        string GetText();

        void SetText(string text);
    }

    /// <summary>
    /// Receives <c>ProfilerMarker</c> scopes. Sampled on hot paths, so an implementation must not allocate and should
    /// treat an unmatched <see cref="End"/> as harmless.
    /// </summary>
    public interface INowProfilerSink
    {
        void Begin(string name);

        void End(string name);
    }

    /// <summary>
    /// One open soft-keyboard session. NowUI mirrors <see cref="text"/> into the focused field while the status is
    /// <c>Visible</c>, treats <c>Done</c> as a submit, and sets <see cref="active"/> false before dropping the session.
    /// </summary>
    public interface INowTouchKeyboardSession
    {
        UnityEngine.TouchScreenKeyboard.Status status { get; }

        string text { get; set; }

        bool active { get; set; }
    }

    /// <summary>
    /// Opens soft keyboards. A null touch keyboard on the host makes <c>TouchScreenKeyboard.isSupported</c> false,
    /// which is the branch every desktop and M1 browser host takes.
    /// </summary>
    public interface INowTouchKeyboard
    {
        INowTouchKeyboardSession Open(
            string text,
            UnityEngine.TouchScreenKeyboardType type,
            bool autocorrection,
            bool multiline,
            bool secure);
    }

    /// <summary>
    /// Backs <c>UnityEngine.Resources</c> and <c>Shader.Find</c>. Paths are Unity's resource paths, e.g.
    /// <c>"NowUI/UIMaterial"</c> and <c>"NowUI/NotoSans"</c>; shader names are Unity's, e.g. <c>"NowUI/UI Rectangle"</c>.
    /// A provider returns the <i>same</i> instance for the same path, because core code caches by reference.
    /// </summary>
    public interface INowResourceProvider
    {
        UnityEngine.Object Load(string path, Type type);

        UnityEngine.Shader FindShader(string name);
    }

    /// <summary>
    /// PNG/JPEG codec behind <c>ImageConversion</c>. A null decoder on the host makes
    /// <c>ImageConversion.LoadImage</c> return false rather than throw.
    /// </summary>
    public interface INowImageDecoder
    {
        /// <summary>
        /// Decodes to tightly packed RGBA32 rows in <b>bottom-up</b> order, which is the order
        /// <c>Texture2D.SetPixels32</c> expects.
        /// </summary>
        bool TryDecode(ReadOnlySpan<byte> encoded, out int width, out int height, out byte[] rgba32BottomUp, out string error);

        /// <summary>Encodes a texture; returns null when the format or the codec is unsupported.</summary>
        byte[] TryEncode(UnityEngine.Texture2D texture, NowImageFormat format, int quality);
    }

    /// <summary>
    /// Everything the shim asks of its host. <c>NowRuntime.host</c> is never null: a host that does not implement a
    /// service reports null for that service and the shim takes its documented degraded path.
    /// </summary>
    public interface INowHostServices
    {
        INowClock clock { get; }

        /// <summary>Re-read on every access, so a resize needs no notification.</summary>
        NowScreenInfo screen { get; }

        /// <summary>Never null.</summary>
        INowLogger logger { get; }

        /// <summary>May be null: no clipboard.</summary>
        INowClipboard clipboard { get; }

        /// <summary>May be null, which reports <c>TouchScreenKeyboard.isSupported == false</c>.</summary>
        INowTouchKeyboard touchKeyboard { get; }

        /// <summary>Never null; an empty provider that returns null for everything is the default.</summary>
        INowResourceProvider resources { get; }

        /// <summary>May be null, which makes <c>ImageConversion.LoadImage</c> return false.</summary>
        INowImageDecoder imageDecoder { get; }

        /// <summary>May be null, which makes remote loads fail fast with a named error.</summary>
        INowFetchProvider fetch { get; }

        UnityEngine.RuntimePlatform platform { get; }

        string persistentDataPath { get; }

        string dataPath { get; }

        /// <summary>Exactly 32 entries, indexed by layer, backing <c>LayerMask.LayerToName</c>/<c>NameToLayer</c>.</summary>
        string[] layerNames { get; }
    }
}
