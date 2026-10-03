using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.Services;

/// <summary>
/// Keeps Lumen's unpackaged Windows startup entry synchronized with the user setting.
/// Uses HKCU so no administrator elevation is required.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsStartupService : IDisposable
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lumen";
    private readonly ILogger<WindowsStartupService> _logger;
    private bool _disposed;

    public WindowsStartupService(ILogger<WindowsStartupService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void Apply(bool enabled)
    {
        ThrowIfDisposed();

        // Never register a Visual Studio/debug process as the user's Windows startup executable.
        if (Debugger.IsAttached)
        {
            _logger.LogDebug("Skipping Windows startup synchronization while a debugger is attached.");
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key == null)
        {
            throw new InvalidOperationException("Unable to open the current-user Windows startup registry key.");
        }

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            _logger.LogInformation("Lumen Windows startup registration disabled.");
            return;
        }

        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the Lumen executable path.");

        key.SetValue(ValueName, $"\"{executablePath}\" --minimized", RegistryValueKind.String);
        _logger.LogInformation("Lumen Windows startup registration enabled.");
    }

    public void Dispose() => _disposed = true;

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsStartupService));
        }
    }
}
