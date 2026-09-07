// Mirrors UnityEngine.Vector2 for the NowUI standalone build; behavioural spec: Docs/Standalone/UnityValueTypeSemantics.md (§1).
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Vector2 : IEquatable<Vector2>, IFormattable
    {
        public const float kEpsilon = 0.00001F;
        public const float kEpsilonNormalSqrt = 1e-15f;

        // Declared order matters: native code reinterprets arrays of these as float*.
        public float x;
        public float y;

        private static readonly Vector2 zeroVector = new Vector2(0F, 0F);
        private static readonly Vector2 oneVector = new Vector2(1F, 1F);
        private static readonly Vector2 upVector = new Vector2(0F, 1F);
        private static readonly Vector2 downVector = new Vector2(0F, -1F);
        private static readonly Vector2 leftVector = new Vector2(-1F, 0F);
        private static readonly Vector2 rightVector = new Vector2(1F, 0F);
        private static readonly Vector2 positiveInfinityVector = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        private static readonly Vector2 negativeInfinityVector = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        public static Vector2 zero { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return zeroVector; } }
        public static Vector2 one { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return oneVector; } }
        public static Vector2 up { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return upVector; } }
        public static Vector2 down { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return downVector; } }
        public static Vector2 left { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return leftVector; } }
        public static Vector2 right { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return rightVector; } }
        public static Vector2 positiveInfinity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return positiveInfinityVector; } }
        public static Vector2 negativeInfinity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return negativeInfinityVector; } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public float this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    default: throw new IndexOutOfRangeException("Invalid Vector2 index!");
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    default: throw new IndexOutOfRangeException("Invalid Vector2 index!");
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(float newX, float newY)
        {
            x = newX;
            y = newY;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Scale(Vector2 scale)
        {
            x *= scale.x;
            y *= scale.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Scale(in Vector2 scale)
        {
            x *= scale.x;
            y *= scale.y;
        }

        // §1: mag = magnitude; if (mag > kEpsilon) divide, else zero.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Normalize()
        {
            float mag = magnitude;
            if (mag > kEpsilon)
            {
                x /= mag;
                y /= mag;
            }
            else
            {
                x = 0F;
                y = 0F;
            }
        }

        public readonly Vector2 normalized
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Normalize(this); }
        }

        // §1: double sqrt of the single-precision sum, cast back to float.
        public readonly float magnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return (float)Math.Sqrt(x * x + y * y); }
        }

        public readonly float sqrMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return x * x + y * y; }
        }

        // Undocumented legacy member; same as sqrMagnitude.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float SqrMagnitude()
        {
            return x * x + y * y;
        }

        // §1: x.GetHashCode() ^ (y.GetHashCode() << 2). [verified (1,2) -> 1065353216]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override readonly int GetHashCode()
        {
            return x.GetHashCode() ^ (y.GetHashCode() << 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override readonly bool Equals(object other)
        {
            return other is Vector2 v && Equals(v);
        }

        // Exact component equality with C# == (NaN never equal), unlike operator== which is tolerant.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(Vector2 other)
        {
            return x == other.x && y == other.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(in Vector2 other)
        {
            return x == other.x && y == other.y;
        }

        public override readonly string ToString()
        {
            return ToString(null, null);
        }

        public readonly string ToString(string format)
        {
            return ToString(format, null);
        }

        // §0/§1: template "({0}, {1})", default format "F2", invariant culture when no provider is supplied.
        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F2";
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return string.Format("({0}, {1})", x.ToString(format, formatProvider), y.ToString(format, formatProvider));
        }

        // ---- Static members ----

        // §1: t = Clamp01(t); a + (b - a) * t per component.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector2(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Lerp(in Vector2 a, in Vector2 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector2(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t)
        {
            return new Vector2(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 LerpUnclamped(in Vector2 a, in Vector2 b, float t)
        {
            return new Vector2(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t
            );
        }

        // §1: returns target when already there or within reach; negative delta moves away. [verified (0,(10,0),-3) -> (-3,0)]
        public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta)
        {
            float toX = target.x - current.x;
            float toY = target.y - current.y;

            float sqDist = toX * toX + toY * toY;

            if (sqDist == 0 || (maxDistanceDelta >= 0 && sqDist <= maxDistanceDelta * maxDistanceDelta))
                return target;

            float dist = (float)Math.Sqrt(sqDist);

            return new Vector2(
                current.x + toX / dist * maxDistanceDelta,
                current.y + toY / dist * maxDistanceDelta
            );
        }

        public static Vector2 MoveTowards(in Vector2 current, in Vector2 target, float maxDistanceDelta)
        {
            float toX = target.x - current.x;
            float toY = target.y - current.y;

            float sqDist = toX * toX + toY * toY;

            if (sqDist == 0 || (maxDistanceDelta >= 0 && sqDist <= maxDistanceDelta * maxDistanceDelta))
                return target;

            float dist = (float)Math.Sqrt(sqDist);

            return new Vector2(
                current.x + toX / dist * maxDistanceDelta,
                current.y + toY / dist * maxDistanceDelta
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Scale(Vector2 a, Vector2 b)
        {
            return new Vector2(a.x * b.x, a.y * b.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Scale(in Vector2 a, in Vector2 b)
        {
            return new Vector2(a.x * b.x, a.y * b.y);
        }

        // §1: mag > kEpsilon ? value / mag : zero.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Normalize(Vector2 value)
        {
            float mag = value.magnitude;
            if (mag > kEpsilon)
                return new Vector2(value.x / mag, value.y / mag);
            return zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Normalize(in Vector2 value)
        {
            float mag = value.magnitude;
            if (mag > kEpsilon)
                return new Vector2(value.x / mag, value.y / mag);
            return zero;
        }

        // §1: factor = -2 * Dot(n, d); factor * n + d per component. Normal is not normalised.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Reflect(Vector2 inDirection, Vector2 inNormal)
        {
            float factor = -2F * Dot(inNormal, inDirection);
            return new Vector2(factor * inNormal.x + inDirection.x, factor * inNormal.y + inDirection.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Reflect(in Vector2 inDirection, in Vector2 inNormal)
        {
            float factor = -2F * Dot(inNormal, inDirection);
            return new Vector2(factor * inNormal.x + inDirection.x, factor * inNormal.y + inDirection.y);
        }

        // 90 degrees counter-clockwise.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Perpendicular(Vector2 inDirection)
        {
            return new Vector2(-inDirection.y, inDirection.x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Perpendicular(in Vector2 inDirection)
        {
            return new Vector2(-inDirection.y, inDirection.x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector2 lhs, Vector2 rhs)
        {
            return lhs.x * rhs.x + lhs.y * rhs.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(in Vector2 lhs, in Vector2 rhs)
        {
            return lhs.x * rhs.x + lhs.y * rhs.y;
        }

        // §1: compares the product of squared magnitudes against 1e-30 BEFORE the square root (Vector3 differs).
        public static float Angle(Vector2 from, Vector2 to)
        {
            float denominator = from.sqrMagnitude * to.sqrMagnitude;
            if (denominator < kEpsilonNormalSqrt * kEpsilonNormalSqrt)
                return 0F;

            denominator = (float)Math.Sqrt(denominator);
            float dot = Mathf.Clamp(Dot(from, to) / denominator, -1F, 1F);
            return (float)Math.Acos(dot) * Mathf.Rad2Deg;
        }

        public static float Angle(in Vector2 from, in Vector2 to)
        {
            float denominator = from.sqrMagnitude * to.sqrMagnitude;
            if (denominator < kEpsilonNormalSqrt * kEpsilonNormalSqrt)
                return 0F;

            denominator = (float)Math.Sqrt(denominator);
            float dot = Mathf.Clamp(Dot(from, to) / denominator, -1F, 1F);
            return (float)Math.Acos(dot) * Mathf.Rad2Deg;
        }

        // §1: Angle * Sign(cross); Mathf.Sign(0) == 1 so collinear vectors give +angle.
        public static float SignedAngle(Vector2 from, Vector2 to)
        {
            float unsignedAngle = Angle(from, to);
            float sign = Mathf.Sign(from.x * to.y - from.y * to.x);
            return unsignedAngle * sign;
        }

        public static float SignedAngle(in Vector2 from, in Vector2 to)
        {
            float unsignedAngle = Angle(from, to);
            float sign = Mathf.Sign(from.x * to.y - from.y * to.x);
            return unsignedAngle * sign;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Vector2 a, Vector2 b)
        {
            float diffX = a.x - b.x;
            float diffY = a.y - b.y;
            return (float)Math.Sqrt(diffX * diffX + diffY * diffY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(in Vector2 a, in Vector2 b)
        {
            float diffX = a.x - b.x;
            float diffY = a.y - b.y;
            return (float)Math.Sqrt(diffX * diffX + diffY * diffY);
        }

        // §1: normalised components are materialised as float locals before scaling (forces single precision).
        // A negative maxLength flips the vector. [verified ((3,4),-1) -> (-0.6,-0.8)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ClampMagnitude(Vector2 vector, float maxLength)
        {
            float sqrMagnitude = vector.sqrMagnitude;
            if (sqrMagnitude > maxLength * maxLength)
            {
                float mag = (float)Math.Sqrt(sqrMagnitude);

                float normalizedX = vector.x / mag;
                float normalizedY = vector.y / mag;
                return new Vector2(normalizedX * maxLength, normalizedY * maxLength);
            }
            return vector;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ClampMagnitude(in Vector2 vector, float maxLength)
        {
            float sqrMagnitude = vector.sqrMagnitude;
            if (sqrMagnitude > maxLength * maxLength)
            {
                float mag = (float)Math.Sqrt(sqrMagnitude);

                float normalizedX = vector.x / mag;
                float normalizedY = vector.y / mag;
                return new Vector2(normalizedX * maxLength, normalizedY * maxLength);
            }
            return vector;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrMagnitude(Vector2 a)
        {
            return a.x * a.x + a.y * a.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrMagnitude(in Vector2 a)
        {
            return a.x * a.x + a.y * a.y;
        }

        // Componentwise Mathf.Min / Mathf.Max (NaN ordering follows Mathf's a < b ? a : b form).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Min(Vector2 lhs, Vector2 rhs)
        {
            return new Vector2(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Min(in Vector2 lhs, in Vector2 rhs)
        {
            return new Vector2(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Max(Vector2 lhs, Vector2 rhs)
        {
            return new Vector2(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Max(in Vector2 lhs, in Vector2 rhs)
        {
            return new Vector2(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y));
        }

        public static Vector2 SmoothDamp(Vector2 current, Vector2 target, ref Vector2 currentVelocity, float smoothTime, float maxSpeed)
        {
            float deltaTime = NowUI.Engine.EngineClock.deltaTime;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static Vector2 SmoothDamp(Vector2 current, Vector2 target, ref Vector2 currentVelocity, float smoothTime)
        {
            float deltaTime = NowUI.Engine.EngineClock.deltaTime;
            float maxSpeed = Mathf.Infinity;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        // §1 SmoothDamp algorithm, steps 1-8. Note: no deltaTime guard in the overshoot branch (NaN velocity when
        // deltaTime == 0), unlike Mathf.SmoothDamp. [verified (0,(10,0),ref 0,0.3,inf,0.016) -> (0.05165842,0), vel (6.392509,0)]
        // Precision note: the spec's [verified] oracle (0.05165842 [0x3D5397C8]) is reproduced bit-for-bit only when every
        // expression is evaluated in double and rounded to float at each local store (the Mono editor JIT's default float
        // evaluation model on the probe machine); a pure single-precision evaluation lands at 0.051657677 [0x3D539700]
        // because of the cancellation in step 7. The verified value is the oracle and Mathf.SmoothDamp uses the same
        // widened evaluation, so the intermediates are widened explicitly here. // unverified vs Unity: IL2CPP builds may differ.
        public static Vector2 SmoothDamp(Vector2 current, Vector2 target, ref Vector2 currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            // Step 1: critically damped spring coefficients.
            smoothTime = Mathf.Max(0.0001F, smoothTime);
            float omega = (float)(2.0 / smoothTime);

            float x = (float)((double)omega * deltaTime);
            float exp = (float)(1.0 / (1.0 + x + 0.48F * (double)x * x + 0.235F * (double)x * x * x));

            // Step 2: change = current - target.
            float changeX = (float)((double)current.x - target.x);
            float changeY = (float)((double)current.y - target.y);
            Vector2 originalTo = target;

            // Step 3: clamp maximum speed.
            float maxChange = (float)((double)maxSpeed * smoothTime);

            float maxChangeSq = (float)((double)maxChange * maxChange);
            float sqDist = (float)((double)changeX * changeX + (double)changeY * changeY);
            if (sqDist > maxChangeSq)
            {
                float mag = (float)Math.Sqrt(sqDist);
                changeX = (float)((double)changeX / mag * maxChange);
                changeY = (float)((double)changeY / mag * maxChange);
            }

            // Step 4: target' = current - change.
            target.x = (float)((double)current.x - changeX);
            target.y = (float)((double)current.y - changeY);

            // Step 5: temp = (velocity + omega * change) * deltaTime.
            float tempX = (float)(((double)currentVelocity.x + (double)omega * changeX) * deltaTime);
            float tempY = (float)(((double)currentVelocity.y + (double)omega * changeY) * deltaTime);

            // Step 6: velocity = (velocity - omega * temp) * exp.
            currentVelocity.x = (float)(((double)currentVelocity.x - (double)omega * tempX) * exp);
            currentVelocity.y = (float)(((double)currentVelocity.y - (double)omega * tempY) * exp);

            // Step 7: output = target' + (change + temp) * exp.
            float outputX = (float)((double)target.x + ((double)changeX + tempX) * exp);
            float outputY = (float)((double)target.y + ((double)changeY + tempY) * exp);

            // Step 8: overshoot guard via dot product; velocity division by deltaTime is unguarded on purpose.
            float origMinusCurrentX = (float)((double)originalTo.x - current.x);
            float origMinusCurrentY = (float)((double)originalTo.y - current.y);
            float outMinusOrigX = (float)((double)outputX - originalTo.x);
            float outMinusOrigY = (float)((double)outputY - originalTo.y);

            if ((double)origMinusCurrentX * outMinusOrigX + (double)origMinusCurrentY * outMinusOrigY > 0)
            {
                outputX = originalTo.x;
                outputY = originalTo.y;

                currentVelocity.x = (float)(((double)outputX - originalTo.x) / deltaTime);
                currentVelocity.y = (float)(((double)outputY - originalTo.y) / deltaTime);
            }

            return new Vector2(outputX, outputY);
        }

        // ---- Operators ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }

        // Vector2 (unlike Vector3/Vector4) has componentwise vector*vector and vector/vector operators.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator *(Vector2 a, Vector2 b) { return new Vector2(a.x * b.x, a.y * b.y); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator /(Vector2 a, Vector2 b) { return new Vector2(a.x / b.x, a.y / b.y); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator -(Vector2 a) { return new Vector2(-a.x, -a.y); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator *(Vector2 a, float d) { return new Vector2(a.x * d, a.y * d); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator *(float d, Vector2 a) { return new Vector2(a.x * d, a.y * d); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator /(Vector2 a, float d) { return new Vector2(a.x / d, a.y / d); }

        // §1: squared distance < kEpsilon*kEpsilon (~1e-10). False when any component is NaN.
        // [verified zero == (1e-6,0) true; zero == (1e-5,0) false]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector2 lhs, Vector2 rhs)
        {
            float diffX = lhs.x - rhs.x;
            float diffY = lhs.y - rhs.y;
            return (diffX * diffX + diffY * diffY) < kEpsilon * kEpsilon;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vector2 lhs, Vector2 rhs)
        {
            return !(lhs == rhs);
        }

        // ---- Conversions (declared on Vector2 per §1) ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(Vector3 v)
        {
            return new Vector2(v.x, v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3(Vector2 v)
        {
            return new Vector3(v.x, v.y, 0);
        }
    }
}
