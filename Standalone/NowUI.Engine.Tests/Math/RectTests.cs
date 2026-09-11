// Tests for the UnityEngine.Rect (§6), RectInt (§7) and Bounds (§10) shims against
// Docs/Standalone/UnityValueTypeSemantics.md. Every [verified] value in those sections is asserted exactly; formula-only
// members are checked with values worked out by hand from the spec's expression (arithmetic in the comments).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class RectTests
    {
        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        private static readonly CultureInfo German = new CultureInfo("de-DE");

        /// <summary>Runs <paramref name="body"/> with a comma-decimal current culture so "null provider → invariant" is observable.</summary>
        private static void WithGermanCulture(Action body)
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            CultureInfo savedUi = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = German;
                CultureInfo.CurrentUICulture = German;
                body();
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
                CultureInfo.CurrentUICulture = savedUi;
            }
        }

        // =====================================================================================================
        // §6 Rect
        // =====================================================================================================

        #region Rect: shape

        [Test]
        public void Rect_IsSerializableSequentialStructWithFourPrivateFloats()
        {
            Type t = typeof(Rect);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True);
            Assert.That(t.IsLayoutSequential, Is.True);
            Assert.That(typeof(IEquatable<Rect>).IsAssignableFrom(t), Is.True);
            Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);
            Assert.That(Marshal.SizeOf<Rect>(), Is.EqualTo(16));
            Assert.That(t.GetFields(BindingFlags.Instance | BindingFlags.Public), Is.Empty, "Rect keeps its fields private");

            // Field order m_XMin, m_YMin, m_Width, m_Height (spec §6).
            Assert.That((int)Marshal.OffsetOf<Rect>("m_XMin"), Is.EqualTo(0));
            Assert.That((int)Marshal.OffsetOf<Rect>("m_YMin"), Is.EqualTo(4));
            Assert.That((int)Marshal.OffsetOf<Rect>("m_Width"), Is.EqualTo(8));
            Assert.That((int)Marshal.OffsetOf<Rect>("m_Height"), Is.EqualTo(12));
        }

        [Test]
        public unsafe void Rect_ReinterpretsAsFourFloatsInDeclaredOrder()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f);
            float* p = (float*)&r;
            Assert.That(p[0], Is.EqualTo(1f));
            Assert.That(p[1], Is.EqualTo(2f));
            Assert.That(p[2], Is.EqualTo(3f));
            Assert.That(p[3], Is.EqualTo(4f));
        }

        #endregion

        #region Rect: constructors and statics

        [Test]
        public void Rect_FloatConstructor_StoresRawFields()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f);
            Assert.That(r.x, Is.EqualTo(1f));
            Assert.That(r.y, Is.EqualTo(2f));
            Assert.That(r.width, Is.EqualTo(3f));
            Assert.That(r.height, Is.EqualTo(4f));
        }

        [Test]
        public void Rect_VectorConstructor_StoresPositionAndSize()
        {
            Rect r = new Rect(new Vector2(1f, 2f), new Vector2(3f, 4f));
            Assert.That(r.x, Is.EqualTo(1f));
            Assert.That(r.y, Is.EqualTo(2f));
            Assert.That(r.width, Is.EqualTo(3f));
            Assert.That(r.height, Is.EqualTo(4f));
        }

        [Test]
        public void Rect_CopyConstructor_CopiesAllFields()
        {
            Rect src = new Rect(1f, 2f, -3f, -4f);
            Rect r = new Rect(src);
            Assert.That(r == src, Is.True);
            Assert.That(r.width, Is.EqualTo(-3f), "negative sizes are not normalised");
        }

        [Test]
        public void Rect_NegativeSizes_AreKeptVerbatim()
        {
            Rect r = new Rect(5f, 6f, -3f, -4f);
            Assert.That(r.width, Is.EqualTo(-3f));
            Assert.That(r.height, Is.EqualTo(-4f));
            Assert.That(r.xMin, Is.EqualTo(5f));
            Assert.That(r.xMax, Is.EqualTo(2f)); // 5 + (-3)
            Assert.That(r.yMax, Is.EqualTo(2f)); // 6 + (-4)
        }

        [Test]
        public void Rect_Zero_IsAllZero()
        {
            Rect z = Rect.zero;
            Assert.That(z.x, Is.EqualTo(0f));
            Assert.That(z.y, Is.EqualTo(0f));
            Assert.That(z.width, Is.EqualTo(0f));
            Assert.That(z.height, Is.EqualTo(0f));
            Assert.That(Rect.zero == new Rect(0f, 0f, 0f, 0f), Is.True);
        }

        [Test]
        public void Rect_MinMaxRect_SubtractsMinFromMax()
        {
            // (1, 2, 5 - 1, 8 - 2) = (1, 2, 4, 6)
            Rect r = Rect.MinMaxRect(1f, 2f, 5f, 8f);
            Assert.That(r.x, Is.EqualTo(1f));
            Assert.That(r.y, Is.EqualTo(2f));
            Assert.That(r.width, Is.EqualTo(4f));
            Assert.That(r.height, Is.EqualTo(6f));
        }

        [Test]
        public void Rect_MinMaxRect_AllowsInvertedEdges()
        {
            // (5, 8, 1 - 5, 2 - 8) = (5, 8, -4, -6): nothing normalises
            Rect r = Rect.MinMaxRect(5f, 8f, 1f, 2f);
            Assert.That(r.x, Is.EqualTo(5f));
            Assert.That(r.y, Is.EqualTo(8f));
            Assert.That(r.width, Is.EqualTo(-4f));
            Assert.That(r.height, Is.EqualTo(-6f));
        }

        [Test]
        public void Rect_NormalizedToPoint_LerpsBetweenEdges()
        {
            // x = Lerp(10, 110, 0.25) = 10 + 100 * 0.25 = 35 ; y = Lerp(20, 70, 0.5) = 20 + 50 * 0.5 = 45
            Rect r = new Rect(10f, 20f, 100f, 50f);
            Vector2 p = Rect.NormalizedToPoint(r, new Vector2(0.25f, 0.5f));
            Assert.That(p.x, Is.EqualTo(35f));
            Assert.That(p.y, Is.EqualTo(45f));
        }

        [Test]
        public void Rect_NormalizedToPoint_IsClamped()
        {
            // Clamp01(2) = 1 → xMin + width = 110 ; Clamp01(-1) = 0 → yMin = 20
            Rect r = new Rect(10f, 20f, 100f, 50f);
            Vector2 p = Rect.NormalizedToPoint(r, new Vector2(2f, -1f));
            Assert.That(p.x, Is.EqualTo(110f));
            Assert.That(p.y, Is.EqualTo(20f));
        }

        [Test]
        public void Rect_PointToNormalized_InverseLerpsBetweenEdges()
        {
            // x = InverseLerp(10, 110, 35) = (35 - 10) / 100 = 0.25 ; y = InverseLerp(20, 70, 45) = 25 / 50 = 0.5
            Rect r = new Rect(10f, 20f, 100f, 50f);
            Vector2 n = Rect.PointToNormalized(r, new Vector2(35f, 45f));
            Assert.That(n.x, Is.EqualTo(0.25f));
            Assert.That(n.y, Is.EqualTo(0.5f));
        }

        [Test]
        public void Rect_PointToNormalized_IsClamped()
        {
            // (200 - 10) / 100 = 1.9 → 1 ; (-5 - 20) / 50 = -0.5 → 0
            Rect r = new Rect(10f, 20f, 100f, 50f);
            Vector2 n = Rect.PointToNormalized(r, new Vector2(200f, -5f));
            Assert.That(n.x, Is.EqualTo(1f));
            Assert.That(n.y, Is.EqualTo(0f));
        }

        [Test]
        public void Rect_PointToNormalized_ZeroSizeGivesZero()
        {
            // InverseLerp(a, a, v) = 0 (spec §6/§11), even when the point is off to the right.
            Rect r = new Rect(10f, 20f, 0f, 0f);
            Vector2 n = Rect.PointToNormalized(r, new Vector2(50f, 50f));
            Assert.That(n.x, Is.EqualTo(0f));
            Assert.That(n.y, Is.EqualTo(0f));
        }

        #endregion

        #region Rect: property getters

        [Test]
        public void Rect_DerivedGetters_ForPositiveRect()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f);
            Assert.That(r.position, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(r.size, Is.EqualTo(new Vector2(3f, 4f)));
            Assert.That(r.min, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(r.max, Is.EqualTo(new Vector2(4f, 6f)));          // (1+3, 2+4)
            Assert.That(r.center, Is.EqualTo(new Vector2(2.5f, 4f)));     // (1 + 3*0.5, 2 + 4*0.5)
            Assert.That(r.xMin, Is.EqualTo(1f));
            Assert.That(r.yMin, Is.EqualTo(2f));
            Assert.That(r.xMax, Is.EqualTo(4f));
            Assert.That(r.yMax, Is.EqualTo(6f));
        }

        [Test]
        public void Rect_DerivedGetters_ForNegativeSizeRectAreNotNormalised()
        {
            Rect r = new Rect(1f, 2f, -3f, 4f);
            Assert.That(r.min, Is.EqualTo(new Vector2(1f, 2f)), "min is the raw origin, not the smaller edge");
            Assert.That(r.max, Is.EqualTo(new Vector2(-2f, 6f)));         // 1 + (-3)
            Assert.That(r.center.x, Is.EqualTo(-0.5f));                    // 1 + (-3)*0.5
            Assert.That(r.xMin, Is.EqualTo(1f));
            Assert.That(r.xMax, Is.EqualTo(-2f));
        }

        [Test]
        public void Rect_ObsoleteEdgeAliases_MapToMinMaxGetters()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f);
#pragma warning disable CS0618
            Assert.That(r.left, Is.EqualTo(1f));
            Assert.That(r.right, Is.EqualTo(4f));
            Assert.That(r.top, Is.EqualTo(2f));
            Assert.That(r.bottom, Is.EqualTo(6f));
