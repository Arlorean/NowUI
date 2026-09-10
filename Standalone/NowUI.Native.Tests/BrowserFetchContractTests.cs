using System.Net;
using System.Net.Http;
using NowUI.Engine;
using NowUI.Hosting;
using NUnit.Framework;

namespace NowUI.Native.Tests;

public sealed class BrowserFetchContractTests
{
    [Test]
    public async Task BrowserRequestsStreamWithoutCredentialsAndRejectOpaqueRedirects()
    {
        using var provider = new NowHttpFetchProvider(new HttpClient(new Handler(request =>
        {
            Assert.That(request.Options.TryGetValue(new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse"), out bool streaming) && streaming, Is.True);
            Assert.That(request.Options.TryGetValue(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), out var options), Is.True);
            Assert.That(options!["redirect"], Is.EqualTo("manual"));
            Assert.That(options["credentials"], Is.EqualTo("omit"));
            return new HttpResponseMessage((HttpStatusCode)0) { ReasonPhrase = "opaqueredirect" };
        })), browserTransport: true);
        var sink = new Sink();
        using var handle = provider.Start(new NowFetchRequest("https://example.test/redirect", "GET", null, 10, false), sink);
        await Wait(handle);
        Assert.That(sink.Outcome, Is.EqualTo(NowFetchOutcome.ProtocolError));
        Assert.That(sink.Error, Does.Contain("redirect Location is hidden"));
        Assert.That(sink.Bytes, Is.Zero);
    }

    [Test]
    public async Task BrowserNetworkErrorsExplainCorsAndRetainSinkLimits()
    {
        using var failed = new NowHttpFetchProvider(new HttpClient(new Handler(_ => throw new HttpRequestException("Failed to fetch"))), browserTransport: true);
        var error = new Sink();
        using var handle = failed.Start(new NowFetchRequest("https://example.test/image", "GET", null, 10, false), error);
        await Wait(handle);
        Assert.That(error.Outcome, Is.EqualTo(NowFetchOutcome.ConnectionError));
        Assert.That(error.Error, Does.Contain("CORS").And.Contain("DNS"));
        using var bounded = new NowHttpFetchProvider(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[100]) })), browserTransport: true);
        var limit = new Sink { Maximum = 10 };
        using var limited = bounded.Start(new NowFetchRequest("https://example.test/large", "GET", null, 10, false), limit);
        await Wait(limited);
        Assert.That(limit.Outcome, Is.EqualTo(NowFetchOutcome.LimitExceeded));
        Assert.That(limit.Bytes, Is.Zero, "Declared oversize responses must be rejected before body transfer.");
    }

    static async Task Wait(INowFetchHandle handle)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!handle.isDone) await Task.Delay(1, timeout.Token);
    }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    sealed class Sink : INowFetchSink
    {
        public int Maximum = int.MaxValue, Bytes;
        ulong declared;
        public NowFetchOutcome Outcome;
        public string Error = "";
        public void OnResponse(long statusCode, IReadOnlyDictionary<string, string> headers) { }
        public void OnContentLength(ulong length) { declared = length; }
        public bool OnData(byte[] bytes, int count) { if (declared > (ulong)Maximum || Bytes + count > Maximum) return false; Bytes += count; return true; }
        public void OnComplete(NowFetchOutcome outcome, string error) { Outcome = outcome; Error = error; }
    }
}
