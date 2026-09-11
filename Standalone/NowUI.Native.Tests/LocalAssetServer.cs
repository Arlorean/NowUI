using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NowUI.Native.Tests;

internal sealed class LocalAssetServer : IDisposable
{
    internal sealed record Response(byte[] Body, int Status = 200, string? Location = null, bool Chunked = false,
        int DelayMilliseconds = 0, long? DeclaredLength = null, string ContentType = "application/json", string? ContentEncoding = null);
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stopping = new();
    readonly Func<string, Response> handler;
    readonly Task accepting;
    readonly ConcurrentBag<Task> clients = new();
    internal ConcurrentQueue<string> Requests { get; } = new();
    internal string Url { get; }

    internal LocalAssetServer(Func<string, Response> handler)
    {
        this.handler = handler;
        listener.Start();
        Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = Accept();
    }

    async Task Accept()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stopping.Token);
                clients.Add(Serve(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (stopping.IsCancellationRequested) { }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            string? first = await reader.ReadLineAsync(stopping.Token);
            if (first == null) return;
            string path = first.Split(' ')[1];
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stopping.Token))) { }
            Requests.Enqueue(path);
            var response = handler(path);
            if (response.DelayMilliseconds > 0) await Task.Delay(response.DelayMilliseconds, stopping.Token);
            string headers = $"HTTP/1.1 {response.Status} Test\r\nConnection: close\r\nContent-Type: {response.ContentType}\r\n";
            if (response.Location != null) headers += "Location: " + response.Location + "\r\n";
            if (response.ContentEncoding != null) headers += "Content-Encoding: " + response.ContentEncoding + "\r\n";
            headers += response.Chunked ? "Transfer-Encoding: chunked\r\n" : "Content-Length: " + (response.DeclaredLength ?? response.Body.Length) + "\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"), stopping.Token);
            if (response.Chunked)
            {
                for (int offset = 0; offset < response.Body.Length; offset += 11)
                {
                    int count = Math.Min(11, response.Body.Length - offset);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(count.ToString("x") + "\r\n"), stopping.Token);
                    await stream.WriteAsync(response.Body.AsMemory(offset, count), stopping.Token);
                    await stream.WriteAsync("\r\n"u8.ToArray(), stopping.Token);
                }
                await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), stopping.Token);
            }
            else await stream.WriteAsync(response.Body, stopping.Token);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        stopping.Cancel(); listener.Stop();
        accepting.GetAwaiter().GetResult();
        Task.WhenAll(clients.ToArray()).GetAwaiter().GetResult();
        stopping.Dispose();
    }
}
