// Tests for the UnityEngine.Matrix4x4 / FrustumPlanes shim against Docs/Standalone/UnityValueTypeSemantics.md section 8
// (plus the cross-cutting rules of section 0 and the Vector4 rules of section 3 that the matrix delegates to).
// Every [verified] value in the spec is asserted bit-exactly unless the spec itself says Unity's result is only
// reproducible within an ulp or so (inverse, native sin/cos, decomposeProjection, LookAt, rotation). Formula-only
// members are checked with values worked out by hand from the spec's expression (arithmetic in the comments).
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class MatrixTests
    {
        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
        private const int NegativeZeroBits = unchecked((int)0x80000000);

        /// <summary>Builds a matrix from sixteen ROW-major values (m00, m01, m02, m03, m10, ...).</summary>
        private static Matrix4x4 Rows(
            float m00, float m01, float m02, float m03,
            float m10, float m11, float m12, float m13,
            float m20, float m21, float m22, float m23,
            float m30, float m31, float m32, float m33)
        {
            Matrix4x4 m;
            m.m00 = m00; m.m01 = m01; m.m02 = m02; m.m03 = m03;
            m.m10 = m10; m.m11 = m11; m.m12 = m12; m.m13 = m13;
            m.m20 = m20; m.m21 = m21; m.m22 = m22; m.m23 = m23;
            m.m30 = m30; m.m31 = m31; m.m32 = m32; m.m33 = m33;
            return m;
        }

        private static void AssertBitExact(Matrix4x4 expected, Matrix4x4 actual, string context = "")
        {
            for (int i = 0; i < 16; i++)
            {
                Assert.That(Bits(actual[i]), Is.EqualTo(Bits(expected[i])),
                    $"{context} element [{i}] (row {i % 4}, col {i / 4}): expected {expected[i]:R} got {actual[i]:R}");
            }
        }

        private static void AssertWithin(Matrix4x4 expected, Matrix4x4 actual, float tolerance, string context = "")
        {
            for (int i = 0; i < 16; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance),
                    $"{context} element [{i}] (row {i % 4}, col {i / 4})");
            }
        }

        /// <summary>q and -q are the same rotation; align the sign before comparing components.</summary>
        private static void AssertSameRotation(Quaternion expected, Quaternion actual, float tolerance, string context = "")
        {
            if (Quaternion.Dot(expected, actual) < 0f)
                actual = new Quaternion(-actual.x, -actual.y, -actual.z, -actual.w);
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance), context + " x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance), context + " y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance), context + " z");
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(tolerance), context + " w");
        }

        // Row-major 1..16: rows (1,2,3,4) / (5,6,7,8) / (9,10,11,12) / (13,14,15,16).
        private static Matrix4x4 Sequential() => Rows(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);

        // ------------------------------------------------------------------ section 0 / 8: type shape and storage

        [Test]
        public void TypeShape_MatchesSpec()
        {
            Type t = typeof(Matrix4x4);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True, "[Serializable]");
            Assert.That(t.IsLayoutSequential, Is.True, "StructLayout(LayoutKind.Sequential)");
            Assert.That(typeof(IEquatable<Matrix4x4>).IsAssignableFrom(t), Is.True);
            Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);

            var fields = t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.That(fields.Length, Is.EqualTo(16), "exactly sixteen instance fields (arrays are reinterpreted as float*)");
            foreach (var f in fields)
            {
                Assert.That(f.FieldType, Is.EqualTo(typeof(float)), f.Name);
                Assert.That(f.IsPublic, Is.True, f.Name);
            }
            Assert.That(Marshal.SizeOf<Matrix4x4>(), Is.EqualTo(64));
        }

        [Test]
        public unsafe void Storage_IsColumnMajor_InMemory()
        {
            // Spec "Storage": memory order m00, m10, m20, m30, m01, m11, m21, m31, m02, m12, m22, m32, m03, m13, m23, m33,
            // and the int indexer walks the same order, so raw float i must equal this[i].
            Assert.That(sizeof(Matrix4x4), Is.EqualTo(64));
            Matrix4x4 m = Sequential();
            float* p = (float*)&m;
            float[] expectedMemory = { 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15, 4, 8, 12, 16 };
            for (int i = 0; i < 16; i++)
            {
                Assert.That(p[i], Is.EqualTo(expectedMemory[i]), $"raw float {i}");
                Assert.That(m[i], Is.EqualTo(expectedMemory[i]), $"this[{i}]");
            }
        }

        [Test]
        public unsafe void FrustumPlanes_Shape()
        {
            Type t = typeof(FrustumPlanes);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True);
            Assert.That(sizeof(FrustumPlanes), Is.EqualTo(24));
            FrustumPlanes fp = new FrustumPlanes { left = 1, right = 2, bottom = 3, top = 4, zNear = 5, zFar = 6 };
            float* p = (float*)&fp;
            // Declared order: left, right, bottom, top, zNear, zFar.
            for (int i = 0; i < 6; i++)
                Assert.That(p[i], Is.EqualTo(i + 1f), $"field {i}");
        }

        // ------------------------------------------------------------------ Constructor

        [Test]
        public void Constructor_TakesColumns()
        {
            var m = new Matrix4x4(new Vector4(1, 2, 3, 4), new Vector4(5, 6, 7, 8), new Vector4(9, 10, 11, 12), new Vector4(13, 14, 15, 16));
            // Column 0 = (m00, m10, m20, m30) = (1,2,3,4) etc.
            Assert.That(m.m00, Is.EqualTo(1f)); Assert.That(m.m10, Is.EqualTo(2f)); Assert.That(m.m20, Is.EqualTo(3f)); Assert.That(m.m30, Is.EqualTo(4f));
            Assert.That(m.m01, Is.EqualTo(5f)); Assert.That(m.m11, Is.EqualTo(6f)); Assert.That(m.m21, Is.EqualTo(7f)); Assert.That(m.m31, Is.EqualTo(8f));
            Assert.That(m.m02, Is.EqualTo(9f)); Assert.That(m.m12, Is.EqualTo(10f)); Assert.That(m.m22, Is.EqualTo(11f)); Assert.That(m.m32, Is.EqualTo(12f));
            Assert.That(m.m03, Is.EqualTo(13f)); Assert.That(m.m13, Is.EqualTo(14f)); Assert.That(m.m23, Is.EqualTo(15f)); Assert.That(m.m33, Is.EqualTo(16f));
        }

        // ------------------------------------------------------------------ Indexers

        [Test]
        public void Indexer_Int_MapsToFieldsInMemoryOrder()
        {
            Matrix4x4 m = Sequential();
            Assert.That(m[0], Is.EqualTo(m.m00)); Assert.That(m[1], Is.EqualTo(m.m10)); Assert.That(m[2], Is.EqualTo(m.m20)); Assert.That(m[3], Is.EqualTo(m.m30));
            Assert.That(m[4], Is.EqualTo(m.m01)); Assert.That(m[5], Is.EqualTo(m.m11)); Assert.That(m[6], Is.EqualTo(m.m21)); Assert.That(m[7], Is.EqualTo(m.m31));
            Assert.That(m[8], Is.EqualTo(m.m02)); Assert.That(m[9], Is.EqualTo(m.m12)); Assert.That(m[10], Is.EqualTo(m.m22)); Assert.That(m[11], Is.EqualTo(m.m32));
            Assert.That(m[12], Is.EqualTo(m.m03)); Assert.That(m[13], Is.EqualTo(m.m13)); Assert.That(m[14], Is.EqualTo(m.m23)); Assert.That(m[15], Is.EqualTo(m.m33));
        }

        [Test]
        public void Indexer_RowColumn_IsRowPlusColumnTimes4()
        {
            Matrix4x4 m = Sequential();
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    Assert.That(m[row, col], Is.EqualTo(m[row + col * 4]), $"[{row},{col}]");
            // [verified] m[0,3] == m[12] == m03.
            Assert.That(m[0, 3], Is.EqualTo(m[12]));
            Assert.That(m[0, 3], Is.EqualTo(m.m03));
            Assert.That(m[0, 3], Is.EqualTo(4f));
        }

        [Test]
        public void Indexer_Set_WritesFields()
        {
            Matrix4x4 m = Matrix4x4.zero;
            m[12] = 5f;
            Assert.That(m.m03, Is.EqualTo(5f));
            m[1, 2] = 7f; // row 1, col 2 -> index 9 -> m12
            Assert.That(m.m12, Is.EqualTo(7f));
            Assert.That(m[9], Is.EqualTo(7f));
            m[3, 0] = 9f; // index 3 -> m30
            Assert.That(m.m30, Is.EqualTo(9f));
        }

        [TestCase(16)]
        [TestCase(-1)]
        [TestCase(100)]
        public void Indexer_Int_OutOfRange_ThrowsWithMessage(int index)
        {
            Matrix4x4 m = Matrix4x4.identity;
            var getEx = Assert.Throws<IndexOutOfRangeException>(() => { float _ = m[index]; });
            Assert.That(getEx.Message, Is.EqualTo("Invalid matrix index!"));
            var setEx = Assert.Throws<IndexOutOfRangeException>(() => { m[index] = 1f; });
            Assert.That(setEx.Message, Is.EqualTo("Invalid matrix index!"));
        }

        [Test]
        public void Indexer_RowColumn_OutOfRange_ThrowsWithMessage()
        {
            Matrix4x4 m = Matrix4x4.identity;
            // row 0, col 4 -> index 16
            var ex = Assert.Throws<IndexOutOfRangeException>(() => { float _ = m[0, 4]; });
            Assert.That(ex.Message, Is.EqualTo("Invalid matrix index!"));
            var setEx = Assert.Throws<IndexOutOfRangeException>(() => { m[4, 3] = 1f; }); // index 16
            Assert.That(setEx.Message, Is.EqualTo("Invalid matrix index!"));
        }

        // ------------------------------------------------------------------ Accessors

        [Test]
        public void GetColumn_ReturnsColumn()
        {
            Matrix4x4 m = Sequential();
            Assert.That(m.GetColumn(0), Is.EqualTo(new Vector4(1, 5, 9, 13)));
            Assert.That(m.GetColumn(1), Is.EqualTo(new Vector4(2, 6, 10, 14)));
            Assert.That(m.GetColumn(2), Is.EqualTo(new Vector4(3, 7, 11, 15)));
            Assert.That(m.GetColumn(3), Is.EqualTo(new Vector4(4, 8, 12, 16)));
        }

        [Test]
        public void GetRow_ReturnsRow()
        {
            Matrix4x4 m = Sequential();
            Assert.That(m.GetRow(0), Is.EqualTo(new Vector4(1, 2, 3, 4)));
            Assert.That(m.GetRow(1), Is.EqualTo(new Vector4(5, 6, 7, 8)));
            Assert.That(m.GetRow(2), Is.EqualTo(new Vector4(9, 10, 11, 12)));
            Assert.That(m.GetRow(3), Is.EqualTo(new Vector4(13, 14, 15, 16)));
        }

        [TestCase(4)]
        [TestCase(-1)]
        public void GetColumn_GetRow_OutOfRange_ThrowWithMessages(int index)
        {
            Matrix4x4 m = Matrix4x4.identity;
            var colEx = Assert.Throws<IndexOutOfRangeException>(() => m.GetColumn(index));
            Assert.That(colEx.Message, Is.EqualTo("Invalid column index!"));
            var rowEx = Assert.Throws<IndexOutOfRangeException>(() => m.GetRow(index));
            Assert.That(rowEx.Message, Is.EqualTo("Invalid row index!"));
        }

        [Test]
        public void SetColumn_SetRow_WriteThroughIndexer()
        {
            Matrix4x4 m = Matrix4x4.zero;
            m.SetColumn(2, new Vector4(1, 2, 3, 4));
            Assert.That(m.m02, Is.EqualTo(1f)); Assert.That(m.m12, Is.EqualTo(2f)); Assert.That(m.m22, Is.EqualTo(3f)); Assert.That(m.m32, Is.EqualTo(4f));
            Assert.That(m.GetColumn(2), Is.EqualTo(new Vector4(1, 2, 3, 4)));

            m = Matrix4x4.zero;
            m.SetRow(1, new Vector4(5, 6, 7, 8));
            Assert.That(m.m10, Is.EqualTo(5f)); Assert.That(m.m11, Is.EqualTo(6f)); Assert.That(m.m12, Is.EqualTo(7f)); Assert.That(m.m13, Is.EqualTo(8f));
            Assert.That(m.GetRow(1), Is.EqualTo(new Vector4(5, 6, 7, 8)));
        }

        [Test]
        public void SetColumn_SetRow_BadIndex_ThrowMatrixIndexMessage()
        {
            // Both go through the indexer, so the message is the matrix one, not the column/row one.
            Matrix4x4 m = Matrix4x4.zero;
            var colEx = Assert.Throws<IndexOutOfRangeException>(() => m.SetColumn(4, Vector4.one));
            Assert.That(colEx.Message, Is.EqualTo("Invalid matrix index!"));
            var rowEx = Assert.Throws<IndexOutOfRangeException>(() => m.SetRow(4, Vector4.one));
            Assert.That(rowEx.Message, Is.EqualTo("Invalid matrix index!"));
        }

        [Test]
        public void GetPosition_ReturnsTranslationColumn()
        {
            Matrix4x4 m = Sequential();
            Assert.That(m.GetPosition(), Is.EqualTo(new Vector3(4, 8, 12)));
            Assert.That(Matrix4x4.Translate(new Vector3(7, 8, 9)).GetPosition(), Is.EqualTo(new Vector3(7, 8, 9)));
        }

        // ------------------------------------------------------------------ Statics

        [Test]
        public void Zero_AllZero()
        {
            Matrix4x4 z = Matrix4x4.zero;
            for (int i = 0; i < 16; i++)
                Assert.That(Bits(z[i]), Is.EqualTo(0), $"[{i}]");
        }

        [Test]
        public void Identity_IsIdentity()
        {
            Matrix4x4 id = Matrix4x4.identity;
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    Assert.That(id[row, col], Is.EqualTo(row == col ? 1f : 0f), $"[{row},{col}]");
            Assert.That(id.isIdentity, Is.True);
        }

        [Test]
        public void StaticPresets_AreNotMutableThroughTheGetter()
        {
            Matrix4x4 id = Matrix4x4.identity;
            id.m00 = 5f;
            Assert.That(Matrix4x4.identity.m00, Is.EqualTo(1f));
        }

        // ------------------------------------------------------------------ Equality

        [Test]
        public void OperatorEquals_UsesVector4ColumnTolerance()
        {
            // Vector4 ==: squared 4-component column difference < kEpsilon^2 = 1e-10.
            Matrix4x4 a = Matrix4x4.identity;
            Matrix4x4 b = Matrix4x4.identity;
            b.m00 = 1f + 5e-6f; // diff ~5.0e-6, squared ~2.5e-11 < 1e-10 -> equal
            Assert.That(a == b, Is.True);
            Assert.That(a != b, Is.False);
            Assert.That(a.Equals(b), Is.False, "Equals is exact");

            Matrix4x4 c = Matrix4x4.identity;
            c.m00 = 1f + 2e-5f; // diff ~2e-5, squared 4e-10 > 1e-10 -> not equal
            Assert.That(a == c, Is.False);
            Assert.That(a != c, Is.True);
        }

        [Test]
        public void OperatorEquals_TolerancePerColumn_NotWholeMatrix()
        {
            // 8e-6 in one element of every column: each column's squared diff = 6.4e-11 < 1e-10, so every column
            // compares equal even though the whole-matrix squared diff (2.56e-10) would exceed the threshold.
            Matrix4x4 a = Matrix4x4.zero;
            Matrix4x4 b = Matrix4x4.zero;
            b.m00 = 8e-6f; b.m11 = 8e-6f; b.m22 = 8e-6f; b.m33 = 8e-6f;
            Assert.That(a == b, Is.True);

            // Two 8e-6 deviations in the SAME column: 2 * 6.4e-11 = 1.28e-10 > 1e-10 -> not equal.
            Matrix4x4 c = Matrix4x4.zero;
            c.m00 = 8e-6f; c.m10 = 8e-6f;
            Assert.That(a == c, Is.False);
        }

        [Test]
        public void Equals_IsExact_And_NaNIsNeverEqual()
        {
            Matrix4x4 a = Sequential();
            Matrix4x4 b = Sequential();
            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals(in b), Is.True);
            Assert.That(a.Equals("not a matrix"), Is.False);
            Assert.That(a.Equals(null), Is.False);

            // Sequential's m21 (row 2, column 1) is 10; one ulp there is 2^-20 = 9.5367432e-7, so the squared column
            // difference is 9.1e-13 < 1e-10: Equals false (exact), == true (tolerance).
            b.m21 = 10.000001f;
            Assert.That(a.Equals(b), Is.False);
            Assert.That(a == b, Is.True);

            Matrix4x4 n = Matrix4x4.identity;
            n.m00 = float.NaN;
            Assert.That(n.Equals(n), Is.False, "Matrix4x4 uses C# == on floats, so NaN != NaN");
            Assert.That(n == n, Is.False);
            Assert.That(n != n, Is.True);
        }

        [Test]
        public void GetHashCode_ColumnFormula()
        {
            // c0 ^ (c1 << 2) ^ (c2 >> 2) ^ (c3 >> 1) using Vector4.GetHashCode of the columns.
            Matrix4x4 m = Sequential();
            int expected = m.GetColumn(0).GetHashCode() ^ (m.GetColumn(1).GetHashCode() << 2) ^ (m.GetColumn(2).GetHashCode() >> 2) ^ (m.GetColumn(3).GetHashCode() >> 1);
            Assert.That(m.GetHashCode(), Is.EqualTo(expected));
            // Hand value (raw IEEE bits): columns (1,2,3,4)=265289728, (5,6,7,8), (9,10,11,12), (13,14,15,16) combine to -456589312.
            Assert.That(m.GetHashCode(), Is.EqualTo(-456589312));
        }

        [Test]
        public void GetHashCode_IdentityAndZero_HandValues()
        {
            // identity: c0 = hash(1,0,0,0) = 0x3F800000; c1 = hash(0,1,0,0) = 0x3F800000 << 2 = 0xFE000000;
            // c2 = hash(0,0,1,0) = 0x3F800000 >> 2 = 0x0FE00000; c3 = hash(0,0,0,1) = 0x3F800000 >> 1 = 0x1FC00000.
            // result = 0x3F800000 ^ (0xFE000000 << 2 = 0xF8000000) ^ (0x0FE00000 >> 2 = 0x03F80000) ^ (0x1FC00000 >> 1 = 0x0FE00000)
            //        = 0xCB980000 = -879230976.
            Assert.That(Matrix4x4.identity.GetHashCode(), Is.EqualTo(unchecked((int)0xCB980000)));
            Assert.That(Matrix4x4.identity.GetHashCode(), Is.EqualTo(-879230976));
            Assert.That(Matrix4x4.zero.GetHashCode(), Is.EqualTo(0));
        }

        [Test]
        public void GetHashCode_NegativeZeroAndNaN_AreNormalisedByTheRuntime()
        {
            // Spec section 0: the hash is built from float.GetHashCode(), which on .NET Core normalises -0 to +0 and
            // every NaN to one value (Unity's Mono returns the raw bits). The shim inherits whatever the runtime does,
            // so this records the .NET behaviour rather than a Unity-verified value.
            Matrix4x4 a = Matrix4x4.zero;
            Matrix4x4 b = Matrix4x4.zero;
            b.m00 = -0f;
            Assert.That(Bits(b.m00), Is.EqualTo(NegativeZeroBits), "the field really holds -0");
            Assert.That(b.GetHashCode(), Is.EqualTo(a.GetHashCode()), "-0 hashes as +0 on .NET Core");

            Matrix4x4 n1 = Matrix4x4.zero; n1.m00 = float.NaN;
            Matrix4x4 n2 = Matrix4x4.zero; n2.m00 = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00001)); // a different NaN payload
            Assert.That(float.IsNaN(n2.m00), Is.True);
            Assert.That(n2.GetHashCode(), Is.EqualTo(n1.GetHashCode()), "all NaNs hash alike on .NET Core");
        }

        [Test]
        public void GetHashCode_EqualMatricesHashEqually()
        {
            Matrix4x4 a = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(2, 3, 4));
            Matrix4x4 b = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(2, 3, 4));
            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        // ------------------------------------------------------------------ Operators

        [Test]
        public void MatrixProduct_HandComputed()
        {
            // A = rows (1,2,3,4)/(5,6,7,8)/(9,10,11,12)/(13,14,15,16); B = rows (1,0,2,0)/(0,1,0,2)/(3,0,1,0)/(0,3,0,1).
            // AB[0,0] = 1*1 + 2*0 + 3*3 + 4*0 = 10; AB[0,1] = 0+2+0+12 = 14; AB[0,2] = 2+0+3+0 = 5; AB[0,3] = 0+4+0+4 = 8
            // AB[1,*] = (5+21, 6+24, 10+7, 12+8) = (26, 30, 17, 20)
            // AB[2,*] = (9+33, 10+36, 18+11, 20+12) = (42, 46, 29, 32)
            // AB[3,*] = (13+45, 14+48, 26+15, 28+16) = (58, 62, 41, 44)
            Matrix4x4 a = Sequential();
            Matrix4x4 b = Rows(1, 0, 2, 0, 0, 1, 0, 2, 3, 0, 1, 0, 0, 3, 0, 1);
            Matrix4x4 expected = Rows(10, 14, 5, 8, 26, 30, 17, 20, 42, 46, 29, 32, 58, 62, 41, 44);
            AssertBitExact(expected, a * b);

            AssertBitExact(a, a * Matrix4x4.identity);
            AssertBitExact(a, Matrix4x4.identity * a);
            AssertBitExact(Matrix4x4.zero, a * Matrix4x4.zero);
        }

        [Test]
        public void MatrixProduct_SumsLeftToRightInFloat()
        {
            // Row 0 of lhs = (1, 2^24, -2^24, 0), column 0 of rhs = (1, 1, 1, 0):
            // left-to-right float: (1 + 2^24) rounds to 2^24 (ties-to-even), then - 2^24 = 0.
            // Any other association (1 + (2^24 - 2^24)) would give 1.
            Matrix4x4 lhs = Rows(1, 16777216f, -16777216f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Matrix4x4 rhs = Rows(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0);
            Assert.That((lhs * rhs).m00, Is.EqualTo(0f));

            // Same for the Vector4 product.
            Assert.That((lhs * new Vector4(1, 1, 1, 0)).x, Is.EqualTo(0f));
        }

        [Test]
        public void MatrixTimesVector4_ColumnVector()
        {
            // A * (1,2,3,4): row dots = (1+4+9+16, 5+12+21+32, 9+20+33+48, 13+28+45+64) = (30, 70, 110, 150).
            Vector4 r = Sequential() * new Vector4(1, 2, 3, 4);
            Assert.That(r.x, Is.EqualTo(30f));
            Assert.That(r.y, Is.EqualTo(70f));
            Assert.That(r.z, Is.EqualTo(110f));
            Assert.That(r.w, Is.EqualTo(150f));
        }

        // ------------------------------------------------------------------ Transform helpers

        [Test]
        public void MultiplyPoint_DividesByW()
        {
            // identity with m30 = 1: w = 1*x + 1 = 2 for point (1,2,3); res = (1,2,3) * (1/2) = (0.5, 1, 1.5).
            Matrix4x4 m = Matrix4x4.identity;
            m.m30 = 1f;
            Vector3 r = m.MultiplyPoint(new Vector3(1, 2, 3));
            Assert.That(r.x, Is.EqualTo(0.5f));
            Assert.That(r.y, Is.EqualTo(1f));
            Assert.That(r.z, Is.EqualTo(1.5f));
        }

        [Test]
        public void MultiplyPoint_UsesTranslationAndFullRows()
        {
            // Sequential * (1,2,3,1): rows -> (1+4+9+4, 5+12+21+8, 9+20+33+12) = (18, 46, 74); w = 13+28+45+16 = 102.
            // w = 1/102; res = (18/102, 46/102, 74/102) evaluated as res * (1/102) in float.
            Vector3 r = Sequential().MultiplyPoint(new Vector3(1, 2, 3));
            float invW = 1f / 102f;
            Assert.That(Bits(r.x), Is.EqualTo(Bits(18f * invW)));
            Assert.That(Bits(r.y), Is.EqualTo(Bits(46f * invW)));
            Assert.That(Bits(r.z), Is.EqualTo(Bits(74f * invW)));
        }

        [Test]
        public void MultiplyPoint_DivisionByZeroW_IsUnguarded()
        {
            // identity with m30 = 1 and point (-1, 0, 2): w = -1 + 1 = 0 -> 1/0 = +inf; res = (-1, 0, 2) * inf = (-inf, NaN, +inf).
            Matrix4x4 m = Matrix4x4.identity;
            m.m30 = 1f;
            Vector3 r = m.MultiplyPoint(new Vector3(-1, 0, 2));
            Assert.That(float.IsNegativeInfinity(r.x), Is.True);
            Assert.That(float.IsNaN(r.y), Is.True);
            Assert.That(float.IsPositiveInfinity(r.z), Is.True);
        }

        [Test]
        public void MultiplyPoint3x4_NoDivide()
        {
            // Sequential * (1,2,3,1) without the divide: (18, 46, 74).
            Vector3 r = Sequential().MultiplyPoint3x4(new Vector3(1, 2, 3));
            Assert.That(r, Is.EqualTo(new Vector3(18, 46, 74)));

            // The bottom row is ignored entirely.
            Matrix4x4 m = Matrix4x4.identity;
            m.m30 = 5f; m.m33 = 0f;
            Assert.That(m.MultiplyPoint3x4(new Vector3(1, 2, 3)), Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(Matrix4x4.Translate(new Vector3(10, 20, 30)).MultiplyPoint3x4(new Vector3(1, 2, 3)), Is.EqualTo(new Vector3(11, 22, 33)));
        }

        [Test]
        public void MultiplyVector_Upper3x3Only()
        {
            // Sequential upper 3x3 * (1,2,3): (1+4+9, 5+12+21, 9+20+33) = (14, 38, 62); translation column ignored.
            Vector3 r = Sequential().MultiplyVector(new Vector3(1, 2, 3));
            Assert.That(r, Is.EqualTo(new Vector3(14, 38, 62)));
            Assert.That(Matrix4x4.Translate(new Vector3(10, 20, 30)).MultiplyVector(new Vector3(1, 2, 3)), Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(Matrix4x4.Scale(new Vector3(2, 3, 4)).MultiplyVector(new Vector3(1, 1, 1)), Is.EqualTo(new Vector3(2, 3, 4)));
        }

        [Test]
        public void TransformPlane_UsesInverseTranspose()
        {
            // Translate(1,2,3): inverse = Translate(-1,-2,-3). Plane y = 0 (normal (0,1,0), d = 0):
            // a = it.m00*0 + it.m10*1 + it.m20*0 + it.m30*0 = 0; b = it.m11*1 = 1; c = 0; d = it.m03*0 + it.m13*1 + ... = -2.
            Plane p = Matrix4x4.Translate(new Vector3(1, 2, 3)).TransformPlane(new Plane(Vector3.up, 0f));
            Assert.That(p.normal, Is.EqualTo(Vector3.up));
            Assert.That(p.distance, Is.EqualTo(-2f));

            // Rotate 90 degrees about y via Rotate((0.5,0.5,0.5,0.5)) style exact matrix: use the permutation matrix
            // x->y, y->z, z->x (m10 = 1, m21 = 1, m02 = 1). Its inverse is its transpose (m01 = 1, m12 = 1, m20 = 1).
            // Plane normal (1,0,0), d = 4: a = it.m00*1 + it.m10*0 + it.m20*0 + it.m30*4 = 0; b = it.m01*1 = 1; c = it.m02 = 0; d = it.m03*1 + it.m33*4 = 4.
            Matrix4x4 perm = Rows(0, 0, 1, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1);
            Plane q = perm.TransformPlane(new Plane(Vector3.right, 4f));
            Assert.That(q.normal.x, Is.EqualTo(0f).Within(1e-7f));
            Assert.That(q.normal.y, Is.EqualTo(1f).Within(1e-7f));
            Assert.That(q.normal.z, Is.EqualTo(0f).Within(1e-7f));
            Assert.That(q.distance, Is.EqualTo(4f).Within(1e-7f));
        }

        // ------------------------------------------------------------------ Managed factories

        [Test]
        public void Scale_IsDiagonal()
        {
            Matrix4x4 expected = Rows(2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 4, 0, 0, 0, 0, 1);
            AssertBitExact(expected, Matrix4x4.Scale(new Vector3(2, 3, 4)));
            Vector3 s = new Vector3(2, 3, 4);
            AssertBitExact(expected, Matrix4x4.Scale(in s));
        }

        [Test]
        public void Translate_IsIdentityWithLastColumn()
        {
            Matrix4x4 expected = Rows(1, 0, 0, 5, 0, 1, 0, 6, 0, 0, 1, 7, 0, 0, 0, 1);
            AssertBitExact(expected, Matrix4x4.Translate(new Vector3(5, 6, 7)));
            Vector3 t = new Vector3(5, 6, 7);
            AssertBitExact(expected, Matrix4x4.Translate(in t));
        }

        [Test]
        public void Rotate_Identity()
        {
            AssertBitExact(Matrix4x4.identity, Matrix4x4.Rotate(Quaternion.identity));
        }

        [Test]
        public void Rotate_HandComputed_HalfHalfHalfHalf()
        {
            // q = (0.5, 0.5, 0.5, 0.5) (120 degrees about (1,1,1)): x2 = y2 = z2 = 1; xx = yy = zz = xy = xz = yz = wx = wy = wz = 0.5.
            // m00 = 1 - (0.5+0.5) = 0; m10 = xy + wz = 1; m20 = xz - wy = 0
            // m01 = xy - wz = 0;       m11 = 1 - (xx+zz) = 0; m21 = yz + wx = 1
            // m02 = xz + wy = 1;       m12 = yz - wx = 0;     m22 = 1 - (xx+yy) = 0
            Matrix4x4 expected = Rows(0, 0, 1, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1);
            AssertBitExact(expected, Matrix4x4.Rotate(new Quaternion(0.5f, 0.5f, 0.5f, 0.5f)));
        }

        [Test]
        public void Rotate_HandComputed_XAxis_FollowsFormulaInFloat()
        {
            // q = (0.6, 0, 0, 0.8) (unit): x2 = 1.2; xx = 0.6*1.2; wx = 0.8*1.2; all y/z products 0.
            // m00 = 1 - (0+0) = 1; m11 = 1 - (xx+0); m22 = 1 - (xx+0); m21 = yz + wx = wx; m12 = yz - wx = -wx;
            // m10 = xy + wz = 0; m20 = xz - wy = 0; m01 = 0; m02 = 0 (all zero products, sign of zero kept as +0).
            float x2 = 0.6f * 2f;
            float xx = 0.6f * x2;
            float wx = 0.8f * x2;
            Matrix4x4 m = Matrix4x4.Rotate(new Quaternion(0.6f, 0f, 0f, 0.8f));
            Assert.That(Bits(m.m00), Is.EqualTo(Bits(1f)));
            Assert.That(Bits(m.m11), Is.EqualTo(Bits(1f - (xx + 0f))));
            Assert.That(Bits(m.m22), Is.EqualTo(Bits(1f - (xx + 0f))));
            Assert.That(Bits(m.m21), Is.EqualTo(Bits(0f + wx)));
            Assert.That(Bits(m.m12), Is.EqualTo(Bits(0f - wx)));
            Assert.That(m.m10, Is.EqualTo(0f)); Assert.That(m.m20, Is.EqualTo(0f)); Assert.That(m.m01, Is.EqualTo(0f)); Assert.That(m.m02, Is.EqualTo(0f));
            Assert.That(m.m30, Is.EqualTo(0f)); Assert.That(m.m31, Is.EqualTo(0f)); Assert.That(m.m32, Is.EqualTo(0f)); Assert.That(m.m33, Is.EqualTo(1f));
            Assert.That(m.m03, Is.EqualTo(0f)); Assert.That(m.m13, Is.EqualTo(0f)); Assert.That(m.m23, Is.EqualTo(0f));
            // Numerically: xx = 0.72, wx = 0.96000004, 1 - xx = 0.27999997 (float).
            Assert.That(m.m11, Is.EqualTo(0.28f).Within(1e-6f));
            Assert.That(m.m21, Is.EqualTo(0.96f).Within(1e-6f));
        }

        [Test]
        public void Rotate_DoesNotNormalise()
        {
            // q = (0, 0, 0, 2): xx.. = 0, so m = identity regardless of |q| = 2 (no normalisation).
            AssertBitExact(Matrix4x4.identity, Matrix4x4.Rotate(new Quaternion(0, 0, 0, 2f)));
            // q = (0,0,0,0): also identity (1 - 0).
            AssertBitExact(Matrix4x4.identity, Matrix4x4.Rotate(new Quaternion(0, 0, 0, 0)));
            // q = (1, 0, 0, 0) doubled to (2,0,0,0): xx = 2*4 = 8 -> m11 = m22 = -7 (a normalised q would give -1).
            Matrix4x4 m = Matrix4x4.Rotate(new Quaternion(2f, 0, 0, 0));
            Assert.That(m.m11, Is.EqualTo(-7f));
            Assert.That(m.m22, Is.EqualTo(-7f));
        }

        [Test]
        public void Rotate_InTwin_MatchesByValue()
        {
            Quaternion q = Quaternion.Euler(10, 20, 30);
            AssertBitExact(Matrix4x4.Rotate(q), Matrix4x4.Rotate(in q));
        }

        [Test]
        public void Rotate_MatchesQuaternionVectorProduct()
        {
            // Rotate(q).MultiplyVector(v) and q * v use the same coefficient formulas (spec section 8 / section 9).
            Quaternion q = Quaternion.Euler(10, 20, 30);
            Vector3 v = new Vector3(1, 2, 3);
            Vector3 viaMatrix = Matrix4x4.Rotate(q).MultiplyVector(v);
            Vector3 viaQuat = q * v;
            Assert.That(viaMatrix.x, Is.EqualTo(viaQuat.x).Within(1e-6f));
            Assert.That(viaMatrix.y, Is.EqualTo(viaQuat.y).Within(1e-6f));
            Assert.That(viaMatrix.z, Is.EqualTo(viaQuat.z).Within(1e-6f));
        }

        [Test]
        public void StaticAliases_MatchProperties()
        {
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(2, 3, 4));
            Assert.That(Bits(Matrix4x4.Determinant(m)), Is.EqualTo(Bits(m.determinant)));
            Assert.That(Bits(Matrix4x4.Determinant(in m)), Is.EqualTo(Bits(m.determinant)));
            AssertBitExact(m.inverse, Matrix4x4.Inverse(m));
            AssertBitExact(m.inverse, Matrix4x4.Inverse(in m));
            AssertBitExact(m.transpose, Matrix4x4.Transpose(m));
            AssertBitExact(m.transpose, Matrix4x4.Transpose(in m));
        }

        [Test]
        public void Frustum_FromPlanes_CallsSixFloatOverload()
        {
            var fp = new FrustumPlanes { left = -1, right = 1, bottom = -1, top = 1, zNear = 0.3f, zFar = 1000 };
            AssertBitExact(Matrix4x4.Frustum(-1, 1, -1, 1, 0.3f, 1000), Matrix4x4.Frustum(fp));
            AssertBitExact(Matrix4x4.Frustum(-1, 1, -1, 1, 0.3f, 1000), Matrix4x4.Frustum(in fp));
        }

        // ------------------------------------------------------------------ TRS

        [Test]
        public void TRS_IsBitIdenticalTo_TranslateRotateScale()
        {
            Vector3 pos = new Vector3(1.5f, -2.25f, 3.125f);
            Quaternion q = Quaternion.Euler(10, 20, 30);
            Vector3 s = new Vector3(2, 3, 4);
            Matrix4x4 expected = Matrix4x4.Translate(pos) * Matrix4x4.Rotate(q) * Matrix4x4.Scale(s);
            AssertBitExact(expected, Matrix4x4.TRS(pos, q, s), "TRS");
            AssertBitExact(expected, Matrix4x4.TRS(in pos, in q, in s), "TRS(in)");

            Matrix4x4 set = Matrix4x4.zero;
            set.SetTRS(pos, q, s);
            AssertBitExact(expected, set, "SetTRS");
        }

        [Test]
        public void TRS_EquivalentColumnScaledRotate()
        {
            // "take Rotate(q), multiply column 0 by s.x, column 1 by s.y, column 2 by s.z, set column 3 to (pos, 1)".
            Vector3 pos = new Vector3(1.5f, -2.25f, 3.125f);
            Quaternion q = Quaternion.Euler(10, 20, 30);
            Vector3 s = new Vector3(2, 3, 4);
            Matrix4x4 r = Matrix4x4.Rotate(q);
            Matrix4x4 expected;
            expected.m00 = r.m00 * s.x; expected.m10 = r.m10 * s.x; expected.m20 = r.m20 * s.x; expected.m30 = 0f;
            expected.m01 = r.m01 * s.y; expected.m11 = r.m11 * s.y; expected.m21 = r.m21 * s.y; expected.m31 = 0f;
            expected.m02 = r.m02 * s.z; expected.m12 = r.m12 * s.z; expected.m22 = r.m22 * s.z; expected.m32 = 0f;
            expected.m03 = pos.x; expected.m13 = pos.y; expected.m23 = pos.z; expected.m33 = 1f;
            AssertBitExact(expected, Matrix4x4.TRS(pos, q, s));
        }

        [Test]
        public void Rotate_Equals_TRS_ZeroQOne_BitForBit()
        {
            Quaternion q = Quaternion.Euler(120, 200, 300);
            AssertBitExact(Matrix4x4.Rotate(q), Matrix4x4.TRS(Vector3.zero, q, Vector3.one));
        }

        [Test]
        public void TRS_DoesNotNormaliseQuaternion()
        {
            // (0,0,0,2) is not unit; Rotate gives identity for it, so TRS(zero, (0,0,0,2), one) must be identity too.
            AssertBitExact(Matrix4x4.identity, Matrix4x4.TRS(Vector3.zero, new Quaternion(0, 0, 0, 2f), Vector3.one));
        }

        // ------------------------------------------------------------------ transpose

        [Test]
        public void Transpose_Exact()
        {
            Matrix4x4 expected = Rows(1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15, 4, 8, 12, 16);
            AssertBitExact(expected, Sequential().transpose);
            AssertBitExact(Sequential(), Sequential().transpose.transpose);
            // -0 and NaN are preserved as bits.
            Matrix4x4 m = Matrix4x4.zero;
            m.m01 = -0f; m.m23 = float.NaN;
            Matrix4x4 t = m.transpose;
            Assert.That(Bits(t.m10), Is.EqualTo(NegativeZeroBits));
            Assert.That(float.IsNaN(t.m32), Is.True);
        }

        // ------------------------------------------------------------------ determinant

        [Test]
        public void Determinant_Verified_Scale234_Is24()
        {
            Assert.That(Bits(Matrix4x4.Scale(new Vector3(2, 3, 4)).determinant), Is.EqualTo(Bits(24f)));
        }

        [Test]
        public void Determinant_Verified_TRS234_Is24Exactly()
        {
            // The spec does not name the quaternion used by the probe; a generic rotation is used here. Exactness relies
            // on the double-precision cofactor expansion rounding det(R)*24 back to 24 after the cast.
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(2, 3, 4));
            Assert.That(Bits(m.determinant), Is.EqualTo(Bits(24f)), $"det = {m.determinant:R}");
        }

        [Test]
        public void Determinant_Verified_Perspective_BitExact()
        {
            // [verified] Perspective(60,1.5,0.3,1000).determinant = -1.20036006 bit-exact (0xBF99A566).
            float det = Matrix4x4.Perspective(60, 1.5f, 0.3f, 1000).determinant;
            Assert.That(Bits(det), Is.EqualTo(unchecked((int)0xBF99A566)), $"det = {det:R}");
            Assert.That(det, Is.EqualTo(-1.20036006f));
        }

        [Test]
        public void Determinant_HandComputed()
        {
            // Upper triangular rows (2,0,0,1)/(0,3,0,2)/(0,0,4,3)/(0,0,0,1): det = 2*3*4*1 = 24.
            Assert.That(Rows(2, 0, 0, 1, 0, 3, 0, 2, 0, 0, 4, 3, 0, 0, 0, 1).determinant, Is.EqualTo(24f));
            // rows (1,2,0,0)/(3,4,0,0)/(0,0,1,0)/(0,0,0,1): det = 1*4 - 2*3 = -2.
            Assert.That(Rows(1, 2, 0, 0, 3, 4, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1).determinant, Is.EqualTo(-2f));
            // Swapping two rows negates: rows (0,1,0,0)/(1,0,0,0)/(0,0,1,0)/(0,0,0,1) -> -1.
            Assert.That(Rows(0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1).determinant, Is.EqualTo(-1f));
            Assert.That(Matrix4x4.identity.determinant, Is.EqualTo(1f));
            Assert.That(Matrix4x4.zero.determinant, Is.EqualTo(0f));
            Assert.That(Matrix4x4.Translate(new Vector3(5, 6, 7)).determinant, Is.EqualTo(1f));
            // Full 4x4 via the spec's cofactor terms, rows (1,2,3,4)/(5,6,7,8)/(2,6,4,8)/(3,1,1,2):
            // v0 = m20*m31 - m21*m30 = 2*1 - 6*3 = -16; v1 = m20*m32 - m22*m30 = 2*1 - 4*3 = -10; v2 = m20*m33 - m23*m30 = 2*2 - 8*3 = -20;
            // v3 = m21*m32 - m22*m31 = 6*1 - 4*1 = 2;   v4 = m21*m33 - m23*m31 = 6*2 - 8*1 = 4;    v5 = m22*m33 - m23*m32 = 4*2 - 8*1 = 0;
            // t00 = +(v5*m11 - v4*m12 + v3*m13) = 0 - 4*7 + 2*8 = -12
            // t10 = -(v5*m10 - v2*m12 + v1*m13) = -(0 + 20*7 - 10*8) = -60
            // t20 = +(v4*m10 - v2*m11 + v0*m13) = 4*5 + 20*6 - 16*8 = 12
            // t30 = -(v3*m10 - v1*m11 + v0*m12) = -(2*5 + 10*6 - 16*7) = 42
            // det = t00*m00 + t10*m01 + t20*m02 + t30*m03 = -12 - 120 + 36 + 168 = 72
            Assert.That(Rows(1, 2, 3, 4, 5, 6, 7, 8, 2, 6, 4, 8, 3, 1, 1, 2).determinant, Is.EqualTo(72f));
        }

        [Test]
        public void Determinant_IsEvaluatedInDouble()
        {
            // rows (a,b,0,0)/(c,d,0,0)/(0,0,1,0)/(0,0,0,1) with a = d = 2^24+2, b = 2^24, c = 2^24+4:
            // a*d = 2^48 + 2^26 + 4 (exact in double, rounds to 2^48 + 2^26 in float); b*c = 2^48 + 2^26 exactly.
            // Double cofactor expansion: det = a*d - b*c = 4; a float-per-operation evaluation would give 0.
            float a = 16777218f, b = 16777216f, c = 16777220f, d = 16777218f;
            Assert.That(Rows(a, b, 0, 0, c, d, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1).determinant, Is.EqualTo(4f));
        }

        // ------------------------------------------------------------------ inverse

        [Test]
        public void Inverse_Singular_ReturnsZero()
        {
            AssertBitExact(Matrix4x4.zero, Matrix4x4.zero.inverse, "zero");
            AssertBitExact(Matrix4x4.zero, Matrix4x4.Scale(new Vector3(1, 0, 1)).inverse, "Scale(1,0,1)");
            AssertBitExact(Matrix4x4.zero, Matrix4x4.Scale(new Vector3(0, 0, 0)).inverse, "Scale(0)");
            // Rank-deficient non-diagonal: rows (1,2,3,4)/(5,6,7,8)/(9,10,11,12)/(13,14,15,16) has det 0. Float
            // elimination alone would not catch this one (its pivots stay nonzero and it produces garbage of order 1e6),
            // so the determinant test is what makes it return zero.
            AssertBitExact(Matrix4x4.zero, Sequential().inverse, "Sequential");
        }

        [Test]
        public void Inverse_TinyButNonSingular_IsNotTreatedAsSingular()
        {
            // Spec §8 gates on "determinant 0", but the determinant is only reported as a float: Scale(1e-16)'s
            // determinant is 1e-48, which is 0f after the cast while the matrix is perfectly invertible (its elimination
            // pivot is 1e-16). Gating on the float value would return zero here and lose a legitimate inverse.
            Matrix4x4 tiny = Matrix4x4.Scale(new Vector3(1e-16f, 1e-16f, 1e-16f));
            Assert.That(tiny.determinant, Is.EqualTo(0f), "the float determinant does underflow");
            AssertBitExact(Matrix4x4.Scale(new Vector3(1e16f, 1e16f, 1e16f)), tiny.inverse, "Scale(1e-16)");

            // Same for a rotated tiny scale (determinant 6e-48, also 0f): round-tripping a point through m and
            // m.inverse returns it, which a zero matrix could never do.
            Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(10, 20, 30), new Vector3(1e-16f, 2e-16f, 3e-16f));
            Assert.That(m.determinant, Is.EqualTo(0f), "the float determinant underflows here too");
            Assert.That(m.inverse, Is.Not.EqualTo(Matrix4x4.zero));
            Vector3 p = m.inverse.MultiplyPoint3x4(m.MultiplyPoint3x4(new Vector3(1, 2, 3)));
            Assert.That(p.x, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(p.y, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(p.z, Is.EqualTo(3f).Within(1e-3f));
        }

        [Test]
        public void Inverse_TranslateAndScale_Exact()
        {
            AssertBitExact(Matrix4x4.Translate(new Vector3(-1, -2, -3)), Matrix4x4.Translate(new Vector3(1, 2, 3)).inverse);
            AssertBitExact(Matrix4x4.Scale(new Vector3(0.5f, 0.25f, 0.125f)), Matrix4x4.Scale(new Vector3(2, 4, 8)).inverse);
            AssertBitExact(Matrix4x4.identity, Matrix4x4.identity.inverse);
        }

        [Test]
        public void Inverse_PermutationRequiringPivoting()
        {
            // rows (0,1,0,0)/(0,0,1,0)/(1,0,0,0)/(0,0,0,1): zero on the diagonal, so elimination must pivot;
            // inverse is the transpose (x<-z, y<-x, z<-y).
            Matrix4x4 perm = Rows(0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1);
            AssertBitExact(perm.transpose, perm.inverse);
        }

        [Test]
        public void Inverse_TimesOriginal_IsIdentity_Approximately()
        {
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(2, 3, 4));
            AssertWithin(Matrix4x4.identity, m * m.inverse, 1e-5f, "TRS * inverse");
            AssertWithin(Matrix4x4.identity, m.inverse * m, 1e-5f, "inverse * TRS");

            Matrix4x4 p = Matrix4x4.Perspective(60, 1.5f, 0.3f, 1000);
            AssertWithin(Matrix4x4.identity, p * p.inverse, 1e-4f, "Perspective * inverse");
        }

        [Test]
        public void Inverse_Perspective_M22_NearZero_NotExact()
        {
            // Unity's elimination gives m22 = -5.9568897e-08 instead of 0; the spec says the bit pattern is not
            // reproducible, so only the magnitude is checked.
            Matrix4x4 inv = Matrix4x4.Perspective(60, 1.5f, 0.3f, 1000).inverse;
            Assert.That(inv.m22, Is.EqualTo(0f).Within(1e-6f));
            // The analytically exact entries: inverse of a perspective matrix has m32 = 1/m23, m33 = -m22/m23, m23 = -1 (from m32 = -1).
            Matrix4x4 p = Matrix4x4.Perspective(60, 1.5f, 0.3f, 1000);
            Assert.That(inv.m00, Is.EqualTo(1f / p.m00).Within(1e-6f));
            Assert.That(inv.m11, Is.EqualTo(1f / p.m11).Within(1e-6f));
            Assert.That(inv.m23, Is.EqualTo(-1f).Within(1e-6f));
            // The z/w block of P is [[m22, m23], [-1, 0]] with determinant m23, so its inverse is
            // (1/m23) * [[0, -m23], [1, m22]] = [[0, -1], [1/m23, m22/m23]].
            Assert.That(inv.m32, Is.EqualTo(1f / p.m23).Within(1e-5f));
            Assert.That(inv.m33, Is.EqualTo(p.m22 / p.m23).Within(1e-5f));
        }

        [Test]
        public void Inverse_TRS_MatchesInverse3DAffine_Approximately()
        {
            // Spec: differs from Inverse3DAffine by about 1.8e-7 for TRS matrices.
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(2, 3, 4));
            Matrix4x4 affine = Matrix4x4.zero;
            Assert.That(Matrix4x4.Inverse3DAffine(m, ref affine), Is.True);
            AssertWithin(affine, m.inverse, 2e-6f, "inverse vs Inverse3DAffine");
        }

        [Test]
        public void Inverse3DAffine_Singular_ReturnsFalseAndZero()
        {
            Matrix4x4 result = Matrix4x4.identity;
            Assert.That(Matrix4x4.Inverse3DAffine(Matrix4x4.Scale(new Vector3(1, 0, 1)), ref result), Is.False);
            AssertBitExact(Matrix4x4.zero, result);

            result = Matrix4x4.identity;
            Assert.That(Matrix4x4.Inverse3DAffine(Matrix4x4.zero, ref result), Is.False);
            AssertBitExact(Matrix4x4.zero, result);
        }

        [Test]
        public void Inverse3DAffine_Regular_ReturnsTrueAndInverse()
        {
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(2, 3, 4));
            Matrix4x4 result = Matrix4x4.zero;
            Assert.That(Matrix4x4.Inverse3DAffine(m, ref result), Is.True);
            AssertWithin(Matrix4x4.identity, m * result, 1e-5f);
            Assert.That(result.m30, Is.EqualTo(0f)); Assert.That(result.m31, Is.EqualTo(0f)); Assert.That(result.m32, Is.EqualTo(0f)); Assert.That(result.m33, Is.EqualTo(1f));

            // Translate(1,2,3): inverse translation = (-1,-2,-3) exactly.
            Matrix4x4 t = Matrix4x4.zero;
            Assert.That(Matrix4x4.Inverse3DAffine(Matrix4x4.Translate(new Vector3(1, 2, 3)), ref t), Is.True);
            AssertBitExact(Matrix4x4.Translate(new Vector3(-1, -2, -3)), t);

            Matrix4x4 input = Matrix4x4.Scale(new Vector3(2, 4, 8));
            Matrix4x4 viaIn = Matrix4x4.zero;
            Assert.That(Matrix4x4.Inverse3DAffine(in input, ref viaIn), Is.True);
            // The translation column is -(inv3 * t) = -(0) = negative zero here; the spec gives no verified bit pattern
            // for Inverse3DAffine, so the comparison is by value (which treats -0 and +0 as equal).
            AssertWithin(Matrix4x4.Scale(new Vector3(0.5f, 0.25f, 0.125f)), viaIn, 0f, "Scale(2,4,8) affine inverse");
            Assert.That(viaIn.m00, Is.EqualTo(0.5f)); Assert.That(viaIn.m11, Is.EqualTo(0.25f)); Assert.That(viaIn.m22, Is.EqualTo(0.125f));
        }

        // ------------------------------------------------------------------ isIdentity

        [Test]
        public void IsIdentity_IsApproximate()
        {
            Assert.That(Matrix4x4.identity.isIdentity, Is.True);
            Assert.That(Matrix4x4.zero.isIdentity, Is.False);

            // 9e-6 deviations pass ...
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = 1f + 9e-6f; m.m01 = 9e-6f; m.m12 = -9e-6f; m.m33 = 1f - 9e-6f;
            Assert.That(m.isIdentity, Is.True, "9e-6");

            // ... 1.1e-5 deviations fail (diagonal and off-diagonal).
            m = Matrix4x4.identity; m.m00 = 1f + 1.1e-5f;
            Assert.That(m.isIdentity, Is.False, "m00 = 1 + 1.1e-5");
            m = Matrix4x4.identity; m.m01 = 1.1e-5f;
            Assert.That(m.isIdentity, Is.False, "m01 = 1.1e-5");
            m = Matrix4x4.identity; m.m21 = -1.1e-5f;
            Assert.That(m.isIdentity, Is.False, "m21 = -1.1e-5");
        }

        [Test]
        public void IsIdentity_ToleranceIsStrictlyLessThan()
        {
            // The spec could not verify whether a deviation of exactly 1e-5 passes and prescribes Abs(diff) < 1e-5f,
            // so an off-diagonal element of exactly 1e-5 must fail (|1e-5| < 1e-5 is false) while its predecessor passes.
            Matrix4x4 m = Matrix4x4.identity; m.m01 = 1e-5f;
            Assert.That(m.isIdentity, Is.False, "exactly 1e-5 is not < 1e-5");
            m = Matrix4x4.identity; m.m01 = BitConverter.Int32BitsToSingle(Bits(1e-5f) - 1); // one ulp below 1e-5
            Assert.That(m.isIdentity, Is.True, "one ulp below 1e-5 passes");
        }

        [Test]
        public void IsIdentity_ChecksBottomRow()
        {
            Matrix4x4 m = Matrix4x4.identity; m.m30 = 1e-4f;
            Assert.That(m.isIdentity, Is.False, "m30 = 1e-4");
            m = Matrix4x4.identity; m.m33 = 1f + 1e-4f;
            Assert.That(m.isIdentity, Is.False, "m33 = 1 + 1e-4");
        }

        [Test]
        public void IsIdentity_NaN_IsFalse()
        {
            for (int i = 0; i < 16; i++)
            {
                Matrix4x4 m = Matrix4x4.identity;
                m[i] = float.NaN;
                Assert.That(m.isIdentity, Is.False, $"NaN at [{i}]");
            }
        }

        [Test]
        public void IsIdentity_NotIdentity_Obvious()
        {
            Assert.That(Matrix4x4.Scale(new Vector3(2, 1, 1)).isIdentity, Is.False);
            Assert.That(Matrix4x4.Translate(new Vector3(0, 0, 1e-3f)).isIdentity, Is.False);
            Assert.That(Matrix4x4.Perspective(60, 1.5f, 0.3f, 1000).isIdentity, Is.False);
        }

        // ------------------------------------------------------------------ ValidTRS

        [Test]
        public void ValidTRS_OnlyChecksLastRow()
        {
            Assert.That(Matrix4x4.identity.ValidTRS(), Is.True);
            Assert.That(Matrix4x4.Scale(new Vector3(0, 0, 0)).ValidTRS(), Is.True, "zero scale passes");
            Matrix4x4 shear = Matrix4x4.identity; shear.m01 = 0.5f;
            Assert.That(shear.ValidTRS(), Is.True, "shear passes");
            Assert.That(Matrix4x4.Ortho(0, 800, 600, 0, -1, 1).ValidTRS(), Is.True, "Ortho passes");
            Assert.That(Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(2, 3, 4)).ValidTRS(), Is.True);

            Matrix4x4 m = Matrix4x4.identity; m.m30 = 1e-6f;
            Assert.That(m.ValidTRS(), Is.False, "m30 = 1e-6");
            m = Matrix4x4.identity; m.m33 = 1f + 1e-6f;
            Assert.That(m.ValidTRS(), Is.False, "m33 = 1 + 1e-6");
            m = Matrix4x4.identity; m.m31 = -1e-7f;
            Assert.That(m.ValidTRS(), Is.False, "m31 = -1e-7");
            m = Matrix4x4.identity; m.m32 = float.NaN;
            Assert.That(m.ValidTRS(), Is.False, "m32 = NaN");
            Assert.That(Matrix4x4.Perspective(60, 1.5f, 0.3f, 1000).ValidTRS(), Is.False, "Perspective fails");
            Assert.That(Matrix4x4.zero.ValidTRS(), Is.False, "zero fails (m33 = 0)");
        }

        // ------------------------------------------------------------------ lossyScale

        [Test]
        public void LossyScale_PositiveScale()
        {
            Assert.That(Matrix4x4.identity.lossyScale, Is.EqualTo(Vector3.one));
            Assert.That(Matrix4x4.Scale(new Vector3(2, 3, 4)).lossyScale, Is.EqualTo(new Vector3(2, 3, 4)));
            Vector3 ls = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(2, 3, 4)).lossyScale;
            Assert.That(ls.x, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(ls.y, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(ls.z, Is.EqualTo(4f).Within(1e-5f));
        }

        [TestCase(-2f, 3f, 4f, -2f, 3f, 4f)]
        [TestCase(2f, -3f, 4f, -2f, 3f, 4f)]
        [TestCase(2f, 3f, -4f, -2f, 3f, 4f)]
        [TestCase(-2f, -3f, -4f, -2f, 3f, 4f)]
        [TestCase(-2f, -3f, 4f, 2f, 3f, 4f)]
        [TestCase(-2f, 3f, -4f, 2f, 3f, 4f)]
        [TestCase(2f, -3f, -4f, 2f, 3f, 4f)]
        public void LossyScale_SignGoesToXOnly(float sx, float sy, float sz, float ex, float ey, float ez)
        {
            // [verified] the sign of the upper-3x3 determinant is applied to x only.
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(sx, sy, sz));
            Vector3 ls = m.lossyScale;
            Assert.That(ls.x, Is.EqualTo(ex).Within(1e-5f), "x");
            Assert.That(ls.y, Is.EqualTo(ey).Within(1e-5f), "y");
            Assert.That(ls.z, Is.EqualTo(ez).Within(1e-5f), "z");

            // Same without rotation: exact.
            Vector3 plain = Matrix4x4.Scale(new Vector3(sx, sy, sz)).lossyScale;
            Assert.That(plain, Is.EqualTo(new Vector3(ex, ey, ez)));
        }

        [Test]
        public void LossyScale_ShearedIdentity_Verified()
        {
            // [verified] m01 = 0.5 -> (1, 1.118034, 1): |column1| = sqrt(0.5^2 + 1^2) = sqrt(1.25) = 1.118034 (float).
            Matrix4x4 m = Matrix4x4.identity; m.m01 = 0.5f;
            Vector3 ls = m.lossyScale;
            Assert.That(ls.x, Is.EqualTo(1f));
            Assert.That(Bits(ls.y), Is.EqualTo(Bits(1.118034f)));
            Assert.That(ls.z, Is.EqualTo(1f));
        }

        [Test]
        public void LossyScale_Perspective_Verified()
        {
            // [verified] Perspective(60,1.5,0.3,1000).lossyScale = (-1.1547005, 1.7320508, 1.0006001):
            // column0 = (m00,0,0), column1 = (0,m11,0), column2 = (0,0,m22) with m22 < 0 so det3x3 < 0 -> sign on x.
            Matrix4x4 p = Matrix4x4.Perspective(60, 1.5f, 0.3f, 1000);
            Vector3 ls = p.lossyScale;
            Assert.That(ls.x, Is.EqualTo(-1.1547005f).Within(2).Ulps, "x");
            Assert.That(ls.y, Is.EqualTo(1.7320508f).Within(2).Ulps, "y");
            Assert.That(ls.z, Is.EqualTo(1.0006001f).Within(2).Ulps, "z");
            Assert.That(ls.x, Is.EqualTo(-p.m00));
            Assert.That(ls.y, Is.EqualTo(p.m11));
            Assert.That(ls.z, Is.EqualTo(-p.m22));
        }

        // ------------------------------------------------------------------ rotation

        [Test]
        public void Rotation_OfRotateMatrix_RoundTrips()
        {
            // [verified] Rotate(Euler(120,200,300)).rotation is the source quaternion within 1 ulp in Unity; the shim's
            // extraction is unverified so a looser per-component tolerance is used.
            Quaternion q = Quaternion.Euler(120, 200, 300);
            AssertSameRotation(q, Matrix4x4.Rotate(q).rotation, 1e-6f, "Euler(120,200,300)");
            Quaternion q2 = Quaternion.Euler(10, 20, 30);
            AssertSameRotation(q2, Matrix4x4.Rotate(q2).rotation, 1e-6f, "Euler(10,20,30)");
            AssertSameRotation(Quaternion.identity, Matrix4x4.identity.rotation, 0f, "identity");
        }

        [Test]
        public void Rotation_OfTRS_IgnoresPositiveScale()
        {
            // [verified] TRS(p, q, (2,3,4)).rotation ~ q within 3e-8.
            Quaternion q = Quaternion.Euler(10, 20, 30);
            Quaternion r = Matrix4x4.TRS(new Vector3(1, 2, 3), q, new Vector3(2, 3, 4)).rotation;
            AssertSameRotation(q, r, 1e-6f);
        }

        [Test]
        public void Rotation_OfTRS_NegativeScale_FollowsSignOnXConvention()
        {
            // [verified] (2,-3,4) -> q * AngleAxis(180, z); (2,3,-4) -> q * AngleAxis(180, y); (-2,-3,-4) -> q * AngleAxis(180, x); (-2,3,4) -> q.
            Quaternion q = Quaternion.Euler(10, 20, 30);
            Vector3 p = new Vector3(1, 2, 3);
            AssertSameRotation(q * Quaternion.AngleAxis(180, Vector3.forward), Matrix4x4.TRS(p, q, new Vector3(2, -3, 4)).rotation, 1e-6f, "(2,-3,4)");
            AssertSameRotation(q * Quaternion.AngleAxis(180, Vector3.up), Matrix4x4.TRS(p, q, new Vector3(2, 3, -4)).rotation, 1e-6f, "(2,3,-4)");
            AssertSameRotation(q * Quaternion.AngleAxis(180, Vector3.right), Matrix4x4.TRS(p, q, new Vector3(-2, -3, -4)).rotation, 1e-6f, "(-2,-3,-4)");
            AssertSameRotation(q, Matrix4x4.TRS(p, q, new Vector3(-2, 3, 4)).rotation, 1e-6f, "(-2,3,4)");
        }

        [Test]
        public void Rotation_Perspective_IsIdentity()
        {
            // [verified] Perspective(...).rotation = identity.
            AssertSameRotation(Quaternion.identity, Matrix4x4.Perspective(60, 1.5f, 0.3f, 1000).rotation, 1e-6f);
        }

        [Test]
        public void Rotation_ResultIsUnit()
        {
            Quaternion r = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(45, 90, 135), new Vector3(0.5f, 2, 7)).rotation;
            Assert.That(Quaternion.Dot(r, r), Is.EqualTo(1f).Within(1e-6f));
        }

        // ------------------------------------------------------------------ Ortho

        [Test]
        public void Ortho_Verified_0_800_600_0_m1_1_BitExact()
        {
            // dx = 800, dy = -600, dz = 2: m00 = 2/800 = 0.0025, m03 = -(800+0)/800 = -1, m11 = 2/-600 = -0.0033333334,
            // m13 = -(0+600)/-600 = 1, m22 = -2/2 = -1, m23 = -(1 + -1)/2 = -0.
            Matrix4x4 m = Matrix4x4.Ortho(0, 800, 600, 0, -1, 1);
            Matrix4x4 expected = Rows(0.0025f, 0, 0, -1, 0, -0.0033333334f, 0, 1, 0, 0, -1, -0f, 0, 0, 0, 1);
            AssertBitExact(expected, m);
            Assert.That(Bits(m.m23), Is.EqualTo(NegativeZeroBits), "m23 is negative zero");
        }

        [Test]
        public void Ortho_Verified_0_800_0_600_m100_100_BitExact()
        {
            // dx = 800, dy = 600, dz = 200: m00 = 0.0025, m03 = -1, m11 = 2/600 = 0.0033333334, m13 = -1, m22 = -2/200 = -0.01, m23 = -(0)/200 = -0.
            Matrix4x4 m = Matrix4x4.Ortho(0, 800, 0, 600, -100, 100);
            Matrix4x4 expected = Rows(0.0025f, 0, 0, -1, 0, 0.0033333334f, 0, -1, 0, 0, -0.01f, -0f, 0, 0, 0, 1);
            AssertBitExact(expected, m);
            Assert.That(Bits(m.m23), Is.EqualTo(NegativeZeroBits));
        }

        [Test]
        public void Ortho_Verified_m1_1_m1_1_03_1000_ZRow()
        {
            // [verified] m22 = -0.0020006 [0xBB031C80], m23 = -1.0006001 [0xBF8013AA] (Unity); formula with dz once:
            // dz = 999.7; m22 = -2/999.7; m23 = -(1000.3)/999.7. The spec allows 1 ulp here.
            Matrix4x4 m = Matrix4x4.Ortho(-1, 1, -1, 1, 0.3f, 1000);
            Assert.That(m.m22, Is.EqualTo(BitConverter.Int32BitsToSingle(unchecked((int)0xBB031C80))).Within(1).Ulps, $"m22 = {m.m22:R} bits 0x{Bits(m.m22):X8}");
            Assert.That(m.m23, Is.EqualTo(BitConverter.Int32BitsToSingle(unchecked((int)0xBF8013AA))).Within(1).Ulps, $"m23 = {m.m23:R} bits 0x{Bits(m.m23):X8}");
            Assert.That(m.m00, Is.EqualTo(1f)); Assert.That(m.m11, Is.EqualTo(1f));
            Assert.That(Bits(m.m03), Is.EqualTo(NegativeZeroBits), "-(1 + -1)/2 = -0");
            Assert.That(Bits(m.m13), Is.EqualTo(NegativeZeroBits));
            Assert.That(m.m33, Is.EqualTo(1f));
        }

        [Test]
        public void Ortho_FormulaWithArbitraryValues()
        {
            // Ortho(2, 10, -4, 4, 1, 9): dx = 8, dy = 8, dz = 8 -> m00 = 0.25, m03 = -12/8 = -1.5, m11 = 0.25, m13 = -0/8 = -0,
            // m22 = -0.25, m23 = -10/8 = -1.25; rest identity.
            Matrix4x4 m = Matrix4x4.Ortho(2, 10, -4, 4, 1, 9);
            Matrix4x4 expected = Rows(0.25f, 0, 0, -1.5f, 0, 0.25f, 0, -0f, 0, 0, -0.25f, -1.25f, 0, 0, 0, 1);
            AssertBitExact(expected, m);
            Assert.That(m.ValidTRS(), Is.True);
        }

        // ------------------------------------------------------------------ Perspective

        [Test]
        public void Perspective_Verified_90_1_01_100_BitExact()
        {
            // rad = 45 * Deg2Rad; cot = cos/sin = 1 (0.70710677/0.70710677); dz = 0.1 - 100 = -99.9;
            // m22 = 100.1/-99.9 = -1.002002; m23 = 2*0.1*100/-99.9 = 20/-99.9 = -0.2002002; m32 = -1; m33 = 0.
            Matrix4x4 m = Matrix4x4.Perspective(90, 1, 0.1f, 100);
            Matrix4x4 expected = Rows(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1.002002f, -0.2002002f, 0, 0, -1, 0);
            AssertBitExact(expected, m);
        }

        [Test]
        public void Perspective_60_15_03_1000_WithinUlps()
        {
            // cot(30 deg) = 1.7320508; m00 = cot/1.5 = 1.1547005; dz = -999.7; m22 = 1000.3/-999.7 = -1.0006001; m23 = 600/-999.7 = -0.60018003.
            Matrix4x4 m = Matrix4x4.Perspective(60, 1.5f, 0.3f, 1000);
            Assert.That(m.m00, Is.EqualTo(1.1547005f).Within(2).Ulps);
            Assert.That(m.m11, Is.EqualTo(1.7320508f).Within(2).Ulps);
            Assert.That(m.m22, Is.EqualTo(-1.0006001f).Within(2).Ulps);
            Assert.That(m.m23, Is.EqualTo(-0.60018003f).Within(2).Ulps);
            Assert.That(m.m32, Is.EqualTo(-1f));
            Assert.That(m.m33, Is.EqualTo(0f));
            // Everything else is zero. Linear indices are column-major: the non-zero entries are
            // m00 = [0], m11 = [5], m22 = [10], m32 = [11] and m23 = [14].
            int[] zeroIdx = { 1, 2, 3, 4, 6, 7, 8, 9, 12, 13, 15 };
            foreach (int i in zeroIdx)
                Assert.That(m[i], Is.EqualTo(0f), $"[{i}]");
        }

        [Test]
        public void Perspective_FollowsFormulaInFloat()
        {
            // Recompute the spec formula literally and compare bit-for-bit (same Math.Cos/Sin path as the shim is allowed).
            float fov = 37f, aspect = 1.25f, zNear = 0.5f, zFar = 250f;
            float rad = (fov / 2f) * Mathf.Deg2Rad;
            float cot = (float)Math.Cos(rad) / (float)Math.Sin(rad);
            float dz = zNear - zFar;
            Matrix4x4 m = Matrix4x4.Perspective(fov, aspect, zNear, zFar);
            Assert.That(Bits(m.m00), Is.EqualTo(Bits(cot / aspect)));
            Assert.That(Bits(m.m11), Is.EqualTo(Bits(cot)));
            Assert.That(Bits(m.m22), Is.EqualTo(Bits((zFar + zNear) / dz)));
            Assert.That(Bits(m.m23), Is.EqualTo(Bits(2f * zNear * zFar / dz)));
            Assert.That(m.m32, Is.EqualTo(-1f));
        }

        // ------------------------------------------------------------------ Frustum

        [Test]
        public void Frustum_Verified_m1_1_m1_1_03_1000_WithinUlp()
        {
            // m00 = 2*0.3/2 = 0.3; m02 = 0/2 = 0; m11 = 0.3; m12 = 0; m22 = -(1000.3)/999.7 = -1.0006001; m23 = -(600)/999.7 = -0.60018003; m32 = -1.
            Matrix4x4 m = Matrix4x4.Frustum(-1, 1, -1, 1, 0.3f, 1000);
            Assert.That(m.m00, Is.EqualTo(0.3f).Within(1).Ulps);
            Assert.That(m.m11, Is.EqualTo(0.3f).Within(1).Ulps);
            Assert.That(m.m22, Is.EqualTo(-1.0006001f).Within(1).Ulps);
            Assert.That(m.m23, Is.EqualTo(-0.60018003f).Within(1).Ulps);
            Assert.That(m.m32, Is.EqualTo(-1f));
            Assert.That(m.m02, Is.EqualTo(0f));
            Assert.That(m.m12, Is.EqualTo(0f));
            Assert.That(m.m33, Is.EqualTo(0f));
            // Column-major linear indices: m02 = [8] and m12 = [9] are zero for this symmetric frustum, while the
            // non-zero entries are m00 = [0], m11 = [5], m22 = [10], m32 = [11] and m23 = [14].
            int[] zeroIdx = { 1, 2, 3, 4, 6, 7, 8, 9, 12, 13, 15 };
            foreach (int i in zeroIdx)
                Assert.That(m[i], Is.EqualTo(0f), $"[{i}]");
        }

        [Test]
        public void Frustum_OffAxis_HandComputed()
        {
            // Frustum(1, 3, -2, 2, 2, 6): r-l = 2, t-b = 4, f-n = 4:
            // m00 = 2*2/2 = 2; m02 = (3+1)/2 = 2; m11 = 2*2/4 = 1; m12 = (2-2)/4 = 0; m22 = -(6+2)/4 = -2; m23 = -(2*6*2)/4 = -6; m32 = -1.
            Matrix4x4 m = Matrix4x4.Frustum(1, 3, -2, 2, 2, 6);
            Matrix4x4 expected = Rows(2, 0, 2, 0, 0, 1, 0, 0, 0, 0, -2, -6, 0, 0, -1, 0);
            AssertBitExact(expected, m);
        }

        [Test]
        public void Frustum_SymmetricMatchesPerspective()
        {
            // Perspective(90,1,0.1,100) has cot = 1 exactly, so Frustum(-0.1,0.1,-0.1,0.1,0.1,100) must produce the same z row.
            Matrix4x4 p = Matrix4x4.Perspective(90, 1, 0.1f, 100);
            Matrix4x4 f = Matrix4x4.Frustum(-0.1f, 0.1f, -0.1f, 0.1f, 0.1f, 100);
            Assert.That(f.m00, Is.EqualTo(p.m00).Within(1).Ulps);
            Assert.That(f.m11, Is.EqualTo(p.m11).Within(1).Ulps);
            Assert.That(f.m22, Is.EqualTo(p.m22).Within(1).Ulps);
            Assert.That(f.m23, Is.EqualTo(p.m23).Within(1).Ulps);
        }

        // ------------------------------------------------------------------ decomposeProjection

        [Test]
        public void DecomposeProjection_Verified_Perspective()
        {
            // [verified] Perspective(60,1.5,0.3,1000) -> l = -0.25980765, r = 0.25980765, b = -0.1732051, t = 0.1732051, n = 0.3, f = 1000.1341.
            FrustumPlanes fp = Matrix4x4.Perspective(60, 1.5f, 0.3f, 1000).decomposeProjection;
            Assert.That(fp.left, Is.EqualTo(-0.25980765f).Within(1e-6f), "left");
            Assert.That(fp.right, Is.EqualTo(0.25980765f).Within(1e-6f), "right");
            Assert.That(fp.bottom, Is.EqualTo(-0.1732051f).Within(1e-6f), "bottom");
            Assert.That(fp.top, Is.EqualTo(0.1732051f).Within(1e-6f), "top");
            Assert.That(fp.zNear, Is.EqualTo(0.3f).Within(1e-6f), "zNear");
            Assert.That(fp.zFar, Is.EqualTo(1000.1341f).Within(1e-3f), "zFar");
        }

        [Test]
        public void DecomposeProjection_RoundTripsFrustum()
        {
            // Frustum(1,3,-2,2,2,6) is exact in float (see Frustum_OffAxis_HandComputed), so decomposing it recovers the planes.
            FrustumPlanes fp = Matrix4x4.Frustum(1, 3, -2, 2, 2, 6).decomposeProjection;
            Assert.That(fp.left, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(fp.right, Is.EqualTo(3f).Within(1e-6f));
            Assert.That(fp.bottom, Is.EqualTo(-2f).Within(1e-6f));
            Assert.That(fp.top, Is.EqualTo(2f).Within(1e-6f));
            Assert.That(fp.zNear, Is.EqualTo(2f).Within(1e-6f));
            Assert.That(fp.zFar, Is.EqualTo(6f).Within(1e-6f));
        }

        [Test]
        public void DecomposeProjection_RoundTripsOrtho()
        {
            // unverified vs Unity (the spec only gives the perspective sample); Ortho(2,10,-4,4,1,9) is exact in float.
            FrustumPlanes fp = Matrix4x4.Ortho(2, 10, -4, 4, 1, 9).decomposeProjection;
            Assert.That(fp.left, Is.EqualTo(2f).Within(1e-6f));
            Assert.That(fp.right, Is.EqualTo(10f).Within(1e-6f));
            Assert.That(fp.bottom, Is.EqualTo(-4f).Within(1e-6f));
            Assert.That(fp.top, Is.EqualTo(4f).Within(1e-6f));
            Assert.That(fp.zNear, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(fp.zFar, Is.EqualTo(9f).Within(1e-6f));
        }

        // ------------------------------------------------------------------ LookAt

        [Test]
        public void LookAt_MatchesTRSWithLookRotation()
        {
            // [verified] equals TRS(from, LookRotation(to - from, up), one) within 6e-8.
            Vector3 from = new Vector3(1, 2, 3);
            Vector3 to = new Vector3(4, -1, 7);
            Vector3 up = Vector3.up;
            Matrix4x4 expected = Matrix4x4.TRS(from, Quaternion.LookRotation(to - from, up), Vector3.one);
            AssertWithin(expected, Matrix4x4.LookAt(from, to, up), 1e-6f, "LookAt");
            AssertWithin(expected, Matrix4x4.LookAt(in from, in to, in up), 1e-6f, "LookAt(in)");

            Vector3 tiltedUp = new Vector3(0, 1, 1);
            Matrix4x4 expected2 = Matrix4x4.TRS(from, Quaternion.LookRotation(to - from, tiltedUp), Vector3.one);
            AssertWithin(expected2, Matrix4x4.LookAt(from, to, tiltedUp), 1e-6f, "LookAt tilted up");
        }

        [Test]
        public void LookAt_Basics()
        {
            // Looking down +z from the origin with +y up is the identity.
            AssertWithin(Matrix4x4.identity, Matrix4x4.LookAt(Vector3.zero, Vector3.forward, Vector3.up), 1e-7f);
            // Looking down +x: TRS(zero, LookRotation(right), one) -> column2 = (1,0,0), column0 = (0,0,-1).
            Matrix4x4 m = Matrix4x4.LookAt(Vector3.zero, Vector3.right, Vector3.up);
            Assert.That(m.GetColumn(2).x, Is.EqualTo(1f).Within(1e-7f));
            Assert.That(m.GetColumn(0).z, Is.EqualTo(-1f).Within(1e-7f));
            Assert.That(m.GetColumn(1).y, Is.EqualTo(1f).Within(1e-7f));
            Assert.That(m.GetRow(3), Is.EqualTo(new Vector4(0, 0, 0, 1)));
        }

        [Test]
        public void LookAt_Degenerate_FromEqualsTo_IdentityRotationWithTranslation()
        {
            Vector3 from = new Vector3(5, 6, 7);
            AssertBitExact(Matrix4x4.Translate(from), Matrix4x4.LookAt(from, from, Vector3.up));
        }

        [Test]
        public void LookAt_Verified_ForwardParallelToUp_IsIdentity()
        {
            // [verified] LookAt(zero, up, up) returns the identity matrix (unlike LookRotation(up, up)).
            AssertBitExact(Matrix4x4.identity, Matrix4x4.LookAt(Vector3.zero, Vector3.up, Vector3.up));
        }

        // ------------------------------------------------------------------ ToString

        [Test]
        public void ToString_Verified_Identity()
        {
            const string expected = "1.00000\t0.00000\t0.00000\t0.00000\n0.00000\t1.00000\t0.00000\t0.00000\n0.00000\t0.00000\t1.00000\t0.00000\n0.00000\t0.00000\t0.00000\t1.00000\n";
            Assert.That(Matrix4x4.identity.ToString(), Is.EqualTo(expected));
            Assert.That(Matrix4x4.identity.ToString(null), Is.EqualTo(expected));
            Assert.That(Matrix4x4.identity.ToString(""), Is.EqualTo(expected));
            Assert.That(Matrix4x4.identity.ToString(null, null), Is.EqualTo(expected));
            Assert.That(((IFormattable)Matrix4x4.identity).ToString(null, null), Is.EqualTo(expected));
        }

        [Test]
        public void ToString_IsRowMajor()
        {
            // Translate(1,2,3) printed with F0: the translation appears in the last column of each row.
            Assert.That(Matrix4x4.Translate(new Vector3(1, 2, 3)).ToString("F0"), Is.EqualTo("1\t0\t0\t1\n0\t1\t0\t2\n0\t0\t1\t3\n0\t0\t0\t1\n"));
            Assert.That(Sequential().ToString("F0"), Is.EqualTo("1\t2\t3\t4\n5\t6\t7\t8\n9\t10\t11\t12\n13\t14\t15\t16\n"));
        }

        [Test]
        public void ToString_FormatAndProvider()
        {
            Matrix4x4 m = Matrix4x4.Translate(new Vector3(1.5f, -2.25f, 3));
            // unverified vs Unity: -2.25 is an exact "F1" midpoint and .NET (Core) rounds it to even -> "-2.2";
            // Unity's Mono formatter may round away from zero -> "-2.3". The shim only splices component.ToString,
            // so this is the runtime's formatter, not the matrix's, and the spec pins neither.
            Assert.That(m.ToString("F1"), Is.EqualTo("1.0\t0.0\t0.0\t1.5\n0.0\t1.0\t0.0\t-2.2\n0.0\t0.0\t1.0\t3.0\n0.0\t0.0\t0.0\t1.0\n"));
            // A non-midpoint value rounds identically everywhere.
            Assert.That(Matrix4x4.Translate(new Vector3(0, -2.34f, 0)).ToString("F1"), Does.Contain("\t-2.3\n"));
            var de = CultureInfo.GetCultureInfo("de-DE");
            Assert.That(m.ToString("F2", de), Is.EqualTo("1,00\t0,00\t0,00\t1,50\n0,00\t1,00\t0,00\t-2,25\n0,00\t0,00\t1,00\t3,00\n0,00\t0,00\t0,00\t1,00\n"));
            // Null provider falls back to the invariant culture even when the current culture uses commas.
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = de;
                Assert.That(m.ToString("F2", null), Is.EqualTo("1.00\t0.00\t0.00\t1.50\n0.00\t1.00\t0.00\t-2.25\n0.00\t0.00\t1.00\t3.00\n0.00\t0.00\t0.00\t1.00\n"));
                Assert.That(m.ToString(), Does.StartWith("1.00000\t0.00000\t0.00000\t1.50000\n"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void ToString_SpecialValues()
        {
            Matrix4x4 m = Matrix4x4.zero;
            m.m00 = float.NaN; m.m01 = float.PositiveInfinity; m.m02 = float.NegativeInfinity; m.m03 = -0f;
            string s = m.ToString("F1");
            Assert.That(s, Does.StartWith("NaN\tInfinity\t-Infinity\t-0.0\n"));
        }
    }
}
