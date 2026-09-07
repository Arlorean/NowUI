// Mirrors UnityEngine.Mesh for the NowUI standalone build.
// Governed by StandaloneCoreDesign.md §3.5 ("Mesh.cs"), §1.2 (the allocation rule) and inventory §A line 96.
//
// The shim Mesh is a handle over a NowUI.Engine.NowMeshData CPU store. It retains COPIES of everything it is given:
// Now.UploadCapturedMeshes and NowMesh.UploadMesh hand the same static scratch lists to every draw list in a frame, so
// a Mesh that aliased the caller's array would show the next draw list's contents (design §3.5). Unity copies at call
// time; so does this. Nothing here touches a backend — creation is lazy, which is what makes hazard D.1 #4 safe.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NowUI.Engine;
using Unity.Collections;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// A vertex/index buffer handle with a retained CPU copy (design §3.5).
    /// </summary>
    /// <remarks>
    /// Two storage shapes are supported because NowUI uses both:
    /// <list type="bullet">
    /// <item><b>Interleaved</b> — <see cref="SetVertexBufferParams"/> declares a
    /// <see cref="VertexAttributeDescriptor"/> layout and <see cref="SetVertexBufferData{T}(T[], int, int, int, int, MeshUpdateFlags)"/>
    /// uploads packed vertex structs (<c>Now.cs:1886-1894</c>, <c>NowMesh.cs:1972-1981</c>).</item>
    /// <item><b>Per-attribute streams</b> — <see cref="SetVertices(Vector3[])"/> plus <see cref="SetUVs(int, Vector2[])"/>
    /// on channels 0..7 (<c>Now.cs:1898-1906</c>, <c>NowMesh.cs:1983-1991</c>).</item>
    /// </list>
    /// The read-back getters (<see cref="GetVertices"/>, <see cref="GetUVs(int, List{Vector4})"/>,
    /// <see cref="GetTriangles(List{int}, int)"/>) serve both, de-interleaving through the descriptor table when the
    /// store is interleaved. That is what <c>NowEffectsMesh.ReadMesh</c> depends on.
    /// </remarks>
    public sealed class Mesh : Object
    {
        /// <summary>Highest UV channel index Unity accepts.</summary>
        const int MaxUVChannel = 7;

        /// <summary>The retained CPU store. Backends read it directly rather than through a per-draw snapshot.</summary>
        internal NowMeshData data = new NowMeshData();

        /// <summary>Backend-side handle; 0 means "not created yet" (design §3.5).</summary>
        internal int backendId;

        Bounds m_Bounds;

        /// <summary>Bumped on every CPU-side change so a backend can re-upload only on a version mismatch (§3.5).</summary>
        internal uint version => data.version;

        public Mesh()
        {
        }

        // ---- shape -------------------------------------------------------------------------------------------------

        /// <summary>Live vertices in whichever storage shape is active.</summary>
        public int vertexCount => data.vertexCount;

        /// <summary>
        /// Number of sub-meshes.
        /// </summary>
        /// <remarks>
        /// Growing the count appends default descriptors (empty index ranges), which is Unity's behaviour; shrinking
        /// drops the tail but keeps the array, per the allocation rule.
        /// </remarks>
        public int subMeshCount
        {
            get => data.subMeshCount;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "subMeshCount cannot be negative.");

                data.EnsureSubMeshCapacity(value);

                for (int i = data.subMeshCount; i < value; ++i)
                    data.subMeshes[i] = default;

                data.subMeshCount = value;
                Touch();
            }
        }

        /// <summary>
        /// Index element width.
        /// </summary>
        /// <remarks>
        /// Changing the format recreates the index buffer in Unity, so the existing indices are dropped rather than
        /// converted. NowUI only ever sets this before uploading (<c>NowMesh.cs:1958-1965</c>) or through
        /// <see cref="SetIndexBufferParams"/>, so nothing observes the difference.
        /// </remarks>
        public IndexFormat indexFormat
        {
            get => data.indexFormat;
            set
            {
                if (data.indexFormat == value)
                    return;

                data.indexFormat = value;
                data.indexCount = 0;
                Touch();
            }
        }

        /// <summary>Local-space bounds. Stored exactly as given — nothing recomputes it behind the caller's back.</summary>
        public Bounds bounds
        {
            get => m_Bounds;
            set
            {
                m_Bounds = value;
                Touch();
            }
        }

        /// <summary>
        /// A hint that the mesh is rewritten every frame. A no-op here: the standalone store is always CPU-resident and
        /// the backend picks its buffer usage from the version churn instead.
        /// </summary>
        public void MarkDynamic()
        {
        }

        /// <summary>Announces an out-of-band change, so a backend re-uploads on the next bind.</summary>
        public void MarkModified() => Touch();

        /// <summary>
        /// A hint that the CPU copy may be released. A no-op: the standalone build keeps the CPU store because
        /// <c>NowEffectsMesh</c> reads it back (design §1.2).
        /// </summary>
        public void UploadMeshData(bool markNoLongerReadable)
        {
        }

        /// <summary>Clears all data, keeping the vertex layout (Unity's default).</summary>
        public void Clear() => Clear(true);

        /// <summary>
        /// Clears all vertex data, indices and sub-meshes, keeping buffer capacity (allocation rule §1.2).
        /// </summary>
        /// <remarks>
        /// Bounds are reset too, as Unity does — a cleared mesh has no extent. <c>NowMesh.UploadMesh</c> assigns
        /// <see cref="bounds"/> after the upload, so the reset is never observed there.
        /// <para>
        /// Unlike Unity, a cleared (or brand-new) mesh reports <c>subMeshCount == 0</c> rather than 1. Design §3.5
        /// specifies "zero counts" for <see cref="Clear()"/> and the shim keeps that literal; <see cref="SetTriangles"/>
        /// grows the table on demand so the one Unity idiom that depends on the implicit first sub-mesh still works.
        /// </para>
        /// </remarks>
        public void Clear(bool keepVertexLayout)
        {
            data.Clear(keepVertexLayout);
            m_Bounds = default;
        }

        // ---- interleaved vertex buffer -----------------------------------------------------------------------------

        /// <summary>
        /// Declares an interleaved vertex layout and reserves room for <paramref name="vertexCount"/> vertices.
        /// </summary>
        public void SetVertexBufferParams(int vertexCount, params VertexAttributeDescriptor[] attributes)
        {
            if (vertexCount < 0)
                throw new ArgumentOutOfRangeException(nameof(vertexCount));
            if (attributes == null)
                throw new ArgumentNullException(nameof(attributes));

            data.SetLayout(attributes, attributes.Length);
            data.vertexCount = vertexCount;
            NowMeshData.EnsureBytes(ref data.vertexBytes, vertexCount * data.vertexStride);

            // The per-attribute streams are stale the moment an interleaved layout is declared; zero their counts so a
            // later read-back cannot mix the two shapes.
            for (int i = 0; i < data.streams.Length; ++i)
                data.streams[i].count = 0;

            Touch();
        }

        /// <summary>Uploads packed vertex structs into the interleaved buffer.</summary>
        /// <remarks>
        /// <paramref name="meshBufferStart"/> is a <em>vertex</em> index, so its byte offset uses the buffer stride,
        /// while <paramref name="count"/> counts <typeparamref name="T"/> elements. For NowUI's two vertex structs the
        /// two sizes agree; keeping them separate is what makes a partial-stride upload behave as Unity's does.
        /// </remarks>
        public void SetVertexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, int stream = 0, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            CheckRange(dataStart, count, data.Length, nameof(data));
            WriteVertexBuffer(new ReadOnlySpan<T>(data, dataStart, count), meshBufferStart, flags);
        }

        /// <inheritdoc cref="SetVertexBufferData{T}(T[], int, int, int, int, MeshUpdateFlags)"/>
        public void SetVertexBufferData<T>(List<T> data, int dataStart, int meshBufferStart, int count, int stream = 0, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            CheckRange(dataStart, count, data.Count, nameof(data));
            WriteVertexBuffer(NowMeshData.AsSpan(data, dataStart, count), meshBufferStart, flags);
        }

        /// <inheritdoc cref="SetVertexBufferData{T}(T[], int, int, int, int, MeshUpdateFlags)"/>
        public void SetVertexBufferData<T>(NativeArray<T> data, int dataStart, int meshBufferStart, int count, int stream = 0, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            CheckRange(dataStart, count, data.Length, nameof(data));
            WriteVertexBuffer(data.AsReadOnlySpan().Slice(dataStart, count), meshBufferStart, flags);
        }

        void WriteVertexBuffer<T>(ReadOnlySpan<T> source, int meshBufferStart, MeshUpdateFlags flags) where T : struct
        {
            if (!data.interleaved)
                throw new InvalidOperationException("SetVertexBufferData requires SetVertexBufferParams to have declared a vertex layout.");
            if (meshBufferStart < 0)
                throw new ArgumentOutOfRangeException(nameof(meshBufferStart));

            int elementSize = Unsafe.SizeOf<T>();
            int byteOffset = meshBufferStart * data.vertexStride;
            NowMeshData.EnsureBytes(ref data.vertexBytes, byteOffset + source.Length * elementSize);
            NowMeshData.CopyElements(source, data.vertexBytes, byteOffset);

            Touch();
            RecalculateBoundsUnless(flags);
        }

        // ---- per-attribute streams ---------------------------------------------------------------------------------

        public void SetVertices(Vector3[] inVertices) => SetVertices(inVertices, 0, inVertices?.Length ?? 0);

        public void SetVertices(Vector3[] inVertices, int start, int length) =>
            SetVertices(inVertices, start, length, MeshUpdateFlags.Default);

        public void SetVertices(Vector3[] inVertices, int start, int length, MeshUpdateFlags flags)
        {
            if (inVertices == null)
                throw new ArgumentNullException(nameof(inVertices));

            CheckRange(start, length, inVertices.Length, nameof(inVertices));
            WriteStream(VertexAttribute.Position, new ReadOnlySpan<Vector3>(inVertices, start, length), true, flags);
        }

        public void SetVertices(List<Vector3> inVertices) => SetVertices(inVertices, 0, inVertices?.Count ?? 0);

        public void SetVertices(List<Vector3> inVertices, int start, int length) =>
            SetVertices(inVertices, start, length, MeshUpdateFlags.Default);

        public void SetVertices(List<Vector3> inVertices, int start, int length, MeshUpdateFlags flags)
        {
            if (inVertices == null)
                throw new ArgumentNullException(nameof(inVertices));

            CheckRange(start, length, inVertices.Count, nameof(inVertices));
            WriteStream(VertexAttribute.Position, NowMeshData.AsSpan(inVertices, start, length), true, flags);
        }

        public void SetNormals(Vector3[] inNormals) => SetNormals(inNormals, 0, inNormals?.Length ?? 0);

        public void SetNormals(Vector3[] inNormals, int start, int length) =>
            SetNormals(inNormals, start, length, MeshUpdateFlags.Default);

        public void SetNormals(Vector3[] inNormals, int start, int length, MeshUpdateFlags flags)
        {
            if (inNormals == null)
                throw new ArgumentNullException(nameof(inNormals));

            CheckRange(start, length, inNormals.Length, nameof(inNormals));
            WriteStream(VertexAttribute.Normal, new ReadOnlySpan<Vector3>(inNormals, start, length), false, flags);
        }

        public void SetNormals(List<Vector3> inNormals) => SetNormals(inNormals, 0, inNormals?.Count ?? 0);

        public void SetNormals(List<Vector3> inNormals, int start, int length) =>
            SetNormals(inNormals, start, length, MeshUpdateFlags.Default);

        public void SetNormals(List<Vector3> inNormals, int start, int length, MeshUpdateFlags flags)
        {
            if (inNormals == null)
                throw new ArgumentNullException(nameof(inNormals));

            CheckRange(start, length, inNormals.Count, nameof(inNormals));
            WriteStream(VertexAttribute.Normal, NowMeshData.AsSpan(inNormals, start, length), false, flags);
        }

        public void SetTangents(Vector4[] inTangents) => SetTangents(inTangents, 0, inTangents?.Length ?? 0);

        public void SetTangents(Vector4[] inTangents, int start, int length) =>
            SetTangents(inTangents, start, length, MeshUpdateFlags.Default);

        public void SetTangents(Vector4[] inTangents, int start, int length, MeshUpdateFlags flags)
        {
            if (inTangents == null)
                throw new ArgumentNullException(nameof(inTangents));

            CheckRange(start, length, inTangents.Length, nameof(inTangents));
            WriteStream(VertexAttribute.Tangent, new ReadOnlySpan<Vector4>(inTangents, start, length), false, flags);
        }

        public void SetTangents(List<Vector4> inTangents) => SetTangents(inTangents, 0, inTangents?.Count ?? 0);

        public void SetTangents(List<Vector4> inTangents, int start, int length) =>
            SetTangents(inTangents, start, length, MeshUpdateFlags.Default);

        public void SetTangents(List<Vector4> inTangents, int start, int length, MeshUpdateFlags flags)
        {
            if (inTangents == null)
                throw new ArgumentNullException(nameof(inTangents));

            CheckRange(start, length, inTangents.Count, nameof(inTangents));
            WriteStream(VertexAttribute.Tangent, NowMeshData.AsSpan(inTangents, start, length), false, flags);
        }

        public void SetColors(Color[] inColors) => SetColors(inColors, 0, inColors?.Length ?? 0);

        public void SetColors(Color[] inColors, int start, int length) =>
            SetColors(inColors, start, length, MeshUpdateFlags.Default);

        public void SetColors(Color[] inColors, int start, int length, MeshUpdateFlags flags)
        {
            if (inColors == null)
                throw new ArgumentNullException(nameof(inColors));

            CheckRange(start, length, inColors.Length, nameof(inColors));
            WriteStream(VertexAttribute.Color, new ReadOnlySpan<Color>(inColors, start, length), false, flags);
        }

        public void SetColors(List<Color> inColors) => SetColors(inColors, 0, inColors?.Count ?? 0);

        public void SetColors(List<Color> inColors, int start, int length) =>
            SetColors(inColors, start, length, MeshUpdateFlags.Default);

        public void SetColors(List<Color> inColors, int start, int length, MeshUpdateFlags flags)
        {
            if (inColors == null)
                throw new ArgumentNullException(nameof(inColors));

            CheckRange(start, length, inColors.Count, nameof(inColors));
            WriteStream(VertexAttribute.Color, NowMeshData.AsSpan(inColors, start, length), false, flags);
        }

        public void SetColors(Color32[] inColors) => SetColors(inColors, 0, inColors?.Length ?? 0);

        public void SetColors(Color32[] inColors, int start, int length) =>
            SetColors(inColors, start, length, MeshUpdateFlags.Default);

        public void SetColors(Color32[] inColors, int start, int length, MeshUpdateFlags flags)
        {
            if (inColors == null)
                throw new ArgumentNullException(nameof(inColors));

            CheckRange(start, length, inColors.Length, nameof(inColors));
            WriteStream(VertexAttribute.Color, new ReadOnlySpan<Color32>(inColors, start, length), false, flags);
        }

        public void SetColors(List<Color32> inColors) => SetColors(inColors, 0, inColors?.Count ?? 0);

        public void SetColors(List<Color32> inColors, int start, int length) =>
            SetColors(inColors, start, length, MeshUpdateFlags.Default);

        public void SetColors(List<Color32> inColors, int start, int length, MeshUpdateFlags flags)
        {
            if (inColors == null)
                throw new ArgumentNullException(nameof(inColors));

            CheckRange(start, length, inColors.Count, nameof(inColors));
            WriteStream(VertexAttribute.Color, NowMeshData.AsSpan(inColors, start, length), false, flags);
        }

        public void SetUVs(int channel, Vector2[] uvs) => SetUVs(channel, uvs, 0, uvs?.Length ?? 0, MeshUpdateFlags.Default);

        public void SetUVs(int channel, Vector2[] uvs, int start, int length) =>
            SetUVs(channel, uvs, start, length, MeshUpdateFlags.Default);

        public void SetUVs(int channel, Vector2[] uvs, int start, int length, MeshUpdateFlags flags)
        {
            if (uvs == null)
                throw new ArgumentNullException(nameof(uvs));

            CheckRange(start, length, uvs.Length, nameof(uvs));
            WriteStream(UVAttribute(channel), new ReadOnlySpan<Vector2>(uvs, start, length), false, flags);
        }

        public void SetUVs(int channel, List<Vector2> uvs) => SetUVs(channel, uvs, 0, uvs?.Count ?? 0, MeshUpdateFlags.Default);

        public void SetUVs(int channel, List<Vector2> uvs, int start, int length) =>
            SetUVs(channel, uvs, start, length, MeshUpdateFlags.Default);

        public void SetUVs(int channel, List<Vector2> uvs, int start, int length, MeshUpdateFlags flags)
        {
            if (uvs == null)
                throw new ArgumentNullException(nameof(uvs));

            CheckRange(start, length, uvs.Count, nameof(uvs));
            WriteStream(UVAttribute(channel), NowMeshData.AsSpan(uvs, start, length), false, flags);
        }

        public void SetUVs(int channel, Vector3[] uvs) => SetUVs(channel, uvs, 0, uvs?.Length ?? 0, MeshUpdateFlags.Default);

        public void SetUVs(int channel, Vector3[] uvs, int start, int length) =>
            SetUVs(channel, uvs, start, length, MeshUpdateFlags.Default);

        public void SetUVs(int channel, Vector3[] uvs, int start, int length, MeshUpdateFlags flags)
        {
            if (uvs == null)
                throw new ArgumentNullException(nameof(uvs));

            CheckRange(start, length, uvs.Length, nameof(uvs));
            WriteStream(UVAttribute(channel), new ReadOnlySpan<Vector3>(uvs, start, length), false, flags);
        }

        public void SetUVs(int channel, List<Vector3> uvs) => SetUVs(channel, uvs, 0, uvs?.Count ?? 0, MeshUpdateFlags.Default);

        public void SetUVs(int channel, List<Vector3> uvs, int start, int length) =>
            SetUVs(channel, uvs, start, length, MeshUpdateFlags.Default);

        public void SetUVs(int channel, List<Vector3> uvs, int start, int length, MeshUpdateFlags flags)
        {
            if (uvs == null)
                throw new ArgumentNullException(nameof(uvs));

            CheckRange(start, length, uvs.Count, nameof(uvs));
            WriteStream(UVAttribute(channel), NowMeshData.AsSpan(uvs, start, length), false, flags);
        }

        public void SetUVs(int channel, Vector4[] uvs) => SetUVs(channel, uvs, 0, uvs?.Length ?? 0, MeshUpdateFlags.Default);

        public void SetUVs(int channel, Vector4[] uvs, int start, int length) =>
            SetUVs(channel, uvs, start, length, MeshUpdateFlags.Default);

        public void SetUVs(int channel, Vector4[] uvs, int start, int length, MeshUpdateFlags flags)
        {
            if (uvs == null)
                throw new ArgumentNullException(nameof(uvs));

            CheckRange(start, length, uvs.Length, nameof(uvs));
            WriteStream(UVAttribute(channel), new ReadOnlySpan<Vector4>(uvs, start, length), false, flags);
        }

        public void SetUVs(int channel, List<Vector4> uvs) => SetUVs(channel, uvs, 0, uvs?.Count ?? 0, MeshUpdateFlags.Default);

        public void SetUVs(int channel, List<Vector4> uvs, int start, int length) =>
            SetUVs(channel, uvs, start, length, MeshUpdateFlags.Default);

        public void SetUVs(int channel, List<Vector4> uvs, int start, int length, MeshUpdateFlags flags)
        {
            if (uvs == null)
                throw new ArgumentNullException(nameof(uvs));

            CheckRange(start, length, uvs.Count, nameof(uvs));
            WriteStream(UVAttribute(channel), NowMeshData.AsSpan(uvs, start, length), false, flags);
        }

        /// <summary>Copies one channel into its stream, switching the mesh out of interleaved storage.</summary>
        void WriteStream<T>(VertexAttribute attribute, ReadOnlySpan<T> source, bool definesVertexCount, MeshUpdateFlags flags) where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            ref NowMeshStream stream = ref data.streams[(int)attribute];

            NowMeshData.EnsureBytes(ref stream.bytes, source.Length * elementSize);
            NowMeshData.CopyElements(source, stream.bytes, 0);
            stream.elementSize = elementSize;
            stream.count = source.Length;

            // Writing a channel means the interleaved buffer is no longer the truth. Unity resolves this by rebuilding
            // its single vertex buffer; the shim just flips the storage shape, which is observationally the same for
            // every NowUI call site because both paths always start from a Clear.
            data.interleaved = false;

            if (definesVertexCount)
                data.vertexCount = source.Length;

            Touch();

            if (definesVertexCount)
                RecalculateBoundsUnless(flags);
        }

        // ---- index buffer ------------------------------------------------------------------------------------------

        /// <summary>Reserves an index buffer of <paramref name="indexCount"/> elements in <paramref name="format"/>.</summary>
        public void SetIndexBufferParams(int indexCount, IndexFormat format)
        {
            if (indexCount < 0)
                throw new ArgumentOutOfRangeException(nameof(indexCount));

            data.indexFormat = format;
            data.indexCount = indexCount;
            NowMeshData.EnsureBytes(ref data.indexBytes, indexCount * NowMeshData.IndexSize(format));
            Touch();
        }

        /// <summary>Uploads indices. <typeparamref name="T"/> must match the declared <see cref="indexFormat"/> width.</summary>
        public void SetIndexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            CheckRange(dataStart, count, data.Length, nameof(data));
            WriteIndexBuffer(new ReadOnlySpan<T>(data, dataStart, count), meshBufferStart);
        }

        /// <inheritdoc cref="SetIndexBufferData{T}(T[], int, int, int, MeshUpdateFlags)"/>
        public void SetIndexBufferData<T>(List<T> data, int dataStart, int meshBufferStart, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            CheckRange(dataStart, count, data.Count, nameof(data));
            WriteIndexBuffer(NowMeshData.AsSpan(data, dataStart, count), meshBufferStart);
        }

        /// <inheritdoc cref="SetIndexBufferData{T}(T[], int, int, int, MeshUpdateFlags)"/>
        public void SetIndexBufferData<T>(NativeArray<T> data, int dataStart, int meshBufferStart, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            CheckRange(dataStart, count, data.Length, nameof(data));
            WriteIndexBuffer(data.AsReadOnlySpan().Slice(dataStart, count), meshBufferStart);
        }

        void WriteIndexBuffer<T>(ReadOnlySpan<T> source, int meshBufferStart) where T : struct
        {
            if (meshBufferStart < 0)
                throw new ArgumentOutOfRangeException(nameof(meshBufferStart));

            int elementSize = Unsafe.SizeOf<T>();
            int byteOffset = meshBufferStart * NowMeshData.IndexSize(data.indexFormat);
            NowMeshData.EnsureBytes(ref data.indexBytes, byteOffset + source.Length * elementSize);
            NowMeshData.CopyElements(source, data.indexBytes, byteOffset);
            Touch();
        }

        // ---- sub-meshes --------------------------------------------------------------------------------------------

        public void SetSubMesh(int index, SubMeshDescriptor desc, MeshUpdateFlags flags = MeshUpdateFlags.Default)
        {
            if (index < 0 || index >= data.subMeshCount)
                throw new ArgumentOutOfRangeException(nameof(index), "Specified submesh index is out of range.");

            data.subMeshes[index] = desc;
            Touch();
        }

        public void SetSubMeshes(SubMeshDescriptor[] desc, int start, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default)
        {
            if (desc == null)
                throw new ArgumentNullException(nameof(desc));

            CheckRange(start, count, desc.Length, nameof(desc));

            data.EnsureSubMeshCapacity(count);
            Array.Copy(desc, start, data.subMeshes, 0, count);
            data.subMeshCount = count;
            Touch();
        }

        public SubMeshDescriptor GetSubMesh(int index)
        {
            if (index < 0 || index >= data.subMeshCount)
                throw new ArgumentOutOfRangeException(nameof(index), "Specified submesh index is out of range.");

            return data.subMeshes[index];
        }

        /// <summary>
        /// Replaces one sub-mesh's index list.
        /// </summary>
        /// <remarks>
        /// This is the cold, convenience path (editor previews and tooling), not the hot upload path, so it rebuilds the
        /// flat index buffer through a temporary list rather than complicating <see cref="SetIndexBufferData{T}(T[], int, int, int, MeshUpdateFlags)"/>.
        /// Setting sub-mesh 0 on a fresh mesh grows the table to one entry, which is how the Unity idiom
        /// <c>SetTriangles(t, 0)</c> works without an explicit <see cref="subMeshCount"/> assignment.
        /// </remarks>
        public void SetTriangles(int[] triangles, int submesh, bool calculateBounds = true)
        {
            if (triangles == null)
                throw new ArgumentNullException(nameof(triangles));
            if (submesh < 0 || submesh > data.subMeshCount)
                throw new ArgumentOutOfRangeException(nameof(submesh), "Specified submesh index is out of range.");

            if (submesh == data.subMeshCount)
                subMeshCount = submesh + 1;

            if (data.indexFormat == IndexFormat.UInt16)
            {
                for (int i = 0; i < triangles.Length; ++i)
                {
                    if ((uint)triangles[i] > ushort.MaxValue)
                        throw new ArgumentException(
                            "Mesh.indexFormat is UInt16 but an index does not fit in 16 bits. Set indexFormat to UInt32 first.",
                            nameof(triangles));
                }
            }

            int total = data.subMeshCount;
            var rebuilt = new List<int>();
            var descriptors = new SubMeshDescriptor[total];

            for (int s = 0; s < total; ++s)
            {
                var descriptor = data.subMeshes[s];
                int indexStart = rebuilt.Count;

                if (s == submesh)
                {
                    for (int i = 0; i < triangles.Length; ++i)
                        rebuilt.Add(triangles[i]);
                }
                else
                {
                    for (int i = 0; i < descriptor.indexCount; ++i)
                        rebuilt.Add(ReadIndex(descriptor.indexStart + i));
                }

                descriptor.indexStart = indexStart;
                descriptor.indexCount = rebuilt.Count - indexStart;
                descriptor.topology = s == submesh ? MeshTopology.Triangles : descriptor.topology;
                descriptors[s] = descriptor;
            }

            int indexSize = NowMeshData.IndexSize(data.indexFormat);
            NowMeshData.EnsureBytes(ref data.indexBytes, rebuilt.Count * indexSize);

            for (int i = 0; i < rebuilt.Count; ++i)
                WriteIndex(i, rebuilt[i]);

            data.indexCount = rebuilt.Count;
            Array.Copy(descriptors, data.subMeshes, total);
            Touch();

            if (calculateBounds)
                RecalculateBounds();
        }

        public int[] GetTriangles(int submesh)
        {
            var descriptor = GetSubMesh(submesh);
            var result = new int[descriptor.indexCount];

            for (int i = 0; i < descriptor.indexCount; ++i)
                result[i] = ReadIndex(descriptor.indexStart + i) + descriptor.baseVertex;

            return result;
        }

        /// <summary>
        /// Appends one sub-mesh's indices to <paramref name="triangles"/> after clearing it.
        /// </summary>
        /// <remarks>
        /// The list is cleared and filled with exactly <c>indexCount</c> entries — Unity's semantics, which
        /// <c>NowEffectsMesh.Deform</c> relies on when it early-outs on <c>_triangles.Count == 0</c>.
        /// <paramref name="submesh"/> out of range clears the list and returns instead of throwing, matching Unity's
        /// tolerant read-back path.
        /// </remarks>
        public void GetTriangles(List<int> triangles, int submesh)
        {
            if (triangles == null)
                throw new ArgumentNullException(nameof(triangles));

            triangles.Clear();

            if (submesh < 0 || submesh >= data.subMeshCount)
                return;

            var descriptor = data.subMeshes[submesh];
            for (int i = 0; i < descriptor.indexCount; ++i)
                triangles.Add(ReadIndex(descriptor.indexStart + i) + descriptor.baseVertex);
        }

        // ---- read-back ---------------------------------------------------------------------------------------------

        public void GetVertices(List<Vector3> vertices) => ReadVector3Channel(VertexAttribute.Position, vertices);

        public void GetNormals(List<Vector3> normals) => ReadVector3Channel(VertexAttribute.Normal, normals);

        public void GetTangents(List<Vector4> tangents) => ReadVector4Channel(VertexAttribute.Tangent, tangents);

        public void GetUVs(int channel, List<Vector2> uvs) => ReadVector2Channel(UVAttribute(channel), uvs);

        public void GetUVs(int channel, List<Vector3> uvs) => ReadVector3Channel(UVAttribute(channel), uvs);

        public void GetUVs(int channel, List<Vector4> uvs) => ReadVector4Channel(UVAttribute(channel), uvs);

        public void GetColors(List<Color> colors)
        {
            if (colors == null)
                throw new ArgumentNullException(nameof(colors));

            colors.Clear();

            if (!TryLocateChannel(VertexAttribute.Color, out var bytes, out int offset, out int stride, out var format, out int dimension, out int count))
                return;

            for (int i = 0; i < count; ++i)
            {
                var v = ReadVector4(bytes, offset + i * stride, format, dimension);
                colors.Add(new Color(v.x, v.y, v.z, v.w));
            }
        }

        public void GetColors(List<Color32> colors)
        {
            if (colors == null)
                throw new ArgumentNullException(nameof(colors));

            colors.Clear();

            if (!TryLocateChannel(VertexAttribute.Color, out var bytes, out int offset, out int stride, out var format, out int dimension, out int count))
                return;

            for (int i = 0; i < count; ++i)
            {
                var v = ReadVector4(bytes, offset + i * stride, format, dimension);
                // Through the Color -> Color32 operator, so the quantisation matches everything else in the shim.
                colors.Add((Color32)new Color(v.x, v.y, v.z, v.w));
            }
        }

        /// <summary>
        /// Recomputes <see cref="bounds"/> from the position channel.
        /// </summary>
        /// <remarks>An empty mesh gets a zero bounds at the origin, as Unity's does.</remarks>
        public void RecalculateBounds()
        {
            if (!TryLocateChannel(VertexAttribute.Position, out var bytes, out int offset, out int stride, out var format, out int dimension, out int count) || count == 0)
            {
                m_Bounds = default;
                Touch();
                return;
            }

            var first = ReadVector4(bytes, offset, format, dimension);
            float minX = first.x, minY = first.y, minZ = first.z;
            float maxX = minX, maxY = minY, maxZ = minZ;

            for (int i = 1; i < count; ++i)
            {
                var v = ReadVector4(bytes, offset + i * stride, format, dimension);

                if (v.x < minX) minX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.z < minZ) minZ = v.z;
                if (v.x > maxX) maxX = v.x;
                if (v.y > maxY) maxY = v.y;
                if (v.z > maxZ) maxZ = v.z;
            }

            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
            m_Bounds = bounds;
            Touch();
        }

        // ---- internals ---------------------------------------------------------------------------------------------

        void ReadVector2Channel(VertexAttribute attribute, List<Vector2> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();

            if (!TryLocateChannel(attribute, out var bytes, out int offset, out int stride, out var format, out int dimension, out int count))
                return;

            for (int i = 0; i < count; ++i)
            {
                var v = ReadVector4(bytes, offset + i * stride, format, dimension);
                destination.Add(new Vector2(v.x, v.y));
            }
        }

        void ReadVector3Channel(VertexAttribute attribute, List<Vector3> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();

            if (!TryLocateChannel(attribute, out var bytes, out int offset, out int stride, out var format, out int dimension, out int count))
                return;

            for (int i = 0; i < count; ++i)
            {
                var v = ReadVector4(bytes, offset + i * stride, format, dimension);
                destination.Add(new Vector3(v.x, v.y, v.z));
            }
        }

        void ReadVector4Channel(VertexAttribute attribute, List<Vector4> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();

            if (!TryLocateChannel(attribute, out var bytes, out int offset, out int stride, out var format, out int dimension, out int count))
                return;

            for (int i = 0; i < count; ++i)
                destination.Add(ReadVector4(bytes, offset + i * stride, format, dimension));
        }

        /// <summary>
        /// Resolves where one attribute's data lives, in either storage shape.
        /// </summary>
        /// <remarks>
        /// For the interleaved shape the stride is the whole vertex and the offset comes from the descriptor table —
        /// this is the de-interleaving step. For the stream shape each element is packed, so the stride <em>is</em> the
        /// element size, and the element size is what tells the dimension apart (see <see cref="NowMeshStream"/>).
        /// </remarks>
        bool TryLocateChannel(VertexAttribute attribute, out byte[] bytes, out int byteOffset, out int stride, out VertexAttributeFormat format, out int dimension, out int count)
        {
            if (data.interleaved)
            {
                if (data.vertexCount > 0 &&
                    data.vertexBytes != null &&
                    data.TryGetAttribute(attribute, out byteOffset, out format, out dimension))
                {
                    bytes = data.vertexBytes;
                    stride = data.vertexStride;
                    count = data.vertexCount;
                    return true;
                }
            }
            else
            {
                ref NowMeshStream stream = ref data.streams[(int)attribute];
                if (stream.count > 0 && stream.bytes != null)
                {
                    bytes = stream.bytes;
                    byteOffset = 0;
                    stride = stream.elementSize;
                    count = stream.count;

                    if (attribute == VertexAttribute.Color && stream.elementSize == 4)
                    {
                        // A four-byte colour channel is a Color32, not a single float.
                        format = VertexAttributeFormat.UNorm8;
                        dimension = 4;
                    }
                    else
                    {
                        format = VertexAttributeFormat.Float32;
                        dimension = stream.elementSize / 4;
                    }

                    return true;
                }
            }

            bytes = null;
            byteOffset = 0;
            stride = 0;
            format = VertexAttributeFormat.Float32;
            dimension = 0;
            count = 0;
            return false;
        }

        /// <summary>
        /// Widens one stored element to a <see cref="Vector4"/>. Components the channel does not carry read as 0, which
        /// is what makes <c>GetUVs(0, List&lt;Vector4&gt;)</c> over a two-component channel behave as Unity's does.
        /// </summary>
        static Vector4 ReadVector4(byte[] bytes, int offset, VertexAttributeFormat format, int dimension)
        {
            if (format == VertexAttributeFormat.UNorm8)
            {
                float x = dimension > 0 ? bytes[offset] / 255f : 0f;
                float y = dimension > 1 ? bytes[offset + 1] / 255f : 0f;
                float z = dimension > 2 ? bytes[offset + 2] / 255f : 0f;
                float w = dimension > 3 ? bytes[offset + 3] / 255f : 0f;
                return new Vector4(x, y, z, w);
            }

            // Float32 is the only other format NowUI's layouts declare (design §3.2); anything else would need a
            // conversion table the core never exercises, so it is deliberately not guessed at here.
            return new Vector4(
                dimension > 0 ? ReadFloat(bytes, offset) : 0f,
                dimension > 1 ? ReadFloat(bytes, offset + 4) : 0f,
                dimension > 2 ? ReadFloat(bytes, offset + 8) : 0f,
                dimension > 3 ? ReadFloat(bytes, offset + 12) : 0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float ReadFloat(byte[] bytes, int offset) => Unsafe.ReadUnaligned<float>(ref bytes[offset]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int ReadIndex(int element)
        {
            if (data.indexBytes == null)
                return 0;

            return data.indexFormat == IndexFormat.UInt32
                ? Unsafe.ReadUnaligned<int>(ref data.indexBytes[element * 4])
                : Unsafe.ReadUnaligned<ushort>(ref data.indexBytes[element * 2]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void WriteIndex(int element, int value)
        {
            if (data.indexFormat == IndexFormat.UInt32)
                Unsafe.WriteUnaligned(ref data.indexBytes[element * 4], value);
            else
                Unsafe.WriteUnaligned(ref data.indexBytes[element * 2], (ushort)value);
        }

        /// <summary>Maps a UV channel index onto its contiguous <see cref="VertexAttribute"/>.</summary>
        static VertexAttribute UVAttribute(int channel)
        {
            if (channel < 0 || channel > MaxUVChannel)
                throw new ArgumentOutOfRangeException(nameof(channel), "The uv channel must be in [0..7].");

            // TexCoord0..TexCoord7 are contiguous at 4..11, which is what makes this addition legal.
            return VertexAttribute.TexCoord0 + channel;
        }

        void Touch()
        {
            unchecked { ++data.version; }
        }

        /// <summary>Honours <see cref="MeshUpdateFlags.DontRecalculateBounds"/>; the default is to recalculate, as Unity does.</summary>
        void RecalculateBoundsUnless(MeshUpdateFlags flags)
        {
            if ((flags & MeshUpdateFlags.DontRecalculateBounds) == 0)
                RecalculateBounds();
        }

        static void CheckRange(int start, int length, int available, string parameterName)
        {
            if (start < 0 || length < 0 || start + length > available)
                throw new ArgumentOutOfRangeException(parameterName, $"Range [{start}, {start + length}) is outside the {available} available elements.");
        }

        internal override void OnDestroyResources()
        {
            NowUI.Engine.NowRuntime.backend.ReleaseMesh(this);
            backendId = 0;
        }

        internal override Object CloneForInstantiate()
        {
            var clone = new Mesh { name = name, hideFlags = hideFlags, m_Bounds = m_Bounds };
            var source = data;
            var target = clone.data;

            target.interleaved = source.interleaved;
            target.vertexStride = source.vertexStride;
            target.vertexCount = source.vertexCount;
            target.indexFormat = source.indexFormat;
            target.indexCount = source.indexCount;
            target.layoutCount = source.layoutCount;

            if (source.layoutCount > 0)
            {
                target.layout = new VertexAttributeDescriptor[source.layoutCount];
                Array.Copy(source.layout, target.layout, source.layoutCount);
            }

            if (source.vertexBytes != null)
            {
                int used = source.vertexCount * source.vertexStride;
                NowMeshData.EnsureBytes(ref target.vertexBytes, used);
                Array.Copy(source.vertexBytes, target.vertexBytes, used);
            }

            if (source.indexBytes != null)
            {
                int used = source.indexCount * NowMeshData.IndexSize(source.indexFormat);
                NowMeshData.EnsureBytes(ref target.indexBytes, used);
                Array.Copy(source.indexBytes, target.indexBytes, used);
            }

            for (int i = 0; i < source.streams.Length; ++i)
            {
                ref NowMeshStream from = ref source.streams[i];
                if (from.count == 0 || from.bytes == null)
                    continue;

                ref NowMeshStream to = ref target.streams[i];
                int used = from.count * from.elementSize;
                NowMeshData.EnsureBytes(ref to.bytes, used);
                Array.Copy(from.bytes, to.bytes, used);
                to.elementSize = from.elementSize;
                to.count = from.count;
            }

            target.EnsureSubMeshCapacity(source.subMeshCount);
            Array.Copy(source.subMeshes, target.subMeshes, source.subMeshCount);
            target.subMeshCount = source.subMeshCount;

            return clone;
        }
    }
}
