// Mirrors UnityEngine.Quaternion for the NowUI standalone build; behavioural spec: Docs/Standalone/UnityValueTypeSemantics.md (§9).
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Quaternion : IEquatable<Quaternion>, IFormattable
    {
        // Spec §9: 1e-6, ten times smaller than the vector epsilon.
        public const float kEpsilon = 0.000001F;

        // Declared order matters: native code reinterprets Quaternion[] as float* (spec §0).
        public float x;
        public float y;
        public float z;
        public float w;

        #region Constructor / Set / indexer

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(float newX, float newY, float newZ, float newW)
        {
            x = newX;
            y = newY;
            z = newZ;
            w = newW;
        }

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
                        throw new IndexOutOfRangeException("Invalid Quaternion index!");
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
                        throw new IndexOutOfRangeException("Invalid Quaternion index!");
                }
            }
        }

        #endregion

        #region Static presets

        private static readonly Quaternion identityQuaternion = new Quaternion(0F, 0F, 0F, 1F);

        public static Quaternion identity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => identityQuaternion; }

        #endregion

        #region Operators

        // Spec §9: Hamilton product, lhs applied after rhs.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion operator *(Quaternion lhs, Quaternion rhs)
        {
            return new Quaternion(
                lhs.w * rhs.x + lhs.x * rhs.w + lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.w * rhs.y + lhs.y * rhs.w + lhs.z * rhs.x - lhs.x * rhs.z,
                lhs.w * rhs.z + lhs.z * rhs.w + lhs.x * rhs.y - lhs.y * rhs.x,
                lhs.w * rhs.w - lhs.x * rhs.x - lhs.y * rhs.y - lhs.z * rhs.z);
        }

        // Spec §9: rotate a point (assumes unit rotation). [verified] Euler(0,90,0) * forward = (0.99999994, 0, 5.96e-8).
        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            float x2 = rotation.x * 2F;
            float y2 = rotation.y * 2F;
            float z2 = rotation.z * 2F;
            float xx = rotation.x * x2;
            float yy = rotation.y * y2;
            float zz = rotation.z * z2;
            float xy = rotation.x * y2;
            float xz = rotation.x * z2;
            float yz = rotation.y * z2;
            float wx = rotation.w * x2;
            float wy = rotation.w * y2;
            float wz = rotation.w * z2;

            Vector3 res;
            res.x = (1F - (yy + zz)) * point.x + (xy - wz) * point.y + (xz + wy) * point.z;
            res.y = (xy + wz) * point.x + (1F - (xx + zz)) * point.y + (yz - wx) * point.z;
            res.z = (xz - wy) * point.x + (yz + wx) * point.y + (1F - (xx + yy)) * point.z;
            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEqualUsingDot(float dot)
        {
            // Spec §9: Dot(lhs, rhs) > 1 - kEpsilon; q == -q is false; only meaningful for unit quaternions.
            return dot > 1.0F - kEpsilon;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Quaternion lhs, Quaternion rhs)
        {
            return IsEqualUsingDot(Dot(lhs, rhs));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Quaternion lhs, Quaternion rhs)
        {
            return !IsEqualUsingDot(Dot(lhs, rhs));
        }

        #endregion

        #region Managed static members (spec §9 "Instance/static managed members")

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Quaternion a, Quaternion b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(in Quaternion a, in Quaternion b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        }

        // Spec §9: dot = Min(Abs(Dot(a,b)), 1); (dot > 1 - kEpsilon) ? 0 : Acos(dot) * 2 * Rad2Deg.
        // [verified] Angle(q, -q) = 0; Angle(identity, Euler(0,180,0)) = 180.
        public static float Angle(Quaternion a, Quaternion b)
        {
            float dot = Mathf.Min(Mathf.Abs(Dot(a, b)), 1.0F);
            return IsEqualUsingDot(dot) ? 0.0F : Mathf.Acos(dot) * 2.0F * Mathf.Rad2Deg;
        }

        public static float Angle(in Quaternion a, in Quaternion b)
        {
            float dot = Mathf.Min(Mathf.Abs(Dot(a, b)), 1.0F);
            return IsEqualUsingDot(dot) ? 0.0F : Mathf.Acos(dot) * 2.0F * Mathf.Rad2Deg;
        }

        // Spec §9: angle = Angle(from, to); angle == 0 ? to : SlerpUnclamped(from, to, Min(1, maxDegreesDelta / angle)).
        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegreesDelta)
        {
            float angle = Angle(from, to);
            if (angle == 0.0F)
                return to;
            return SlerpUnclamped(from, to, Mathf.Min(1.0F, maxDegreesDelta / angle));
        }

        public static Quaternion RotateTowards(in Quaternion from, in Quaternion to, float maxDegreesDelta)
        {
            return RotateTowards(from, to, maxDegreesDelta);
        }

        // Spec §9: mag = Mathf.Sqrt(Dot(q, q)); mag < Mathf.Epsilon ? identity : q / mag per component.
        // Because Dot is a float, tiny quaternions underflow ((1e-30,0,0,0) → identity); NaN propagates.
        public static Quaternion Normalize(Quaternion q)
        {
            float mag = Mathf.Sqrt(Dot(q, q));
            if (mag < Mathf.Epsilon)
                return identity;
            return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
        }

        public static Quaternion Normalize(in Quaternion q)
        {
            float mag = Mathf.Sqrt(Dot(q, q));
            if (mag < Mathf.Epsilon)
                return identity;
            return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
        }

        public void Normalize()
        {
            this = Normalize(this);
        }

        public readonly Quaternion normalized
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Normalize(this);
        }

        // Spec §9: Inverse is the plain conjugate with no normalisation, for any input.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Inverse(Quaternion rotation)
        {
            return new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Inverse(in Quaternion rotation)
        {
            return new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w);
        }

        #endregion

        #region Lerp / Slerp (native in Unity; spec §9 "Native members")

        // Spec §9: Lerp(a, b, t) = LerpUnclamped(a, b, Clamp01(t)).
        public static Quaternion Lerp(Quaternion a, Quaternion b, float t)
        {
            return LerpUnclamped(a, b, Mathf.Clamp01(t));
        }

        public static Quaternion Lerp(in Quaternion a, in Quaternion b, float t)
        {
            return LerpUnclamped(a, b, Mathf.Clamp01(t));
        }

        // Spec §9: if Dot(a,b) < 0 interpolate toward -b (a + t*(-b - a)) else a + t*(b - a); then normalise by
        // sqrt(Dot(q,q)) with no epsilon guard (non-unit inputs come out unit; Lerp((0,0,0,2), …, 0) = identity).
        public static Quaternion LerpUnclamped(Quaternion a, Quaternion b, float t)
        {
            Quaternion q;
            if (Dot(a, b) < 0.0F)
            {
                q.x = a.x + t * (-b.x - a.x);
                q.y = a.y + t * (-b.y - a.y);
                q.z = a.z + t * (-b.z - a.z);
                q.w = a.w + t * (-b.w - a.w);
            }
            else
            {
                q.x = a.x + t * (b.x - a.x);
                q.y = a.y + t * (b.y - a.y);
                q.z = a.z + t * (b.z - a.z);
                q.w = a.w + t * (b.w - a.w);
            }

            // Division (not multiply-by-reciprocal): with a unit input whose Dot rounds to 1 ± 1 ulp the sqrt is
            // exactly 1, so Slerp/Lerp(q, ±q, t) hands back q bit-for-bit as the spec observes.
            float mag = MathF.Sqrt(Dot(q, q));
            q.x /= mag;
            q.y /= mag;
            q.z /= mag;
            q.w /= mag;
            return q;
        }

        public static Quaternion LerpUnclamped(in Quaternion a, in Quaternion b, float t)
        {
            return LerpUnclamped(a, b, t);
        }

        // Spec §9: Slerp(a, b, t) = SlerpUnclamped(a, b, Clamp01(t)).
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            return SlerpUnclamped(a, b, Mathf.Clamp01(t));
        }

        public static Quaternion Slerp(in Quaternion a, in Quaternion b, float t)
        {
            return SlerpUnclamped(a, b, Mathf.Clamp01(t));
        }

        // Spec §9: dot = Dot(a,b); if dot < 0 { dot = -dot; b = -b }; if dot < 0.95: sine formula with no final
        // normalisation, else normalised Lerp(a, b, t). Handles q vs -q (returns a), 180° pairs (short way) and
        // extrapolation (SlerpUnclamped(identity, Euler(0,120,0), 1.5) = (0, 1, 0, -3.4e-08)).
        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t)
        {
            float dot = Dot(a, b);
            if (dot < 0.0F)
            {
                dot = -dot;
                b.x = -b.x;
                b.y = -b.y;
                b.z = -b.z;
                b.w = -b.w;
            }

            if (dot < 0.95F)
            {
                float angle = MathF.Acos(dot);
                float invSin = 1.0F / MathF.Sin(angle);
                float sa = MathF.Sin(angle * t);
                float sb = MathF.Sin(angle * (1.0F - t));
                return new Quaternion(
                    (a.x * sb + b.x * sa) * invSin,
                    (a.y * sb + b.y * sa) * invSin,
                    (a.z * sb + b.z * sa) * invSin,
                    (a.w * sb + b.w * sa) * invSin);
            }

            // Normalised lerp fallback. b has already been flipped when needed, so LerpUnclamped's own Dot(a,b) < 0
            // test does not fire again. LerpUnclamped (not the clamped Lerp): SlerpUnclamped is the unclamped entry
            // point (Slerp is defined as SlerpUnclamped(a, b, Clamp01(t))), so clamping here would silently stop
            // extrapolating for every near-parallel pair and make the member discontinuous across dot = 0.95.
            return LerpUnclamped(a, b, t);
        }

        public static Quaternion SlerpUnclamped(in Quaternion a, in Quaternion b, float t)
        {
            return SlerpUnclamped(a, b, t);
        }

        #endregion

        #region Euler angles (native in Unity; spec §9 "Native members")

        public Vector3 eulerAngles
        {
            // Spec §9: MakePositive(ToEulerRad(this) * Rad2Deg).
            readonly get => MakePositive(ToEulerRad(this) * Mathf.Rad2Deg);
            // Spec §9: this = FromEulerRad(value * Deg2Rad).
            set => this = FromEulerRad(value * Mathf.Deg2Rad);
        }

        // Spec §9: Euler(x, y, z) = FromEulerRad((x*Deg2Rad, y*Deg2Rad, z*Deg2Rad)).
        public static Quaternion Euler(float x, float y, float z)
        {
            return FromEulerRad(new Vector3(x * Mathf.Deg2Rad, y * Mathf.Deg2Rad, z * Mathf.Deg2Rad));
        }

        // Spec §9: Euler(Vector3 e) = FromEulerRad(e * Deg2Rad) — bit-identical to the 3-float form [verified].
        public static Quaternion Euler(Vector3 euler)
        {
            return FromEulerRad(euler * Mathf.Deg2Rad);
        }

        public static Quaternion Euler(in Vector3 euler)
        {
            return FromEulerRad(euler * Mathf.Deg2Rad);
        }

        // Spec §9: ZXY order. With half angles qX = (sin(x/2),0,0,cos(x/2)), qY = (0,sin(y/2),0,cos(y/2)),
        // qZ = (0,0,sin(z/2),cos(z/2)) the result is (qY * qX) * qZ. Trig is single precision (native sinf/cosf);
        // the probe matched this product bit-for-bit in most cases and within 1 ulp otherwise.
        private static Quaternion FromEulerRad(Vector3 euler)
        {
            float hx = euler.x * 0.5F;
            float hy = euler.y * 0.5F;
            float hz = euler.z * 0.5F;

            Quaternion qX = new Quaternion(MathF.Sin(hx), 0F, 0F, MathF.Cos(hx));
            Quaternion qY = new Quaternion(0F, MathF.Sin(hy), 0F, MathF.Cos(hy));
            Quaternion qZ = new Quaternion(0F, 0F, MathF.Sin(hz), MathF.Cos(hz));

            return (qY * qX) * qZ;
        }

        // Spec §9: inverse of FromEulerRad in ZXY order, in radians (MakePositive is applied by eulerAngles, not here).
        // Scale-invariant: the input is normalised first, so (0,0,0,0) and (0,0,0,2) both give (0,0,0).
        // Standard formula for a unit quaternion: build the rotation matrix, x = asin(clamp(-m12, -1, 1));
        // not singular: y = atan2(m02, m22), z = atan2(m10, m11); singular (|m12| ≈ 1): y = atan2(-m20, m00), z = 0.
        private static Vector3 ToEulerRad(Quaternion q)
        {
            q = Normalize(q);

            // Diagonal entries use the "xx - yy - zz + ww" form (not "1 - 2(yy + zz)"): of the orderings tried, this is
            // the one that reproduces the spec's round-trip oracles bit-for-bit (Euler(10,20,30) → (9.999999, 20.000002,
            // 30.000002); Euler(90,45,0) → exactly (90, 45, 0); Euler(89.99,45,0) → (90, 45.000004, 0)).
            float sxx = q.x * q.x;
            float syy = q.y * q.y;
            float szz = q.z * q.z;
            float sww = q.w * q.w;
            float m00 = sxx - syy - szz + sww;
            float m11 = syy - sxx - szz + sww;
            float m22 = szz - sxx - syy + sww;

            float x2 = q.x * 2F;
            float y2 = q.y * 2F;
            float z2 = q.z * 2F;
            float xy = q.x * y2;
            float xz = q.x * z2;
            float yz = q.y * z2;
            float wx = q.w * x2;
            float wy = q.w * y2;
            float wz = q.w * z2;

            float m02 = xz + wy;
            float m10 = xy + wz;
            float m20 = xz - wy;

            // sinX == -m12 == -(yz - wx); written as wx - yz so a zero rotation yields +0 rather than -0.
            float sinX = wx - yz;
            if (sinX > 1F) sinX = 1F;
            if (sinX < -1F) sinX = -1F;

            Vector3 euler;
            euler.x = MathF.Asin(sinX);

            // unverified vs Unity: exact singular threshold. The spec observes the branch firing when |sin x| rounds
            // to 1 in float (Euler(89.99,45,0) → (90, 45.000004, 0)), so the test is "|sin x| reached 1 after the
            // float matrix build" rather than a looser tolerance.
            if (sinX >= 1F || sinX <= -1F)
            {
                euler.y = MathF.Atan2(-m20, m00);
                euler.z = 0F;
            }
            else
            {
                euler.y = MathF.Atan2(m02, m22);
                euler.z = MathF.Atan2(m10, m11);
            }
            return euler;
        }

        // Spec §9: negativeFlip = -0.0001 * Rad2Deg (≈ -0.0057296°), positiveFlip = 360 + negativeFlip; per component
        // add 360 if below negativeFlip, subtract 360 if above positiveFlip. Tiny negatives are NOT wrapped
        // ([verified] Euler(0,-0.001,0).eulerAngles.y = -0.001; Euler(0,359.999,0) → -0.00100085).
        private static Vector3 MakePositive(Vector3 euler)
        {
            float negativeFlip = -0.0001F * Mathf.Rad2Deg;
            float positiveFlip = 360.0F + negativeFlip;

            if (euler.x < negativeFlip)
                euler.x += 360.0F;
            else if (euler.x > positiveFlip)
                euler.x -= 360.0F;

            if (euler.y < negativeFlip)
                euler.y += 360.0F;
            else if (euler.y > positiveFlip)
                euler.y -= 360.0F;

            if (euler.z < negativeFlip)
                euler.z += 360.0F;
            else if (euler.z > positiveFlip)
                euler.z -= 360.0F;

            return euler;
        }

        #endregion

        #region Angle/axis (native in Unity; spec §9 "Native members")

        // Spec §9: axis normalised internally; if axis.magnitude <= 1e-6 (or NaN) the result is identity; otherwise
        // half = degrees * Deg2Rad * 0.5; s = sin(half) / |axis|; result = (s*axis.x, s*axis.y, s*axis.z, cos(half)).
        // [verified] AngleAxis(180, up) = (0, 1, 0, -4.371139e-08); 1e-6 axis → identity, 2e-6 → rotation.
        public static Quaternion AngleAxis(float angle, Vector3 axis)
        {
            float mag = axis.magnitude;
            if (!(mag > 1e-6F))
                return identity;

            float half = angle * Mathf.Deg2Rad * 0.5F;
            float s = MathF.Sin(half) / mag;
            return new Quaternion(s * axis.x, s * axis.y, s * axis.z, MathF.Cos(half));
        }

        public static Quaternion AngleAxis(float angle, in Vector3 axis)
        {
            return AngleAxis(angle, (Vector3)axis);
        }

        // Spec §9: native axis/angle in radians, then angle *= Rad2Deg. [verified] identity → 0, (1,0,0);
        // (0,0,0,-1) → 360, (1,0,0); Euler(0,90,0) → 89.99999°, axis (0, 1.0000001, 0) (axis not exactly unit).
        public readonly void ToAngleAxis(out float angle, out Vector3 axis)
        {
            ToAxisAngleRad(this, out axis, out angle);
            angle *= Mathf.Rad2Deg;
        }

        // unverified vs Unity: exact native formula. Reconstructed as len = |(x,y,z)|, angle = 2*atan2(len, w),
        // axis = (x,y,z)/len with the (1,0,0) fallback when len is zero. This reproduces the identity and (0,0,0,-1)
        // oracles exactly; for Euler(0,90,0) it yields the mathematically exact 90° / (0,1,0) where the probe saw
        // 89.99999° / (0, 1.0000001, 0) — a 1-ulp artefact of the native normalisation that no correctly rounded
        // sqrt/acos/asin/atan2 combination reproduces (several were tried).
        private static void ToAxisAngleRad(Quaternion q, out Vector3 axis, out float angle)
        {
            float len = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
            angle = 2F * MathF.Atan2(len, q.w);

            if (!(len > 0F))
            {
                axis = new Vector3(1F, 0F, 0F);
                return;
            }

            axis = new Vector3(q.x / len, q.y / len, q.z / len);
        }

        #endregion

        #region LookRotation / FromToRotation (native in Unity; spec §9 "Native members")

        public static Quaternion LookRotation(Vector3 forward)
        {
            return LookRotation(forward, Vector3.up);
        }

        // Spec §9: rotation whose z axis is normalize(forward) and whose y axis is up re-orthogonalised
        // (right = normalize(cross(up, forward)), up' = cross(forward, right)). Zero forward → identity.
        // [verified] (forward, up) → identity; (right, up) → (0, 0.70710677, 0, 0.70710677); (-forward, up) → (0,1,0,0);
        // ((1,2,3), up) → (-0.27465674, 0.15385647, 0.04457066, 0.94810617);
        // ((1,2,3), (0,1,1)) → (-0.19896805, 0.24396692, 0.38962165, 0.865498); (up, up) → (-0.70710677, 0, 0, 0.70710677).
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards)
        {
            float fx = forward.x, fy = forward.y, fz = forward.z;
            float fmag = MathF.Sqrt(fx * fx + fy * fy + fz * fz);
            if (fmag == 0F)
                return identity; // Unity also logs "Look rotation viewing vector is zero".
            fx /= fmag;
            fy /= fmag;
            fz /= fmag;

            // right = cross(up, forward)
            float rx = upwards.y * fz - upwards.z * fy;
            float ry = upwards.z * fx - upwards.x * fz;
            float rz = upwards.x * fy - upwards.y * fx;
            float rmag = MathF.Sqrt(rx * rx + ry * ry + rz * rz);
            if (!(rmag > 1e-6F))
            {
                // unverified vs Unity: fallback basis when forward is parallel to up (or up is zero). The only probed
                // case, (up, up) → a -90° x rotation, equals the shortest rotation taking +z to forward, which is what
                // this fallback produces.
                return FromToRotation(new Vector3(0F, 0F, 1F), new Vector3(fx, fy, fz));
            }
            rx /= rmag;
            ry /= rmag;
            rz /= rmag;

            // up' = cross(forward, right)
            float ux = fy * rz - fz * ry;
            float uy = fz * rx - fx * rz;
            float uz = fx * ry - fy * rx;

            // Column basis: right, up', forward.
            Quaternion q = FromBasis(rx, ry, rz, ux, uy, uz, fx, fy, fz);

            // Canonicalise signed zeros: cross products of a basis carrying negative zeros (e.g. -Vector3.forward is
            // (-0,-0,-1)) propagate them into the quaternion, but every probed LookRotation result has +0 in the slots
            // that are zero — [verified] (-forward, up) = (0, 1, 0, 0), not (0, 1, -0, -0); also (forward, up),
            // (right, up) and (up, up). Adding +0 turns -0 into +0 and leaves every other value (NaN included)
            // untouched. // unverified vs Unity: whether the native path can ever emit -0 here.
            return new Quaternion(q.x + 0F, q.y + 0F, q.z + 0F, q.w + 0F);
        }

        public static Quaternion LookRotation(in Vector3 forward, in Vector3 upwards)
        {
            return LookRotation((Vector3)forward, (Vector3)upwards);
        }

        // Rotation-matrix → quaternion for an orthonormal right-handed basis given as columns (Shepperd's method:
        // pick the largest of trace / diagonal to keep the divisor well away from zero).
        // unverified vs Unity: last-ulp rounding of the native conversion.
        private static Quaternion FromBasis(
            float m00, float m10, float m20,
            float m01, float m11, float m21,
            float m02, float m12, float m22)
        {
            float trace = m00 + m11 + m22;
            Quaternion q;
            if (trace > 0F)
            {
                // s = sqrt(trace + 1) with a single shared 0.5/s reciprocal, not (sqrt*2) with three divides: the
                // reciprocal form is what reproduces LookRotation((1,2,3),(0,1,1)) = (-0.19896805 [0xBE4BBE48],
                // 0.24396692, 0.38962165 [0x3EC77C7D], 0.865498) bit-exactly; the divide form is 1 ulp low on x and z.
                // ((1,2,3), up) and (right, up) are bit-exact either way.
                float s = MathF.Sqrt(trace + 1F);
                float inv = 0.5F / s;
                q.w = s * 0.5F;
                q.x = (m21 - m12) * inv;
                q.y = (m02 - m20) * inv;
                q.z = (m10 - m01) * inv;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = MathF.Sqrt(1F + m00 - m11 - m22) * 2F;
                q.x = 0.25F * s;
                q.y = (m01 + m10) / s;
                q.z = (m02 + m20) / s;
                q.w = (m21 - m12) / s;
            }
            else if (m11 > m22)
            {
                float s = MathF.Sqrt(1F + m11 - m00 - m22) * 2F;
                q.y = 0.25F * s;
                q.x = (m01 + m10) / s;
                q.z = (m12 + m21) / s;
                q.w = (m02 - m20) / s;
            }
            else
            {
                float s = MathF.Sqrt(1F + m22 - m00 - m11) * 2F;
                q.z = 0.25F * s;
                q.x = (m02 + m20) / s;
                q.y = (m12 + m21) / s;
                q.w = (m10 - m01) / s;
            }
            return q;
        }

        // Spec §9: shortest rotation taking direction from to direction to; inputs normalised; zero input → identity;
        // antiparallel inputs pick a perpendicular axis ((right, left) → (0,1,0,0); (up, down) → (1,0,-0,0)).
        // [verified] ((1,2,3),(3,2,1)) → (-0.15430337, 0.30860674, -0.15430337, 0.9258201).
        public static Quaternion FromToRotation(Vector3 fromDirection, Vector3 toDirection)
        {
            float ax = fromDirection.x, ay = fromDirection.y, az = fromDirection.z;
            float bx = toDirection.x, by = toDirection.y, bz = toDirection.z;

            float amag = MathF.Sqrt(ax * ax + ay * ay + az * az);
            float bmag = MathF.Sqrt(bx * bx + by * by + bz * bz);
            if (amag == 0F || bmag == 0F)
                return identity;
            ax /= amag; ay /= amag; az /= amag;
            bx /= bmag; by /= bmag; bz /= bmag;

            // q = (cross(a, b), 1 + dot(a, b)) normalised — the half-angle construction of the shortest arc.
            float cx = ay * bz - az * by;
            float cy = az * bx - ax * bz;
            float cz = ax * by - ay * bx;
            float cw = 1F + (ax * bx + ay * by + az * bz);

            float lenSq = cx * cx + cy * cy + cz * cz + cw * cw;
            if (lenSq > 1e-12F)
            {
                float inv = 1F / MathF.Sqrt(lenSq);
                return new Quaternion(cx * inv, cy * inv, cz * inv, cw * inv);
            }

            // Antiparallel: 180° about an axis perpendicular to `from`. unverified vs Unity: the exact axis choice
            // and sign convention. Reconstructed from the probe: right→left rotates about +y, up→down about +x, which
            // matches "cross the coordinate axis of the smallest |component| (ties → last) with `from`, then make the
            // dominant component positive".
            float absX = MathF.Abs(ax), absY = MathF.Abs(ay), absZ = MathF.Abs(az);
            float hx = 0F, hy = 0F, hz = 0F;
            if (absX < absY && absX < absZ) hx = 1F;
            else if (absY < absZ) hy = 1F;
            else hz = 1F;

            // axis = cross(helper, from)
            float px = hy * az - hz * ay;
            float py = hz * ax - hx * az;
            float pz = hx * ay - hy * ax;
            float pinv = 1F / MathF.Sqrt(px * px + py * py + pz * pz);
            px *= pinv; py *= pinv; pz *= pinv;

            float dominant = px;
            if (MathF.Abs(py) > MathF.Abs(dominant)) dominant = py;
            if (MathF.Abs(pz) > MathF.Abs(dominant)) dominant = pz;
            if (dominant < 0F)
            {
                px = -px; py = -py; pz = -pz;
            }
            return new Quaternion(px, py, pz, 0F);
        }

        public static Quaternion FromToRotation(in Vector3 fromDirection, in Vector3 toDirection)
        {
            return FromToRotation((Vector3)fromDirection, (Vector3)toDirection);
        }

        public void SetLookRotation(Vector3 view)
        {
            this = LookRotation(view, Vector3.up);
        }

        public void SetLookRotation(Vector3 view, Vector3 up)
        {
            this = LookRotation(view, up);
        }

        public void SetFromToRotation(Vector3 fromDirection, Vector3 toDirection)
        {
            this = FromToRotation(fromDirection, toDirection);
        }

        #endregion

        #region Equality / hashing / formatting

        // Spec §9: GetHashCode as Vector4 (x ^ (y<<2) ^ (z>>2) ^ (w>>1) of the component hash codes).
        public override readonly int GetHashCode()
        {
            return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1);
        }

        public override readonly bool Equals(object other)
        {
            return other is Quaternion q && Equals(q);
        }

        // Spec §0/§9: float.Equals on all four (NaN-equal), unlike the vector types.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(Quaternion other)
        {
            return x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(in Quaternion other)
        {
            return x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);
        }

        public override readonly string ToString()
        {
            return ToString(null, null);
        }

        public readonly string ToString(string format)
        {
            return ToString(format, null);
        }

        // Spec §0/§9: template "({0}, {1}, {2}, {3})", default format "F5", invariant number format when provider is null.
        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F5";
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return string.Format("({0}, {1}, {2}, {3})",
                x.ToString(format, formatProvider),
                y.ToString(format, formatProvider),
                z.ToString(format, formatProvider),
                w.ToString(format, formatProvider));
        }

        #endregion

        #region Obsolete radian-based API (spec §9; thin wrappers over the radian functions, NowUI does not use them)

        // unverified vs Unity: these obsolete wrappers are reconstructed from their names and the spec's one-line
        // description ("thin wrappers over the native radian functions"; AxisAngle(axis, rad) = AngleAxis(Rad2Deg * rad, axis)).

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerRotation(float x, float y, float z)
        {
            return FromEulerRad(new Vector3(x, y, z));
        }

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerRotation(Vector3 euler)
        {
            return FromEulerRad(euler);
        }

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerRotation(float x, float y, float z)
        {
            this = FromEulerRad(new Vector3(x, y, z));
        }

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerRotation(Vector3 euler)
        {
            this = FromEulerRad(euler);
        }

        [Obsolete("Use Quaternion.eulerAngles instead. This function was deprecated because it uses radians instead of degrees.")]
        public readonly Vector3 ToEuler()
        {
            return ToEulerRad(this);
        }

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerAngles(float x, float y, float z)
        {
            return FromEulerRad(new Vector3(x, y, z));
        }

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerAngles(Vector3 euler)
        {
            return FromEulerRad(euler);
        }

        [Obsolete("Use Quaternion.ToAngleAxis instead. This function was deprecated because it uses radians instead of degrees.")]
        public readonly void ToAxisAngle(out Vector3 axis, out float angle)
        {
            ToAxisAngleRad(this, out axis, out angle);
        }

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerAngles(float x, float y, float z)
        {
            this = FromEulerRad(new Vector3(x, y, z));
        }

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerAngles(Vector3 euler)
        {
            this = FromEulerRad(euler);
        }

        [Obsolete("Use Quaternion.eulerAngles instead. This function was deprecated because it uses radians instead of degrees.")]
        public readonly Vector3 ToEulerAngles()
        {
            return ToEulerRad(this);
        }

        [Obsolete("Use Quaternion.AngleAxis instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetAxisAngle(Vector3 axis, float angle)
        {
            this = AxisAngle(axis, angle);
        }

        [Obsolete("Use Quaternion.AngleAxis instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion AxisAngle(Vector3 axis, float angle)
        {
            return AngleAxis(Mathf.Rad2Deg * angle, axis);
        }

        #endregion
    }
}
