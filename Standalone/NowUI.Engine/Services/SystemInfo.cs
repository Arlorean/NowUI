// Mirrors UnityEngine.SystemInfo.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.7 member list; §4.1 NowRenderCaps, the single source for these).
// Behaviour spec: Docs/Standalone/UnityDependencyInventory.md (§A.3 "UnityEngine.SystemInfo").
using System.Runtime.InteropServices;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// Device capabilities. Every graphics answer is read from <c>NowRuntime.backend.caps</c> on access, never cached,
    /// so a backend swapped in after startup (or a WebGL context restored with different limits) is reflected at once.
    /// </summary>
    public static class SystemInfo
    {
        /// <summary>
        /// The largest texture dimension the backend accepts. Core code clamps atlas sizes against it and guards with
        /// <c>Mathf.Max(1, …)</c>, so a backend reporting 0 degrades rather than divides by zero.
        /// </summary>
        public static int maxTextureSize => NowRuntime.backend.caps.maxTextureSize;

        /// <summary>Whether the backend can render to this format (<c>NowSdf</c> picks R8 versus a fallback on it).</summary>
        public static bool SupportsRenderTextureFormat(RenderTextureFormat format)
        {
            return NowRuntime.backend.caps.SupportsRenderTextureFormat(format);
        }

        /// <summary>Whether the backend can sample a texture stored in this format.</summary>
        public static bool SupportsTextureFormat(TextureFormat format)
        {
            return NowRuntime.backend.caps.SupportsTextureFormat(format);
        }

        /// <summary>
        /// Whether multisampled textures work at all.
        /// </summary>
        /// <remarks>
        /// Unity declares this as an <c>int</c> (a sample count, 0 when unsupported), not a <c>bool</c>. The design
        /// (§3.7) specifies <c>bool</c> and is followed here; the divergence is reported rather than silently
        /// corrected. Nothing in the standalone compile set reads it - the one caller,
        /// <c>NowWorldGlassBackdrop</c>, is on the host-only exclude list (§5.3) - so the narrowing costs nothing
        /// today, but it does mean the standalone public API differs from Unity's at this member.
        /// </remarks>
        public static bool supportsMultisampledTextures => NowRuntime.backend.caps.supportsMultisampledTextures;

        /// <summary>
        /// The largest MSAA sample count usable for <paramref name="desc"/>: the backend's ceiling, rounded down to a
        /// power of two, and never above what the descriptor asked for. 1 means "no multisampling", which is what
        /// Unity returns on a device without it.
        /// </summary>
        public static int GetRenderTextureSupportedMSAASampleCount(RenderTextureDescriptor desc)
        {
            NowRenderCaps caps = NowRuntime.backend.caps;
            if (!caps.supportsMultisampledTextures)
                return 1;

            int requested = desc.msaaSamples;
            if (requested < 1)
                requested = 1;

            int ceiling = caps.maxMsaaSamples;
            if (ceiling < 1)
                ceiling = 1;

            int allowed = requested < ceiling ? requested : ceiling;

            // Sample counts are powers of two; asking for 3 gets 2, exactly as a driver would report.
            int result = 1;
            while ((result << 1) <= allowed)
                result <<= 1;

            return result;
        }

        /// <summary>
        /// Always false. M1 targets WebGL2, which has no compute shaders at all, and NowUI's only use of the flag is
        /// to choose a non-compute path (design §3.7).
        /// </summary>
        public static bool supportsComputeShaders => false;

        /// <summary>Whether the backend can draw instanced geometry.</summary>
        public static bool supportsInstancing => NowRuntime.backend.caps.supportsInstancing;

        /// <summary>The backend's human-readable device name, e.g. the WebGL <c>RENDERER</c> string.</summary>
        public static string graphicsDeviceName => NowRuntime.backend.caps.deviceName;

        /// <summary>Which graphics API the backend speaks.</summary>
        public static Rendering.GraphicsDeviceType graphicsDeviceType => NowRuntime.backend.caps.deviceType;

        /// <summary>Approximate video memory in megabytes, as the backend reports it.</summary>
        public static int graphicsMemorySize => NowRuntime.backend.caps.graphicsMemorySizeMb;

        /// <summary>
        /// The host operating system.
        /// </summary>
        /// <remarks>
        /// The one member here that is <i>not</i> from <c>caps</c>: §4.1's capability struct carries no OS string, and
        /// the OS is a property of the process rather than of the render backend. <c>RuntimeInformation</c> answers it
        /// on every runtime the standalone build targets, wasm included.
        /// </remarks>
        public static string operatingSystem => RuntimeInformation.OSDescription;
    }
}
