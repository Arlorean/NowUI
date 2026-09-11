using System.Reflection;
using System.Runtime.Loader;
using NowUI.Engine;
using NowUI.Hosting;

namespace NowUI.Cli;

internal sealed class SceneAssembly : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;
    private readonly string directory;

    internal SceneAssembly(string path) : base("NowUI scene", isCollectible: true)
    {
        directory = Path.GetDirectoryName(path)!;
        resolver = new AssemblyDependencyResolver(path);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // The scene and renderer must share the actual runtime statics, graphics handles and interface identity.
        foreach (var shared in new[] { typeof(INowScene).Assembly, typeof(Now).Assembly, typeof(NowRuntime).Assembly })
        {
            if (shared.GetName().Name != name.Name) continue;
            if (name.Version != shared.GetName().Version)
                throw new FileLoadException($"Scene references {name}; CLI provides {shared.GetName()}. Build both against the same NowUI version.");
            return shared;
        }
        string? path = resolver.ResolveAssemblyToPath(name);
        if (path == null && name.Name != null)
        {
            string beside = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(beside)) path = beside;
        }
        return path == null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string name)
    {
        string? path = resolver.ResolveUnmanagedDllToPath(name);
        return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    internal Type FindScene(string assemblyPath, string? requested)
    {
        var assembly = LoadFromAssemblyPath(assemblyPath);
        Type[] candidates;
        try
        {
            candidates = assembly.GetExportedTypes().Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters &&
                typeof(INowScene).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        }
        catch (ReflectionTypeLoadException exception)
        {
            throw new InvalidOperationException("Could not load scene dependencies. Set <EnableDynamicLoading>true</EnableDynamicLoading> " +
                "in the scene project so dependencies are copied to its output." + Environment.NewLine +
                string.Join(Environment.NewLine, exception.LoaderExceptions.Select(e => e?.Message)), exception);
        }
        var matches = requested == null ? candidates : candidates.Where(t => t.FullName == requested || t.Name == requested).ToArray();
        if (matches.Length == 1) return matches[0];
        string names = candidates.Length == 0 ? "(none)" : string.Join(", ", candidates.Select(t => t.FullName));
        throw new InvalidOperationException($"Select one public INowScene class with a public parameterless constructor using --scene. Available: {names}.");
    }
}
