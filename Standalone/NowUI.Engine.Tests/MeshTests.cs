// Tests for unit U8 — UnityEngine.Mesh, NowUI.Engine.NowMeshData, and the UnityEngine.Rendering structs
// SubMeshDescriptor / VertexAttributeDescriptor / RenderTargetIdentifier.
// Governed by StandaloneCoreDesign.md §3.5, §3.10, §1.2 (allocation rule) and §8 (U8's acceptance:
// set/get round trips for all 8 UV channels, interleaved de-interleave, zero-allocation steady state).
//
// The fixtures mirror NowMesh.RenderVertexLayout / NowMesh.CanvasVertexLayout (Assets/NowUI/Runtime/NowMesh.cs:185-226)
// and the upload sequence in NowMesh.UploadMesh (1956-2026) and Now.UploadCapturedMeshes (1871-1961), because those
// are the only shapes the standalone core actually produces.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NowUI.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class MeshTests
    {
        // ---- fixtures ------------------------------------------------------------------------------------------

        /// <summary>Mirrors NowUI's NowRenderVertex packing under NowMesh.RenderVertexLayout.</summary>
        [StructLayout(LayoutKind.Sequential)]
        struct RenderVertex
        {
            public Vector3 position;
            public Vector2 uv0;
            public Vector4 uv1;
            public Vector4 uv2;
            public Vector4 uv3;
            public Vector4 uv4;
            public Vector4 uv5;
            public Vector4 uv6;
            public Vector4 uv7;
        }

        /// <summary>Mirrors NowUI's NowCanvasVertex packing under NowMesh.CanvasVertexLayout.</summary>
        [StructLayout(LayoutKind.Sequential)]
        struct CanvasVertex
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector4 tangent;
            public Vector4 color;
            public Vector4 uv0;
            public Vector4 uv1;
            public Vector4 uv2;
            public Vector4 uv3;
        }

        static readonly VertexAttributeDescriptor[] RenderVertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord7, VertexAttributeFormat.Float32, 4),
        };

        static readonly VertexAttributeDescriptor[] CanvasVertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
        };

        static Vector3 Position(int i) => new Vector3(i, i * 2f, i * 3f);

        static Vector2 Uv0(int i) => new Vector2(i * 0.5f, i * 0.25f);

        static Vector4 Uv(int channel, int i) => new Vector4(channel * 100 + i, channel * 100 + i + 0.5f, -channel, i * 0.125f);

        // ---- VertexAttributeDescriptor -------------------------------------------------------------------------

        [Test]
        public void VertexAttributeDescriptorConstructorDefaultsMatchUnity()
        {
            // Supplying only the attribute must fill in Float32 / dimension 3 / stream 0.
            var descriptor = new VertexAttributeDescriptor(VertexAttribute.Position);

            Assert.That(descriptor.attribute, Is.EqualTo(VertexAttribute.Position));
            Assert.That(descriptor.format, Is.EqualTo(VertexAttributeFormat.Float32));
            Assert.That(descriptor.dimension, Is.EqualTo(3));
            Assert.That(descriptor.stream, Is.EqualTo(0));
        }

        [Test]
        public void DefaultVertexAttributeDescriptorIsZeroInitialised()
        {
            // `new S()` on a struct is zero-init and never reaches a constructor with all-optional parameters — the C#
            // rule, so this is Unity's behaviour too. A dimension of 0 here is correct, not a missing default.
            var descriptor = new VertexAttributeDescriptor();

            Assert.That(descriptor, Is.EqualTo(default(VertexAttributeDescriptor)));
            Assert.That(descriptor.dimension, Is.Zero);
            Assert.That(descriptor.byteSize, Is.Zero);
        }

        [Test]
        public void VertexAttributeDescriptorByteSizeFollowsFormatAndDimension()
        {
            Assert.That(new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3).byteSize, Is.EqualTo(12));
            Assert.That(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2).byteSize, Is.EqualTo(8));
            Assert.That(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4).byteSize, Is.EqualTo(4));
            Assert.That(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 4).byteSize, Is.EqualTo(8));
        }

        [Test]
        public void VertexAttributeDescriptorEqualityIsByValue()
        {
            var a = new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4);
            var b = new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4);
            var c = new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2);

            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void RenderVertexLayoutStrideMatchesTheStructItDescribes()
        {
            int stride = 0;
            foreach (var descriptor in RenderVertexLayout)
                stride += descriptor.byteSize;

            Assert.That(stride, Is.EqualTo(Marshal.SizeOf<RenderVertex>()));

            stride = 0;
            foreach (var descriptor in CanvasVertexLayout)
                stride += descriptor.byteSize;

            Assert.That(stride, Is.EqualTo(Marshal.SizeOf<CanvasVertex>()));
        }

        // ---- SubMeshDescriptor -----------------------------------------------------------------------------------

        [Test]
        public void SubMeshDescriptorConstructorLeavesVertexRangeAtZero()
        {
            var descriptor = new SubMeshDescriptor(12, 30);

            Assert.That(descriptor.indexStart, Is.EqualTo(12));
            Assert.That(descriptor.indexCount, Is.EqualTo(30));
            Assert.That(descriptor.topology, Is.EqualTo(MeshTopology.Triangles));
            Assert.That(descriptor.baseVertex, Is.EqualTo(0));
            Assert.That(descriptor.firstVertex, Is.EqualTo(0));
            Assert.That(descriptor.vertexCount, Is.EqualTo(0));
        }

        [Test]
        public void SubMeshDescriptorSupportsNowUIsObjectInitialiserShape()
        {
            // This is verbatim the shape NowMesh.UploadMesh (2020-2024) and Now.UploadCapturedMeshes (1938-1944) use.
            var descriptor = new SubMeshDescriptor(0, 96)
            {
                firstVertex = 4,
                vertexCount = 64,
            };

            Assert.That(descriptor.firstVertex, Is.EqualTo(4));
            Assert.That(descriptor.vertexCount, Is.EqualTo(64));
            Assert.That(descriptor.baseVertex, Is.EqualTo(0), "NowUI writes global indices, so baseVertex must stay 0.");
        }

        [Test]
        public void SubMeshDescriptorEqualityIsByValue()
        {
            var a = new SubMeshDescriptor(0, 6) { firstVertex = 1, vertexCount = 4 };
            var b = new SubMeshDescriptor(0, 6) { firstVertex = 1, vertexCount = 4 };
            var c = new SubMeshDescriptor(0, 6) { firstVertex = 2, vertexCount = 4 };

            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        // ---- RenderTargetIdentifier ------------------------------------------------------------------------------

        [Test]
        public void DefaultRenderTargetIdentifierMeansCurrentTarget()
        {
            RenderTargetIdentifier identifier = default;

            Assert.That(identifier.kind, Is.EqualTo(RenderTargetIdentifier.Kind.None));
            Assert.That(identifier, Is.EqualTo(default(RenderTargetIdentifier)));
        }

        [Test]
        public void RenderTargetIdentifierConvertsImplicitlyFromBuiltinAndNameID()
        {
            RenderTargetIdentifier builtin = BuiltinRenderTextureType.CameraTarget;
            RenderTargetIdentifier named = 4242;

            Assert.That(builtin.kind, Is.EqualTo(RenderTargetIdentifier.Kind.BuiltinType));
            Assert.That(builtin.builtin, Is.EqualTo(BuiltinRenderTextureType.CameraTarget));
            Assert.That(named.kind, Is.EqualTo(RenderTargetIdentifier.Kind.NameID));
            Assert.That(named.nameID, Is.EqualTo(4242));
            Assert.That(builtin == named, Is.False);
        }

        [Test]
        public void RenderTargetIdentifierRetargetsMipFaceAndSlice()
        {
            RenderTargetIdentifier source = BuiltinRenderTextureType.CameraTarget;
            var retargeted = new RenderTargetIdentifier(source, 3, CubemapFace.NegativeY, RenderTargetIdentifier.AllDepthSlices);

            Assert.That(retargeted.kind, Is.EqualTo(RenderTargetIdentifier.Kind.BuiltinType));
            Assert.That(retargeted.builtin, Is.EqualTo(BuiltinRenderTextureType.CameraTarget));
            Assert.That(retargeted.mipLevel, Is.EqualTo(3));
            Assert.That(retargeted.face, Is.EqualTo(CubemapFace.NegativeY));
            Assert.That(retargeted.depthSlice, Is.EqualTo(-1));
            Assert.That(retargeted == source, Is.False, "A different mip/face/slice is a different target.");
        }

        [Test]
        public void RenderTargetIdentifierEqualityIsByValue()
        {
            RenderTargetIdentifier a = 7;
            RenderTargetIdentifier b = 7;
            RenderTargetIdentifier c = 8;

            Assert.That(a == b, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a != c, Is.True);
            Assert.That(a.Equals((object)b), Is.True);
        }

        // ---- stream path: all eight UV channels round-trip --------------------------------------------------------

        [Test]
        public void EightUVChannelsRoundTripThroughTheStreamPath()
        {
            const int count = 16;
            var mesh = new Mesh();

            var positions = new Vector3[count];
            var uv0 = new Vector2[count];
            var uvN = new Vector4[7][];

            for (int i = 0; i < count; ++i)
            {
                positions[i] = Position(i);
                uv0[i] = Uv0(i);
            }

            for (int channel = 1; channel <= 7; ++channel)
            {
                uvN[channel - 1] = new Vector4[count];
                for (int i = 0; i < count; ++i)
                    uvN[channel - 1][i] = Uv(channel, i);
            }

            // The exact call sequence NowMesh.UploadMesh takes on its fallback path (1983-1991).
            var flags = MeshUpdateFlags.DontRecalculateBounds;
            mesh.SetVertices(positions, 0, count, flags);
            mesh.SetUVs(0, uv0, 0, count, flags);
            for (int channel = 1; channel <= 7; ++channel)
                mesh.SetUVs(channel, uvN[channel - 1], 0, count, flags);

            Assert.That(mesh.vertexCount, Is.EqualTo(count));

            var readPositions = new List<Vector3>();
            mesh.GetVertices(readPositions);
            Assert.That(readPositions.Count, Is.EqualTo(count));

            var readUv0 = new List<Vector2>();
            mesh.GetUVs(0, readUv0);
            Assert.That(readUv0.Count, Is.EqualTo(count));

            for (int i = 0; i < count; ++i)
            {
                Assert.That(readPositions[i], Is.EqualTo(Position(i)), $"position {i}");
                Assert.That(readUv0[i], Is.EqualTo(Uv0(i)), $"uv0 {i}");
            }

            var readUv = new List<Vector4>();
            for (int channel = 1; channel <= 7; ++channel)
            {
                mesh.GetUVs(channel, readUv);
                Assert.That(readUv.Count, Is.EqualTo(count), $"channel {channel} length");

                for (int i = 0; i < count; ++i)
                    Assert.That(readUv[i], Is.EqualTo(Uv(channel, i)), $"uv{channel} {i}");
            }
        }

        [Test]
        public void StreamPathAcceptsListsAsWellAsArrays()
        {
            var positions = new List<Vector3> { Position(0), Position(1), Position(2) };
            var uv3 = new List<Vector4> { Uv(3, 0), Uv(3, 1), Uv(3, 2) };

            var mesh = new Mesh();
            mesh.SetVertices(positions);
            mesh.SetUVs(3, uv3);

            var read = new List<Vector4>();
            mesh.GetUVs(3, read);

            Assert.That(mesh.vertexCount, Is.EqualTo(3));
            Assert.That(read, Is.EqualTo(uv3));
        }

        [Test]
        public void NormalsTangentsAndColorsRoundTripThroughTheStreamPath()
        {
            var mesh = new Mesh();
            var normals = new[] { Vector3.up, Vector3.right };
            var tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(0, 1, 0, 1) };
            var colors = new[] { new Color32(255, 0, 0, 255), new Color32(0, 128, 255, 64) };

            mesh.SetVertices(new[] { Position(0), Position(1) });
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetColors(colors);

            var readNormals = new List<Vector3>();
            var readTangents = new List<Vector4>();
            var readColors = new List<Color32>();

            mesh.GetNormals(readNormals);
            mesh.GetTangents(readTangents);
            mesh.GetColors(readColors);

            Assert.That(readNormals, Is.EqualTo(normals));
            Assert.That(readTangents, Is.EqualTo(tangents));
            Assert.That(readColors, Is.EqualTo(colors));
        }

        [Test]
        public void ReadBackOfAnUnsetChannelClearsTheListAndLeavesItEmpty()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new[] { Position(0) });

            var scratch = new List<Vector4> { Vector4.one, Vector4.one };
            mesh.GetUVs(5, scratch);

            Assert.That(scratch, Is.Empty, "Unity clears the caller's list even when the channel is absent.");
        }

        [Test]
        public void ReadBackWidensAndNarrowsAcrossDimensions()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new[] { Position(0) });
            mesh.SetUVs(0, new[] { new Vector2(1f, 2f) });
            mesh.SetUVs(1, new[] { new Vector4(3f, 4f, 5f, 6f) });

            var wide = new List<Vector4>();
            mesh.GetUVs(0, wide);
            Assert.That(wide[0], Is.EqualTo(new Vector4(1f, 2f, 0f, 0f)), "Missing components read as 0.");

            var narrow = new List<Vector2>();
            mesh.GetUVs(1, narrow);
            Assert.That(narrow[0], Is.EqualTo(new Vector2(3f, 4f)), "Extra components are dropped.");
        }

        [Test]
        public void UVChannelOutsideZeroToSevenThrows()
        {
            var mesh = new Mesh();

            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.SetUVs(8, new[] { Vector4.zero }));
            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.SetUVs(-1, new[] { Vector4.zero }));
            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.GetUVs(8, new List<Vector4>()));
        }

        // ---- interleaved path ------------------------------------------------------------------------------------

        [Test]
        public void InterleavedRenderVertexLayoutDeInterleavesOnReadBack()
        {
            const int count = 8;
            var vertices = new RenderVertex[count];

            for (int i = 0; i < count; ++i)
            {
                vertices[i] = new RenderVertex
                {
                    position = Position(i),
                    uv0 = Uv0(i),
                    uv1 = Uv(1, i),
                    uv2 = Uv(2, i),
                    uv3 = Uv(3, i),
                    uv4 = Uv(4, i),
                    uv5 = Uv(5, i),
                    uv6 = Uv(6, i),
                    uv7 = Uv(7, i),
                };
            }

            var mesh = new Mesh();
            mesh.SetVertexBufferParams(count, RenderVertexLayout);
            mesh.SetVertexBufferData(vertices, 0, 0, count, 0, MeshUpdateFlags.DontRecalculateBounds);

            Assert.That(mesh.vertexCount, Is.EqualTo(count));

            var readPositions = new List<Vector3>();
            mesh.GetVertices(readPositions);

            var readUv0 = new List<Vector2>();
            mesh.GetUVs(0, readUv0);

            for (int i = 0; i < count; ++i)
            {
                Assert.That(readPositions[i], Is.EqualTo(Position(i)), $"position {i}");
                Assert.That(readUv0[i], Is.EqualTo(Uv0(i)), $"uv0 {i}");
            }

            var readUv = new List<Vector4>();
            for (int channel = 1; channel <= 7; ++channel)
            {
                mesh.GetUVs(channel, readUv);
                Assert.That(readUv.Count, Is.EqualTo(count), $"channel {channel} length");

                for (int i = 0; i < count; ++i)
                    Assert.That(readUv[i], Is.EqualTo(Uv(channel, i)), $"uv{channel} {i}");
            }
        }

        [Test]
        public void InterleavedCanvasVertexLayoutDeInterleavesEveryChannel()
        {
            const int count = 4;
            var vertices = new CanvasVertex[count];

            for (int i = 0; i < count; ++i)
            {
                vertices[i] = new CanvasVertex
                {
                    position = Position(i),
                    normal = new Vector3(0f, 0f, i),
                    tangent = new Vector4(1f, 0f, 0f, -1f),
                    color = new Vector4(i / 4f, 0.5f, 1f, 1f),
                    uv0 = Uv(0, i),
                    uv1 = Uv(1, i),
                    uv2 = Uv(2, i),
                    uv3 = Uv(3, i),
                };
            }

            var mesh = new Mesh();
            mesh.SetVertexBufferParams(count, CanvasVertexLayout);
            mesh.SetVertexBufferData(vertices, 0, 0, count, 0, MeshUpdateFlags.DontRecalculateBounds);

            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color>();
            var uvs = new List<Vector4>();

            mesh.GetNormals(normals);
            mesh.GetTangents(tangents);
            mesh.GetColors(colors);

            for (int i = 0; i < count; ++i)
            {
                Assert.That(normals[i], Is.EqualTo(new Vector3(0f, 0f, i)));
                Assert.That(tangents[i], Is.EqualTo(new Vector4(1f, 0f, 0f, -1f)));
                Assert.That(colors[i], Is.EqualTo(new Color(i / 4f, 0.5f, 1f, 1f)));
            }

            for (int channel = 0; channel <= 3; ++channel)
            {
                mesh.GetUVs(channel, uvs);
                for (int i = 0; i < count; ++i)
                    Assert.That(uvs[i], Is.EqualTo(Uv(channel, i)), $"uv{channel} {i}");
            }

            // Channels the canvas layout does not declare must read back empty, not as garbage from another channel.
            mesh.GetUVs(6, uvs);
            Assert.That(uvs, Is.Empty);
        }

        [Test]
        public void InterleavedUploadHonoursMeshBufferStartOffset()
        {
            var vertices = new RenderVertex[2];
            vertices[0].position = Position(1);
            vertices[1].position = Position(2);

            var mesh = new Mesh();
            mesh.SetVertexBufferParams(3, RenderVertexLayout);
            mesh.SetVertexBufferData(vertices, 0, 1, 2, 0, MeshUpdateFlags.DontRecalculateBounds);

            var read = new List<Vector3>();
            mesh.GetVertices(read);

            Assert.That(read.Count, Is.EqualTo(3));
            Assert.That(read[1], Is.EqualTo(Position(1)));
            Assert.That(read[2], Is.EqualTo(Position(2)));
        }

        [Test]
        public void SwitchingFromInterleavedToStreamStorageDropsTheStaleInterleavedData()
        {
            var mesh = new Mesh();
            var vertices = new RenderVertex[2];
            vertices[0].uv1 = Uv(1, 0);
            vertices[1].uv1 = Uv(1, 1);

            mesh.SetVertexBufferParams(2, RenderVertexLayout);
            mesh.SetVertexBufferData(vertices, 0, 0, 2, 0, MeshUpdateFlags.DontRecalculateBounds);

            mesh.SetVertices(new[] { Position(0), Position(1) });

            var read = new List<Vector4>();
            mesh.GetUVs(1, read);

            Assert.That(read, Is.Empty, "The interleaved buffer is no longer the truth once a stream is written.");
        }

        [Test]
        public void SetVertexBufferDataBeforeParamsThrows()
        {
            var mesh = new Mesh();

            Assert.Throws<InvalidOperationException>(
                () => mesh.SetVertexBufferData(new RenderVertex[1], 0, 0, 1, 0, MeshUpdateFlags.DontRecalculateBounds));
        }

        [Test]
        public void OutOfRangeSourceSliceThrows()
        {
            var mesh = new Mesh();
            mesh.SetVertexBufferParams(4, RenderVertexLayout);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => mesh.SetVertexBufferData(new RenderVertex[2], 0, 0, 3, 0, MeshUpdateFlags.DontRecalculateBounds));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => mesh.SetVertices(new[] { Vector3.zero }, 1, 1, MeshUpdateFlags.DontRecalculateBounds));
        }

        // ---- index buffers ---------------------------------------------------------------------------------------

        [Test]
        public void SixteenBitIndicesRoundTripThroughGetTriangles()
        {
            var indices = new ushort[] { 0, 1, 2, 2, 3, 0 };

            var mesh = new Mesh();
            mesh.SetVertices(new[] { Position(0), Position(1), Position(2), Position(3) });
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length, MeshUpdateFlags.DontValidateIndices);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length) { firstVertex = 0, vertexCount = 4 });

            var read = new List<int>();
            mesh.GetTriangles(read, 0);

            Assert.That(mesh.indexFormat, Is.EqualTo(IndexFormat.UInt16));
            Assert.That(read, Is.EqualTo(new[] { 0, 1, 2, 2, 3, 0 }));
        }

        [Test]
        public void ThirtyTwoBitIndicesRoundTripThroughGetTriangles()
        {
            var indices = new[] { 70000, 70001, 70002 };

            var mesh = new Mesh();
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length, MeshUpdateFlags.DontValidateIndices);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length));

            var read = new List<int>();
            mesh.GetTriangles(read, 0);

            Assert.That(mesh.indexFormat, Is.EqualTo(IndexFormat.UInt32));
            Assert.That(read, Is.EqualTo(indices));
        }

        [Test]
        public void GetTrianglesAppliesBaseVertexAndClearsFirst()
        {
            var mesh = new Mesh();
            mesh.SetIndexBufferParams(3, IndexFormat.UInt16);
            mesh.SetIndexBufferData(new ushort[] { 0, 1, 2 }, 0, 0, 3, MeshUpdateFlags.DontValidateIndices);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 3) { baseVertex = 10 });

            var read = new List<int> { 99, 99, 99, 99 };
            mesh.GetTriangles(read, 0);

            Assert.That(read, Is.EqualTo(new[] { 10, 11, 12 }));
        }

        [Test]
        public void GetTrianglesOnAnAbsentSubMeshYieldsAnEmptyList()
        {
            var mesh = new Mesh();
            var read = new List<int> { 1, 2, 3 };

            mesh.GetTriangles(read, 0);

            Assert.That(read, Is.Empty, "NowEffectsMesh early-outs on Count == 0, so this must not throw.");
        }

        [Test]
        public void SetTrianglesGrowsTheSubMeshTableAndRecalculatesBounds()
        {
            var mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(new[] { new Vector3(-1f, -2f, 0f), new Vector3(3f, 4f, 0f), new Vector3(0f, 0f, 5f) });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);

            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.GetTriangles(0), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(mesh.bounds.min, Is.EqualTo(new Vector3(-1f, -2f, 0f)));
            Assert.That(mesh.bounds.max, Is.EqualTo(new Vector3(3f, 4f, 5f)));
        }

        [Test]
        public void SetTrianglesRejectsIndicesTooWideForTheDeclaredFormat()
        {
            var mesh = new Mesh();

            Assert.That(mesh.indexFormat, Is.EqualTo(IndexFormat.UInt16));
            Assert.Throws<ArgumentException>(() => mesh.SetTriangles(new[] { 0, 1, 70000 }, 0, false));
        }

        // ---- sub-meshes --------------------------------------------------------------------------------------------

        [Test]
        public void SetSubMeshesReplacesTheWholeTable()
        {
            var descriptors = new[]
            {
                new SubMeshDescriptor(0, 6) { firstVertex = 0, vertexCount = 4 },
                new SubMeshDescriptor(6, 9) { firstVertex = 4, vertexCount = 6 },
                new SubMeshDescriptor(15, 3) { firstVertex = 10, vertexCount = 3 },
            };

            var mesh = new Mesh();
            mesh.SetSubMeshes(descriptors, 0, descriptors.Length, MeshUpdateFlags.DontRecalculateBounds);

            Assert.That(mesh.subMeshCount, Is.EqualTo(3));
            for (int i = 0; i < descriptors.Length; ++i)
                Assert.That(mesh.GetSubMesh(i), Is.EqualTo(descriptors[i]));

            // A shorter table replaces, never merges.
            mesh.SetSubMeshes(descriptors, 1, 1, MeshUpdateFlags.DontRecalculateBounds);
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.GetSubMesh(0), Is.EqualTo(descriptors[1]));
        }

        [Test]
        public void SubMeshCountGrowsWithDefaultDescriptorsAndIndexingIsRangeChecked()
        {
            var mesh = new Mesh();
            mesh.subMeshCount = 2;

            Assert.That(mesh.GetSubMesh(1), Is.EqualTo(default(SubMeshDescriptor)));
            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.GetSubMesh(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.SetSubMesh(2, new SubMeshDescriptor(0, 3)));
        }

        // ---- bounds, clear, lifetime -------------------------------------------------------------------------------

        [Test]
        public void BoundsAreStoredExactlyAsGiven()
        {
            var mesh = new Mesh();
            var bounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f));

            mesh.bounds = bounds;

            Assert.That(mesh.bounds, Is.EqualTo(bounds));
        }

        [Test]
        public void DontRecalculateBoundsLeavesBoundsAlone()
        {
            var mesh = new Mesh();
            var bounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f));
            mesh.bounds = bounds;

            mesh.SetVertices(new[] { new Vector3(100f, 100f, 100f) }, 0, 1, MeshUpdateFlags.DontRecalculateBounds);

            Assert.That(mesh.bounds, Is.EqualTo(bounds), "NowUI's upload path relies on this flag being honoured.");
        }

        [Test]
        public void DefaultFlagsRecalculateBoundsFromThePositionChannel()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new[] { new Vector3(-2f, 0f, 1f), new Vector3(4f, 6f, 1f) });

            Assert.That(mesh.bounds.min, Is.EqualTo(new Vector3(-2f, 0f, 1f)));
            Assert.That(mesh.bounds.max, Is.EqualTo(new Vector3(4f, 6f, 1f)));
        }

        [Test]
        public void RecalculateBoundsOnAnEmptyMeshYieldsZeroBounds()
        {
            var mesh = new Mesh();
            mesh.bounds = new Bounds(Vector3.one, Vector3.one);

            mesh.RecalculateBounds();

            Assert.That(mesh.bounds, Is.EqualTo(default(Bounds)));
        }

        [Test]
        public void ClearZeroesEveryCountAndResetsBounds()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new[] { Position(0), Position(1) });
            mesh.SetUVs(2, new[] { Uv(2, 0), Uv(2, 1) });
            mesh.SetIndexBufferParams(3, IndexFormat.UInt16);
            mesh.subMeshCount = 2;
            mesh.bounds = new Bounds(Vector3.one, Vector3.one);

            mesh.Clear();

            Assert.That(mesh.vertexCount, Is.Zero);
            Assert.That(mesh.subMeshCount, Is.Zero);
            Assert.That(mesh.bounds, Is.EqualTo(default(Bounds)));

            var read = new List<Vector4>();
            mesh.GetUVs(2, read);
            Assert.That(read, Is.Empty);
        }

        [Test]
        public void ClearKeepsTheVertexLayoutUnlessAskedNotTo()
        {
            var mesh = new Mesh();
            mesh.SetVertexBufferParams(4, RenderVertexLayout);

            mesh.Clear(true);
            Assert.That(mesh.data.interleaved, Is.True);
            Assert.That(mesh.data.layoutCount, Is.EqualTo(RenderVertexLayout.Length));

            mesh.Clear(false);
            Assert.That(mesh.data.interleaved, Is.False);
            Assert.That(mesh.data.layoutCount, Is.Zero);
            Assert.That(mesh.data.vertexStride, Is.Zero);
        }

        [Test]
        public void ClearKeepsBufferCapacity()
        {
            var mesh = new Mesh();
            mesh.SetVertexBufferParams(256, RenderVertexLayout);
            mesh.SetIndexBufferParams(1024, IndexFormat.UInt32);

            int vertexCapacity = mesh.data.vertexBytes.Length;
            int indexCapacity = mesh.data.indexBytes.Length;
            var subMeshTable = mesh.data.subMeshes;

            mesh.subMeshCount = 4;
            mesh.Clear();

            Assert.That(mesh.data.vertexBytes.Length, Is.EqualTo(vertexCapacity));
            Assert.That(mesh.data.indexBytes.Length, Is.EqualTo(indexCapacity));
            Assert.That(mesh.data.subMeshes, Is.SameAs(mesh.data.subMeshes));
            Assert.That(subMeshTable, Is.Not.Null);
        }

        [Test]
        public void ChangingIndexFormatDropsTheIndexBufferContents()
        {
            var mesh = new Mesh();
            mesh.SetIndexBufferParams(3, IndexFormat.UInt16);
            mesh.SetIndexBufferData(new ushort[] { 1, 2, 3 }, 0, 0, 3, MeshUpdateFlags.DontValidateIndices);

            mesh.indexFormat = IndexFormat.UInt32;

            Assert.That(mesh.data.indexCount, Is.Zero, "Unity recreates the index buffer when the format changes.");
        }

        [Test]
        public void VersionAdvancesOnEveryChange()
        {
            var mesh = new Mesh();
            uint start = mesh.data.version;

            mesh.SetVertices(new[] { Position(0) });
            uint afterVertices = mesh.data.version;
            mesh.MarkModified();

            Assert.That(afterVertices, Is.GreaterThan(start));
            Assert.That(mesh.data.version, Is.GreaterThan(afterVertices));
        }

        [Test]
        public void MarkDynamicAndUploadMeshDataAreHarmlessNoOps()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new[] { Position(0) });

            Assert.DoesNotThrow(() => mesh.MarkDynamic());
            Assert.DoesNotThrow(() => mesh.UploadMeshData(true));
            Assert.That(mesh.vertexCount, Is.EqualTo(1));
        }

        // ---- allocation --------------------------------------------------------------------------------------------

        [Test]
        public void SteadyStateUploadAndReadBackAllocatesNothing()
        {
            const int count = 64;
            var mesh = new Mesh();

            var positions = new Vector3[count];
            var uv0 = new Vector2[count];
            var uvN = new Vector4[count];
            var indices = new ushort[count * 3];
            var descriptors = new SubMeshDescriptor[1];

            for (int i = 0; i < count; ++i)
            {
                positions[i] = Position(i);
                uv0[i] = Uv0(i);
                uvN[i] = Uv(1, i);
            }

            var readPositions = new List<Vector3>(count);
            var readUv0 = new List<Vector2>(count);
            var readUv = new List<Vector4>(count);
            var readIndices = new List<int>(indices.Length);

            // Two warm-up rounds: the first sizes every buffer, the second proves the sizing has settled.
            for (int round = 0; round < 2; ++round)
                Cycle();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int round = 0; round < 16; ++round)
                Cycle();
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero, "The steady-state upload path must not allocate (design §1.2).");

            void Cycle()
            {
                var flags = MeshUpdateFlags.DontRecalculateBounds;

                mesh.Clear(true);
                mesh.SetVertices(positions, 0, count, flags);
                mesh.SetUVs(0, uv0, 0, count, flags);
                for (int channel = 1; channel <= 7; ++channel)
                    mesh.SetUVs(channel, uvN, 0, count, flags);

                mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
                mesh.SetIndexBufferData(indices, 0, 0, indices.Length, MeshUpdateFlags.DontValidateIndices);

                descriptors[0] = new SubMeshDescriptor(0, indices.Length) { firstVertex = 0, vertexCount = count };
                mesh.SetSubMeshes(descriptors, 0, 1, MeshUpdateFlags.DontValidateIndices);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one);

                mesh.GetVertices(readPositions);
                mesh.GetUVs(0, readUv0);
                for (int channel = 1; channel <= 7; ++channel)
                    mesh.GetUVs(channel, readUv);
                mesh.GetTriangles(readIndices, 0);
            }
        }

        [Test]
        public void SteadyStateInterleavedUploadAllocatesNothing()
        {
            const int count = 64;
            var mesh = new Mesh();
            var vertices = new RenderVertex[count];
            var readPositions = new List<Vector3>(count);
            var readUv = new List<Vector4>(count);

            for (int round = 0; round < 2; ++round)
                Cycle();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int round = 0; round < 16; ++round)
                Cycle();
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero, "The interleaved upload path must not allocate (design §1.2).");

            void Cycle()
            {
                mesh.Clear(true);
                mesh.SetVertexBufferParams(count, RenderVertexLayout);
                mesh.SetVertexBufferData(vertices, 0, 0, count, 0, MeshUpdateFlags.DontRecalculateBounds);
                mesh.GetVertices(readPositions);
                mesh.GetUVs(7, readUv);
            }
        }

        // ---- NowMeshData -------------------------------------------------------------------------------------------

        [Test]
        public void MeshRetainsACopyNotTheCallersArray()
        {
            // Now.UploadCapturedMeshes reuses one static scratch list for every draw list in the frame, so a Mesh that
            // aliased it would show the next draw list's contents (design §3.5).
            var scratch = new[] { Position(0), Position(1) };

            var mesh = new Mesh();
            mesh.SetVertices(scratch);

            scratch[0] = new Vector3(999f, 999f, 999f);

            var read = new List<Vector3>();
            mesh.GetVertices(read);

            Assert.That(read[0], Is.EqualTo(Position(0)));
        }

        [Test]
        public void StreamsAreIndexedByVertexAttribute()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new[] { Position(0) });
            mesh.SetUVs(7, new[] { Uv(7, 0) });

            Assert.That(mesh.data.streams.Length, Is.EqualTo(NowMeshData.StreamCount));
            Assert.That(mesh.data.streams[(int)VertexAttribute.Position].count, Is.EqualTo(1));
            Assert.That(mesh.data.streams[(int)VertexAttribute.Position].elementSize, Is.EqualTo(12));
            Assert.That(mesh.data.streams[(int)VertexAttribute.TexCoord7].count, Is.EqualTo(1));
            Assert.That(mesh.data.streams[(int)VertexAttribute.TexCoord7].elementSize, Is.EqualTo(16));
        }

        [Test]
        public void TryGetAttributeReportsInterleavedOffsets()
        {
            var mesh = new Mesh();
            mesh.SetVertexBufferParams(1, RenderVertexLayout);

            Assert.That(mesh.data.TryGetAttribute(VertexAttribute.Position, out int offset, out var format, out int dimension), Is.True);
            Assert.That(offset, Is.Zero);
            Assert.That(format, Is.EqualTo(VertexAttributeFormat.Float32));
            Assert.That(dimension, Is.EqualTo(3));

            Assert.That(mesh.data.TryGetAttribute(VertexAttribute.TexCoord1, out offset, out _, out dimension), Is.True);
            Assert.That(offset, Is.EqualTo(12 + 8), "TexCoord1 follows a float3 position and a float2 uv0.");
            Assert.That(dimension, Is.EqualTo(4));

            Assert.That(mesh.data.TryGetAttribute(VertexAttribute.Normal, out _, out _, out _), Is.False);
        }

        [Test]
        public void IndexSizeFollowsTheFormat()
        {
            Assert.That(NowMeshData.IndexSize(IndexFormat.UInt16), Is.EqualTo(2));
            Assert.That(NowMeshData.IndexSize(IndexFormat.UInt32), Is.EqualTo(4));
        }

        [Test]
        public void EnsureBytesGrowsGeometricallyAndNeverShrinks()
        {
            byte[] buffer = null;

            NowMeshData.EnsureBytes(ref buffer, 10);
            Assert.That(buffer.Length, Is.GreaterThanOrEqualTo(10));

            int grown = buffer.Length;
            NowMeshData.EnsureBytes(ref buffer, 1);
            Assert.That(buffer.Length, Is.EqualTo(grown), "Shrinking would defeat the allocation rule.");

            NowMeshData.EnsureBytes(ref buffer, grown * 4);
            Assert.That(buffer.Length, Is.GreaterThanOrEqualTo(grown * 4));
        }
    }
}
