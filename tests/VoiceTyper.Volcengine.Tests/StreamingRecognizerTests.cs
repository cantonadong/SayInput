using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using VoiceTyper.Core.Speech;
using Xunit;

namespace VoiceTyper.Volcengine.Tests;

public sealed class StreamingRecognizerTests
{
    [Theory]
    [InlineData(401, "Speech credentials were rejected.")]
    [InlineData(403, "Speech resource is not enabled for these credentials.")]
    [InlineData(429, "Speech service rate limit was reached.")]
    [InlineData(500, "Speech service is temporarily unavailable.")]
    public async Task Upgrade_failure_reports_safe_actionable_category(int status, string message)
    {
        await using var server = new SocketServer();
        await using var recognizer = new VolcengineStreamingRecognizer(Options(server, true));
        var serving = server.RejectAsync(status);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recognizer.StartAsync(new(Guid.NewGuid()), default));

        Assert.Equal(message, error.Message);
        Assert.DoesNotContain("test-key", error.ToString());
        await serving.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TerminalBeforeEndOfAudioNeverReturnsTruncatedSessionText()
    {
        await using var server = new SocketServer();
        await using var recognizer = new VolcengineStreamingRecognizer(Options(server));
        var earlyResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalUpdates = 0;
        recognizer.RecognitionUpdated += (_, update) =>
        {
            if (update.IsFinal) Interlocked.Increment(ref terminalUpdates);
            earlyResponse.TrySetResult();
        };
        var serving = Task.Run(async () =>
        {
            using var socket = await server.AcceptAsync();
            await SocketServer.ReceiveAsync(socket);
            // A terminal response to the configuration cannot include the microphone audio.
            await socket.SendAsync(SeedProtocolTests.Response("{\"result\":{\"text\":\"truncated\"}}", 2), WebSocketMessageType.Binary, true, default);
            try { await SocketServer.ReceiveAsync(socket); } catch (WebSocketException) { }
        });
        var error = await Record.ExceptionAsync(async () =>
        {
            await recognizer.StartAsync(new(Guid.NewGuid()), default);
            await earlyResponse.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await recognizer.SendAudioAsync(new byte[100], default);
            await recognizer.CompleteAsync(default);
        });
        Assert.IsType<InvalidDataException>(error);
        Assert.Equal(0, terminalUpdates);
        await serving.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamsBatchesAndTailAndReturnsOnlyTerminalCumulativeText(bool apiKey)
    {
        await using var server = new SocketServer();
        await using var recognizer = new VolcengineStreamingRecognizer(Options(server, apiKey));
        var updates = new List<SpeechRecognitionEvent>();
        recognizer.RecognitionUpdated += (_, update) => { lock (updates) updates.Add(update); };
        var session = Guid.NewGuid();
        var serving = Task.Run(async () =>
        {
            using var socket = await server.AcceptAsync();
            Assert.Equal("-1", server.Headers["X-Api-Sequence"]);
            Assert.Equal(apiKey ? "test-key" : "test-token", server.Headers[apiKey ? "X-Api-Key" : "X-Api-Access-Key"]);
            Assert.Equal(!apiKey, server.Headers.ContainsKey("X-Api-App-Key"));
            Assert.Equal(0x10, (await SocketServer.ReceiveAsync(socket))[1]);
            await socket.SendAsync(SeedProtocolTests.Response("{}"), WebSocketMessageType.Binary, true, default);
            var first = await SocketServer.ReceiveAsync(socket);
            Assert.Equal(0x20, first[1]);
            var pcm = SeedProtocolTests.Unzip(first[8..]);
            Assert.Equal(6400, pcm.Length);
            Assert.All(pcm[..100], value => Assert.Equal(1, value));
            Assert.All(pcm[100..], value => Assert.Equal(2, value));
            await socket.SendAsync(SeedProtocolTests.Response("{\"result\":{\"text\":\"partial\",\"utterances\":[{\"definite\":true}]}}"), WebSocketMessageType.Binary, true, default);
            var tail = await SocketServer.ReceiveAsync(socket);
            Assert.Equal(0x22, tail[1]);
            Assert.Equal(100, SeedProtocolTests.Unzip(tail[8..]).Length);
            var terminal = SeedProtocolTests.Response("{\"result\":{\"text\":\"最终文本\"}}", 3);
            // Real WebSocket fragmentation must be reassembled before protocol decoding.
            await socket.SendAsync(terminal.AsMemory(0, 5), WebSocketMessageType.Binary, false, default);
            await socket.SendAsync(terminal.AsMemory(5), WebSocketMessageType.Binary, true, default);
        });
        await recognizer.StartAsync(new(session), default);
        await recognizer.SendAudioAsync(Enumerable.Repeat((byte)1, 100).ToArray(), default);
        await recognizer.SendAudioAsync(Enumerable.Repeat((byte)2, 6400).ToArray(), default);
        var result = await recognizer.CompleteAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("最终文本", result.Text);
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(updates, update => update.Text == "partial" && !update.IsFinal);
        Assert.Single(updates, update => update.IsFinal);
        Assert.All(updates, update => Assert.Equal(session, update.SessionId));
    }

    [Fact]
    public async Task PeerCloseNeverPromotesPartialToFinal()
    {
        await using var server = new SocketServer();
        await using var recognizer = new VolcengineStreamingRecognizer(Options(server));
        var serving = Task.Run(async () =>
        {
            using var socket = await server.AcceptAsync();
            await SocketServer.ReceiveAsync(socket);
            await socket.SendAsync(SeedProtocolTests.Response("{}"), WebSocketMessageType.Binary, true, default);
            await SocketServer.ReceiveAsync(socket);
            await socket.SendAsync(SeedProtocolTests.Response("{\"result\":{\"text\":\"unfinished\"}}"), WebSocketMessageType.Binary, true, default);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", default);
        });
        await recognizer.StartAsync(new(Guid.NewGuid()), default);
        await Assert.ThrowsAnyAsync<Exception>(() => recognizer.CompleteAsync(default));
        await serving;
    }

    [Fact]
    public async Task AbortInterruptsHandshakeAndAllowsNewSession()
    {
        await using var server = new SocketServer();
        await using var recognizer = new VolcengineStreamingRecognizer(Options(server));
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = Task.Run(async () =>
        {
            using var socket = await server.AcceptAsync();
            await SocketServer.ReceiveAsync(socket);
            accepted.SetResult();
            try { await SocketServer.ReceiveAsync(socket); } catch (WebSocketException) { }
        });
        var start = recognizer.StartAsync(new(Guid.NewGuid()), default);
        await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await recognizer.AbortAsync(default).WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await serving.WaitAsync(TimeSpan.FromSeconds(2));
        var next = Task.Run(async () =>
        {
            using var socket = await server.AcceptAsync();
            await SocketServer.ReceiveAsync(socket);
            await socket.SendAsync(SeedProtocolTests.Response("{}"), WebSocketMessageType.Binary, true, default);
            Assert.Equal(0x22, (await SocketServer.ReceiveAsync(socket))[1]);
            await socket.SendAsync(SeedProtocolTests.Response("{\"result\":{\"text\":\"\"}}", 2), WebSocketMessageType.Binary, true, default);
        });
        await recognizer.StartAsync(new(Guid.NewGuid()), default);
        Assert.Equal("", (await recognizer.CompleteAsync(default)).Text);
        await next;
    }

    [Fact]
    public async Task ProviderErrorAndOptionsNeverExposeCredentials()
    {
        await using var server = new SocketServer();
        var options = Options(server);
        Assert.DoesNotContain("test-token", options.ToString());
        await using var recognizer = new VolcengineStreamingRecognizer(options);
        var serving = Task.Run(async () =>
        {
            using var socket = await server.AcceptAsync();
            await SocketServer.ReceiveAsync(socket);
            await socket.SendAsync(SeedProtocolTests.Response("test-token", error: 45000001), WebSocketMessageType.Binary, true, default);
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => recognizer.StartAsync(new(Guid.NewGuid()), default));
        Assert.Contains("45000001", error.Message);
        Assert.DoesNotContain("test-token", error.ToString());
        await serving;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnresponsiveServiceHasBoundedHandshakeAndFinalWaits(bool duringFinal)
    {
        await using var server = new SocketServer();
        await using var recognizer = new VolcengineStreamingRecognizer(Options(server));
        var serving = Task.Run(async () =>
        {
            using var socket = await server.AcceptAsync();
            await SocketServer.ReceiveAsync(socket);
            if (duringFinal)
            {
                await socket.SendAsync(SeedProtocolTests.Response("{}"), WebSocketMessageType.Binary, true, default);
                await SocketServer.ReceiveAsync(socket);
            }
            try { await SocketServer.ReceiveAsync(socket); } catch (WebSocketException) { }
        });
        if (duringFinal) await recognizer.StartAsync(new(Guid.NewGuid()), default);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => duringFinal
            ? recognizer.CompleteAsync(default)
            : recognizer.StartAsync(new(Guid.NewGuid()), default));
        Assert.InRange(elapsed.Elapsed.TotalSeconds, duringFinal ? 14 : 9, duringFinal ? 25 : 20);
        await serving.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DisposeInterruptsPendingFinalAndPreventsRestart()
    {
        await using var server = new SocketServer();
        var recognizer = new VolcengineStreamingRecognizer(Options(server));
        var finalSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = Task.Run(async () =>
        {
            using var socket = await server.AcceptAsync();
            await SocketServer.ReceiveAsync(socket);
            await socket.SendAsync(SeedProtocolTests.Response("{}"), WebSocketMessageType.Binary, true, default);
            await SocketServer.ReceiveAsync(socket);
            finalSent.SetResult();
            try { await SocketServer.ReceiveAsync(socket); } catch (WebSocketException) { }
        });
        await recognizer.StartAsync(new(Guid.NewGuid()), default);
        var completion = recognizer.CompleteAsync(default);
        await finalSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await recognizer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => recognizer.StartAsync(new(Guid.NewGuid()), default));
        await recognizer.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConcurrentAbortAndDisposeRemainIdempotent()
    {
        await using var server = new SocketServer();
        var recognizer = new VolcengineStreamingRecognizer(Options(server));
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = Task.Run(async () =>
        {
            using var socket = await server.AcceptAsync();
            await SocketServer.ReceiveAsync(socket);
            accepted.SetResult();
            try { await SocketServer.ReceiveAsync(socket); } catch (WebSocketException) { }
        });
        var starting = recognizer.StartAsync(new(Guid.NewGuid()), default);
        await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(async () =>
        {
            if (index % 2 == 0) await recognizer.AbortAsync(default);
            else await recognizer.DisposeAsync();
        }))).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        await serving.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("ws://example.com/asr")]
    [InlineData("wss://example.com/asr")]
    [InlineData("wss://openspeech.bytedance.com.evil.test/asr")]
    [InlineData("wss://user:password@openspeech.bytedance.com/api/v3/sauc/bigmodel")]
    public void RejectsUntrustedEndpoints(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => new VolcengineStreamingRecognizer(new() { ApiKey = "key", Endpoint = new(endpoint) }));
    }

