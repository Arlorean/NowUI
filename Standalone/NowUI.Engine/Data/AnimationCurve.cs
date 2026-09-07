// Mirrors UnityEngine.AnimationCurve for the NowUI standalone build.
// Spec: Docs/Standalone/GradientCurveSemantics.md §5 (GC §5); design: StandaloneCoreDesign.md §3.6 (`AnimationCurve.cs`).
//
// The behaviours that look like bugs but are Unity's, all [verified] in GC §5:
//   * the wrap-mode setters normalise: only Loop (2), PingPong (4) and Default (0) survive; everything else -- Once,
//     Clamp, ClampForever, 3, 16, -1 -- stores as ClampForever (8) (GC §5.7);
//   * WrapMode.Default *evaluates* exactly like Loop while still reading back as 0 (GC §5.7);
//   * ClampForever does not clamp the time: it evaluates a zero-coefficient polynomial anchored at the edge key, so
//     a finite t gives the edge value but +-Infinity gives NaN (0 * Infinity) (GC §5.7);
//   * the Hermite grouping in EvaluateHermite is bit-exact over 22 probe samples and the 1e-4 minimum segment length
//     is pinned by a 1e-7-long segment -- do not "simplify" either (GC §5.6);
//   * AddKey returns -1 and changes nothing on a duplicate time, and MoveKey onto another key's time *removes* the
//     moved key (GC §5.2, §5.3);
//   * Equals covers the wrap modes and ignores tangentMode; GetHashCode does the opposite (GC §5.10).

using System;

namespace UnityEngine
{
    /// <summary>
    /// A cubic curve through a sorted list of <see cref="Keyframe"/>s. Mirrors <c>UnityEngine.AnimationCurve</c>
    /// (GC §5). Not sealed, matching Unity.
    /// </summary>
    public class AnimationCurve : IEquatable<AnimationCurve>
    {
        private Keyframe[] _keys = Array.Empty<Keyframe>();
        private int _count;

        // Stored already normalised (GC §5.7). ClampForever is the default for a new curve.
        private WrapMode _preWrapMode = WrapMode.ClampForever;
        private WrapMode _postWrapMode = WrapMode.ClampForever;

        /// <summary>An empty curve: length 0, both wrap modes ClampForever (GC §5.1).</summary>
        public AnimationCurve()
        {
        }

        /// <summary>Creates a curve from <paramref name="keys"/>, stable-sorted by time; null gives an empty curve.</summary>
        public AnimationCurve(params Keyframe[] keys)
        {
            SetKeysInternal(keys == null ? ReadOnlySpan<Keyframe>.Empty : new ReadOnlySpan<Keyframe>(keys));
        }

        // -------------------------------------------------------------------------------------------------------
        // Storage (GC §5.1)
        // -------------------------------------------------------------------------------------------------------

        /// <summary>The number of keys.</summary>
        public int length => _count;

        /// <summary>
        /// The keys. The getter returns a copy for a non-empty curve (NowUI sorts the returned array in place) and
        /// the same empty instance for an empty one; the setter stable-sorts by time and treats null as empty.
        /// </summary>
        public Keyframe[] keys
        {
            get
            {
                if (_count == 0)
                    return Array.Empty<Keyframe>();
                Keyframe[] result = new Keyframe[_count];
                Array.Copy(_keys, result, _count);
                return result;
            }
            set => SetKeysInternal(value == null ? ReadOnlySpan<Keyframe>.Empty : new ReadOnlySpan<Keyframe>(value));
        }

