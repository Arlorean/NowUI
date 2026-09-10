using System.Net;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using NowUI.Cli;
using NUnit.Framework;

namespace NowUI.Native.Tests;

[NonParallelizable]
public sealed class BrowserServerTests
{
    string root = null!;
    [SetUp] public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "nowui-browser-server-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "<h1>NowUI</h1>");
        File.WriteAllBytes(Path.Combine(root, "runtime.wasm"), [0, 97, 115, 109]);
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "assets", "hello world.json"), "{\"ok\":true}");
    }
    [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [TestCase("/../outside.txt")]
    [TestCase("/%2e%2e/outside.txt")]
    [TestCase("/assets/%2E%2E/../outside.txt")]
    [TestCase("/assets%5c..%5c..%5coutside.txt")]
    [TestCase("/C:/Windows/win.ini")]
    [TestCase("/%00index.html")]
    [TestCase("http://example.com/index.html")]
    public void RejectsTraversalAndNonLocalPathSyntax(string target)
        => Assert.That(BrowserServer.TryResolvePath(root, target, out _), Is.False);

    [Test]
    public void AllowsEscapedAssetNamesAndQueriesInsideTheSite()
    {
        Assert.That(BrowserServer.TryResolvePath(root, "/assets/hello%20world.json?v=2", out var path), Is.True);
        Assert.That(path, Is.EqualTo(Path.Combine(root, "assets", "hello world.json")));
        Assert.That(BrowserServer.TryResolvePath(root, "/", out path), Is.True);
        Assert.That(path, Is.EqualTo(Path.Combine(root, "index.html")));
    }

    [Test]
    public void ADirectoryNamedIndexHtmlDoesNotRecurse()
    {
        Directory.CreateDirectory(Path.Combine(root, "assets", "index.html"));
        Assert.That(BrowserServer.TryResolvePath(root, "/assets/", out _), Is.False);
    }

    [Test]
    public void RejectsSymbolicLinksEscapingTheSite()
    {
        string outside = Path.Combine(Path.GetTempPath(), "nowui-browser-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside site");
        try
        {
            try { Directory.CreateSymbolicLink(Path.Combine(root, "escape"), outside); }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            { Assert.Ignore("This host does not allow creating a symbolic link: " + error.Message); }
            Assert.That(BrowserServer.TryResolvePath(root, "/escape/secret.txt", out _), Is.False);
        }
        finally { Directory.Delete(outside, true); }
    }

    [Test]
    public async Task ServesHtmlWasmAndHeadRejectsWritesAndDoesNotListDirectories()
    {
        using var stop = new CancellationTokenSource();
        var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = BrowserServer.ServeAsync(root, 0, stop.Token, ready.SetResult);
        try
        {
            var uri = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(uri.Host, Is.EqualTo("127.0.0.1"));
            using var client = new HttpClient { BaseAddress = uri };
            Assert.That(await client.GetStringAsync("/"), Is.EqualTo("<h1>NowUI</h1>"));
            using var wasm = await client.GetAsync("runtime.wasm");
            Assert.That(wasm.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/wasm"));
            Assert.That(await wasm.Content.ReadAsByteArrayAsync(), Is.EqualTo(new byte[] { 0, 97, 115, 109 }));
            using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "runtime.wasm"));
            Assert.That(head.Content.Headers.ContentLength, Is.EqualTo(4));
            Assert.That(await head.Content.ReadAsByteArrayAsync(), Is.Empty);
            using var listing = await client.GetAsync("assets/");
            Assert.That(listing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            using var post = await client.PostAsync("index.html", new StringContent("overwrite"));
            Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            Assert.That(await client.GetStringAsync("/"), Is.EqualTo("<h1>NowUI</h1>"));
            Assert.That(await RawRequest(uri.Port, "GET /%2e%2e/outside.txt HTTP/1.1\r\nHost: localhost\r\n\r\n"), Does.StartWith("HTTP/1.1 403"));
        }
        finally
        {
            stop.Cancel();
            try { await server.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    [Test]
    public async Task CancellationClosesConnectionsWaitingForRequestHeaders()
    {
        using var stop = new CancellationTokenSource();
        var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = BrowserServer.ServeAsync(root, 0, stop.Token, ready.SetResult);
        using var client = new TcpClient();
        try
        {
            var uri = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(IPAddress.Loopback, uri.Port);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n"));
        }
        finally
        {
            stop.Cancel();
            try { await server.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    [TestCase("br, gzip", "br")]
    [TestCase("gzip;q=0.8, br;q=0.4", "gzip")]
    [TestCase("br;q=0, gzip", "gzip")]
    [TestCase("br;q=0, gzip;q=0", null)]
    [TestCase("identity;q=1, br;q=0.5", null)]
    [TestCase("*;q=0.8, br;q=0", "gzip")]
    [TestCase("BR;Q=1", "br")]
    [TestCase("gzip;q=invalid", null)]
    [TestCase("", null)]
    public async Task NegotiatesAdjacentCompressionWithoutChangingTheAssetMime(string acceptEncoding, string? expected)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(root, "runtime.wasm"));
        foreach (string suffix in new[] { ".br", ".gz" })
        {
            using var file = File.Create(Path.Combine(root, "runtime.wasm" + suffix));
            using Stream compressor = suffix == ".br" ? new BrotliStream(file, CompressionLevel.SmallestSize) : new GZipStream(file, CompressionLevel.SmallestSize);
            compressor.Write(original);
        }
        using var stop = new CancellationTokenSource();
        var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = BrowserServer.ServeAsync(root, 0, stop.Token, ready.SetResult);
        try
        {
            using var client = new HttpClient { BaseAddress = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
            using var response = await client.GetAsync("runtime.wasm?v=1");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.Vary, Does.Contain("Accept-Encoding"));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/wasm"));
            Assert.That(response.Content.Headers.ContentEncoding.SingleOrDefault(), Is.EqualTo(expected));
            byte[] body = await response.Content.ReadAsByteArrayAsync();
            if (expected != null)
            {
                using var bytes = new MemoryStream(body);
                using Stream decoder = expected == "br" ? new BrotliStream(bytes, CompressionMode.Decompress) : new GZipStream(bytes, CompressionMode.Decompress);
                using var decoded = new MemoryStream();
                await decoder.CopyToAsync(decoded);
                Assert.That(decoded.ToArray(), Is.EqualTo(original));
            }
            else Assert.That(body, Is.EqualTo(original));
            using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "runtime.wasm"));
            Assert.That(head.Content.Headers.ContentLength, Is.EqualTo(body.Length));
            Assert.That(head.Content.Headers.ContentEncoding.SingleOrDefault(), Is.EqualTo(expected));
            Assert.That(await head.Content.ReadAsByteArrayAsync(), Is.Empty);
            using var denied = new HttpRequestMessage(HttpMethod.Get, "runtime.wasm");
            denied.Headers.TryAddWithoutValidation("Accept-Encoding", "*;q=0");
            using var deniedResponse = await client.SendAsync(denied);
            Assert.That(deniedResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotAcceptable));
        }
        finally
        {
            stop.Cancel();
            try { await server.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    [TestCase("app.js", "text/javascript; charset=utf-8")]
    [TestCase("runtime.wasm", "application/wasm")]
    [TestCase("manifest.json", "application/json; charset=utf-8")]
    [TestCase("font.woff2", "font/woff2")]
    [TestCase("font.ttf", "font/ttf")]
    [TestCase("blob.data", "application/octet-stream")]
    [TestCase("assets.dat", "application/octet-stream")]
    public void RuntimeAndFontContentTypesAreExplicit(string path, string contentType)
        => Assert.That(BrowserServer.ContentType(path), Is.EqualTo(contentType));

    static async Task<string> RawRequest(int port, string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync();
    }
}
