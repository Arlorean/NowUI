using NowUI.Engine;
using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Cli;

internal sealed class CaptureHost : INowHostServices, INowClock, INowLogger, INowInputProvider, IDisposable
{
    private readonly DefaultHostServices defaults = new();
    public double realtimeSeconds { get; set; }
    internal int Errors { get; private set; }
    public INowClock clock => this;
    public INowLogger logger => this;
    public NowScreenInfo screen { get; private set; }
    public INowResourceProvider resources { get; }
    public INowImageDecoder imageDecoder { get; } = new NowImageDecoder();
    public INowClipboard clipboard { get; set; } = null!;
    public INowTouchKeyboard touchKeyboard => null!;
    internal NowHttpFetchProvider Http { get; } = new();
    public INowFetchProvider fetch => Http;
    public RuntimePlatform platform => defaults.platform;
    public string persistentDataPath => defaults.persistentDataPath;
    public string dataPath { get; }
    public string[] layerNames => defaults.layerNames;

    internal CaptureHost(int width, int height, INowResourceProvider resources, string dataPath)
    {
        screen = new NowScreenInfo(width, height, 96);
        this.resources = resources;
        this.dataPath = dataPath;
    }

    internal void Resize(int width, int height, float scale)
        => screen = new NowScreenInfo(width, height, 96 * scale);

    public void Dispose() => Http.Dispose();

    public void Log(LogType type, string message, Exception exception, UnityEngine.Object context)
    {
        if (type is LogType.Error or LogType.Assert or LogType.Exception) Errors++;
        Console.Error.WriteLine($"[{type}] {message}");
        if (exception != null) Console.Error.WriteLine(exception);
    }

    public bool TryGetSnapshot(NowInputSurface surface, out NowInputSnapshot snapshot)
    {
        snapshot = default;
        snapshot.frame = Time.frameCount;
        snapshot.inputPass = Time.frameCount;
        snapshot.time = Time.realtimeSinceStartup;
        return true;
    }
}
