namespace VoiceTyper.Core.Input;

public interface ITextInjectionService
{
    Task<TextInjectionResult> InjectAsync(TargetWindow target, string text, CancellationToken cancellationToken);
}
