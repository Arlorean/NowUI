// Mirrors UnityEngine.BoundsInt for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.1 (member list); value-type conventions from
// Docs/Standalone/UnityValueTypeSemantics.md (§10 Bounds is the float sibling of this type).
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// Integer axis-aligned box stored as raw (position, size), the integer sibling of <see cref="Bounds"/>.
    /// Like <see cref="RectInt"/> the raw storage may describe an inverted box (a negative size component); the
    /// xMin/xMax/yMin/yMax/zMin/zMax and min/max accessors normalise it with Min/Max, while position/size and the
    /// component accessors hand the raw values back unchanged.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct BoundsInt : IEquatable<BoundsInt>
    {
        private Vector3Int m_Position;
        private Vector3Int m_Size;

        public BoundsInt(Vector3Int position, Vector3Int size)
        {
            m_Position = position;
            m_Size = size;
        }

        public BoundsInt(int xMin, int yMin, int zMin, int sizeX, int sizeY, int sizeZ)
        {
            m_Position = new Vector3Int(xMin, yMin, zMin);
            m_Size = new Vector3Int(sizeX, sizeY, sizeZ);
        }

        // ---- raw storage ----

        public Vector3Int position
        {
            readonly get => m_Position;
            set => m_Position = value;
        }

        public Vector3Int size
        {
            readonly get => m_Size;
            set => m_Size = value;
        }

        public int x
        {
            readonly get => m_Position.x;
            set => m_Position.x = value;
        }

        public int y
        {
            readonly get => m_Position.y;
            set => m_Position.y = value;
        }

        public int z
        {
            readonly get => m_Position.z;
            set => m_Position.z = value;
        }

        public int sizeX
        {
            readonly get => m_Size.x;
            set => m_Size.x = value;
        }

        public int sizeY
        {
            readonly get => m_Size.y;
            set => m_Size.y = value;
        }

        public int sizeZ
        {
            readonly get => m_Size.z;
            set => m_Size.z = value;
        }

        // ---- normalised extents ----
        // Min/Max rather than a bare position / position + size so an inverted box (negative size) still reports a
        // min that is <= its max, which is what Contains and ClampToBounds are written against.

        public readonly int xMin => System.Math.Min(m_Position.x, m_Position.x + m_Size.x);

        public readonly int xMax => System.Math.Max(m_Position.x, m_Position.x + m_Size.x);

        public readonly int yMin => System.Math.Min(m_Position.y, m_Position.y + m_Size.y);

        public readonly int yMax => System.Math.Max(m_Position.y, m_Position.y + m_Size.y);

        public readonly int zMin => System.Math.Min(m_Position.z, m_Position.z + m_Size.z);

        public readonly int zMax => System.Math.Max(m_Position.z, m_Position.z + m_Size.z);

        /// <summary>
        /// The low corner. Assigning moves the corner while <i>keeping the current max</i>, so the box is resized
        /// rather than translated — the same asymmetry Unity has, and the reason a min assignment can invert a box
        /// when the new min passes the old max.
        /// </summary>
        public Vector3Int min
        {
            readonly get => new Vector3Int(xMin, yMin, zMin);
            set
            {
                int oldXMax = xMax;
                int oldYMax = yMax;
                int oldZMax = zMax;
                m_Position = value;
                m_Size = new Vector3Int(oldXMax - value.x, oldYMax - value.y, oldZMax - value.z);
            }
        }

        /// <summary>
        /// The high corner. Assigning keeps the raw position and rewrites the size, the mirror of <see cref="min"/>.
        /// </summary>
        public Vector3Int max
        {
            readonly get => new Vector3Int(xMax, yMax, zMax);
            set => m_Size = new Vector3Int(value.x - m_Position.x, value.y - m_Position.y, value.z - m_Position.z);
        }

        /// <summary>
        /// The geometric centre in float space: raw position plus half the raw size. Integer division would round
        /// an odd-sized box off centre, so the halving is done in float (<c>/ 2f</c>).
        /// </summary>
        public readonly Vector3 center =>
            new Vector3(m_Position.x + m_Size.x / 2f, m_Position.y + m_Size.y / 2f, m_Position.z + m_Size.z / 2f);

        // ---- queries ----

        /// <summary>
        /// Min-inclusive, max-exclusive containment: a cell sits inside the box when its coordinate is >= the min
        /// and strictly &lt; the max on every axis. That is the tile convention — a 1x1x1 box contains exactly its
        /// own position — and it means a zero-sized box contains nothing.
        /// </summary>
        public readonly bool Contains(Vector3Int position)
        {
            return position.x >= xMin && position.y >= yMin && position.z >= zMin
                && position.x < xMax && position.y < yMax && position.z < zMax;
        }

        // ---- mutation ----

        /// <summary>
        /// Clips this box into <paramref name="bounds"/>: the position is clamped into the other box first, then the
        /// size is shrunk so the far corner cannot pass the other box's max. The position is clamped against
        /// <c>bounds.xMax</c> before <c>bounds.xMin</c> so a box that starts entirely past the far side lands on the
        /// clamping box rather than outside it.
        /// </summary>
        public void ClampToBounds(BoundsInt bounds)
        {
            m_Position = new Vector3Int(
                System.Math.Max(System.Math.Min(bounds.xMax, m_Position.x), bounds.xMin),
                System.Math.Max(System.Math.Min(bounds.yMax, m_Position.y), bounds.yMin),
                System.Math.Max(System.Math.Min(bounds.zMax, m_Position.z), bounds.zMin));
            m_Size = new Vector3Int(
                System.Math.Min(bounds.xMax - m_Position.x, m_Size.x),
                System.Math.Min(bounds.yMax - m_Position.y, m_Size.y),
                System.Math.Min(bounds.zMax - m_Position.z, m_Size.z));
        }

        /// <summary>
        /// Sets both corners at once: position becomes <paramref name="min"/> and size becomes max - min. Unlike the
        /// <see cref="min"/>/<see cref="max"/> setters this writes the raw storage directly, so passing a max below
        /// the min stores a negative size.
        /// </summary>
        public void SetMinMax(Vector3Int min, Vector3Int max)
        {
            m_Position = min;
            m_Size = new Vector3Int(max.x - min.x, max.y - min.y, max.z - min.z);
        }

        // ---- equality ----
        // Over the raw storage, not the normalised extents: two boxes that describe the same region with different
        // raw values (one inverted) are not equal, matching how Vector3Int-backed structs compare elsewhere.

        public static bool operator ==(BoundsInt lhs, BoundsInt rhs)
        {
            return lhs.m_Position == rhs.m_Position && lhs.m_Size == rhs.m_Size;
        }

        public static bool operator !=(BoundsInt lhs, BoundsInt rhs)
        {
            return !(lhs == rhs);
        }

        public override readonly bool Equals(object other)
        {
            return other is BoundsInt b && Equals(b);
        }

        public readonly bool Equals(BoundsInt other)
        {
            return m_Position.Equals(other.m_Position) && m_Size.Equals(other.m_Size);
        }

        public override readonly int GetHashCode()
        {
            // Same mix as Bounds: the size is shifted so (a, b) and (b, a) do not collide.
            return m_Position.GetHashCode() ^ (m_Size.GetHashCode() << 2);
        }

        // ---- ToString ----
        // "Position: {0}, Size: {1}" with each Vector3Int formatted by its own ToString ("(x, y, z)"), invariant.

        public override readonly string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture.NumberFormat,
                "Position: {0}, Size: {1}",
                m_Position.ToString(),
                m_Size.ToString());
        }
    }
}
