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
        '\b' => new('\b', ConsoleKey.Backspace, false, false, false),
        // Both BS and DEL can mean ordinary Backspace; require explicit modifiers for word deletion.
        '\x7f' => new('\b', ConsoleKey.Backspace, false, false, false),
        '\t' => new(c, ConsoleKey.Tab, false, false, false),
        >= '\x01' and <= '\x1a' => new(c, ConsoleKey.A + (c - 1), false, false, true),
        _ => new(c, 0, false, false, false)
    };
    public static ConsoleKeyInfo? Sequence(string value)
    {
        if (value.Length < 3 || value[0] != '\x1b' || value[1] is not ('[' or 'O')) return null;
        char final = value[^1];
        var parameters = value[2..^1].Split(';');
        int modifier = 1;
        if (parameters.Length > 2 || (parameters.Length == 2 &&
            (!int.TryParse(parameters[1], out modifier) || modifier is < 1 or > 8))) return null;
        if (parameters[0].Length > 0 && !int.TryParse(parameters[0], out _)) return null;
        var key = final switch
        {
            'A' => ConsoleKey.UpArrow,
            'B' => ConsoleKey.DownArrow,
            'C' => ConsoleKey.RightArrow,
            'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home,
            'F' => ConsoleKey.End,
            'u' => parameters[0] switch
            {
                "8" or "127" => ConsoleKey.Backspace, "13" => ConsoleKey.Enter,
                "97" => ConsoleKey.A, "99" => ConsoleKey.C, "118" => ConsoleKey.V,
                "120" => ConsoleKey.X, "121" => ConsoleKey.Y, "122" => ConsoleKey.Z,
                _ => (ConsoleKey)0
            },
            '~' => parameters[0] switch
            {
                "1" or "7" => ConsoleKey.Home, "4" or "8" => ConsoleKey.End,
                "3" => ConsoleKey.Delete, "5" => ConsoleKey.PageUp, "6" => ConsoleKey.PageDown,
                _ => (ConsoleKey)0
            },
            _ => (ConsoleKey)0
        };
        int flags = modifier - 1;
        return key == 0 ? null : new ConsoleKeyInfo('\0', key, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
    }
}
