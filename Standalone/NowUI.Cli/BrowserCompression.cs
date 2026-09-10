using System.IO.Compression;

namespace NowUI.Cli;

internal sealed record BrowserCompressionReport(int Files, long SourceBytes, long BrotliBytes, long GzipBytes);

/// <summary>Precompresses static output; originals remain the canonical unencoded representation.</summary>
internal static class BrowserCompression
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wasm", ".js", ".mjs", ".json", ".html", ".css", ".ttf", ".otf", ".lottie", ".asset", ".meta",
        ".bin", ".data", ".dat", ".svg", ".txt", ".md", ".vert", ".frag", ".glsl"
    };

    internal static BrowserCompressionReport Compress(string siteDirectory, long maxFileBytes = 64L * 1024 * 1024,
        long maxTotalBytes = 512L * 1024 * 1024)
    {
        string root = Path.GetFullPath(siteDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Browser site directory is missing: " + root);
        if (maxFileBytes < 1 || maxTotalBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        var files = Directory.EnumerateFiles(root, "*", options)
            .Where(path => Extensions.Contains(Path.GetExtension(path))).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new FileInfo(path)).Where(file => file.Length >= 256).ToArray();
        if (files.Length > 16384 || files.Any(file => file.Length > maxFileBytes) || files.Sum(file => file.Length) > maxTotalBytes)
            throw new InvalidDataException($"Browser compression input exceeds its bounds (16,384 files, {maxFileBytes} bytes per file, {maxTotalBytes} bytes total).");
        long original = 0, brotli = 0, gzip = 0;
        foreach (var file in files)
        {
            original += file.Length;
            brotli += Encode(file, ".br");
            gzip += Encode(file, ".gz");
        }
        return new BrowserCompressionReport(files.Length, original, brotli, gzip);
    }

    static long Encode(FileInfo source, string extension)
    {
        string output = source.FullName + extension;
        string temporary = output + ".nowui-compress-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var input = source.OpenRead())
            using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (Stream encoder = extension == ".br"
                ? new BrotliStream(destination, CompressionLevel.Optimal)
                : new GZipStream(destination, CompressionLevel.Optimal))
                input.CopyTo(encoder);
            long length = new FileInfo(temporary).Length;
            if (length < source.Length)
            {
                File.Move(temporary, output, overwrite: true);
                return length;
            }
            // A larger encoding offers no benefit; do not leave a stale variant from a previous compression pass.
            if (File.Exists(output)) File.Delete(output);
            return source.Length;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
