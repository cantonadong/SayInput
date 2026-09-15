using VoiceTyper.Core.Input;

namespace VoiceTyper.Windows.Input;

public sealed class WindowsTextInjectionService : ITextInjectionService
{
    private readonly IForegroundWindowService windows;
    private readonly ITextInjectionService fallback;
    private readonly Func<KeyboardInput[], uint> send;
    private readonly Func<bool> modifiersDown;
    public WindowsTextInjectionService(IForegroundWindowService windows) : this(windows, new ClipboardTextInjectionService(windows), KeyboardInput.Send) { }
    internal WindowsTextInjectionService(IForegroundWindowService windows, ITextInjectionService fallback, Func<KeyboardInput[], uint> send,
        Func<bool>? modifiersDown = null)
    { this.windows = windows; this.fallback = fallback; this.send = send; this.modifiersDown = modifiersDown ?? KeyboardInput.ModifiersDown; }

    public async Task<TextInjectionResult> InjectAsync(TargetWindow target, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(text)) return new(true, false);
        var inputs = KeyboardInput.Unicode(text);
        if (!await windows.EnsureForegroundAsync(target, cancellationToken).ConfigureAwait(false) || modifiersDown())
            return await fallback.InjectAsync(target, text, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var sent = send(inputs);
        if (sent == inputs.Length) return new(true, false);
        if (sent != 0) return new(false, false, "只输入了部分文字，请核对原窗口；为避免重复，没有重试。");
        return await fallback.InjectAsync(target, text, cancellationToken).ConfigureAwait(false);
    }
}
