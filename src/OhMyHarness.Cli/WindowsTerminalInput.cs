using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OhMyHarness.Cli;

// ReadKey translates (and can discard) paste delimiters on Windows. Read the VT
// stream instead, so pasted newlines can never become Send commands.
interface ITerminalInput : IDisposable
{
    bool TryRead(out char character);
}

sealed class WindowsTerminalInput : ITerminalInput
{
    readonly ConcurrentQueue<char> characters = new();
    readonly nint input = GetStdHandle(-10);
    readonly uint oldMode;
    readonly Thread reader;
    readonly ManualResetEventSlim started = new();
    volatile bool stopping;
    Exception? failure;
    nint threadHandle;
    [DllImport("kernel32.dll")] static extern nint GetStdHandle(int handle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetConsoleMode(nint handle, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetConsoleMode(nint handle, uint mode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ReadConsoleW(nint handle, [Out] char[] buffer, uint count, out uint read, nint control);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] static extern bool CancelSynchronousIo(nint thread);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
    public WindowsTerminalInput()
    {
        if (!GetConsoleMode(input, out oldMode) || !SetConsoleMode(input, (oldMode | 0x200u | 0x80u) & ~(1u | 2u | 4u | 0x40u)))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        reader = new Thread(Read) { IsBackground = true, Name = "OhMyHarness terminal input" };
        reader.Start(); started.Wait();
    }
    void Read()
    {
        threadHandle = OpenThread(1, false, GetCurrentThreadId()); started.Set();
        var buffer = new char[2048];
        while (!stopping)
        {
            if (!ReadConsoleW(input, buffer, (uint)buffer.Length, out var read, 0))
            {
                if (!stopping) failure = new Win32Exception(Marshal.GetLastWin32Error());
                return;
            }
            for (int i = 0; i < read; i++) characters.Enqueue(buffer[i]);
        }
    }
    public bool TryRead(out char character)
    {
        if (failure != null) throw new IOException("Terminal input failed.", failure);
        return characters.TryDequeue(out character);
    }
    public void Dispose()
    {
        stopping = true;
        // Repeated cancellation covers the tiny interval immediately before ReadConsole.
        for (int i = 0; i < 25 && reader.IsAlive; i++) { CancelSynchronousIo(threadHandle); reader.Join(20); }
        if (threadHandle != 0) CloseHandle(threadHandle);
        SetConsoleMode(input, oldMode); started.Dispose();
    }
}

public static class TerminalKeys
{
    public static ConsoleKeyInfo Character(char c) => c switch
    {
        '\r' => new(c, ConsoleKey.Enter, false, false, false),
        '\x1b' => new(c, ConsoleKey.Escape, false, false, false),
        '\b' or '\x7f' => new('\b', ConsoleKey.Backspace, false, false, false),
        '\t' => new(c, ConsoleKey.Tab, false, false, false),
        >= '\x01' and <= '\x1a' => new(c, ConsoleKey.A + (c - 1), false, false, true),
        _ => new(c, 0, false, false, false)
    };
    public static ConsoleKeyInfo? Sequence(string value)
    {
        var key = value switch
        {
            "\x1b[A" or "\x1bOA" => ConsoleKey.UpArrow,
            "\x1b[B" or "\x1bOB" => ConsoleKey.DownArrow,
            "\x1b[C" or "\x1bOC" => ConsoleKey.RightArrow,
            "\x1b[D" or "\x1bOD" => ConsoleKey.LeftArrow,
            "\x1b[H" or "\x1bOH" or "\x1b[1~" or "\x1b[7~" => ConsoleKey.Home,
            "\x1b[F" or "\x1bOF" or "\x1b[4~" or "\x1b[8~" => ConsoleKey.End,
            "\x1b[3~" => ConsoleKey.Delete,
            "\x1b[5~" => ConsoleKey.PageUp,
            "\x1b[6~" => ConsoleKey.PageDown,
            _ => (ConsoleKey)0
        };
        return key == 0 ? null : new ConsoleKeyInfo('\0', key, false, false, false);
    }
}
