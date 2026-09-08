// W9. A render backend that counts the vertices NowUI actually submitted.
//
// It exists because of what "the op decodes and draws" is worth without it. The other backends in this assembly
// answer "was Now.StartUI entered" (NowRecordingRenderBackend logs BeginFrame/EndFrame) and "how many DrawMesh
// calls happened" (NullRenderBackend.meshDraws) - and neither distinguishes a circle that tessellated from a
// circle whose builder was configured, never filled, and batched into nothing. NowUI batches aggressively, so a
// frame with eight shapes and a frame with none can produce the SAME number of DrawMesh calls against the same
// material.
//
// The vertex count cannot. A shape that reached the mesh added vertices to it; one that did not, did not. So this
// decorator sums `mesh.vertexCount` over every DrawMesh in a frame, and the drawing tests assert on the delta
// between a frame with the op and the same frame without it. That is the difference between "the decoder ran" and
// "geometry exists", and it is the only assertion in this file that is hard to fake.
//
// Everything else is pass-through to the wrapped NowRecordingRenderBackend, so BridgeTestHost.backend keeps
// working exactly as it did for the 56 tests that were here before.

using System;
using NowUI.Engine;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Bridge.Tests
{
    /// <summary>Wraps a backend and counts submitted vertices per frame.</summary>
    public sealed class VertexCountingBackend : INowRenderBackend
    {
        private readonly NowRecordingRenderBackend m_Inner;

        public VertexCountingBackend(NowRecordingRenderBackend inner)
        {
            m_Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>The backend the assembly's existing assertions read.</summary>
        public NowRecordingRenderBackend inner => m_Inner;

        /// <summary>Vertices submitted since the last <see cref="ResetCounters"/>.</summary>
        public long vertices { get; private set; }

        /// <summary>DrawMesh calls since the last <see cref="ResetCounters"/>.</summary>
        public int draws { get; private set; }

        private readonly System.Collections.Generic.List<Vector3> m_Scratch =
            new System.Collections.Generic.List<Vector3>(1024);

        private Vector2 m_Min = new Vector2(float.MaxValue, float.MaxValue);
        private Vector2 m_Max = new Vector2(float.MinValue, float.MinValue);

        /// <summary>
        /// The XY extent of every vertex submitted since the last <see cref="ResetCounters"/>, or a default rect
        /// when nothing was submitted.
        /// </summary>
        /// <remarks>
        /// This is what proves the COORDINATE MODEL rather than merely the decode. A drawing op's coordinates are
        /// canvas-local and the decoder adds the origin; the only way to see that from outside is to look at where
        /// the geometry actually landed. So the drawing tests assert that a rect at canvas-local (10, 0) inside a
        /// canvas whose top is 400px down submits the same geometry as a rect at screen (10, 400) with no canvas
        /// at all - an equality, which is robust to whatever sign convention NowUI's vertex positions use.
        /// </remarks>
        public Rect geometry
        {
            get
            {
                if (m_Min.x > m_Max.x) return default;
                return new Rect(m_Min.x, m_Min.y, m_Max.x - m_Min.x, m_Max.y - m_Min.y);
            }
        }

        /// <summary>Whether any vertex position was read back at all.</summary>
        public bool hasGeometry => m_Min.x <= m_Max.x;

        public void ResetCounters()
        {
            vertices = 0;
            draws = 0;
            m_Min = new Vector2(float.MaxValue, float.MaxValue);
            m_Max = new Vector2(float.MinValue, float.MinValue);
        }

        private void Accumulate(Mesh mesh)
        {
            m_Scratch.Clear();
            mesh.GetVertices(m_Scratch);

            for (int i = 0; i < m_Scratch.Count; ++i)
            {
                Vector3 v = m_Scratch[i];
                if (v.x < m_Min.x) m_Min.x = v.x;
                if (v.y < m_Min.y) m_Min.y = v.y;
                if (v.x > m_Max.x) m_Max.x = v.x;
                if (v.y > m_Max.y) m_Max.y = v.y;
            }
        }

        public NowRenderCaps caps => m_Inner.caps;

        public void BeginFrame(int frameCount) => m_Inner.BeginFrame(frameCount);

        public void EndFrame() => m_Inner.EndFrame();

        public void UploadTexture2D(Texture2D texture, ReadOnlySpan<byte> pixels, RectInt dirtyRect, bool generateMips)
            => m_Inner.UploadTexture2D(texture, pixels, dirtyRect, generateMips);

        public void UpdateSampler(Texture texture) => m_Inner.UpdateSampler(texture);

        public void ReleaseTexture(Texture texture) => m_Inner.ReleaseTexture(texture);

        public bool CreateRenderTexture(RenderTexture texture, in NowRenderTextureRequest request)
            => m_Inner.CreateRenderTexture(texture, in request);

        public bool IsRenderTextureLost(RenderTexture texture) => m_Inner.IsRenderTextureLost(texture);

        public void ReleaseRenderTexture(RenderTexture texture) => m_Inner.ReleaseRenderTexture(texture);

        public void ReleaseMesh(Mesh mesh) => m_Inner.ReleaseMesh(mesh);

        public void ReleaseMaterial(Material material) => m_Inner.ReleaseMaterial(material);

        public bool ResolveShader(Shader shader) => m_Inner.ResolveShader(shader);

        public void SetRenderTarget(in NowRenderTarget target) => m_Inner.SetRenderTarget(in target);

        public void SetViewport(in Rect pixelRect) => m_Inner.SetViewport(in pixelRect);

        public void SetViewProjection(in Matrix4x4 view, in Matrix4x4 projection)
            => m_Inner.SetViewProjection(in view, in projection);

        public void ClearRenderTarget(bool clearDepth, bool clearColor, in Color color, float depth)
            => m_Inner.ClearRenderTarget(clearDepth, clearColor, in color, depth);

        public void DrawMesh(Mesh mesh, int subMesh, in Matrix4x4 model, Material material, int pass,
                             MaterialPropertyBlock properties)
        {
            ++draws;

            if (mesh != null)
            {
                vertices += mesh.vertexCount;
                Accumulate(mesh);
            }

            m_Inner.DrawMesh(mesh, subMesh, in model, material, pass, properties);
        }

        public void DrawProcedural(in Matrix4x4 model, Material material, int pass, MeshTopology topology,
                                   int vertexCount, int instanceCount, MaterialPropertyBlock properties)
        {
            ++draws;
            vertices += (long)vertexCount * Math.Max(1, instanceCount);

            m_Inner.DrawProcedural(in model, material, pass, topology, vertexCount, instanceCount, properties);
        }

        public void Blit(Texture source, in NowRenderTarget destination, Material material, int pass,
                         in Vector2 scale, in Vector2 offset, int sourceDepthSlice, int destinationDepthSlice)
            => m_Inner.Blit(source, in destination, material, pass, in scale, in offset, sourceDepthSlice,
                            destinationDepthSlice);

        public void CopyTexture(Texture source, Texture destination) => m_Inner.CopyTexture(source, destination);
    }
}
