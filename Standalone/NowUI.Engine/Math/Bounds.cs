// Mirrors UnityEngine.Bounds for the NowUI standalone build; spec: Docs/Standalone/UnityValueTypeSemantics.md (§10).
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// Axis-aligned box stored as centre + extents (half size), in that order, six floats total (spec §10).
    /// Extents may go negative (Expand with a negative amount, size setter); nothing normalises them.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Bounds : IEquatable<Bounds>, IFormattable
    {
        private Vector3 m_Center;
        private Vector3 m_Extents;

        public Bounds(Vector3 center, Vector3 size)
        {
            m_Center = center;
            m_Extents = new Vector3(size.x * 0.5F, size.y * 0.5F, size.z * 0.5F);
        }

        // ---- properties (spec §10) ----

        public Vector3 center
        {
            readonly get => m_Center;
            set => m_Center = value;
        }

        public Vector3 extents
        {
            readonly get => m_Extents;
            set => m_Extents = value;
        }

        public Vector3 size
        {
            readonly get => new Vector3(m_Extents.x * 2.0F, m_Extents.y * 2.0F, m_Extents.z * 2.0F);
            set => m_Extents = new Vector3(value.x * 0.5F, value.y * 0.5F, value.z * 0.5F);
        }

        /// <summary>center - extents; the setter keeps the current max and re-derives centre/extents.</summary>
        public Vector3 min
        {
            readonly get => new Vector3(m_Center.x - m_Extents.x, m_Center.y - m_Extents.y, m_Center.z - m_Extents.z);
            set => SetMinMax(value, max);
        }

        /// <summary>center + extents; the setter keeps the current min and re-derives centre/extents.</summary>
        public Vector3 max
        {
            readonly get => new Vector3(m_Center.x + m_Extents.x, m_Center.y + m_Extents.y, m_Center.z + m_Extents.z);
            set => SetMinMax(min, value);
        }

        // ---- methods ----

        /// <summary>extents = (max - min) * 0.5 first, then center = min + extents (spec §10 order).</summary>
        public void SetMinMax(Vector3 min, Vector3 max)
        {
            m_Extents = new Vector3((max.x - min.x) * 0.5F, (max.y - min.y) * 0.5F, (max.z - min.z) * 0.5F);
            m_Center = new Vector3(min.x + m_Extents.x, min.y + m_Extents.y, min.z + m_Extents.z);
        }

        /// <summary>Unit box + (3,0,0) → center (1.25,0,0), extents (1.75,0.5,0.5) [verified].</summary>
        public void Encapsulate(Vector3 point)
        {
            SetMinMax(Vector3.Min(min, point), Vector3.Max(max, point));
        }

        public void Encapsulate(Bounds bounds)
        {
            Encapsulate(bounds.min);
            Encapsulate(bounds.max);
        }

        /// <summary>Adds amount/2 to every extent; may drive extents negative (spec §10).</summary>
        public void Expand(float amount)
        {
            amount *= 0.5f;
            m_Extents.x += amount;
            m_Extents.y += amount;
            m_Extents.z += amount;
        }

        public void Expand(Vector3 amount)
        {
            m_Extents.x += amount.x * 0.5f;
            m_Extents.y += amount.y * 0.5f;
            m_Extents.z += amount.z * 0.5f;
        }

        /// <summary>Inclusive slab overlap computed from min/max, so negative extents still report intersections [verified].</summary>
        public readonly bool Intersects(Bounds bounds)
        {
            Vector3 lhsMin = min;
            Vector3 lhsMax = max;
            Vector3 rhsMin = bounds.min;
            Vector3 rhsMax = bounds.max;
            return lhsMin.x <= rhsMax.x && lhsMax.x >= rhsMin.x &&
                   lhsMin.y <= rhsMax.y && lhsMax.y >= rhsMin.y &&
                   lhsMin.z <= rhsMax.z && lhsMax.z >= rhsMin.z;
        }

        /// <summary>
        /// Native in Unity (spec §10): inclusive on the faces; any negative extent → false; a NaN coordinate passes
        /// because the test is "reject if p &lt; min or p &gt; max" [verified for NaN on x].
        /// </summary>
        public readonly bool Contains(Vector3 point)
        {
            if (m_Extents.x < 0f || m_Extents.y < 0f || m_Extents.z < 0f)
                return false;

            Vector3 lo = min;
            Vector3 hi = max;
            if (point.x < lo.x || point.x > hi.x)
                return false;
            if (point.y < lo.y || point.y > hi.y)
                return false;
            if (point.z < lo.z || point.z > hi.z)
                return false;
            return true;
        }

        /// <summary>Squared distance from point to the box, 0 inside: (2,0,0) → 2.25, (2,2,2) → 6.75 [verified].</summary>
        public readonly float SqrDistance(Vector3 point)
        {
            // unverified vs Unity: behaviour for negative extents (implemented as distance to ClosestPoint).
            Vector3 closest = ClosestPoint(point);
            float dx = point.x - closest.x;
            float dy = point.y - closest.y;
            float dz = point.z - closest.z;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>Per component max(min_i, min(max_i, p_i)); with negative extents this yields min (spec §10).</summary>
        public readonly Vector3 ClosestPoint(Vector3 point)
        {
            Vector3 lo = min;
            Vector3 hi = max;
            return new Vector3(
                Mathf.Max(lo.x, Mathf.Min(hi.x, point.x)),
                Mathf.Max(lo.y, Mathf.Min(hi.y, point.y)),
                Mathf.Max(lo.z, Mathf.Min(hi.z, point.z)));
        }

        /// <summary>Spec §10: native slab test. True when the ray hits the box.</summary>
        public readonly bool IntersectRay(Ray ray)
        {
            return IntersectRay(ray.origin, ray.direction, out _);
        }

        /// <summary>
        /// Spec §10: native slab test; <paramref name="distance"/> is the ray parameter of the entry point (a world
        /// distance, because Ray normalises its direction), may be negative when the origin is inside, and is 0 on a miss.
        /// </summary>
        public readonly bool IntersectRay(Ray ray, out float distance)
        {
            return IntersectRay(ray.origin, ray.direction, out distance);
        }

        /// <summary>
        /// Slab test: distance is the ray parameter of the entry point (negative when the origin is inside), 0 on a
        /// miss; grazing an edge counts as a hit (spec §10). This is the implementation the two public
        /// <c>IntersectRay(Ray…)</c> overloads forward to, and it is usable without constructing a Ray; the direction
        /// is expected to be normalised (Ray normalises it).
        /// </summary>
        internal readonly bool IntersectRay(Vector3 origin, Vector3 direction, out float distance)
        {
            // unverified vs Unity: exact slab-test arithmetic, NaN handling and boxes entirely behind the origin.
            Vector3 lo = min;
            Vector3 hi = max;
            float tMin = float.NegativeInfinity;
            float tMax = float.PositiveInfinity;

            if (!Slab(lo.x, hi.x, origin.x, direction.x, ref tMin, ref tMax) ||
                !Slab(lo.y, hi.y, origin.y, direction.y, ref tMin, ref tMax) ||
                !Slab(lo.z, hi.z, origin.z, direction.z, ref tMin, ref tMax) ||
                tMax < 0f)
            {
                distance = 0f;
                return false;
            }

            distance = tMin;
            return true;
        }

        private static bool Slab(float lo, float hi, float o, float d, ref float tMin, ref float tMax)
        {
            if (d == 0f)
                return !(o < lo || o > hi);

            float inv = 1f / d;
            float t1 = (lo - o) * inv;
            float t2 = (hi - o) * inv;
            if (t1 > t2)
            {
                float tmp = t1;
                t1 = t2;
                t2 = tmp;
            }
            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            return tMin <= tMax;
        }

        // ---- equality (spec §10: tolerant == via Vector3.==, exact Equals via Vector3.Equals) ----

        public static bool operator ==(Bounds lhs, Bounds rhs)
        {
            return lhs.m_Center == rhs.m_Center && lhs.m_Extents == rhs.m_Extents;
        }

        public static bool operator !=(Bounds lhs, Bounds rhs)
        {
            return !(lhs == rhs);
        }

        public override readonly bool Equals(object other)
        {
            return other is Bounds bounds && Equals(bounds);
        }

        public readonly bool Equals(Bounds other)
        {
            return m_Center.Equals(other.m_Center) && m_Extents.Equals(other.m_Extents);
        }

        public override readonly int GetHashCode()
        {
            return m_Center.GetHashCode() ^ (m_Extents.GetHashCode() << 2);
        }

        // ---- ToString (spec §10: "Center: {0}, Extents: {1}", default "F2" handed to Vector3) ----

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
                "Center: {0}, Extents: {1}",
                m_Center.ToString(format, formatProvider),
                m_Extents.ToString(format, formatProvider));
        }
    }
}
