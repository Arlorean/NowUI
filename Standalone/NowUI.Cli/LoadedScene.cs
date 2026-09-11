using NowUI.Engine;

namespace NowUI.Cli;

/// <summary>Loads a private copy so compiling a scene never overwrites a mapped assembly.</summary>
internal sealed class LoadedScene : IDisposable
{
    private SceneAssembly? loader;
    private string? directory;
    private static readonly List<string> retiredDirectories = new();
    internal string AssemblyPath { get; }
    internal Type SceneType { get; private set; }

    private LoadedScene(string original, string? requested)
    {
        directory = Path.Combine(Path.GetTempPath(), "nowui-scene-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.GetDirectoryName(original)!;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(directory, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            AssemblyPath = Path.Combine(directory, Path.GetFileName(original));
            loader = new SceneAssembly(AssemblyPath);
            SceneType = loader.FindScene(AssemblyPath, requested);
        }
        catch { Dispose(); throw; }
    }

    internal static LoadedScene Create(string path, string? requested) => new(path, requested);

    internal void ShutdownRuntime()
    {
        var assemblies = loader!.Assemblies.ToArray();
        foreach (var assembly in assemblies) NowRuntime.RegisterAssembly(assembly);
        try { NowRuntime.Shutdown(); }
        finally { foreach (var assembly in assemblies) NowRuntime.UnregisterAssembly(assembly); }
    }

    public void Dispose()
    {
        if (directory == null) return;
        SceneType = null!;
        loader?.Unload();
        loader = null;
        lock (retiredDirectories) retiredDirectories.Add(directory);
        directory = null;
    }

    /// <summary>Called between reloads and after the scene stack has unwound, outside normal drawing.</summary>
    internal static void Cleanup()
    {
        lock (retiredDirectories)
        {
            if (retiredDirectories.Count == 0) return;
            // Native libraries and mapped managed assemblies release after collectible contexts finalize.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            for (int i = retiredDirectories.Count - 1; i >= 0; i--)
            {
                try { Directory.Delete(retiredDirectories[i], recursive: true); retiredDirectories.RemoveAt(i); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
