using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Rendering.Direct3D;

/// <summary>
/// Video presenter for desktop wallpaper frames.
/// Uploads decoded RGB32 frames to a reusable GPU texture for filtered D3D11 scaling and DWM composition.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class Direct3D11VideoPresenter : IDisposable
{
    private readonly ILogger _logger;
    private readonly object _syncLock = new();

    private IntPtr _targetHwnd = IntPtr.Zero;
    private MonitorBounds _bounds;
    private MonitorBounds _displayBounds;
    private float _renderScale = 1;
    private DesktopSwapChain? _swapChain;
    private bool _isHardwareAccelerated;
    private bool _isInitialized;
    private bool _isDisposed;
    private bool _loggedPresentation;


    // Presentation uses the GPU; the decoder still supplies CPU RGB32 frames.
    public bool IsHardwareAccelerated => _isHardwareAccelerated;
    public bool IsInitialized => _isInitialized;
    public MonitorBounds CurrentBounds => _displayBounds;
    internal MonitorBounds RenderBounds => _bounds;
    internal IntPtr Device => _isHardwareAccelerated ? _swapChain?.Device ?? IntPtr.Zero : IntPtr.Zero;
    internal void SetVisualAdjustments(WallpaperSettings settings)
    {
        ThrowIfDisposed();
        lock (_syncLock)
        {
            _renderScale = Math.Clamp(settings.RenderScale, .25f, 1f);
            Resize(_displayBounds);
            _swapChain?.SetFiltering(settings.HighQualityFiltering);
            _swapChain?.SetVisualAdjustments(settings.Brightness, settings.Contrast, settings.Saturation);
        }
    }

    internal void PresentFrame(XenMotion.UI.Rendering.Media.DecodedVideoFrame frame, PlaybackScalingMode scalingMode)
    {
        if (frame.TexturePointer == IntPtr.Zero)
        {
            PresentFrame(frame.DataPointer, frame.Width, frame.Height, scalingMode);
            return;
        }
        ThrowIfDisposed();
        lock (_syncLock)
        {
            if (_swapChain is null) return;
            var viewport = VideoViewportCalculator.Calculate(frame.Width, frame.Height, _bounds, scalingMode);
            if (viewport.IsEmpty) return;
            _swapChain.RenderTexture(frame.TexturePointer, frame.TextureSubresource, frame.Width, frame.Height, viewport);
            _swapChain.PresentGpu();
            if (!_loggedPresentation)
            {
                _loggedPresentation = true;
                _logger.LogInformation("First GPU-resident video frame: {Width}x{Height}; no CPU readback/upload.", frame.Width, frame.Height);
            }
        }
    }

    public Direct3D11VideoPresenter(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes D3D11 resources for the target desktop window and monitor dimensions.
    /// </summary>
    public void Initialize(IntPtr targetHwnd, MonitorBounds bounds)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            _swapChain?.Dispose();
            _swapChain = null;
            _isHardwareAccelerated = false;
            _targetHwnd = targetHwnd;
            _bounds = bounds;
            _displayBounds = bounds;
            _renderScale = 1;
            _loggedPresentation = false;

            if (_targetHwnd == IntPtr.Zero)
            {
                _logger.LogDebug("Target HWND is zero; Direct3D11VideoPresenter running in detached/headless mode.");
                _isInitialized = true;
                return;
            }

            _swapChain = new DesktopSwapChain(targetHwnd, bounds.Width, bounds.Height);
            _isHardwareAccelerated = _swapChain.IsHardwareDevice;
            _isInitialized = true;
        }
    }

    /// <summary>
    /// Presents a decoded video frame to the monitor window using the specified scaling mode.
    /// </summary>
    public void PresentFrame(
        IntPtr bufferPtr,
        int bufferWidth,
        int bufferHeight,
        PlaybackScalingMode scalingMode,
        MonitorBounds? virtualDesktopBounds = null)
    {
        ThrowIfDisposed();
        lock (_syncLock)
        {
            if (bufferPtr == IntPtr.Zero || bufferWidth <= 0 || bufferHeight <= 0)
            {
                return;
            }

            IntPtr hwnd = _targetHwnd;
            MonitorBounds bounds = _bounds;

            if (hwnd == IntPtr.Zero)
            {
                return; // Detached/headless mode, no presentation target
            }

            // Calculate destination rendering rectangle based on scaling mode
            var viewport = VideoViewportCalculator.Calculate(
                bufferWidth,
                bufferHeight,
                bounds,
                scalingMode,
                virtualDesktopBounds);

            if (viewport.IsEmpty)
            {
                return;
            }

            try
            {
                _swapChain!.Render(bufferPtr, bufferWidth, bufferHeight, viewport);
                _swapChain.PresentGpu();
                if (!_loggedPresentation)
                {
                    _loggedPresentation = true;
                    _logger.LogInformation("First GPU frame: target 0x{Hwnd:X}; source {Width}x{Height}; viewport {Viewport}.",
                        hwnd.ToInt64(), bufferWidth, bufferHeight, viewport);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error presenting frame to HWND 0x{Hwnd:X8}.", hwnd.ToInt64());
                throw;
            }
        }
    }
    /// <summary>Resizes the presentation viewport when the display layout changes.</summary>
    public void Resize(MonitorBounds newBounds)
    {
        ThrowIfDisposed();
        lock (_syncLock)
        {
            _displayBounds = newBounds;
            var renderBounds = new MonitorBounds(newBounds.X, newBounds.Y,
                Math.Max(1, (int)Math.Round(newBounds.Width * _renderScale)),
                Math.Max(1, (int)Math.Round(newBounds.Height * _renderScale)));
            if (_bounds.Width != renderBounds.Width || _bounds.Height != renderBounds.Height)
                _swapChain?.Resize(renderBounds.Width, renderBounds.Height);
            _bounds = renderBounds;
            _logger.LogDebug("Presenter bounds resized to {Bounds}.", newBounds);
        }
    }
    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _swapChain?.Dispose(); _swapChain = null;

            _targetHwnd = IntPtr.Zero;
            _isInitialized = false;
        }

        _logger.LogDebug("Direct3D11VideoPresenter disposed.");
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(Direct3D11VideoPresenter));
        }
    }
}
