using System.Diagnostics;
using NowUI.Desktop;
using NowUI.Engine;
using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Cli;

internal static class PreviewRunner
{
    internal static void Run(RenderOptions options, LoadedScene initial)
    {
        byte[]? png = null;
        using var backend = new DesktopRenderBackend(options.Width, options.Height, visible: true,
            title: "NowUI — " + initial.SceneType.Name, colorSpace: options.ColorSpace);
        using var watch = options.Watch ? new PreviewWatch(options) : null;
        LoadedScene loaded = initial;
        ProjectResources? resources = null;
        CaptureHost? host = null;
        DesktopInput? input = null;
        INowScene? scene = null;
        var clock = new Stopwatch();
        Console.Error.WriteLine("Renderer: " + backend.RendererDescription);
        try
        {
            StartScene();
            int frames = 0;
            while (!backend.IsClosing && (!options.Frames.HasValue || frames < options.Frames.Value))
            {
                backend.ProcessEvents();
                if (backend.IsClosing) break;
                var replacement = watch?.Poll();
                if (replacement != null)
                {
                    StopScene();
                    loaded.Dispose();
                    loaded = replacement;
                    LoadedScene.Cleanup();
                    StartScene();
                    Console.WriteLine("Reloaded saved code and assets; scene state restarted.");
                }
                if (backend.IsMinimized) { Thread.Sleep(20); continue; }
                float scale = backend.Width / (float)Math.Max(1, backend.Window.ClientSize.X);
                host!.Resize(backend.Width, backend.Height, scale);
                host.realtimeSeconds = clock.Elapsed.TotalSeconds;
                NowRuntime.BeginFrame();
                try
                {
                    input!.Drain(scale);
                    RenderTexture.active = null;
                    backend.ClearRenderTarget(false, true, new Color(.055f, .065f, .08f, 1), 1f);
                    using (Now.StartUI(scale))
                        scene!.Draw(new NowRect(0, 0, backend.Width / scale, backend.Height / scale));
                }
                finally { NowRuntime.EndFrame(); }
                if (host.Errors != 0) throw new InvalidOperationException($"The preview logged {host.Errors} error(s); see diagnostics above.");
                backend.Present();
                if (++frames == 1)
                    Console.WriteLine($"Preview opened: {loaded.SceneType.FullName} ({backend.Width}x{backend.Height}). " +
                        (options.Watch ? "Watching saved code and assets. " : "") + "Close the window to exit.");
            }
            if (options.Output.Length > 0 && frames > 0)
                png = NowPng.EncodeRgba(backend.Width, backend.Height, backend.ReadPixelsRgbaBottomUp());
        }
        finally
        {
            try { StopScene(); }
            finally { loaded.Dispose(); }
        }
        if (png != null)
        {
            Program.WriteOutput(options.Output, png);
            Console.WriteLine(options.Output);
        }

        void StartScene()
        {
            resources = new ProjectResources(options, loaded.AssemblyPath);
            host = new CaptureHost(backend.Width, backend.Height, resources.Provider, resources.DataPath);
            NowRuntime.RegisterAssembly(typeof(Now).Assembly);
            NowRuntime.Initialize(host, backend);
            NowRuntime.colorSpace = options.ColorSpace;
            NowRuntime.isPlaying = true;
            input = new DesktopInput(backend.Window);
            input.Install();
            host.clipboard = input.Clipboard;
            scene = (INowScene)Activator.CreateInstance(loaded.SceneType)!;
            clock.Restart();
        }

        void StopScene()
        {
            input?.Dispose(); input = null;
            try { (scene as IDisposable)?.Dispose(); }
            finally
            {
                scene = null;
                try { if (host != null) loaded.ShutdownRuntime(); }
                finally
                {
                    try { host?.Dispose(); }
                    finally { resources?.Dispose(); resources = null; }
                }
            }
            if (host?.Errors > 0) throw new InvalidOperationException($"The preview logged {host.Errors} error(s), including cleanup; no PNG was written.");
            host = null;
        }
    }
}
