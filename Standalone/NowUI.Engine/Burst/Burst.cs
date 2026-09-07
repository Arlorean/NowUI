// Mirrors Unity.Burst: BurstCompileAttribute, BurstDiscardAttribute, FloatMode and FloatPrecision.
//
// Governed by Docs/Standalone/StandaloneCoreDesign.md section 3.9. NowUI's use is exactly two attribute
// applications (NowLottieBurstTessellator and NowManagedFontBaker, per
// Docs/Standalone/UnityDependencyInventory.md section A row "Unity.Burst.BurstCompile").
//
// WHY the attribute is a pure no-op and not a code path: there is no Burst compiler in the standalone build, so
// every [BurstCompile] type runs as ordinary IL. That is the same code Unity runs with Burst disabled, and design
// section 3.9 notes NowSdfBakeJob uses only plain IEEE operations (dot, saturate, min, sqrt and the float2/float4
// operators) in Burst's default non-fast-math mode, so the bake output is bit-identical either way.

using System;

namespace Unity.Burst
{
    /// <summary>
    /// Mirrors <c>Unity.Burst.BurstCompileAttribute</c>. Declarative only.
    /// </summary>
    /// <remarks>
    /// A class rather than a sealed class, and non-inherited with <c>AllowMultiple = false</c>, to match Unity's
    /// declaration so anything that reflects over it sees the same shape.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Assembly,
        AllowMultiple = false, Inherited = false)]
    public class BurstCompileAttribute : Attribute
    {
        public BurstCompileAttribute()
        {
        }

        public BurstCompileAttribute(FloatPrecision floatPrecision, FloatMode floatMode)
        {
            FloatPrecision = floatPrecision;
            FloatMode = floatMode;
        }

        /// <summary>Requested floating-point optimisation mode. Ignored: the shim always runs plain IEEE IL.</summary>
        public FloatMode FloatMode { get; set; }

        /// <summary>Requested precision for transcendental functions. Ignored, as above.</summary>
        public FloatPrecision FloatPrecision { get; set; }

        /// <summary>Whether Unity should block on the Burst compile. Meaningless without a Burst compiler.</summary>
        public bool CompileSynchronously { get; set; }

        /// <summary>Whether Burst should elide container safety checks. Meaningless here.</summary>
        public bool DisableSafetyChecks { get; set; }

        /// <summary>Whether Burst should skip this member entirely. Meaningless here.</summary>
        public bool DisableDirectCall { get; set; }
    }

    /// <summary>
    /// Mirrors <c>Unity.Burst.BurstDiscardAttribute</c>. In Unity this deletes the method body from Burst-compiled
    /// code; here the method simply always runs, which is the managed behaviour Unity falls back to when Burst is
    /// disabled.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class BurstDiscardAttribute : Attribute
    {
    }

    /// <summary>Mirrors <c>Unity.Burst.FloatMode</c>; numeric values match Unity.</summary>
    public enum FloatMode
    {
        Default = 0,
        Strict = 1,
        Deterministic = 2,
        Fast = 3,
    }

    /// <summary>Mirrors <c>Unity.Burst.FloatPrecision</c>; numeric values match Unity.</summary>
    public enum FloatPrecision
    {
        Standard = 0,
        High = 1,
        Medium = 2,
        Low = 3,
    }
}
