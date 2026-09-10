using NowUI.Cli;
using NowUI.Desktop;
using NowUI.Engine;
using NowUI.Hosting;
using NUnit.Framework;
using OpenTK.Windowing.Desktop;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Native.Tests;

[NonParallelizable, Category("NativeGraphics")]
public sealed class MeshUploadTests
{
    [Test, Apartment(ApartmentState.STA)]
    public void SwitchingPackedAndSeparateVertexLayoutsPreservesRenderedPixels()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("This in-process GLFW check requires Windows; portable GPU captures run in CLI child processes.");
        if (Environment.GetEnvironmentVariable("NOWUI_TEST_NATIVE_GRAPHICS") != "1")
            Assert.Ignore("Set NOWUI_TEST_NATIVE_GRAPHICS=1 in a desktop graphics session.");
        bool guard = GLFWProvider.CheckForMainThread;
        GLFWProvider.CheckForMainThread = false;
        try { CheckLayouts(); }
        finally { GLFWProvider.CheckForMainThread = guard; }
    }

    static void CheckLayouts()
    {
        using var backend = new DesktopRenderBackend(96, 96);
        using var resources = new NowFileResources();
        using var host = new CaptureHost(96, 96, resources, AppContext.BaseDirectory);
        NowRuntime.RegisterAssembly(typeof(Now).Assembly);
        NowRuntime.Initialize(host, backend);
        try
        {
            using var list = new NowDrawList();
            NowRuntime.BeginFrame();
            try
            {
                using (list.Begin(new Vector2(96, 96)))
                {
                    Now.Rectangle(new NowRect(12, 16, 54, 48)).SetColor(Color.red).SetRadius(7).Draw();
                    Now.Rectangle(new NowRect(36, 42, 46, 40)).SetColor(new Color(0, 1, 0, .6f)).Draw();
                }
                var mesh = list.mesh;
                byte[] expected = Draw();
                Assert.That(expected.Count(v => v != 0), Is.GreaterThan(1000), "The comparison must contain real geometry.");
                var positions = new List<Vector3>(); mesh.GetVertices(positions);
                var uv = Enumerable.Range(0, 8).Select(_ => new List<Vector4>()).ToArray();
                for (int i = 0; i < 8; ++i) mesh.GetUVs(i, uv[i]);

                mesh.SetVertices(positions);
                for (int i = 0; i < 8; ++i) mesh.SetUVs(i, uv[i]);
                Assert.That(Draw(), Is.EqualTo(expected), "Separate channels must match the packed layout.");
                foreach (int uv0Dimension in new[] { 4, 2 })
                {
                    var layout = new VertexAttributeDescriptor[9];
                    layout[0] = new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
                    for (int i = 0; i < 8; ++i)
                        layout[i + 1] = new(VertexAttribute.TexCoord0 + i, VertexAttributeFormat.Float32, i == 0 ? uv0Dimension : 4);
                    var packed = new float[positions.Count * (31 + uv0Dimension)];
                    int at = 0;
                    for (int vertex = 0; vertex < positions.Count; ++vertex)
                    {
                        var p = positions[vertex]; packed[at++] = p.x; packed[at++] = p.y; packed[at++] = p.z;
                        for (int channel = 0; channel < 8; ++channel)
                        {
                            var value = uv[channel][vertex];
                            packed[at++] = value.x; packed[at++] = value.y;
                            if (channel != 0 || uv0Dimension == 4) { packed[at++] = value.z; packed[at++] = value.w; }
                        }
                    }
                    mesh.SetVertexBufferParams(positions.Count, layout);
                    mesh.SetVertexBufferData(packed, 0, 0, packed.Length);
                    Assert.That(Draw(), Is.EqualTo(expected), $"Changing stride with UV0 dimension {uv0Dimension} must preserve pixels.");
                }

                byte[] Draw()
                {
                    backend.ClearRenderTarget(false, true, Color.clear, 1);
                    backend.SetViewProjection(Matrix4x4.identity, Matrix4x4.Ortho(0, 96, -96, 0, -1, 100));
                    for (int i = 0; i < list.batches.Count; ++i)
                        backend.DrawMesh(mesh, i, Matrix4x4.identity, list.batches[i].material, 0, null!);
                    return backend.ReadPixelsRgbaBottomUp();
                }
            }
            finally { NowRuntime.EndFrame(); }
        }
        finally { NowRuntime.Shutdown(); }
    }
}
