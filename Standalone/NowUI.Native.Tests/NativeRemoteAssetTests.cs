using System.Diagnostics;
using System.Text;
using NowUI.Cli;
using NowUI.Engine;
using NowUI.Hosting;
using NowUI.Markdown;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Native.Tests;

[NonParallelizable]
public sealed class NativeRemoteAssetTests
{
    CaptureHost host = null!;
    NowFileResources resources = null!;
    long byteLimit;
    int redirects;
    Func<Uri, bool>? policy;
    [SetUp]
    public void SetUp()
    {
        byteLimit = NowLottieAsset.maxDownloadBytes; redirects = NowLottieAsset.maxRedirects; policy = NowLottieAsset.remoteUrlPolicy;
        resources = new NowFileResources(); host = new CaptureHost(200, 150, resources, Path.GetTempPath());
        NowRuntime.RegisterAssembly(typeof(Now).Assembly);
        NowRuntime.RegisterAssembly(typeof(NowMarkdownImages).Assembly);
        NowRuntime.Initialize(host, new NullRenderBackend());
    }
    [TearDown]
    public void TearDown()
    {
        NowRuntime.Shutdown(); host.Dispose(); resources.Dispose();
        NowLottieAsset.maxDownloadBytes = byteLimit; NowLottieAsset.maxRedirects = redirects; NowLottieAsset.remoteUrlPolicy = policy;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExistingLottieCacheLoadsHttpJsonAndArchivesIncludingChunkedBodies(bool archive)
    {
        byte[] data = archive ? LottieProjectAssetTests.Archive(LottieProjectAssetTests.MinimalJson) : Encoding.UTF8.GetBytes(LottieProjectAssetTests.MinimalJson);
        using var server = new LocalAssetServer(_ => new(data, Chunked: true));
        var state = WaitLottie(server.Url + "/animation", out var asset, out string error);
        Assert.That(state, Is.EqualTo(NowLottieCacheState.Loaded), error);
        Assert.That(asset!.duration, Is.EqualTo(2));
        Assert.That(server.Requests.Count, Is.EqualTo(1));
    }

    [Test]
    public void RedirectTargetsUseExistingUrlPolicyBeforeAnotherRequestStarts()
    {
        using var server = new LocalAssetServer(_ => new([], 302, "/blocked"));
        int thread = Environment.CurrentManagedThreadId;
        var visited = new List<string>();
        NowLottieAsset.remoteUrlPolicy = uri => { Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(thread)); visited.Add(uri.AbsolutePath); return uri.AbsolutePath != "/blocked"; };
        Assert.That(WaitLottie(server.Url + "/start", out _, out var error), Is.EqualTo(NowLottieCacheState.Failed));
        Assert.That(error, Does.Contain("Refused").Or.Contain("policy"));
        Assert.That(visited, Does.Contain("/blocked"));
        Assert.That(server.Requests, Is.EqualTo(new[] { "/start" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExistingByteLimitStopsDeclaredAndChunkedResponses(bool chunked)
    {
        NowLottieAsset.maxDownloadBytes = 16;
        using var server = new LocalAssetServer(_ => new(Encoding.UTF8.GetBytes(LottieProjectAssetTests.MinimalJson), Chunked: chunked));
        Assert.That(WaitLottie(server.Url + "/large", out _, out var error), Is.EqualTo(NowLottieCacheState.Failed));
        Assert.That(error, Does.Contain("limit"));
    }

    [Test]
    public void RedirectCountAndAccumulatedBytesKeepCoreLimits()
    {
        NowLottieAsset.maxRedirects = 1;
        using var server = new LocalAssetServer(_ => new([], 302, "/loop"));
        Assert.That(WaitLottie(server.Url + "/loop", out _, out var error), Is.EqualTo(NowLottieCacheState.Failed));
        Assert.That(error, Does.Contain("redirect limit"));
        Assert.That(server.Requests.Count, Is.EqualTo(2));
        NowLottieAsset.maxDownloadBytes = 100;
        using var bytesServer = new LocalAssetServer(path => path == "/start" ? new(new byte[90], 302, "/final")
            : new(Encoding.UTF8.GetBytes(LottieProjectAssetTests.MinimalJson)));
        Assert.That(WaitLottie(bytesServer.Url + "/start", out _, out error), Is.EqualTo(NowLottieCacheState.Failed));
        Assert.That(error, Does.Contain("across redirects"));
    }

    [Test]
    public void HttpCompressionCannotBypassTheDecodedByteLimit()
    {
        string json = LottieProjectAssetTests.MinimalJson.Replace("\"v\":", "\"nm\":\"" + new string('a', 4096) + "\",\"v\":");
        using var compressed = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(json));
        byte[] body = compressed.ToArray();
        Assert.That(body.Length, Is.LessThan(512));
        NowLottieAsset.maxDownloadBytes = 512;
        using var server = new LocalAssetServer(_ => new(body, ContentEncoding: "gzip"));
        Assert.That(WaitLottie(server.Url + "/compressed", out _, out var error), Is.EqualTo(NowLottieCacheState.Failed));
        Assert.That(error, Does.Contain("limit"));
    }

    [Test]
    public void TransportReportsTimeoutAbortProtocolErrorAndDisposalWithoutLateCallbacks()
    {
        using var server = new LocalAssetServer(path => path == "/404" ? new([], 404) : new([], DelayMilliseconds: 3000));
        var timeout = new Sink();
        using (var request = host.Http.Start(new NowFetchRequest(server.Url + "/slow", "GET", null, 1, false), timeout))
            Wait(() => request.isDone);
        Assert.That(timeout.Outcome, Is.EqualTo(NowFetchOutcome.Timeout));
        var aborted = new Sink();
        using (var request = host.Http.Start(new NowFetchRequest(server.Url + "/abort", "GET", null, 10, false), aborted))
        { request.Abort(); Wait(() => request.isDone); }
        Assert.That(aborted.Outcome, Is.EqualTo(NowFetchOutcome.Aborted));
        var protocol = new Sink();
        using (var request = host.Http.Start(new NowFetchRequest(server.Url + "/404", "GET", null, 10, false), protocol))
            Wait(() => request.isDone);
        Assert.That(protocol.Outcome, Is.EqualTo(NowFetchOutcome.ProtocolError));
        var disposed = new Sink();
        var pending = host.Http.Start(new NowFetchRequest(server.Url + "/dispose", "GET", null, 10, false), disposed);
        pending.Dispose(); Wait(() => pending.isDone);
        Assert.That(disposed.Completions, Is.Zero);
        var owned = new Sink();
        using var provider = new NowHttpFetchProvider();
        using var ownedRequest = provider.Start(new NowFetchRequest(server.Url + "/owner-dispose", "GET", null, 10, false), owned);
        provider.Dispose(); Wait(() => ownedRequest.isDone);
        Assert.That(owned.Completions, Is.Zero);
    }

    [Test]
    public void MarkdownUsesSameTransportAndBroadImageDecoder()
    {
        byte[] bmp = CreateBmp();
        using var server = new LocalAssetServer(_ => new(bmp, ContentType: "image/bmp"));
        string url = server.Url + "/image.bmp";
        NowMarkdownImageState state = default; Texture2D? texture = null;
        Wait(() => { Tick(); state = NowMarkdownImages.GetState(url, out texture); return state is NowMarkdownImageState.Loaded or NowMarkdownImageState.Failed; });
        Assert.That(state, Is.EqualTo(NowMarkdownImageState.Loaded));
        Assert.That((texture!.width, texture.height, texture.isReadable), Is.EqualTo((1, 1, false)),
            "Markdown keeps its normal sealed texture ownership contract.");
        Assert.That(host.imageDecoder.TryDecode(bmp, out _, out _, out var rgba, out var error), Is.True, error);
        Assert.That(rgba, Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
    }

    internal static byte[] CreateBmp()
    {
        byte[] bytes = new byte[58];
        using var writer = new BinaryWriter(new MemoryStream(bytes));
        writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(58); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(1); writer.Write(1); writer.Write((short)1); writer.Write((short)24);
        writer.Write(0); writer.Write(4); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        writer.Write(new byte[] { 0, 0, 255, 0 }); return bytes;
    }

    NowLottieCacheState WaitLottie(string url, out NowLottieAsset? asset, out string error)
    {
        NowLottieAsset? result = null; string message = ""; NowLottieCacheState state = default;
        Wait(() => { Tick(); state = NowLottieCache.GetState(url, out result, out message); return state is NowLottieCacheState.Loaded or NowLottieCacheState.Failed; });
        asset = result; error = message; return state;
    }
    void Tick() { host.realtimeSeconds += .01; NowRuntime.BeginFrame(); NowRuntime.EndFrame(); }
    static void Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready()) { if (clock.Elapsed.TotalSeconds > 10) Assert.Fail("Remote request did not finish."); Thread.Sleep(5); }
    }
    sealed class Sink : INowFetchSink
    {
        internal NowFetchOutcome Outcome; internal int Completions;
        public void OnResponse(long statusCode, IReadOnlyDictionary<string, string> headers) { }
        public void OnContentLength(ulong length) { }
        public bool OnData(byte[] bytes, int count) => true;
        public void OnComplete(NowFetchOutcome outcome, string error) { Outcome = outcome; Interlocked.Increment(ref Completions); }
    }
}
