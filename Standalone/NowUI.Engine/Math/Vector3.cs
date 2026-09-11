// Mirrors UnityEngine.Vector3 for the NowUI standalone build; behavioural spec: Docs/Standalone/UnityValueTypeSemantics.md (§2).
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Vector3 : IEquatable<Vector3>, IFormattable
    {
        public const float kEpsilon = 0.00001F;
        public const float kEpsilonNormalSqrt = 1e-15F;

        // Declared order matters: native code reinterprets Vector3[] as float* (spec §0).
        public float x;
        public float y;
        public float z;

        // Threshold used by the native Slerp/RotateTowards paths for "nearly parallel" direction tests (spec §2:
        // "assume 1e-5-class comparisons on the normalised dot product"). // unverified vs Unity: exact threshold.
        private const float kParallelDotEpsilon = 1e-5F;

        #region Constructors

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3(float x, float y)
        {
            this.x = x;
            this.y = y;
            z = 0F;
        }

        #endregion

        #region Indexer

        public float this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    case 2: return z;
                    default: throw new IndexOutOfRangeException("Invalid Vector3 index!");
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    default: throw new IndexOutOfRangeException("Invalid Vector3 index!");
                }
            }
        }

        #endregion

        #region Static presets

        private static readonly Vector3 zeroVector = new Vector3(0F, 0F, 0F);
        private static readonly Vector3 oneVector = new Vector3(1F, 1F, 1F);
        private static readonly Vector3 upVector = new Vector3(0F, 1F, 0F);
        private static readonly Vector3 downVector = new Vector3(0F, -1F, 0F);
        private static readonly Vector3 leftVector = new Vector3(-1F, 0F, 0F);
        private static readonly Vector3 rightVector = new Vector3(1F, 0F, 0F);
        private static readonly Vector3 forwardVector = new Vector3(0F, 0F, 1F);
        private static readonly Vector3 backVector = new Vector3(0F, 0F, -1F);
        private static readonly Vector3 positiveInfinityVector = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private static readonly Vector3 negativeInfinityVector = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        public static Vector3 zero { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => zeroVector; }
        public static Vector3 one { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => oneVector; }
        public static Vector3 up { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => upVector; }
        public static Vector3 down { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => downVector; }
        public static Vector3 left { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => leftVector; }
        public static Vector3 right { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => rightVector; }
        public static Vector3 forward { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => forwardVector; }
        public static Vector3 back { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => backVector; }
        public static Vector3 positiveInfinity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => positiveInfinityVector; }
        public static Vector3 negativeInfinity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => negativeInfinityVector; }

        [Obsolete("Use Vector3.forward instead.")]
        public static Vector3 fwd { get => forwardVector; }

        #endregion

        #region Instance members

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(float newX, float newY, float newZ)
        {
            x = newX;
            y = newY;
            z = newZ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Scale(Vector3 scale)
        {
            x *= scale.x;
            y *= scale.y;
            z *= scale.z;
        }

        // Spec §0 in-twin, matching Vector2.Scale(in Vector2) / Vector4.Scale(in Vector4) so `in`-passing callers
        // bind the same way on all three types.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Scale(in Vector3 scale)
        {
            x *= scale.x;
            y *= scale.y;
            z *= scale.z;
        }

        // Spec §2: threshold mag > kEpsilon, else all components become zero.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Normalize()
        {
            float mag = Magnitude(this);
            if (mag > kEpsilon)
            {
                x /= mag;
                y /= mag;
                z /= mag;
            }
            else
            {
                x = 0F;
                y = 0F;
                z = 0F;
            }
        }

        public readonly Vector3 normalized
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Normalize(this);
        }

        // Spec §2: f32(Math.Sqrt(x*x + y*y + z*z)) — double sqrt of the float sum, cast back to float.
        public readonly float magnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (float)Math.Sqrt(x * x + y * y + z * z);
        }

        public readonly float sqrMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => x * x + y * y + z * z;
        }

        // Spec §2 [verified]: (1,2,3) → 797966336.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override readonly int GetHashCode()
        {
            return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override readonly bool Equals(object other)
        {
            return other is Vector3 v && Equals(v);
        }

        // Exact component equality with C# == (NaN-unequal), spec §0/§2.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(Vector3 other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(in Vector3 other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override readonly string ToString()
        {
            return ToString(null, null);
        }

        public readonly string ToString(string format)
        {
            return ToString(format, null);
        }

        // Template "({0}, {1}, {2})", default format "F2", invariant culture when provider is null (spec §0).
        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F2";
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return "(" + x.ToString(format, formatProvider) + ", " + y.ToString(format, formatProvider) + ", " + z.ToString(format, formatProvider) + ")";
        }

        #endregion

        #region Static members: interpolation and movement

        // Spec §1/§2: t = Clamp01(t); component = a + (b - a) * t.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector3(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Lerp(in Vector3 a, in Vector3 b, float t)
        {
            float tc = Mathf.Clamp01(t);
            return new Vector3(
                a.x + (b.x - a.x) * tc,
                a.y + (b.y - a.y) * tc,
                a.z + (b.z - a.z) * tc);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t)
        {
            return new Vector3(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 LerpUnclamped(in Vector3 a, in Vector3 b, float t)
        {
            return new Vector3(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t);
        }

        // Spec §1 algorithm extended to three components; a negative maxDistanceDelta moves away from target.
        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
        {
            float toX = target.x - current.x;
            float toY = target.y - current.y;
            float toZ = target.z - current.z;

            float sqDist = toX * toX + toY * toY + toZ * toZ;

            if (sqDist == 0 || (maxDistanceDelta >= 0 && sqDist <= maxDistanceDelta * maxDistanceDelta))
                return target;

            float dist = (float)Math.Sqrt(sqDist);

            return new Vector3(
                current.x + toX / dist * maxDistanceDelta,
                current.y + toY / dist * maxDistanceDelta,
                current.z + toZ / dist * maxDistanceDelta);
        }

        public static Vector3 MoveTowards(in Vector3 current, in Vector3 target, float maxDistanceDelta)
        {
            return MoveTowards(current, target, maxDistanceDelta);
        }

        // Native in Unity. Spec §2: direction along the great circle, magnitude linear; zero-length or parallel
        // inputs fall back to Lerp; antiparallel inputs rotate a about a fixed perpendicular axis by π·t.
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
        {
            return SlerpUnclamped(a, b, Mathf.Clamp01(t));
        }

        public static Vector3 Slerp(in Vector3 a, in Vector3 b, float t)
        {
            return SlerpUnclamped(a, b, Mathf.Clamp01(t));
        }

        public static Vector3 SlerpUnclamped(in Vector3 a, in Vector3 b, float t)
        {
            return SlerpUnclamped((Vector3)a, (Vector3)b, t);
        }

        public static Vector3 SlerpUnclamped(Vector3 a, Vector3 b, float t)
        {
            float magA = Magnitude(a);
            float magB = Magnitude(b);

            // Zero-length input → plain lerp. [verified: Slerp(zero, up, 0.5) = (0, 0.5, 0)]
            if (magA <= kEpsilon || magB <= kEpsilon)
                return LerpUnclamped(a, b, t);

            float invA = 1F / magA;
            float invB = 1F / magB;
            Vector3 dirA = new Vector3(a.x * invA, a.y * invA, a.z * invA);
            Vector3 dirB = new Vector3(b.x * invB, b.y * invB, b.z * invB);

            float dot = Mathf.Clamp(Dot(dirA, dirB), -1F, 1F);

            // Nearly identical directions → plain lerp. [verified: Slerp(right, right*3, 0.5) = (2, 0, 0)]
            // unverified vs Unity: the exact parallel threshold.
            if (dot >= 1F - kParallelDotEpsilon)
                return LerpUnclamped(a, b, t);

            float mag = magA + (magB - magA) * t;
            Vector3 dir;

            if (dot <= -1F + kParallelDotEpsilon)
            {
                // Antiparallel: rotate dirA about a fixed perpendicular axis by π·t. [verified: Slerp(right, left, 0.5)
                // = (≈0, 0, -1); Slerp(up, down, 0.5) = (0, ≈0, -1)] // unverified vs Unity: the antiparallel threshold.
                dir = RotateAboutPerpendicularAxis(dirA, Mathf.PI * t);
            }
            else
            {
                float angle = MathF.Acos(dot);
                float invSin = 1F / MathF.Sin(angle);
                float wa = MathF.Sin((1F - t) * angle) * invSin;
                float wb = MathF.Sin(t * angle) * invSin;
                dir = new Vector3(
                    dirA.x * wa + dirB.x * wb,
                    dirA.y * wa + dirB.y * wb,
                    dirA.z * wa + dirB.z * wb);
            }

            return new Vector3(dir.x * mag, dir.y * mag, dir.z * mag);
        }

        // Native in Unity. Spec §2 [verified]: RotateTowards(right, up, 0.5, 0) = (cos 0.5, sin 0.5, 0);
        // (right, up*3, 0.5, 1) has magnitude 2; (right, left, 0.5, 0) = (cos 0.5, 0, -sin 0.5).
        public static Vector3 RotateTowards(in Vector3 current, in Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta)
        {
            return RotateTowards((Vector3)current, (Vector3)target, maxRadiansDelta, maxMagnitudeDelta);
        }

        public static Vector3 RotateTowards(Vector3 current, Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta)
        {
            float magC = Magnitude(current);
            float magT = Magnitude(target);

            // With a zero-length input there is no direction to rotate; only the magnitude can move.
            // unverified vs Unity: zero-length handling.
            if (magC <= kEpsilon || magT <= kEpsilon)
                return MoveTowards(current, target, maxMagnitudeDelta);

            float invC = 1F / magC;
            float invT = 1F / magT;
            Vector3 dirC = new Vector3(current.x * invC, current.y * invC, current.z * invC);
            Vector3 dirT = new Vector3(target.x * invT, target.y * invT, target.z * invT);

            float dot = Mathf.Clamp(Dot(dirC, dirT), -1F, 1F);
            float newMag = Mathf.MoveTowards(magC, magT, maxMagnitudeDelta);
            Vector3 dir;

            if (dot >= 1F - kParallelDotEpsilon)
            {
                // Same direction: nothing to rotate toward; a negative delta rotates away about an arbitrary
                // perpendicular axis. // unverified vs Unity: negative-delta behaviour for parallel inputs.
                dir = maxRadiansDelta < 0F
                    ? RotateAboutPerpendicularAxis(dirC, Mathf.Min(-maxRadiansDelta, Mathf.PI))
                    : dirT;
            }
            else if (dot <= -1F + kParallelDotEpsilon)
            {
                // Antiparallel: rotate about the fixed perpendicular axis (same rule as Slerp); the target
                // direction is reached after π radians. A negative delta cannot rotate further away.
                dir = maxRadiansDelta > 0F
                    ? RotateAboutPerpendicularAxis(dirC, Mathf.Min(maxRadiansDelta, Mathf.PI))
                    : dirC;
            }
            else
            {
                float angle = MathF.Acos(dot);
                float step;
                if (maxRadiansDelta >= 0F)
                    step = Mathf.Min(maxRadiansDelta, angle);
                else
                    step = -Mathf.Min(-maxRadiansDelta, Mathf.PI - angle); // rotate away, stopping at the opposite direction

                if (step == angle)
                {
                    dir = dirT;
                }
                else
                {
                    // Rotate dirC toward dirT within their common plane by `step` radians, directly by cos/sin of the
                    // step about the in-plane perpendicular (Rodrigues with an axis orthogonal to dirC). The
                    // sine-weight form sin(angle - step)/sin(angle) is algebraically identical but lands 1 ulp high:
                    // it reaches cos(0.5) through sin(pi/2 - 0.5) of a rounded pi/2. [verified] RotateTowards(right, up,
                    // 0.5, 0) = (0.87758255 [0x3F60A940], 0.47942555, 0) = (cos 0.5, sin 0.5, 0) and
                    // (right, up*3, 0.5, 1) = (1.7551651 [0x3FE0A940], 0.9588511, 0) are bit-exact only this way.
                    float px = dirT.x - dirC.x * dot;
                    float py = dirT.y - dirC.y * dot;
                    float pz = dirT.z - dirC.z * dot;
                    float pmag = MathF.Sqrt(px * px + py * py + pz * pz);
                    float pinv = 1F / pmag;
                    px *= pinv; py *= pinv; pz *= pinv;

                    float c = MathF.Cos(step);
                    float s = MathF.Sin(step);
                    dir = new Vector3(
                        dirC.x * c + px * s,
                        dirC.y * c + py * s,
                        dirC.z * c + pz * s);
                }
            }

            return new Vector3(dir.x * newMag, dir.y * newMag, dir.z * newMag);
        }

        // Perpendicular-axis rule reconstructed in spec §2: if |dir.z| > 1/√2 use normalize(0, -dir.z, dir.y),
        // else normalize(-dir.y, dir.x, 0). Rotates the unit vector `dir` about that axis by `angle` radians
        // (Rodrigues with axis ⟂ dir, so the axis·dir term vanishes).
        private static Vector3 RotateAboutPerpendicularAxis(Vector3 dir, float angle)
        {
            Vector3 axis = PerpendicularAxis(dir);
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);
            // cross(axis, dir)
            float cx = axis.y * dir.z - axis.z * dir.y;
            float cy = axis.z * dir.x - axis.x * dir.z;
            float cz = axis.x * dir.y - axis.y * dir.x;
            return new Vector3(
                dir.x * c + cx * s,
                dir.y * c + cy * s,
                dir.z * c + cz * s);
        }

        private static Vector3 PerpendicularAxis(Vector3 dir)
        {
            const float kInvSqrt2 = 0.70710678118654752F;
            Vector3 axis = Mathf.Abs(dir.z) > kInvSqrt2
                ? new Vector3(0F, -dir.z, dir.y)
                : new Vector3(-dir.y, dir.x, 0F);
            return Normalize(axis);
        }

        #endregion

        #region Static members: SmoothDamp

        // Spec §0: the overloads that omit deltaTime read Unity's Time.deltaTime; the shim's frame clock is
        // NowUI.Engine.EngineClock.deltaTime (same source Mathf.SmoothDamp uses).
        public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float maxSpeed)
        {
            float deltaTime = NowUI.Engine.EngineClock.deltaTime;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime)
        {
            float deltaTime = NowUI.Engine.EngineClock.deltaTime;
            float maxSpeed = Mathf.Infinity;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        // Spec §1 algorithm (steps 1-8) with three components; the overshoot branch divides by deltaTime unguarded.
        //
        // Precision model: the spec's [verified] oracles for this algorithm (out 0.05165842 [0x3D5397C8], velocity
        // 6.392509 [0x40CC8F6F], and the scalar maxSpeed=5 pair 0.007748774 / 0.95887625) are reproduced bit-exactly
        // only when each statement is evaluated with double-precision intermediates and rounded to float on store
        // (Mono's default JIT model: R4 locals, R8 evaluation stack). Pure single-precision evaluation, which spec §0
        // claims, lands 1 ulp away on three of the four oracles. The verified values win, so this member widens every
        // expression to double explicitly and stores through float locals, mirroring what the probe's runtime did.
        // Float literals are widened from their float values (0.48F, not 0.48) to match the IL constants.
        public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            float outputX = 0F;
            float outputY = 0F;
            float outputZ = 0F;

            // Step 1
            smoothTime = Mathf.Max(0.0001F, smoothTime);
            float omega = (float)(2.0 / smoothTime);

            float x = (float)((double)omega * deltaTime);
            float exp = (float)(1.0 / (1.0 + x + (double)0.48F * x * x + (double)0.235F * x * x * x));

            // Step 2
            float changeX = (float)((double)current.x - target.x);
            float changeY = (float)((double)current.y - target.y);
            float changeZ = (float)((double)current.z - target.z);
            Vector3 originalTo = target;

            // Step 3: clamp the change vector to the maximum speed
            float maxChange = (float)((double)maxSpeed * smoothTime);

            float maxChangeSq = (float)((double)maxChange * maxChange);
            float sqDist = (float)((double)changeX * changeX + (double)changeY * changeY + (double)changeZ * changeZ);
            if (sqDist > maxChangeSq)
            {
                float mag = (float)Math.Sqrt(sqDist);
                changeX = (float)((double)changeX / mag * maxChange);
                changeY = (float)((double)changeY / mag * maxChange);
                changeZ = (float)((double)changeZ / mag * maxChange);
            }

            // Step 4
            target.x = (float)((double)current.x - changeX);
            target.y = (float)((double)current.y - changeY);
            target.z = (float)((double)current.z - changeZ);

            // Step 5
            float tempX = (float)(((double)currentVelocity.x + (double)omega * changeX) * deltaTime);
            float tempY = (float)(((double)currentVelocity.y + (double)omega * changeY) * deltaTime);
            float tempZ = (float)(((double)currentVelocity.z + (double)omega * changeZ) * deltaTime);

            // Step 6
            currentVelocity.x = (float)(((double)currentVelocity.x - (double)omega * tempX) * exp);
            currentVelocity.y = (float)(((double)currentVelocity.y - (double)omega * tempY) * exp);
            currentVelocity.z = (float)(((double)currentVelocity.z - (double)omega * tempZ) * exp);

            // Step 7
            outputX = (float)((double)target.x + ((double)changeX + tempX) * exp);
            outputY = (float)((double)target.y + ((double)changeY + tempY) * exp);
            outputZ = (float)((double)target.z + ((double)changeZ + tempZ) * exp);

            // Step 8: overshoot guard (3-component dot product, no deltaTime guard)
            float origMinusCurrentX = (float)((double)originalTo.x - current.x);
            float origMinusCurrentY = (float)((double)originalTo.y - current.y);
            float origMinusCurrentZ = (float)((double)originalTo.z - current.z);
            float outMinusOrigX = (float)((double)outputX - originalTo.x);
            float outMinusOrigY = (float)((double)outputY - originalTo.y);
            float outMinusOrigZ = (float)((double)outputZ - originalTo.z);

            if ((double)origMinusCurrentX * outMinusOrigX + (double)origMinusCurrentY * outMinusOrigY + (double)origMinusCurrentZ * outMinusOrigZ > 0)
            {
                outputX = originalTo.x;
                outputY = originalTo.y;
                outputZ = originalTo.z;

                currentVelocity.x = (float)(((double)outputX - originalTo.x) / deltaTime);
                currentVelocity.y = (float)(((double)outputY - originalTo.y) / deltaTime);
                currentVelocity.z = (float)(((double)outputZ - originalTo.z) / deltaTime);
            }

            return new Vector3(outputX, outputY, outputZ);
        }

        #endregion

        #region Static members: algebra

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Scale(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Scale(in Vector3 a, in Vector3 b)
        {
            return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Cross(Vector3 lhs, Vector3 rhs)
        {
            return new Vector3(
                lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.z * rhs.x - lhs.x * rhs.z,
                lhs.x * rhs.y - lhs.y * rhs.x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Cross(in Vector3 lhs, in Vector3 rhs)
        {
            return new Vector3(
                lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.z * rhs.x - lhs.x * rhs.z,
                lhs.x * rhs.y - lhs.y * rhs.x);
        }

        // Spec §1: factor = -2 * Dot(inNormal, inDirection); component = factor * n + d. Normal is not normalised.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Reflect(Vector3 inDirection, Vector3 inNormal)
        {
            float factor = -2F * Dot(inNormal, inDirection);
            return new Vector3(
                factor * inNormal.x + inDirection.x,
                factor * inNormal.y + inDirection.y,
                factor * inNormal.z + inDirection.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Reflect(in Vector3 inDirection, in Vector3 inNormal)
        {
            float factor = -2F * Dot(inNormal, inDirection);
            return new Vector3(
                factor * inNormal.x + inDirection.x,
                factor * inNormal.y + inDirection.y,
                factor * inNormal.z + inDirection.z);
        }

        // Spec §1/§2: mag > kEpsilon ? value / mag : zero.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Normalize(Vector3 value)
        {
            float mag = Magnitude(value);
            if (mag > kEpsilon)
                return value / mag;
            return zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Normalize(in Vector3 value)
        {
            float mag = Magnitude(value);
            if (mag > kEpsilon)
                return value / mag;
            return zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector3 lhs, Vector3 rhs)
        {
            return lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(in Vector3 lhs, in Vector3 rhs)
        {
            return lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z;
        }

        // Spec §2: sqrMag < Mathf.Epsilon → zero; else onNormal * (Dot(vector, onNormal) / sqrMag).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Project(Vector3 vector, Vector3 onNormal)
        {
            float sqrMag = Dot(onNormal, onNormal);
            if (sqrMag < Mathf.Epsilon)
                return zero;

            float dot = Dot(vector, onNormal);
            float k = dot / sqrMag;
            return new Vector3(onNormal.x * k, onNormal.y * k, onNormal.z * k);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Project(in Vector3 vector, in Vector3 onNormal)
        {
            return Project((Vector3)vector, (Vector3)onNormal);
        }

        // Spec §2: same guard as Project but returns the input unchanged; else vector - planeNormal * k.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal)
        {
            float sqrMag = Dot(planeNormal, planeNormal);
            if (sqrMag < Mathf.Epsilon)
                return vector;

            float dot = Dot(vector, planeNormal);
            float k = dot / sqrMag;
            return new Vector3(
                vector.x - planeNormal.x * k,
                vector.y - planeNormal.y * k,
                vector.z - planeNormal.z * k);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOnPlane(in Vector3 vector, in Vector3 planeNormal)
        {
            return ProjectOnPlane((Vector3)vector, (Vector3)planeNormal);
        }

        // Spec §2: denominator = f32(sqrt(sqrMag(from) * sqrMag(to))); < kEpsilonNormalSqrt → 0;
        // dot clamped to [-1, 1]; f32(Math.Acos(dot)) * Rad2Deg.
        public static float Angle(Vector3 from, Vector3 to)
        {
            float denominator = (float)Math.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (denominator < kEpsilonNormalSqrt)
                return 0F;

            float dot = Mathf.Clamp(Dot(from, to) / denominator, -1F, 1F);
            return (float)Math.Acos(dot) * Mathf.Rad2Deg;
        }

        public static float Angle(in Vector3 from, in Vector3 to)
        {
            return Angle((Vector3)from, (Vector3)to);
        }

        // Spec §2: Angle(from, to) * Sign(Dot(axis, Cross(from, to))) with the cross product inlined; Sign(0) = 1.
        public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
        {
            float unsignedAngle = Angle(from, to);

            float crossX = from.y * to.z - from.z * to.y;
            float crossY = from.z * to.x - from.x * to.z;
            float crossZ = from.x * to.y - from.y * to.x;
            float sign = Mathf.Sign(axis.x * crossX + axis.y * crossY + axis.z * crossZ);
            return unsignedAngle * sign;
        }

        public static float SignedAngle(in Vector3 from, in Vector3 to, in Vector3 axis)
        {
            return SignedAngle((Vector3)from, (Vector3)to, (Vector3)axis);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Vector3 a, Vector3 b)
        {
            float diffX = a.x - b.x;
            float diffY = a.y - b.y;
            float diffZ = a.z - b.z;
            return (float)Math.Sqrt(diffX * diffX + diffY * diffY + diffZ * diffZ);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(in Vector3 a, in Vector3 b)
        {
            float diffX = a.x - b.x;
            float diffY = a.y - b.y;
            float diffZ = a.z - b.z;
            return (float)Math.Sqrt(diffX * diffX + diffY * diffY + diffZ * diffZ);
        }

        // Spec §1: normalised components materialised as float locals before scaling; negative maxLength flips.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ClampMagnitude(Vector3 vector, float maxLength)
        {
            float sqrmag = vector.sqrMagnitude;
            if (sqrmag > maxLength * maxLength)
            {
                float mag = (float)Math.Sqrt(sqrmag);
                float normalizedX = vector.x / mag;
                float normalizedY = vector.y / mag;
                float normalizedZ = vector.z / mag;
                return new Vector3(
                    normalizedX * maxLength,
                    normalizedY * maxLength,
                    normalizedZ * maxLength);
            }
            return vector;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ClampMagnitude(in Vector3 vector, float maxLength)
        {
            return ClampMagnitude((Vector3)vector, maxLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Magnitude(Vector3 vector)
        {
            return (float)Math.Sqrt(vector.x * vector.x + vector.y * vector.y + vector.z * vector.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Magnitude(in Vector3 vector)
        {
            return (float)Math.Sqrt(vector.x * vector.x + vector.y * vector.y + vector.z * vector.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrMagnitude(Vector3 vector)
        {
            return vector.x * vector.x + vector.y * vector.y + vector.z * vector.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrMagnitude(in Vector3 vector)
        {
            return vector.x * vector.x + vector.y * vector.y + vector.z * vector.z;
        }

        // Componentwise Mathf.Min/Max (NaN ordering follows Mathf: Min(NaN, 1) = 1, Min(1, NaN) = NaN).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Min(Vector3 lhs, Vector3 rhs)
        {
            return new Vector3(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y), Mathf.Min(lhs.z, rhs.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Min(in Vector3 lhs, in Vector3 rhs)
        {
            return new Vector3(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y), Mathf.Min(lhs.z, rhs.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Max(Vector3 lhs, Vector3 rhs)
        {
            return new Vector3(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y), Mathf.Max(lhs.z, rhs.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Max(in Vector3 lhs, in Vector3 rhs)
        {
            return new Vector3(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y), Mathf.Max(lhs.z, rhs.z));
        }

        // Native Gram–Schmidt (spec §2 [verified]: ((2,0,0),(1,1,0)) → (1,0,0),(0,1,0)). When the tangent is
        // degenerate after projection it is replaced by a vector perpendicular to the normal.
        // unverified vs Unity: degenerate-tangent fallback choice and thresholds.
        public static void OrthoNormalize(ref Vector3 normal, ref Vector3 tangent)
        {
            normal = Normalize(normal);
            tangent = OrthoNormalizeAgainst(tangent, normal);
        }

        public static void OrthoNormalize(ref Vector3 normal, ref Vector3 tangent, ref Vector3 binormal)
        {
            normal = Normalize(normal);
            tangent = OrthoNormalizeAgainst(tangent, normal);

            float dn = Dot(binormal, normal);
            float dt = Dot(binormal, tangent);
            Vector3 b = new Vector3(
                binormal.x - normal.x * dn - tangent.x * dt,
                binormal.y - normal.y * dn - tangent.y * dt,
                binormal.z - normal.z * dn - tangent.z * dt);
            if (b.sqrMagnitude > kEpsilon * kEpsilon)
                binormal = Normalize(b);
            else
                binormal = Cross(normal, tangent);
        }

        private static Vector3 OrthoNormalizeAgainst(Vector3 tangent, Vector3 unitNormal)
        {
            float d = Dot(tangent, unitNormal);
            Vector3 t = new Vector3(
                tangent.x - unitNormal.x * d,
                tangent.y - unitNormal.y * d,
                tangent.z - unitNormal.z * d);
            if (t.sqrMagnitude > kEpsilon * kEpsilon)
                return Normalize(t);
            if (unitNormal.sqrMagnitude > kEpsilon * kEpsilon)
                return PerpendicularAxis(unitNormal);
            return zero;
        }

        #endregion

        #region Operators

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator +(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 a)
        {
            return new Vector3(-a.x, -a.y, -a.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(Vector3 a, float d)
        {
            return new Vector3(a.x * d, a.y * d, a.z * d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(float d, Vector3 a)
        {
            return new Vector3(a.x * d, a.y * d, a.z * d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator /(Vector3 a, float d)
        {
            return new Vector3(a.x / d, a.y / d, a.z / d);
        }

        // Spec §1/§2: squared difference < kEpsilon * kEpsilon (≈1e-10); false with NaN.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector3 lhs, Vector3 rhs)
        {
            float diffX = lhs.x - rhs.x;
            float diffY = lhs.y - rhs.y;
            float diffZ = lhs.z - rhs.z;
            float sqrmag = diffX * diffX + diffY * diffY + diffZ * diffZ;
            return sqrmag < kEpsilon * kEpsilon;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vector3 lhs, Vector3 rhs)
        {
            return !(lhs == rhs);
        }

        #endregion

        #region Obsolete

        // Spec §2: radians, f32(Math.Acos(Clamp(Dot(from.normalized, to.normalized), -1, 1))).
        [Obsolete("Use Vector3.Angle instead. AngleBetween uses radians instead of degrees and was deprecated for this reason")]
        public static float AngleBetween(Vector3 from, Vector3 to)
        {
            return (float)Math.Acos(Mathf.Clamp(Dot(from.normalized, to.normalized), -1F, 1F));
        }

        [Obsolete("Use Vector3.ProjectOnPlane instead.")]
        public static Vector3 Exclude(Vector3 excludeThis, Vector3 fromThat)
        {
            return ProjectOnPlane(fromThat, excludeThis);
        }

        #endregion
    }
}
