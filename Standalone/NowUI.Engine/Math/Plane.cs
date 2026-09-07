// Mirrors UnityEngine.Plane (minimal surface, needed by Matrix4x4.TransformPlane) for the NowUI standalone build; spec: Docs/Standalone/UnityValueTypeSemantics.md.
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// Infinite plane described by a unit normal and the signed distance from the origin along that normal
    /// (a point p is on the plane when Dot(normal, p) + distance == 0). The spec has no Plane section, so every member
    /// here is documented-behaviour only.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Plane : IFormattable
    {
        // unverified vs Unity: no Plane section in the spec; semantics follow the public documentation.
        private Vector3 m_Normal;
        private float m_Distance;

        public Vector3 normal
        {
            readonly get => m_Normal;
            set => m_Normal = value;
        }

        public float distance
        {
            readonly get => m_Distance;
            set => m_Distance = value;
        }

        /// <summary>Normal is normalised; distance is taken as given.</summary>
        public Plane(Vector3 inNormal, float d)
        {
            m_Normal = inNormal.normalized;
            m_Distance = d;
        }

        /// <summary>Normal is normalised; distance = -Dot(normal, inPoint).</summary>
        public Plane(Vector3 inNormal, Vector3 inPoint)
        {
            m_Normal = inNormal.normalized;
            m_Distance = -Vector3.Dot(m_Normal, inPoint);
        }

        /// <summary>Plane through three points; normal = normalize(cross(b - a, c - a)).</summary>
        public Plane(Vector3 a, Vector3 b, Vector3 c)
        {
            m_Normal = Vector3.Cross(b - a, c - a).normalized;
            m_Distance = -Vector3.Dot(m_Normal, a);
        }

        public void SetNormalAndPosition(Vector3 inNormal, Vector3 inPoint)
        {
            m_Normal = inNormal.normalized;
            m_Distance = -Vector3.Dot(m_Normal, inPoint);
        }

        public void Set3Points(Vector3 a, Vector3 b, Vector3 c)
        {
            m_Normal = Vector3.Cross(b - a, c - a).normalized;
            m_Distance = -Vector3.Dot(m_Normal, a);
        }

        public void Flip()
        {
            m_Normal = -m_Normal;
            m_Distance = -m_Distance;
        }

        public readonly Plane flipped => new Plane(-m_Normal, -m_Distance);

        public void Translate(Vector3 translation)
        {
            m_Distance += Vector3.Dot(m_Normal, translation);
        }

        public static Plane Translate(Plane plane, Vector3 translation)
        {
            return new Plane(plane.m_Normal, plane.m_Distance + Vector3.Dot(plane.m_Normal, translation));
        }

        public readonly Vector3 ClosestPointOnPlane(Vector3 point)
        {
            float pointToPlaneDistance = Vector3.Dot(m_Normal, point) + m_Distance;
            return point - m_Normal * pointToPlaneDistance;
        }

        public readonly float GetDistanceToPoint(Vector3 point)
        {
            return Vector3.Dot(m_Normal, point) + m_Distance;
        }

        public readonly bool GetSide(Vector3 point)
        {
            return Vector3.Dot(m_Normal, point) + m_Distance > 0f;
        }

        public readonly bool SameSide(Vector3 inPt0, Vector3 inPt1)
        {
            float d0 = GetDistanceToPoint(inPt0);
            float d1 = GetDistanceToPoint(inPt1);
            return (d0 > 0f && d1 > 0f) || (d0 <= 0f && d1 <= 0f);
        }

        public override readonly string ToString() => ToString(null, null);

        public readonly string ToString(string format) => ToString(format, null);

        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F1";
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return string.Format("(normal:{0}, distance:{1})", m_Normal.ToString(format, formatProvider), m_Distance.ToString(format, formatProvider));
        }
    }
}
