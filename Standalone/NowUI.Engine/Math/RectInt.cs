// Mirrors UnityEngine.RectInt for the NowUI standalone build; spec: Docs/Standalone/UnityValueTypeSemantics.md (§7).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// Integer rectangle stored as raw (xMin, yMin, width, height). The raw fields may describe an inverted rect;
    /// the xMin/xMax/yMin/yMax/min/max getters normalise it, the raw x/y/width/height properties do not (spec §7).
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct RectInt : IEquatable<RectInt>, IFormattable
    {
        private int m_XMin;
        private int m_YMin;
        private int m_Width;
        private int m_Height;

        public RectInt(int xMin, int yMin, int width, int height)
        {
            m_XMin = xMin;
            m_YMin = yMin;
            m_Width = width;
            m_Height = height;
        }

        public RectInt(Vector2Int position, Vector2Int size)
        {
            m_XMin = position.x;
            m_YMin = position.y;
            m_Width = size.x;
            m_Height = size.y;
        }

        private static readonly RectInt zeroRect = new RectInt(0, 0, 0, 0);

        public static RectInt zero => zeroRect;

        // ---- raw properties ----

        public int x
        {
            readonly get => m_XMin;
            set => m_XMin = value;
        }

        public int y
        {
            readonly get => m_YMin;
            set => m_YMin = value;
        }

        public int width
        {
            readonly get => m_Width;
            set => m_Width = value;
        }

        public int height
        {
            readonly get => m_Height;
            set => m_Height = value;
        }

        public Vector2Int position
        {
            readonly get => new Vector2Int(m_XMin, m_YMin);
            set
            {
                m_XMin = value.x;
                m_YMin = value.y;
            }
        }

        public Vector2Int size
        {
            readonly get => new Vector2Int(m_Width, m_Height);
            set
            {
                m_Width = value.x;
                m_Height = value.y;
            }
        }

        /// <summary>Raw centre (not normalised); get only (spec §7).</summary>
        public readonly Vector2 center => new Vector2(m_XMin + m_Width * 0.5f, m_YMin + m_Height * 0.5f);

        // ---- normalising edge properties (spec §7) ----

        /// <summary>Getter normalises inverted rects; setter keeps the (normalised) old xMax fixed.</summary>
        public int xMin
        {
            readonly get => Mathf.Min(m_XMin, m_XMin + m_Width);
            set
            {
                int oldXMax = xMax;
                m_XMin = value;
                m_Width = oldXMax - m_XMin;
            }
        }

        public int yMin
        {
            readonly get => Mathf.Min(m_YMin, m_YMin + m_Height);
            set
            {
                int oldYMax = yMax;
                m_YMin = value;
                m_Height = oldYMax - m_YMin;
            }
        }

        public int xMax
        {
            readonly get => Mathf.Max(m_XMin, m_XMin + m_Width);
            set => m_Width = value - m_XMin;
        }

        public int yMax
        {
            readonly get => Mathf.Max(m_YMin, m_YMin + m_Height);
            set => m_Height = value - m_YMin;
        }

        public Vector2Int min
        {
            readonly get => new Vector2Int(xMin, yMin);
            set
            {
                xMin = value.x;
                yMin = value.y;
            }
        }

        public Vector2Int max
        {
            readonly get => new Vector2Int(xMax, yMax);
            set
            {
                xMax = value.x;
                yMax = value.y;
            }
        }

        // ---- methods ----

        public void SetMinMax(Vector2Int min, Vector2Int max)
        {
            m_XMin = min.x;
            m_YMin = min.y;
            m_Width = max.x - min.x;
            m_Height = max.y - min.y;
        }

        /// <summary>Clamps position and raw size into the (normalised) edges of <paramref name="bounds"/> (spec §7).</summary>
        public void ClampToBounds(RectInt bounds)
        {
            int xmin = bounds.xMin;
            int xmax = bounds.xMax;
            int ymin = bounds.yMin;
            int ymax = bounds.yMax;

            m_XMin = Math.Max(Math.Min(xmax, m_XMin), xmin);
            m_YMin = Math.Max(Math.Min(ymax, m_YMin), ymin);
            m_Width = Math.Min(xmax - m_XMin, m_Width);
            m_Height = Math.Min(ymax - m_YMin, m_Height);
        }

        /// <summary>Min inclusive, max exclusive on the normalised edges, so inverted rects still contain points.</summary>
        public readonly bool Contains(Vector2Int position)
        {
            return position.x >= xMin && position.y >= yMin && position.x < xMax && position.y < yMax;
        }

        /// <summary>Strict test on the normalised edges (spec §7).</summary>
        public readonly bool Overlaps(RectInt other)
        {
            return other.xMin < xMax && other.xMax > xMin && other.yMin < yMax && other.yMax > yMin;
        }

        // ---- equality: exact on the four raw fields (spec §7) ----

        public static bool operator ==(RectInt lhs, RectInt rhs)
        {
            return lhs.m_XMin == rhs.m_XMin && lhs.m_YMin == rhs.m_YMin && lhs.m_Width == rhs.m_Width && lhs.m_Height == rhs.m_Height;
        }

        public static bool operator !=(RectInt lhs, RectInt rhs)
        {
            return !(lhs == rhs);
        }

        public override readonly bool Equals(object other)
        {
            return other is RectInt rect && Equals(rect);
        }

        public readonly bool Equals(RectInt other)
        {
            return m_XMin == other.m_XMin && m_YMin == other.m_YMin && m_Width == other.m_Width && m_Height == other.m_Height;
        }

        public override readonly int GetHashCode()
        {
            int xh = m_XMin.GetHashCode();
            int yh = m_YMin.GetHashCode();
            int wh = m_Width.GetHashCode();
            int hh = m_Height.GetHashCode();
            return xh ^ (yh << 4) ^ (yh >> 28) ^ (wh >> 4) ^ (wh << 28) ^ (hh >> 4) ^ (hh << 28);
        }

        // ---- ToString (spec §0/§7: same template as Rect, no default format) ----

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
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return string.Format(
                "(x:{0}, y:{1}, width:{2}, height:{3})",
                m_XMin.ToString(format, formatProvider),
                m_YMin.ToString(format, formatProvider),
                m_Width.ToString(format, formatProvider),
                m_Height.ToString(format, formatProvider));
        }

        // ---- position enumeration (spec §7 "allPositionsWithin") ----

        /// <summary>Enumerates every integer position in [min, max) — x fastest, then y.</summary>
        public readonly PositionEnumerator allPositionsWithin => new PositionEnumerator(min, max);

        /// <summary>Struct enumerator; GetEnumerator returns itself so foreach works without boxing (spec §7).</summary>
        public struct PositionEnumerator : IEnumerator<Vector2Int>
        {
            private readonly Vector2Int _min;
            private readonly Vector2Int _max;
            private Vector2Int _current;

            public PositionEnumerator(Vector2Int min, Vector2Int max)
            {
                _min = min;
                _max = max;
                _current = min;
                _current.x--;
            }

            public PositionEnumerator GetEnumerator()
            {
                return this;
            }

            public bool MoveNext()
            {
                if (_current.y >= _max.y)
                    return false;

                _current.x++;
                if (_current.x >= _max.x)
                {
                    _current.x = _min.x;
                    if (_current.x >= _max.x)
                        return false;

                    _current.y++;
                    if (_current.y >= _max.y)
                        return false;
                }

                return true;
            }

            public void Reset()
            {
                _current = _min;
                _current.x--;
            }

            public Vector2Int Current => _current;

            object IEnumerator.Current => Current;

            void IDisposable.Dispose()
            {
            }
        }
    }
}
