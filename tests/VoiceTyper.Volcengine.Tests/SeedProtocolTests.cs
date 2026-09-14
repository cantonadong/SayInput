using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VoiceTyper.Volcengine.Protocol;
using Xunit;

namespace VoiceTyper.Volcengine.Tests;

public sealed class SeedProtocolTests
{
    [Fact]
    public void ConfigurationDeclaresPcmAndCumulativeResults()
    {
        var packet = SeedProtocol.EncodeConfiguration();
        Assert.Equal(new byte[] { 0x11, 0x10, 0x11, 0 }, packet[..4]);
        using var json = JsonDocument.Parse(Unzip(packet[8..]));
        Assert.Equal("pcm", json.RootElement.GetProperty("audio").GetProperty("format").GetString());
        Assert.Equal(16000, json.RootElement.GetProperty("audio").GetProperty("rate").GetInt32());
        Assert.Equal("full", json.RootElement.GetProperty("request").GetProperty("result_type").GetString());
        Assert.False(json.RootElement.GetProperty("audio").TryGetProperty("language", out _));
    }

    [Fact]
    public void AudioIsRawCompressedWithSignedSequenceAndFinalFlag()
    {
        byte[] audio = [0, 1, 2, 3];
        var packet = SeedProtocol.EncodeAudio(audio, true, -7);
        Assert.Equal(new byte[] { 0x11, 0x23, 0x01, 0 }, packet[..4]);
        Assert.Equal(-7, BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(4)));
        Assert.Equal(packet.Length - 12, BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(8)));
        Assert.Equal(audio, Unzip(packet[12..]));
        Assert.Equal(0x22, SeedProtocol.EncodeAudio([], true)[1]);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(2, true, false)]
    [InlineData(3, true, true)]
    public void DecodesSequenceAndTerminalFlagsIndependently(int flags, bool final, bool hasSequence)
    {
        var packet = Response("{\"result\":{\"text\":\"你好\",\"utterances\":[{\"definite\":true}]}}", flags, true);
        var result = SeedProtocol.Decode(packet);
        Assert.Equal("你好", result.Text);
        Assert.Equal(final, result.IsFinal);
        Assert.Equal(hasSequence ? -7 : (int?)null, result.Sequence);
    }

    [Fact]
    public void ErrorCodeIsDecodedWithoutReflectingProviderMessage()
    {
        var result = SeedProtocol.Decode(Response("secret-reflected-by-server", 0, false, 45000001));
        Assert.Equal(45000001u, result.ErrorCode);
        Assert.Equal("", result.Text);
        Assert.DoesNotContain("secret", result.ToString());
    }

    [Fact]
    public void MalformedTruncatedAndOversizedFramesFailClosed()
    {
        var valid = Response("{\"result\":{\"text\":\"a\"}}", 0, false);
        for (var i = 0; i < valid.Length; i++)
            Assert.Throws<InvalidDataException>(() => SeedProtocol.Decode(valid.AsSpan(0, i)));
        var badVersion = valid.ToArray(); badVersion[0] = 0x21;
        Assert.Throws<InvalidDataException>(() => SeedProtocol.Decode(badVersion));
        var badJson = Response("not-json", 0, false);
        Assert.Throws<InvalidDataException>(() => SeedProtocol.Decode(badJson));
        var bomb = Response(new string('a', SeedProtocol.MaxPayloadBytes + 1), 0, true);
        Assert.Throws<InvalidDataException>(() => SeedProtocol.Decode(bomb));
    }

    internal static byte[] Response(string body, int flags = 0, bool gzip = true, uint? error = null)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        if (gzip)
        {
            using var compressed = new MemoryStream();
            using (var zip = new GZipStream(compressed, CompressionLevel.Fastest, true)) zip.Write(payload);
            payload = compressed.ToArray();
        }
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0x11, (byte)((error.HasValue ? 0xf0 : 0x90) | flags), (byte)(0x10 | (gzip ? 1 : 0)), 0 });
        Span<byte> number = stackalloc byte[4];
        if ((flags & 1) != 0) { BinaryPrimitives.WriteInt32BigEndian(number, -7); stream.Write(number); }
        if (error.HasValue) { BinaryPrimitives.WriteUInt32BigEndian(number, error.Value); stream.Write(number); }
        BinaryPrimitives.WriteInt32BigEndian(number, payload.Length); stream.Write(number);
        stream.Write(payload);
        return stream.ToArray();
    }

    internal static byte[] Unzip(byte[] payload)
    {
        using var zip = new GZipStream(new MemoryStream(payload), CompressionMode.Decompress);
        using var output = new MemoryStream(); zip.CopyTo(output); return output.ToArray();
    }
}
