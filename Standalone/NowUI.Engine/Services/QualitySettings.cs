// Mirrors UnityEngine.QualitySettings.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.7 member list; H.14 "default Gamma"; §4.5 NowRuntime.colorSpace).
// Behaviour spec: Docs/Standalone/UnityDependencyInventory.md (§A.3 "UnityEngine.QualitySettings").
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// The handful of quality knobs NowUI reads. The one that matters is <see cref="activeColorSpace"/>: NowUI's
    /// colour maths and its shaders' colour-space includes must agree with what the framebuffer expects.
    /// </summary>
    public static class QualitySettings
    {
        private static int s_AntiAliasing;
        private static int s_VSyncCount = 1;

        /// <summary>
        /// What the framebuffer expects, from <c>NowRuntime.colorSpace</c> - <b>Gamma by default</b> (design H.14),
        /// matching this project's ProjectSettings, and settable there by a host that renders linear. Read-only here,
        /// as in Unity, where the active colour space is a build setting rather than a runtime toggle.
        /// </summary>
        public static ColorSpace activeColorSpace => NowRuntime.colorSpace;

        /// <summary>
        /// The requested MSAA sample count; 0 (no multisampling) by default. The shim does not act on it - it is
        /// state a host sets and the backend reads when it creates its default framebuffer.
        /// </summary>
        public static int antiAliasing
        {
            get => s_AntiAliasing;
            set => s_AntiAliasing = value;
        }

        /// <summary>
        /// How many display refreshes a frame is held for; 1 (Unity's default) means vsync on. Advisory here too: the
        /// standalone build does not own the presentation loop, the host does.
        /// </summary>
        public static int vSyncCount
        {
            get => s_VSyncCount;
            set => s_VSyncCount = value;
        }
    }
}
