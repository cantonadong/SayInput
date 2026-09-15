namespace VoiceTyper.Core.Audio;

public interface ISystemOutputMuteService
{
    Task MuteAsync(CancellationToken cancellationToken);
    Task RestoreAsync();
}
