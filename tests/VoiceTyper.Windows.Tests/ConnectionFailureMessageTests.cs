using VoiceTyper.App;
using Xunit;

namespace VoiceTyper.Windows.Tests;

public sealed class ConnectionFailureMessageTests
{
    [Theory]
    [InlineData("Speech credentials were rejected.", "凭据")]
    [InlineData("Speech resource is not enabled for these credentials.", "Resource ID")]
    [InlineData("Speech service rate limit was reached.", "频率")]
    [InlineData("Speech service is temporarily unavailable.", "暂时")]
    [InlineData("Network unavailable or speech service connection failed.", "网络")]
    public void Known_provider_failures_are_translated_to_actionable_safe_messages(string error, string expected)
    {
        Assert.Contains(expected, MainWindow.ConnectionFailureMessage(new InvalidOperationException(error)));
    }

    [Fact]
    public void Unknown_failures_do_not_expose_exception_details()
    {
        var message = MainWindow.ConnectionFailureMessage(new InvalidOperationException("secret response body"));

        Assert.DoesNotContain("secret response body", message);
        Assert.Contains("麦克风", message);
    }
}
