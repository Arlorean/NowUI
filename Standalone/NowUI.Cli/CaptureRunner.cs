using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using NowUI.Desktop;
using NowUI.Engine;
using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Cli;

internal static class CaptureRunner
{
    internal static void Run(RenderOptions options, LoadedScene loaded)
    {
        string assemblyPath = loaded.AssemblyPath;
        Type sceneType = loaded.SceneType;
        double lastTime = options.Animate ? (Math.Max(1, (int)Math.Ceiling(options.Duration * options.Fps)) - 1) / (double)options.Fps : options.Time;
        var replay = InputReplay.Load(options.Input, options.Fps, lastTime);
        string? staging = null;
        byte[]? png = null;
        if (options.Animate)
        {
            string parent = Path.GetDirectoryName(options.Output)!;
            Directory.CreateDirectory(parent);
            staging = Path.Combine(parent, ".nowui-frames-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
        }
        try
        {
            using (var backend = new DesktopRenderBackend(options.Width, options.Height, visible: false, colorSpace: options.ColorSpace))
            using (var resources = new ProjectResources(options, assemblyPath))
            {
                using var host = new CaptureHost(options.Width, options.Height, resources.Provider, resources.DataPath);
                Console.Error.WriteLine("Renderer: " + backend.RendererDescription);
                NowRuntime.RegisterAssembly(typeof(Now).Assembly);
                NowRuntime.Initialize(host, backend);
                NowRuntime.colorSpace = options.ColorSpace;
                NowRuntime.isPlaying = true;
                using var input = new DesktopInput();
                input.SetViewportSize(options.Width, options.Height, options.Width, options.Height);
                input.Install();
                host.clipboard = input.Clipboard;
                INowScene? scene = null;
                long settledRequests = 0;
                try
                {
                    scene = (INowScene)Activator.CreateInstance(sceneType)!;
                    DrawFrame(0, -1);
                    DrawFrame(0, -1);
                    SettleRemote(0);
                    if (options.Animate)
                    {
                        int frames = Math.Max(1, (int)Math.Ceiling(options.Duration * options.Fps));
                        for (int frame = 0; frame < frames; frame++)
                        {
                            DrawFrame(frame / (double)options.Fps, frame);
                            SettleRemote(frame / (double)options.Fps);
                            File.WriteAllBytes(Path.Combine(staging!, $"frame-{frame:D6}.png"), Encode());
                        }
                        File.WriteAllText(Path.Combine(staging!, "animation.json"), JsonSerializer.Serialize(new
                        {
                            width = options.Width, height = options.Height, fps = options.Fps, frames,
                            duration = frames / (double)options.Fps, colorSpace = options.ColorSpace.ToString(), pattern = "frame-%06d.png"
                        }, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    else
                    {
                        DrawFrame(0, 0);
                        SettleRemote(0);
                        int steps = (int)Math.Ceiling(options.Time * options.Fps);
                        for (int step = 1; step <= steps; step++)
                        {
                            double time = Math.Min(step / (double)options.Fps, options.Time);
                            DrawFrame(time, step);
                            SettleRemote(time);
                        }
                        png = Encode();
                    }

                    byte[] Encode() => NowPng.EncodeRgba(options.Width, options.Height, backend.ReadPixelsRgbaBottomUp());
                    void SettleRemote(double time)
                    {
                        bool Pending() => host.Http.pendingCount > 0 || host.Http.requestCount != settledRequests || NowLottieCache.pendingDownloadCount > 0;
                        if (!Pending()) return;
                        var waiting = Stopwatch.StartNew();
                        // The core's coroutines publish results on frame boundaries. Keep playback time fixed
                        // while transport finishes. Ordinary frames with no remote work draw exactly once.
                        while (Pending())
                        {
                            if (waiting.Elapsed.TotalSeconds > options.LoadTimeout)
                                throw new TimeoutException($"Remote resources did not settle within --load-timeout {options.LoadTimeout} seconds; no output was published.");
                            settledRequests = host.Http.requestCount;
                            if (host.Http.pendingCount > 0) Thread.Sleep(5);
                            DrawFrame(time, -1);
                        }
                    }
                    void DrawFrame(double time, int frame)
                    {
                        host.realtimeSeconds = time;
                        NowRuntime.BeginFrame();
                        try
                        {
                            if (frame >= 0) replay.Apply(frame, input);
                            input.Drain();
                            RenderTexture.active = null;
                            backend.ClearRenderTarget(false, true, Color.clear, 1f);
                            using (Now.StartUI(1f)) scene.Draw(new NowRect(0, 0, options.Width, options.Height));
                        }
                        finally { NowRuntime.EndFrame(); }
                        if (host.Errors != 0) throw new InvalidOperationException($"The scene logged {host.Errors} error(s); no output was published.");
                    }
                }
                finally
                {
                    try { (scene as IDisposable)?.Dispose(); }
                    finally { loaded.ShutdownRuntime(); }
                }
                if (host.Errors != 0) throw new InvalidOperationException($"The scene logged {host.Errors} error(s), including cleanup; no output was published.");
            }
            if (staging != null)
            {
                Directory.Move(staging, options.Output);
                staging = null;
                Console.WriteLine($"Animated {sceneType.FullName} ({options.Fps} fps, {options.Duration.ToString("G", CultureInfo.InvariantCulture)}s)");
            }
            else
            {
                Program.WriteOutput(options.Output, png!);
                Console.WriteLine($"Rendered {sceneType.FullName} ({options.Width}x{options.Height}, t={options.Time.ToString("G", CultureInfo.InvariantCulture)}s)");
            }
            Console.WriteLine(options.Output);
        }
        finally { if (staging != null) Directory.Delete(staging, recursive: true); }
    }
}
