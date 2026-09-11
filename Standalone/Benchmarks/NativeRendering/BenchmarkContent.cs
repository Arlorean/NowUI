using System;
using NowUI;
using NowUI.Sdf;
using NowUI.Samples.NativePreview;
using UnityEngine;

namespace NowUI.Benchmarks
{
    // Compiled unchanged by the native runner and the staged Unity benchmark.
    public sealed class BenchmarkContent : IDisposable
    {
        public const int Width = 1100, Height = 760;
        readonly InteractiveContent ui = new InteractiveContent { Animate = false };
        readonly Texture2D image;
        readonly NowLottieAsset[] animations;
        readonly NowSdfGraph imageGraph;
        readonly NowSdfGraph circleGraph;
        public readonly string scenario;

        public BenchmarkContent(string scenario, byte[][] animationBytes)
        {
            this.scenario = scenario;
            image = new Texture2D(32, 32, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[32 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float dx = x - 15.5f, dy = y - 15.5f;
                bool inside = dx * dx + dy * dy < 150 && !(x > 14 && y < 12);
                pixels[y * 32 + x] = new Color32(238, (byte)(80 + y * 4), 80, inside ? (byte)255 : (byte)0);
            }
            image.SetPixels32(pixels); image.Apply(false, false);
            imageGraph = NowSdf.Graph().Image(new NowRect(16, 16, 128, 128), image);
            circleGraph = NowSdf.Graph().SetColor(new Color(.25f, .65f, 1f, 1f)).Circle(new Vector2(80, 80), 53);
            animations = new NowLottieAsset[animationBytes.Length];
            for (int i = 0; i < animations.Length; i++)
            {
                animations[i] = ScriptableObject.CreateInstance<NowLottieAsset>();
                animations[i].SetSource(animationBytes[i]);
            }
        }

        public void Draw(int frame)
        {
            var view = new NowRect(0, 0, Width, Height);
            if (scenario == "controls-text") { ui.Draw(view); return; }
            Now.Rectangle(view).SetColor(new Color(.04f, .06f, .09f, 1)).Draw();
            if (scenario == "image-sdf")
            {
                float t = .5f + .45f * Mathf.Sin(frame / 60f);
                for (int i = 0; i < 8; i++)
                {
                    var rect = new NowRect(80 + i % 4 * 240, 120 + i / 4 * 260, 160, 160);
                    NowSdf.Scene(rect, 700 + i).SetOutline(2, Color.white)
                        .SetShadow(new Vector2(4, 5), 8, new Color(0, 0, 0, .6f), 1)
                        .Morph(imageGraph, circleGraph, t).Draw();
                }
                return;
            }
            for (int i = 0; i < animations.Length; i++)
                Now.Lottie(new NowRect(100 + i % 2 * 500, 30 + i / 2 * 360, 320, 320), animations[i])
                    .SetTime(frame / 60f).Draw();
        }

        // Untimed replay through the shared capture API makes the workload size reviewable.
        public (int vertices, int batches) CaptureGeometry(int frame)
        {
            using (var renderer = new NowRenderer())
            {
                using (NowInput.Begin(new Vector2(Width, Height)))
                using (renderer.Begin(new Vector2(Width, Height))) Draw(frame);
                return (renderer.mesh.vertexCount, renderer.batchCount);
            }
        }

        public void Dispose()
        {
            ui.Dispose();
            NowSdf.Reset();
            Destroy(image);
            foreach (var animation in animations) Destroy(animation);
        }
        static void Destroy(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
