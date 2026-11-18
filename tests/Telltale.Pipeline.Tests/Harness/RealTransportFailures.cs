using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// The exceptions .NET's own HTTP handler raises when a connection fails in a given way, caught from a real
/// socket on this machine. A test that fakes a failure throws one of these, so it fakes what the runtime
/// really gives and not what a test author expects it to give.
/// </summary>
public static class RealTransportFailures
{
    private static readonly Lazy<Task<HttpRequestException>> Reset = new(() => CaptureAsync(ResetAfterTheRequestAsync));
    private static readonly Lazy<Task<HttpRequestException>> ClosedMidAnswer = new(() => CaptureAsync(CloseMidAnswerAsync));

    /// <summary>The far end reset the connection (a TCP RST) after the request was sent and before any answer.</summary>
    public static Task<HttpRequestException> ConnectionResetAsync() => Reset.Value;

    /// <summary>The far end closed the connection cleanly halfway through the answer's body.</summary>
    public static Task<HttpRequestException> ClosedHalfwayThroughTheAnswerAsync() => ClosedMidAnswer.Value;

    private static async Task<HttpRequestException> CaptureAsync(Func<Socket, Task> misbehave)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync();
            await ReadTheRequestAsync(socket);
            await misbehave(socket);
        });

        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        try
        {
            using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/rest/api/3/status"));
        }
        catch (HttpRequestException exception)
        {
            await server;
            return exception;
        }

        throw new InvalidOperationException("The misbehaving server was answered without a failure.");
    }

    private static async Task ReadTheRequestAsync(Socket socket)
    {
        var buffer = new byte[4096];
        var read = new StringBuilder();
        while (!read.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var count = await socket.ReceiveAsync(buffer, SocketFlags.None);
            if (count == 0)
            {
                return;
            }

            read.Append(Encoding.ASCII.GetString(buffer, 0, count));
        }
    }

    private static Task ResetAfterTheRequestAsync(Socket socket)
    {
        // Lingering for no time makes Close send a reset in place of the usual orderly close.
        socket.LingerState = new LingerOption(enable: true, seconds: 0);
        socket.Close();
        return Task.CompletedTask;
    }

    private static async Task CloseMidAnswerAsync(Socket socket)
    {
        await socket.SendAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 100\r\n\r\n{\"synthetic\":"), SocketFlags.None);
        socket.Shutdown(SocketShutdown.Both);
        socket.Close();
    }
}
