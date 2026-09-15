namespace VoiceTyper.App.Bootstrap;

internal static class ShutdownSequence
{
    public static async Task DrainConfigurationAsync(Task activeToggle, Func<Task> waitForSave,
        Func<Task> stopAndDrain)
    {
        await activeToggle;
        await waitForSave();
        await stopAndDrain();
    }
}
