namespace OhMyHarness.Core;

public static class PlatformSupport
{
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static string ShellName => OperatingSystem.IsWindows() ? "PowerShell" : OperatingSystem.IsMacOS() ? "zsh" : "sh";
    public static string SystemPrompt => OperatingSystem.IsMacOS()
        ? "The host is macOS. Use POSIX paths and zsh shell commands. WIN/META/CMD mean Command; ALT/OPTION mean Option, CTRL means Control. Use keyboard_keys for available keys. Desktop input requires macOS Accessibility permission; screenshots require Screen Recording permission, in addition to application approval. A missing OS permission cannot be bypassed by Always Allow."
        : "The host is Windows. Use Windows paths and PowerShell commands. WIN/META mean the Windows key. Use keyboard_keys for available keys.";
}
