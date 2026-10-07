using System.Text;
using System.Text.Json;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Services;

/// <summary>
/// Writes small local crash reports without sending user data anywhere. Reports are retained locally
/// so a future diagnostics UI can offer explicit export by the user.
/// </summary>
public sealed class CrashReportService
{
    private readonly string _logDirectory;
    private readonly ILogger<CrashReportService> _logger;
    private readonly object _sync = new();

    public CrashReportService(string appDataRoot, ILogger<CrashReportService> logger)
    {
        _logDirectory = Path.Combine(appDataRoot, "Logs");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string WriteCrashReport(Exception exception, string source, string[]? args = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Directory.CreateDirectory(_logDirectory);

        var timestamp = DateTimeOffset.UtcNow;
        var fileName = $"crash-{timestamp:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(_logDirectory, fileName);

        var report = new
        {
            timestampUtc = timestamp,
            source,
            process = Environment.ProcessPath,
            processId = Environment.ProcessId,
            os = Environment.OSVersion.VersionString,
            runtime = Environment.Version.ToString(),
            args = args ?? [],
            exception = SerializeException(exception)
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        lock (_sync)
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        _logger.LogError(exception, "Unhandled exception captured by XenMotion crash reporting. Report: {ReportPath}", path);
        CleanupOldReports();
        return path;
    }

    private void CleanupOldReports()
    {
        try
        {
            var files = new DirectoryInfo(_logDirectory)
                .GetFiles("crash-*.json")
                .OrderByDescending(x => x.CreationTimeUtc)
                .Skip(20)
                .ToArray();
            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not prune old crash reports.");
        }
    }

    private static object SerializeException(Exception exception)
        => new
        {
            type = exception.GetType().FullName,
            message = exception.Message,
            details = exception.Data.Keys.Cast<object>().ToDictionary(key => key.ToString() ?? "", key => exception.Data[key]?.ToString()),
            stackTrace = exception.StackTrace,
            inner = exception.InnerException is null ? null : SerializeException(exception.InnerException)
        };
}
