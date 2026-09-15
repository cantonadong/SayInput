using VoiceTyper.Volcengine;
using Xunit;

namespace VoiceTyper.Volcengine.Tests;

public sealed class VolcengineOptionsTests
{
    [Fact]
    public void New_configuration_defaults_to_streaming_recognition_2_0_hourly_resource()
    {
        Assert.Equal("volc.seedasr.sauc.duration", new VolcengineOptions().ResourceId);
    }

    [Fact]
    public void New_configuration_uses_streaming_recognition_2_0_optimized_endpoint()
    {
        Assert.Equal("wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async", new VolcengineOptions().Endpoint.AbsoluteUri);
    }
}
