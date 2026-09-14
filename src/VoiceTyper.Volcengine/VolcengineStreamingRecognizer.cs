using System.Net.WebSockets;
using VoiceTyper.Core.Dictation;
using VoiceTyper.Core.Speech;
using VoiceTyper.Volcengine.Protocol;

namespace VoiceTyper.Volcengine;

public sealed class VolcengineStreamingRecognizer : IStreamingSpeechRecognizer
{
    private const int PacketBytes = 6400; // 200 ms of 16 kHz, signed PCM16 mono.
    private readonly VolcengineOptions options;
    private readonly object sync = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private Session? current;
    private bool disposed;

    public VolcengineStreamingRecognizer(VolcengineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
    }

    public event EventHandler<SpeechRecognitionEvent>? RecognitionUpdated;

    public async Task StartAsync(SpeechRecognitionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        Session session;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (current is not null) throw new InvalidOperationException("A speech session is already active.");
            current = session = new Session(options.SessionId);
        }
        await sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            SetHeaders(session.Socket, options.SessionId);
            await session.Socket.ConnectAsync(this.options.Endpoint, timeout.Token).ConfigureAwait(false);
            await SendPacketAsync(session, SeedProtocol.EncodeConfiguration(), timeout.Token).ConfigureAwait(false);
            var acknowledgement = await ReadResponseAsync(session, timeout.Token).ConfigureAwait(false);
            Publish(session, acknowledgement);
            session.Ready = true;
            session.Receiver = ReceiveLoopAsync(session);
        }
        catch (Exception ex)
        {
            var failure = SafeFailure(ex, cancellationToken, session.Token, "Speech connection timed out.");
            Close(session);
            session.Dispose();
            throw failure;
        }
        finally { sendGate.Release(); }
    }

    public async ValueTask SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken cancellationToken)
    {
        if (pcm16.Length % 2 != 0) throw new ArgumentException("PCM must contain complete 16-bit samples.", nameof(pcm16));
        var session = GetSession();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token);
        await sendGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!session.Ready || session.Completing) throw new InvalidOperationException("Speech session is not accepting audio.");
            while (!pcm16.IsEmpty)
            {
                var count = Math.Min(pcm16.Length, PacketBytes - session.Count);
                pcm16[..count].CopyTo(session.Audio.AsMemory(session.Count));
                session.Count += count;
                pcm16 = pcm16[count..];
                if (session.Count == PacketBytes)
                {
                    await SendPacketAsync(session, SeedProtocol.EncodeAudio(session.Audio), linked.Token).ConfigureAwait(false);
                    session.Count = 0;
                }
            }
        }
        catch (Exception ex)
        {
            var failure = SafeFailure(ex, cancellationToken, session.Token, "Speech audio send timed out.");
            session.Stop();
            throw failure;
        }
        finally { sendGate.Release(); }
    }

    public async Task<DictationResult> CompleteAsync(CancellationToken cancellationToken)
    {
        var session = GetSession();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await sendGate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                if (!session.Ready) throw new InvalidOperationException("Speech session is not ready.");
                if (!session.Completing)
                {
                    session.Completing = true;
                    await SendPacketAsync(session, SeedProtocol.EncodeAudio(session.Audio.AsSpan(0, session.Count), true), timeout.Token).ConfigureAwait(false);
                    session.Count = 0;
                }
            }
            finally { sendGate.Release(); }
            return await session.Final.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw SafeFailure(ex, cancellationToken, session.Token, "Speech recognition timed out.");
        }
        finally { await EndSessionAsync(session).ConfigureAwait(false); }
    }

    public async Task AbortAsync(CancellationToken cancellationToken)
    {
        Session? session;
        lock (sync) session = current;
        if (session is not null) await EndSessionAsync(session).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        lock (sync) disposed = true;
        await AbortAsync(default).ConfigureAwait(false);
    }

    private Session GetSession()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return current ?? throw new InvalidOperationException("No speech session is active.");
        }
    }

    private void SetHeaders(ClientWebSocket socket, Guid id)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) socket.Options.SetRequestHeader("X-Api-Key", options.ApiKey);
        else
        {
            socket.Options.SetRequestHeader("X-Api-App-Key", options.AppId);
            socket.Options.SetRequestHeader("X-Api-Access-Key", options.AccessToken);
        }
        socket.Options.SetRequestHeader("X-Api-Resource-Id", options.ResourceId);
        socket.Options.SetRequestHeader("X-Api-Request-Id", id.ToString());
        socket.Options.SetRequestHeader("X-Api-Connect-Id", id.ToString());
        socket.Options.SetRequestHeader("X-Api-Sequence", "-1");
        socket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
    }

    private static async Task SendPacketAsync(Session session, byte[] packet, CancellationToken cancellationToken) =>
        await session.Socket.SendAsync(packet.AsMemory(), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);

    private async Task ReceiveLoopAsync(Session session)
    {
        try
        {
            while (!session.Final.Task.IsCompleted)
                Publish(session, await ReadResponseAsync(session, session.Token).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            session.Final.TrySetException(SafeFailure(ex, default, session.Token, "Speech recognition timed out."));
            session.Socket.Abort();
        }
    }

    private static async Task<SeedResponse> ReadResponseAsync(Session session, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        ValueWebSocketReceiveResult received;
        do
        {
            received = await session.Socket.ReceiveAsync(session.ReceiveBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (received.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException("Speech connection closed before the final result.");
            if (message.Length + received.Count > SeedProtocol.MaxFrameBytes) throw new InvalidDataException("Speech response exceeds the size limit.");
            message.Write(session.ReceiveBuffer, 0, received.Count);
        } while (!received.EndOfMessage);
        return SeedProtocol.Decode(message.GetBuffer().AsSpan(0, (int)message.Length));
    }

    private void Publish(Session session, SeedResponse response)
    {
        if (response.ErrorCode.HasValue) throw new InvalidOperationException($"Speech service error ({response.ErrorCode.Value}).");
        if (session.Token.IsCancellationRequested) return;
        if (response.IsFinal)
        {
            // Utterance `definite` is not a session final; only the terminal frame settles completion.
            if (session.Final.TrySetResult(new(response.Text)))
                RecognitionUpdated?.Invoke(this, new(session.Id, response.Text, true));
        }
        else if (response.Text.Length != 0) RecognitionUpdated?.Invoke(this, new(session.Id, response.Text, false));
    }

    private async Task EndSessionAsync(Session session)
    {
        session.Stop();
        await sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await session.Receiver.ConfigureAwait(false);
            Close(session);
            session.Dispose();
        }
        finally { sendGate.Release(); }
    }

    private void Close(Session session)
    {
        session.Stop();
        lock (sync) { if (ReferenceEquals(current, session)) current = null; }
    }

    private static Exception SafeFailure(Exception error, CancellationToken caller, CancellationToken lifetime, string timeoutMessage)
    {
        if (caller.IsCancellationRequested || lifetime.IsCancellationRequested) return new OperationCanceledException("Speech session was cancelled.");
        if (error is OperationCanceledException) return new TimeoutException(timeoutMessage);
        if (error is InvalidDataException) return new InvalidDataException("Invalid or incomplete speech service response.");
        if (error is InvalidOperationException && error.Message.StartsWith("Speech service error (", StringComparison.Ordinal))
            return new InvalidOperationException(error.Message);
        return new InvalidOperationException("Network unavailable or speech service connection failed.");
    }

    private sealed class Session : IDisposable
    {
        private readonly CancellationTokenSource lifetime = new();
        private readonly object cleanup = new();
        private bool stopped;
        private bool disposed;
        public Guid Id { get; }
        public CancellationToken Token { get; }
        public ClientWebSocket Socket { get; } = new();
        public byte[] Audio { get; } = new byte[PacketBytes];
        public byte[] ReceiveBuffer { get; } = new byte[8192];
        public int Count { get; set; }
        public bool Ready { get; set; }
        public bool Completing { get; set; }
        public Task Receiver { get; set; } = Task.CompletedTask;
        public TaskCompletionSource<DictationResult> Final { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Session(Guid id) { Id = id; Token = lifetime.Token; }

        public void Stop()
        {
            lock (cleanup)
            {
                if (stopped) return;
                stopped = true;
                lifetime.Cancel();
                Socket.Abort();
                Final.TrySetCanceled();
            }
        }

        public void Dispose()
        {
            lock (cleanup)
            {
                if (disposed) return;
                disposed = true;
                Socket.Dispose();
                lifetime.Dispose();
                Array.Clear(Audio);
                Array.Clear(ReceiveBuffer);
            }
        }
    }
}
