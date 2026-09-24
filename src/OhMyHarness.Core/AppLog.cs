using System.Text.Json;
using System.Threading.Channels;

namespace OhMyHarness.Core;

public enum AppLogLevel { Debug, Information, Warning, Error, Critical }

public static class AppLog
{
    sealed record Settings(string Root, bool Enabled, AppLogLevel Level, int Days);
    sealed record Entry(Settings Settings, string Json, TaskCompletionSource? Flushed = null);
    static Settings settings = new(PortableStorage.Root, true, AppLogLevel.Information, 7);
    static readonly Channel<Entry> queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait });
    static readonly Task worker = Task.Run(WriteLoop);
    static AppLog()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Write(AppLogLevel.Critical, "process.unhandled", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => Write(AppLogLevel.Error, "task.unobserved", args.Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutdownAsync().GetAwaiter().GetResult();
    }
    public static string DirectoryPath => Path.Combine(settings.Root, "logs");
    public static void Configure(FeatureSettings config)
    {
        settings = new(PortableStorage.Root, config.LogsEnabled, Enum.TryParse<AppLogLevel>(config.LogLevel, out var level) ? level : AppLogLevel.Information, Math.Clamp(config.LogRetentionDays, 1, 365));
        // A configuration change also applies retention when no events meet the selected level.
        if (settings.Enabled) queue.Writer.TryWrite(new(settings, ""));
    }
    public static void Write(AppLogLevel level, string operation, Exception? error = null, int? chatId = null)
    {
        var current = settings;
        if (!current.Enabled || level < current.Level) return;
        // Never store prompts, tool arguments, API keys, headers or response bodies.
        var json = JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, level = level.ToString(), operation, chatId,
            errorType = error?.GetType().FullName, errorCode = error?.HResult,
            httpStatus = (error as HttpRequestException)?.StatusCode, stack = error?.StackTrace });
        queue.Writer.TryWrite(new(current, json));
    }
    public static void Prune(string directory, int days, DateTime utcNow)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "omh-*.jsonl", SearchOption.TopDirectoryOnly))
            try { if (File.GetLastWriteTimeUtc(path) < utcNow.AddDays(-Math.Clamp(days, 1, 365))) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    static async Task WriteLoop()
    {
        var lastPrune = new Dictionary<string, DateOnly>();
        await foreach (var entry in queue.Reader.ReadAllAsync())
        {
            try
            {
                if (entry.Flushed != null) continue;
                var now = DateTime.UtcNow; var directory = Path.Combine(entry.Settings.Root, "logs");
                Directory.CreateDirectory(directory);
                if (entry.Json.Length == 0 || lastPrune.GetValueOrDefault(directory) != DateOnly.FromDateTime(now))
                { Prune(directory, entry.Settings.Days, now); lastPrune[directory] = DateOnly.FromDateTime(now); }
                if (entry.Json.Length > 0) await File.AppendAllTextAsync(Path.Combine(directory, $"omh-{now:yyyy-MM-dd}-{Environment.ProcessId}.jsonl"), entry.Json + "\n").ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Diagnostics must not stop an agent. */ }
            finally { entry.Flushed?.TrySetResult(); }
        }
    }
    public static async Task FlushAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(new(settings, "", completion));
        await completion.Task;
    }
    public static async Task ShutdownAsync()
    {
        queue.Writer.TryComplete();
        try { await worker.WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { }
    }
}
