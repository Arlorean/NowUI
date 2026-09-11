// Mirrors UnityEngine.HideFlags for the NowUI standalone build.
// Governed by StandaloneCoreDesign.md §3.2 ("Enums/HideFlags.cs"); values verified against
// UnityEngine.CoreModule.dll from Unity 6000.4.0f1.
//
// NowUI sets hideFlags on the Material/Mesh/Texture2D instances it creates (inventory §A.4), and the shim's
// UnityEngine.Object stores the value verbatim, so the numbers have to be Unity's.

using System;

namespace UnityEngine
{
    /// <summary>
    /// Bits controlling whether an object is hidden, saved, or unloaded (design §3.2).
    /// <para>
    /// The two composite members are Unity's own and are deliberately *not* the union of everything:
    /// <c>DontSave = 52</c> is <c>DontSaveInEditor | DontSaveInBuild | DontUnloadUnusedAsset</c> (4|16|32) and
    /// <c>HideAndDontSave = 61</c> is that plus <c>HideInHierarchy | NotEditable</c> (1|8) — note it does
    /// <em>not</em> include <c>HideInInspector</c> (2), which is why 61 rather than 63.
    /// </para>
    /// </summary>
    [Flags]
    public enum HideFlags
    {
        /// <summary>Visible in the hierarchy, saved, unloaded with unused assets.</summary>
        None = 0,
        HideInHierarchy = 1,
        HideInInspector = 2,
        DontSaveInEditor = 4,
        NotEditable = 8,
        DontSaveInBuild = 16,
        DontUnloadUnusedAsset = 32,
        DontSave = 52,
        HideAndDontSave = 61,
    }
}
