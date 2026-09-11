using System.Diagnostics;
using System.Text.Json;
using NowUI;
using NowUI.Benchmarks;
using NowUI.Desktop;
using NowUI.Engine;
using NowUI.Hosting;
using NowUI.Internal;
using OpenTK.Graphics.OpenGL4;
using UnityEngine;
using GL = OpenTK.Graphics.OpenGL4.GL;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var startup = Stopwatch.StartNew();
        string output = Option(args, "--output") ?? "native-benchmark.json";
        string selected = Option(args, "--scenario") ?? "all";
        int warmup = int.Parse(Option(args, "--warmup") ?? "240");
        int samples = int.Parse(Option(args, "--samples") ?? "600");
        bool managed = args.Contains("--managed-vg");
        using var backend = new DesktopRenderBackend(BenchmarkContent.Width, BenchmarkContent.Height);
        using var resources = new NowFileResources();
        var host = new BenchmarkHost(resources);
        NowRuntime.Initialize(host, backend);
        NowRuntime.colorSpace = ColorSpace.Gamma;
        NowRuntime.isPlaying = true;
        NowInput.defaultProvider = host;
        NowLottieNative.forceManagedTessellation = managed;
        NowLottieNative.forceManagedCopy = managed;
        byte[][] animationBytes = new[] { "1f600", "1f602", "2764", "u1f63b" }
            .Select(name => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Animations", name + ".lottie"))).ToArray();
        var results = new List<object>();
        double initializationMs = startup.Elapsed.TotalMilliseconds;
        try
        {
            foreach (string scenario in new[] { "controls-text", "image-sdf", "animated-lottie" })
            {
                if (selected != "all" && selected != scenario) continue;
                var setup = Stopwatch.StartNew();
                using var content = new BenchmarkContent(scenario, animationBytes);
                double sceneSetupMs = setup.Elapsed.TotalMilliseconds;
                double firstFrameMs = Frame(0, content, backend, host, out _, out _, out _);
                if (results.Count == 0) Console.WriteLine("NOWUI_BENCHMARK_READY");
                for (int i = 0; i < warmup; i++) Frame(i, content, backend, host, out _, out _, out _);
                GL.Finish();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var cpu = new double[samples];
                var draw = new double[samples];
                var submit = new double[samples];
                var allocated = new long[samples];
                var gpu = new double[samples];
                int[] queries = new int[samples];
                GL.GenQueries(samples, queries);
                // Timer query retrieval and synchronization happen only after the complete measured block.
                for (int i = 0; i < samples; i++)
                {
                    GL.BeginQuery(QueryTarget.TimeElapsed, queries[i]);
                    cpu[i] = Frame(warmup + i, content, backend, host, out allocated[i], out draw[i], out submit[i]);
                    GL.EndQuery(QueryTarget.TimeElapsed);
                }
                GL.Finish();
                for (int i = 0; i < samples; i++)
                {
                    GL.GetQueryObject(queries[i], GetQueryObjectParam.QueryResult, out long nanoseconds);
                    gpu[i] = nanoseconds / 1_000_000.0;
                }
                GL.DeleteQueries(samples, queries);
                // Verification/artifacts are outside every measured timing/allocation window.
                byte[] pixels = backend.ReadPixelsRgbaBottomUp();
                string imagePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, scenario + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
                File.WriteAllBytes(imagePath, NowPng.EncodeRgba(backend.Width, backend.Height, pixels));
                int changed = 0;
                for (int i = 0; i < pixels.Length; i += 4) if (pixels[i] > 64 || pixels[i + 1] > 64 || pixels[i + 2] > 64) changed++;
                if (changed < 1000) throw new InvalidOperationException("Benchmark produced insufficient visible geometry: " + scenario);
                // Separate allocation probes leave the timed loop and its timestamp count unchanged.
                long setupBytes = 0, drawBytes = 0, submitBytes = 0;
                const int allocationProbeFrames = 64;
                for (int i = 0; i < allocationProbeFrames; i++)
                {
                    AllocationProbe(warmup + samples + i, content, backend, host, out long a, out long b, out long c);
                    setupBytes += a; drawBytes += b; submitBytes += c;
                }
                NowRuntime.BeginFrame();
                var geometry = content.CaptureGeometry(warmup + samples - 1);
                NowRuntime.EndFrame();
                if (scenario == "animated-lottie")
                {
                    foreach (int validationFrame in new[] { 0, 37, 121, 317, 599 })
                    {
                        Frame(validationFrame, content, backend, host, out _, out _, out _);
                        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(imagePath)!, $"{scenario}-{validationFrame:D3}.png"),
                            NowPng.EncodeRgba(backend.Width, backend.Height, backend.ReadPixelsRgbaBottomUp()));
                    }
                }
                results.Add(new { scenario, scene_setup_ms = sceneSetupMs, first_frame_ms = firstFrameMs,
                    cpu_ms = Stats(cpu), draw_ms = Stats(draw), submit_ms = Stats(submit), gpu_ms = Stats(gpu),
                    allocated_bytes_per_frame = new { mean = allocated.Average(), median = Percentile(allocated.Select(x => (double)x).ToArray(), .5), max = allocated.Max() },
                    allocation_probe = new { frames = allocationProbeFrames, setup_bytes = setupBytes / (double)allocationProbeFrames,
                        draw_bytes = drawBytes / (double)allocationProbeFrames, submit_bytes = submitBytes / (double)allocationProbeFrames },
                    geometry = new { vertices = geometry.vertices, batches = geometry.batches },
                    raw_cpu_ms = cpu, raw_gpu_ms = gpu, raw_allocated_bytes = allocated, visible_pixels = changed });
            }
            var report = new { format_version = 1, renderer = backend.RendererDescription, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, width = backend.Width, height = backend.Height,
                color_space = "Gamma", warmup, samples, native_vg_available = NowLottieNative.available,
                native_tessellation = NowLottieNative.tessellationAvailable, managed_vg_requested = managed,
                initialize_from_main_ms = initializationMs, scenarios = results,
                timing = "CPU frame build and GPU submission; GPU elapsed query separately. GPU query intervals can include GPU idle while the CPU submits and are not pure shader execution cost. Query retrieval, synchronization, readback, PNG and file I/O are outside per-frame CPU timings. Hidden window, VSync off. Startup excludes process launch and project compilation. Allocation phase probes run separately after validation capture." };
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(output);
            return 0;
        }
        finally { NowRuntime.Shutdown(); }
    }

    static double Frame(int frame, BenchmarkContent content, DesktopRenderBackend backend, BenchmarkHost host,
        out long allocated, out double drawMs, out double submitMs)
    {
        host.realtimeSeconds = frame / 60.0;
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        NowRuntime.BeginFrame();
        RenderTexture.active = null;
        backend.ClearRenderTarget(false, true, Color.clear, 1);
        var scope = Now.StartUI(1f);
        long drawStart = Stopwatch.GetTimestamp();
        content.Draw(frame);
        long submitStart = Stopwatch.GetTimestamp();
        scope.Dispose();
        NowRuntime.EndFrame();
        long end = Stopwatch.GetTimestamp();
        allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        drawMs = (submitStart - drawStart) * 1000.0 / Stopwatch.Frequency;
        submitMs = (end - submitStart) * 1000.0 / Stopwatch.Frequency;
        return (end - start) * 1000.0 / Stopwatch.Frequency;
    }
    static object Stats(double[] values) => new { median = Percentile(values, .5), p95 = Percentile(values, .95), mean = values.Average() };
    static void AllocationProbe(int frame, BenchmarkContent content, DesktopRenderBackend backend, BenchmarkHost host,
        out long setup, out long draw, out long submit)
    {
        host.realtimeSeconds = frame / 60.0;
        long a = GC.GetAllocatedBytesForCurrentThread();
        NowRuntime.BeginFrame();
        RenderTexture.active = null;
        backend.ClearRenderTarget(false, true, Color.clear, 1);
        var scope = Now.StartUI(1f);
        long b = GC.GetAllocatedBytesForCurrentThread();
        content.Draw(frame);
        long c = GC.GetAllocatedBytesForCurrentThread();
        scope.Dispose();
        NowRuntime.EndFrame();
        long d = GC.GetAllocatedBytesForCurrentThread();
        setup = b - a; draw = c - b; submit = d - c;
    }
    static double Percentile(double[] values, double p) { var sorted = (double[])values.Clone(); Array.Sort(sorted); return sorted[(int)Math.Ceiling((sorted.Length - 1) * p)]; }
    static string? Option(string[] args, string name) { int index = Array.IndexOf(args, name); return index < 0 ? null : args[index + 1]; }
}

internal sealed class BenchmarkHost : INowHostServices, INowClock, INowInputProvider
{
    readonly DefaultHostServices defaults = new();
    public BenchmarkHost(INowResourceProvider resources) { this.resources = resources; }
    public double realtimeSeconds { get; set; }
    public INowClock clock => this;
    public NowScreenInfo screen => new(BenchmarkContent.Width, BenchmarkContent.Height, 96);
    public INowLogger logger => defaults.logger;
    public INowClipboard? clipboard => null;
    public INowTouchKeyboard? touchKeyboard => null;
    public INowResourceProvider resources { get; }
    public INowImageDecoder imageDecoder { get; } = new NowPngImageDecoder();
    public INowFetchProvider? fetch => null;
    public RuntimePlatform platform => defaults.platform;
    public string persistentDataPath => defaults.persistentDataPath;
    public string dataPath => AppContext.BaseDirectory;
    public string[] layerNames => defaults.layerNames;
    public bool TryGetSnapshot(NowInputSurface surface, out NowInputSnapshot snapshot)
    { snapshot = default; snapshot.frame = Time.frameCount; snapshot.inputPass = Time.frameCount; snapshot.time = Time.realtimeSinceStartup; return true; }
}
