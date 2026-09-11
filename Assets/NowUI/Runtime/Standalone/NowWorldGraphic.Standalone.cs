#if NOWUI_STANDALONE
// Standalone stand-in for the host-only NowWorldGraphic (Runtime/NowWorldGraphic.cs),
// which is a MonoBehaviour and is excluded from the standalone compile
// (StandaloneCoreDesign.md §5.3). Core calls NowWorldGraphic.ReleaseCachedMaterial from
// five sites — Now.cs:1063, Now.cs:1087, NowFont.cs:2256, NowGradient.cs:715 and
// NowSdf.cs:3548 — to let world-space hosts drop their per-material clones before the
// source material is destroyed. With no world hosts there is nothing to release, so the
// body is inert and core takes exactly the branch it takes in Unity when no
// NowWorldGraphic instance exists.
//
// The declaration below (namespace, accessibility, signature) is copied from
// Runtime/NowWorldGraphic.cs:1930. The M4 host-identity refactor (§4.9) deletes this file.
using UnityEngine;

namespace NowUI
{
    internal static class NowWorldGraphic
    {
        internal static void ReleaseCachedMaterial(Material source)
        {
        }
    }
}
#endif