    private static VolcengineOptions Options(SocketServer server, bool apiKey = false) => new()
    {
        Endpoint = server.Endpoint, AppId = apiKey ? "" : "123", AccessToken = apiKey ? "" : "test-token", ApiKey = apiKey ? "test-key" : ""
    };
}

// Actual TCP/HTTP upgrade avoids an HttpListener URL reservation or a new server dependency.
internal sealed class SocketServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    public Uri Endpoint { get; }
    public Dictionary<string, string> Headers { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public SocketServer()
    {
        listener.Start();
        Endpoint = new($"ws://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/asr");
    }

    public async Task<WebSocket> AcceptAsync()
    {
        var client = await listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var header = new List<byte>();
        var one = new byte[1];
        while (header.Count < 16384)
        {
            if (await stream.ReadAsync(one) == 0) throw new IOException("Client disconnected during upgrade.");
            header.Add(one[0]);
            if (header.Count >= 4 && Encoding.ASCII.GetString(header.TakeLast(4).ToArray()) == "\r\n\r\n") break;
        }
        Headers = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(line => line.Split(':', 2)).ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(Headers["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
        return WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);
    }

    public async Task RejectAsync(int status)
    {
        using var client = await listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var header = new List<byte>();
        var one = new byte[1];
        while (header.Count < 16384)
        {
            if (await stream.ReadAsync(one) == 0) throw new IOException("Client disconnected during upgrade.");
            header.Add(one[0]);
            if (header.Count >= 4 && Encoding.ASCII.GetString(header.TakeLast(4).ToArray()) == "\r\n\r\n") break;
        }
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Rejected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
    }

    public static async Task<byte[]> ReceiveAsync(WebSocket socket)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) return [];
            output.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return output.ToArray();
    }

    public ValueTask DisposeAsync() { listener.Stop(); return ValueTask.CompletedTask; }
}
