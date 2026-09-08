// A PLACEHOLDER for the real WebGL2 backend, which is another work unit's file.
//
// It exists so this project builds, publishes and can be measured on its own, and so the host half can be verified
// end to end in a browser before a single GL call exists: it forwards every call to NullRenderBackend (which
// validates the way a real backend would - a null mesh throws, an out-of-range sub-mesh throws) and prints the frame's
// backend traffic to the console. A page running this placeholder should print exactly the eight operations
// Docs/Standalone/M2-Scouting.md section 3 recorded for this scene:
//
//     BeginFrame(1) / UpdateSampler / UploadTexture2D / SetViewProjection / DrawMesh / SetViewProjection / DrawMesh /
//     EndFrame
//
// which is a real result about the host, the resource provider and the core - and no result at all about drawing,
// because nothing here draws. The canvas stays black.
//
// IT DISAPPEARS AUTOMATICALLY. NowUI.Web.csproj compiles Placeholder/ only while WebGL2Backend.cs does not exist next
// to it; the moment the real backend lands, this file is dropped from the build and the `WebGL2Backend.CreateAsync`
// call in Program.cs resolves to the real one. If the real backend's factory has a different shape, that call - one
// line - is the only thing that has to change.
using System;
using NowUI.Engine;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Web
{
    /// <summary>Stands in for the WebGL2 backend until the real one lands. Draws nothing; reports everything.</summary>
    public sealed class WebGL2Backend : INowRenderBackend
    {
        /// <summary>Operations logged before the placeholder goes quiet, so a 60 Hz page does not flood the console.</summary>
        private const int k_LoggedFrames = 2;

        private readonly NullRenderBackend m_Inner = new NullRenderBackend();
        private readonly string m_CanvasSelector;

        private int m_Frame;

        private WebGL2Backend(string canvasSelector)
        {
            m_CanvasSelector = canvasSelector;
        }

        /// <summary>
        /// The shape the host expects of the real backend: give it the canvas selector, get back a backend whose GL
        /// context exists and whose two programs are compiled and linked.
        /// </summary>
        public static Task<WebGL2Backend> CreateAsync(string canvasSelector)
        {
            BrowserInterop.Log(1,
                "[NowUI] WebGL2Backend is the PLACEHOLDER (canvas '" + canvasSelector + "'): the frame runs and the " +
                "backend traffic is logged, but nothing is drawn. Drop WebGL2Backend.cs next to Program.cs to " +
                "replace it.");

            return Task.FromResult(new WebGL2Backend(canvasSelector));
        }

        /// <summary>The canvas this backend would own.</summary>
        public string canvasSelector
        {
            get { return m_CanvasSelector; }
        }

        /// <inheritdoc />
        public NowRenderCaps caps
        {
            get { return m_Inner.caps; }
        }

        /// <inheritdoc />
        public void BeginFrame(int frameCount)
        {
            m_Frame = frameCount;
            Trace("BeginFrame(" + frameCount + ")");
            m_Inner.BeginFrame(frameCount);
        }

        /// <inheritdoc />
        public void EndFrame()
        {
            Trace("EndFrame()");
            m_Inner.EndFrame();
        }

        /// <inheritdoc />
        public void UploadTexture2D(Texture2D texture, ReadOnlySpan<byte> pixels, RectInt dirtyRect, bool generateMips)
        {
            Trace("UploadTexture2D(" + Name(texture) + ", pixels=" + pixels.Length + ", dirty=(" +
                  dirtyRect.x + ", " + dirtyRect.y + ", " + dirtyRect.width + ", " + dirtyRect.height +
                  "), mips=" + generateMips + ")");
            m_Inner.UploadTexture2D(texture, pixels, dirtyRect, generateMips);
        }

        /// <inheritdoc />
        public void UpdateSampler(Texture texture)
        {
            Trace("UpdateSampler(" + Name(texture) + ")");
            m_Inner.UpdateSampler(texture);
        }

        /// <inheritdoc />
        public void ReleaseTexture(Texture texture)
        {
            Trace("ReleaseTexture(" + Name(texture) + ")");
            m_Inner.ReleaseTexture(texture);
        }

        /// <inheritdoc />
        public bool CreateRenderTexture(RenderTexture texture, in NowRenderTextureRequest request)
        {
            Trace("CreateRenderTexture(" + Name(texture) + ")");
            return m_Inner.CreateRenderTexture(texture, in request);
        }

        /// <inheritdoc />
        public bool IsRenderTextureLost(RenderTexture texture)
        {
            return m_Inner.IsRenderTextureLost(texture);
        }

        /// <inheritdoc />
        public void ReleaseRenderTexture(RenderTexture texture)
        {
            Trace("ReleaseRenderTexture(" + Name(texture) + ")");
            m_Inner.ReleaseRenderTexture(texture);
        }

        /// <inheritdoc />
        public void ReleaseMesh(Mesh mesh)
        {
            m_Inner.ReleaseMesh(mesh);
        }

        /// <inheritdoc />
        public void ReleaseMaterial(Material material)
        {
            m_Inner.ReleaseMaterial(material);
        }

        /// <inheritdoc />
        public bool ResolveShader(Shader shader)
        {
            Trace("ResolveShader(" + Name(shader) + ")");
            return m_Inner.ResolveShader(shader);
        }

        /// <inheritdoc />
        public void SetRenderTarget(in NowRenderTarget target)
        {
            Trace("SetRenderTarget()");
            m_Inner.SetRenderTarget(in target);
        }

        /// <inheritdoc />
        public void SetViewport(in Rect pixelRect)
        {
            Trace("SetViewport(" + pixelRect.width + "x" + pixelRect.height + ")");
            m_Inner.SetViewport(in pixelRect);
        }

        /// <inheritdoc />
        public void SetViewProjection(in Matrix4x4 view, in Matrix4x4 projection)
        {
            Trace("SetViewProjection()");
            m_Inner.SetViewProjection(in view, in projection);
        }

        /// <inheritdoc />
        public void ClearRenderTarget(bool clearDepth, bool clearColor, in Color color, float depth)
        {
            Trace("ClearRenderTarget(color=" + clearColor + ", depth=" + clearDepth + ")");
            m_Inner.ClearRenderTarget(clearDepth, clearColor, in color, depth);
        }

        /// <inheritdoc />
        public void DrawMesh(Mesh mesh, int subMesh, in Matrix4x4 model, Material material, int pass,
                             MaterialPropertyBlock properties)
        {
            Trace("DrawMesh(" + Name(mesh) + ", subMesh=" + subMesh + ", material=" + Name(material) +
                  ", pass=" + pass + ", properties=" + (properties == null ? "none" : "block") + ")");
            m_Inner.DrawMesh(mesh, subMesh, in model, material, pass, properties);
        }

        /// <inheritdoc />
        public void DrawProcedural(in Matrix4x4 model, Material material, int pass, MeshTopology topology,
                                   int vertexCount, int instanceCount, MaterialPropertyBlock properties)
        {
            Trace("DrawProcedural(vertices=" + vertexCount + ", instances=" + instanceCount + ")");
            m_Inner.DrawProcedural(in model, material, pass, topology, vertexCount, instanceCount, properties);
        }

        /// <inheritdoc />
        public void Blit(Texture source, in NowRenderTarget destination, Material material, int pass,
                         in Vector2 scale, in Vector2 offset, int sourceDepthSlice, int destinationDepthSlice)
        {
            Trace("Blit(" + Name(source) + ")");
            m_Inner.Blit(source, in destination, material, pass, in scale, in offset, sourceDepthSlice,
                destinationDepthSlice);
        }

        /// <inheritdoc />
        public void CopyTexture(Texture source, Texture destination)
        {
            Trace("CopyTexture(" + Name(source) + " -> " + Name(destination) + ")");
            m_Inner.CopyTexture(source, destination);
        }

        private void Trace(string message)
        {
            if (m_Frame > k_LoggedFrames)
                return;

            BrowserInterop.Log(0, "  " + message);
        }

        private static string Name(UnityEngine.Object value)
        {
            if (ReferenceEquals(value, null))
                return "null";

            return string.IsNullOrEmpty(value.name) ? ("#" + value.GetInstanceID()) : value.name;
        }
    }
}
