using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NowUI.Hosting;

namespace NowUI.Cli;

internal sealed record BrowserAssetBundle(string PropsPath, string ReportPath, int ProjectFiles, int HostFiles, long Bytes, bool HasProject);

/// <summary>Stages a bounded set of original UI source assets for the same provider on the browser VFS.</summary>
internal static class BrowserAssets
{
    const long FileLimit = 64L * 1024 * 1024;
    const long DefaultByteLimit = 256L * 1024 * 1024;
    const int DefaultFileLimit = 4096;
    sealed record Entry(string Source, string VirtualPath, long Bytes, bool Project);

    internal static BrowserAssetBundle Stage(string? unityProject, string hostResourceDirectory, string stagingDirectory,
        long maxBytes = DefaultByteLimit, int maxFiles = DefaultFileLimit, IReadOnlyCollection<string>? roots = null)
    {
        if (maxBytes < 1 || maxFiles < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        string hostRoot = Path.GetFullPath(hostResourceDirectory);
        string staging = Path.GetFullPath(stagingDirectory);
        if (!Directory.Exists(hostRoot)) throw new DirectoryNotFoundException("NowUI browser resources are missing: " + hostRoot);
        if (Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).Any())
            throw new IOException("Browser asset staging must be empty: " + staging);

        var entries = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        long total = 0;
        int skipped = 0;
        var aliases = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        foreach (string file in Directory.EnumerateFiles(hostRoot, "*", enumeration).OrderBy(p => p, StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(hostRoot, file).Replace('\\', '/');
            if (relative is "shaders.json" or "materials.json" || relative.StartsWith("NowUI/", StringComparison.Ordinal)
                && (relative.EndsWith(".font.json", StringComparison.Ordinal) || relative.EndsWith(".family.json", StringComparison.Ordinal)
                    || Path.GetExtension(relative) is ".ttf" or ".otf"))
                Add(file, "/host/NowUI/Resources/" + relative, false);
        }
        if (!entries.ContainsKey("/host/NowUI/Resources/shaders.json") || !entries.ContainsKey("/host/NowUI/Resources/materials.json"))
            throw new InvalidDataException("The browser requires the bundled NowUI shader and material resources.");

        bool hasProject = !string.IsNullOrWhiteSpace(unityProject);
        if (hasProject)
        {
            using var builtIns = new NowFileResources(hostRoot);
            using var project = new NowProjectAssets(unityProject!, builtIns);
            var files = project.IndexedFiles();
            var selected = roots == null ? files.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase) : Select(files, roots);
            var pending = new Queue<string>(selected.OrderBy(value => value, StringComparer.Ordinal));
            var inspected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guids = project.IndexedGuids();
            var canonical = files.GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).First(), StringComparer.OrdinalIgnoreCase);
            var emitted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (pending.TryDequeue(out string? path))
            {
                if (!inspected.Add(path)) continue;
                var pair = new KeyValuePair<string, string>(path, files[path]);
                if (roots != null && (Path.GetExtension(pair.Value).ToLowerInvariant() is ".asset" or ".json") && new FileInfo(pair.Value).Length > FileLimit)
                    throw new InvalidDataException($"Selected browser asset exceeds the 64 MiB per-file limit: {path}");
                if (!Supported(pair.Value, pair.Key)) { skipped++; continue; }
                if (emitted.TryGetValue(pair.Value, out string? firstPath))
                {
                    aliases.Add(path, firstPath);
                    continue;
                }
                emitted.Add(pair.Value, path);
                Add(pair.Value, "/project/" + pair.Key, true);
                string meta = pair.Value + ".meta";
                if (File.Exists(meta)) Add(meta, "/project/" + pair.Key + ".meta", true);
                if (!Path.GetExtension(pair.Value).Equals(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string guid in NowUnitySerializedAssets.ReferencedGuids(pair.Value))
                {
                    if (!guids.TryGetValue(guid, out var targets) || targets.Count == 0)
                        throw new InvalidDataException($"Browser asset '{path}' references missing GUID {guid}.");
                    if (targets.Count != 1) throw new InvalidDataException($"Browser asset '{path}' references duplicate GUID {guid}: {string.Join(", ", targets)}");
                    string dependency = canonical[targets[0]];
                    if (!Supported(targets[0], dependency))
                        throw new InvalidDataException($"Browser asset '{path}' references unsupported asset '{dependency}' (GUID {guid}).");
                    if (selected.Add(dependency)) pending.Enqueue(dependency);
                }
            }
        }

