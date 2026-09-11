#if NOWUI_STANDALONE
// Standalone stand-in for NowRectTransformProjection, which lives inside the host-only
// Runtime/Input/NowRectTransformInputProvider.cs — that file is excluded from the
// standalone compile (StandaloneCoreDesign.md §5.3) because it projects pointer input
// through a Canvas RectTransform.
//
// The one core caller is NowOverlay.BlockContainsScreenPoint (NowOverlay.cs:1632), which
// already returns early when block.hostRectTransform == null. In the standalone build the
// RectTransform scene stub is always null there, so the call is unreachable in practice
// and returning false keeps the same answer the real projection gives for a null rect.
// WorldToScreenPoint has no core caller today; it is kept so the stand-in's surface matches
// the real type, and its body is the real camera == null branch verbatim.
//
// The declarations below (namespace, accessibility, parameter lists) are copied from
// Runtime/Input/NowRectTransformInputProvider.cs:251, :253 and :262.
// The M4 host-identity refactor (§4.9) deletes this file.
using UnityEngine;

namespace NowUI
{
    internal static class NowRectTransformProjection
    {
        public static Vector2 WorldToScreenPoint(Camera camera, Vector3 worldPoint)
        {
            return new Vector2(worldPoint.x, worldPoint.y);
        }

        public static bool ScreenPointToLocalPointInRectangle(
            RectTransform rectTransform,
            Vector2 screenPoint,
            Camera camera,
            out Vector2 localPoint)
        {
            localPoint = default;
            return false;
        }
    }
}
#endif
