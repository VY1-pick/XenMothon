using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Services;

/// <summary>Retains the latest local run so desktop failures can be diagnosed after the UI closes.</summary>
public sealed class LocalFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public LocalFileLoggerProvider(string appDataRoot)
    {
        string directory = Path.Combine(appDataRoot, "Logs");
        Directory.CreateDirectory(directory);
        _writer = new StreamWriter(new FileStream(Path.Combine(directory, "wallpaper-latest.log"),
            FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _writer.WriteLine($"{DateTimeOffset.UtcNow:O} Executable: {Environment.ProcessPath}; PID: {Environment.ProcessId}; OS: {Environment.OSVersion}");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }

    private sealed class FileLogger(LocalFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information && level != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            lock (owner._sync)
            {
                if (owner._disposed) return;
                try
                {
                    owner._writer.WriteLine($"{DateTimeOffset.UtcNow:O} {level} {category}: {formatter(state, exception)}");
                    if (exception is not null) owner._writer.WriteLine(exception);
                }
                catch (IOException) { /* A logging failure must not stop wallpaper playback. */ }
            }
        }
    }
}
