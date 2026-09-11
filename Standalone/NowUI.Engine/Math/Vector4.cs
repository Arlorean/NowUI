// Mirrors UnityEngine.Vector4 for the NowUI standalone build; behavioural spec: Docs/Standalone/UnityValueTypeSemantics.md (§3).
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Vector4 : IEquatable<Vector4>, IFormattable
    {
        // Spec §3: Vector4 has kEpsilon only (no kEpsilonNormalSqrt).
        public const float kEpsilon = 0.00001F;

        // Declared order matters: native code reinterprets Vector4[] as float* (spec §0).
        public float x;
        public float y;
        public float z;
        public float w;

        #region Constructors

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = 0F;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4(float x, float y)
        {
            this.x = x;
            this.y = y;
            this.z = 0F;
            this.w = 0F;
        }

        #endregion

        #region Indexer

        public float this[int index]
        {
            readonly get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    case 2: return z;
                    case 3: return w;
                    default:
                        throw new IndexOutOfRangeException("Invalid Vector4 index!");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    case 3: w = value; break;
                    default:
                        throw new IndexOutOfRangeException("Invalid Vector4 index!");
                }
            }
        }

        #endregion

        #region Static presets

        private static readonly Vector4 zeroVector = new Vector4(0F, 0F, 0F, 0F);
        private static readonly Vector4 oneVector = new Vector4(1F, 1F, 1F, 1F);
        private static readonly Vector4 positiveInfinityVector = new Vector4(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private static readonly Vector4 negativeInfinityVector = new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        public static Vector4 zero { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => zeroVector; }
        public static Vector4 one { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => oneVector; }
        public static Vector4 positiveInfinity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => positiveInfinityVector; }
        public static Vector4 negativeInfinity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => negativeInfinityVector; }

        #endregion

        #region Instance members

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(float newX, float newY, float newZ, float newW)
        {
            x = newX;
            y = newY;
            z = newZ;
            w = newW;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Scale(Vector4 scale)
        {
            x *= scale.x;
            y *= scale.y;
            z *= scale.z;
            w *= scale.w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Scale(in Vector4 scale)
        {
            x *= scale.x;
            y *= scale.y;
            z *= scale.z;
            w *= scale.w;
        }

        // Spec §3: mag = Magnitude(this); > kEpsilon divide, else zero all four.
        public void Normalize()
        {
            float mag = Magnitude(this);
            if (mag > kEpsilon)
            {
                x /= mag;
                y /= mag;
                z /= mag;
                w /= mag;
            }
            else
            {
                x = 0F;
                y = 0F;
                z = 0F;
                w = 0F;
            }
        }

        public readonly Vector4 normalized
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Normalize(this);
        }

        // Spec §3: magnitude = f32(Math.Sqrt(Dot(this, this))).
        public readonly float magnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (float)Math.Sqrt(Dot(this, this));
        }

        public readonly float sqrMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Dot(this, this);
        }

        // Undocumented legacy member kept for API parity (spec §3).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float SqrMagnitude() => Dot(this, this);

        // Spec §3: x ^ (y << 2) ^ (z >> 2) ^ (w >> 1) of the component hash codes ([verified] (1,2,3,4) → 265289728).
        public override readonly int GetHashCode()
        {
            return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1);
        }

        public override readonly bool Equals(object other)
        {
            return other is Vector4 v && Equals(v);
        }

        // Exact component equality via C# == (NaN-unequal), spec §0/§3.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(Vector4 other)
        {
            return x == other.x && y == other.y && z == other.z && w == other.w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(in Vector4 other)
        {
            return x == other.x && y == other.y && z == other.z && w == other.w;
        }

        public override readonly string ToString()
        {
            return ToString(null, null);
        }

        public readonly string ToString(string format)
        {
            return ToString(format, null);
        }

        // Spec §0/§3: template "({0}, {1}, {2}, {3})", default format "F2", invariant number format when provider is null.
        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F2";
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return string.Format("({0}, {1}, {2}, {3})",
                x.ToString(format, formatProvider),
                y.ToString(format, formatProvider),
                z.ToString(format, formatProvider),
                w.ToString(format, formatProvider));
        }

        #endregion

        #region Static members

        // Spec §1/§3: t = Clamp01(t); component = a + (b - a) * t.
        public static Vector4 Lerp(Vector4 a, Vector4 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector4(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t,
                a.w + (b.w - a.w) * t);
        }

        public static Vector4 Lerp(in Vector4 a, in Vector4 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector4(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t,
                a.w + (b.w - a.w) * t);
        }

        public static Vector4 LerpUnclamped(Vector4 a, Vector4 b, float t)
        {
            return new Vector4(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t,
                a.w + (b.w - a.w) * t);
        }

        public static Vector4 LerpUnclamped(in Vector4 a, in Vector4 b, float t)
        {
            return new Vector4(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t,
                a.w + (b.w - a.w) * t);
        }

        // Spec §3: four-component version of the Vector2 MoveTowards algorithm (§1). A negative maxDistanceDelta moves away.
        public static Vector4 MoveTowards(Vector4 current, Vector4 target, float maxDistanceDelta)
        {
            float toX = target.x - current.x;
            float toY = target.y - current.y;
            float toZ = target.z - current.z;
            float toW = target.w - current.w;

            float sqDist = toX * toX + toY * toY + toZ * toZ + toW * toW;

            if (sqDist == 0 || (maxDistanceDelta >= 0 && sqDist <= maxDistanceDelta * maxDistanceDelta))
                return target;

            float dist = (float)Math.Sqrt(sqDist);

            return new Vector4(
                current.x + toX / dist * maxDistanceDelta,
                current.y + toY / dist * maxDistanceDelta,
                current.z + toZ / dist * maxDistanceDelta,
                current.w + toW / dist * maxDistanceDelta);
        }

        public static Vector4 MoveTowards(in Vector4 current, in Vector4 target, float maxDistanceDelta)
        {
            return MoveTowards(current, target, maxDistanceDelta);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Scale(Vector4 a, Vector4 b)
        {
            return new Vector4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Scale(in Vector4 a, in Vector4 b)
        {
            return new Vector4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        }

        // Spec §3: mag = Magnitude(a); mag > kEpsilon ? a / mag : zero.
        public static Vector4 Normalize(Vector4 a)
        {
            float mag = Magnitude(a);
            if (mag > kEpsilon)
                return a / mag;
            return zero;
        }

        public static Vector4 Normalize(in Vector4 a)
        {
            float mag = Magnitude(a);
            if (mag > kEpsilon)
                return a / mag;
            return zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector4 a, Vector4 b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(in Vector4 a, in Vector4 b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        }

        // Spec §3: b * (Dot(a, b) / Dot(b, b)) with NO zero guard (NaN for zero b).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Project(Vector4 a, Vector4 b)
        {
            return b * (Dot(a, b) / Dot(b, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Project(in Vector4 a, in Vector4 b)
        {
            return b * (Dot(a, b) / Dot(b, b));
        }

        // Spec §3: Distance(a, b) = Magnitude(a - b).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Vector4 a, Vector4 b)
        {
            return Magnitude(a - b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(in Vector4 a, in Vector4 b)
        {
            return Magnitude(a - b);
        }

        // Spec §3: f32(Math.Sqrt(Dot(a, a))).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Magnitude(Vector4 a)
        {
            return (float)Math.Sqrt(Dot(a, a));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Magnitude(in Vector4 a)
        {
            return (float)Math.Sqrt(Dot(a, a));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrMagnitude(Vector4 a)
        {
            return Dot(a, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrMagnitude(in Vector4 a)
        {
            return Dot(a, a);
        }

        // Componentwise Mathf.Min / Mathf.Max (NaN ordering follows Mathf, spec §11).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Min(Vector4 lhs, Vector4 rhs)
        {
            return new Vector4(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y), Mathf.Min(lhs.z, rhs.z), Mathf.Min(lhs.w, rhs.w));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Min(in Vector4 lhs, in Vector4 rhs)
        {
            return new Vector4(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y), Mathf.Min(lhs.z, rhs.z), Mathf.Min(lhs.w, rhs.w));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Max(Vector4 lhs, Vector4 rhs)
        {
            return new Vector4(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y), Mathf.Max(lhs.z, rhs.z), Mathf.Max(lhs.w, rhs.w));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Max(in Vector4 lhs, in Vector4 rhs)
        {
            return new Vector4(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y), Mathf.Max(lhs.z, rhs.z), Mathf.Max(lhs.w, rhs.w));
        }

        #endregion

        #region Operators

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator +(Vector4 a, Vector4 b)
        {
            return new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 a, Vector4 b)
        {
            return new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 a)
        {
            return new Vector4(-a.x, -a.y, -a.z, -a.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Vector4 a, float d)
        {
            return new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(float d, Vector4 a)
        {
            return new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator /(Vector4 a, float d)
        {
            return new Vector4(a.x / d, a.y / d, a.z / d, a.w / d);
        }

        // Spec §3: squared 4-component difference < kEpsilon * kEpsilon (false when any NaN is involved).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector4 lhs, Vector4 rhs)
        {
            float diffx = lhs.x - rhs.x;
            float diffy = lhs.y - rhs.y;
            float diffz = lhs.z - rhs.z;
            float diffw = lhs.w - rhs.w;
            float sqrmag = diffx * diffx + diffy * diffy + diffz * diffz + diffw * diffw;
            return sqrmag < kEpsilon * kEpsilon;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vector4 lhs, Vector4 rhs)
        {
            return !(lhs == rhs);
        }

        #endregion

        #region Conversions (declared on Vector4 per spec §3; Vector4<->Color live on Color)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(Vector3 v)
        {
            return new Vector4(v.x, v.y, v.z, 0.0F);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3(Vector4 v)
        {
            return new Vector3(v.x, v.y, v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(Vector2 v)
        {
            return new Vector4(v.x, v.y, 0.0F, 0.0F);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(Vector4 v)
        {
            return new Vector2(v.x, v.y);
        }

        #endregion
    }
}
