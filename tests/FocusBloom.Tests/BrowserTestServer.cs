using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FocusBloom.Tests;

internal sealed class BrowserTestServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    public int Port { get; }
    public string AllowedUrl => $"http://127.0.0.1:{Port}/lesson";
    public string DeniedUrl => $"http://localhost:{Port}/outside";

    public BrowserTestServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = RespondAsync(client);
            }
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task RespondAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = await reader.ReadLineAsync();
                while (await reader.ReadLineAsync() is { Length: > 0 }) { }
                var path = request?.Split(' ')[1] ?? "/";
                if (path == "/redirect")
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: {DeniedUrl}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                    return;
                }
                var svg = path == "/pixel.svg";
                var body = svg ? "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10' fill='green'/></svg>"
                    : $"<html><head><title>Learning fixture</title></head><body><h1>Learning fixture</h1><img id='asset' src='http://localhost:{Port}/pixel.svg'><p>Local navigation verification</p></body></html>";
                var data = Encoding.UTF8.GetBytes(body);
                var headers = $"HTTP/1.1 200 OK\r\nContent-Type: {(svg ? "image/svg+xml" : "text/html; charset=utf-8")}\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers));
                await stream.WriteAsync(data);
            }
            catch (IOException) { }
        }
    }

    public void Dispose() => _listener.Stop();
}
