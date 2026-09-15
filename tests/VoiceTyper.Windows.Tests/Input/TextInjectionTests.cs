using System.Runtime.InteropServices;
using VoiceTyper.Core.Input;
using VoiceTyper.Windows.Input;
using Xunit;

namespace VoiceTyper.Windows.Tests.Input;

public sealed class TextInjectionTests
{
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    public async Task Partial_send_reports_failure_without_fallback_or_duplicate_text(uint sent)
    {
        var fallback = new Fallback();
        var service = new WindowsTextInjectionService(new Foreground(), fallback, _ => sent, () => false);
        var result = await service.InjectAsync(new(1, 2, "test", "test"), "AB", default);
        Assert.False(result.Succeeded);
        Assert.False(result.CopiedToClipboard);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(0, fallback.Calls);
    }

    private sealed class Foreground : IForegroundWindowService
    {
        public TargetWindow? Capture() => new(1, 2, "test", "test");
        public bool IsValid(TargetWindow target) => true;
        public Task<bool> EnsureForegroundAsync(TargetWindow target, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Fallback : ITextInjectionService
    {
        public int Calls;
        public Task<TextInjectionResult> InjectAsync(TargetWindow target, string text, CancellationToken ct)
        { Calls++; return Task.FromResult(new TextInjectionResult(true, true)); }
    }

    [Fact]
    public void Unicode_batch_preserves_chinese_and_surrogate_pairs_without_return_key()
    {
        var batch = KeyboardInput.Unicode("你😀");
        Assert.Equal(6, batch.Length);
        Assert.Equal(40, Marshal.SizeOf<KeyboardInput>());
        Assert.Equal(new ushort[] { 0x4F60, 0x4F60, 0xD83D, 0xD83D, 0xDE00, 0xDE00 }, batch.Select(x => x.Scan));
        Assert.All(batch, x => { Assert.Equal(1u, x.Type); Assert.Equal(0, x.Key); });
        Assert.Equal(new uint[] { 4, 6, 4, 6, 4, 6 }, batch.Select(x => x.Flags));
    }
}
