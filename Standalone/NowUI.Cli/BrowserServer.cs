using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NowUI.Cli;

/// <summary>A development server for an exported browser app. No URL reservations or admin privileges are required.</summary>
internal static class BrowserServer
{
    internal static void Run(string siteDirectory, int port, bool openBrowser)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            ServeAsync(siteDirectory, port, stop.Token, uri =>
            {
                Console.WriteLine(uri.AbsoluteUri);
                Console.WriteLine("Press Ctrl+C to stop the browser preview server.");
                if (openBrowser) Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { Console.CancelKeyPress -= cancel; }
    }

    internal static async Task ServeAsync(string siteDirectory, int port, CancellationToken stop, Action<Uri>? ready = null)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(siteDirectory));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Browser output directory was not found: " + root);
        if ((uint)port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var listener = new TcpListener(IPAddress.Loopback, port);
        using var slots = new SemaphoreSlim(16);
        var active = new ConcurrentDictionary<TcpClient, Task>();
        listener.Start();
        try
        {
            int actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            ready?.Invoke(new Uri($"http://127.0.0.1:{actualPort}/"));
            while (!stop.IsCancellationRequested)
            {
                await slots.WaitAsync(stop);
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(stop); }
                catch { slots.Release(); throw; }
                // Register before starting so shutdown also awaits connection cleanup.
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var task = HandleRegisteredAsync(client, registered.Task);
                active[client] = task;
                registered.SetResult();
            }
        }
        finally
        {
            listener.Stop();
            foreach (var client in active.Keys) client.Dispose();
            await Task.WhenAll(active.Values);
        }

