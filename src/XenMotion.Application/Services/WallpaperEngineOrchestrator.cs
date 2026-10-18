using System.Collections.Concurrent;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using XenMotion.Core.StateMachines;
using Microsoft.Extensions.Logging;

namespace XenMotion.Application.Services;

/// <summary>
/// Central orchestrator coordinating wallpaper playback per display output.
/// Enforces latest-request-wins cancellation on rapid wallpaper switches,
/// provides global pause/resume control, and routes performance state transitions.
/// </summary>
public sealed class WallpaperEngineOrchestrator : IWallpaperEngineOrchestrator
{
    private readonly ILogger<WallpaperEngineOrchestrator> _logger;
    private readonly Func<string, WallpaperType, IWallpaperRenderer>? _rendererFactory;
    private readonly ConcurrentDictionary<string, MonitorSession> _sessions = new();
    private readonly object _globalLock = new();
    private volatile bool _isGloballyPaused;
    private volatile bool _isGloballyMuted;
    private float _lastGlobalVolume = 1.0f;
    private bool _isDisposed;
    private readonly ISettingsService? _settingsService;

    public event EventHandler<(string MonitorDeviceId, WallpaperState State)>? MonitorStateChanged;
    public event EventHandler<bool>? GlobalPauseChanged;

    public bool IsGloballyPaused => _isGloballyPaused;

    public WallpaperEngineOrchestrator(
        ILogger<WallpaperEngineOrchestrator> logger,
        Func<WallpaperType, IWallpaperRenderer>? rendererFactory = null,
        ISettingsService? settingsService = null,
        Func<string, WallpaperType, IWallpaperRenderer>? monitorRendererFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rendererFactory = monitorRendererFactory ?? (rendererFactory is null ? null : (_, type) => rendererFactory(type));
        _settingsService = settingsService;
    }

