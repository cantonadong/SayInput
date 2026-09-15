using System.Runtime.InteropServices;
using System.Windows;
using VoiceTyper.Core.Input;

namespace VoiceTyper.Windows.Input;

public sealed class ClipboardTextInjectionService(IForegroundWindowService windows) : ITextInjectionService
{
    public Task<TextInjectionResult> InjectAsync(TargetWindow target, string text, CancellationToken cancellationToken) =>
        OnSta<TextInjectionResult>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            DataObject? saved = null;
            var existing = ClipboardRetry.Run(Clipboard.GetDataObject);
            if (existing is not null)
            {
                saved = new DataObject();
                // Materialize delayed formats before changing clipboard ownership.
                foreach (var format in existing.GetFormats(false))
                {
                    var data = existing.GetData(format, false);
                    if (data is not null) saved.SetData(format, data, false);
                }
            }
            ClipboardRetry.Run(() => Clipboard.SetDataObject(text, true));
            var version = GetClipboardSequenceNumber();
            if (!windows.EnsureForegroundAsync(target, cancellationToken).GetAwaiter().GetResult() || KeyboardInput.ModifiersDown())
                return new(false, true, "无法安全输入原窗口，文字已复制，可手动粘贴。");
            var batch = KeyboardInput.Paste();
            if (KeyboardInput.Send(batch) != batch.Length)
            {
                KeyboardInput.Send([new() { Type = 1, Key = 0x56, Flags = 2 }, new() { Type = 1, Key = 0x11, Flags = 2 }]);
                return new(false, true, "输入失败，文字已复制。");
            }
            // ponytail: clipboard paste has no consumption acknowledgement; allow 350 ms before best-effort restoration.
            // Slow applications may need manual paste mode instead of automatic restoration.
            Thread.Sleep(350);
            if (GetClipboardSequenceNumber() == version)
                try
                {
                    if (saved is null) ClipboardRetry.Run(Clipboard.Clear);
                    else ClipboardRetry.Run(() => Clipboard.SetDataObject(saved, true));
                }
                catch (ExternalException) { }
            return new(true, false);
        });

    public static Task<bool> CopyAsync(string text) => OnSta(() =>
    {
        ClipboardRetry.Run(() => Clipboard.SetDataObject(text, true));
        return true;
    });

    private static Task<T> OnSta<T>(Func<T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(operation()); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "VoiceTyper.Clipboard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}

internal static class ClipboardRetry
{
    private const int Attempts = 5;

    public static void Run(Action operation) => Run(() => { operation(); return true; });

    public static T Run<T>(Func<T> operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return operation(); }
            catch (ExternalException) when (attempt < Attempts) { Thread.Sleep(20); }
        }
    }
}
