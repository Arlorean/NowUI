#if NOWUI_STANDALONE
// Standalone stand-in for the host-only NowLottieBurstTessellator
// (Runtime/Lottie/NowLottieBurstTessellator.cs), which is excluded from the standalone
// compile (StandaloneCoreDesign.md §5.3) because its Burst jobs and NativeArray scratch
// buffers are a Unity-only acceleration path.
//
// NowLottieRenderer calls TryFill/TryStroke as an opportunistic fast path and falls
// through to the scalar managed tessellator whenever they return false
// (NowLottieRenderer.cs:729 and :804), so returning false here selects the scalar path —
// the same path Unity takes when the Burst attempt declines. InvalidateClip only drops a
// cached clip copy this stand-in never builds (NowLottieRenderer.cs:51).
//
// The declarations below (namespace, accessibility, parameter lists) are copied from
// Runtime/Lottie/NowLottieBurstTessellator.cs:23, :29, :1273, :1320 and :1468.
// The M4 host-identity refactor (§4.9) deletes this file.
using System.Collections.Generic;

namespace NowUI.Internal
{
    internal static class NowLottieBurstTessellator
    {
        public static bool forceScalar;

        public static bool TryFill(
            NowLottieContourSet contours,
            List<NowLottiePolyline> clipPolylines,
            bool clipInvert,
            bool evenOdd,
            in NowLottiePaint paint,
            NowLottieDrawBuffer buffer,
            float aaWidth,
            float gradientSpan,
            float tolerance)
        {
            return false;
        }

        public static bool TryStroke(
            NowLottieContourSet contours,
            float width,
            int cap,
            int join,
            in NowLottiePaint paint,
            NowLottieDrawBuffer buffer,
            float aaWidth,
            float tolerance)
        {
            return false;
        }

        internal static void InvalidateClip(List<NowLottiePolyline> clipPolylines)
        {
        }
    }
}
#endif