        /// <summary>Reads one key. Out of range throws <c>IndexOutOfRangeException("GetKey")</c> (GC §5.1).</summary>
        public Keyframe this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new IndexOutOfRangeException("GetKey");
                return _keys[index];
            }
        }

        /// <summary>Copies the keys out; too small a destination throws the same message Unity uses (GC §5).</summary>
        public void GetKeys(Span<Keyframe> keys)
        {
            if (keys.Length < _count)
                throw new ArgumentException("Destination array must be large enough to store the keys", "keys");
            for (int i = 0; i < _count; i++)
                keys[i] = _keys[i];
        }

        /// <summary>Replaces the keys, stable-sorted by time (GC §5.1).</summary>
        public void SetKeys(ReadOnlySpan<Keyframe> keys) => SetKeysInternal(keys);

        /// <summary>Removes every key (GC §5.1).</summary>
        public void ClearKeys() => _count = 0;

        private void SetKeysInternal(ReadOnlySpan<Keyframe> keys)
        {
            EnsureCapacity(keys.Length);
            for (int i = 0; i < keys.Length; i++)
                _keys[i] = keys[i];
            _count = keys.Length;
            StableSortByTime();
        }

        private void EnsureCapacity(int n)
        {
            if (_keys.Length >= n)
                return;
            int capacity = _keys.Length == 0 ? (n < 4 ? 4 : n) : _keys.Length;
            while (capacity < n)
                capacity *= 2;
            Array.Resize(ref _keys, capacity);
        }

        // Insertion sort: stable, so keys sharing a time keep the order they were given (GC §5.1).
        private void StableSortByTime()
        {
            for (int i = 1; i < _count; i++)
            {
                Keyframe key = _keys[i];
                int j = i - 1;
                while (j >= 0 && _keys[j].time > key.time)
                {
                    _keys[j + 1] = _keys[j];
                    j--;
                }
                _keys[j + 1] = key;
            }
        }

        // -------------------------------------------------------------------------------------------------------
        // Wrap modes (GC §5.7)
        // -------------------------------------------------------------------------------------------------------

        /// <summary>Wrap mode below the first key. Setting anything but Loop/PingPong/Default stores ClampForever.</summary>
        public WrapMode preWrapMode
        {
            get => _preWrapMode;
            set => _preWrapMode = NormaliseWrapMode(value);
        }

        /// <summary>Wrap mode at and above the last key; same normalisation as <see cref="preWrapMode"/>.</summary>
        public WrapMode postWrapMode
        {
            get => _postWrapMode;
            set => _postWrapMode = NormaliseWrapMode(value);
        }

        private static WrapMode NormaliseWrapMode(WrapMode mode)
        {
            int v = (int)mode;
            // Only these three survive; Once/Clamp (1), ClampForever (8) and every undefined value store as 8.
            if (v == (int)WrapMode.Loop || v == (int)WrapMode.PingPong || v == (int)WrapMode.Default)
                return mode;
            return WrapMode.ClampForever;
        }

        // -------------------------------------------------------------------------------------------------------
        // Evaluate (GC §5.4 - §5.8)
        // -------------------------------------------------------------------------------------------------------

        /// <summary>Evaluates the curve at <paramref name="time"/> (GC §5.4).</summary>
        public float Evaluate(float time)
        {
            int n = _count;
            if (n == 0)
                return 0f;                  // any t, including NaN
            if (n == 1)
                return _keys[0].value;      // any t, any wrap mode, including NaN

            float t0 = _keys[0].time;
            float t1 = _keys[n - 1].time;
            float t = time;

            // t0 <= t < t1 is never wrapped; t >= t1 uses postWrapMode, so under Loop Evaluate(t1) is the value at t0.
            if (time < t0)
            {
                WrapMode mode = _preWrapMode;
                if (mode == WrapMode.Loop || mode == WrapMode.Default)
                    t = WrapLoop(time, t0, t1);
                else if (mode == WrapMode.PingPong)
                    t = WrapPingPong(time, t0, t1);
                else
                    return EvaluateClampForever(time, _keys[0]);
            }
            else if (time >= t1)
            {
                WrapMode mode = _postWrapMode;
                if (mode == WrapMode.Loop || mode == WrapMode.Default)
                    t = WrapLoop(time, t0, t1);
                else if (mode == WrapMode.PingPong)
                    t = WrapPingPong(time, t0, t1);
                else
                    return EvaluateClampForever(time, _keys[n - 1]);
            }

            // Largest index with K[i].time <= t, clamped to [0, n-2]. With duplicate times this picks the LAST
            // duplicate as the left key, which is why Evaluate at a duplicated time returns the last of them.
            // A NaN t fails every comparison and lands on segment 0, whose polynomial then yields NaN.
            int i = n - 1;
            while (i > 0 && !(_keys[i].time <= t))
                i--;
            if (i > n - 2)
                i = n - 2;

            Keyframe left = _keys[i];
            Keyframe right = _keys[i + 1];

            // Step: an infinite tangent on either side of the segment. +Infinity anywhere wins and holds the left
            // value; -Infinity alone jumps to the right value. Only the segment's own two tangents count.
            float m1 = left.outTangent;
            float m2 = right.inTangent;
            if (float.IsInfinity(m1) || float.IsInfinity(m2))
            {
                if (float.IsPositiveInfinity(m1) || float.IsPositiveInfinity(m2))
                    return left.value;
                return right.value;
            }

            // Weighted segments take the Bézier path; the bit test is on the raw int, so 7 and -1 count as weighted.
            bool outWeighted = ((int)left.weightedMode & (int)WeightedMode.Out) != 0;
            bool inWeighted = ((int)right.weightedMode & (int)WeightedMode.In) != 0;
            if (outWeighted || inWeighted)
                return EvaluateWeighted(left, right, t, outWeighted, inWeighted);

            return EvaluateHermite(left, right, t);
        }

        // ClampForever does not clamp t: it evaluates the edge key's polynomial with zero coefficients. For a finite t
        // that is the edge value; for +-Infinity the 0 * Infinity products make it NaN, which a plain clamp would miss.
        private static float EvaluateClampForever(float time, Keyframe edge)
        {
            float x = time - edge.time;
            return x * (x * (x * 0f + 0f) + 0f) + edge.value;
        }

        private static float WrapLoop(float time, float t0, float t1)
        {
            float range = t1 - t0;
            if (!(range > 0f))
                return t0;                  // degenerate (all keys at one time); not covered by the probe
            float x = time - t0;
            x %= range;                     // fmod: NaN for an infinite t, which is what Unity returns there
            if (x < 0f)
                x += range;                 // fold into [0, range); -0 is left alone, matching t' == t0
            return t0 + x;
        }

        private static float WrapPingPong(float time, float t0, float t1)
        {
            float range = t1 - t0;
            if (!(range > 0f))
                return t0;
            float twoRange = 2f * range;
            float x = time - t0;
            x %= twoRange;
            if (x < 0f)
                x += twoRange;
            if (x > range)
                x = twoRange - x;
            return t0 + x;
        }

        // The bit-exact cubic Hermite of GC §5.6. The grouping is load-bearing: (m1 + m2 - 2*d*len) * (len*len) and
        // (3*d - (2*m1 + m2)*dx) / dx / dx differ in the last bit from the algebraically equal alternatives.
        private static float EvaluateHermite(Keyframe left, Keyframe right, float t)
        {
            float dx = right.time - left.time;
            if (dx < 1e-4f)
                dx = 1e-4f;                 // minimum segment length; pinned by a 1e-7-long segment probe

            float len = 1f / dx;
            float d = right.value - left.value;
            float m1 = left.outTangent;
            float m2 = right.inTangent;

            float a = (m1 + m2 - 2f * d * len) * (len * len);
            float b = (3f * d - (2f * m1 + m2) * dx) / dx / dx;
            float c = m1;
            float x = t - left.time;        // NOT clamped by the 1e-4 rule

            return x * (x * (x * a + b) + c) + left.value;
        }

        // Weighted segments are a cubic Bézier in (time, value) and Unity solves Bx(s) = t numerically, so this path
        // is accurate to ~1e-6 rather than bit-exact (GC §5.8 says bit-exactness is neither achievable nor required).
        // Doubles are used for the root solve so the residual is far below that tolerance.
        private static float EvaluateWeighted(Keyframe left, Keyframe right, float t, bool outWeighted, bool inWeighted)
        {
            double dt = (double)right.time - left.time;
            if (!(dt > 0.0))
                return right.value;         // degenerate segment; unspecified by the probe

            // The unweighted side always contributes 1/3, which is what makes weights of exactly 1/3 agree with the
            // Hermite form mathematically.
            double ow = outWeighted ? left.outWeight : 1.0 / 3.0;
            double iw = inWeighted ? right.inWeight : 1.0 / 3.0;

            double p0x = left.time, p0y = left.value;
            double p1x = left.time + dt * ow, p1y = left.value + dt * ow * left.outTangent;
            double p2x = right.time - dt * iw, p2y = right.value - dt * iw * right.inTangent;
            double p3x = right.time, p3y = right.value;

            double target = t;
            double s = (target - p0x) / dt;             // linear seed
            if (!(s >= 0.0))
                s = 0.0;
            else if (s > 1.0)
                s = 1.0;

            // Bx(0) = left.time and Bx(1) = right.time regardless of the weights, so [0,1] always brackets a root.
            // Newton, safeguarded by bisection: the bisection also handles the non-monotonic weight>1 regime, where
            // GC §5.8 records that Unity's own root choice is unspecified.
            double lo = 0.0, hi = 1.0;
            for (int iter = 0; iter < 64; iter++)
            {
                double x = Bezier(p0x, p1x, p2x, p3x, s) - target;
                if (x > 0.0)
                    hi = s;
                else
                    lo = s;
                if (x > -1e-12 && x < 1e-12)
                    break;

                double slope = BezierDerivative(p0x, p1x, p2x, p3x, s);
                double next = s - x / slope;
                if (!(next > lo && next < hi))
                    next = 0.5 * (lo + hi);
                s = next;
            }

            return (float)Bezier(p0y, p1y, p2y, p3y, s);
        }

        private static double Bezier(double p0, double p1, double p2, double p3, double s)
        {
            double m = 1.0 - s;
            return m * m * m * p0 + 3.0 * m * m * s * p1 + 3.0 * m * s * s * p2 + s * s * s * p3;
        }

        private static double BezierDerivative(double p0, double p1, double p2, double p3, double s)
        {
            double m = 1.0 - s;
            return 3.0 * m * m * (p1 - p0) + 6.0 * m * s * (p2 - p1) + 3.0 * s * s * (p3 - p2);
        }

        // -------------------------------------------------------------------------------------------------------
        // Key editing (GC §5.2, §5.3, §5.5)
        // -------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Inserts <paramref name="key"/> verbatim, leaving neighbours untouched. Returns the new index, or -1
        /// without changing anything when a key already exists at that time (GC §5.2 — the docs' "replaces" is wrong).
        /// </summary>
        public int AddKey(Keyframe key)
        {
            if (HasKeyAtTime(key.time, -1))
                return -1;
            int index = FindInsertIndex(key.time);
            Insert(index, key);
            return index;
        }

        /// <summary>
        /// Adds a key with smooth tangents: the new key gets both weights 1/3, then
        /// <see cref="SmoothTangents"/> with weight 0 runs on it and on each existing neighbour (GC §5.2).
        /// </summary>
        public int AddKey(float time, float value)
        {
            if (HasKeyAtTime(time, -1))
                return -1;

            Keyframe key = new Keyframe(time, value);
            key.inWeight = 1f / 3f;
            key.outWeight = 1f / 3f;

            int index = FindInsertIndex(time);
            Insert(index, key);

            if (index - 1 >= 0)
                SmoothTangents(index - 1, 0f);
            SmoothTangents(index, 0f);
            if (index + 1 < _count)
                SmoothTangents(index + 1, 0f);

            return index;
        }

        /// <summary>
        /// Replaces the key at <paramref name="index"/>. If a <b>different</b> key already sits at
        /// <c>key.time</c> the key at <paramref name="index"/> is <b>removed</b> and -1 is returned — the curve loses
        /// a key (GC §5.3). Otherwise returns the key's new index after re-sorting.
        /// </summary>
        public int MoveKey(int index, Keyframe key)
        {
            if (index < 0 || index >= _count)
                throw new IndexOutOfRangeException("MoveKey");

            if (HasKeyAtTime(key.time, index))
            {
                RemoveAt(index);
                return -1;
            }

            RemoveAt(index);
            int target = FindInsertIndex(key.time);
            Insert(target, key);
            return target;
        }

        /// <summary>Removes the key at <paramref name="index"/>, leaving neighbours untouched (GC §5.3).</summary>
        public void RemoveKey(int index)
        {
            if (index < 0 || index >= _count)
                throw new IndexOutOfRangeException("RemoveKey");
            RemoveAt(index);
        }

        /// <summary>
        /// Sets both tangents of the key at <paramref name="index"/> to
        /// <c>0.5 * (1 + weight) * sL + 0.5 * (1 - weight) * sR</c> — <paramref name="weight"/> is <b>not</b> clamped,
        /// and an end key uses its single available slope for both sides. The inner weights become 1/3;
        /// <c>weightedMode</c> and <c>tangentMode</c> are untouched (GC §5.5).
        /// </summary>
        public void SmoothTangents(int index, float weight)
        {
            if (index < 0 || index >= _count)
                throw new IndexOutOfRangeException("SmoothTangents");
            if (_count == 1)
                return;                     // a lone key keeps its tangents and weights

            int n = _count;
            float sL, sR;
            if (index > 0 && index < n - 1)
            {
                sL = (_keys[index].value - _keys[index - 1].value) / (_keys[index].time - _keys[index - 1].time);
                sR = (_keys[index + 1].value - _keys[index].value) / (_keys[index + 1].time - _keys[index].time);
            }
            else if (index > 0)
            {
                sL = (_keys[index].value - _keys[index - 1].value) / (_keys[index].time - _keys[index - 1].time);
                sR = sL;
            }
            else
            {
                sR = (_keys[index + 1].value - _keys[index].value) / (_keys[index + 1].time - _keys[index].time);
                sL = sR;
            }

            float tangent = 0.5f * (1f + weight) * sL + 0.5f * (1f - weight) * sR;
            _keys[index].inTangent = tangent;
            _keys[index].outTangent = tangent;
            if (index > 0)
                _keys[index].inWeight = 1f / 3f;
            if (index < n - 1)
                _keys[index].outWeight = 1f / 3f;
        }

        private bool HasKeyAtTime(float time, int ignoreIndex)
        {
            for (int i = 0; i < _count; i++)
            {
                if (i == ignoreIndex)
                    continue;
                if (_keys[i].time == time)
                    return true;
            }
            return false;
        }

        private int FindInsertIndex(float time)
        {
            int i = 0;
            while (i < _count && _keys[i].time < time)
                i++;
            return i;
        }

        private void Insert(int index, Keyframe key)
        {
            EnsureCapacity(_count + 1);
            for (int i = _count; i > index; i--)
                _keys[i] = _keys[i - 1];
            _keys[index] = key;
            _count++;
        }

        private void RemoveAt(int index)
        {
            for (int i = index; i < _count - 1; i++)
                _keys[i] = _keys[i + 1];
            _count--;
        }

        // -------------------------------------------------------------------------------------------------------
        // Static helpers (GC §5.9)
        // -------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A straight line from (timeStart, valueStart) to (timeEnd, valueEnd) — except that the keys go through the
        /// sorting constructor while their tangents stay with them, so <c>Linear(1, 0, 0, 1)</c> is an ease curve,
        /// not a line (GC §5.9). Equal times give a one-key curve.
        /// </summary>
        public static AnimationCurve Linear(float timeStart, float valueStart, float timeEnd, float valueEnd)
        {
            if (timeStart == timeEnd)
                return new AnimationCurve(new Keyframe(timeStart, valueStart));

            float tangent = (valueEnd - valueStart) / (timeEnd - timeStart);
            return new AnimationCurve(
                new Keyframe(timeStart, valueStart, 0f, tangent),
                new Keyframe(timeEnd, valueEnd, tangent, 0f));
        }

        /// <summary>A smoothstep between the two points: two keys with all tangents 0 (GC §5.9).</summary>
        public static AnimationCurve EaseInOut(float timeStart, float valueStart, float timeEnd, float valueEnd)
        {
            if (timeStart == timeEnd)
                return new AnimationCurve(new Keyframe(timeStart, valueStart));

            return new AnimationCurve(
                new Keyframe(timeStart, valueStart, 0f, 0f),
                new Keyframe(timeEnd, valueEnd, 0f, 0f));
        }

        /// <summary>A constant value over the range; defined as <c>Linear(timeStart, value, timeEnd, value)</c>.</summary>
        public static AnimationCurve Constant(float timeStart, float timeEnd, float value)
            => Linear(timeStart, value, timeEnd, value);

        /// <summary>Copies the keys and both wrap modes from <paramref name="other"/> (GC §5.10).</summary>
        public void CopyFrom(AnimationCurve other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));
            EnsureCapacity(other._count);
            for (int i = 0; i < other._count; i++)
                _keys[i] = other._keys[i];
            _count = other._count;
            _preWrapMode = other._preWrapMode;
            _postWrapMode = other._postWrapMode;
        }

        // -------------------------------------------------------------------------------------------------------
        // Equality (GC §5.10)
        // -------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Content equality over every key field <b>except</b> <c>tangentMode</c>, plus both wrap modes (GC §5.10).
        /// </summary>
        public bool Equals(AnimationCurve other)
        {
            if (ReferenceEquals(other, null))
                return false;
            if (ReferenceEquals(other, this))
                return true;
            if (_count != other._count)
                return false;
            if (_preWrapMode != other._preWrapMode || _postWrapMode != other._postWrapMode)
                return false;

            for (int i = 0; i < _count; i++)
            {
                Keyframe a = _keys[i];
                Keyframe b = other._keys[i];
                if (a.time != b.time || a.value != b.value ||
                    a.inTangent != b.inTangent || a.outTangent != b.outTangent ||
                    a.inWeight != b.inWeight || a.outWeight != b.outWeight ||
                    a.weightedMode != b.weightedMode)
                    return false;
            }

            return true;
        }

        /// <summary>Requires the exact same runtime type before forwarding to <see cref="Equals(AnimationCurve)"/>.</summary>
        public override bool Equals(object o)
        {
            if (o == null || o.GetType() != GetType())
                return false;
            return Equals((AnimationCurve)o);
        }

        /// <summary>
        /// A content hash over the key fields. It deliberately <b>ignores the wrap modes</b> and <b>includes</b>
        /// <c>tangentMode</c>, so it is inconsistent with <see cref="Equals(AnimationCurve)"/> in exactly the two ways
        /// Unity's is; an empty curve hashes to 0. Unity's native formula is unspecified, so the mixing below is our
        /// own (GC §5.10).
        /// </summary>
        public override int GetHashCode()
        {
            if (_count == 0)
                return 0;

            int hash = 17;
            for (int i = 0; i < _count; i++)
            {
                Keyframe k = _keys[i];
                hash = hash * 31 + BitConverter.SingleToInt32Bits(k.time);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(k.value);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(k.inTangent);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(k.outTangent);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(k.inWeight);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(k.outWeight);
                hash = hash * 31 + (int)k.weightedMode;
                hash = hash * 31 + k.tangentModeInternal;
            }
            return hash;
        }
    }
}