        async Task HandleRegisteredAsync(TcpClient client, Task registered)
        {
            await registered;
            try { await HandleAsync(client, root, stop); }
            finally
            {
                client.Dispose();
                slots.Release();
                active.TryRemove(client, out var ignored);
            }
        }
    }

    static async Task HandleAsync(TcpClient client, string root, CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        try
        {
            using var stream = client.GetStream();
            byte[] header = new byte[16 * 1024];
            int length = 0, end = -1;
            while (length < header.Length && end < 0)
            {
                int read = await stream.ReadAsync(header.AsMemory(length), token);
                if (read == 0) return;
                int start = Math.Max(0, length - 3);
                length += read;
                for (int i = start; i + 3 < length; i++)
                    if (header[i] == 13 && header[i + 1] == 10 && header[i + 2] == 13 && header[i + 3] == 10) { end = i; break; }
            }
            if (end < 0) { await Error(stream, 431, "Request Header Fields Too Large", false, token); return; }
            int firstLineEnd = Array.IndexOf(header, (byte)13, 0, end + 1);
            if (firstLineEnd < 0) { await Error(stream, 400, "Bad Request", false, token); return; }
            string[] request = Encoding.ASCII.GetString(header, 0, firstLineEnd).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (request.Length != 3 || request[2] is not ("HTTP/1.0" or "HTTP/1.1"))
            { await Error(stream, 400, "Bad Request", false, token); return; }
            bool head = request[0] == "HEAD";
            if (request[0] is not ("GET" or "HEAD"))
            { await Error(stream, 405, "Method Not Allowed", head, token, "Allow: GET, HEAD\r\n"); return; }
            if (!TryResolvePath(root, request[1], out string path))
            { await Error(stream, 403, "Forbidden", head, token); return; }
            if (!File.Exists(path)) { await Error(stream, 404, "Not Found", head, token); return; }
            string acceptEncoding = string.Join(",", Encoding.ASCII.GetString(header, firstLineEnd + 2, end - firstLineEnd)
                .Split("\r\n").Where(line => line.StartsWith("Accept-Encoding:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line[(line.IndexOf(':') + 1)..].Trim()));
            if (!SelectEncoding(root, path, acceptEncoding, out string servedPath, out string? encoding))
            { await Error(stream, 406, "Not Acceptable", head, token, "Vary: Accept-Encoding\r\n"); return; }
            await using var file = new FileStream(servedPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
            string responseHeaders = "Vary: Accept-Encoding\r\n" + (encoding == null ? "" : "Content-Encoding: " + encoding + "\r\n");
            await Header(stream, 200, "OK", ContentType(path), file.Length, token, responseHeaders);
            if (!head) await file.CopyToAsync(stream, token);
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException or UnauthorizedAccessException)
        {
            // A dropped client or an output replaced during a rebuild does not terminate the preview server.
        }
    }

    static bool SelectEncoding(string root, string path, string header, out string servedPath, out string? encoding)
    {
        servedPath = path;
        encoding = null;
        var quality = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string item in header.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = item.Split(';');
            double weight = 1;
            foreach (string parameter in parts.Skip(1))
            {
                string[] pair = parameter.Split('=', 2, StringSplitOptions.TrimEntries);
                if (pair[0].Equals("q", StringComparison.OrdinalIgnoreCase) &&
                    (pair.Length != 2 || !double.TryParse(pair[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out weight)
                     || weight < 0 || weight > 1)) weight = 0;
            }
            quality[parts[0].Trim()] = weight;
        }
        // Identity is an allowed fallback unless explicitly excluded; an explicit
        // identity preference also competes against the available compressed forms.
        double identity = quality.GetValueOrDefault("identity", quality.GetValueOrDefault("*", 1) == 0 ? 0 : 1);
        double selectedQuality = quality.ContainsKey("identity") ? identity : 0;
        foreach (string candidate in new[] { "br", "gzip" })
        {
            double weight = quality.GetValueOrDefault(candidate, quality.GetValueOrDefault("*", 0));
            if (weight <= 0 || weight < selectedQuality || weight == selectedQuality && encoding != null) continue;
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/') + (candidate == "br" ? ".br" : ".gz");
            // Resolve the adjacent file separately: a sidecar symlink may not escape the site.
            if (!TryResolvePath(root, "/" + Uri.EscapeDataString(relative), out string compressed) || !File.Exists(compressed)) continue;
            servedPath = compressed;
            encoding = candidate;
            selectedQuality = weight;
        }
        return encoding != null || identity > 0;
    }

    internal static bool TryResolvePath(string siteDirectory, string requestTarget, out string path)
        => TryResolvePath(siteDirectory, requestTarget, allowIndex: true, out path);

    static bool TryResolvePath(string siteDirectory, string requestTarget, bool allowIndex, out string path)
    {
        path = "";
        if (!requestTarget.StartsWith('/')) return false;
        int query = requestTarget.IndexOfAny(['?', '#']);
        string raw = query >= 0 ? requestTarget[..query] : requestTarget;
        try
        {
            string decoded = Uri.UnescapeDataString(raw);
            if (decoded.IndexOfAny(['\\', '\0', ':']) >= 0) return false;
            string[] segments = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(x => x is "." or "..")) return false;
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(siteDirectory));
            string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string candidate = root;
            foreach (string segment in segments)
            {
                candidate = Path.GetFullPath(Path.Combine(candidate, segment));
                if (!candidate.StartsWith(prefix, comparison)) return false;
                FileSystemInfo item = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
                if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    var target = item.ResolveLinkTarget(returnFinalTarget: true);
                    if (target == null || !(target.FullName.Equals(root, comparison) || target.FullName.StartsWith(prefix, comparison))) return false;
                }
            }
            if (Directory.Exists(candidate))
            {
                if (!allowIndex) return false;
                // Check index.html through the same containment logic, including file symlinks.
                return TryResolvePath(root, raw.TrimEnd('/') + "/index.html", allowIndex: false, out path);
            }
            path = candidate;
            return candidate.StartsWith(prefix, comparison);
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        { return false; }
    }

    internal static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" or ".map" or ".webmanifest" => "application/json; charset=utf-8",
        ".wasm" => "application/wasm",
        ".svg" => "image/svg+xml",
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp",
        ".ico" => "image/x-icon", ".woff" => "font/woff", ".woff2" => "font/woff2", ".ttf" => "font/ttf", ".otf" => "font/otf",
        ".txt" => "text/plain; charset=utf-8", ".xml" => "application/xml",
        ".gz" => "application/gzip", ".br" or ".bin" or ".data" or ".dat" or ".dll" or ".pdb" or ".nowfont" => "application/octet-stream",
        _ => "application/octet-stream"
    };

    static Task Header(NetworkStream stream, int status, string reason, string contentType, long length, CancellationToken token, string extra = "")
    {
        string header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n{extra}\r\n";
        return stream.WriteAsync(Encoding.ASCII.GetBytes(header), token).AsTask();
    }

    static async Task Error(NetworkStream stream, int status, string reason, bool head, CancellationToken token, string extra = "")
    {
        byte[] body = Encoding.UTF8.GetBytes(reason + "\n");
        await Header(stream, status, reason, "text/plain; charset=utf-8", body.Length, token, extra);
        if (!head) await stream.WriteAsync(body, token);
    }
}
