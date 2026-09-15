using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VoiceTyper.Core.Hotkeys;

namespace VoiceTyper.Windows.Keyboard;

/// <summary>
/// Events run on the hook thread. Handlers must only perform short, nonblocking work.
/// Marshal UI updates asynchronously; call Start/Stop/Dispose outside event handlers.
/// </summary>
public sealed class RightAltHotkeyService : IHotkeyService
{
    private readonly object lifecycle = new();
    private readonly RightAltKeyState keys = new();
    private readonly KeyboardNative.HookProc callback;
    private Thread? worker;
    private uint threadId;
    private volatile bool stopping;
    private bool disposed;
    private Exception? loopError;

    public event EventHandler? Pressed;
    public event EventHandler? Released;
    public event EventHandler? Cancelled;
    public volatile bool CancelEnabled;

    public RightAltHotkeyService() => callback = OnKeyboard;

    public void Start()
    {
        EnsureOutsideCallback();
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker?.IsAlive == true) return;
            StopCore();
            stopping = false;
            loopError = null;
            keys.Reset();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            worker = new Thread(() => Run(ready)) { IsBackground = true, Name = "VoiceTyper.KeyboardHook" };
            worker.Start();
            try { ready.Task.GetAwaiter().GetResult(); }
            catch
            {
                worker.Join();
                worker = null;
                throw;
            }
        }
    }

    public void Stop()
    {
        EnsureOutsideCallback();
        lock (lifecycle) StopCore();
    }

    public void Dispose()
    {
        EnsureOutsideCallback();
        lock (lifecycle)
        {
            disposed = true;
            StopCore();
        }
    }

    private void StopCore()
    {
        if (worker is null) return;
        stopping = true;
        if (worker.IsAlive && !KeyboardNative.PostThreadMessageW(threadId, KeyboardNative.Quit, 0, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (!worker.Join(TimeSpan.FromSeconds(1)))
                throw new Win32Exception(error, "Unable to stop keyboard hook thread.");
        }
        if (!worker.Join(TimeSpan.FromSeconds(3)))
            throw new TimeoutException("Keyboard hook handler did not return during shutdown.");
        worker = null;
        threadId = 0;
        keys.Reset();
        if (loopError is { } failure)
        {
            loopError = null;
            throw new InvalidOperationException("Keyboard hook stopped with an error.", failure);
        }
    }

    private void Run(TaskCompletionSource ready)
    {
        nint hook = 0;
        try
        {
            threadId = KeyboardNative.GetCurrentThreadId();
            // Create the queue before announcing readiness, so Stop can post WM_QUIT.
            KeyboardNative.PeekMessageW(out _, 0, 0, 0, 0);
            hook = KeyboardNative.SetWindowsHookExW(KeyboardNative.LowLevelKeyboard, callback,
                KeyboardNative.GetModuleHandleW(null), 0);
            if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install keyboard hook.");
            ready.SetResult();
            int result;
            while ((result = KeyboardNative.GetMessageW(out _, 0, 0, 0)) > 0) { }
            if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Keyboard message loop failed.");
        }
        catch (Exception error)
        {
            loopError = error;
            ready.TrySetException(error);
        }
        finally
        {
            if (hook != 0 && !KeyboardNative.UnhookWindowsHookEx(hook))
                loopError ??= new Win32Exception(Marshal.GetLastWin32Error(), "Unable to release keyboard hook.");
            GC.KeepAlive(callback);
        }
    }

    private nint OnKeyboard(int code, nuint message, nint data)
    {
        if (code == 0 && !stopping)
        {
            try
            {
                var input = Marshal.PtrToStructure<KeyboardNative.KeyboardData>(data);
                var decision = keys.Process(input.VirtualKey, (uint)message, input.Flags, CancelEnabled);
                if (decision.Edge == KeyEdge.Pressed) Notify(Pressed);
                else if (decision.Edge == KeyEdge.Released) Notify(Released);
                else if (decision.Edge == KeyEdge.Cancelled) Notify(Cancelled);
                if (decision.Suppress) return 1;
            }
            catch (Exception)
            {
                // Never allow managed exceptions to cross the native callback boundary.
                Trace.TraceError("Keyboard hook callback failed.");
            }
        }
        return KeyboardNative.CallNextHookEx(0, code, message, data);
    }

    private void Notify(EventHandler? handlers)
    {
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception) { Trace.TraceError("Hotkey event handler failed."); }
        }
    }

    private void EnsureOutsideCallback()
    {
        if (Thread.CurrentThread == worker)
            throw new InvalidOperationException("Schedule hotkey lifecycle changes outside the hook callback.");
    }
}
