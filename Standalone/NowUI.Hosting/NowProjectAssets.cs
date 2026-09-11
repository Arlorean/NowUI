using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NowUI.Engine;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NowUI.Hosting
{
    /// <summary>Reads supported Unity project assets directly, without an Editor or export step.</summary>
    /// <remarks>
    /// Accepts Unity Resources paths, project paths such as Assets/UI/logo.png, and
    /// sprite names after #. Loaded objects are shared for this provider's lifetime;
    /// callers must not destroy them. A new provider reads the latest files.
    /// </remarks>
    public sealed class NowProjectAssets : INowResourceProvider, IDisposable
    {
        private readonly NowFileResources builtIns;
        private readonly Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> guids = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> resources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Object> loaded = new(StringComparer.Ordinal);
        private readonly List<Object> owned = new();
        private readonly HashSet<string> loading = new(StringComparer.Ordinal);
        private bool indexed, disposed;
        private int loadDepth;

        /// <summary>Creates a lazy asset reader. The caller retains ownership of built-in resources.</summary>
        public NowProjectAssets(string projectRoot, NowFileResources builtIns)
        {
            this.projectRoot = Path.GetFullPath(projectRoot ?? throw new ArgumentNullException(nameof(projectRoot)));
            if (!Directory.Exists(Path.Combine(this.projectRoot, "Assets")))
                throw new DirectoryNotFoundException("Unity project has no Assets directory: " + this.projectRoot);
            this.builtIns = builtIns ?? throw new ArgumentNullException(nameof(builtIns));
        }

        public string projectRoot { get; }

        // Build hosts can provision these same canonical paths without reimplementing package resolution.
        internal IReadOnlyDictionary<string, string> IndexedFiles()
        {
            ThrowIfDisposed();
            EnsureIndex();
            return paths;
        }

        internal IReadOnlyDictionary<string, List<string>> IndexedGuids()
        {
            ThrowIfDisposed();
            EnsureIndex();
            return guids;
        }

        // A local package can also live below Assets. Browser staging keeps one physical file
        // and restores its other logical path instead of manufacturing a second GUID identity.
        internal void AddAliases(IReadOnlyDictionary<string, string> aliases)
        {
            ThrowIfDisposed();
            EnsureIndex();
            foreach (var alias in aliases)
            {
                if (!paths.TryGetValue(alias.Value, out string source))
                    throw new InvalidDataException("Browser asset alias target is missing: " + alias.Value);
                if (paths.TryGetValue(alias.Key, out string previous) && !string.Equals(previous, source, StringComparison.Ordinal))
                    throw new InvalidDataException("Browser asset alias conflicts with a file: " + alias.Key);
                paths[alias.Key] = source;
                int resourceIndex = alias.Key.IndexOf("/Resources/", StringComparison.Ordinal);
                if (resourceIndex < 0) continue;
                string resourceName = alias.Key.Substring(resourceIndex + "/Resources/".Length);
                resourceName = resourceName.Substring(0, resourceName.Length - Path.GetExtension(resourceName).Length);
                Add(resources, resourceName, source);
            }
        }

        /// <summary>Finds a Unity project containing the supplied directory or file; null outside a project.</summary>
        public static string FindProjectRoot(string startingPath)
        {
            if (string.IsNullOrWhiteSpace(startingPath)) return null;
            string full = Path.GetFullPath(startingPath);
            var directory = new DirectoryInfo(File.Exists(full) || Path.HasExtension(full) && !Directory.Exists(full)
                ? Path.GetDirectoryName(full) : full);
            for (; directory != null; directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings"))) return directory.FullName;
            return null;
        }

        /// <summary>Loads by Resources path or project path, returning null for a missing asset or wrong type.</summary>
        public T LoadAsset<T>(string path) where T : Object => Load(path, typeof(T)) as T;

        /// <summary>Loads a particular subasset using its Unity local file identifier.</summary>
        public Object LoadAsset(string path, Type type, long? localFileId = null)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(path)) return null;
            EnsureIndex();
            string selector = null;
            int hash = path.LastIndexOf('#');
            if (hash >= 0) { selector = path.Substring(hash + 1); path = path.Substring(0, hash); }
            string normalized = path.Replace('\\', '/');
            string absolute = null;
            if (Path.IsPathRooted(path))
            {
                string full = Path.GetFullPath(path);
                absolute = paths.Values.FirstOrDefault(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            }
            else if (!paths.TryGetValue(normalized, out absolute) && resources.TryGetValue(normalized, out var candidates))
            {
                if (candidates.Count != 1)
                    throw new InvalidDataException("Ambiguous Resources path '" + path + "': " + string.Join(", ", candidates));
                absolute = candidates[0];
            }
            if (absolute == null) return null;
            return LoadResolved(absolute, type, selector, localFileId);
        }

        /// <inheritdoc />
        public Object Load(string path, Type type)
        {
            ThrowIfDisposed();
            // Native stock shader programs use the host's matching material templates.
            // Preserve that exact mapping rather than deserialize arbitrary Unity shaders.
            if (type != null && (typeof(Material).IsAssignableFrom(type) || typeof(Shader).IsAssignableFrom(type)))
            {
                var template = builtIns.Load(path, type);
                if (template != null) return template;
            }
            var asset = LoadAsset(path, type);
            return asset ?? builtIns.Load(path, type);
        }

        public Shader FindShader(string name) { ThrowIfDisposed(); return builtIns.FindShader(name); }

        internal Object ResolveGuid(string guid, long localId, Type type)
        {
            if (localId == 0) return null;
            EnsureIndex();
            if (!guids.TryGetValue(guid, out var candidates))
                throw new FileNotFoundException("Asset reference GUID " + guid + " (fileID " + localId + ") was not found in " + projectRoot);
            if (candidates.Count != 1)
                throw new InvalidDataException("Duplicate asset GUID " + guid + ": " + string.Join(", ", candidates));
            return LoadResolved(candidates[0], type, null, localId);
        }

        private Object LoadResolved(string absolute, Type type, string selector, long? localId)
        {
            string extension = Path.GetExtension(absolute).ToLowerInvariant();
            if (extension == ".mat")
            {
                // A baked font can reference one of NowUI's standard material assets.
                // Use the native renderer's matching program/template for those exact resource paths.
                string portablePath = absolute.Replace('\\', '/');
                int marker = portablePath.IndexOf("/Resources/", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    string name = portablePath.Substring(marker + "/Resources/".Length);
                    var material = builtIns.Load(name.Substring(0, name.Length - 4), type);
                    if (material != null) return material;
                }
            }
            bool texture = extension is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp";
            bool lottie = extension is ".lottie" or ".json";
            bool sprite = texture && (type == typeof(Sprite) || selector != null || localId.HasValue && localId != 2800000);
            if (texture && type != null && !type.IsAssignableFrom(sprite ? typeof(Sprite) : typeof(Texture2D))) return null;
            // Main ScriptableObject fileID is normally 11400000; null loads and references must share a shell for cycles.
            string identity = extension == ".asset" ? (localId ?? 11400000).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : texture ? (sprite ? "sprite:" + (selector ?? localId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "main") : "texture") : "main";
            if (lottie && localId.HasValue && localId.Value != NowLottieAssets.ImportedFileId && localId.Value != 11400000)
                throw new InvalidDataException($"Lottie '{absolute}' has no animation subasset with fileID {localId}.");
            if (lottie && selector != null && selector != "animation")
                throw new InvalidDataException($"Lottie '{absolute}' has no named subasset '{selector}'; its main object is animation.");
            string key = absolute + "#" + identity;
            if (loaded.TryGetValue(key, out var existing) && existing != null)
                return type == null || type.IsInstanceOfType(existing) ? existing : null;
            if (!loading.Add(key)) throw new InvalidDataException("Asset reference cycle could not be resolved: " + absolute);
            bool outermost = loadDepth++ == 0;
            int checkpoint = owned.Count;
            var previousKeys = outermost ? new HashSet<string>(loaded.Keys, StringComparer.Ordinal) : null;
            try
            {
                void Own(Object value)
                {
                    if (value == null) return;
                    owned.Add(value);
                    if (!loaded.ContainsKey(key)) loaded[key] = value;
                }
                Object result;
                if (texture)
                {
                    if (sprite)
                    {
                        var image = (Texture2D)LoadResolved(absolute, typeof(Texture2D), null, 2800000);
                        result = NowUnityTextureAssets.LoadSprite(absolute, selector, localId, image, Own);
                        var spriteResult = (Sprite)result;
                        // A named lookup and a serialized fileID reference denote the same sprite.
                        var canonical = loaded.Values.OfType<Sprite>().FirstOrDefault(s => s != null &&
                            !ReferenceEquals(s, spriteResult) && ReferenceEquals(s.texture, image) &&
                            s.name == spriteResult.name && s.rect.Equals(spriteResult.rect) && s.border.Equals(spriteResult.border) &&
                            s.pivot.Equals(spriteResult.pivot) && s.pixelsPerUnit == spriteResult.pixelsPerUnit);
                        if (canonical != null)
                        {
                            owned.Remove(spriteResult);
                            Object.DestroyImmediate(spriteResult);
                            result = canonical;
                        }
                    }
                    else result = NowUnityTextureAssets.LoadTexture(absolute, Own);
                }
                else if (lottie)
                    result = NowLottieAssets.Load(absolute, type, localId, Own);
                else if (extension is ".asset" or ".ttf" or ".otf")
                    result = NowUnitySerializedAssets.Load(absolute, type, localId,
                        (guid, id, requested) => string.IsNullOrEmpty(guid)
                            ? LoadResolved(absolute, requested, null, id) : ResolveGuid(guid, id, requested), Own);
                else throw new NotSupportedException("Direct asset loading does not support '" + absolute + "' (" + extension + ").");
                if (result != null) loaded[key] = result;
                return result != null && (type == null || type.IsInstanceOfType(result)) ? result : null;
            }
            catch (Exception exception)
            {
                if (outermost)
                {
                    for (int i = owned.Count - 1; i >= checkpoint; i--)
                    {
                        if (owned[i] != null) Object.DestroyImmediate(owned[i]);
                        owned.RemoveAt(i);
                    }
                    foreach (var added in loaded.Keys.Where(k => !previousKeys.Contains(k)).ToArray()) loaded.Remove(added);
                }
                throw new InvalidDataException("Could not load Unity asset '" + absolute + "': " + exception.Message, exception);
            }
            finally { loadDepth--; loading.Remove(key); }
        }

        private void EnsureIndex()
        {
            if (indexed) return;
            AddRoot(Path.Combine(projectRoot, "Assets"), "Assets");
            string packages = Path.Combine(projectRoot, "Packages");
            if (Directory.Exists(packages))
            {
                foreach (string embedded in Directory.EnumerateDirectories(packages).OrderBy(p => p, StringComparer.Ordinal))
                    AddRoot(embedded, "Packages/" + Path.GetFileName(embedded));
                AddResolvedPackages(packages);
            }
            indexed = true;
        }

        private void AddResolvedPackages(string packages)
        {
            string lockPath = Path.Combine(packages, "packages-lock.json");
            if (!File.Exists(lockPath)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
            if (!document.RootElement.TryGetProperty("dependencies", out var dependencies)) return;
            string cache = Path.Combine(projectRoot, "Library", "PackageCache");
            foreach (var dependency in dependencies.EnumerateObject())
            {
                string alias = "Packages/" + dependency.Name;
                if (Directory.Exists(Path.Combine(packages, dependency.Name))) continue;
                string version = dependency.Value.GetProperty("version").GetString();
                string resolved = null;
                if (version.StartsWith("file:", StringComparison.Ordinal))
                {
                    string relative = Uri.UnescapeDataString(version.Substring(5));
                    resolved = Path.GetFullPath(relative, packages);
                    if (!Directory.Exists(resolved)) resolved = Path.GetFullPath(relative, projectRoot);
                }
                else if (Directory.Exists(cache))
                {
                    var matches = Directory.EnumerateDirectories(cache, dependency.Name + "@*")
                        .Where(folder => PackageMatches(folder, dependency.Name, version)).ToArray();
                    if (matches.Length == 1) resolved = matches[0];
                }
                if (resolved != null && Directory.Exists(resolved)) AddRoot(resolved, alias);
            }
        }

        private static bool PackageMatches(string folder, string name, string version)
        {
            string manifest = Path.Combine(folder, "package.json");
            if (!File.Exists(manifest)) return false;
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            return json.RootElement.TryGetProperty("name", out var n) && n.GetString() == name &&
                json.RootElement.TryGetProperty("version", out var v) && v.GetString() == version;
        }

        private void AddRoot(string directory, string alias)
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            foreach (string file in Directory.EnumerateFiles(directory, "*", options).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                if (relative.Split('/').Any(part => part.EndsWith("~", StringComparison.Ordinal) || part.StartsWith(".", StringComparison.Ordinal))) continue;
                string projectPath = alias + "/" + relative;
                paths[projectPath] = file;
                int resourceIndex = projectPath.IndexOf("/Resources/", StringComparison.Ordinal);
                if (resourceIndex >= 0)
                {
                    string resourceName = projectPath.Substring(resourceIndex + "/Resources/".Length);
                    resourceName = resourceName.Substring(0, resourceName.Length - Path.GetExtension(resourceName).Length);
                    Add(resources, resourceName, file);
                }
                string meta = file + ".meta";
                if (!File.Exists(meta)) continue;
                foreach (string line in File.ReadLines(meta))
                    if (line.StartsWith("guid:", StringComparison.Ordinal))
                    {
                        Add(guids, line.Substring(5).Trim(), file);
                        break;
                    }
            }
        }

        private static void Add(Dictionary<string, List<string>> index, string key, string value)
        {
            if (!index.TryGetValue(key, out var values)) index[key] = values = new List<string>();
            if (!values.Contains(value)) values.Add(value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear(); loaded.Clear(); paths.Clear(); guids.Clear(); resources.Clear();
        }

        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(NowProjectAssets)); }
    }
}
