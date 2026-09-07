// Mirrors UnityEngine.Rendering.VertexAttributeDescriptor for the NowUI standalone build.
// Governed by StandaloneCoreDesign.md §3.5 ("Engine/Graphics/Rendering/") and inventory §A line 116
// (NowMesh.CanvasVertexLayout / NowMesh.RenderVertexLayout are arrays of this struct).

using System;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering
{
    /// <summary>
    /// One channel of an interleaved vertex buffer layout: which <see cref="VertexAttribute"/> it carries, in what
    /// <see cref="VertexAttributeFormat"/>, how many components, and which vertex stream it lives in (design §3.5).
    /// </summary>
    /// <remarks>
    /// Unity stores the four values in private fields with public properties rather than as public fields, and the
    /// standalone shim copies that shape: NowUI writes the layout tables with the constructor and never assigns a
    /// member afterwards, but a host or backend that reflects over the type must see Unity's member list.
    /// <para>
    /// The constructor's defaults (<c>Position</c>, <c>Float32</c>, dimension 3, stream 0) are Unity's, which is what
    /// makes <c>new VertexAttributeDescriptor()</c> a valid position channel rather than a zero-dimension one.
    /// </para>
    /// </remarks>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct VertexAttributeDescriptor : IEquatable<VertexAttributeDescriptor>
    {
        VertexAttribute m_Attribute;
        VertexAttributeFormat m_Format;
        int m_Dimension;
        int m_Stream;

        /// <summary>The channel this descriptor feeds.</summary>
        public VertexAttribute attribute
        {
            readonly get => m_Attribute;
            set => m_Attribute = value;
        }

        /// <summary>The element type of each component.</summary>
        public VertexAttributeFormat format
        {
            readonly get => m_Format;
            set => m_Format = value;
        }

        /// <summary>Number of components (1..4).</summary>
        public int dimension
        {
            readonly get => m_Dimension;
            set => m_Dimension = value;
        }

        /// <summary>Which interleaved vertex stream the channel lives in. NowUI only ever uses stream 0.</summary>
        public int stream
        {
            readonly get => m_Stream;
            set => m_Stream = value;
        }

        /// <summary>Creates a descriptor; every parameter has Unity's default.</summary>
        public VertexAttributeDescriptor(
            VertexAttribute attribute = VertexAttribute.Position,
            VertexAttributeFormat format = VertexAttributeFormat.Float32,
            int dimension = 3,
            int stream = 0)
        {
            m_Attribute = attribute;
            m_Format = format;
            m_Dimension = dimension;
            m_Stream = stream;
        }

        /// <summary>
        /// Bytes this channel occupies in a vertex. Internal because Unity does not expose it, but the shim's
        /// <see cref="Mesh"/> needs it to compute the interleaved stride and per-attribute offsets (design §3.5).
        /// </summary>
        internal readonly int byteSize => FormatByteSize(m_Format) * m_Dimension;

        /// <summary>Bytes per component of a <see cref="VertexAttributeFormat"/>.</summary>
        internal static int FormatByteSize(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32:
                    return 4;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16:
                    return 2;
                case VertexAttributeFormat.UNorm8:
                case VertexAttributeFormat.SNorm8:
                case VertexAttributeFormat.UInt8:
                case VertexAttributeFormat.SInt8:
                    return 1;
                default:
                    return 4;
            }
        }

        public readonly bool Equals(VertexAttributeDescriptor other) =>
            m_Attribute == other.m_Attribute &&
            m_Format == other.m_Format &&
            m_Dimension == other.m_Dimension &&
            m_Stream == other.m_Stream;

        public override readonly bool Equals(object other) => other is VertexAttributeDescriptor d && Equals(d);

        public override readonly int GetHashCode() =>
            HashCode.Combine((int)m_Attribute, (int)m_Format, m_Dimension, m_Stream);

        public static bool operator ==(VertexAttributeDescriptor lhs, VertexAttributeDescriptor rhs) => lhs.Equals(rhs);

        public static bool operator !=(VertexAttributeDescriptor lhs, VertexAttributeDescriptor rhs) => !lhs.Equals(rhs);

        public override readonly string ToString() =>
            $"(attr={m_Attribute} fmt={m_Format} dim={m_Dimension} stream={m_Stream})";
    }
}
