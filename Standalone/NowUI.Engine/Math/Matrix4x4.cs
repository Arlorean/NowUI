// Mirrors UnityEngine.Matrix4x4 (and UnityEngine.FrustumPlanes) for the NowUI standalone build; spec: Docs/Standalone/UnityValueTypeSemantics.md (section 8).
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>Six clip-space planes recovered from / fed into a projection matrix (spec section 8, "FrustumPlanes").</summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct FrustumPlanes
    {
        public float left;
        public float right;
        public float bottom;
        public float top;
        public float zNear;
        public float zFar;
    }

    /// <summary>
    /// 4x4 float matrix. Sixteen public floats named mRC (R = row, C = column) declared in column-major memory order so
    /// that unsafe reinterpretation / serialisation matches Unity (spec section 8, "Storage").
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Matrix4x4 : IEquatable<Matrix4x4>, IFormattable
    {
        // Column 0
        public float m00;
        public float m10;
        public float m20;
        public float m30;
        // Column 1
        public float m01;
        public float m11;
        public float m21;
        public float m31;
        // Column 2
        public float m02;
        public float m12;
        public float m22;
        public float m32;
        // Column 3
        public float m03;
        public float m13;
        public float m23;
        public float m33;

        // ------------------------------------------------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>Each Vector4 is a column (spec section 8, "Constructor").</summary>
        public Matrix4x4(Vector4 column0, Vector4 column1, Vector4 column2, Vector4 column3)
        {
            m00 = column0.x; m10 = column0.y; m20 = column0.z; m30 = column0.w;
            m01 = column1.x; m11 = column1.y; m21 = column1.z; m31 = column1.w;
            m02 = column2.x; m12 = column2.y; m22 = column2.z; m32 = column2.w;
            m03 = column3.x; m13 = column3.y; m23 = column3.z; m33 = column3.w;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Indexers / accessors
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>this[row, column] = this[row + column * 4] (spec section 8, "Indexers").</summary>
        public float this[int row, int column]
        {
            readonly get => this[row + column * 4];
            set => this[row + column * 4] = value;
        }

        /// <summary>Index 0..15 walks the fields in memory (column-major) order.</summary>
        public float this[int index]
        {
            readonly get
            {
                switch (index)
                {
                    case 0: return m00;
                    case 1: return m10;
                    case 2: return m20;
                    case 3: return m30;
                    case 4: return m01;
                    case 5: return m11;
                    case 6: return m21;
                    case 7: return m31;
                    case 8: return m02;
                    case 9: return m12;
                    case 10: return m22;
                    case 11: return m32;
                    case 12: return m03;
                    case 13: return m13;
                    case 14: return m23;
                    case 15: return m33;
                    default: throw new IndexOutOfRangeException("Invalid matrix index!");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: m00 = value; break;
                    case 1: m10 = value; break;
                    case 2: m20 = value; break;
                    case 3: m30 = value; break;
                    case 4: m01 = value; break;
                    case 5: m11 = value; break;
                    case 6: m21 = value; break;
                    case 7: m31 = value; break;
                    case 8: m02 = value; break;
                    case 9: m12 = value; break;
                    case 10: m22 = value; break;
                    case 11: m32 = value; break;
                    case 12: m03 = value; break;
                    case 13: m13 = value; break;
                    case 14: m23 = value; break;
                    case 15: m33 = value; break;
                    default: throw new IndexOutOfRangeException("Invalid matrix index!");
                }
            }
        }

        public readonly Vector4 GetColumn(int index)
        {
            switch (index)
            {
                case 0: return new Vector4(m00, m10, m20, m30);
                case 1: return new Vector4(m01, m11, m21, m31);
                case 2: return new Vector4(m02, m12, m22, m32);
                case 3: return new Vector4(m03, m13, m23, m33);
                default: throw new IndexOutOfRangeException("Invalid column index!");
            }
        }

        public readonly Vector4 GetRow(int index)
        {
            switch (index)
            {
                case 0: return new Vector4(m00, m01, m02, m03);
                case 1: return new Vector4(m10, m11, m12, m13);
                case 2: return new Vector4(m20, m21, m22, m23);
                case 3: return new Vector4(m30, m31, m32, m33);
                default: throw new IndexOutOfRangeException("Invalid row index!");
            }
        }

        /// <summary>Goes through the [row, column] indexer, so a bad index throws "Invalid matrix index!".</summary>
        public void SetColumn(int index, Vector4 column)
        {
            this[0, index] = column.x;
            this[1, index] = column.y;
            this[2, index] = column.z;
            this[3, index] = column.w;
        }

        /// <summary>Goes through the [row, column] indexer, so a bad index throws "Invalid matrix index!".</summary>
        public void SetRow(int index, Vector4 row)
        {
            this[index, 0] = row.x;
            this[index, 1] = row.y;
            this[index, 2] = row.z;
            this[index, 3] = row.w;
        }

        public readonly Vector3 GetPosition() => new Vector3(m03, m13, m23);

        // ------------------------------------------------------------------------------------------------------------
        // Statics
        // ------------------------------------------------------------------------------------------------------------

        private static readonly Matrix4x4 zeroMatrix = new Matrix4x4(
            new Vector4(0f, 0f, 0f, 0f),
            new Vector4(0f, 0f, 0f, 0f),
            new Vector4(0f, 0f, 0f, 0f),
            new Vector4(0f, 0f, 0f, 0f));

        private static readonly Matrix4x4 identityMatrix = new Matrix4x4(
            new Vector4(1f, 0f, 0f, 0f),
            new Vector4(0f, 1f, 0f, 0f),
            new Vector4(0f, 0f, 1f, 0f),
            new Vector4(0f, 0f, 0f, 1f));

        public static Matrix4x4 zero => zeroMatrix;
        public static Matrix4x4 identity => identityMatrix;

        // ------------------------------------------------------------------------------------------------------------
        // Equality / hashing (spec section 8, "Equality")
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>Hash of the four columns' Vector4 hashes: c0 ^ (c1 &lt;&lt; 2) ^ (c2 &gt;&gt; 2) ^ (c3 &gt;&gt; 1).</summary>
        public override readonly int GetHashCode()
        {
            return GetColumn(0).GetHashCode() ^ (GetColumn(1).GetHashCode() << 2) ^ (GetColumn(2).GetHashCode() >> 2) ^ (GetColumn(3).GetHashCode() >> 1);
        }

        public override readonly bool Equals(object other)
        {
            return other is Matrix4x4 m && Equals(m);
        }

        /// <summary>Exact per-column Vector4.Equals (C# == on floats, so NaN is never equal).</summary>
        public readonly bool Equals(Matrix4x4 other)
        {
            return GetColumn(0).Equals(other.GetColumn(0))
                && GetColumn(1).Equals(other.GetColumn(1))
                && GetColumn(2).Equals(other.GetColumn(2))
                && GetColumn(3).Equals(other.GetColumn(3));
        }

        public readonly bool Equals(in Matrix4x4 other) => Equals(other);

        /// <summary>Each column compared with the Vector4 tolerance (squared column difference &lt; 1e-10).</summary>
        public static bool operator ==(Matrix4x4 lhs, Matrix4x4 rhs)
        {
            return lhs.GetColumn(0) == rhs.GetColumn(0)
                && lhs.GetColumn(1) == rhs.GetColumn(1)
                && lhs.GetColumn(2) == rhs.GetColumn(2)
                && lhs.GetColumn(3) == rhs.GetColumn(3);
        }

        public static bool operator !=(Matrix4x4 lhs, Matrix4x4 rhs) => !(lhs == rhs);

        // ------------------------------------------------------------------------------------------------------------
        // Operators (spec section 8, "Operators")
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>Standard product; element [r,c] = lhs.mr0*rhs.m0c + lhs.mr1*rhs.m1c + lhs.mr2*rhs.m2c + lhs.mr3*rhs.m3c (left-to-right float sums).</summary>
        public static Matrix4x4 operator *(Matrix4x4 lhs, Matrix4x4 rhs)
        {
            Matrix4x4 res;
            res.m00 = lhs.m00 * rhs.m00 + lhs.m01 * rhs.m10 + lhs.m02 * rhs.m20 + lhs.m03 * rhs.m30;
            res.m01 = lhs.m00 * rhs.m01 + lhs.m01 * rhs.m11 + lhs.m02 * rhs.m21 + lhs.m03 * rhs.m31;
            res.m02 = lhs.m00 * rhs.m02 + lhs.m01 * rhs.m12 + lhs.m02 * rhs.m22 + lhs.m03 * rhs.m32;
            res.m03 = lhs.m00 * rhs.m03 + lhs.m01 * rhs.m13 + lhs.m02 * rhs.m23 + lhs.m03 * rhs.m33;

            res.m10 = lhs.m10 * rhs.m00 + lhs.m11 * rhs.m10 + lhs.m12 * rhs.m20 + lhs.m13 * rhs.m30;
            res.m11 = lhs.m10 * rhs.m01 + lhs.m11 * rhs.m11 + lhs.m12 * rhs.m21 + lhs.m13 * rhs.m31;
            res.m12 = lhs.m10 * rhs.m02 + lhs.m11 * rhs.m12 + lhs.m12 * rhs.m22 + lhs.m13 * rhs.m32;
            res.m13 = lhs.m10 * rhs.m03 + lhs.m11 * rhs.m13 + lhs.m12 * rhs.m23 + lhs.m13 * rhs.m33;

            res.m20 = lhs.m20 * rhs.m00 + lhs.m21 * rhs.m10 + lhs.m22 * rhs.m20 + lhs.m23 * rhs.m30;
            res.m21 = lhs.m20 * rhs.m01 + lhs.m21 * rhs.m11 + lhs.m22 * rhs.m21 + lhs.m23 * rhs.m31;
            res.m22 = lhs.m20 * rhs.m02 + lhs.m21 * rhs.m12 + lhs.m22 * rhs.m22 + lhs.m23 * rhs.m32;
            res.m23 = lhs.m20 * rhs.m03 + lhs.m21 * rhs.m13 + lhs.m22 * rhs.m23 + lhs.m23 * rhs.m33;

            res.m30 = lhs.m30 * rhs.m00 + lhs.m31 * rhs.m10 + lhs.m32 * rhs.m20 + lhs.m33 * rhs.m30;
            res.m31 = lhs.m30 * rhs.m01 + lhs.m31 * rhs.m11 + lhs.m32 * rhs.m21 + lhs.m33 * rhs.m31;
            res.m32 = lhs.m30 * rhs.m02 + lhs.m31 * rhs.m12 + lhs.m32 * rhs.m22 + lhs.m33 * rhs.m32;
            res.m33 = lhs.m30 * rhs.m03 + lhs.m31 * rhs.m13 + lhs.m32 * rhs.m23 + lhs.m33 * rhs.m33;
            return res;
        }

        /// <summary>Column-vector product: x = m00*v.x + m01*v.y + m02*v.z + m03*v.w, etc.</summary>
        public static Vector4 operator *(Matrix4x4 lhs, Vector4 vector)
        {
            Vector4 res;
            res.x = lhs.m00 * vector.x + lhs.m01 * vector.y + lhs.m02 * vector.z + lhs.m03 * vector.w;
            res.y = lhs.m10 * vector.x + lhs.m11 * vector.y + lhs.m12 * vector.z + lhs.m13 * vector.w;
            res.z = lhs.m20 * vector.x + lhs.m21 * vector.y + lhs.m22 * vector.z + lhs.m23 * vector.w;
            res.w = lhs.m30 * vector.x + lhs.m31 * vector.y + lhs.m32 * vector.z + lhs.m33 * vector.w;
            return res;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Transform helpers (spec section 8, "Transform helpers")
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>Full projective transform: w = 1 / (m30*x + m31*y + m32*z + m33) and the result is scaled by it, no guard.</summary>
        public readonly Vector3 MultiplyPoint(Vector3 point)
        {
            Vector3 res;
            float w;
            res.x = m00 * point.x + m01 * point.y + m02 * point.z + m03;
            res.y = m10 * point.x + m11 * point.y + m12 * point.z + m13;
            res.z = m20 * point.x + m21 * point.y + m22 * point.z + m23;
            w = m30 * point.x + m31 * point.y + m32 * point.z + m33;

            w = 1f / w;
            res.x *= w;
            res.y *= w;
            res.z *= w;
            return res;
        }

        /// <summary>Affine transform: as MultiplyPoint without the perspective divide.</summary>
        public readonly Vector3 MultiplyPoint3x4(Vector3 point)
        {
            Vector3 res;
            res.x = m00 * point.x + m01 * point.y + m02 * point.z + m03;
            res.y = m10 * point.x + m11 * point.y + m12 * point.z + m13;
            res.z = m20 * point.x + m21 * point.y + m22 * point.z + m23;
            return res;
        }

        /// <summary>Upper 3x3 only, no translation.</summary>
        public readonly Vector3 MultiplyVector(Vector3 vector)
        {
            Vector3 res;
            res.x = m00 * vector.x + m01 * vector.y + m02 * vector.z;
            res.y = m10 * vector.x + m11 * vector.y + m12 * vector.z;
            res.z = m20 * vector.x + m21 * vector.y + m22 * vector.z;
            return res;
        }

        /// <summary>Transforms a plane by this matrix: (a,b,c,d) = inverse^T * (n.x, n.y, n.z, distance).</summary>
        public readonly Plane TransformPlane(Plane plane)
        {
            Matrix4x4 it = inverse;

            float x = plane.normal.x;
            float y = plane.normal.y;
            float z = plane.normal.z;
            float w = plane.distance;

            float a = it.m00 * x + it.m10 * y + it.m20 * z + it.m30 * w;
            float b = it.m01 * x + it.m11 * y + it.m21 * z + it.m31 * w;
            float c = it.m02 * x + it.m12 * y + it.m22 * z + it.m32 * w;
            float d = it.m03 * x + it.m13 * y + it.m23 * z + it.m33 * w;

            return new Plane(new Vector3(a, b, c), d);
        }

        // ------------------------------------------------------------------------------------------------------------
        // Managed factories (spec section 8, "Factories (managed)")
        // ------------------------------------------------------------------------------------------------------------

        public static Matrix4x4 Scale(Vector3 vector)
        {
            Matrix4x4 m;
            m.m00 = vector.x; m.m01 = 0f;       m.m02 = 0f;       m.m03 = 0f;
            m.m10 = 0f;       m.m11 = vector.y; m.m12 = 0f;       m.m13 = 0f;
            m.m20 = 0f;       m.m21 = 0f;       m.m22 = vector.z; m.m23 = 0f;
            m.m30 = 0f;       m.m31 = 0f;       m.m32 = 0f;       m.m33 = 1f;
            return m;
        }

        public static Matrix4x4 Scale(in Vector3 vector) => Scale(vector);

        public static Matrix4x4 Translate(Vector3 vector)
        {
            Matrix4x4 m;
            m.m00 = 1f; m.m01 = 0f; m.m02 = 0f; m.m03 = vector.x;
            m.m10 = 0f; m.m11 = 1f; m.m12 = 0f; m.m13 = vector.y;
            m.m20 = 0f; m.m21 = 0f; m.m22 = 1f; m.m23 = vector.z;
            m.m30 = 0f; m.m31 = 0f; m.m32 = 0f; m.m33 = 1f;
            return m;
        }

        public static Matrix4x4 Translate(in Vector3 vector) => Translate(vector);

        /// <summary>Assumes a unit quaternion; no normalisation (spec section 8, "Rotate").</summary>
        public static Matrix4x4 Rotate(Quaternion q)
        {
            float x2 = q.x * 2f;
            float y2 = q.y * 2f;
            float z2 = q.z * 2f;
            float xx = q.x * x2;
            float yy = q.y * y2;
            float zz = q.z * z2;
            float xy = q.x * y2;
            float xz = q.x * z2;
            float yz = q.y * z2;
            float wx = q.w * x2;
            float wy = q.w * y2;
            float wz = q.w * z2;

            Matrix4x4 m;
            m.m00 = 1f - (yy + zz); m.m10 = xy + wz;        m.m20 = xz - wy;        m.m30 = 0f;
            m.m01 = xy - wz;        m.m11 = 1f - (xx + zz); m.m21 = yz + wx;        m.m31 = 0f;
            m.m02 = xz + wy;        m.m12 = yz - wx;        m.m22 = 1f - (xx + yy); m.m32 = 0f;
            m.m03 = 0f;             m.m13 = 0f;             m.m23 = 0f;             m.m33 = 1f;
            return m;
        }

        public static Matrix4x4 Rotate(in Quaternion q) => Rotate(q);

        public static float Determinant(Matrix4x4 m) => m.determinant;
        public static float Determinant(in Matrix4x4 m) => m.determinant;

        public static Matrix4x4 Inverse(Matrix4x4 m) => m.inverse;
        public static Matrix4x4 Inverse(in Matrix4x4 m) => m.inverse;

        public static Matrix4x4 Transpose(Matrix4x4 m) => m.transpose;
        public static Matrix4x4 Transpose(in Matrix4x4 m) => m.transpose;

        public static Matrix4x4 Frustum(FrustumPlanes fp) => Frustum(fp.left, fp.right, fp.bottom, fp.top, fp.zNear, fp.zFar);
        public static Matrix4x4 Frustum(in FrustumPlanes fp) => Frustum(fp.left, fp.right, fp.bottom, fp.top, fp.zNear, fp.zFar);

        // ------------------------------------------------------------------------------------------------------------
        // Native members reproduced from the spec's verified behaviour (spec section 8, "Native members")
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Bit-identical (per spec) to Translate(pos) * Rotate(q) * Scale(s); computed literally that way so the float
        /// summation order of the product is preserved. A non-unit quaternion is not normalised.
        /// </summary>
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s)
        {
            return Translate(pos) * Rotate(q) * Scale(s);
        }

        public static Matrix4x4 TRS(in Vector3 pos, in Quaternion q, in Vector3 s) => TRS(pos, q, s);

        public void SetTRS(Vector3 pos, Quaternion q, Vector3 s)
        {
            this = TRS(pos, q, s);
        }

        /// <summary>Exact transpose.</summary>
        public readonly Matrix4x4 transpose
        {
            get
            {
                Matrix4x4 t;
                t.m00 = m00; t.m01 = m10; t.m02 = m20; t.m03 = m30;
                t.m10 = m01; t.m11 = m11; t.m12 = m21; t.m13 = m31;
                t.m20 = m02; t.m21 = m12; t.m22 = m22; t.m23 = m32;
                t.m30 = m03; t.m31 = m13; t.m32 = m23; t.m33 = m33;
                return t;
            }
        }

        /// <summary>
        /// Cofactor expansion evaluated in double precision and cast to float; the spec reports this bit-identical to
        /// Unity for Scale(2,3,4) (24), TRS(...,(2,3,4)) (24) and Perspective(60,1.5,0.3,1000) (-1.20036006).
        /// </summary>
        public readonly float determinant
        {
            get { return (float)DeterminantAsDouble(); }
        }

        /// <summary>
        /// The determinant before the float cast. <c>inverse</c> tests singularity against this instead of against
        /// <see cref="determinant"/>: the cast is what turns a small-but-nonzero determinant into 0f, and a matrix such
        /// as Scale(1e-16, 1e-16, 1e-16) (determinant 1e-48, inverse Scale(1e16, …)) is perfectly invertible.
        /// </summary>
        private readonly double DeterminantAsDouble()
        {
            double d00 = m00, d01 = m01, d02 = m02, d03 = m03;
            double d10 = m10, d11 = m11, d12 = m12, d13 = m13;
            double d20 = m20, d21 = m21, d22 = m22, d23 = m23;
            double d30 = m30, d31 = m31, d32 = m32, d33 = m33;

            double v0 = d20 * d31 - d21 * d30;
            double v1 = d20 * d32 - d22 * d30;
            double v2 = d20 * d33 - d23 * d30;
            double v3 = d21 * d32 - d22 * d31;
            double v4 = d21 * d33 - d23 * d31;
            double v5 = d22 * d33 - d23 * d32;

            double t00 = +(v5 * d11 - v4 * d12 + v3 * d13);
            double t10 = -(v5 * d10 - v2 * d12 + v1 * d13);
            double t20 = +(v4 * d10 - v2 * d11 + v0 * d13);
            double t30 = -(v3 * d10 - v1 * d11 + v0 * d12);

            return t00 * d00 + t10 * d01 + t20 * d02 + t30 * d03;
        }

        /// <summary>
        /// General 4x4 inverse by Gauss-Jordan elimination with partial pivoting in single precision (spec section 8:
        /// Unity's native inverse is not reproducible by cofactor formulas and behaves like pivoted elimination).
        /// A singular matrix returns Matrix4x4.zero.
        /// </summary>
        public readonly Matrix4x4 inverse
        {
            get
            {
                // unverified vs Unity: the exact elimination order / pivot strategy; expect a few ulp of divergence.
                // Spec §8: "A singular matrix (determinant 0) returns Matrix4x4.zero". The test is on the determinant
                // BEFORE the float cast — gating on the float `determinant` reports every matrix whose determinant
                // merely underflows as singular, e.g. Scale(1e-16, 1e-16, 1e-16) (determinant 1e-48 → 0f) which inverts
                // cleanly to Scale(1e16, …) and whose elimination pivot, 1e-16, is nowhere near zero. Rank-deficient
                // matrices whose float elimination would still find a nonzero pivot (rows 1..16, determinant exactly 0)
                // are caught here, matching the documented "singular → zero"; the pivot test below is the backstop.
                if (DeterminantAsDouble() == 0.0)
                    return zeroMatrix;

                // Row-major working copy: a[r * 4 + c]; b starts as the identity and ends as the inverse.
                Span<float> a = stackalloc float[16];
                Span<float> b = stackalloc float[16];
                for (int r = 0; r < 4; r++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        a[r * 4 + c] = this[r, c];
                        b[r * 4 + c] = r == c ? 1f : 0f;
                    }
                }

                for (int col = 0; col < 4; col++)
                {
                    // Partial pivoting: pick the row with the largest magnitude in this column.
                    int pivotRow = col;
                    float pivotAbs = Math.Abs(a[col * 4 + col]);
                    for (int r = col + 1; r < 4; r++)
                    {
                        float candidate = Math.Abs(a[r * 4 + col]);
                        if (candidate > pivotAbs)
                        {
                            pivotAbs = candidate;
                            pivotRow = r;
                        }
                    }

                    if (pivotAbs == 0f || float.IsNaN(pivotAbs))
                        return zeroMatrix;

                    if (pivotRow != col)
                    {
                        for (int c = 0; c < 4; c++)
                        {
                            float tmpA = a[col * 4 + c]; a[col * 4 + c] = a[pivotRow * 4 + c]; a[pivotRow * 4 + c] = tmpA;
                            float tmpB = b[col * 4 + c]; b[col * 4 + c] = b[pivotRow * 4 + c]; b[pivotRow * 4 + c] = tmpB;
                        }
                    }

                    // Normalise the pivot row.
                    float pivot = a[col * 4 + col];
                    for (int c = 0; c < 4; c++)
                    {
                        a[col * 4 + c] /= pivot;
                        b[col * 4 + c] /= pivot;
                    }

                    // Eliminate this column from every other row.
                    for (int r = 0; r < 4; r++)
                    {
                        if (r == col)
                            continue;
                        float factor = a[r * 4 + col];
                        if (factor == 0f)
                            continue;
                        for (int c = 0; c < 4; c++)
                        {
                            a[r * 4 + c] -= factor * a[col * 4 + c];
                            b[r * 4 + c] -= factor * b[col * 4 + c];
                        }
                    }
                }

                Matrix4x4 result;
                result.m00 = b[0];  result.m01 = b[1];  result.m02 = b[2];  result.m03 = b[3];
                result.m10 = b[4];  result.m11 = b[5];  result.m12 = b[6];  result.m13 = b[7];
                result.m20 = b[8];  result.m21 = b[9];  result.m22 = b[10]; result.m23 = b[11];
                result.m30 = b[12]; result.m31 = b[13]; result.m32 = b[14]; result.m33 = b[15];
                return result;
            }
        }

        /// <summary>
        /// Inverse of an affine (3x4) matrix: adjugate of the upper 3x3 in float, translation = -inv3 * t, last row
        /// (0,0,0,1). Returns false and writes Matrix4x4.zero when the 3x3 block is singular.
        /// </summary>
        public static bool Inverse3DAffine(Matrix4x4 input, ref Matrix4x4 result)
        {
            // unverified vs Unity: exact cofactor evaluation order and the singularity test (exact zero determinant assumed).
            float c00 = input.m11 * input.m22 - input.m12 * input.m21;
            float c01 = input.m12 * input.m20 - input.m10 * input.m22;
            float c02 = input.m10 * input.m21 - input.m11 * input.m20;

            float det = input.m00 * c00 + input.m01 * c01 + input.m02 * c02;
            if (det == 0f || float.IsNaN(det))
            {
                result = zeroMatrix;
                return false;
            }

            float invDet = 1f / det;

            Matrix4x4 r;
            r.m00 = c00 * invDet;
            r.m10 = c01 * invDet;
            r.m20 = c02 * invDet;
            r.m01 = (input.m02 * input.m21 - input.m01 * input.m22) * invDet;
            r.m11 = (input.m00 * input.m22 - input.m02 * input.m20) * invDet;
            r.m21 = (input.m01 * input.m20 - input.m00 * input.m21) * invDet;
            r.m02 = (input.m01 * input.m12 - input.m02 * input.m11) * invDet;
            r.m12 = (input.m02 * input.m10 - input.m00 * input.m12) * invDet;
            r.m22 = (input.m00 * input.m11 - input.m01 * input.m10) * invDet;

            r.m03 = -(r.m00 * input.m03 + r.m01 * input.m13 + r.m02 * input.m23);
            r.m13 = -(r.m10 * input.m03 + r.m11 * input.m13 + r.m12 * input.m23);
            r.m23 = -(r.m20 * input.m03 + r.m21 * input.m13 + r.m22 * input.m23);

            r.m30 = 0f;
            r.m31 = 0f;
            r.m32 = 0f;
            r.m33 = 1f;

            result = r;
            return true;
        }

        public static bool Inverse3DAffine(in Matrix4x4 input, ref Matrix4x4 result) => Inverse3DAffine(input, ref result);

        /// <summary>Approximate: every element within 1e-5 of the identity element (bottom row included); NaN and zero are false.</summary>
        public readonly bool isIdentity
        {
            get
            {
                // unverified vs Unity: whether a deviation of exactly 1e-5 passes (strict < assumed, per spec).
                const float tolerance = 1e-5f;
                return Math.Abs(m00 - 1f) < tolerance && Math.Abs(m01) < tolerance && Math.Abs(m02) < tolerance && Math.Abs(m03) < tolerance
                    && Math.Abs(m10) < tolerance && Math.Abs(m11 - 1f) < tolerance && Math.Abs(m12) < tolerance && Math.Abs(m13) < tolerance
                    && Math.Abs(m20) < tolerance && Math.Abs(m21) < tolerance && Math.Abs(m22 - 1f) < tolerance && Math.Abs(m23) < tolerance
                    && Math.Abs(m30) < tolerance && Math.Abs(m31) < tolerance && Math.Abs(m32) < tolerance && Math.Abs(m33 - 1f) < tolerance;
            }
        }

        /// <summary>Only checks that the last row is exactly (0,0,0,1); shear and zero scale still pass.</summary>
        public readonly bool ValidTRS()
        {
            return m30 == 0f && m31 == 0f && m32 == 0f && m33 == 1f;
        }

        /// <summary>Determinant of the upper 3x3 block in float; used for the lossyScale / rotation mirror sign.</summary>
        private readonly float Determinant3x3()
        {
            return m00 * (m11 * m22 - m12 * m21)
                 - m01 * (m10 * m22 - m12 * m20)
                 + m02 * (m10 * m21 - m11 * m20);
        }

        /// <summary>
        /// (sign * |column0|, |column1|, |column2|) with 3-component magnitudes; sign = -1 when the upper 3x3 determinant is
        /// negative and it is always applied to x only (spec section 8, "lossyScale").
        /// </summary>
        public readonly Vector3 lossyScale
        {
            get
            {
                float sx = new Vector3(m00, m10, m20).magnitude;
                float sy = new Vector3(m01, m11, m21).magnitude;
                float sz = new Vector3(m02, m12, m22).magnitude;
                if (Determinant3x3() < 0f)
                    sx = -sx;
                return new Vector3(sx, sy, sz);
            }
        }

        /// <summary>
        /// Rotation of the column-normalised upper 3x3 (proper matrices, det &gt; 0: verified within 3e-8 of the source
        /// quaternion). Improper matrices are unspecified in the spec (data-dependent in Unity); the shim mirrors the
        /// first column for affine (ValidTRS) matrices, which reproduces the spec's four TRS-with-negative-scale cases
        /// (the "sign on x" convention shared with lossyScale), and otherwise extracts from the raw normalised columns,
        /// which reproduces Perspective(...).rotation == identity.
        /// </summary>
        public readonly Quaternion rotation
        {
            get
            {
                // unverified vs Unity: improper (det <= 0) matrices are data-dependent in Unity (Scale(-1,1,1).rotation is a
                // 180-degree y-rotation there, identity here). The zero matrix is equally unspecified: Unity returns one
                // garbage quaternion ((-0.159,-0.384,-0.384,0.824) in the probe), the code below falls through to the
                // largest-diagonal branch with s = 2 and returns (0, 0, 1, 0) — a 180-degree z rotation.
                float c0x = m00, c0y = m10, c0z = m20;
                float c1x = m01, c1y = m11, c1z = m21;
                float c2x = m02, c2y = m12, c2z = m22;

                float len0 = new Vector3(c0x, c0y, c0z).magnitude;
                float len1 = new Vector3(c1x, c1y, c1z).magnitude;
                float len2 = new Vector3(c2x, c2y, c2z).magnitude;

                if (len0 > 0f) { c0x /= len0; c0y /= len0; c0z /= len0; }
                if (len1 > 0f) { c1x /= len1; c1y /= len1; c1z /= len1; }
                if (len2 > 0f) { c2x /= len2; c2y /= len2; c2z /= len2; }

                if (Determinant3x3() < 0f && ValidTRS())
                {
                    c0x = -c0x; c0y = -c0y; c0z = -c0z;
                }

                // Rotation matrix elements r[row][col]: column i is (cix, ciy, ciz).
                float r00 = c0x, r01 = c1x, r02 = c2x;
                float r10 = c0y, r11 = c1y, r12 = c2y;
                float r20 = c0z, r21 = c1z, r22 = c2z;

                float qx, qy, qz, qw;
                float trace = r00 + r11 + r22;
                if (trace > 0f)
                {
                    float s = (float)Math.Sqrt(trace + 1f) * 2f;
                    qw = 0.25f * s;
                    qx = (r21 - r12) / s;
                    qy = (r02 - r20) / s;
                    qz = (r10 - r01) / s;
                }
                else if (r00 > r11 && r00 > r22)
                {
                    float s = (float)Math.Sqrt(1f + r00 - r11 - r22) * 2f;
                    qw = (r21 - r12) / s;
                    qx = 0.25f * s;
                    qy = (r01 + r10) / s;
                    qz = (r02 + r20) / s;
                }
                else if (r11 > r22)
                {
                    float s = (float)Math.Sqrt(1f + r11 - r00 - r22) * 2f;
                    qw = (r02 - r20) / s;
                    qx = (r01 + r10) / s;
                    qy = 0.25f * s;
                    qz = (r12 + r21) / s;
                }
                else
                {
                    float s = (float)Math.Sqrt(1f + r22 - r00 - r11) * 2f;
                    qw = (r10 - r01) / s;
                    qx = (r02 + r20) / s;
                    qy = (r12 + r21) / s;
                    qz = 0.25f * s;
                }

                float mag = (float)Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
                if (!(mag > 0f))
                    return Quaternion.identity;
                return new Quaternion(qx / mag, qy / mag, qz / mag, qw / mag);
            }
        }

        /// <summary>
        /// OpenGL-convention orthographic projection (clip z in [-1, 1], z flipped). dx/dy/dz computed once, as the spec
        /// verified bit-exact for Ortho(0,800,600,0,-1,1) and Ortho(0,800,0,600,-100,100).
        /// </summary>
        public static Matrix4x4 Ortho(float left, float right, float bottom, float top, float zNear, float zFar)
        {
            float dx = right - left;
            float dy = top - bottom;
            float dz = zFar - zNear;

            Matrix4x4 m = identityMatrix;
            m.m00 = 2f / dx;
            m.m03 = -(right + left) / dx;
            m.m11 = 2f / dy;
            m.m13 = -(top + bottom) / dy;
            m.m22 = -2f / dz;
            m.m23 = -(zFar + zNear) / dz;
            return m;
        }

        /// <summary>OpenGL-convention perspective projection; cot = cos(rad)/sin(rad) with rad = (fov/2)*Deg2Rad.</summary>
        public static Matrix4x4 Perspective(float fov, float aspect, float zNear, float zFar)
        {
            // Native sinf/cosf differ from Math.Sin/Cos by 1-2 ulp for some inputs (spec section 8, "Perspective").
            float rad = (fov / 2f) * Mathf.Deg2Rad;
            float cot = (float)Math.Cos(rad) / (float)Math.Sin(rad);
            float dz = zNear - zFar;

            Matrix4x4 m = zeroMatrix;
            m.m00 = cot / aspect;
            m.m11 = cot;
            m.m22 = (zFar + zNear) / dz;
            m.m23 = 2f * zNear * zFar / dz;
            m.m32 = -1f;
            return m;
        }

        /// <summary>OpenGL-convention off-axis frustum projection.</summary>
        public static Matrix4x4 Frustum(float left, float right, float bottom, float top, float zNear, float zFar)
        {
            Matrix4x4 m = zeroMatrix;
            m.m00 = 2f * zNear / (right - left);
            m.m02 = (right + left) / (right - left);
            m.m11 = 2f * zNear / (top - bottom);
            m.m12 = (top + bottom) / (top - bottom);
            m.m22 = -(zFar + zNear) / (zFar - zNear);
            m.m23 = -(2f * zFar * zNear) / (zFar - zNear);
            m.m32 = -1f;
            return m;
        }

        /// <summary>
        /// Recovers the six frustum planes from a projection matrix. Perspective matrices (m32 == -1) invert the Frustum
        /// formulas; orthographic matrices (m33 == 1, m32 == 0) invert the Ortho formulas.
        /// </summary>
        public readonly FrustumPlanes decomposeProjection
        {
            get
            {
                // unverified vs Unity: evaluation order (spec gives only Perspective(60,1.5,0.3,1000) -> l=-0.25980765,
                // r=0.25980765, b=-0.1732051, t=0.1732051, n=0.3, f=1000.1341) and the orthographic branch.
                FrustumPlanes fp;
                if (m32 == 0f && m33 == 1f)
                {
                    // Ortho: m00 = 2/(r-l), m03 = -(r+l)/(r-l), m11 = 2/(t-b), m13 = -(t+b)/(t-b), m22 = -2/(f-n), m23 = -(f+n)/(f-n).
                    fp.left = -(m03 + 1f) / m00;
                    fp.right = (1f - m03) / m00;
                    fp.bottom = -(m13 + 1f) / m11;
                    fp.top = (1f - m13) / m11;
                    fp.zNear = (m23 + 1f) / m22;
                    fp.zFar = (m23 - 1f) / m22;
                }
                else
                {
                    // Frustum: m00 = 2n/(r-l), m02 = (r+l)/(r-l), m11 = 2n/(t-b), m12 = (t+b)/(t-b), m22 = -(f+n)/(f-n), m23 = -2fn/(f-n).
                    float zNear = m23 / (m22 - 1f);
                    float zFar = m23 / (m22 + 1f);
                    fp.zNear = zNear;
                    fp.zFar = zFar;
                    fp.left = zNear * (m02 - 1f) / m00;
                    fp.right = zNear * (m02 + 1f) / m00;
                    fp.bottom = zNear * (m12 - 1f) / m11;
                    fp.top = zNear * (m12 + 1f) / m11;
                }
                return fp;
            }
        }

        /// <summary>
        /// View-basis matrix at <paramref name="from"/> looking toward <paramref name="to"/>: columns (right, up', forward, from).
        /// Equivalent to TRS(from, LookRotation(to - from, up), one) within ~6e-8. Degenerate cases (from == to, or forward
        /// parallel to up) return the identity rotation with translation <paramref name="from"/>, as the spec verified for
        /// LookAt(zero, up, up).
        /// </summary>
        public static Matrix4x4 LookAt(Vector3 from, Vector3 to, Vector3 up)
        {
            // unverified vs Unity: the degenerate-case threshold (a normalised forward / right of zero magnitude is used here).
            Vector3 forward = new Vector3(to.x - from.x, to.y - from.y, to.z - from.z).normalized;
            if (forward.x == 0f && forward.y == 0f && forward.z == 0f)
                return Translate(from);

            Vector3 right = Vector3.Cross(up, forward).normalized;
            if (right.x == 0f && right.y == 0f && right.z == 0f)
                return Translate(from);

            Vector3 newUp = Vector3.Cross(forward, right);

            Matrix4x4 m;
            m.m00 = right.x; m.m01 = newUp.x; m.m02 = forward.x; m.m03 = from.x;
            m.m10 = right.y; m.m11 = newUp.y; m.m12 = forward.y; m.m13 = from.y;
            m.m20 = right.z; m.m21 = newUp.z; m.m22 = forward.z; m.m23 = from.z;
            m.m30 = 0f;      m.m31 = 0f;      m.m32 = 0f;        m.m33 = 1f;
            return m;
        }

        public static Matrix4x4 LookAt(in Vector3 from, in Vector3 to, in Vector3 up) => LookAt(from, to, up);

        /// <summary>Per-element approximate comparison (|a - b| &lt;= threshold for every element).</summary>
        internal static bool CompareApproximately(Matrix4x4 a, Matrix4x4 b, float threshold)
        {
            // unverified vs Unity: native helper; inclusive per-element tolerance assumed.
            for (int i = 0; i < 16; i++)
            {
                if (!(Math.Abs(a[i] - b[i]) <= threshold))
                    return false;
            }
            return true;
        }

        // ------------------------------------------------------------------------------------------------------------
        // ToString (spec section 8, "ToString"): row-major, tab separated, trailing newline, default "F5".
        // ------------------------------------------------------------------------------------------------------------

        public override readonly string ToString() => ToString(null, null);

        public readonly string ToString(string format) => ToString(format, null);

        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F5";
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;

            return string.Format(
                "{0}\t{1}\t{2}\t{3}\n{4}\t{5}\t{6}\t{7}\n{8}\t{9}\t{10}\t{11}\n{12}\t{13}\t{14}\t{15}\n",
                m00.ToString(format, formatProvider), m01.ToString(format, formatProvider), m02.ToString(format, formatProvider), m03.ToString(format, formatProvider),
                m10.ToString(format, formatProvider), m11.ToString(format, formatProvider), m12.ToString(format, formatProvider), m13.ToString(format, formatProvider),
                m20.ToString(format, formatProvider), m21.ToString(format, formatProvider), m22.ToString(format, formatProvider), m23.ToString(format, formatProvider),
                m30.ToString(format, formatProvider), m31.ToString(format, formatProvider), m32.ToString(format, formatProvider), m33.ToString(format, formatProvider));
        }
    }
}
