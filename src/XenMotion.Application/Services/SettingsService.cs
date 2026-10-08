using System.Text.Json;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.Application.Services;

/// <summary>
/// Thread-safe implementation of settings persistence with asynchronous file I/O.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private ApplicationSettings _currentSettings = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(string settingsFilePath, ILogger<SettingsService> logger)
    {
        _settingsFilePath = settingsFilePath ?? throw new ArgumentNullException(nameof(settingsFilePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ApplicationSettings CurrentSettings => _currentSettings;
    public bool IsFirstRun { get; private set; }

    public event EventHandler<ApplicationSettings>? SettingsChanged;

    public async Task LoadSettingsAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IsFirstRun = !File.Exists(_settingsFilePath);
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogInformation("Settings file not found at '{Path}'. Initializing with default settings.", _settingsFilePath);
                _currentSettings = new ApplicationSettings();
            }
            else
            {

            if (new FileInfo(_settingsFilePath).Length > 16 * 1024 * 1024) throw new InvalidDataException("Settings file exceeds the 16 MiB limit.");
            string json = await File.ReadAllTextAsync(_settingsFilePath, ct).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions);
            _currentSettings = loaded ?? new ApplicationSettings();
            _currentSettings.Normalize();
            _logger.LogInformation("Successfully loaded application settings from '{Path}'.", _settingsFilePath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to read settings from '{Path}'. Using default configuration.", _settingsFilePath);
            _currentSettings = new ApplicationSettings();
        }
        finally
        {
            _fileLock.Release();
        }

        SettingsChanged?.Invoke(this, _currentSettings);
    }

    public async Task SaveSettingsAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try { await PersistAsync(_currentSettings, ct).ConfigureAwait(false); }
        finally { _fileLock.Release(); }
        SettingsChanged?.Invoke(this, _currentSettings);
    }

    public async Task UpdateSettingsAsync(Action<ApplicationSettings> updateAction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updateAction);
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Readers keep a stable snapshot until both validation and the atomic write succeed.
            var next = JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(_currentSettings, JsonOptions), JsonOptions)!;
            updateAction(next);
            await PersistAsync(next, ct).ConfigureAwait(false);
            _currentSettings = next;
        }
        finally { _fileLock.Release(); }
        SettingsChanged?.Invoke(this, _currentSettings);
    }

    private async Task PersistAsync(ApplicationSettings settings, CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        settings.Normalize();
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 16 * 1024 * 1024)
            throw new InvalidDataException("Settings exceed the 16 MiB limit; previous settings were kept.");
        string temporary = _settingsFilePath + ".tmp";
        await File.WriteAllTextAsync(temporary, json, ct).ConfigureAwait(false);
        File.Move(temporary, _settingsFilePath, overwrite: true);
        _logger.LogInformation("Successfully persisted application settings to '{Path}'.", _settingsFilePath);
    }
}
