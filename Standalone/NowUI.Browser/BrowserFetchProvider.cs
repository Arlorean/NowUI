using System;
using System.Net.Http;
using NowUI.Engine;
using NowUI.Hosting;

namespace NowUI.Browser;

/// <summary>Streaming browser fetch using the same sink, timeout, byte-limit and cancellation contract as native.</summary>
/// <remarks>Browser CORS rules apply. Redirects stay manual; opaque redirect responses fail rather than bypass URL policy.</remarks>
public sealed class BrowserFetchProvider : INowFetchProvider, IDisposable
{
    readonly NowHttpFetchProvider transport = new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), browserTransport: true);
    public INowFetchHandle Start(in NowFetchRequest request, INowFetchSink sink) => transport.Start(request, sink);
    public void Dispose() => transport.Dispose();
}
