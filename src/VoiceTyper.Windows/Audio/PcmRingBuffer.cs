namespace VoiceTyper.Windows.Audio;

/// <summary>PCM16 rolling history; callers serialize access. Pending network audio must never use overwrite semantics.</summary>
public sealed class PcmRingBuffer
{
    private readonly byte[] buffer;
    private int head, count;
    public PcmRingBuffer(int capacity = 24000)
    {
        if (capacity <= 0 || capacity % 2 != 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        buffer = new byte[capacity];
    }
    public void Write(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length % 2 != 0) throw new ArgumentException("PCM must contain complete samples.", nameof(pcm));
        if (pcm.Length >= buffer.Length) { pcm = pcm[^buffer.Length..]; Reset(); }
        var overwritten = Math.Max(0, count + pcm.Length - buffer.Length);
        head = (head + overwritten) % buffer.Length;
        count -= overwritten;
        var tail = (head + count) % buffer.Length;
        var first = Math.Min(pcm.Length, buffer.Length - tail);
        pcm[..first].CopyTo(buffer.AsSpan(tail));
        pcm[first..].CopyTo(buffer);
        count += pcm.Length;
    }
    public int Read(Span<byte> destination)
    {
        var length = Math.Min(count, destination.Length & ~1);
        var first = Math.Min(length, buffer.Length - head);
        buffer.AsSpan(head, first).CopyTo(destination);
        buffer.AsSpan(0, length - first).CopyTo(destination[first..]);
        head = (head + length) % buffer.Length;
        count -= length;
        return length;
    }
    public void Reset() { Array.Clear(buffer); head = count = 0; }
}
