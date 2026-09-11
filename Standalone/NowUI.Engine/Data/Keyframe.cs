// Mirrors UnityEngine.Keyframe for the NowUI standalone build.
// Spec: Docs/Standalone/GradientCurveSemantics.md §4.2 (GC §4.2); design: StandaloneCoreDesign.md §3.6 (`Keyframe.cs`).
//
// The field order and types are load-bearing: Marshal.SizeOf(typeof(Keyframe)) must be 32, which the semantics suite
// asserts. `weightedMode` and `tangentMode` store the raw int, so out-of-range values such as 7 and -1 round-trip
// unchanged through the property, the constructors and AnimationCurve (GC §4.2).

using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>A single key of an <see cref="AnimationCurve"/> (GC §4.2). 32 bytes, sequential layout.</summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Keyframe
    {
        // Unity's declaration order; do not reorder or add fields (32 bytes).
        private float m_Time;
        private float m_Value;
        private float m_InTangent;
        private float m_OutTangent;
        private int m_TangentMode;
        private int m_WeightedMode;
        private float m_InWeight;
        private float m_OutWeight;

        /// <summary>Tangents 0, weights 0, <see cref="WeightedMode.None"/>, tangentMode 0 (GC §4.2).</summary>
        public Keyframe(float time, float value)
        {
            m_Time = time;
            m_Value = value;
            m_InTangent = 0f;
            m_OutTangent = 0f;
            m_TangentMode = 0;
            m_WeightedMode = 0;
            m_InWeight = 0f;
            m_OutWeight = 0f;
        }

        /// <summary>Weights 0, <see cref="WeightedMode.None"/> (GC §4.2).</summary>
        public Keyframe(float time, float value, float inTangent, float outTangent)
        {
            m_Time = time;
            m_Value = value;
            m_InTangent = inTangent;
            m_OutTangent = outTangent;
            m_TangentMode = 0;
            m_WeightedMode = 0;
            m_InWeight = 0f;
            m_OutWeight = 0f;
        }

        /// <summary>
        /// Sets the weights and — this is the surprise — <see cref="WeightedMode.Both"/>, so a key built with this
        /// constructor makes its segments take the weighted Bézier path (GC §4.2, §5.4).
        /// </summary>
        public Keyframe(float time, float value, float inTangent, float outTangent, float inWeight, float outWeight)
        {
            m_Time = time;
            m_Value = value;
            m_InTangent = inTangent;
            m_OutTangent = outTangent;
            m_TangentMode = 0;
            m_WeightedMode = (int)WeightedMode.Both;
            m_InWeight = inWeight;
            m_OutWeight = outWeight;
        }

        public float time
        {
            readonly get => m_Time;
            set => m_Time = value;
        }

        public float value
        {
            readonly get => m_Value;
            set => m_Value = value;
        }

        public float inTangent
        {
            readonly get => m_InTangent;
            set => m_InTangent = value;
        }

        public float outTangent
        {
            readonly get => m_OutTangent;
            set => m_OutTangent = value;
        }

        public float inWeight
        {
            readonly get => m_InWeight;
            set => m_InWeight = value;
        }

        public float outWeight
        {
            readonly get => m_OutWeight;
            set => m_OutWeight = value;
        }

        /// <summary>
        /// The weighted mode, stored as the raw int: undefined values such as 7 and -1 round-trip, and
        /// <see cref="AnimationCurve"/> bit-tests them (GC §4.2, §5.4).
        /// </summary>
        public WeightedMode weightedMode
        {
            readonly get => (WeightedMode)m_WeightedMode;
            set => m_WeightedMode = (int)value;
        }

        /// <summary>
        /// Editor-only tangent mode. Round-trips unchanged through curves; ignored by
        /// <see cref="AnimationCurve.Equals(AnimationCurve)"/> but included in its hash (GC §5.10).
        /// </summary>
        [Obsolete("Use AnimationUtility.SetKeyBroken, AnimationUtility.SetKeyLeftTangentMode or AnimationUtility.SetKeyRightTangentMode instead.")]
        public int tangentMode
        {
            readonly get => m_TangentMode;
            set => m_TangentMode = value;
        }

        /// <summary>Unity's internal alias for <c>tangentMode</c>, used so the shim's own code avoids the obsolete API.</summary>
        internal int tangentModeInternal
        {
            readonly get => m_TangentMode;
            set => m_TangentMode = value;
        }
    }
}
