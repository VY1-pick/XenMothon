using System.Text.Json;
using Lumen.Core.Models;
using Microsoft.Extensions.Logging;

namespace Lumen.Application.Services;

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

    public event EventHandler<ApplicationSettings>? SettingsChanged;

    public async Task LoadSettingsAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogInformation("Settings file not found at '{Path}'. Initializing with default settings.", _settingsFilePath);
                _currentSettings = new ApplicationSettings();
            }
            else
            {

            string json = await File.ReadAllTextAsync(_settingsFilePath, ct).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions);
            _currentSettings = loaded ?? new ApplicationSettings();
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
        try
        {
            string directory = Path.GetDirectoryName(_settingsFilePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempFile = _settingsFilePath + ".tmp";
            string json = JsonSerializer.Serialize(_currentSettings, JsonOptions);
            await File.WriteAllTextAsync(tempFile, json, ct).ConfigureAwait(false);
            File.Move(tempFile, _settingsFilePath, overwrite: true);

            _logger.LogInformation("Successfully persisted application settings to '{Path}'.", _settingsFilePath);
        }
        finally
        {
            _fileLock.Release();
        }

        SettingsChanged?.Invoke(this, _currentSettings);
    }

    public async Task UpdateSettingsAsync(Action<ApplicationSettings> updateAction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            updateAction(_currentSettings);
        }
        finally
        {
            _fileLock.Release();
        }

        await SaveSettingsAsync(ct).ConfigureAwait(false);
    }
}
