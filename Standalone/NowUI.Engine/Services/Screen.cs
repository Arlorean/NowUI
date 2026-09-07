// Mirrors UnityEngine.Screen (plus the UnityEngine.Resolution / RefreshRate value types it reports).
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.7 member list, §4.4 NowScreenInfo).
// Behaviour spec: Docs/Standalone/UnityDependencyInventory.md (§A.3 "UnityEngine.Screen", hazard H.13 y-flip).
using System.Runtime.InteropServices;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// A display refresh rate as the exact rational Unity reports (59.94 Hz is 60000/1001, not a rounded 60).
    /// </summary>
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct RefreshRate
    {
        public uint numerator;
        public uint denominator;

        /// <summary>The rate in hertz. A zero denominator yields NaN, exactly as the division would.</summary>
        public double value => (double)numerator / denominator;
    }

    /// <summary>A screen resolution and the refresh rate it runs at.</summary>
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Resolution
    {
        // Unity's private field names, so the layout matches what a serializer would see.
        private int m_Width;
        private int m_Height;
        private RefreshRate m_RefreshRate;

        public int width
        {
            get => m_Width;
            set => m_Width = value;
        }

        public int height
        {
            get => m_Height;
            set => m_Height = value;
        }

        public RefreshRate refreshRateRatio
        {
            get => m_RefreshRate;
            set => m_RefreshRate = value;
        }

        /// <summary>
        /// Unity's format, built with the invariant culture: its own output does not become "59,94Hz" under a German
        /// locale, and neither may the shim's.
        /// </summary>
        public override string ToString()
        {
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            return m_Width.ToString(invariant) + " x " + m_Height.ToString(invariant) +
                   " @ " + m_RefreshRate.value.ToString(invariant) + "Hz";
        }
    }

    /// <summary>
    /// The drawing surface's geometry. Every member re-reads <c>NowRuntime.host.screen</c> on access rather than
    /// caching (design §3.7), so a browser resize or a device rotation is visible on the very next read without any
    /// notification plumbing.
    /// </summary>
    public static class Screen
    {
        private static bool s_FullScreen;
        private static bool s_HasOrientationOverride;
        private static ScreenOrientation s_Orientation;

        /// <summary>Surface width in physical pixels.</summary>
        public static int width => NowRuntime.host.screen.width;

        /// <summary>Surface height in physical pixels.</summary>
        public static int height => NowRuntime.host.screen.height;

        /// <summary>
        /// Dots per inch, 96 on a plain desktop host. Unity reports 0 when it does not know, and core code handles
        /// that; a host that does not know should report 0 here for the same reason.
        /// </summary>
        public static float dpi => NowRuntime.host.screen.dpi;

        /// <summary>
        /// The usable rectangle inside the surface, <b>bottom-left origin</b> - Unity's convention, which
        /// <c>NowScreen</c> assumes when it flips the top inset (hazard H.13). Full-rect when nothing is cut out.
        /// </summary>
        public static Rect safeArea => NowRuntime.host.screen.safeArea;

        /// <summary>
        /// Whether the surface covers the display. False by default: a standalone host draws into a window or a
        /// browser canvas, not an exclusive-fullscreen display. Settable as in Unity; a host that really can go
        /// fullscreen sets it after doing so.
        /// </summary>
        public static bool fullScreen
        {
            get => s_FullScreen;
            set => s_FullScreen = value;
        }

        /// <summary>
        /// The surface orientation. Derived from the host's own geometry unless a host assigns one, so a resize that
        /// turns a portrait canvas landscape is reflected without the host having to say so. Assigning a value pins it
        /// (Unity allows the assignment on handhelds); there is no way to un-pin it, which matches Unity, where the
        /// assignment is likewise one-way.
        /// </summary>
        public static ScreenOrientation orientation
        {
            get
            {
                if (s_HasOrientationOverride)
                    return s_Orientation;

                NowScreenInfo info = NowRuntime.host.screen;
                return info.width >= info.height ? ScreenOrientation.LandscapeLeft : ScreenOrientation.Portrait;
            }
            set
            {
                s_HasOrientationOverride = true;
                s_Orientation = value;
            }
        }

        /// <summary>
        /// The current resolution: the host's surface size at a nominal 60 Hz. The host-services contract carries no
        /// refresh rate (§4.4), so inventing a measured one here would be a fabricated reading rather than a shim.
        /// </summary>
        public static Resolution currentResolution
        {
            get
            {
                NowScreenInfo info = NowRuntime.host.screen;
                Resolution resolution = default;
                resolution.width = info.width;
                resolution.height = info.height;
                resolution.refreshRateRatio = new RefreshRate { numerator = 60u, denominator = 1u };
                return resolution;
            }
        }
    }
}
