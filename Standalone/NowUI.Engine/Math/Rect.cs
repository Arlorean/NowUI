// Mirrors UnityEngine.Rect for the NowUI standalone build; spec: Docs/Standalone/UnityValueTypeSemantics.md (§6).
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// Float rectangle stored as (xMin, yMin, width, height). Width/height may be negative; nothing normalises them
    /// (spec §6). Only these four private floats exist so arrays of Rect can be reinterpreted as float*.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Rect : IEquatable<Rect>, IFormattable
    {
        private float m_XMin;
        private float m_YMin;
        private float m_Width;
        private float m_Height;

        public Rect(float x, float y, float width, float height)
        {
            m_XMin = x;
            m_YMin = y;
            m_Width = width;
            m_Height = height;
        }

        public Rect(Vector2 position, Vector2 size)
        {
            m_XMin = position.x;
            m_YMin = position.y;
            m_Width = size.x;
            m_Height = size.y;
        }

        public Rect(Rect source)
        {
            m_XMin = source.m_XMin;
            m_YMin = source.m_YMin;
            m_Width = source.m_Width;
            m_Height = source.m_Height;
        }

        // ---- statics (spec §6 "Static") ----

        private static readonly Rect zeroRect = new Rect(0f, 0f, 0f, 0f);

        public static Rect zero => zeroRect;

        public static Rect MinMaxRect(float xmin, float ymin, float xmax, float ymax)
        {
            return new Rect(xmin, ymin, xmax - xmin, ymax - ymin);
        }

        /// <summary>Clamped lerp between xMin..xMin+width / yMin..yMin+height (spec §6).</summary>
        public static Vector2 NormalizedToPoint(Rect rectangle, Vector2 normalizedRectCoordinates)
        {
            return new Vector2(
                Mathf.Lerp(rectangle.m_XMin, rectangle.m_XMin + rectangle.m_Width, normalizedRectCoordinates.x),
                Mathf.Lerp(rectangle.m_YMin, rectangle.m_YMin + rectangle.m_Height, normalizedRectCoordinates.y));
        }

        /// <summary>Clamped inverse lerp; Mathf.InverseLerp yields 0 when the edge pair is equal (spec §6, §11).</summary>
        public static Vector2 PointToNormalized(Rect rectangle, Vector2 point)
        {
            return new Vector2(
                Mathf.InverseLerp(rectangle.m_XMin, rectangle.m_XMin + rectangle.m_Width, point.x),
                Mathf.InverseLerp(rectangle.m_YMin, rectangle.m_YMin + rectangle.m_Height, point.y));
        }

        // ---- properties (spec §6 "Properties (exact setter semantics)") ----

        /// <summary>Left edge; the setter moves the rect and keeps the width.</summary>
        public float x
        {
            readonly get => m_XMin;
            set => m_XMin = value;
        }

        /// <summary>Bottom/top edge; the setter moves the rect and keeps the height.</summary>
        public float y
        {
            readonly get => m_YMin;
            set => m_YMin = value;
        }

        public float width
        {
            readonly get => m_Width;
            set => m_Width = value;
        }

        public float height
        {
            readonly get => m_Height;
            set => m_Height = value;
        }

        public Vector2 position
        {
            readonly get => new Vector2(m_XMin, m_YMin);
            set
            {
                m_XMin = value.x;
                m_YMin = value.y;
            }
        }

        public Vector2 size
        {
            readonly get => new Vector2(m_Width, m_Height);
            set
            {
                m_Width = value.x;
                m_Height = value.y;
            }
        }

        public Vector2 center
        {
            readonly get => new Vector2(m_XMin + m_Width * 0.5f, m_YMin + m_Height * 0.5f);
            set
            {
                m_XMin = value.x - m_Width * 0.5f;
                m_YMin = value.y - m_Height * 0.5f;
            }
        }

        /// <summary>Setter routes through xMin/yMin, so the size changes (right/top edges stay fixed).</summary>
        public Vector2 min
        {
            readonly get => new Vector2(m_XMin, m_YMin);
            set
            {
                xMin = value.x;
                yMin = value.y;
            }
        }

        /// <summary>Setter routes through xMax/yMax, so the size changes (left/bottom edges stay fixed).</summary>
        public Vector2 max
        {
            readonly get => new Vector2(m_XMin + m_Width, m_YMin + m_Height);
            set
            {
                xMax = value.x;
                yMax = value.y;
            }
        }

        /// <summary>Setter keeps the right edge fixed: width = oldXMax - newXMin.</summary>
        public float xMin
        {
            readonly get => m_XMin;
            set
            {
                float oldXMax = m_XMin + m_Width;
                m_XMin = value;
                m_Width = oldXMax - m_XMin;
            }
        }

        /// <summary>Setter keeps the top edge fixed: height = oldYMax - newYMin.</summary>
        public float yMin
        {
            readonly get => m_YMin;
            set
            {
                float oldYMax = m_YMin + m_Height;
                m_YMin = value;
                m_Height = oldYMax - m_YMin;
            }
        }

        public float xMax
        {
            readonly get => m_XMin + m_Width;
            set => m_Width = value - m_XMin;
        }

        public float yMax
        {
            readonly get => m_YMin + m_Height;
            set => m_Height = value - m_YMin;
        }

        [Obsolete("use xMin")]
        public readonly float left => m_XMin;

        [Obsolete("use xMax")]
        public readonly float right => m_XMin + m_Width;

        [Obsolete("use yMin")]
        public readonly float top => m_YMin;

        [Obsolete("use yMax")]
        public readonly float bottom => m_YMin + m_Height;

        // ---- methods (spec §6 "Methods") ----

        public void Set(float x, float y, float width, float height)
        {
            m_XMin = x;
            m_YMin = y;
            m_Width = width;
            m_Height = height;
        }

        /// <summary>Min inclusive, max exclusive; always false for a negative-size rect (spec §6).</summary>
        public readonly bool Contains(Vector2 point)
        {
            return point.x >= m_XMin && point.x < m_XMin + m_Width && point.y >= m_YMin && point.y < m_YMin + m_Height;
        }

        /// <summary>Uses x and y only; z is ignored.</summary>
        public readonly bool Contains(Vector3 point)
        {
            return point.x >= m_XMin && point.x < m_XMin + m_Width && point.y >= m_YMin && point.y < m_YMin + m_Height;
        }

        /// <summary>With allowInverse an inverted axis becomes max-inclusive / min-exclusive (spec §6).</summary>
        public readonly bool Contains(Vector3 point, bool allowInverse)
        {
            if (!allowInverse)
                return Contains(point);

            float xmax = m_XMin + m_Width;
            float ymax = m_YMin + m_Height;
            bool xAxis = (m_Width < 0f && point.x <= m_XMin && point.x > xmax) ||
                         (m_Width >= 0f && point.x >= m_XMin && point.x < xmax);
            bool yAxis = (m_Height < 0f && point.y <= m_YMin && point.y > ymax) ||
                         (m_Height >= 0f && point.y >= m_YMin && point.y < ymax);
            return xAxis && yAxis;
        }

        /// <summary>Strict comparisons: touching edges do not overlap (spec §6).</summary>
        public readonly bool Overlaps(Rect other)
        {
            return other.m_XMin + other.m_Width > m_XMin &&
                   other.m_XMin < m_XMin + m_Width &&
                   other.m_YMin + other.m_Height > m_YMin &&
                   other.m_YMin < m_YMin + m_Height;
        }

        /// <summary>With allowInverse both rects have their edges ordered with Mathf.Min/Max before the strict test.</summary>
        public readonly bool Overlaps(Rect other, bool allowInverse)
        {
            if (!allowInverse)
                return Overlaps(other);

            float lhsXMax = m_XMin + m_Width;
            float lhsYMax = m_YMin + m_Height;
            float rhsXMax = other.m_XMin + other.m_Width;
            float rhsYMax = other.m_YMin + other.m_Height;

            return Mathf.Max(other.m_XMin, rhsXMax) > Mathf.Min(m_XMin, lhsXMax) &&
                   Mathf.Min(other.m_XMin, rhsXMax) < Mathf.Max(m_XMin, lhsXMax) &&
                   Mathf.Max(other.m_YMin, rhsYMax) > Mathf.Min(m_YMin, lhsYMax) &&
                   Mathf.Min(other.m_YMin, rhsYMax) < Mathf.Max(m_YMin, lhsYMax);
        }

        // ---- equality (spec §6 "Equality": exact ==, NaN-equal Equals) ----

        public static bool operator ==(Rect lhs, Rect rhs)
        {
            return lhs.m_XMin == rhs.m_XMin && lhs.m_YMin == rhs.m_YMin && lhs.m_Width == rhs.m_Width && lhs.m_Height == rhs.m_Height;
        }

        public static bool operator !=(Rect lhs, Rect rhs)
        {
            return !(lhs == rhs);
        }

        public override readonly bool Equals(object other)
        {
            return other is Rect rect && Equals(rect);
        }

        /// <summary>float.Equals per component, so NaN equals NaN (spec §0/§6).</summary>
        public readonly bool Equals(Rect other)
        {
            return m_XMin.Equals(other.m_XMin) && m_YMin.Equals(other.m_YMin) && m_Width.Equals(other.m_Width) && m_Height.Equals(other.m_Height);
        }

        /// <summary>Note the x / width / y / height order of the terms; (1,2,3,4) hashes to 247463936 [verified].</summary>
        public override readonly int GetHashCode()
        {
            return m_XMin.GetHashCode() ^ (m_Width.GetHashCode() << 2) ^ (m_YMin.GetHashCode() >> 2) ^ (m_Height.GetHashCode() >> 1);
        }

        // ---- ToString (spec §0/§6: template "(x:{0}, y:{1}, width:{2}, height:{3})", default "F2") ----

        public override readonly string ToString()
        {
            return ToString(null, null);
        }

        public readonly string ToString(string format)
        {
            return ToString(format, null);
        }

        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F2";
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return string.Format(
                "(x:{0}, y:{1}, width:{2}, height:{3})",
                m_XMin.ToString(format, formatProvider),
                m_YMin.ToString(format, formatProvider),
                m_Width.ToString(format, formatProvider),
                m_Height.ToString(format, formatProvider));
        }
    }
}