#pragma warning restore CS0618
        }

        #endregion

        #region Rect: setters

        [Test]
        public void Rect_XAndYSetters_MoveWithoutResizing()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f) { x = 10f, y = 20f };
            Assert.That(r.x, Is.EqualTo(10f));
            Assert.That(r.y, Is.EqualTo(20f));
            Assert.That(r.width, Is.EqualTo(3f));
            Assert.That(r.height, Is.EqualTo(4f));
            Assert.That(r.xMax, Is.EqualTo(13f));
        }

        [Test]
        public void Rect_WidthAndHeightSetters_WriteRawFields()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f) { width = -7f, height = 0f };
            Assert.That(r.width, Is.EqualTo(-7f));
            Assert.That(r.height, Is.EqualTo(0f));
            Assert.That(r.x, Is.EqualTo(1f));
            Assert.That(r.y, Is.EqualTo(2f));
        }

        [Test]
        public void Rect_PositionSetter_KeepsSize()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f) { position = new Vector2(-5f, -6f) };
            Assert.That(r.x, Is.EqualTo(-5f));
            Assert.That(r.y, Is.EqualTo(-6f));
            Assert.That(r.size, Is.EqualTo(new Vector2(3f, 4f)));
        }

        [Test]
        public void Rect_SizeSetter_KeepsPosition()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f) { size = new Vector2(30f, 40f) };
            Assert.That(r.width, Is.EqualTo(30f));
            Assert.That(r.height, Is.EqualTo(40f));
            Assert.That(r.position, Is.EqualTo(new Vector2(1f, 2f)));
        }

        [Test]
        public void Rect_CenterSetter_MovesOriginByHalfSize()
        {
            // m_XMin = 10 - 3*0.5 = 8.5 ; m_YMin = 10 - 4*0.5 = 8 ; size unchanged
            Rect r = new Rect(1f, 2f, 3f, 4f) { center = new Vector2(10f, 10f) };
            Assert.That(r.x, Is.EqualTo(8.5f));
            Assert.That(r.y, Is.EqualTo(8f));
            Assert.That(r.width, Is.EqualTo(3f));
            Assert.That(r.height, Is.EqualTo(4f));
            Assert.That(r.center, Is.EqualTo(new Vector2(10f, 10f)));
        }

        [Test]
        public void Rect_XMinSetter_KeepsRightEdgeFixed()
        {
            // oldXMax = 1 + 3 = 4 ; m_XMin = 0 ; m_Width = 4 - 0 = 4
            Rect r = new Rect(1f, 2f, 3f, 4f) { xMin = 0f };
            Assert.That(r.x, Is.EqualTo(0f));
            Assert.That(r.width, Is.EqualTo(4f));
            Assert.That(r.xMax, Is.EqualTo(4f));
            Assert.That(r.height, Is.EqualTo(4f), "y axis untouched");
        }

        [Test]
        public void Rect_XMinSetter_PastRightEdgeGivesNegativeWidth()
        {
            // oldXMax = 4 ; m_XMin = 6 ; m_Width = 4 - 6 = -2
            Rect r = new Rect(1f, 2f, 3f, 4f) { xMin = 6f };
            Assert.That(r.x, Is.EqualTo(6f));
            Assert.That(r.width, Is.EqualTo(-2f));
            Assert.That(r.xMax, Is.EqualTo(4f));
        }

        [Test]
        public void Rect_YMinSetter_KeepsTopEdgeFixed()
        {
            // oldYMax = 2 + 4 = 6 ; m_YMin = -1 ; m_Height = 6 - (-1) = 7
            Rect r = new Rect(1f, 2f, 3f, 4f) { yMin = -1f };
            Assert.That(r.y, Is.EqualTo(-1f));
            Assert.That(r.height, Is.EqualTo(7f));
            Assert.That(r.yMax, Is.EqualTo(6f));
            Assert.That(r.width, Is.EqualTo(3f), "x axis untouched");
        }

        [Test]
        public void Rect_XMaxSetter_ChangesWidthOnly()
        {
            // m_Width = 10 - 1 = 9
            Rect r = new Rect(1f, 2f, 3f, 4f) { xMax = 10f };
            Assert.That(r.x, Is.EqualTo(1f));
            Assert.That(r.width, Is.EqualTo(9f));
            Assert.That(r.xMax, Is.EqualTo(10f));
        }

        [Test]
        public void Rect_YMaxSetter_ChangesHeightOnly()
        {
            // m_Height = 0 - 2 = -2
            Rect r = new Rect(1f, 2f, 3f, 4f) { yMax = 0f };
            Assert.That(r.y, Is.EqualTo(2f));
            Assert.That(r.height, Is.EqualTo(-2f));
            Assert.That(r.yMax, Is.EqualTo(0f));
        }

        [Test]
        public void Rect_MinSetter_RoutesThroughXMinYMin()
        {
            // xMin = 0 → width = 4 - 0 = 4 ; yMin = 0 → height = 6 - 0 = 6
            Rect r = new Rect(1f, 2f, 3f, 4f) { min = new Vector2(0f, 0f) };
            Assert.That(r.x, Is.EqualTo(0f));
            Assert.That(r.y, Is.EqualTo(0f));
            Assert.That(r.width, Is.EqualTo(4f));
            Assert.That(r.height, Is.EqualTo(6f));
            Assert.That(r.max, Is.EqualTo(new Vector2(4f, 6f)), "max unchanged");
        }

        [Test]
        public void Rect_MaxSetter_RoutesThroughXMaxYMax()
        {
            // width = 10 - 1 = 9 ; height = 10 - 2 = 8
            Rect r = new Rect(1f, 2f, 3f, 4f) { max = new Vector2(10f, 10f) };
            Assert.That(r.x, Is.EqualTo(1f));
            Assert.That(r.y, Is.EqualTo(2f));
            Assert.That(r.width, Is.EqualTo(9f));
            Assert.That(r.height, Is.EqualTo(8f));
            Assert.That(r.min, Is.EqualTo(new Vector2(1f, 2f)), "min unchanged");
        }

        [Test]
        public void Rect_Set_OverwritesAllFields()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f);
            r.Set(10f, 20f, 30f, 40f);
            Assert.That(r == new Rect(10f, 20f, 30f, 40f), Is.True);
        }

        #endregion

        #region Rect: Contains

        [Test]
        public void Rect_ContainsVector2_MinInclusiveMaxExclusive()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f); // x ∈ [1, 4), y ∈ [2, 6)
            Assert.That(r.Contains(new Vector2(1f, 2f)), Is.True, "min corner inclusive");
            Assert.That(r.Contains(new Vector2(3.999f, 5.999f)), Is.True);
            Assert.That(r.Contains(new Vector2(4f, 2f)), Is.False, "x == xMax exclusive");
            Assert.That(r.Contains(new Vector2(1f, 6f)), Is.False, "y == yMax exclusive");
            Assert.That(r.Contains(new Vector2(0.999f, 3f)), Is.False);
            Assert.That(r.Contains(new Vector2(2f, 1.999f)), Is.False);
            Assert.That(r.Contains(new Vector2(4f, 6f)), Is.False, "max corner exclusive");
        }

        [Test]
        public void Rect_ContainsVector2_NegativeSizeNeverContains()
        {
            Rect r = new Rect(1f, 2f, -3f, 4f); // xMax = -2 < xMin
            Assert.That(r.Contains(new Vector2(1f, 3f)), Is.False);
            Assert.That(r.Contains(new Vector2(0f, 3f)), Is.False);
            Assert.That(r.Contains(new Vector2(-1f, 3f)), Is.False);
            Assert.That(r.Contains(new Vector2(-2f, 3f)), Is.False);
        }

        [Test]
        public void Rect_ContainsVector2_NaNIsFalse()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f);
            Assert.That(r.Contains(new Vector2(float.NaN, 3f)), Is.False);
            Assert.That(r.Contains(new Vector2(2f, float.NaN)), Is.False);
        }

        [Test]
        public void Rect_ContainsVector3_IgnoresZ()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f);
            Assert.That(r.Contains(new Vector3(2f, 3f, 999f)), Is.True);
            Assert.That(r.Contains(new Vector3(2f, 3f, float.NaN)), Is.True);
            Assert.That(r.Contains(new Vector3(4f, 3f, 0f)), Is.False, "x == xMax exclusive");
        }

        [Test]
        public void Rect_ContainsAllowInverseFalse_MatchesPlainContains()
        {
            Rect r = new Rect(1f, 2f, -3f, 4f);
            Assert.That(r.Contains(new Vector3(0f, 3f, 0f), false), Is.False);
            Rect p = new Rect(1f, 2f, 3f, 4f);
            Assert.That(p.Contains(new Vector3(1f, 2f, 0f), false), Is.True);
            Assert.That(p.Contains(new Vector3(4f, 2f, 0f), false), Is.False);
        }

        [Test]
        public void Rect_ContainsAllowInverseTrue_InvertedAxisIsMaxInclusiveMinExclusive()
        {
            // width < 0: xmax = 4 + (-3) = 1 ; xAxis = point.x <= 4 && point.x > 1
            Rect r = new Rect(4f, 2f, -3f, 4f);
            Assert.That(r.Contains(new Vector3(4f, 3f, 0f), true), Is.True, "x == m_XMin inclusive on an inverted axis");
            Assert.That(r.Contains(new Vector3(2.5f, 3f, 0f), true), Is.True);
            Assert.That(r.Contains(new Vector3(1f, 3f, 0f), true), Is.False, "x == xmax exclusive on an inverted axis");
            Assert.That(r.Contains(new Vector3(4.001f, 3f, 0f), true), Is.False);
            // y axis is still the normal orientation: [2, 6)
            Assert.That(r.Contains(new Vector3(3f, 2f, 0f), true), Is.True);
            Assert.That(r.Contains(new Vector3(3f, 6f, 0f), true), Is.False);
        }

        [Test]
        public void Rect_ContainsAllowInverseTrue_InvertedHeight()
        {
            // height < 0: ymax = 6 + (-4) = 2 ; yAxis = point.y <= 6 && point.y > 2
            Rect r = new Rect(1f, 6f, 3f, -4f);
            Assert.That(r.Contains(new Vector3(2f, 6f, 0f), true), Is.True);
            Assert.That(r.Contains(new Vector3(2f, 2f, 0f), true), Is.False);
            Assert.That(r.Contains(new Vector3(2f, 4f, 0f), true), Is.True);
        }

        [Test]
        public void Rect_ContainsAllowInverseTrue_ZeroWidthIsFalse()
        {
            // m_Width >= 0 branch: point.x >= 1 && point.x < 1 can never hold
            Rect r = new Rect(1f, 2f, 0f, 4f);
            Assert.That(r.Contains(new Vector3(1f, 3f, 0f), true), Is.False);
        }

        #endregion

        #region Rect: Overlaps

        [Test]
        public void Rect_Overlaps_TrueWhenInteriorsIntersect()
        {
            Rect a = new Rect(0f, 0f, 10f, 10f);
            Assert.That(a.Overlaps(new Rect(5f, 5f, 10f, 10f)), Is.True);
            Assert.That(a.Overlaps(new Rect(2f, 2f, 1f, 1f)), Is.True, "contained rect");
            Assert.That(new Rect(2f, 2f, 1f, 1f).Overlaps(a), Is.True, "containing rect");
            Assert.That(a.Overlaps(new Rect(-5f, -5f, 6f, 6f)), Is.True);
        }

        [Test]
        public void Rect_Overlaps_TouchingEdgesDoNotOverlap()
        {
            Rect a = new Rect(0f, 0f, 10f, 10f);
            Assert.That(a.Overlaps(new Rect(10f, 0f, 5f, 5f)), Is.False, "other.xMin == xMax");
            Assert.That(a.Overlaps(new Rect(-5f, 0f, 5f, 5f)), Is.False, "other.xMax == xMin");
            Assert.That(a.Overlaps(new Rect(0f, 10f, 5f, 5f)), Is.False, "other.yMin == yMax");
            Assert.That(a.Overlaps(new Rect(0f, -5f, 5f, 5f)), Is.False, "other.yMax == yMin");
            Assert.That(a.Overlaps(new Rect(9.999f, 0f, 5f, 5f)), Is.True);
            Assert.That(a.Overlaps(new Rect(11f, 0f, 5f, 5f)), Is.False);
        }

        [Test]
        public void Rect_Overlaps_NegativeSizeOtherIsFalseWithoutAllowInverse()
        {
            // other.xMax = 10 + (-10) = 0 > xMin 0 fails → false even though the areas coincide
            Rect a = new Rect(0f, 0f, 10f, 10f);
            Rect inverted = new Rect(10f, 10f, -10f, -10f);
            Assert.That(a.Overlaps(inverted), Is.False);
            Assert.That(inverted.Overlaps(a), Is.False);
            Assert.That(a.Overlaps(inverted, false), Is.False);
        }

        [Test]
        public void Rect_OverlapsAllowInverse_NormalisesBothRects()
        {
            // Max(10, 0) = 10 > Min(0, 10) = 0 ; Min(10, 0) = 0 < Max(0, 10) = 10 → true
            Rect a = new Rect(0f, 0f, 10f, 10f);
            Rect inverted = new Rect(10f, 10f, -10f, -10f);
            Assert.That(a.Overlaps(inverted, true), Is.True);
            Assert.That(inverted.Overlaps(a, true), Is.True);
            Assert.That(inverted.Overlaps(new Rect(5f, 5f, 2f, 2f), true), Is.True);
            // still strict: normalised edges touching → false
            Assert.That(a.Overlaps(new Rect(15f, 5f, -5f, 5f), true), Is.False, "normalised other.xMin == 10 == xMax");
            Assert.That(a.Overlaps(new Rect(15f, 5f, -5.5f, 5f), true), Is.True);
        }

        [Test]
        public void Rect_OverlapsAllowInverseTrue_ForPositiveRectsMatchesPlain()
        {
            Rect a = new Rect(0f, 0f, 10f, 10f);
            Assert.That(a.Overlaps(new Rect(5f, 5f, 10f, 10f), true), Is.True);
            Assert.That(a.Overlaps(new Rect(10f, 0f, 5f, 5f), true), Is.False);
        }

        #endregion

        #region Rect: equality, hashing

        [Test]
        public void Rect_OperatorEquals_IsExactWithNoTolerance()
        {
            Rect a = new Rect(1f, 2f, 3f, 4f);
            Assert.That(a == new Rect(1f, 2f, 3f, 4f), Is.True);
            Assert.That(a != new Rect(1f, 2f, 3f, 4f), Is.False);
            // 1e-7 would pass the Vector2 tolerance; Rect has none.
            Assert.That(a == new Rect(1f + 1e-7f, 2f, 3f, 4f), Is.False);
            Assert.That(a != new Rect(1f + 1e-7f, 2f, 3f, 4f), Is.True);
            Assert.That(a == new Rect(1f, 2f, 3f, 4.0000005f), Is.False);
        }

        [Test]
        public void Rect_OperatorEquals_EachFieldParticipates()
        {
            Rect a = new Rect(1f, 2f, 3f, 4f);
            Assert.That(a == new Rect(9f, 2f, 3f, 4f), Is.False);
            Assert.That(a == new Rect(1f, 9f, 3f, 4f), Is.False);
            Assert.That(a == new Rect(1f, 2f, 9f, 4f), Is.False);
            Assert.That(a == new Rect(1f, 2f, 3f, 9f), Is.False);
        }

        [Test]
        public void Rect_OperatorEquals_NaNIsFalseAndNotEqualIsTrue()
        {
            Rect n = new Rect(float.NaN, 2f, 3f, 4f);
#pragma warning disable CS1718
            Assert.That(n == n, Is.False);
            Assert.That(n != n, Is.True);
#pragma warning restore CS1718
        }

        [Test]
        public void Rect_Equals_UsesFloatEqualsSoNaNEqualsNaN()
        {
            // [verified] Rect.Equals(itself) with a NaN component is true.
            Rect n = new Rect(float.NaN, 2f, 3f, 4f);
            Assert.That(n.Equals(n), Is.True);
            Assert.That(n.Equals(new Rect(float.NaN, 2f, 3f, 4f)), Is.True);
            Assert.That(new Rect(1f, float.NaN, float.NaN, float.NaN).Equals(new Rect(1f, float.NaN, float.NaN, float.NaN)), Is.True);
            Assert.That(n.Equals(new Rect(1f, 2f, 3f, 4f)), Is.False);
        }

        [Test]
        public void Rect_Equals_IsExactOtherwise()
        {
            Rect a = new Rect(1f, 2f, 3f, 4f);
            Assert.That(a.Equals(new Rect(1f, 2f, 3f, 4f)), Is.True);
            Assert.That(a.Equals(new Rect(1f + 1e-7f, 2f, 3f, 4f)), Is.False);
            Assert.That(new Rect(0f, 0f, 0f, 0f).Equals(new Rect(-0f, -0f, -0f, -0f)), Is.True, "float.Equals treats ±0 as equal");
        }

        [Test]
        public void Rect_EqualsObject_HandlesBoxedRectAndOtherTypes()
        {
            Rect a = new Rect(1f, 2f, 3f, 4f);
            Assert.That(a.Equals((object)new Rect(1f, 2f, 3f, 4f)), Is.True);
            Assert.That(a.Equals((object)new Rect(0f, 2f, 3f, 4f)), Is.False);
            Assert.That(a.Equals(null), Is.False);
            Assert.That(a.Equals("(x:1.00, y:2.00, width:3.00, height:4.00)"), Is.False);
            Assert.That(a.Equals(new RectInt(1, 2, 3, 4)), Is.False);
        }

        [Test]
        public void Rect_GetHashCode_VerifiedValue()
        {
            // [verified] (1,2,3,4) → 247463936
            Assert.That(new Rect(1f, 2f, 3f, 4f).GetHashCode(), Is.EqualTo(247463936));
        }

        [Test]
        public void Rect_GetHashCode_FollowsXWidthYHeightFormula()
        {
            // x ^ (width << 2) ^ (y >> 2) ^ (height >> 1) on the raw float bits:
            // (1,0,0,0): 0x3F800000 ^ 0 ^ 0 ^ 0 = 1065353216
            Assert.That(new Rect(1f, 0f, 0f, 0f).GetHashCode(), Is.EqualTo(1065353216));
            // (0,0,0,0) → 0
            Assert.That(new Rect(0f, 0f, 0f, 0f).GetHashCode(), Is.EqualTo(0));
            // (5,-2,0.5,8): 0x40A00000 ^ (0x3F000000 << 2 = 0xFC000000) ^ (0xC0000000 >> 2 = 0xF0000000) ^ (0x41000000 >> 1 = 0x20800000)
            //             = 0x40A00000 ^ 0xFC000000 = 0xBCA00000 ; ^ 0xF0000000 = 0x4CA00000 ; ^ 0x20800000 = 0x6C200000 = 1814036480
            Assert.That(new Rect(5f, -2f, 0.5f, 8f).GetHashCode(), Is.EqualTo(1814036480));
            // The x/width/y/height order matters: swapping y and width changes the hash.
            Assert.That(new Rect(1f, 3f, 2f, 4f).GetHashCode(), Is.Not.EqualTo(new Rect(1f, 2f, 3f, 4f).GetHashCode()));
        }

        [Test]
        public void Rect_GetHashCode_EqualRectsHashEqually()
        {
            Assert.That(new Rect(1.25f, -7f, 3e5f, 4e-3f).GetHashCode(), Is.EqualTo(new Rect(1.25f, -7f, 3e5f, 4e-3f).GetHashCode()));
        }

        #endregion

        #region Rect: ToString

        [Test]
        public void Rect_ToString_DefaultIsF2Template()
        {
            Assert.That(new Rect(1f, 2f, 3f, 4f).ToString(), Is.EqualTo("(x:1.00, y:2.00, width:3.00, height:4.00)"));
            Assert.That(new Rect(-1.5f, 0f, 2.345f, 1e3f).ToString(), Is.EqualTo("(x:-1.50, y:0.00, width:2.35, height:1000.00)"));
        }

        [Test]
        public void Rect_ToString_NullOrEmptyFormatFallsBackToF2()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f);
            Assert.That(r.ToString(null), Is.EqualTo(r.ToString()));
            Assert.That(r.ToString(""), Is.EqualTo(r.ToString()));
            Assert.That(r.ToString(null, null), Is.EqualTo(r.ToString()));
            Assert.That(((IFormattable)r).ToString(null, null), Is.EqualTo(r.ToString()));
        }

        [Test]
        public void Rect_ToString_HonoursExplicitFormat()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f);
            Assert.That(r.ToString("F0"), Is.EqualTo("(x:1, y:2, width:3, height:4)"));
            Assert.That(r.ToString("F3"), Is.EqualTo("(x:1.000, y:2.000, width:3.000, height:4.000)"));
            Assert.That(new Rect(1.5f, 2f, 3f, 4f).ToString("E2"), Is.EqualTo("(x:1.50E+000, y:2.00E+000, width:3.00E+000, height:4.00E+000)"));
        }

        [Test]
        public void Rect_ToString_HonoursExplicitProvider()
        {
            Assert.That(new Rect(1.5f, 2f, 3f, 4f).ToString("F1", German), Is.EqualTo("(x:1,5, y:2,0, width:3,0, height:4,0)"));
            Assert.That(new Rect(1.5f, 2f, 3f, 4f).ToString(null, German), Is.EqualTo("(x:1,50, y:2,00, width:3,00, height:4,00)"));
        }

        [Test]
        public void Rect_ToString_NullProviderIsInvariantEvenUnderCommaCulture()
        {
            WithGermanCulture(() =>
            {
                Rect r = new Rect(1.5f, 2f, 3f, 4f);
                Assert.That(r.ToString(), Is.EqualTo("(x:1.50, y:2.00, width:3.00, height:4.00)"));
                Assert.That(r.ToString("F1"), Is.EqualTo("(x:1.5, y:2.0, width:3.0, height:4.0)"));
                Assert.That(r.ToString("F1", null), Is.EqualTo("(x:1.5, y:2.0, width:3.0, height:4.0)"));
            });
        }

        #endregion

        // =====================================================================================================
        // §7 RectInt
        // =====================================================================================================

        #region RectInt: shape, constructors, raw properties

        [Test]
        public void RectInt_IsSerializableSequentialStructWithFourPrivateInts()
        {
            Type t = typeof(RectInt);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True);
            Assert.That(t.IsLayoutSequential, Is.True);
            Assert.That(typeof(IEquatable<RectInt>).IsAssignableFrom(t), Is.True);
            Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);
            Assert.That(Marshal.SizeOf<RectInt>(), Is.EqualTo(16));
            Assert.That(t.GetFields(BindingFlags.Instance | BindingFlags.Public), Is.Empty);
            Assert.That((int)Marshal.OffsetOf<RectInt>("m_XMin"), Is.EqualTo(0));
            Assert.That((int)Marshal.OffsetOf<RectInt>("m_YMin"), Is.EqualTo(4));
            Assert.That((int)Marshal.OffsetOf<RectInt>("m_Width"), Is.EqualTo(8));
            Assert.That((int)Marshal.OffsetOf<RectInt>("m_Height"), Is.EqualTo(12));
        }

        [Test]
        public void RectInt_Constructors_StoreRawFields()
        {
            RectInt a = new RectInt(1, 2, 3, 4);
            Assert.That(a.x, Is.EqualTo(1));
            Assert.That(a.y, Is.EqualTo(2));
            Assert.That(a.width, Is.EqualTo(3));
            Assert.That(a.height, Is.EqualTo(4));

            RectInt b = new RectInt(new Vector2Int(-1, -2), new Vector2Int(-3, -4));
            Assert.That(b.x, Is.EqualTo(-1));
            Assert.That(b.y, Is.EqualTo(-2));
            Assert.That(b.width, Is.EqualTo(-3), "raw size is not normalised");
            Assert.That(b.height, Is.EqualTo(-4));
        }

        [Test]
        public void RectInt_Zero_IsAllZero()
        {
            Assert.That(RectInt.zero == new RectInt(0, 0, 0, 0), Is.True);
            Assert.That(RectInt.zero.size, Is.EqualTo(Vector2Int.zero));
        }

        [Test]
        public void RectInt_RawPropertySetters_WriteFieldsIndependently()
        {
            RectInt r = new RectInt(1, 2, 3, 4) { x = 10, y = 20, width = -30, height = 40 };
            Assert.That(r.x, Is.EqualTo(10));
            Assert.That(r.y, Is.EqualTo(20));
            Assert.That(r.width, Is.EqualTo(-30));
            Assert.That(r.height, Is.EqualTo(40));
        }

        [Test]
        public void RectInt_PositionAndSize_GetAndSetRawFields()
        {
            RectInt r = new RectInt(1, 2, 3, 4);
            Assert.That(r.position, Is.EqualTo(new Vector2Int(1, 2)));
            Assert.That(r.size, Is.EqualTo(new Vector2Int(3, 4)));
            r.position = new Vector2Int(7, 8);
            r.size = new Vector2Int(-9, -10);
            Assert.That(r.x, Is.EqualTo(7));
            Assert.That(r.y, Is.EqualTo(8));
            Assert.That(r.width, Is.EqualTo(-9));
            Assert.That(r.height, Is.EqualTo(-10));
            Assert.That(r.position, Is.EqualTo(new Vector2Int(7, 8)));
            Assert.That(r.size, Is.EqualTo(new Vector2Int(-9, -10)), "size getter is raw even when inverted");
        }

        [Test]
        public void RectInt_Center_IsRawFloatCentre()
        {
            // (1 + 3*0.5, 2 + 4*0.5) = (2.5, 4)
            Assert.That(new RectInt(1, 2, 3, 4).center, Is.EqualTo(new Vector2(2.5f, 4f)));
            // Inverted rect: (1 + (-3)*0.5, 2 + 4*0.5) = (-0.5, 4) — raw, not normalised
            Assert.That(new RectInt(1, 2, -3, 4).center, Is.EqualTo(new Vector2(-0.5f, 4f)));
            // (4 + (-3)*0.5, 6 + (-4)*0.5) = (2.5, 4): same centre as the positive rect covering the same cells
            Assert.That(new RectInt(4, 6, -3, -4).center, Is.EqualTo(new Vector2(2.5f, 4f)));
        }

        #endregion

        #region RectInt: normalising edge properties

        [Test]
        public void RectInt_EdgeGetters_ForPositiveRect()
        {
            RectInt r = new RectInt(1, 2, 3, 4);
            Assert.That(r.xMin, Is.EqualTo(1));
            Assert.That(r.xMax, Is.EqualTo(4));
            Assert.That(r.yMin, Is.EqualTo(2));
            Assert.That(r.yMax, Is.EqualTo(6));
            Assert.That(r.min, Is.EqualTo(new Vector2Int(1, 2)));
            Assert.That(r.max, Is.EqualTo(new Vector2Int(4, 6)));
        }

        [Test]
        public void RectInt_EdgeGetters_NormaliseInvertedRect()
        {
            // xMin = Min(5, 5 + (-3)) = 2 ; xMax = Max(5, 2) = 5 ; yMin = Min(7, 3) = 3 ; yMax = 7
            RectInt r = new RectInt(5, 7, -3, -4);
            Assert.That(r.xMin, Is.EqualTo(2));
            Assert.That(r.xMax, Is.EqualTo(5));
            Assert.That(r.yMin, Is.EqualTo(3));
            Assert.That(r.yMax, Is.EqualTo(7));
            Assert.That(r.min, Is.EqualTo(new Vector2Int(2, 3)));
            Assert.That(r.max, Is.EqualTo(new Vector2Int(5, 7)));
            Assert.That(r.x, Is.EqualTo(5), "raw x is untouched");
            Assert.That(r.width, Is.EqualTo(-3), "raw width is untouched");
        }

        [Test]
        public void RectInt_XMinSetter_UsesNormalisedOldXMax()
        {
            // positive: old xMax = 4 ; m_XMin = 0 ; m_Width = 4 - 0 = 4
            RectInt a = new RectInt(1, 2, 3, 4) { xMin = 0 };
            Assert.That(a.x, Is.EqualTo(0));
            Assert.That(a.width, Is.EqualTo(4));
            Assert.That(a.height, Is.EqualTo(4));

            // inverted (5,7,-3,-4): normalised old xMax = 5 ; m_XMin = 0 ; m_Width = 5 - 0 = 5 (the rect un-inverts on x)
            RectInt b = new RectInt(5, 7, -3, -4) { xMin = 0 };
            Assert.That(b.x, Is.EqualTo(0));
            Assert.That(b.width, Is.EqualTo(5));
            Assert.That(b.xMax, Is.EqualTo(5));
            Assert.That(b.height, Is.EqualTo(-4), "y axis untouched");
        }

        [Test]
        public void RectInt_YMinSetter_UsesNormalisedOldYMax()
        {
            // positive: old yMax = 6 ; m_YMin = -1 ; m_Height = 6 - (-1) = 7
            RectInt a = new RectInt(1, 2, 3, 4) { yMin = -1 };
            Assert.That(a.y, Is.EqualTo(-1));
            Assert.That(a.height, Is.EqualTo(7));

            // inverted: normalised old yMax = 7 ; m_YMin = 1 ; m_Height = 7 - 1 = 6
            RectInt b = new RectInt(5, 7, -3, -4) { yMin = 1 };
            Assert.That(b.y, Is.EqualTo(1));
            Assert.That(b.height, Is.EqualTo(6));
            Assert.That(b.width, Is.EqualTo(-3), "x axis untouched");
        }

        [Test]
        public void RectInt_XMaxSetter_UsesRawXMin()
        {
            // positive: m_Width = 10 - 1 = 9
            RectInt a = new RectInt(1, 2, 3, 4) { xMax = 10 };
            Assert.That(a.x, Is.EqualTo(1));
            Assert.That(a.width, Is.EqualTo(9));

            // inverted (5,7,-3,-4): m_Width = 10 - m_XMin(5) = 5, NOT 10 - normalised xMin(2)
            RectInt b = new RectInt(5, 7, -3, -4) { xMax = 10 };
            Assert.That(b.x, Is.EqualTo(5));
            Assert.That(b.width, Is.EqualTo(5));
            Assert.That(b.xMax, Is.EqualTo(10));

            // setting xMax below the raw x gives a negative width
            RectInt c = new RectInt(1, 2, 3, 4) { xMax = -1 };
            Assert.That(c.width, Is.EqualTo(-2));
            Assert.That(c.xMin, Is.EqualTo(-1), "normalised getter now reports the new edge as xMin");
            Assert.That(c.xMax, Is.EqualTo(1));
        }

        [Test]
        public void RectInt_YMaxSetter_UsesRawYMin()
        {
            // positive: m_Height = 0 - 2 = -2
            RectInt a = new RectInt(1, 2, 3, 4) { yMax = 0 };
            Assert.That(a.y, Is.EqualTo(2));
            Assert.That(a.height, Is.EqualTo(-2));

            // inverted: m_Height = 10 - 7 = 3
            RectInt b = new RectInt(5, 7, -3, -4) { yMax = 10 };
            Assert.That(b.height, Is.EqualTo(3));
        }

        [Test]
        public void RectInt_MinSetter_RoutesThroughXMinYMin()
        {
            // old max = (4, 6) ; new raw origin (0, 0) ; width = 4, height = 6
            RectInt r = new RectInt(1, 2, 3, 4) { min = new Vector2Int(0, 0) };
            Assert.That(r.x, Is.EqualTo(0));
            Assert.That(r.y, Is.EqualTo(0));
            Assert.That(r.width, Is.EqualTo(4));
            Assert.That(r.height, Is.EqualTo(6));
            Assert.That(r.max, Is.EqualTo(new Vector2Int(4, 6)));
        }

        [Test]
        public void RectInt_MaxSetter_RoutesThroughXMaxYMax()
        {
            // width = 10 - 1 = 9 ; height = 10 - 2 = 8
            RectInt r = new RectInt(1, 2, 3, 4) { max = new Vector2Int(10, 10) };
            Assert.That(r.x, Is.EqualTo(1));
            Assert.That(r.y, Is.EqualTo(2));
            Assert.That(r.width, Is.EqualTo(9));
            Assert.That(r.height, Is.EqualTo(8));
        }

        #endregion

        #region RectInt: methods

        [Test]
        public void RectInt_SetMinMax_WritesOriginAndDifference()
        {
            RectInt r = new RectInt(9, 9, 9, 9);
            r.SetMinMax(new Vector2Int(1, 2), new Vector2Int(4, 7));
            Assert.That(r == new RectInt(1, 2, 3, 5), Is.True);
        }

        [Test]
        public void RectInt_SetMinMax_InvertedInputProducesNegativeSize()
        {
            // m_XMin = 4 ; m_Width = 1 - 4 = -3 ; m_YMin = 7 ; m_Height = 2 - 7 = -5
            RectInt r = new RectInt(0, 0, 0, 0);
            r.SetMinMax(new Vector2Int(4, 7), new Vector2Int(1, 2));
            Assert.That(r == new RectInt(4, 7, -3, -5), Is.True);
            Assert.That(r.min, Is.EqualTo(new Vector2Int(1, 2)), "getters normalise it again");
        }

        [Test]
        public void RectInt_ClampToBounds_ShrinksOversizedRect()
        {
            // bounds edges 0..10 ; m_XMin = Max(Min(10, -5), 0) = 0 ; m_Width = Min(10 - 0, 20) = 10 (same for y)
            RectInt r = new RectInt(-5, -5, 20, 20);
            r.ClampToBounds(new RectInt(0, 0, 10, 10));
            Assert.That(r == new RectInt(0, 0, 10, 10), Is.True);
        }

        [Test]
        public void RectInt_ClampToBounds_ClipsRectHangingOverTheEdge()
        {
            // m_XMin = Max(Min(10, 8), 0) = 8 ; m_Width = Min(10 - 8, 5) = 2
            RectInt r = new RectInt(8, 8, 5, 5);
            r.ClampToBounds(new RectInt(0, 0, 10, 10));
            Assert.That(r == new RectInt(8, 8, 2, 2), Is.True);
        }

        [Test]
        public void RectInt_ClampToBounds_RectOutsideCollapsesToZeroSizeAtEdge()
        {
            // m_XMin = Max(Min(10, 15), 0) = 10 ; m_Width = Min(10 - 10, 2) = 0
            RectInt r = new RectInt(15, 15, 2, 2);
            r.ClampToBounds(new RectInt(0, 0, 10, 10));
            Assert.That(r == new RectInt(10, 10, 0, 0), Is.True);
        }

        [Test]
        public void RectInt_ClampToBounds_LeavesRectInsideUnchanged()
        {
            RectInt r = new RectInt(2, 3, 4, 5);
            r.ClampToBounds(new RectInt(0, 0, 10, 10));
            Assert.That(r == new RectInt(2, 3, 4, 5), Is.True);
        }

        [Test]
        public void RectInt_ClampToBounds_KeepsNegativeRawSize()
        {
            // m_XMin = 5 ; m_Width = Min(10 - 5, -3) = -3 (Min keeps the negative raw width)
            RectInt r = new RectInt(5, 5, -3, -3);
            r.ClampToBounds(new RectInt(0, 0, 10, 10));
            Assert.That(r == new RectInt(5, 5, -3, -3), Is.True);
        }

        [Test]
        public void RectInt_ClampToBounds_UsesNormalisedBoundsEdges()
        {
            // bounds (10,10,-10,-10) normalises to 0..10 — identical result to the positive bounds
            RectInt r = new RectInt(-5, -5, 20, 20);
            r.ClampToBounds(new RectInt(10, 10, -10, -10));
            Assert.That(r == new RectInt(0, 0, 10, 10), Is.True);
        }

        [Test]
        public void RectInt_Contains_MinInclusiveMaxExclusive()
        {
            RectInt r = new RectInt(1, 2, 3, 4); // x ∈ [1,4), y ∈ [2,6)
            Assert.That(r.Contains(new Vector2Int(1, 2)), Is.True);
            Assert.That(r.Contains(new Vector2Int(3, 5)), Is.True);
            Assert.That(r.Contains(new Vector2Int(4, 2)), Is.False, "x == xMax");
            Assert.That(r.Contains(new Vector2Int(3, 6)), Is.False, "y == yMax");
            Assert.That(r.Contains(new Vector2Int(0, 3)), Is.False);
            Assert.That(r.Contains(new Vector2Int(2, 1)), Is.False);
        }

        [Test]
        public void RectInt_Contains_InvertedRectStillContainsPoints()
        {
            // (4,6,-3,-4) normalises to x ∈ [1,4), y ∈ [2,6) — unlike Rect, which never contains for negative sizes
            RectInt r = new RectInt(4, 6, -3, -4);
            Assert.That(r.Contains(new Vector2Int(1, 2)), Is.True);
            Assert.That(r.Contains(new Vector2Int(3, 5)), Is.True);
            Assert.That(r.Contains(new Vector2Int(4, 6)), Is.False, "raw origin is the exclusive max corner");
            Assert.That(r.Contains(new Vector2Int(0, 1)), Is.False);
        }

        [Test]
        public void RectInt_Contains_ZeroSizeContainsNothing()
        {
            Assert.That(new RectInt(1, 2, 0, 4).Contains(new Vector2Int(1, 3)), Is.False);
            Assert.That(new RectInt(1, 2, 3, 0).Contains(new Vector2Int(2, 2)), Is.False);
        }

        [Test]
        public void RectInt_Overlaps_StrictOnNormalisedEdges()
        {
            RectInt a = new RectInt(0, 0, 10, 10);
            Assert.That(a.Overlaps(new RectInt(5, 5, 10, 10)), Is.True);
            Assert.That(a.Overlaps(new RectInt(2, 2, 1, 1)), Is.True);
            Assert.That(a.Overlaps(new RectInt(10, 0, 5, 5)), Is.False, "touching on x");
            Assert.That(a.Overlaps(new RectInt(0, 10, 5, 5)), Is.False, "touching on y");
            Assert.That(a.Overlaps(new RectInt(-5, 0, 5, 5)), Is.False);
            Assert.That(a.Overlaps(new RectInt(9, 9, 5, 5)), Is.True);
            Assert.That(a.Overlaps(new RectInt(11, 11, 5, 5)), Is.False);
        }

        [Test]
        public void RectInt_Overlaps_InvertedRectsAreNormalisedFirst()
        {
            RectInt a = new RectInt(0, 0, 10, 10);
            RectInt inverted = new RectInt(10, 10, -10, -10);
            Assert.That(a.Overlaps(inverted), Is.True);
            Assert.That(inverted.Overlaps(a), Is.True);
            Assert.That(inverted.Overlaps(new RectInt(5, 5, 2, 2)), Is.True);
            Assert.That(inverted.Overlaps(new RectInt(10, 0, 5, 5)), Is.False, "normalised edges only touch");
        }

        [Test]
        public void RectInt_Overlaps_ZeroSizeFollowsTheAsymmetricStrictFormula()
        {
            // Spec §7: other.xMin < xMax && other.xMax > xMin && other.yMin < yMax && other.yMax > yMin.
            // A zero-size rect has xMin == xMax, so it still "overlaps" when it sits strictly inside:
            // 5 < 10 && 5 > 0 && 5 < 10 && 5 > 0 → true.
            RectInt a = new RectInt(0, 0, 10, 10);
            Assert.That(a.Overlaps(new RectInt(5, 5, 0, 0)), Is.True);
            Assert.That(new RectInt(5, 5, 0, 0).Overlaps(a), Is.True, "symmetric for an interior degenerate rect");
            // On the low corner the second clause fails: other.xMax 0 > xMin 0 is false.
            Assert.That(a.Overlaps(new RectInt(0, 0, 0, 0)), Is.False);
            // On the high corner the first clause fails: other.xMin 10 < xMax 10 is false.
            Assert.That(a.Overlaps(new RectInt(10, 10, 0, 0)), Is.False);
            // A degenerate receiver on its own low corner is false for the same reason.
            Assert.That(new RectInt(0, 0, 0, 0).Overlaps(a), Is.False);
        }

        [Test]
        public void Rect_Overlaps_ZeroSizeFollowsTheAsymmetricStrictFormula()
        {
            // Spec §6: other.xMax > xMin && other.xMin < xMax && other.yMax > yMin && other.yMin < yMax.
            // Same degenerate behaviour as RectInt: 5 > 0 && 5 < 10 (and the same on y) → true.
            Rect a = new Rect(0f, 0f, 10f, 10f);
            Assert.That(a.Overlaps(new Rect(5f, 5f, 0f, 0f)), Is.True);
            Assert.That(new Rect(5f, 5f, 0f, 0f).Overlaps(a), Is.True);
            Assert.That(a.Overlaps(new Rect(0f, 0f, 0f, 0f)), Is.False, "other.xMax 0 > xMin 0 fails");
            Assert.That(a.Overlaps(new Rect(10f, 10f, 0f, 0f)), Is.False, "other.xMin 10 < xMax 10 fails");
        }

        #endregion

        #region RectInt: equality, hashing, ToString

        [Test]
        public void RectInt_Equality_IsExactOnRawFields()
        {
            RectInt a = new RectInt(1, 2, 3, 4);
            Assert.That(a == new RectInt(1, 2, 3, 4), Is.True);
            Assert.That(a != new RectInt(1, 2, 3, 4), Is.False);
            Assert.That(a.Equals(new RectInt(1, 2, 3, 4)), Is.True);
            Assert.That(a.Equals((object)new RectInt(1, 2, 3, 4)), Is.True);
            // Same normalised cells, different raw fields → not equal.
            RectInt inverted = new RectInt(4, 6, -3, -4);
            Assert.That(inverted.min == a.min && inverted.max == a.max, Is.True, "sanity: same normalised edges");
            Assert.That(a == inverted, Is.False);
            Assert.That(a != inverted, Is.True);
            Assert.That(a.Equals(inverted), Is.False);
            Assert.That(a == new RectInt(0, 2, 3, 4), Is.False);
            Assert.That(a == new RectInt(1, 0, 3, 4), Is.False);
            Assert.That(a == new RectInt(1, 2, 0, 4), Is.False);
            Assert.That(a == new RectInt(1, 2, 3, 0), Is.False);
        }

        [Test]
        public void RectInt_EqualsObject_RejectsOtherTypes()
        {
            RectInt a = new RectInt(1, 2, 3, 4);
            Assert.That(a.Equals(null), Is.False);
            Assert.That(a.Equals(new Rect(1f, 2f, 3f, 4f)), Is.False);
            Assert.That(a.Equals(1), Is.False);
        }

        [Test]
        public void RectInt_GetHashCode_FollowsSpecFormula()
        {
            // xh ^ (yh << 4) ^ (yh >> 28) ^ (wh >> 4) ^ (wh << 28) ^ (hh >> 4) ^ (hh << 28)
            // (1,2,3,4): 1 ^ 32 ^ 0 ^ 0 ^ 0x30000000 ^ 0 ^ 0x40000000 = 0x70000021 = 1879048225
            Assert.That(new RectInt(1, 2, 3, 4).GetHashCode(), Is.EqualTo(1879048225));
            // (0,-1,0,0): (-1 << 4) = 0xFFFFFFF0 ; (-1 >> 28) = 0xFFFFFFFF (arithmetic) ; xor = 0x0000000F = 15
            Assert.That(new RectInt(0, -1, 0, 0).GetHashCode(), Is.EqualTo(15));
            // (-7,3,100,-9): 0xFFFFFFF9 ^ 0x30 ^ 0 ^ 6 ^ 0x40000000 ^ 0xFFFFFFFF ^ 0x70000000 = 0x30000030 = 805306416
            Assert.That(new RectInt(-7, 3, 100, -9).GetHashCode(), Is.EqualTo(805306416));
            Assert.That(new RectInt(0, 0, 0, 0).GetHashCode(), Is.EqualTo(0));
            Assert.That(new RectInt(5, 0, 0, 0).GetHashCode(), Is.EqualTo(5), "x is mixed in unshifted");
        }

        [Test]
        public void RectInt_ToString_HasNoDefaultFormat()
        {
            Assert.That(new RectInt(1, 2, 3, 4).ToString(), Is.EqualTo("(x:1, y:2, width:3, height:4)"));
            Assert.That(new RectInt(-1, 0, -30, 1000).ToString(), Is.EqualTo("(x:-1, y:0, width:-30, height:1000)"));
            Assert.That(new RectInt(1, 2, 3, 4).ToString(null), Is.EqualTo("(x:1, y:2, width:3, height:4)"));
            Assert.That(new RectInt(1, 2, 3, 4).ToString(null, null), Is.EqualTo("(x:1, y:2, width:3, height:4)"));
            Assert.That(new RectInt(1, 2, 3, 4).ToString(""), Is.EqualTo("(x:1, y:2, width:3, height:4)"));
        }

        [Test]
        public void RectInt_ToString_HonoursFormatAndProvider()
        {
            Assert.That(new RectInt(1, 2, 3, 4).ToString("D3"), Is.EqualTo("(x:001, y:002, width:003, height:004)"));
            Assert.That(new RectInt(1000, 2, 3, 4).ToString("N0"), Is.EqualTo("(x:1,000, y:2, width:3, height:4)"), "null provider → invariant");
            Assert.That(new RectInt(1000, 2, 3, 4).ToString("N0", German), Is.EqualTo("(x:1.000, y:2, width:3, height:4)"));
            Assert.That(new RectInt(255, 0, 0, 0).ToString("X2"), Is.EqualTo("(x:FF, y:00, width:00, height:00)"));
        }

        [Test]
        public void RectInt_ToString_NullProviderIsInvariantEvenUnderCommaCulture()
        {
            WithGermanCulture(() =>
            {
                Assert.That(new RectInt(1000, -2, 3, 4).ToString("N0"), Is.EqualTo("(x:1,000, y:-2, width:3, height:4)"));
                Assert.That(new RectInt(1000, -2, 3, 4).ToString(), Is.EqualTo("(x:1000, y:-2, width:3, height:4)"));
            });
        }

        #endregion

        #region RectInt: allPositionsWithin

        private static List<Vector2Int> Collect(RectInt r)
        {
            var list = new List<Vector2Int>();
            foreach (Vector2Int p in r.allPositionsWithin)
                list.Add(p);
            return list;
        }

        [Test]
        public void RectInt_AllPositionsWithin_EnumeratesXFastestThenY()
        {
            // (1,2,2,3): x ∈ {1,2}, y ∈ {2,3,4}
            List<Vector2Int> got = Collect(new RectInt(1, 2, 2, 3));
            Assert.That(got, Is.EqualTo(new[]
            {
                new Vector2Int(1, 2), new Vector2Int(2, 2),
                new Vector2Int(1, 3), new Vector2Int(2, 3),
                new Vector2Int(1, 4), new Vector2Int(2, 4),
            }));
        }

        [Test]
        public void RectInt_AllPositionsWithin_SingleCell()
        {
            Assert.That(Collect(new RectInt(-3, 7, 1, 1)), Is.EqualTo(new[] { new Vector2Int(-3, 7) }));
        }

        [Test]
        public void RectInt_AllPositionsWithin_EmptyForZeroOrNegativeNormalisedArea()
        {
            Assert.That(Collect(new RectInt(1, 2, 0, 3)), Is.Empty, "min.x >= max.x");
            Assert.That(Collect(new RectInt(1, 2, 2, 0)), Is.Empty, "min.y >= max.y");
            Assert.That(Collect(RectInt.zero), Is.Empty);
        }

        [Test]
        public void RectInt_AllPositionsWithin_UsesNormalisedMinMaxForInvertedRect()
        {
            // (3,5,-2,-3): min (1,2), max (3,5) → same six cells as (1,2,2,3)
            Assert.That(Collect(new RectInt(3, 5, -2, -3)), Is.EqualTo(Collect(new RectInt(1, 2, 2, 3))));
        }

        [Test]
        public void RectInt_PositionEnumerator_IsAStructThatReturnsItselfFromGetEnumerator()
        {
            Assert.That(typeof(RectInt.PositionEnumerator).IsValueType, Is.True);
            Assert.That(typeof(IEnumerator<Vector2Int>).IsAssignableFrom(typeof(RectInt.PositionEnumerator)), Is.True);
            RectInt.PositionEnumerator e = new RectInt(1, 2, 2, 3).allPositionsWithin;
            Assert.That(e.GetEnumerator(), Is.TypeOf<RectInt.PositionEnumerator>());
            // GetEnumerator returns a copy of itself: same initial Current.
            Assert.That(e.GetEnumerator().Current, Is.EqualTo(e.Current));
        }

        [Test]
        public void RectInt_PositionEnumerator_InitialStateAndMoveNextSequence()
        {
            // _current = min with x-- → (0, 2) before the first MoveNext (spec §7).
            RectInt.PositionEnumerator e = new RectInt.PositionEnumerator(new Vector2Int(1, 2), new Vector2Int(3, 5));
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(0, 2)));
            Assert.That(((IEnumerator)e).Current, Is.EqualTo((object)new Vector2Int(0, 2)));

            Assert.That(e.MoveNext(), Is.True);
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(1, 2)));
            Assert.That(e.MoveNext(), Is.True);
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(2, 2)));
            Assert.That(e.MoveNext(), Is.True);
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(1, 3)), "wraps x to min.x and advances y");
            Assert.That(e.MoveNext(), Is.True);
            Assert.That(e.MoveNext(), Is.True);
            Assert.That(e.MoveNext(), Is.True);
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(2, 4)));
            Assert.That(e.MoveNext(), Is.False, "exhausted");
            Assert.That(e.MoveNext(), Is.False, "stays exhausted (_current.y >= _max.y)");
        }

        [Test]
        public void RectInt_PositionEnumerator_EmptyWidthReturnsFalseImmediately()
        {
            // min.x >= max.x: MoveNext does x++ → x >= max.x → x = min.x → still >= max.x → false
            RectInt.PositionEnumerator e = new RectInt.PositionEnumerator(new Vector2Int(1, 2), new Vector2Int(1, 5));
            Assert.That(e.MoveNext(), Is.False);
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(1, 2)), "x was reset to min.x before bailing out");
        }

        [Test]
        public void RectInt_PositionEnumerator_EmptyHeightReturnsFalseImmediately()
        {
            // _current.y (2) >= _max.y (2) → false without touching x
            RectInt.PositionEnumerator e = new RectInt.PositionEnumerator(new Vector2Int(1, 2), new Vector2Int(3, 2));
            Assert.That(e.MoveNext(), Is.False);
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(0, 2)));
        }

        [Test]
        public void RectInt_PositionEnumerator_ResetRestoresInitialStateAndDisposeIsNoOp()
        {
            RectInt.PositionEnumerator e = new RectInt.PositionEnumerator(new Vector2Int(1, 2), new Vector2Int(3, 5));
            e.MoveNext();
            e.MoveNext();
            e.MoveNext();
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(1, 3)));
            e.Reset();
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(0, 2)));
            Assert.That(e.MoveNext(), Is.True);
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(1, 2)));
            ((IDisposable)e).Dispose();
            Assert.That(e.MoveNext(), Is.True, "Dispose does not change the state");
            Assert.That(e.Current, Is.EqualTo(new Vector2Int(2, 2)));
        }

        #endregion

        // =====================================================================================================
        // §10 Bounds
        // =====================================================================================================

        private static Bounds UnitBox() => new Bounds(Vector3.zero, Vector3.one);

        private static void AssertV3Exact(Vector3 actual, float x, float y, float z, string label = null)
        {
            Assert.That(Bits(actual.x), Is.EqualTo(Bits(x)), (label ?? "v") + ".x");
            Assert.That(Bits(actual.y), Is.EqualTo(Bits(y)), (label ?? "v") + ".y");
            Assert.That(Bits(actual.z), Is.EqualTo(Bits(z)), (label ?? "v") + ".z");
        }

        #region Bounds: shape, constructor, properties

        [Test]
        public void Bounds_IsSerializableSequentialStructWithCenterThenExtents()
        {
            Type t = typeof(Bounds);
            Assert.That(t.IsValueType, Is.True);
            Assert.That(t.IsSerializable, Is.True);
            Assert.That(t.IsLayoutSequential, Is.True);
            Assert.That(typeof(IEquatable<Bounds>).IsAssignableFrom(t), Is.True);
            Assert.That(typeof(IFormattable).IsAssignableFrom(t), Is.True);
            Assert.That(Marshal.SizeOf<Bounds>(), Is.EqualTo(24));
            Assert.That(t.GetFields(BindingFlags.Instance | BindingFlags.Public), Is.Empty);
            Assert.That((int)Marshal.OffsetOf<Bounds>("m_Center"), Is.EqualTo(0));
            Assert.That((int)Marshal.OffsetOf<Bounds>("m_Extents"), Is.EqualTo(12));
        }

        [Test]
        public unsafe void Bounds_ReinterpretsAsSixFloats_CenterThenExtents()
        {
            Bounds b = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f));
            float* p = (float*)&b;
            Assert.That(p[0], Is.EqualTo(1f));
            Assert.That(p[1], Is.EqualTo(2f));
            Assert.That(p[2], Is.EqualTo(3f));
            Assert.That(p[3], Is.EqualTo(1f));
            Assert.That(p[4], Is.EqualTo(2f));
            Assert.That(p[5], Is.EqualTo(3f));
        }

        [Test]
        public void Bounds_Constructor_HalvesSizeIntoExtents()
        {
            Bounds b = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f));
            AssertV3Exact(b.center, 1f, 2f, 3f, "center");
            AssertV3Exact(b.extents, 1f, 2f, 3f, "extents");
            AssertV3Exact(b.size, 2f, 4f, 6f, "size");
        }

        [Test]
        public void Bounds_Constructor_NegativeSizeGivesNegativeExtents()
        {
            Bounds b = new Bounds(Vector3.zero, new Vector3(-1f, 2f, -3f));
            AssertV3Exact(b.extents, -0.5f, 1f, -1.5f, "extents");
        }

        [Test]
        public void Bounds_Constructor_UsesHalfMultiplyNotDivide()
        {
            // size.x * 0.5F is exact for any finite float; check an odd value and a denormal-ish one bit-exactly.
            float s = 0.3f;
            Bounds b = new Bounds(Vector3.zero, new Vector3(s, s, s));
            Assert.That(Bits(b.extents.x), Is.EqualTo(Bits(s * 0.5f)));
            Assert.That(Bits(b.size.x), Is.EqualTo(Bits((s * 0.5f) * 2.0f)));
        }

        [Test]
        public void Bounds_CenterAndExtents_SettersAreRaw()
        {
            Bounds b = UnitBox();
            b.center = new Vector3(5f, 6f, 7f);
            b.extents = new Vector3(-1f, 2f, 3f);
            AssertV3Exact(b.center, 5f, 6f, 7f, "center");
            AssertV3Exact(b.extents, -1f, 2f, 3f, "extents");
            AssertV3Exact(b.size, -2f, 4f, 6f, "size");
        }

        [Test]
        public void Bounds_SizeSetter_HalvesIntoExtentsAndKeepsCenter()
        {
            Bounds b = new Bounds(new Vector3(1f, 2f, 3f), Vector3.one);
            b.size = new Vector3(10f, 20f, 30f);
            AssertV3Exact(b.extents, 5f, 10f, 15f, "extents");
            AssertV3Exact(b.center, 1f, 2f, 3f, "center");
        }

        [Test]
        public void Bounds_MinMaxGetters_AreCenterMinusPlusExtents()
        {
            Bounds b = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f));
            AssertV3Exact(b.min, 0f, 0f, 0f, "min");
            AssertV3Exact(b.max, 2f, 4f, 6f, "max");
            // Negative extents: min > max, nothing normalises.
            Bounds n = new Bounds(Vector3.zero, new Vector3(-1f, 1f, 1f));
            Assert.That(n.min.x, Is.EqualTo(0.5f));
            Assert.That(n.max.x, Is.EqualTo(-0.5f));
        }

        [Test]
        public void Bounds_MinSetter_KeepsMaxAndRederivesCenterAndExtents()
        {
            // unit box: max = 0.5 ; new min = -2 → extents = (0.5 - (-2)) * 0.5 = 1.25 ; center = -2 + 1.25 = -0.75
            Bounds b = UnitBox();
            b.min = new Vector3(-2f, -2f, -2f);
            AssertV3Exact(b.extents, 1.25f, 1.25f, 1.25f, "extents");
            AssertV3Exact(b.center, -0.75f, -0.75f, -0.75f, "center");
            AssertV3Exact(b.max, 0.5f, 0.5f, 0.5f, "max");
        }

        [Test]
        public void Bounds_MaxSetter_KeepsMinAndRederivesCenterAndExtents()
        {
            // unit box: min = -0.5 ; new max = 2 → extents = (2 - (-0.5)) * 0.5 = 1.25 ; center = -0.5 + 1.25 = 0.75
            Bounds b = UnitBox();
            b.max = new Vector3(2f, 2f, 2f);
            AssertV3Exact(b.extents, 1.25f, 1.25f, 1.25f, "extents");
            AssertV3Exact(b.center, 0.75f, 0.75f, 0.75f, "center");
            AssertV3Exact(b.min, -0.5f, -0.5f, -0.5f, "min");
        }

        [Test]
        public void Bounds_SetMinMax_DerivesExtentsFirstThenCenter()
        {
            // extents = (0.3 - 0.1) * 0.5 (float) = 0.10000001 [0x3DCCCCCE] ; center = 0.1 + extents = 0.20000002 [0x3E4CCCCE]
            Bounds b = UnitBox();
            b.SetMinMax(new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0.3f, 0.3f, 0.3f));
            Assert.That(Bits(b.extents.x), Is.EqualTo(0x3DCCCCCE));
            Assert.That(Bits(b.center.x), Is.EqualTo(0x3E4CCCCE));
            // Sanity: the naive (min + max) * 0.5 midpoint would be a different float, so the order is observable.
            Assert.That(Bits((0.1f + 0.3f) * 0.5f), Is.Not.EqualTo(0x3E4CCCCE));
        }

        [Test]
        public void Bounds_SetMinMax_InvertedInputGivesNegativeExtents()
        {
            // extents = (0 - 4) * 0.5 = -2 ; center = 4 + (-2) = 2
            Bounds b = UnitBox();
            b.SetMinMax(new Vector3(4f, 4f, 4f), Vector3.zero);
            AssertV3Exact(b.extents, -2f, -2f, -2f, "extents");
            AssertV3Exact(b.center, 2f, 2f, 2f, "center");
        }

        #endregion

        #region Bounds: Encapsulate / Expand

        [Test]
        public void Bounds_EncapsulatePoint_VerifiedValue()
        {
            // [verified] unit box + (3,0,0) → center (1.25,0,0), extents (1.75,0.5,0.5)
            Bounds b = UnitBox();
            b.Encapsulate(new Vector3(3f, 0f, 0f));
            AssertV3Exact(b.center, 1.25f, 0f, 0f, "center");
            AssertV3Exact(b.extents, 1.75f, 0.5f, 0.5f, "extents");
        }

        [Test]
        public void Bounds_EncapsulatePoint_InsidePointLeavesBoxUnchanged()
        {
            Bounds b = UnitBox();
            b.Encapsulate(new Vector3(0.25f, -0.25f, 0.5f));
            AssertV3Exact(b.center, 0f, 0f, 0f, "center");
            AssertV3Exact(b.extents, 0.5f, 0.5f, 0.5f, "extents");
        }

        [Test]
        public void Bounds_EncapsulatePoint_GrowsTowardsNegativeSide()
        {
            // min = Min((-0.5,-0.5,-0.5), (0,-3,0)) = (-0.5,-3,-0.5) ; max = (0.5,0.5,0.5)
            // extents.y = (0.5 - (-3)) * 0.5 = 1.75 ; center.y = -3 + 1.75 = -1.25
            Bounds b = UnitBox();
            b.Encapsulate(new Vector3(0f, -3f, 0f));
            AssertV3Exact(b.center, 0f, -1.25f, 0f, "center");
            AssertV3Exact(b.extents, 0.5f, 1.75f, 0.5f, "extents");
        }

        [Test]
        public void Bounds_EncapsulateBounds_UsesOtherMinThenMax()
        {
            // other: center 5, size 2 → min 4, max 6. Result min = -0.5, max = 6 → extents = 3.25, center = -0.5 + 3.25 = 2.75
            Bounds b = UnitBox();
            b.Encapsulate(new Bounds(new Vector3(5f, 5f, 5f), new Vector3(2f, 2f, 2f)));
            AssertV3Exact(b.extents, 3.25f, 3.25f, 3.25f, "extents");
            AssertV3Exact(b.center, 2.75f, 2.75f, 2.75f, "center");
        }

        [Test]
        public void Bounds_EncapsulateBounds_ContainedBoxLeavesBoxUnchanged()
        {
            Bounds b = UnitBox();
            b.Encapsulate(new Bounds(Vector3.zero, new Vector3(0.5f, 0.5f, 0.5f)));
            AssertV3Exact(b.center, 0f, 0f, 0f, "center");
            AssertV3Exact(b.extents, 0.5f, 0.5f, 0.5f, "extents");
        }

        [Test]
        public void Bounds_ExpandFloat_AddsHalfAmountToEveryExtent()
        {
            // 0.5 + 1*0.5 = 1.0
            Bounds b = UnitBox();
            b.Expand(1f);
            AssertV3Exact(b.extents, 1f, 1f, 1f, "extents");
            AssertV3Exact(b.center, 0f, 0f, 0f, "center");
        }

        [Test]
        public void Bounds_ExpandFloat_NegativeAmountCanMakeExtentsNegative()
        {
            // 0.5 + (-3)*0.5 = -1
            Bounds b = UnitBox();
            b.Expand(-3f);
            AssertV3Exact(b.extents, -1f, -1f, -1f, "extents");
        }

        [Test]
        public void Bounds_ExpandVector_AddsHalfPerAxis()
        {
            // (0.5 + 1, 0.5 + 2, 0.5 + 3) = (1.5, 2.5, 3.5)
            Bounds b = UnitBox();
            b.Expand(new Vector3(2f, 4f, 6f));
            AssertV3Exact(b.extents, 1.5f, 2.5f, 3.5f, "extents");
            AssertV3Exact(b.size, 3f, 5f, 7f, "size");
        }

        #endregion

        #region Bounds: Intersects / Contains / distances

        [Test]
        public void Bounds_Intersects_InclusiveOnTouchingFaces()
        {
            Bounds a = UnitBox();
            Assert.That(a.Intersects(new Bounds(new Vector3(0.5f, 0f, 0f), Vector3.one)), Is.True, "overlapping");
            Assert.That(a.Intersects(new Bounds(new Vector3(1f, 0f, 0f), Vector3.one)), Is.True, "faces touch at x = 0.5 (inclusive)");
            Assert.That(a.Intersects(new Bounds(new Vector3(1f, 1f, 1f), Vector3.one)), Is.True, "corners touch");
            Assert.That(a.Intersects(new Bounds(new Vector3(1.0001f, 0f, 0f), Vector3.one)), Is.False);
            Assert.That(a.Intersects(new Bounds(new Vector3(0f, 0f, -2f), Vector3.one)), Is.False);
            Assert.That(a.Intersects(new Bounds(Vector3.zero, new Vector3(10f, 10f, 10f))), Is.True, "containing box");
        }

        [Test]
        public void Bounds_Intersects_NegativeExtentBoxStillIntersects()
        {
            // [verified] negative-x-extent box (min.x = 0.5, max.x = -0.5) vs unit box:
            // 0.5 <= 0.5 && -0.5 >= -0.5 → true on x; y/z as unit box.
            Bounds neg = new Bounds(Vector3.zero, new Vector3(-1f, 1f, 1f));
            Assert.That(neg.Intersects(UnitBox()), Is.True);
            Assert.That(UnitBox().Intersects(neg), Is.True);
        }

        [Test]
        public void Bounds_Intersects_NaNIsFalse()
        {
            Assert.That(UnitBox().Intersects(new Bounds(new Vector3(float.NaN, 0f, 0f), Vector3.one)), Is.False);
        }

        [Test]
        public void Bounds_Contains_VerifiedFaceBoundary()
        {
            // [verified] (0.5,0,0) true ; (0.5000001,0,0) false
            Bounds b = UnitBox();
            Assert.That(b.Contains(new Vector3(0.5f, 0f, 0f)), Is.True);
            Assert.That(b.Contains(new Vector3(0.5000001f, 0f, 0f)), Is.False);
            Assert.That(b.Contains(new Vector3(-0.5f, 0f, 0f)), Is.True);
            Assert.That(b.Contains(new Vector3(-0.5000001f, 0f, 0f)), Is.False);
        }

        [Test]
        public void Bounds_Contains_InclusiveOnEdgesAndCorners()
        {
            Bounds b = UnitBox();
            Assert.That(b.Contains(Vector3.zero), Is.True);
            Assert.That(b.Contains(new Vector3(0.5f, 0.5f, 0f)), Is.True, "edge");
            Assert.That(b.Contains(new Vector3(0.5f, 0.5f, 0.5f)), Is.True, "corner");
            Assert.That(b.Contains(new Vector3(-0.5f, -0.5f, -0.5f)), Is.True, "min corner");
            Assert.That(b.Contains(new Vector3(0.5f, 0.5f, 0.50001f)), Is.False);
            Assert.That(b.Contains(new Vector3(0f, 0.6f, 0f)), Is.False);
            Assert.That(b.Contains(new Vector3(0f, 0f, -0.6f)), Is.False);
        }

        [Test]
        public void Bounds_Contains_ZeroExtentsContainsOnlyTheCenter()
        {
            Bounds b = new Bounds(new Vector3(1f, 2f, 3f), Vector3.zero);
            Assert.That(b.Contains(new Vector3(1f, 2f, 3f)), Is.True);
            Assert.That(b.Contains(new Vector3(1f, 2f, 3.0001f)), Is.False);
        }

        [Test]
        public void Bounds_Contains_AnyNegativeExtentIsAlwaysFalse()
        {
            // [verified] negative-extent box contains nothing, not even its own centre.
            Bounds x = new Bounds(Vector3.zero, new Vector3(-1f, 1f, 1f));
            Assert.That(x.Contains(Vector3.zero), Is.False);
            Assert.That(x.Contains(new Vector3(0.25f, 0f, 0f)), Is.False);
            Bounds y = new Bounds(Vector3.zero, new Vector3(1f, -1f, 1f));
            Assert.That(y.Contains(Vector3.zero), Is.False);
            Bounds z = new Bounds(Vector3.zero, new Vector3(1f, 1f, -1f));
            Assert.That(z.Contains(Vector3.zero), Is.False);
        }

        [Test]
        public void Bounds_Contains_NaNCoordinateIsTrue()
        {
            // [verified] (NaN,0,0) → true: the test is "reject if p < min or p > max", which NaN never triggers.
            Bounds b = UnitBox();
            Assert.That(b.Contains(new Vector3(float.NaN, 0f, 0f)), Is.True);
            // Same rule applied to the other components (spec §15 lists these as not separately verified).
            Assert.That(b.Contains(new Vector3(0f, float.NaN, 0f)), Is.True);
            Assert.That(b.Contains(new Vector3(0f, 0f, float.NaN)), Is.True);
            Assert.That(b.Contains(new Vector3(float.NaN, 5f, 0f)), Is.False, "the non-NaN component still rejects");
        }

        [Test]
        public void Bounds_SqrDistance_VerifiedValues()
        {
            // [verified] (2,0,0) → 2.25 ; (2,2,2) → 6.75 (unit box)
            Bounds b = UnitBox();
            Assert.That(b.SqrDistance(new Vector3(2f, 0f, 0f)), Is.EqualTo(2.25f));
            Assert.That(b.SqrDistance(new Vector3(2f, 2f, 2f)), Is.EqualTo(6.75f));
        }

        [Test]
        public void Bounds_SqrDistance_ZeroInsideAndOnFaces()
        {
            Bounds b = UnitBox();
            Assert.That(b.SqrDistance(Vector3.zero), Is.EqualTo(0f));
            Assert.That(b.SqrDistance(new Vector3(0.5f, 0.5f, 0.5f)), Is.EqualTo(0f));
            Assert.That(b.SqrDistance(new Vector3(0.25f, -0.25f, 0.1f)), Is.EqualTo(0f));
            // (-3, 0, 0): (-0.5 - (-3))² = 6.25
            Assert.That(b.SqrDistance(new Vector3(-3f, 0f, 0f)), Is.EqualTo(6.25f));
            // Off-centre box: center (1,2,3), extents (1,1,1); point (5,2,3) → (5 - 2)² = 9
            Assert.That(new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 2f, 2f)).SqrDistance(new Vector3(5f, 2f, 3f)), Is.EqualTo(9f));
        }

        [Test]
        public void Bounds_ClosestPoint_ClampsPerComponent()
        {
            Bounds b = UnitBox();
            AssertV3Exact(b.ClosestPoint(new Vector3(2f, 0f, 0f)), 0.5f, 0f, 0f, "outside +x");
            AssertV3Exact(b.ClosestPoint(new Vector3(-2f, 3f, 0.25f)), -0.5f, 0.5f, 0.25f, "mixed");
            AssertV3Exact(b.ClosestPoint(new Vector3(0.1f, -0.2f, 0.3f)), 0.1f, -0.2f, 0.3f, "inside is unchanged");
            AssertV3Exact(b.ClosestPoint(new Vector3(0.5f, 0.5f, 0.5f)), 0.5f, 0.5f, 0.5f, "corner is unchanged");
        }

        [Test]
        public void Bounds_ClosestPoint_NegativeExtentsYieldMin()
        {
            // [verified] negative-x box (min.x = 0.5 > max.x = -0.5): max(0.5, min(-0.5, 0.25)) = 0.5
            Bounds neg = new Bounds(Vector3.zero, new Vector3(-1f, 1f, 1f));
            AssertV3Exact(neg.ClosestPoint(new Vector3(0.25f, 0f, 0f)), 0.5f, 0f, 0f, "closest");
            AssertV3Exact(neg.ClosestPoint(new Vector3(-10f, 0f, 0f)), 0.5f, 0f, 0f, "closest");
            AssertV3Exact(neg.ClosestPoint(new Vector3(10f, 0f, 0f)), 0.5f, 0f, 0f, "closest");
        }

        #endregion

        #region Bounds: IntersectRay (slab test; public Ray overloads plus the origin/direction implementation)

        [Test]
        public void Bounds_IntersectRay_HitFromOutsideGivesEntryDistance()
        {
            // origin (-5,0,0) → +x: entry at x = -0.5 → t = 4.5
            Assert.That(UnitBox().IntersectRay(new Vector3(-5f, 0f, 0f), Vector3.right, out float d), Is.True);
            Assert.That(d, Is.EqualTo(4.5f));
        }

        [Test]
        public void Bounds_IntersectRay_OriginInsideGivesNegativeDistance()
        {
            // [verified] from the centre heading +x the "entry" parameter is -0.5
            Assert.That(UnitBox().IntersectRay(Vector3.zero, Vector3.right, out float d), Is.True);
            Assert.That(d, Is.EqualTo(-0.5f));
        }

        [Test]
        public void Bounds_IntersectRay_MissGivesFalseAndZero()
        {
            Assert.That(UnitBox().IntersectRay(new Vector3(-5f, 2f, 0f), Vector3.right, out float d), Is.False);
            Assert.That(d, Is.EqualTo(0f));
            // box entirely behind the origin
            Assert.That(UnitBox().IntersectRay(new Vector3(5f, 0f, 0f), Vector3.right, out float d2), Is.False);
            Assert.That(d2, Is.EqualTo(0f));
        }

        [Test]
        public void Bounds_IntersectRay_GrazingAnEdgeCountsAsHit()
        {
            // travels along the y = 0.5, z = 0.5 edge
            Assert.That(UnitBox().IntersectRay(new Vector3(-5f, 0.5f, 0.5f), Vector3.right, out float d), Is.True);
            Assert.That(d, Is.EqualTo(4.5f));
        }

        [Test]
        public void Bounds_IntersectRay_DiagonalHit()
        {
            // origin (-2,-2,-2) direction normalised (1,1,1)/√3: entry at the (-0.5,-0.5,-0.5) corner, t = 1.5 * √3
            Vector3 dir = new Vector3(1f, 1f, 1f).normalized;
            Assert.That(UnitBox().IntersectRay(new Vector3(-2f, -2f, -2f), dir, out float d), Is.True);
            Assert.That(d, Is.EqualTo(1.5f * MathF.Sqrt(3f)).Within(4).Ulps);
        }

        [Test]
        public void Bounds_IntersectRay_PublicRayOverloadsExistAndForward()
        {
            // Spec §10 lists IntersectRay(Ray) and IntersectRay(Ray, out float) as public members of Bounds, so both
            // are part of the surface; they forward to the origin/direction slab test with Ray's normalised direction.
            Bounds box = UnitBox();
            var ray = new Ray(new Vector3(-5f, 0f, 0f), new Vector3(3f, 0f, 0f));   // direction normalises to +x
            Assert.That(box.IntersectRay(ray), Is.True);
            Assert.That(box.IntersectRay(ray, out float d), Is.True);
            Assert.That(d, Is.EqualTo(4.5f), "distance is a world distance because Ray normalises its direction");

            // Inside → negative entry parameter; a miss → false with distance 0.
            Assert.That(box.IntersectRay(new Ray(Vector3.zero, Vector3.right), out float inside), Is.True);
            Assert.That(inside, Is.EqualTo(-0.5f));
            Assert.That(box.IntersectRay(new Ray(new Vector3(-5f, 2f, 0f), Vector3.right), out float miss), Is.False);
            Assert.That(miss, Is.EqualTo(0f));

            // Both overloads are public instance methods (a golden/API dump compares this surface).
            Assert.That(typeof(Bounds).GetMethod("IntersectRay", new[] { typeof(Ray) }), Is.Not.Null);
            Assert.That(typeof(Bounds).GetMethod("IntersectRay", new[] { typeof(Ray), typeof(float).MakeByRefType() }), Is.Not.Null);
        }

        [Test]
        public void Ray_NormalisesDirectionAndGetsPoints()
        {
            // The shim's Ray exists to carry the two Bounds.IntersectRay overloads (spec §10 references it but has no
            // Ray section, so this is documented behaviour only): the constructor and the setter both normalise.
            var ray = new Ray(new Vector3(1f, 2f, 3f), new Vector3(0f, 0f, 5f));
            AssertV3Exact(ray.origin, 1f, 2f, 3f, "origin");
            AssertV3Exact(ray.direction, 0f, 0f, 1f, "direction");
            AssertV3Exact(ray.GetPoint(2f), 1f, 2f, 5f, "GetPoint");
            AssertV3Exact(ray.GetPoint(0f), 1f, 2f, 3f, "GetPoint(0)");
            AssertV3Exact(ray.GetPoint(-1f), 1f, 2f, 2f, "GetPoint(-1)");

            ray.direction = new Vector3(0f, -4f, 0f);
            AssertV3Exact(ray.direction, 0f, -1f, 0f, "direction setter normalises");
            ray.origin = Vector3.zero;
            AssertV3Exact(ray.origin, 0f, 0f, 0f, "origin setter is raw");

            // A zero direction stays zero (Vector3.Normalize's kEpsilon branch), it does not become NaN.
            AssertV3Exact(new Ray(Vector3.zero, Vector3.zero).direction, 0f, 0f, 0f, "zero direction");

            Assert.That(new Ray(new Vector3(1f, 2f, 3f), Vector3.forward).ToString(),
                Is.EqualTo("Origin: (1.00, 2.00, 3.00), Dir: (0.00, 0.00, 1.00)"));
            Assert.That(typeof(Ray).Namespace, Is.EqualTo("UnityEngine"));
            Assert.That(typeof(Ray).IsValueType, Is.True);
        }

        #endregion

        #region Bounds: equality, hashing, ToString

        [Test]
        public void Bounds_OperatorEquals_UsesVector3Tolerance()
        {
            // [verified] 1e-6 centre difference → equal (sqr diff 1e-12 < 1e-10)
            Bounds a = UnitBox();
            Bounds nearCenter = new Bounds(new Vector3(1e-6f, 0f, 0f), Vector3.one);
            Assert.That(a == nearCenter, Is.True);
            Assert.That(a != nearCenter, Is.False);
            // extents also compared with the tolerance
            Bounds nearExtents = new Bounds(Vector3.zero, new Vector3(1f + 2e-6f, 1f, 1f));
            Assert.That(a == nearExtents, Is.True);
            // 1e-3 difference: sqr diff 1e-6 ≥ 1e-10 → not equal
            Assert.That(a == new Bounds(new Vector3(1e-3f, 0f, 0f), Vector3.one), Is.False);
            Assert.That(a != new Bounds(new Vector3(1e-3f, 0f, 0f), Vector3.one), Is.True);
            Assert.That(a == new Bounds(Vector3.zero, new Vector3(1.001f, 1f, 1f)), Is.False);
        }

        [Test]
        public void Bounds_OperatorEquals_NaNIsFalseAndNotEqualIsTrue()
        {
            Bounds n = new Bounds(new Vector3(float.NaN, 0f, 0f), Vector3.one);
#pragma warning disable CS1718
            Assert.That(n == n, Is.False);
            Assert.That(n != n, Is.True);
#pragma warning restore CS1718
            Bounds e = new Bounds(Vector3.zero, new Vector3(float.NaN, 1f, 1f));
            Assert.That(e == UnitBox(), Is.False);
        }

        [Test]
        public void Bounds_Equals_IsExactViaVector3Equals()
        {
            Bounds a = UnitBox();
            Assert.That(a.Equals(UnitBox()), Is.True);
            Assert.That(a.Equals((object)UnitBox()), Is.True);
            Bounds near = new Bounds(new Vector3(1e-6f, 0f, 0f), Vector3.one);
            Assert.That(a == near, Is.True, "sanity: tolerant ==");
            Assert.That(a.Equals(near), Is.False, "Equals is exact");
            Assert.That(a.Equals(new Bounds(Vector3.zero, new Vector3(1f, 1f, 1.0000001f))), Is.False);
        }

        [Test]
        public void Bounds_Equals_NaNIsNotEqualToItself()
        {
            // Vector3.Equals uses C# == on floats (spec §0), so unlike Rect a NaN Bounds is not Equals to itself.
            Bounds n = new Bounds(new Vector3(float.NaN, 0f, 0f), Vector3.one);
            Assert.That(n.Equals(n), Is.False);
            Bounds e = new Bounds(Vector3.zero, new Vector3(float.NaN, 1f, 1f));
            Assert.That(e.Equals(e), Is.False);
        }

        [Test]
        public void Bounds_EqualsObject_RejectsOtherTypes()
        {
            Bounds a = UnitBox();
            Assert.That(a.Equals(null), Is.False);
            Assert.That(a.Equals(Vector3.zero), Is.False);
            Assert.That(a.Equals(new Rect(0f, 0f, 1f, 1f)), Is.False);
        }

        [Test]
        public void Bounds_GetHashCode_IsCenterHashXorExtentsHashShifted()
        {
            // center (1,2,3) hashes to 797966336 [verified for Vector3]; extents (0,0,0) hash 0 → 797966336
            Assert.That(new Bounds(new Vector3(1f, 2f, 3f), Vector3.zero).GetHashCode(), Is.EqualTo(797966336));
            // center 0 (hash 0), extents (1,2,3): 797966336 << 2 = 0x2F900000 << 2 = 0xBE400000 = -1103101952
            Assert.That(new Bounds(Vector3.zero, new Vector3(2f, 4f, 6f)).GetHashCode(), Is.EqualTo(-1103101952));
            // center (1,2,3), extents (0.5,0.5,0.5): V3(0.5,0.5,0.5) = 0x3F000000 ^ (0x3F000000 << 2 = 0xFC000000) ^ (0x3F000000 >> 2 = 0x0FC00000)
            //   = 0xC3000000 ^ 0x0FC00000 = 0xCCC00000 ; << 2 = 0x33000000 ; 0x2F900000 ^ 0x33000000 = 0x1C900000 = 479199232
            Assert.That(new Bounds(new Vector3(1f, 2f, 3f), Vector3.one).GetHashCode(), Is.EqualTo(479199232));
            Assert.That(new Bounds(Vector3.zero, Vector3.zero).GetHashCode(), Is.EqualTo(0));
        }

        [Test]
        public void Bounds_GetHashCode_MatchesComponentFormula()
        {
            Bounds b = new Bounds(new Vector3(1.5f, -2.25f, 1e-3f), new Vector3(7f, 0.125f, -3f));
            int expected = b.center.GetHashCode() ^ (b.extents.GetHashCode() << 2);
            Assert.That(b.GetHashCode(), Is.EqualTo(expected));
        }

        [Test]
        public void Bounds_ToString_DefaultIsF2ViaVector3()
        {
            Bounds b = new Bounds(Vector3.one, new Vector3(2f, 2f, 2f));
            Assert.That(b.ToString(), Is.EqualTo("Center: (1.00, 1.00, 1.00), Extents: (1.00, 1.00, 1.00)"));
            Assert.That(UnitBox().ToString(), Is.EqualTo("Center: (0.00, 0.00, 0.00), Extents: (0.50, 0.50, 0.50)"));
            Assert.That(b.ToString(null), Is.EqualTo(b.ToString()));
            Assert.That(b.ToString(""), Is.EqualTo(b.ToString()));
            Assert.That(b.ToString(null, null), Is.EqualTo(b.ToString()));
        }

        [Test]
        public void Bounds_ToString_HonoursFormatAndProvider()
        {
            Bounds b = new Bounds(new Vector3(1.5f, -2f, 3f), new Vector3(1f, 1f, 1f));
            Assert.That(b.ToString("F1"), Is.EqualTo("Center: (1.5, -2.0, 3.0), Extents: (0.5, 0.5, 0.5)"));
            Assert.That(b.ToString("F0"), Is.EqualTo("Center: (2, -2, 3), Extents: (0, 0, 0)"), "F0 rounds half to even (1.5 → 2, 0.5 → 0)");
            Assert.That(b.ToString("F1", German), Is.EqualTo("Center: (1,5, -2,0, 3,0), Extents: (0,5, 0,5, 0,5)"));
            Assert.That(b.ToString(null, German), Is.EqualTo("Center: (1,50, -2,00, 3,00), Extents: (0,50, 0,50, 0,50)"));
        }

        [Test]
        public void Bounds_ToString_NullProviderIsInvariantEvenUnderCommaCulture()
        {
            WithGermanCulture(() =>
            {
                Bounds b = new Bounds(new Vector3(1.5f, -2f, 3f), Vector3.one);
                Assert.That(b.ToString(), Is.EqualTo("Center: (1.50, -2.00, 3.00), Extents: (0.50, 0.50, 0.50)"));
                Assert.That(b.ToString("F1", null), Is.EqualTo("Center: (1.5, -2.0, 3.0), Extents: (0.5, 0.5, 0.5)"));
            });
        }

        #endregion
    }
}
