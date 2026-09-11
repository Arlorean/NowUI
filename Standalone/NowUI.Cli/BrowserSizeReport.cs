using System.Text.Json;

namespace NowUI.Cli;

internal sealed record BrowserFileSize(string Path, string Category, long OriginalBytes, long BrotliBytes, long GzipBytes);
internal sealed record BrowserCategorySize(string Category, long OriginalBytes, long BrotliBytes, long GzipBytes);
internal sealed record BrowserSizeReport(long OriginalBytes, long BrotliBytes, long GzipBytes, long StoredBytes,
    IReadOnlyList<BrowserFileSize> Files)
{
    internal const string FileName = "nowui-size.json";
    public IEnumerable<BrowserCategorySize> Categories => Files.GroupBy(file => file.Category)
        .Select(group => new BrowserCategorySize(group.Key, group.Sum(file => file.OriginalBytes),
            group.Sum(file => file.BrotliBytes), group.Sum(file => file.GzipBytes)))
        .OrderByDescending(category => category.BrotliBytes);
    public string Note => "Full site totals include all assets, ICU locale variants and notices. Brotli/gzip totals use the original when no encoded sidecar exists. Stored bytes include originals and both encodings. This report and its encodings are excluded. Initial browser downloads depend on locale and requested resources; these are not cold-load measurements.";

    internal static BrowserSizeReport Measure(string directory)
    {
        var paths = Directory.EnumerateFiles(directory, "*", new EnumerationOptions
        { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false })
            .ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'),
                path => new FileInfo(path).Length, StringComparer.Ordinal);
        paths.Remove(FileName);
        paths.Remove(FileName + ".br");
        paths.Remove(FileName + ".gz");
        var files = new List<BrowserFileSize>();
        foreach (var (path, bytes) in paths)
        {
            // An archive with no matching original is a resource, not a generated sidecar.
            if ((path.EndsWith(".br", StringComparison.Ordinal) || path.EndsWith(".gz", StringComparison.Ordinal)) &&
                paths.ContainsKey(path[..^3])) continue;
            files.Add(new(path, CategoryFor(path), bytes,
                paths.GetValueOrDefault(path + ".br", bytes), paths.GetValueOrDefault(path + ".gz", bytes)));
        }
        files.Sort((a, b) =>
        {
            int order = b.OriginalBytes.CompareTo(a.OriginalBytes);
            return order != 0 ? order : StringComparer.Ordinal.Compare(a.Path, b.Path);
        });
        return new(files.Sum(file => file.OriginalBytes), files.Sum(file => file.BrotliBytes),
            files.Sum(file => file.GzipBytes), paths.Values.Sum(), files);
    }

    internal static BrowserSizeReport Write(string directory)
    {
        var report = Measure(directory);
        File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return report;
    }

    static string CategoryFor(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        if (path.EndsWith(".symbols", StringComparison.OrdinalIgnoreCase)) return "diagnostics";
        if (name.StartsWith("icudt", StringComparison.Ordinal)) return "globalization";
        // Native .NET, font shaping and vector rendering share one linked WASM file.
        if (name == "dotnet.native.wasm") return "native-runtime-and-plugins";
        if (path.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase))
        {
            if (name.StartsWith("NowUI.", StringComparison.Ordinal)) return "nowui-managed-code";
            if (name.StartsWith("System.", StringComparison.Ordinal) || name is "netstandard.wasm" or "mscorlib.wasm")
                return "dotnet-managed-code";
            if (name is "AssetsTools.NET.wasm" or "YamlDotNet.wasm" or "StbImageSharp.wasm")
                return "third-party-managed-code";
            return "application-managed-code";
        }
        if (path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ttf.asset", StringComparison.OrdinalIgnoreCase)) return "fonts";
        if (path.Contains("/supportFiles/", StringComparison.Ordinal)) return "assets";
        if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase)) return "javascript";
        return "other";
    }
}
