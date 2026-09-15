using System.Runtime.InteropServices;
using VoiceTyper.Windows.Input;
using Xunit;

namespace VoiceTyper.Windows.Tests.Input;

public sealed class ClipboardRetryTests
{
    [Fact]
    public void Transient_clipboard_contention_is_retried_until_the_operation_succeeds()
    {
        var attempts = 0;

        var result = ClipboardRetry.Run(() =>
        {
            attempts++;
            if (attempts < 3) throw new ExternalException("clipboard busy");
            return "copied";
        });

        Assert.Equal("copied", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void Clipboard_contention_stops_after_the_bounded_attempt_count()
    {
        var attempts = 0;

        Assert.Throws<ExternalException>(() => ClipboardRetry.Run<int>(() =>
        {
            attempts++;
            throw new ExternalException("clipboard busy");
        }));

        Assert.Equal(5, attempts);
    }
}
