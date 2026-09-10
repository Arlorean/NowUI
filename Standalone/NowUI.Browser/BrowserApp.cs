using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using NowUI.Engine;
using NowUI.Hosting;
using UnityEngine;

[assembly: SupportedOSPlatform("browser")]

namespace NowUI.Browser
{
    /// <summary>Browser host for the same INowScene contract used by the native CLI.</summary>
    public static partial class BrowserApp
    {
        static BrowserResources resources;
        static BrowserHost host;
        static WebGL2Backend backend;
        static BrowserInput input;
        static INowScene scene;
        static bool failed;

        public static async Task RunAsync()
        {
            try
            {
                using var config = JsonDocument.Parse(File.ReadAllText("/host/scene.json"));
                var settings = config.RootElement;
                string assemblyName = settings.GetProperty("assembly").GetString();
                string typeName = settings.GetProperty("scene").GetString();
                string baseUri = BaseUri();
                resources = BrowserResources.Create();
                host = new BrowserHost(resources);
                host.screen = new NowScreenInfo(CanvasWidth(), CanvasHeight(), (float)(96 * DevicePixelRatio()));
                backend = await WebGL2Backend.CreateAsync("#nowui-canvas");
                NowRuntime.RegisterAssembly(typeof(Now).Assembly);
                var assembly = Assembly.Load(new AssemblyName(assemblyName));
                foreach (var item in settings.GetProperty("assemblies").EnumerateArray())
                    NowRuntime.RegisterAssembly(Assembly.Load(new AssemblyName(item.GetString())));
                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                    if (loaded.GetName().Name.StartsWith("NowUI", StringComparison.Ordinal) || loaded == assembly)
                        NowRuntime.RegisterAssembly(loaded);
                NowRuntime.Initialize(host, backend);
                NowRuntime.colorSpace = ColorSpace.Gamma;
                NowRuntime.isPlaying = true;
                input = await BrowserInput.CreateAsync("#nowui-canvas", (float)DevicePixelRatio(), new Uri(new Uri(baseUri), "nowui-input.js").ToString());
                input.Install();
                host.clipboard = input.Clipboard;
                Type type = assembly.GetType(typeName, throwOnError: true);
                if (type.IsAbstract || !typeof(INowScene).IsAssignableFrom(type))
                    throw new InvalidOperationException("The selected browser scene must implement INowScene: " + typeName);
                scene = (INowScene)Activator.CreateInstance(type);
            }
            catch { Shutdown(); throw; }
        }

        [JSExport]
        public static void Frame(double seconds)
        {
            if (failed || scene == null) return;
            try
            {
                float scale = (float)DevicePixelRatio();
                int width = CanvasWidth(), height = CanvasHeight();
                host.realtimeSeconds = seconds;
                host.screen = new NowScreenInfo(width, height, 96 * scale);
                NowRuntime.BeginFrame();
                try
                {
                    input.Drain(scale);
                    RenderTexture.active = null;
                    backend.ClearRenderTarget(false, true, Color.clear, 1);
                    using (Now.StartUI(scale)) scene.Draw(new NowRect(0, 0, width / scale, height / scale));
                }
                finally { NowRuntime.EndFrame(); }
                if (host.errors > 0) throw new InvalidOperationException("The scene logged an error; see the browser console.");
            }
            catch { failed = true; throw; }
        }

        [JSExport]
        public static void Shutdown()
        {
            input?.Dispose(); input = null;
            try { (scene as IDisposable)?.Dispose(); }
            finally
            {
                scene = null;
                try { if (host != null) NowRuntime.Shutdown(); }
                finally { host?.Dispose(); host = null; resources?.Dispose(); resources = null; }
            }
        }

        [JSImport("host.baseUri", "main.js")] internal static partial string BaseUri();
        [JSImport("host.width", "main.js")] internal static partial int CanvasWidth();
        [JSImport("host.height", "main.js")] internal static partial int CanvasHeight();
        [JSImport("host.scale", "main.js")] internal static partial double DevicePixelRatio();
        [JSImport("host.log", "main.js")] internal static partial void Log(int level, string message);
    }

    internal sealed class BrowserHost : INowHostServices, INowClock, INowLogger, IDisposable
    {
        readonly BrowserFetchProvider transport = new();
        public BrowserHost(INowResourceProvider resources) { this.resources = resources; }
        public int errors { get; private set; }
        public double realtimeSeconds { get; set; }
        public INowClock clock => this;
        public NowScreenInfo screen { get; set; }
        public INowLogger logger => this;
        public INowResourceProvider resources { get; }
        public INowClipboard clipboard { get; set; }
        public INowImageDecoder imageDecoder { get; } = new NowImageDecoder();
        public INowFetchProvider fetch => transport;
        public INowTouchKeyboard touchKeyboard => null;
        public RuntimePlatform platform => RuntimePlatform.WebGLPlayer;
        public string persistentDataPath => "/tmp";
        public string dataPath => "/project/Assets";
        public string[] layerNames => Array.Empty<string>();
        public void Log(LogType type, string message, Exception exception, UnityEngine.Object context)
        {
            int level = type is LogType.Error or LogType.Assert or LogType.Exception ? 2 : type == LogType.Warning ? 1 : 0;
            if (level == 2) errors++;
            BrowserApp.Log(level, exception == null ? message : message + "\n" + exception);
        }
        public void Dispose() => transport.Dispose();
    }
}
