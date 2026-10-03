using System.Collections.Concurrent;
using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Lumen.Core.StateMachines;
using Microsoft.Extensions.Logging;

namespace Lumen.Application.Services;

/// <summary>
/// Central orchestrator coordinating wallpaper playback per display output.
/// Enforces latest-request-wins cancellation on rapid wallpaper switches,
/// provides global pause/resume control, and routes performance state transitions.
/// </summary>
public sealed class WallpaperEngineOrchestrator : IWallpaperEngineOrchestrator
{
    private readonly ILogger<WallpaperEngineOrchestrator> _logger;
    private readonly Func<WallpaperType, IWallpaperRenderer>? _rendererFactory;
    private readonly ConcurrentDictionary<string, MonitorSession> _sessions = new();
    private readonly object _globalLock = new();
    private volatile bool _isGloballyPaused;
    private volatile bool _isGloballyMuted;
    private float _lastGlobalVolume = 1.0f;
    private bool _isDisposed;

    public event EventHandler<(string MonitorDeviceId, WallpaperState State)>? MonitorStateChanged;
    public event EventHandler<bool>? GlobalPauseChanged;

    public bool IsGloballyPaused => _isGloballyPaused;

    public WallpaperEngineOrchestrator(
        ILogger<WallpaperEngineOrchestrator> logger,
        Func<WallpaperType, IWallpaperRenderer>? rendererFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rendererFactory = rendererFactory;
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

    public async Task ApplyWallpaperAsync(
        string monitorDeviceId,
        Wallpaper wallpaper,
        WallpaperSettings? settings = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorDeviceId);
        ArgumentNullException.ThrowIfNull(wallpaper);

        ThrowIfDisposed();

        MonitorSession session = GetOrCreateSession(monitorDeviceId);

        // Cancel any pending switch on this monitor (latest request wins)
        CancellationTokenSource? previousCts;
        CancellationTokenSource linkedCts;
        long switchId;

        lock (session.SwitchLock)
        {
            previousCts = session.ActiveSwitchCts;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            session.ActiveSwitchCts = linkedCts;
            switchId = ++session.CurrentSwitchId;

            // Step 1: Transition to Loading under the monitor switch lock.
            // If currently in an active or ready state, transition through Stopping -> Stopped.
            if (session.StateMachine.CurrentState is WallpaperState.Playing
                or WallpaperState.PartiallyVisible
                or WallpaperState.Paused
                or WallpaperState.Ready)
            {
                session.StateMachine.TransitionTo(WallpaperState.Stopping);
                session.StateMachine.TransitionTo(WallpaperState.Stopped);
            }
            else if (session.StateMachine.CurrentState == WallpaperState.Stopping)
            {
                session.StateMachine.TransitionTo(WallpaperState.Stopped);
            }

            // Only transition to Loading if not already in Loading state (e.g. from an immediate superseded switch)
            if (session.StateMachine.CurrentState != WallpaperState.Loading)
            {
                session.StateMachine.TransitionTo(WallpaperState.Loading);
            }
        }

        // Cancel and dispose the detached previous CTS outside the lock
        CancelAndDispose(previousCts);

        var switchToken = linkedCts.Token;

        try
        {
            _logger.LogInformation("Applying wallpaper '{Title}' ({Id}) to monitor '{Monitor}' [SwitchId: {SwitchId}].",
                wallpaper.Title, wallpaper.Id, monitorDeviceId, switchId);

            switchToken.ThrowIfCancellationRequested();

            // Step 2: Initialize / Load via Renderer if factory provided
            if (_rendererFactory != null)
            {
                var renderer = _rendererFactory(wallpaper.Type);
                IWallpaperRenderer? previousRenderer = null;
                lock (session.SwitchLock)
                {
                    if (switchId != session.CurrentSwitchId)
                    {
                        return;
                    }
                    previousRenderer = session.CurrentRenderer;
                    session.CurrentRenderer = renderer;
                }

                if (previousRenderer != null && !ReferenceEquals(previousRenderer, renderer))
                {
                    try
                    {
                        await previousRenderer.StopAsync(CancellationToken.None).ConfigureAwait(false);
                        await previousRenderer.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing superseded renderer on monitor '{Monitor}'.", monitorDeviceId);
                    }
                }

                await renderer.LoadAsync(wallpaper.FilePath, settings ?? new WallpaperSettings(), switchToken).ConfigureAwait(false);
            }

            switchToken.ThrowIfCancellationRequested();

            // Step 3: Transition to Ready and Play (or stay paused if globally paused)
            lock (session.SwitchLock)
            {
                if (switchId != session.CurrentSwitchId)
                {
                    return;
                }

                session.StateMachine.TransitionTo(WallpaperState.Ready);
                session.ActiveWallpaper = wallpaper;
                session.ActiveSettings = settings ?? new WallpaperSettings();
            }

            if (_isGloballyPaused)
            {
                lock (session.SwitchLock)
                {
                    if (switchId != session.CurrentSwitchId)
                    {
                        return;
                    }

                    session.StateMachine.TransitionTo(WallpaperState.Paused);
                    _logger.LogInformation("Wallpaper '{Title}' loaded in Paused state due to global pause.", wallpaper.Title);
                }
            }
            else
            {
                if (session.CurrentRenderer != null)
                {
                    await session.CurrentRenderer.PlayAsync(switchToken).ConfigureAwait(false);
                }

                switchToken.ThrowIfCancellationRequested();

                lock (session.SwitchLock)
                {
                    if (switchId != session.CurrentSwitchId)
                    {
                        return;
                    }

                    session.StateMachine.TransitionTo(WallpaperState.Playing);
                    _logger.LogInformation("Wallpaper '{Title}' successfully playing on monitor '{Monitor}'.",
                        wallpaper.Title, monitorDeviceId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Wallpaper switch [{SwitchId}] on monitor '{Monitor}' was superseded or cancelled.", switchId, monitorDeviceId);
            lock (session.SwitchLock)
            {
                // If this was the active switch and was cancelled (not superseded), ensure monitor does not remain in Loading
                if (switchId == session.CurrentSwitchId && session.StateMachine.CurrentState == WallpaperState.Loading)
                {
                    session.StateMachine.TryTransitionTo(WallpaperState.Stopped, out _);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply wallpaper to monitor '{Monitor}'. Entering Error state.", monitorDeviceId);
            lock (session.SwitchLock)
            {
                if (switchId == session.CurrentSwitchId)
                {
                    session.StateMachine.TryTransitionTo(WallpaperState.Error, out _);
                }
            }
            throw;
        }
        finally
        {
            ReleaseSwitchCts(session, linkedCts);
        }
    }

    public async Task PauseAsync(string monitorDeviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorDeviceId);
        ThrowIfDisposed();

        if (!_sessions.TryGetValue(monitorDeviceId, out var session))
        {
            return;
        }

        if (session.StateMachine.CanTransitionTo(WallpaperState.Paused))
        {
            if (session.CurrentRenderer != null)
            {
                await session.CurrentRenderer.PauseAsync(ct).ConfigureAwait(false);
            }
            session.StateMachine.TransitionTo(WallpaperState.Paused);
            _logger.LogInformation("Paused wallpaper on monitor '{Monitor}'.", monitorDeviceId);
        }
    }

    public async Task ResumeAsync(string monitorDeviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorDeviceId);
        ThrowIfDisposed();

        if (!_sessions.TryGetValue(monitorDeviceId, out var session))
        {
            return;
        }

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

        CancelAndDispose(activeCts);

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

    public async Task StopAllAsync(CancellationToken ct = default)
    {
        foreach (var monitorId in _sessions.Keys)
        {
            await StopAsync(monitorId, ct).ConfigureAwait(false);
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

            CancelAndDispose(activeCts);

            if (session.CurrentRenderer != null)
            {
                await session.CurrentRenderer.DisposeAsync().ConfigureAwait(false);
            }

            session.StateMachine.TryTransitionTo(WallpaperState.Disposed, out _);
        }

        _sessions.Clear();
    }

    private static void CancelAndDispose(CancellationTokenSource? cts)
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

        try
        {
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void ReleaseSwitchCts(MonitorSession session, CancellationTokenSource cts)
    {
        bool isCurrentOwner = false;
        lock (session.SwitchLock)
        {
            if (ReferenceEquals(session.ActiveSwitchCts, cts))
            {
                session.ActiveSwitchCts = null;
                isCurrentOwner = true;
            }
        }

        if (isCurrentOwner)
        {
            CancelAndDispose(cts);
        }
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