        // All counts and sizes are checked before creating output or copying any project bytes.
        Directory.CreateDirectory(staging);
        var items = new XElement("ItemGroup");
        var reportFiles = new List<object>(entries.Count);
        foreach (var entry in entries.Values)
        {
            string destination = Path.GetFullPath(Path.Combine(staging, "vfs", entry.VirtualPath.TrimStart('/')));
            if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid browser asset path: " + entry.VirtualPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(entry.Source, destination, overwrite: false);
            if (new FileInfo(destination).Length != entry.Bytes)
                throw new IOException("Asset changed during browser staging; retry the build: " + entry.VirtualPath);
            items.Add(VfsItem(destination, entry.VirtualPath));
            using var contents = File.OpenRead(destination);
            reportFiles.Add(new { path = entry.VirtualPath, bytes = entry.Bytes, sha256 = Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant() });
        }
        if (hasProject)
        {
            // A project with no supported assets still has an Assets root on the VFS.
            string marker = Path.Combine(staging, "project-root.txt");
            File.WriteAllText(marker, "NowUI browser project assets\n");
            items.Add(VfsItem(marker, "/project/Assets/.nowui-root"));
        }
        if (aliases.Count > 0)
        {
            string aliasFile = Path.Combine(staging, "vfs/host/asset-aliases.json");
            File.WriteAllText(aliasFile, JsonSerializer.Serialize(aliases));
            items.Add(VfsItem(aliasFile, "/host/asset-aliases.json"));
        }
        string props = Path.Combine(staging, "BrowserAssets.props");
        new XDocument(new XElement("Project", items)).Save(props);
        int projectFiles = entries.Values.Count(p => p.Project);
        string report = Path.Combine(staging, "assets.json");
        File.WriteAllText(report, JsonSerializer.Serialize(new { version = 1, projectFiles, hostFiles = entries.Count - projectFiles,
            bytes = total, skippedUnsupportedFiles = skipped, aliases, files = reportFiles }, new JsonSerializerOptions { WriteIndented = true }));
        return new BrowserAssetBundle(props, report, projectFiles, entries.Count - projectFiles, total, hasProject);

        void Add(string source, string virtualPath, bool project)
        {
            if (virtualPath.Split('/').Any(part => part is "." or "..")) throw new InvalidDataException("Invalid asset path: " + virtualPath);
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Browser asset symlinks are unsupported: " + virtualPath);
            long length = new FileInfo(source).Length;
            if (length > FileLimit) throw new InvalidDataException($"Browser asset exceeds the 64 MiB per-file limit: {virtualPath}");
            if (entries.ContainsKey(virtualPath)) throw new InvalidDataException("Duplicate browser asset path: " + virtualPath);
            if (entries.Count >= maxFiles || length > maxBytes - total)
                throw new InvalidDataException($"Browser assets exceed the bundle limit ({maxFiles} files, {maxBytes} bytes). No asset files were staged.");
            entries.Add(virtualPath, new Entry(source, virtualPath, length, project));
            total += length;
        }
    }

    static HashSet<string> Select(IReadOnlyDictionary<string, string> files, IReadOnlyCollection<string> roots)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string literal in roots)
        {
            if (literal.Length == 0 || literal.Contains('\n') || literal.Contains('\r')) continue;
            string root = literal.Replace('\\', '/');
            int selector = root.LastIndexOf('#');
            if (selector >= 0) root = root[..selector];
            if (files.ContainsKey(root))
            {
                selected.Add(files.Keys.First(path => string.Equals(path, root, StringComparison.OrdinalIgnoreCase)));
                continue;
            }
            bool prefix = root.EndsWith("/", StringComparison.Ordinal);
            foreach (string path in files.Keys)
            {
                int resources = path.IndexOf("/Resources/", StringComparison.Ordinal);
                string alias = resources < 0 ? "" : path[(resources + "/Resources/".Length)..^Path.GetExtension(path).Length];
                if (!prefix && alias == root || prefix && !Excluded(path) &&
                    (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || alias.StartsWith(root, StringComparison.Ordinal)))
                    selected.Add(path);
            }
        }
        return selected;
    }

    static bool Excluded(string path) => path.Split('/').Any(part => part.Equals("Editor", StringComparison.OrdinalIgnoreCase)
        || part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) || part.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || part.Equals("obj", StringComparison.OrdinalIgnoreCase) || part.StartsWith(".", StringComparison.Ordinal));

    static XElement VfsItem(string physical, string virtualPath) => new("WasmFilesToIncludeInFileSystem",
        new XAttribute("Include", MsBuildLiteral(physical)), new XAttribute("TargetPath", MsBuildLiteral(virtualPath)));

    // XML quoting alone does not prevent MSBuild property/item expansion or wildcard/semicolon splitting.
    internal static string MsBuildLiteral(string value) => value.Replace("%", "%25", StringComparison.Ordinal)
        .Replace("$", "%24", StringComparison.Ordinal).Replace("@", "%40", StringComparison.Ordinal)
        .Replace(";", "%3B", StringComparison.Ordinal).Replace("'", "%27", StringComparison.Ordinal)
        .Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal)
        .Replace("*", "%2A", StringComparison.Ordinal).Replace("?", "%3F", StringComparison.Ordinal);

    static bool Supported(string file, string projectPath)
    {
        string extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".ttf" or ".otf" or ".lottie") return true;
        if (extension == ".mat")
        {
            int resource = projectPath.IndexOf("/Resources/", StringComparison.Ordinal);
            string alias = resource < 0 ? "" : projectPath[(resource + "/Resources/".Length)..^4];
            return NowFileResources.requiredMaterialPaths.Contains(alias) || alias == "NowUI/GlassMaterialUGUI";
        }
        if (extension is not (".asset" or ".json") || new FileInfo(file).Length > FileLimit) return false;
        try
        {
            if (extension == ".asset") return NowUnitySerializedAssets.IsKnownAsset(file);
            using var stream = File.OpenRead(file);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            // Ordinary project/config JSON is never provisioned. This is the Lottie document shape.
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("v", out var version) && version.ValueKind == JsonValueKind.String
                && root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array
                && Number("w") && Number("h") && Number("fr") && Number("ip") && Number("op");
            bool Number(string property) => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or JsonException
            or YamlDotNet.Core.YamlException or EndOfStreamException or ArgumentException or IndexOutOfRangeException)
        {
            return false; // A custom/unsupported serialized asset is outside the browser asset contract.
        }
    }
}
