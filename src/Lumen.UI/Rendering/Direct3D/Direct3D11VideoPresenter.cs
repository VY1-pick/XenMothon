using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumen.Core.Enums;
using Lumen.Core.Models;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.Rendering.Direct3D;

/// <summary>
/// Video presenter for desktop wallpaper frames.
/// The current presentation path uses Win32 GDI; a D3D11 device is probed and retained
/// for future GPU-native presentation, but no DXGI swap chain is claimed by this class.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class Direct3D11VideoPresenter : IDisposable
{
    private readonly ILogger _logger;
    private readonly object _syncLock = new();

    private IntPtr _targetHwnd = IntPtr.Zero;
    private MonitorBounds _bounds;
    private IntPtr _d3d11Device = IntPtr.Zero;
    private IntPtr _d3d11Context = IntPtr.Zero;
    private bool _isHardwareAccelerated;
    private bool _isInitialized;
    private bool _isDisposed;

    // True means a hardware D3D11 device was successfully created. It does not mean
    // that the current GDI frame presentation path is GPU-native.
    public bool IsHardwareAccelerated => _isHardwareAccelerated;
    public bool IsInitialized => _isInitialized;
    public MonitorBounds CurrentBounds => _bounds;

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
            _targetHwnd = targetHwnd;
            _bounds = bounds;

            if (_targetHwnd == IntPtr.Zero)
            {
                _logger.LogDebug("Target HWND is zero; Direct3D11VideoPresenter running in detached/headless mode.");
                _isInitialized = true;
                return;
            }

            try
            {
                // Attempt 1: Hardware Direct3D 11 Device
                int hr = D3D11NativeMethods.D3D11CreateDevice(
                    IntPtr.Zero,
                    D3D11NativeMethods.D3D_DRIVER_TYPE_HARDWARE,
                    IntPtr.Zero,
                    D3D11NativeMethods.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                    null,
                    0,
                    D3D11NativeMethods.D3D11_SDK_VERSION,
                    out _d3d11Device,
                    out _,
                    out _d3d11Context);

                if (hr == D3D11NativeMethods.S_OK)
                {
                    _isHardwareAccelerated = true;
                    _logger.LogInformation("Direct3D 11 hardware-accelerated device created successfully (HWND: 0x{Hwnd:X8}).",
                        targetHwnd.ToInt64());
                }
                else
                {
                    // Attempt 2: WARP Software Rasterizer Fallback
                    _logger.LogWarning("Hardware D3D11 device creation failed (HRESULT: 0x{Hr:X8}). Attempting WARP rasterizer.", hr);
                    hr = D3D11NativeMethods.D3D11CreateDevice(
                        IntPtr.Zero,
                        D3D11NativeMethods.D3D_DRIVER_TYPE_WARP,
                        IntPtr.Zero,
                        D3D11NativeMethods.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                        null,
                        0,
                        D3D11NativeMethods.D3D11_SDK_VERSION,
                        out _d3d11Device,
                        out _,
                        out _d3d11Context);

                    if (hr == D3D11NativeMethods.S_OK)
                    {
                        _isHardwareAccelerated = false;
                        _logger.LogInformation("Direct3D 11 WARP software device created successfully.");
                    }
                    else
                    {
                        _logger.LogWarning("D3D11 WARP creation failed (HRESULT: 0x{Hr:X8}). Falling back to Win32 GDI presentation.", hr);
                        _isHardwareAccelerated = false;
                    }
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Direct3D 11 initialization failed. Operating in software GDI presentation mode.");
                _isHardwareAccelerated = false;
                _isInitialized = true;
            }
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

        if (bufferPtr == IntPtr.Zero || bufferWidth <= 0 || bufferHeight <= 0)
        {
            return;
        }

        IntPtr hwnd;
        MonitorBounds bounds;

        lock (_syncLock)
        {
            hwnd = _targetHwnd;
            bounds = _bounds;
        }

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

        // Current presentation path: Win32 GDI / StretchDIBits. The D3D11 device is
        // intentionally not used for frame presentation yet, so this path remains CPU-copy based.
        IntPtr hdc = D3D11NativeMethods.GetDC(hwnd);
        if (hdc == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var bmi = new D3D11NativeMethods.BITMAPINFO
            {
                bmiHeader = new D3D11NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<D3D11NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = bufferWidth,
                    biHeight = -bufferHeight, // top-down DIB
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0, // BI_RGB
                    biSizeImage = (uint)(bufferWidth * bufferHeight * 4)
                }
            };

            D3D11NativeMethods.StretchDIBits(
                hdc,
                viewport.X,
                viewport.Y,
                viewport.Width,
                viewport.Height,
                0,
                0,
                bufferWidth,
                bufferHeight,
                bufferPtr,
                ref bmi,
                D3D11NativeMethods.DIB_RGB_COLORS,
                D3D11NativeMethods.SRCCOPY);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error presenting frame to HWND 0x{Hwnd:X8}.", hwnd.ToInt64());
        }
        finally
        {
            D3D11NativeMethods.ReleaseDC(hwnd, hdc);
        }
    }

    /// <summary>
    /// Resizes the presentation viewport when the display resolution or layout changes.
    /// </summary>
    public void Resize(MonitorBounds newBounds)
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            _bounds = newBounds;
            _logger.LogDebug("Direct3D 11 presenter bounds resized to {Bounds}.", newBounds);
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

            if (_d3d11Context != IntPtr.Zero)
            {
                Marshal.Release(_d3d11Context);
                _d3d11Context = IntPtr.Zero;
            }

            if (_d3d11Device != IntPtr.Zero)
            {
                Marshal.Release(_d3d11Device);
                _d3d11Device = IntPtr.Zero;
            }

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
