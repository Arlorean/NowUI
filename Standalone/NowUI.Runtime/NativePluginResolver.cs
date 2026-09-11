using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NowUI.Internal
{
    /// <summary>Locates the complete desktop plugin next to a portable tool's managed assemblies.</summary>
    internal static class NativePluginResolver
    {
        static readonly Dictionary<string, IntPtr> handles = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly object gate = new object();

        // Standalone-only source: register before either font shaping or native
        // baking first calls P/Invoke, including hosts without a Desktop renderer.
#pragma warning disable CA2255
        [ModuleInitializer]
        internal static void Initialize()
        {
            // Browser plugins are statically linked wasm objects registered by the
            // SDK. Desktop DLL probing is unsupported there and is unnecessary.
            if (OperatingSystem.IsBrowser()) return;
            NativeLibrary.SetDllImportResolver(typeof(NativePluginResolver).Assembly, Resolve);
        }
#pragma warning restore CA2255

        static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (name != "nowui-msdf" && name != "nowui-vg") return IntPtr.Zero;
            string rid = CurrentRid;
            if (rid == null) return IntPtr.Zero;
            lock (gate)
            {
                if (handles.TryGetValue(name, out var cached)) return cached;
                string file = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? name + ".dll"
                    : "lib" + name + (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so");
                // Assembly location also supports a host which references Runtime
                // from another directory; bundled apps use AppContext.BaseDirectory.
                string folder = string.IsNullOrEmpty(assembly.Location) ? null : Path.GetDirectoryName(assembly.Location);
                string path = FindLibrary(AppContext.BaseDirectory, folder, rid, file);
                if (path == null) return IntPtr.Zero; // Preserve normal app-local/system probing for custom hosts.
                // On Windows, search the chosen DLL's directory for its msdf
                // siblings. Unix builds carry $ORIGIN / @loader_path runpaths.
                IntPtr handle = NativeLibrary.Load(path, assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories);
                handles.Add(name, handle);
                paths.Add(name, path);
                // P/Invoke caches entry points, so keep these handles alive for
                // the Runtime assembly's lifetime, including scene hot reloads.
                return handle;
            }
        }

        internal static string CurrentRid => SelectRid(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "unknown",
            RuntimeInformation.ProcessArchitecture);

        internal static string SelectRid(string platform, Architecture architecture) => (platform, architecture) switch
        {
            ("windows", Architecture.X64) => "win-x64",
            ("linux", Architecture.X64) => "linux-x64",
            ("macos", Architecture.X64) => "osx-x64",
            ("macos", Architecture.Arm64) => "osx-arm64",
            _ => null
        };

        internal static string FindLibrary(string applicationDirectory, string assemblyDirectory, string rid, string file)
        {
            if (string.IsNullOrEmpty(rid)) return null;
            string candidate = Path.Combine(applicationDirectory, "runtimes", rid, "native", file);
            if (File.Exists(candidate)) return candidate;
            if (string.IsNullOrEmpty(assemblyDirectory)) return null;
            candidate = Path.Combine(assemblyDirectory, "runtimes", rid, "native", file);
            return File.Exists(candidate) ? candidate : null;
        }

        internal static string LoadedPath(string name)
        {
            lock (gate) return paths.TryGetValue(name, out string path) ? path : null;
        }
    }
}
