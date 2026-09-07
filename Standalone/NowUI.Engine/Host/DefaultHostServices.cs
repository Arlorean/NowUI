// The host services NowRuntime installs when nobody registers a real host.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§4.4 "DefaultHostServices", §3.10 helper-type table).
//
// Everything here is deliberately dependency-free: a Stopwatch, Console, and no resources at all. It is what makes
// `new Texture2D(4, 4)` in a static initialiser safe (design §6.4) - constructing the shim never needs a real host.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NowUI.Engine
{
    /// <summary>Monotonic clock over <see cref="Stopwatch"/>, started when the process first touches the shim.</summary>
    public sealed class NowStopwatchClock : INowClock
    {
        private readonly Stopwatch m_Stopwatch = Stopwatch.StartNew();

        public double realtimeSeconds => m_Stopwatch.Elapsed.TotalSeconds;
    }

    /// <summary>
    /// Writes log messages to the console. Errors, asserts and exceptions go to stderr so a CI run can separate them
    /// without parsing.
    /// </summary>
    public sealed class NowConsoleLogger : INowLogger
    {
        public void Log(UnityEngine.LogType type, string message, Exception exception, UnityEngine.Object context)
        {
            string line = Format(type, message, exception);
            if (type == UnityEngine.LogType.Error || type == UnityEngine.LogType.Assert || type == UnityEngine.LogType.Exception)
                Console.Error.WriteLine(line);
            else
                Console.Out.WriteLine(line);
        }

        private static string Format(UnityEngine.LogType type, string message, Exception exception)
        {
            if (exception == null)
                return "[" + type + "] " + message;
            if (string.IsNullOrEmpty(message))
                return "[" + type + "] " + exception;
            return "[" + type + "] " + message + Environment.NewLine + exception;
        }
    }

    /// <summary>
    /// A clipboard that only remembers what was copied into it. Not installed by default (a default host reports no
    /// clipboard at all); it exists so a test or a headless host can exercise the copy/paste paths.
    /// </summary>
    public sealed class NowMemoryClipboard : INowClipboard
    {
        private string m_Text = "";

        public string GetText()
        {
            return m_Text;
        }

        public void SetText(string text)
        {
            m_Text = text ?? "";
        }
    }

    /// <summary>Resource provider that has nothing: every lookup returns null, which every caller already handles.</summary>
    public sealed class NowEmptyResourceProvider : INowResourceProvider
    {
        /// <summary>Shared instance; the provider is stateless, so one is enough.</summary>
        public static readonly NowEmptyResourceProvider instance = new NowEmptyResourceProvider();

        public UnityEngine.Object Load(string path, Type type)
        {
            return null;
        }

        public UnityEngine.Shader FindShader(string name)
        {
            return null;
        }
    }

    /// <summary>
    /// The default host: a Stopwatch clock, a 1920x1080 96-dpi screen with a full-rect safe area, a console logger, no
    /// clipboard, no touch keyboard, no resources, no image decoder, no fetch, the OS-mapped desktop platform, a temp
    /// directory for persistent data, and Unity's default layer table (design §4.4).
    /// </summary>
    public sealed class DefaultHostServices : INowHostServices
    {
        /// <summary>Unity's out-of-the-box layer table: six named layers (index 3 is empty) then 26 empty slots.</summary>
        private static readonly string[] s_DefaultLayerNames =
        {
            "Default", "TransparentFX", "Ignore Raycast", "", "Water", "UI",
            "", "", "", "", "", "", "", "", "", "",
            "", "", "", "", "", "", "", "", "", "",
            "", "", "", "", "", "",
        };

        private readonly INowClock m_Clock = new NowStopwatchClock();
        private readonly INowLogger m_Logger = new NowConsoleLogger();
        private readonly string[] m_LayerNames;
        private readonly UnityEngine.RuntimePlatform m_Platform;
        private readonly string m_PersistentDataPath;
        private readonly string m_DataPath;

        public DefaultHostServices()
        {
            // A copy per instance: layerNames hands the array out, and a caller that writes into it must not corrupt
            // the table every other DefaultHostServices would report.
            m_LayerNames = (string[])s_DefaultLayerNames.Clone();
            m_Platform = DetectPlatform();
            m_PersistentDataPath = Path.Combine(Path.GetTempPath(), "NowUI");
            m_DataPath = AppContext.BaseDirectory;
        }

        public INowClock clock => m_Clock;

        public NowScreenInfo screen => new NowScreenInfo(1920, 1080, 96f);

        public INowLogger logger => m_Logger;

        public INowClipboard clipboard => null;

        public INowTouchKeyboard touchKeyboard => null;

        public INowResourceProvider resources => NowEmptyResourceProvider.instance;

        public INowImageDecoder imageDecoder => null;

        public INowFetchProvider fetch => null;

        public UnityEngine.RuntimePlatform platform => m_Platform;

        public string persistentDataPath => m_PersistentDataPath;

        public string dataPath => m_DataPath;

        public string[] layerNames => m_LayerNames;

        /// <summary>
        /// Maps the OS onto Unity's desktop <i>player</i> platforms. Deliberately not WebGLPlayer even though the
        /// browser is the eventual target: NowTextEditTests asserts the desktop platform convention, and a browser
        /// host sets <c>WebGLPlayer</c> explicitly through its own <see cref="INowHostServices"/> (design §3.2).
        /// </summary>
        private static UnityEngine.RuntimePlatform DetectPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return UnityEngine.RuntimePlatform.WindowsPlayer;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return UnityEngine.RuntimePlatform.OSXPlayer;
            return UnityEngine.RuntimePlatform.LinuxPlayer;
        }
    }
}
