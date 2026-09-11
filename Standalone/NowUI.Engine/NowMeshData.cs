// The CPU-side store behind the shim's UnityEngine.Mesh handle. Not a Unity type: this is the NowUI.Engine helper
// listed in StandaloneCoreDesign.md §3.10, governed by §3.5 (Mesh) and §1.2 (the allocation rule).
//
// A Mesh in the standalone build is a handle plus one of these. Two storage shapes are supported because NowUI uses
// both: an interleaved vertex buffer described by a VertexAttributeDescriptor[] layout (Now.cs:1886-1894,
// NowMesh.cs:1972-1981), and a per-attribute stream fallback (Now.cs:1898-1906, NowMesh.cs:1983-1991). Everything is
// held as byte[] so a backend can hand the bytes straight to bufferData without a second copy, and so one code path
// serves every element type.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine
{
    /// <summary>
    /// One non-interleaved vertex channel: its raw bytes, the size of one element, and how many elements are live
    /// (design §3.10). <c>bytes.Length</c> is capacity and is never shrunk — see <see cref="NowMeshData"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="elementSize"/> doubles as the channel's dimension tag on read-back: within a single attribute it is
    /// unambiguous (8 bytes is a <c>Vector2</c> UV, 16 a <c>Vector4</c>; 4 bytes is a <c>Color32</c>, 16 a
    /// <c>Color</c>), which is why the struct needs no separate format field.
    /// </remarks>
    public struct NowMeshStream
    {
        public byte[] bytes;
        public int elementSize;
        public int count;
    }

    /// <summary>
    /// The retained CPU copy of a mesh's vertex data, index data and sub-mesh table (design §3.10).
    /// </summary>
    /// <remarks>
    /// <b>Why copies.</b> <c>Now.UploadCapturedMeshes</c> and <c>NowMesh.UploadMesh</c> hand the same static scratch
    /// lists to every draw list in the frame, so a Mesh that merely retained the caller's array would alias the next
    /// draw list's contents. Unity copies at call time; so does this (design §3.5).
    /// <para>
    /// <b>Allocation rule (§1.2).</b> Every buffer here grows and never shrinks, and <see cref="Clear"/> only zeroes
    /// counts. After the first few frames a steady-state upload writes into buffers that are already large enough and
    /// allocates nothing.
    /// </para>
    /// </remarks>
    public sealed class NowMeshData
    {
        /// <summary>
        /// One stream slot per <see cref="VertexAttribute"/> (Position..BlendIndices), so a channel is indexed by the
        /// attribute value itself.
        /// </summary>
        /// <remarks>
        /// Design §3.10 writes this as "<c>NowMeshStream[] streams (8)</c>", counting the eight UV channels NowUI's
        /// fallback path uses. Eight slots cannot also hold Position — and §3.5 requires <c>SetNormals</c>,
        /// <c>SetTangents</c> and <c>SetColors</c> as well — so the array is widened to the full attribute range and
        /// indexed by <see cref="VertexAttribute"/>. The eight UV streams are slots 4..11.
        /// </remarks>
        public const int StreamCount = 14;

        /// <summary>True when vertices live in <see cref="vertexBytes"/> under <see cref="layout"/>.</summary>
        public bool interleaved;

        /// <summary>The interleaved layout, as handed to <c>Mesh.SetVertexBufferParams</c>. Null in stream mode.</summary>
        public VertexAttributeDescriptor[] layout;

        /// <summary>Number of live entries in <see cref="layout"/> (the array itself is capacity).</summary>
        public int layoutCount;

        /// <summary>The interleaved vertex buffer. Capacity, not size: <see cref="vertexCount"/> says what is live.</summary>
        public byte[] vertexBytes;

        /// <summary>Bytes per vertex in <see cref="vertexBytes"/>, summed over <see cref="layout"/>.</summary>
        public int vertexStride;

        /// <summary>Per-attribute streams, indexed by <see cref="VertexAttribute"/>. Used when <see cref="interleaved"/> is false.</summary>
        public readonly NowMeshStream[] streams = new NowMeshStream[StreamCount];

        /// <summary>The index buffer, in <see cref="indexFormat"/> elements.</summary>
        public byte[] indexBytes;

        /// <summary>Width of one index. NowUI switches to 32-bit past 65535 vertices.</summary>
        public IndexFormat indexFormat = IndexFormat.UInt16;

        /// <summary>Live indices in <see cref="indexBytes"/>.</summary>
        public int indexCount;

        /// <summary>Live vertices, in whichever storage shape is active.</summary>
        public int vertexCount;

        /// <summary>Sub-mesh table. Capacity, not size: <see cref="subMeshCount"/> says what is live.</summary>
        public SubMeshDescriptor[] subMeshes = Array.Empty<SubMeshDescriptor>();

        /// <summary>
        /// Live entries in <see cref="subMeshes"/>.
        /// </summary>
        /// <remarks>
        /// Design §3.10 lists only the array. A separate count is required by the allocation rule: <c>Mesh.Clear</c>
        /// must drop the sub-meshes without releasing the array it would immediately need again.
        /// </remarks>
        public int subMeshCount;

        /// <summary>Bumped on every CPU-side change, so a backend can re-upload only on a version mismatch (§3.5).</summary>
        public uint version;

        /// <summary>Bytes per index for a format.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexSize(IndexFormat format) => format == IndexFormat.UInt32 ? 4 : 2;

        /// <summary>
        /// Drops every live count, keeping capacity. <paramref name="keepVertexLayout"/> false also forgets the
        /// interleaved layout, matching <c>Mesh.Clear(false)</c>.
        /// </summary>
        public void Clear(bool keepVertexLayout)
        {
            vertexCount = 0;
            indexCount = 0;
            subMeshCount = 0;

            for (int i = 0; i < streams.Length; ++i)
                streams[i].count = 0;

            if (!keepVertexLayout)
            {
                interleaved = false;
                layoutCount = 0;
                vertexStride = 0;
            }

            unchecked { ++version; }
        }

        /// <summary>Grows a byte buffer to at least <paramref name="needed"/> bytes, preserving nothing.</summary>
        /// <remarks>
        /// Contents are deliberately not preserved: every caller overwrites the region it grew for. Growth is
        /// geometric so a mesh that creeps upward in size does not reallocate every frame.
        /// </remarks>
        public static void EnsureBytes(ref byte[] buffer, int needed)
        {
            if (needed <= 0)
            {
                buffer ??= Array.Empty<byte>();
                return;
            }

            if (buffer != null && buffer.Length >= needed)
                return;

            int capacity = buffer == null || buffer.Length == 0 ? 64 : buffer.Length;
            while (capacity < needed)
                capacity *= 2;

            buffer = new byte[capacity];
        }

        /// <summary>Grows the sub-mesh table to at least <paramref name="needed"/> entries, preserving what is there.</summary>
        public void EnsureSubMeshCapacity(int needed)
        {
            if (subMeshes.Length >= needed)
                return;

            int capacity = subMeshes.Length == 0 ? 4 : subMeshes.Length;
            while (capacity < needed)
                capacity *= 2;

            Array.Resize(ref subMeshes, capacity);
        }

        /// <summary>Replaces the interleaved layout and recomputes <see cref="vertexStride"/>.</summary>
        public void SetLayout(VertexAttributeDescriptor[] attributes, int count)
        {
            if (layout == null || layout.Length < count)
                layout = new VertexAttributeDescriptor[count < 4 ? 4 : count];

            int stride = 0;
            for (int i = 0; i < count; ++i)
            {
                layout[i] = attributes[i];
                // NowUI declares a single stream (0), so the stride is the sum of every channel. A multi-stream layout
                // would need per-stream strides; the design pins the shim to what NowUI emits.
                stride += attributes[i].byteSize;
            }

            layoutCount = count;
            vertexStride = stride;
            interleaved = true;
        }

        /// <summary>
        /// Finds an attribute in the interleaved layout, returning its byte offset within a vertex.
        /// </summary>
        public bool TryGetAttribute(VertexAttribute attribute, out int byteOffset, out VertexAttributeFormat format, out int dimension)
        {
            int offset = 0;
            for (int i = 0; i < layoutCount; ++i)
            {
                ref readonly var descriptor = ref layout[i];
                if (descriptor.attribute == attribute)
                {
                    byteOffset = offset;
                    format = descriptor.format;
                    dimension = descriptor.dimension;
                    return true;
                }

                offset += descriptor.byteSize;
            }

            byteOffset = 0;
            format = VertexAttributeFormat.Float32;
            dimension = 0;
            return false;
        }

        // ---- raw element copies -----------------------------------------------------------------------------------
        //
        // These take `where T : struct` (Unity's constraint) rather than `unmanaged`, so MemoryMarshal.AsBytes is out.
        // Unsafe.CopyBlockUnaligned over a reinterpreted managed reference does the same job for the blittable structs
        // NowUI actually passes (Vector2/3/4, Color32, NowRenderVertex, NowCanvasVertex, ushort, int) and allocates
        // nothing. A non-blittable T would copy its field bits, which is exactly what Unity's own overload rejects at
        // runtime; the shim does not add that check because no core call site can reach it.

        /// <summary>Copies <paramref name="source"/> into <paramref name="destination"/> at a byte offset.</summary>
        public static void CopyElements<T>(ReadOnlySpan<T> source, byte[] destination, int destinationByteOffset) where T : struct
        {
            if (source.Length == 0)
                return;

            int size = Unsafe.SizeOf<T>();
            ref T first = ref MemoryMarshal.GetReference(source);
            ref byte from = ref Unsafe.As<T, byte>(ref first);
            ref byte to = ref destination[destinationByteOffset];
            Unsafe.CopyBlockUnaligned(ref to, ref from, checked((uint)(source.Length * size)));
        }

        /// <summary>Reads one element out of a byte buffer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadElement<T>(byte[] source, int byteOffset) where T : struct =>
            Unsafe.ReadUnaligned<T>(ref source[byteOffset]);

        /// <summary>Views the live part of a list as a span without copying (nothing is allocated).</summary>
        public static ReadOnlySpan<T> AsSpan<T>(List<T> list, int start, int length)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            return CollectionsMarshal.AsSpan(list).Slice(start, length);
        }
    }
}
