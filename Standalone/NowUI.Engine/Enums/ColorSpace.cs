// Mirrors UnityEngine.ColorSpace for the NowUI standalone build; spec: Docs/Standalone/UnityValueTypeSemantics.md (§14).

namespace UnityEngine
{
    /// <summary>Colour space of the project / a texture. Numeric values match Unity exactly (spec §14).</summary>
    public enum ColorSpace
    {
        Uninitialized = -1,
        Gamma = 0,
        Linear = 1,
    }
}
