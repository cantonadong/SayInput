using System.Runtime.InteropServices;

namespace VoiceTyper.Windows.Input;

[StructLayout(LayoutKind.Explicit, Size = 40)]
internal struct KeyboardInput
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(8)] public ushort Key;
    [FieldOffset(10)] public ushort Scan;
    [FieldOffset(12)] public uint Flags;
    public static KeyboardInput[] Unicode(string text)
    {
        if (text.Length > 32768) throw new ArgumentException("识别文字过长。", nameof(text));
        var inputs = new KeyboardInput[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[2 * i] = new() { Type = 1, Scan = text[i], Flags = 4 };
            inputs[2 * i + 1] = new() { Type = 1, Scan = text[i], Flags = 6 };
        }
        return inputs;
    }
    public static KeyboardInput[] Paste() =>
    [new() { Type = 1, Key = 0x11 }, new() { Type = 1, Key = 0x56 },
     new() { Type = 1, Key = 0x56, Flags = 2 }, new() { Type = 1, Key = 0x11, Flags = 2 }];
    public static uint Send(KeyboardInput[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<KeyboardInput>());
    public static bool ModifiersDown() => (GetAsyncKeyState(0x10) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x12) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, KeyboardInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
