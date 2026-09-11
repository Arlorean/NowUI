using NowUI.Hosting;

namespace NowUI.Cli;

/// <summary>Debounced file notifications; builds run away from the graphics thread.</summary>
internal sealed class PreviewWatch : IDisposable
{
    private readonly List<FileSystemWatcher> watchers = new();
    private long changedAt;
    private int generation;
    private int startedGeneration;
    private Task<LoadedScene>? pending;
    private readonly RenderOptions options;
    private readonly CancellationTokenSource cancellation = new();
    private bool disposed;

    internal PreviewWatch(RenderOptions options)
    {
        this.options = options;
        string sceneRoot = Path.GetDirectoryName(options.Project)!;
        string? unityRoot = options.UnityProject ?? NowProjectAssets.FindProjectRoot(options.Project)
            ?? NowProjectAssets.FindProjectRoot(Environment.CurrentDirectory);
        var roots = new[] { sceneRoot, unityRoot }.Where(p => p != null).Select(p => Path.GetFullPath(p!))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var root in roots)
        {
            var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
            watcher.Changed += Changed; watcher.Created += Changed; watcher.Deleted += Changed; watcher.Renamed += Renamed;
            watcher.Error += (_, _) => MarkChanged();
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
    }

    internal static bool IsRelevant(string path)
    {
        string[] segments = path.Replace('\\', '/').Split('/');
        if (segments.Any(s => s is "bin" or "obj" or ".git" or "Library" or "Temp" or "Logs" or "artifacts")) return false;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".cs" or ".csproj" or ".props" or ".targets" or ".asset" or ".meta" or ".png" or ".jpg"
            or ".jpeg" or ".tga" or ".bmp" or ".ttf" or ".otf" or ".json" or ".mat" or ".lottie";
    }

    private void Changed(object sender, FileSystemEventArgs e)
    {
        if (IsRelevant(Path.GetRelativePath(((FileSystemWatcher)sender).Path, e.FullPath))) MarkChanged();
    }
    private void Renamed(object sender, RenamedEventArgs e)
    {
        string root = ((FileSystemWatcher)sender).Path;
        if (IsRelevant(Path.GetRelativePath(root, e.FullPath)) || IsRelevant(Path.GetRelativePath(root, e.OldFullPath))) MarkChanged();
    }
    private void MarkChanged() { Interlocked.Exchange(ref changedAt, Environment.TickCount64); Interlocked.Increment(ref generation); }

    internal LoadedScene? Poll()
    {
        if (pending is { IsCompleted: true })
        {
            var result = pending;
            pending = null;
            try { return result.GetAwaiter().GetResult(); }
            catch (Exception error) { Console.Error.WriteLine("Reload failed; keeping the current preview. " + error.Message); }
        }
        int current = Volatile.Read(ref generation);
        if (pending == null && current != startedGeneration && Environment.TickCount64 - Interlocked.Read(ref changedAt) >= 400)
        {
            startedGeneration = current;
            pending = Task.Run(() =>
            {
                string path = ProjectBuilder.Build(options with { NoBuild = false }, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                return LoadedScene.Create(path, options.Scene);
            });
        }
        return null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var watcher in watchers) watcher.Dispose();
        cancellation.Cancel();
        if (pending != null)
        {
            try { pending.GetAwaiter().GetResult().Dispose(); }
            catch (Exception) { /* Closing the preview also cancels a pending build. */ }
        }
        cancellation.Dispose();
    }
}
