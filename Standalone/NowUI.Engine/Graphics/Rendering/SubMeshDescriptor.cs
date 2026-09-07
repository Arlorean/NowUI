// Mirrors UnityEngine.Rendering.SubMeshDescriptor for the NowUI standalone build.
// Governed by StandaloneCoreDesign.md §3.5 ("Engine/Graphics/Rendering/") and inventory §A line 115
// (Now.UploadCapturedMeshes and NowMesh.UploadMesh construct one per batch).

using System;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering
{
    /// <summary>
    /// The index range, topology and vertex range of one sub-mesh (design §3.5).
    /// </summary>
    /// <remarks>
    /// <b>Note for backend authors.</b> NowUI writes <em>global</em> indices — <c>Now.cs:1922-1940</c> appends with
    /// <c>mesh.AppendTriangles(ref _triangles16, vertexOffset)</c> — and then sets <see cref="firstVertex"/> to that
    /// same <c>vertexOffset</c> while leaving <see cref="baseVertex"/> at 0. So a draw must <b>not</b> add an offset of
    /// its own: <see cref="firstVertex"/>/<see cref="vertexCount"/> describe the range for validation and for a
    /// vertex-range hint, not a shift to apply to the indices.
    /// </remarks>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct SubMeshDescriptor : IEquatable<SubMeshDescriptor>
    {
        Bounds m_Bounds;
        MeshTopology m_Topology;
        int m_IndexStart;
        int m_IndexCount;
        int m_BaseVertex;
        int m_FirstVertex;
        int m_VertexCount;

        /// <summary>Local-space bounds of this sub-mesh. NowUI leaves it at the default and sets Mesh.bounds instead.</summary>
        public Bounds bounds
        {
            readonly get => m_Bounds;
            set => m_Bounds = value;
        }

        /// <summary>How the index range is assembled into primitives.</summary>
        public MeshTopology topology
        {
            readonly get => m_Topology;
            set => m_Topology = value;
        }

        /// <summary>First index of this sub-mesh within the mesh's index buffer.</summary>
        public int indexStart
        {
            readonly get => m_IndexStart;
            set => m_IndexStart = value;
        }

        /// <summary>Number of indices in this sub-mesh.</summary>
        public int indexCount
        {
            readonly get => m_IndexCount;
            set => m_IndexCount = value;
        }

        /// <summary>Value added to every index at draw time. NowUI always leaves this at 0 (see the type remarks).</summary>
        public int baseVertex
        {
            readonly get => m_BaseVertex;
            set => m_BaseVertex = value;
        }

        /// <summary>First vertex referenced by this sub-mesh.</summary>
        public int firstVertex
        {
            readonly get => m_FirstVertex;
            set => m_FirstVertex = value;
        }

        /// <summary>Number of vertices referenced by this sub-mesh.</summary>
        public int vertexCount
        {
            readonly get => m_VertexCount;
            set => m_VertexCount = value;
        }

        /// <summary>
        /// Creates a descriptor over an index range. Everything else stays at zero, which is what lets NowUI write
        /// <c>new SubMeshDescriptor(start, count) { firstVertex = …, vertexCount = … }</c>.
        /// </summary>
        public SubMeshDescriptor(int indexStart, int indexCount, MeshTopology topology = MeshTopology.Triangles)
        {
            m_Bounds = default;
            m_Topology = topology;
            m_IndexStart = indexStart;
            m_IndexCount = indexCount;
            m_BaseVertex = 0;
            m_FirstVertex = 0;
            m_VertexCount = 0;
        }

        public readonly bool Equals(SubMeshDescriptor other) =>
            m_Bounds == other.m_Bounds &&
            m_Topology == other.m_Topology &&
            m_IndexStart == other.m_IndexStart &&
            m_IndexCount == other.m_IndexCount &&
            m_BaseVertex == other.m_BaseVertex &&
            m_FirstVertex == other.m_FirstVertex &&
            m_VertexCount == other.m_VertexCount;

        public override readonly bool Equals(object other) => other is SubMeshDescriptor d && Equals(d);

        public override readonly int GetHashCode() =>
            HashCode.Combine(m_Bounds, (int)m_Topology, m_IndexStart, m_IndexCount, m_BaseVertex, m_FirstVertex, m_VertexCount);

        public static bool operator ==(SubMeshDescriptor lhs, SubMeshDescriptor rhs) => lhs.Equals(rhs);

        public static bool operator !=(SubMeshDescriptor lhs, SubMeshDescriptor rhs) => !lhs.Equals(rhs);

        public override readonly string ToString() =>
            $"(topo={m_Topology} indices={m_IndexStart},{m_IndexCount} vertices={m_FirstVertex},{m_VertexCount} basevtx={m_BaseVertex} bounds={m_Bounds})";
    }
}
