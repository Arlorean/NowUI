#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using NowUI.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace NowUI.Benchmarks
{
    // Staged with the unchanged BenchmarkContent and InteractiveContent sources.
    public sealed class UnityRenderingBenchmark
    {
        [Serializable] sealed class Stats { public double median, p95, mean; }
        [Serializable] sealed class Scenario
        {
            public string scenario;
            public double scene_setup_ms, first_frame_ms;
            public Stats cpu_ms, draw_ms, submit_ms, allocations_per_frame;
            public double[] raw_cpu_ms, raw_draw_ms, raw_submit_ms, raw_allocations;
            public int vertices, batches, visible_pixels;
        }
        [Serializable] sealed class Report
        {
            public string unity_version, renderer, graphics_api, runtime = "Unity Editor Mono", color_space;
            public int width = BenchmarkContent.Width, height = BenchmarkContent.Height, warmup, samples;
            public bool native_tessellation, allocation_bytes_available, allocation_calls_available;
            public string allocation_unit;
            public Scenario[] scenarios;
            public string timing = "Identical C# Draw, scale, viewport, assets and explicit animation times. Main-thread clear/build/immediate submission only; Unity render-thread completion, coroutine yields, present, readback, PNG and file I/O are outside timing/allocation windows. VSync off. Native submits continuously while Unity yields each frame, so queue pressure differs. Editor/Mono and graphics API differ from native CoreCLR/OpenGL; this is a practical host comparison, not a Burst or engine-equivalence claim. GPU timing unavailable in this runner.";
        }
        sealed class IdleInput : INowInputProvider
        {
            public bool TryGetSnapshot(NowInputSurface surface, out NowInputSnapshot snapshot)
            {
                snapshot = default;
                snapshot.frame = Time.frameCount;
                snapshot.inputPass = Time.frameCount;
                snapshot.time = Time.realtimeSinceStartup;
                return true;
            }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator SharedRenderingWorkloads()
        {
            string output = Argument("-nowuiBenchmarkOutput");
            int warmup = int.Parse(Argument("-nowuiBenchmarkWarmup"));
            int samples = int.Parse(Argument("-nowuiBenchmarkSamples"));
            Assert.AreEqual(ColorSpace.Gamma, QualitySettings.activeColorSpace);
            int previousVsync = QualitySettings.vSyncCount, previousRate = Application.targetFrameRate;
            var previousInput = NowInput.defaultProvider;
            var previousTarget = RenderTexture.active;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            NowInput.defaultProvider = new IdleInput();
            var target = new RenderTexture(BenchmarkContent.Width, BenchmarkContent.Height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { antiAliasing = 1, filterMode = FilterMode.Point, useMipMap = false };
            Assert.IsTrue(target.Create());
            var assetNames = new[] { "1f600", "1f602", "2764", "u1f63b" };
            var bytes = new byte[assetNames.Length][];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = File.ReadAllBytes(Path.Combine(Application.dataPath, "NowUI/Assets/AnimatedEmoji", assetNames[i] + ".lottie"));
            var results = new List<Scenario>();
            using var allocations = new NowBenchmarkAllocations(reportAvailability: false);
            try
            {
                foreach (string name in new[] { "controls-text", "image-sdf", "animated-lottie" })
                {
                    long setup = Stopwatch.GetTimestamp();
                    using (var content = new BenchmarkContent(name, bytes))
                    {
                        var result = new Scenario { scenario = name, scene_setup_ms = Elapsed(setup),
                            raw_cpu_ms = new double[samples], raw_draw_ms = new double[samples],
                            raw_submit_ms = new double[samples], raw_allocations = new double[samples] };
                        result.first_frame_ms = Frame(0, content, target, allocations, out _, out _, out _);
                        yield return null;
                        for (int i = 0; i < warmup; i++)
                        { Frame(i, content, target, allocations, out _, out _, out _); yield return null; }
                        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                        for (int i = 0; i < samples; i++)
                        {
                            result.raw_cpu_ms[i] = Frame(warmup + i, content, target, allocations, out double allocated,
                                out result.raw_draw_ms[i], out result.raw_submit_ms[i]);
                            result.raw_allocations[i] = allocated;
                            yield return null;
                        }
                        result.cpu_ms = Summarize(result.raw_cpu_ms);
                        result.draw_ms = Summarize(result.raw_draw_ms);
                        result.submit_ms = Summarize(result.raw_submit_ms);
                        result.allocations_per_frame = Summarize(result.raw_allocations);
                        RenderTexture.active = target;
                        var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
                        try
                        {
                            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                            readback.Apply(false, false);
                            foreach (var pixel in readback.GetPixels32())
                                if (pixel.r > 64 || pixel.g > 64 || pixel.b > 64) result.visible_pixels++;
                            Assert.Greater(result.visible_pixels, 1000, name + " must visibly render");
                            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output), name + ".png"), readback.EncodeToPNG());
                        }
                        finally { UnityEngine.Object.Destroy(readback); }
                        var geometry = content.CaptureGeometry(warmup + samples - 1);
                        result.vertices = geometry.vertices; result.batches = geometry.batches;
                        results.Add(result);
                    }
                    yield return null;
                }
                var report = new Report { unity_version = Application.unityVersion, renderer = SystemInfo.graphicsDeviceName,
                    graphics_api = SystemInfo.graphicsDeviceType.ToString(), color_space = QualitySettings.activeColorSpace.ToString(),
                    warmup = warmup, samples = samples, native_tessellation = NowLottieNative.tessellationAvailable,
                    allocation_bytes_available = allocations.bytesAvailable, allocation_calls_available = allocations.callsAvailable,
                    allocation_unit = allocations.available ? allocations.unit : "unavailable",
                    scenarios = results.ToArray() };
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
                Debug.Log("Shared rendering benchmark: " + output);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                NowInput.defaultProvider = previousInput;
                target.Release(); UnityEngine.Object.Destroy(target);
                QualitySettings.vSyncCount = previousVsync; Application.targetFrameRate = previousRate;
            }
        }

        static double Frame(int frame, BenchmarkContent content, RenderTexture target, NowBenchmarkAllocations allocations, out double allocated, out double draw, out double submit)
        {
            allocations.Begin();
            long start = Stopwatch.GetTimestamp();
            RenderTexture.active = target;
            GL.Clear(false, true, Color.clear);
            var scope = Now.StartUI(new NowRect(0, 0, BenchmarkContent.Width, BenchmarkContent.Height), 1);
            long drawStart = Stopwatch.GetTimestamp();
            content.Draw(frame);
            long submitStart = Stopwatch.GetTimestamp();
            scope.Dispose();
            long end = Stopwatch.GetTimestamp();
            allocated = allocations.End();
            draw = (submitStart - drawStart) * 1000.0 / Stopwatch.Frequency;
            submit = (end - submitStart) * 1000.0 / Stopwatch.Frequency;
            return (end - start) * 1000.0 / Stopwatch.Frequency;
        }
        static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        static Stats Summarize(double[] data)
        {
            double[] sorted = (double[])data.Clone(); Array.Sort(sorted);
            double sum = 0; foreach (double value in data) sum += value;
            return new Stats { median = sorted[(int)Math.Ceiling((data.Length - 1) * .5)],
                p95 = sorted[(int)Math.Ceiling((data.Length - 1) * .95)], mean = sum / data.Length };
        }
        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            throw new ArgumentException("Missing " + name);
        }
    }
}
#endif