    public IReadOnlyDictionary<string, WallpaperState> GetActiveMonitorStates()
    {
        return _sessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.StateMachine.CurrentState);
    }

    public WallpaperState GetMonitorState(string monitorDeviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorDeviceId);
        return _sessions.TryGetValue(monitorDeviceId, out var session)
            ? session.StateMachine.CurrentState
            : WallpaperState.Uninitialized;
    }

    public Wallpaper? GetActiveWallpaper(string monitorDeviceId)
    {
        if (!_sessions.TryGetValue(monitorDeviceId, out var session)) return null;
        lock (session.SwitchLock)
            return session.StateMachine.CurrentState is WallpaperState.Playing or WallpaperState.Paused or WallpaperState.PartiallyVisible
                ? session.ActiveWallpaper : null;
    }

    public async Task ApplyWallpaperAsync(
        string monitorDeviceId,
        Wallpaper wallpaper,
        WallpaperSettings? settings = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorDeviceId);
        ArgumentNullException.ThrowIfNull(wallpaper);
        ThrowIfDisposed();
        var session = GetOrCreateSession(monitorDeviceId);
        CancellationTokenSource? previousCts;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var switchToken = linkedCts.Token;
        long switchId;
        lock (session.SwitchLock)
        {
            previousCts = session.ActiveSwitchCts;
            session.ActiveSwitchCts = linkedCts;
            switchId = ++session.CurrentSwitchId;
        }
        CancelSwitch(previousCts);

        bool entered = false;
        IWallpaperRenderer? renderer = null;
        try
        {
            // Cancellation selects the latest request; the gate protects native resource lifetime.
            await session.OperationGate.WaitAsync(switchToken).ConfigureAwait(false);
            entered = true;
            ThrowIfDisposed();
            switchToken.ThrowIfCancellationRequested();
            lock (session.SwitchLock)
            {
                var state = session.StateMachine.CurrentState;
                if (state == WallpaperState.Error)
                    session.StateMachine.TransitionTo(WallpaperState.Recovering);
                else if (state is not (WallpaperState.Uninitialized or WallpaperState.Stopped or WallpaperState.Recovering or WallpaperState.Loading))
                {
                    if (state != WallpaperState.Stopping) session.StateMachine.TransitionTo(WallpaperState.Stopping);
                    session.StateMachine.TransitionTo(WallpaperState.Stopped);
                }
                if (session.StateMachine.CurrentState != WallpaperState.Loading)
                    session.StateMachine.TransitionTo(WallpaperState.Loading);
            }

            var previousRenderer = session.CurrentRenderer;
            session.CurrentRenderer = null;
            // Reuse the device and desktop layer when switching between the same media type.
            renderer = previousRenderer?.SupportedType == wallpaper.Type ? previousRenderer : _rendererFactory?.Invoke(monitorDeviceId, wallpaper.Type);
            if (previousRenderer != null && !ReferenceEquals(previousRenderer, renderer))
            {
                try { await previousRenderer.StopAsync(CancellationToken.None).ConfigureAwait(false); }
                finally { await previousRenderer.DisposeAsync().ConfigureAwait(false); }
            }
            session.CurrentRenderer = renderer;
            var playbackSettings = settings?.Clone() ?? new WallpaperSettings();
            _settingsService?.CurrentSettings.ApplyEdits(wallpaper.Id, playbackSettings);
            _isGloballyMuted = playbackSettings.IsMuted || playbackSettings.Volume <= 0;
            if (playbackSettings.Volume > 0) _lastGlobalVolume = playbackSettings.Volume;
            if (renderer != null)
                await renderer.LoadAsync(wallpaper.FilePath, playbackSettings, switchToken).ConfigureAwait(false);
            switchToken.ThrowIfCancellationRequested();
            session.StateMachine.TransitionTo(WallpaperState.Ready);
            bool paused = _isGloballyPaused;
            if (!paused && renderer != null)
                await renderer.PlayAsync(switchToken).ConfigureAwait(false);
            switchToken.ThrowIfCancellationRequested();
            lock (session.SwitchLock)
            {
                if (switchId != session.CurrentSwitchId) throw new OperationCanceledException(switchToken);
                session.ActiveWallpaper = wallpaper;
                session.ActiveSettings = playbackSettings;
                session.StateMachine.TransitionTo(paused ? WallpaperState.Paused : WallpaperState.Playing);
            }
        }
        catch (OperationCanceledException)
        {
            if (entered)
            {
                session.StateMachine.TryTransitionTo(WallpaperState.Stopped, out _);
                await ReleaseFailedRendererAsync(session, renderer).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (entered)
            {
                session.StateMachine.TryTransitionTo(WallpaperState.Error, out _);
                await ReleaseFailedRendererAsync(session, renderer).ConfigureAwait(false);
            }
            _logger.LogError(ex, "Failed to apply wallpaper on '{Monitor}'.", monitorDeviceId);
            throw;
        }
        finally
        {
            if (entered) session.OperationGate.Release();
            ReleaseSwitchCts(session, linkedCts);
        }
    }

    private async Task ReleaseFailedRendererAsync(MonitorSession session, IWallpaperRenderer? renderer)
    {
        if (ReferenceEquals(session.CurrentRenderer, renderer)) session.CurrentRenderer = null;
        if (renderer is null) return;
        try { await renderer.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to release unsuccessful wallpaper renderer."); }
    }
    public async Task PauseAsync(string monitorDeviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorDeviceId);
        ThrowIfDisposed();

        if (!_sessions.TryGetValue(monitorDeviceId, out var session))
        {
            return;
        }

        await session.OperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (session.StateMachine.CanTransitionTo(WallpaperState.Paused))
            {
                if (session.CurrentRenderer != null)
                    await session.CurrentRenderer.PauseAsync(ct).ConfigureAwait(false);
                session.StateMachine.TransitionTo(WallpaperState.Paused);
            }
        }
        finally { session.OperationGate.Release(); }
    }

    public async Task ResumeAsync(string monitorDeviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorDeviceId);
        ThrowIfDisposed();

        if (!_sessions.TryGetValue(monitorDeviceId, out var session))
        {
            return;
        }

        await session.OperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        if (session.StateMachine.CurrentState == WallpaperState.Paused &&
            session.StateMachine.CanTransitionTo(WallpaperState.Playing))
        {
            if (session.CurrentRenderer != null)
            {
                await session.CurrentRenderer.PlayAsync(ct).ConfigureAwait(false);
            }
            session.StateMachine.TransitionTo(WallpaperState.Playing);
            _logger.LogInformation("Resumed wallpaper on monitor '{Monitor}'.", monitorDeviceId);
        }
        }
        finally { session.OperationGate.Release(); }
    }

    public async Task PauseAllAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        _isGloballyPaused = true;
        _logger.LogInformation("Global pause requested for all monitors.");

        foreach (var monitorId in _sessions.Keys)
        {
            await PauseAsync(monitorId, ct).ConfigureAwait(false);
        }

        GlobalPauseChanged?.Invoke(this, true);
    }

    public async Task ResumeAllAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        _isGloballyPaused = false;
        _logger.LogInformation("Global resume requested for all monitors.");

        foreach (var monitorId in _sessions.Keys)
        {
            await ResumeAsync(monitorId, ct).ConfigureAwait(false);
        }

        GlobalPauseChanged?.Invoke(this, false);
    }

    public async Task TogglePauseAllAsync(CancellationToken ct = default)
    {
        if (_isGloballyPaused)
        {
            await ResumeAllAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await PauseAllAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task SetVolumeAllAsync(float volume, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        float clamped = Math.Clamp(volume, 0f, 1f);
        _lastGlobalVolume = clamped > 0f ? clamped : _lastGlobalVolume;
        _isGloballyMuted = clamped <= 0f;
        foreach (var session in _sessions.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (session.CurrentRenderer != null)
            {
                await session.CurrentRenderer.SetVolumeAsync(clamped, ct).ConfigureAwait(false);
                if (session.ActiveSettings != null)
                {
                    session.ActiveSettings.IsMuted = clamped <= 0;
                    if (clamped > 0) session.ActiveSettings.Volume = clamped;
                }
            }
        }
    }

    public async Task ToggleMuteAllAsync(CancellationToken ct = default)
    {
        if (_isGloballyMuted)
        {
            await SetVolumeAllAsync(_lastGlobalVolume <= 0f ? 1f : _lastGlobalVolume, ct).ConfigureAwait(false);
        }
        else
        {
            await SetVolumeAllAsync(0f, ct).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(string monitorDeviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorDeviceId);
        ThrowIfDisposed();

        if (!_sessions.TryGetValue(monitorDeviceId, out var session))
        {
            return;
        }

        CancellationTokenSource? activeCts;
        lock (session.SwitchLock)
        {
            activeCts = session.ActiveSwitchCts;
            session.ActiveSwitchCts = null;
            session.CurrentSwitchId++;
        }

        CancelSwitch(activeCts);

        await session.OperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        if (session.StateMachine.CanTransitionTo(WallpaperState.Stopping))
        {
            session.StateMachine.TransitionTo(WallpaperState.Stopping);
            if (session.CurrentRenderer != null)
            {
                await session.CurrentRenderer.StopAsync(ct).ConfigureAwait(false);
            }
            session.StateMachine.TransitionTo(WallpaperState.Stopped);
            _logger.LogInformation("Stopped wallpaper on monitor '{Monitor}'.", monitorDeviceId);
        }
        }
        finally { session.OperationGate.Release(); }
    }

    public async Task StopAllAsync(CancellationToken ct = default)
    {
        foreach (var monitorId in _sessions.Keys)
        {
            await StopAsync(monitorId, ct).ConfigureAwait(false);
        }
    }

    public async Task ApplyPlaybackSettingsAllAsync(WallpaperSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();
        foreach (var session in _sessions.Values)
        {
            Wallpaper? wallpaper;
            bool paused;
            lock (session.SwitchLock)
            {
                if (session.StateMachine.CurrentState is WallpaperState.Stopped or WallpaperState.Uninitialized or WallpaperState.Error) continue;
                wallpaper = session.ActiveWallpaper;
                paused = session.StateMachine.CurrentState == WallpaperState.Paused;
            }
            if (wallpaper is null) continue;
            await session.OperationGate.WaitAsync(ct).ConfigureAwait(false);
            bool updated = false;
            try
            {
                if (session.ActiveWallpaper?.Id != wallpaper.Id || session.StateMachine.CurrentState is WallpaperState.Stopped or WallpaperState.Error) continue;
                var effective = settings.Clone();
                _settingsService?.CurrentSettings.ApplyEdits(wallpaper.Id, effective);
                if (session.CurrentRenderer != null) updated = await session.CurrentRenderer.TryApplySettingsAsync(effective, ct).ConfigureAwait(false);
                if (updated)
                {
                    session.ActiveSettings = effective;
                    _isGloballyMuted = effective.IsMuted || effective.Volume <= 0;
                    if (effective.Volume > 0) _lastGlobalVolume = effective.Volume;
                }
            }
            finally { session.OperationGate.Release(); }
            if (updated) continue;
            await ApplyWallpaperAsync(session.MonitorDeviceId, wallpaper, settings.Clone(), ct).ConfigureAwait(false);
            if (paused) await PauseAsync(session.MonitorDeviceId, ct).ConfigureAwait(false);
        }
    }

    public async Task SetGlobalFpsLimitAsync(int fps, CancellationToken ct = default)
    {
        if (fps is < 15 or > 240)
        {
            throw new ArgumentOutOfRangeException(nameof(fps), "FPS must be between 15 and 240.");
        }

        ThrowIfDisposed();

        foreach (var session in _sessions.Values)
        {
            ct.ThrowIfCancellationRequested();
            await session.OperationGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
            if (session.CurrentRenderer != null)
            {
                try
                {
                    await session.CurrentRenderer.SetFpsLimitAsync(fps, ct).ConfigureAwait(false);
                    if (session.ActiveSettings != null)
                    {
                        session.ActiveSettings.TargetFps = fps;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to update FPS limit for monitor '{Monitor}'.",
                        session.MonitorDeviceId);
                }
            }
            }
            finally { session.OperationGate.Release(); }
        }
    }

    public async Task<bool> RecoverMonitorAsync(string monitorDeviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorDeviceId);
        ThrowIfDisposed();

        if (!_sessions.TryGetValue(monitorDeviceId, out var session))
            return false;

        Wallpaper? wallpaper;
        WallpaperSettings? settings;
        lock (session.SwitchLock)
        {
            if (session.StateMachine.CurrentState != WallpaperState.Error || session.ActiveWallpaper == null)
                return false;

            wallpaper = session.ActiveWallpaper;
            settings = session.ActiveSettings;
            if (!session.StateMachine.TryTransitionTo(WallpaperState.Recovering, out _))
                return false;
        }

        _logger.LogWarning("Attempting automatic wallpaper recovery on monitor '{Monitor}'.", monitorDeviceId);
        try
        {
            await ApplyWallpaperAsync(monitorDeviceId, wallpaper, settings, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic wallpaper recovery failed on monitor '{Monitor}'.", monitorDeviceId);
            return false;
        }
    }

    public async Task UpdatePerformanceStateAsync(
        string monitorDeviceId,
        PerformanceState performanceState,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorDeviceId);
        ThrowIfDisposed();

        if (!_sessions.TryGetValue(monitorDeviceId, out var session))
        {
            return;
        }

        switch (performanceState)
        {
            case PerformanceState.Covered:
            case PerformanceState.FullscreenApplication:
            case PerformanceState.Locked:
            case PerformanceState.DisplayOff:
            case PerformanceState.Sleep:
            case PerformanceState.PausedByUser:
                if (session.StateMachine.CurrentState is WallpaperState.Playing or WallpaperState.PartiallyVisible)
                {
                    await PauseAsync(monitorDeviceId, ct).ConfigureAwait(false);
                }
                break;

            case PerformanceState.ActiveVisible:
                if (!_isGloballyPaused && session.StateMachine.CurrentState == WallpaperState.Paused)
                {
                    await ResumeAsync(monitorDeviceId, ct).ConfigureAwait(false);
                }
                break;

            case PerformanceState.PartiallyVisible:
                if (!_isGloballyPaused && session.StateMachine.CurrentState == WallpaperState.Paused)
                {
                    await ResumeAsync(monitorDeviceId, ct).ConfigureAwait(false);
                }
                if (!_isGloballyPaused && session.StateMachine.CanTransitionTo(WallpaperState.PartiallyVisible))
                {
                    session.StateMachine.TransitionTo(WallpaperState.PartiallyVisible);
                }
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_globalLock)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
        }

        foreach (var session in _sessions.Values)
        {
            CancellationTokenSource? activeCts;
            lock (session.SwitchLock)
            {
                activeCts = session.ActiveSwitchCts;
                session.ActiveSwitchCts = null;
                session.CurrentSwitchId++;
            }

            CancelSwitch(activeCts);

            await session.OperationGate.WaitAsync().ConfigureAwait(false);
            try
            {
            if (session.CurrentRenderer != null)
            {
                await session.CurrentRenderer.DisposeAsync().ConfigureAwait(false);
            }

            session.StateMachine.TryTransitionTo(WallpaperState.Disposed, out _);
            }
            finally { session.OperationGate.Release(); }
        }

        _sessions.Clear();
    }

    private static void CancelSwitch(CancellationTokenSource? cts)
    {
        if (cts == null)
        {
            return;
        }

        try
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (AggregateException)
        {
        }

    }

    private static void ReleaseSwitchCts(MonitorSession session, CancellationTokenSource cts)
    {
        lock (session.SwitchLock)
        {
            if (ReferenceEquals(session.ActiveSwitchCts, cts)) session.ActiveSwitchCts = null;
        }
        // Only the operation that created the CTS disposes it, after all native work is finished.
        cts.Dispose();
    }
    private MonitorSession GetOrCreateSession(string monitorDeviceId)
    {
        return _sessions.GetOrAdd(monitorDeviceId, id =>
        {
            var stateMachine = new WallpaperLifecycleStateMachine();
            stateMachine.StateChanged += (_, e) =>
            {
                MonitorStateChanged?.Invoke(this, (id, e.NewState));
            };

            return new MonitorSession(id, stateMachine);
        });
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(WallpaperEngineOrchestrator));
        }
    }

    private sealed class MonitorSession
    {
        public string MonitorDeviceId { get; }
        public WallpaperLifecycleStateMachine StateMachine { get; }
        public object SwitchLock { get; } = new();
        public SemaphoreSlim OperationGate { get; } = new(1, 1);
        public CancellationTokenSource? ActiveSwitchCts { get; set; }
        public long CurrentSwitchId;
        public IWallpaperRenderer? CurrentRenderer { get; set; }
        public Wallpaper? ActiveWallpaper { get; set; }
        public WallpaperSettings? ActiveSettings { get; set; }

        public MonitorSession(string monitorDeviceId, WallpaperLifecycleStateMachine stateMachine)
        {
            MonitorDeviceId = monitorDeviceId;
            StateMachine = stateMachine;
        }
    }
}
