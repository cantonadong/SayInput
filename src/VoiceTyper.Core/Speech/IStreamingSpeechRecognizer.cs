namespace VoiceTyper.Core.Speech;

public interface IStreamingSpeechRecognizer : IAsyncDisposable
{
    event EventHandler<SpeechRecognitionEvent>? RecognitionUpdated;
    Task StartAsync(SpeechRecognitionOptions options, CancellationToken cancellationToken);
    ValueTask SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken cancellationToken);
    Task<Dictation.DictationResult> CompleteAsync(CancellationToken cancellationToken);
    Task AbortAsync(CancellationToken cancellationToken);
}
