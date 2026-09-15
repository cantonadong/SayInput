using System.Media;
using System.IO;

namespace VoiceTyper.App.Sound;

public sealed class WaveSoundCuePlayer : ISoundCuePlayer, IDisposable
{
    private readonly IReadOnlyDictionary<SoundCue, SoundPlayer> players;
    internal IReadOnlyCollection<SoundCue> AvailableCues => players.Keys.ToArray();

    public WaveSoundCuePlayer(string? baseDirectory = null)
    {
        var root = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "resources", "sounds");
        var loaded = new Dictionary<SoundCue, SoundPlayer>();
        foreach (var pair in new Dictionary<SoundCue, string>
        {
            [SoundCue.Start] = "start.wav", [SoundCue.Stop] = "stop.wav",
            [SoundCue.Success] = "success.wav", [SoundCue.Error] = "error.wav"
        })
        {
            var path = Path.Combine(root, pair.Value);
            if (!IsWave(path)) continue;
            try { var sound = new SoundPlayer(path); sound.Load(); loaded[pair.Key] = sound; }
            catch { }
        }
        players = loaded;
    }

    private static bool IsWave(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 44) return false;
            Span<byte> header = stackalloc byte[12];
            return stream.Read(header) == header.Length &&
                header[..4].SequenceEqual("RIFF"u8) && header[8..].SequenceEqual("WAVE"u8);
        }
        catch { return false; }
    }

    public void Play(SoundCue cue)
    {
        try
        {
            if (players.TryGetValue(cue, out var player)) player.Play();
        }
        catch { }
    }

    public Task PlayAsync(SoundCue cue) => Task.Run(() =>
    {
        try { if (players.TryGetValue(cue, out var player)) player.PlaySync(); } catch { }
    });

    public void Dispose()
    {
        foreach (var player in players.Values) player.Dispose();
    }
}
