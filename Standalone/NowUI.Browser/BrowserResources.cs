using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NowUI.Engine;
using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Browser;

/// <summary>The ordinary host asset providers over the browser's preloaded virtual filesystem.</summary>
public sealed class BrowserResources : INowResourceProvider, IDisposable
{
    readonly NowFileResources builtIns;
    readonly NowProjectAssets project;
    bool disposed;
    BrowserResources(NowFileResources builtIns, NowProjectAssets project) { this.builtIns = builtIns; this.project = project; }

    public static BrowserResources Create()
    {
        var builtIns = new NowFileResources("/host/NowUI/Resources");
        try
        {
            var project = Directory.Exists("/project/Assets") ? new NowProjectAssets("/project", builtIns) : null;
            try
            {
                const string aliasPath = "/host/asset-aliases.json";
                if (project != null && File.Exists(aliasPath))
                    project.AddAliases(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(aliasPath)));
                return new BrowserResources(builtIns, project);
            }
            catch { project?.Dispose(); throw; }
        }
        catch { builtIns.Dispose(); throw; }
    }

    public UnityEngine.Object Load(string path, Type type)
    {
        if (disposed) throw new ObjectDisposedException(nameof(BrowserResources));
        return project != null ? project.Load(path, type) : builtIns.Load(path, type);
    }
    public Shader FindShader(string name)
    {
        if (disposed) throw new ObjectDisposedException(nameof(BrowserResources));
        return builtIns.FindShader(name);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { project?.Dispose(); }
        finally { builtIns.Dispose(); }
    }
}
