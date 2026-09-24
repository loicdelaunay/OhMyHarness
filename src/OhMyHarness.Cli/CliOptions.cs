namespace OhMyHarness.Cli;

public sealed class CliOptions
{
    public string? Directory { get; set; }
    public string? Database { get; set; }
    public string Prompt { get; set; } = "";
    public int? Chat { get; set; }
    public int? Provider { get; set; }
    public bool Run { get; set; }
    public bool Json { get; set; }
    public bool Execute { get; set; }
    public bool Allow { get; set; }
    public bool Help { get; set; }
    public bool Version { get; set; }
    public bool RenderDemo { get; set; }
    public string? Theme { get; set; }

    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for " + args[i - 1]);
            switch (args[i])
            {
                case "--help" or "-h": result.Help = true; break;
                case "--version" or "-v": result.Version = true; break;
                case "--render-demo": result.RenderDemo = true; break;
                case "--theme": result.Theme = Value(); if (!CliThemes.IsValid(result.Theme)) throw new ArgumentException("Unknown CLI theme: " + result.Theme); break;
                case "--database": result.Database = Path.GetFullPath(Value()); break;
                case "--project": result.Directory = Path.GetFullPath(Value()); break;
                case "--chat": result.Chat = Positive(Value()); break;
                case "--provider": result.Provider = Positive(Value()); break;
                case "--json": result.Json = true; break;
                case "--execute": result.Execute = true; break;
                case "--allow-tools": result.Allow = true; break;
                case "run" when i == 0: result.Run = true; break;
                default:
                    if (args[i].StartsWith('-')) throw new ArgumentException("Unknown option: " + args[i]);
                    if (result.Run && result.Prompt.Length == 0) result.Prompt = args[i];
                    else if (!result.Run && result.Directory == null) result.Directory = Path.GetFullPath(args[i]);
                    else throw new ArgumentException("Unexpected argument: " + args[i]);
                    break;
            }
        }
        if (result.Directory != null && !System.IO.Directory.Exists(result.Directory)) throw new DirectoryNotFoundException(result.Directory);
        if (!result.Run && (result.Json || result.Execute || result.Allow)) throw new ArgumentException("--json, --execute and --allow-tools require 'run'.");
        if (result.Directory == null && result.Chat == null) result.Directory = Environment.CurrentDirectory;
        return result;
        static int Positive(string text) => int.TryParse(text, out int value) && value > 0 ? value : throw new ArgumentException("Expected a positive numeric ID.");
    }

    public const string HelpText = """
    OhMyHarness CLI 1.6.1
    Your portable AI workspace, in the terminal.

      omh [project-folder]                       Interactive terminal interface
      omh --database X:\OhMyHarness\database.sqlite  Share the desktop workspace
      omh run "Review this repository" --project .  One-shot, Plan mode by default
      omh run "Implement the fix" --project . --execute
      omh run "Explain the code" --project . --json

    Options
      --database PATH   SQLite file (default: beside omh.exe)
      --project PATH    Attach this source folder to a project
      --chat ID         Resume an existing conversation
      --provider ID     Select a configured provider
      --theme ID        CLI theme (e.g. crt-green, crt-amber, neon-synthwave)
      --render-demo     Offline preview; can be combined with --theme
      --execute         Allow modifying tools in non-interactive runs
      --allow-tools     Approve sensitive tools in this non-interactive process
      --json            Newline-delimited JSON events for automation
      --help, --version

    Interactive keys
      Ctrl+P  Command palette      Ctrl+N  New conversation
      Ctrl+O  Conversations        Ctrl+B  Models
      Enter   Send / queue         Ctrl+J or Alt+Enter  New line
      Escape  Stop this run        Ctrl+Q  Quit
      PgUp / PgDn / mouse wheel    Scroll history; End follows the live response
      /       Commands             Ctrl+L  Redraw

    Configure a provider with /connect. Browser and desktop automation are
    available through MCP in the CLI; the graphical WebView is not hosted here.
    Questions cancel non-interactive runs. Sensitive tools are denied by default.
    """;
}
