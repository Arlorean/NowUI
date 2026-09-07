// Mirrors UnityEngine.GradientColorKey and UnityEngine.GradientAlphaKey for the NowUI standalone build.
// Spec: Docs/Standalone/GradientCurveSemantics.md §1 (GC §1); design: StandaloneCoreDesign.md §3.6 (`GradientKeys.cs`).
//
// Both are plain structs: no interfaces, no operators, no Equals/GetHashCode overrides (the default ValueType
// implementations apply), and no validation of `time`. Clamping and 16-bit quantisation of the time happen only when a
// key is handed to a Gradient (GC §3.1), never here.

using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>A colour key of a <see cref="Gradient"/>. Field order is <c>color, time</c>; 20 bytes (GC §1).</summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct GradientColorKey
    {
        /// <summary>The colour of the key. Its alpha is carried through the getters but ignored by
        /// <see cref="Gradient.Evaluate"/> — gradient alpha comes from the alpha keys (GC §1).</summary>
        public Color color;

        /// <summary>The key time, nominally in [0,1]. Not validated here (GC §1).</summary>
        public float time;

        public GradientColorKey(Color col, float time)
        {
            color = col;
            this.time = time;
        }

        // Unity declares both a by-value and an `in` overload. The C# 7.2 "better ref kind" tiebreaker makes a
        // by-value argument prefer the first, so both can coexist; the `in` form exists for large-struct call sites.
        public GradientColorKey(in Color col, float time)
        {
            color = col;
            this.time = time;
        }
    }

    /// <summary>An alpha key of a <see cref="Gradient"/>. Field order is <c>alpha, time</c>; 8 bytes (GC §1).</summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct GradientAlphaKey
    {
        /// <summary>The alpha of the key. Not clamped (GC §3.4: nothing is clamped by Blend).</summary>
        public float alpha;

        /// <summary>The key time, nominally in [0,1]. Not validated here (GC §1).</summary>
        public float time;

        public GradientAlphaKey(float alpha, float time)
        {
            this.alpha = alpha;
            this.time = time;
        }
    }
}
