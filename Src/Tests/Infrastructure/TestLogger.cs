using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RimAI.Tests.Infrastructure;

/// <summary>
/// Shared test logger. One log file per dotnet-test process invocation, written to
/// logs/test-YYYYMMDD-HHmmss.jsonl alongside the binary. All test loggers write to
/// the same file so the full run is a single auditable stream.
/// Also exposes a captured Events list so individual tests can assert log content.
/// </summary>
public sealed class TestLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Events { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Debug;

    public void Log<TState>(LogLevel level, EventId id, TState state,
        Exception? ex, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;
        var msg = formatter(state, ex);
        Events.Add((level, msg));
        TestLogFile.Write(typeof(T).Name, level, msg);
    }
}

/// <summary>
/// Static JSONL file sink shared across the entire test run. Initialized lazily on
/// first write; flushed and closed on process exit.
/// </summary>
internal static class TestLogFile
{
    private static readonly Lazy<StreamWriter> Writer = new(Open);
    private static readonly object Lock = new();

    private static StreamWriter Open()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"test-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        var w = new StreamWriter(path, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { lock (Lock) { w.Flush(); w.Close(); } } catch { /* best-effort */ } };
        Console.WriteLine($"[TestLog] → {path}");
        return w;
    }

    public static void Write(string category, LogLevel level, string message)
    {
        var line = JsonSerializer.Serialize(new
        {
            ts       = DateTime.UtcNow.ToString("O"),
            level    = level.ToString(),
            category,
            message
        });
        lock (Lock)
        {
            Writer.Value.WriteLine(line);
        }
        Console.WriteLine($"[{level.ToString()[..3].ToUpper()}] [{category}] {message}");
    }
}
