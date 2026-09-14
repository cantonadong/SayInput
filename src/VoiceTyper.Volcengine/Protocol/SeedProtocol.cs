using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;

namespace VoiceTyper.Volcengine.Protocol;

public sealed record SeedResponse(string Text, bool IsFinal, int? Sequence, uint? ErrorCode);

/// <summary>Volcengine v3 binary framing; sizes and sequences are network byte order.</summary>
public static class SeedProtocol
{
    public const int MaxPayloadBytes = 1024 * 1024;
    public const int MaxFrameBytes = MaxPayloadBytes + 256;

    public static byte[] EncodeConfiguration() => Encode(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            user = new { uid = "sayinput" },
            audio = new { format = "pcm", codec = "raw", rate = 16000, bits = 16, channel = 1 },
            request = new { model_name = "bigmodel", enable_itn = true, enable_punc = true, show_utterances = true, result_type = "full" }
        }), 1, 1, false, null);

    public static byte[] EncodeAudio(ReadOnlySpan<byte> pcm16, bool isFinal = false, int? sequence = null)
    {
        if (pcm16.Length % 2 != 0) throw new ArgumentException("PCM must contain complete 16-bit samples.", nameof(pcm16));
        if (sequence.HasValue && (isFinal ? sequence >= 0 : sequence <= 0))
            throw new ArgumentException("Final sequence must be negative; other sequences must be positive.", nameof(sequence));
        return Encode(pcm16, 2, 0, isFinal, sequence);
    }

    private static byte[] Encode(ReadOnlySpan<byte> payload, int type, int serialization, bool final, int? sequence)
    {
        if (payload.Length > MaxPayloadBytes) throw new ArgumentOutOfRangeException(nameof(payload));
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x11, (byte)((type << 4) | (final ? 2 : 0) | (sequence.HasValue ? 1 : 0)), (byte)((serialization << 4) | 1), 0 });
        Span<byte> number = stackalloc byte[4];
        if (sequence.HasValue) { BinaryPrimitives.WriteInt32BigEndian(number, sequence.Value); output.Write(number); }
        var sizeOffset = (int)output.Position;
        output.Write(new byte[4]);
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(payload);
        var result = output.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(sizeOffset), result.Length - sizeOffset - 4);
        return result;
    }

    public static SeedResponse Decode(ReadOnlySpan<byte> frame)
    {
        try
        {
            if (frame.Length < 8 || frame.Length > MaxFrameBytes || frame[0] >> 4 != 1)
                throw new InvalidDataException();
            var offset = (frame[0] & 15) * 4;
            var type = frame[1] >> 4;
            var flags = frame[1] & 15;
            var serialization = frame[2] >> 4;
            var compression = frame[2] & 15;
            if (offset < 4 || offset > frame.Length - 4 || (type != 9 && type != 15) || flags > 3 || serialization > 1 || compression > 1)
                throw new InvalidDataException();
            int? sequence = null;
            if ((flags & 1) != 0) { sequence = ReadInt(frame, ref offset); }
            uint? error = null;
            if (type == 15) error = unchecked((uint)ReadInt(frame, ref offset));
            var size = ReadInt(frame, ref offset);
            if (size < 0 || size > MaxPayloadBytes || size != frame.Length - offset) throw new InvalidDataException();
            var payload = frame[offset..].ToArray();
            if (compression == 1)
            {
                using var gzip = new GZipStream(new MemoryStream(payload), CompressionMode.Decompress);
                using var output = new MemoryStream();
                Span<byte> buffer = stackalloc byte[8192];
                int count;
                while ((count = gzip.Read(buffer)) != 0)
                {
                    if (output.Length + count > MaxPayloadBytes) throw new InvalidDataException();
                    output.Write(buffer[..count]);
                }
                payload = output.ToArray();
            }
            // Provider errors can reflect request data. Expose only their numeric code.
            if (error.HasValue) return new("", false, sequence, error);
            if (serialization != 1) throw new InvalidDataException();
            using var json = JsonDocument.Parse(payload);
            string text = "";
            if (json.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("text", out var value))
                text = value.GetString() ?? "";
            return new(text, (flags & 2) != 0, sequence, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or JsonException or InvalidOperationException)
        {
            throw new InvalidDataException("Invalid speech service response.");
        }
    }

    private static int ReadInt(ReadOnlySpan<byte> frame, ref int offset)
    {
        if (offset > frame.Length - 4) throw new InvalidDataException();
        var value = BinaryPrimitives.ReadInt32BigEndian(frame[offset..]);
        offset += 4;
        return value;
    }
}
