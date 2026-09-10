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
                    project.AddAliases(ReadAliases(File.ReadAllText(aliasPath)));
                return new BrowserResources(builtIns, project);
            }
            catch { project?.Dispose(); throw; }
        }
        catch { builtIns.Dispose(); throw; }
    }

    internal static Dictionary<string, string> ReadAliases(string json)
    {
        // This fixed string map needs no reflection-based serializer metadata.
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Browser asset aliases must be a JSON object.");
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Browser asset alias targets must be strings: " + property.Name);
            aliases[property.Name] = property.Value.GetString();
        }
        return aliases;
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
