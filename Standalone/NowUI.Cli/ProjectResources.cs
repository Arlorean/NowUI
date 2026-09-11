using NowUI.Engine;
using NowUI.Hosting;

namespace NowUI.Cli;

/// <summary>Owns one asset context for a capture or interactive preview.</summary>
internal sealed class ProjectResources : IDisposable
{
    private readonly NowFileResources builtIns = new();
    private readonly NowProjectAssets? project;
    internal INowResourceProvider Provider => (INowResourceProvider?)project ?? builtIns;
    internal string DataPath { get; }

    internal ProjectResources(RenderOptions options, string assemblyPath)
    {
        try
        {
            string? root = options.UnityProject ?? NowProjectAssets.FindProjectRoot(options.Project)
                ?? NowProjectAssets.FindProjectRoot(Environment.CurrentDirectory);
            if (root != null)
            {
                project = new NowProjectAssets(root, builtIns);
                DataPath = Path.Combine(root, "Assets");
                Console.Error.WriteLine("Project assets: " + root);
            }
            else DataPath = Path.GetDirectoryName(assemblyPath)!;
        }
        catch { builtIns.Dispose(); throw; }
    }

    public void Dispose()
    {
        try { project?.Dispose(); }
        finally { builtIns.Dispose(); }
    }
}
