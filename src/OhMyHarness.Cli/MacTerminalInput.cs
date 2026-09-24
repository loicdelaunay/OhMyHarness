using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OhMyHarness.Cli;

sealed class MacTerminalInput : ITerminalInput
{
    readonly string savedMode;
    readonly Queue<char> characters = new();
    readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    readonly byte[] bytes = new byte[4096];
    readonly char[] text = new char[4096];
    [StructLayout(LayoutKind.Sequential)] struct PollFd { public int Fd; public short Events, Revents; }
    [DllImport("libSystem.B.dylib", SetLastError = true)] static extern int poll(ref PollFd fd, uint count, int timeout);
    [DllImport("libSystem.B.dylib", SetLastError = true)] static extern nint read(int fd, [Out] byte[] buffer, nuint count);
    public MacTerminalInput()
    {
        savedMode = Stty("-g").Trim();
        Stty("raw", "-echo");
    }
    static string Stty(params string[] args)
    {
        var info = new ProcessStartInfo("/bin/stty") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("-f"); info.ArgumentList.Add("/dev/tty");
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Cannot configure the terminal.");
        string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Cannot configure the terminal: " + TerminalText.Clean(error));
        return output;
    }
    public bool TryRead(out char character)
    {
        if (characters.TryDequeue(out character)) return true;
        var fd = new PollFd { Fd = 0, Events = 1 };
        int ready = poll(ref fd, 1, 0);
        if (ready < 0 && Marshal.GetLastWin32Error() != 4) throw new IOException("Terminal polling failed.");
        if (ready <= 0 || (fd.Revents & 1) == 0) return false;
        int count = (int)read(0, bytes, (nuint)bytes.Length);
        if (count <= 0) throw new IOException("Terminal input closed.");
        int length = decoder.GetChars(bytes, 0, count, text, 0, false);
        for (int i = 0; i < length; i++) characters.Enqueue(text[i]);
        return characters.TryDequeue(out character);
    }
    public void Dispose() => Stty(savedMode);
}
