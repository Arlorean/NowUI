// Mirrors UnityEngine.Ray (minimal surface, needed by the public Bounds.IntersectRay overloads) for the NowUI standalone
// build; spec: Docs/Standalone/UnityValueTypeSemantics.md (§10 lists IntersectRay(Ray) / IntersectRay(Ray, out float)).
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// Half-line with an origin and a normalised direction. The spec has no Ray section — it only states, under
    /// <c>Bounds.IntersectRay</c>, that "the Ray struct normalises its direction, so it is a world distance" — so every
    /// member here is documented-behaviour only, exactly like <see cref="Plane"/>.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Ray : IFormattable
    {
        // unverified vs Unity: no Ray section in the spec; semantics follow the public documentation and spec §10's
        // note that the direction is normalised on construction.
        private Vector3 m_Origin;
        private Vector3 m_Direction;

        /// <summary>Direction is normalised (a zero direction stays zero, per Vector3.Normalize).</summary>
        public Ray(Vector3 origin, Vector3 direction)
        {
            m_Origin = origin;
            m_Direction = direction.normalized;
        }

        public Vector3 origin
        {
            readonly get => m_Origin;
            set => m_Origin = value;
        }

        /// <summary>The setter normalises too, so <c>direction</c> is always unit length (or zero).</summary>
        public Vector3 direction
        {
            readonly get => m_Direction;
            set => m_Direction = value.normalized;
        }

        /// <summary>Point at <paramref name="distance"/> along the ray: origin + direction * distance.</summary>
        public readonly Vector3 GetPoint(float distance)
        {
            return new Vector3(
                m_Origin.x + m_Direction.x * distance,
                m_Origin.y + m_Direction.y * distance,
                m_Origin.z + m_Direction.z * distance);
        }

        public override readonly string ToString() => ToString(null, null);

        public readonly string ToString(string format) => ToString(format, null);

        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F2";
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return string.Format("Origin: {0}, Dir: {1}", m_Origin.ToString(format, formatProvider), m_Direction.ToString(format, formatProvider));
        }
    }
}
