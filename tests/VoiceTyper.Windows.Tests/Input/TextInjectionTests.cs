using System.Runtime.InteropServices;
using VoiceTyper.Windows.Input;
using Xunit;

namespace VoiceTyper.Windows.Tests.Input;

public sealed class TextInjectionTests
{
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
