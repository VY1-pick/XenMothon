using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumen.Application.Services;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.Rendering.Media;

/// <summary>
/// Hardware-accelerated video decoding engine powered by Windows Media Foundation.
/// Decodes MP4, WebM, WMV, and AVI containers, extracting frame buffers, durations, and native frame rates.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class MediaFoundationPlaybackEngine : IDisposable
{
    private readonly ILogger _logger;
    private readonly object _syncLock = new();

    private MediaFoundationNativeMethods.IMFSourceReader? _reader;
    private string _mediaPath = string.Empty;
    private int _videoWidth;
    private int _videoHeight;
    private double _nativeFps = 30.0;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _isMfStarted;
    private bool _isHardwareAccelerated;
    private bool _isDisposed;

    public int VideoWidth => _videoWidth;
    public int VideoHeight => _videoHeight;
    public double NativeFps => _nativeFps;
    public TimeSpan Duration => _duration;
    public bool IsHardwareAccelerated => _isHardwareAccelerated;

    public MediaFoundationPlaybackEngine(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Opens and prepares the video media file through Windows Media Foundation.
    /// Hardware transforms are requested, but end-to-end hardware acceleration is not claimed by the current RGB32/GDI presentation path.
    /// </summary>
    public void Open(string mediaPath)
    {
        ThrowIfDisposed();
        MediaFormatValidator.Validate(mediaPath);

        lock (_syncLock)
        {
            // Re-opening the engine is supported. Release the previous reader first so
            // repeated wallpaper loads do not accumulate COM/media resources.
            ReleaseReader_NoLock();
            _mediaPath = mediaPath;
            _videoWidth = 0;
            _videoHeight = 0;
            _nativeFps = 30.0;
            _duration = TimeSpan.Zero;
            _isHardwareAccelerated = false;

            try
            {
                // Media Foundation is process-global. Start it once for this engine and
                // keep it alive until the engine itself is disposed.
                if (!_isMfStarted)
                {
                    int startupHr = MediaFoundationNativeMethods.MFStartup(
                        MediaFoundationNativeMethods.MF_VERSION,
                        MediaFoundationNativeMethods.MFSTARTUP_FULL);

                    if (startupHr != 0)
                    {
                        throw new InvalidOperationException(
                            $"MFStartup failed (HRESULT: 0x{startupHr:X8}).");
                    }

                    _isMfStarted = true;
                }

                MediaFoundationNativeMethods.IMFAttributes? attributes = null;
                try
                {
                    int hr = MediaFoundationNativeMethods.MFCreateAttributes(out attributes, 3);
                    if (hr != 0 || attributes == null)
                    {
                        throw new InvalidOperationException(
                            $"MFCreateAttributes failed (HRESULT: 0x{hr:X8}).");
                    }

                    attributes.SetUINT32(
                        MediaFoundationNativeMethods.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                    attributes.SetUINT32(
                        MediaFoundationNativeMethods.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);

                    hr = MediaFoundationNativeMethods.MFCreateSourceReaderFromURL(
                        mediaPath, attributes, out var reader);

                    if (hr != 0 || reader == null)
                    {
                        throw new InvalidOperationException(
                            $"Failed to create Media Foundation source reader for '{mediaPath}' (HRESULT: 0x{hr:X8}).");
                    }

                    _reader = reader;
                }
                finally
                {
                    if (attributes != null)
                    {
                        Marshal.ReleaseComObject(attributes);
                    }
                }

                // Configure a CPU-readable RGB32 output. This is intentionally explicit:
                // the current presenter consumes a CPU buffer, so this path must not claim
                // end-to-end GPU presentation even when Media Foundation uses a hardware
                // decoder internally.
                MediaFoundationNativeMethods.IMFMediaType? targetType = null;
                try
                {
                    int hr = MediaFoundationNativeMethods.MFCreateMediaType(out targetType);
                    if (hr != 0 || targetType == null)
                    {
                        throw new InvalidOperationException(
                            $"MFCreateMediaType failed (HRESULT: 0x{hr:X8}).");
                    }

                    targetType.SetGUID(
                        MediaFoundationNativeMethods.MF_MT_MAJOR_TYPE,
                        MediaFoundationNativeMethods.MFMediaType_Video);
                    targetType.SetGUID(
                        MediaFoundationNativeMethods.MF_MT_SUBTYPE,
                        MediaFoundationNativeMethods.MFVideoFormat_RGB32);

                    _reader!.SetStreamSelection(
                        MediaFoundationNativeMethods.MF_SOURCE_READER_ALL_STREAMS, false);
                    _reader.SetStreamSelection(
                        MediaFoundationNativeMethods.MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);

                    hr = _reader.SetMediaType(
                        MediaFoundationNativeMethods.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                        IntPtr.Zero,
                        targetType);

                    if (hr != 0)
                    {
                        throw new InvalidOperationException(
                            $"MF source reader rejected RGB32 output (HRESULT: 0x{hr:X8}).");
                    }
                }
                finally
                {
                    if (targetType != null)
                    {
                        Marshal.ReleaseComObject(targetType);
                    }
                }

                if (_reader!.GetCurrentMediaType(
                        MediaFoundationNativeMethods.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                        out var currentType) == 0 && currentType != null)
                {
                    try
                    {
                        if (currentType.GetUINT64(
                                MediaFoundationNativeMethods.MF_MT_FRAME_SIZE,
                                out ulong frameSize) == 0)
                        {
                            _videoWidth = (int)(frameSize >> 32);
                            _videoHeight = (int)(frameSize & 0xFFFFFFFF);
                        }

                        if (currentType.GetUINT64(
                                MediaFoundationNativeMethods.MF_MT_FRAME_RATE,
                                out ulong frameRate) == 0)
                        {
                            uint num = (uint)(frameRate >> 32);
                            uint den = (uint)(frameRate & 0xFFFFFFFF);
                            if (den > 0)
                            {
                                _nativeFps = (double)num / den;
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(currentType);
                    }
                }

                var durationGuid = MediaFoundationNativeMethods.MF_PD_DURATION;
                if (_reader.GetPresentationAttribute(
                        MediaFoundationNativeMethods.MF_SOURCE_READER_ALL_STREAMS,
                        ref durationGuid,
                        out var prop) == 0)
                {
                    _duration = TimeSpan.FromTicks(prop.hVal);
                }

                // The current RGB32 -> GDI presentation path does not provide proof of
                // end-to-end hardware decoding/presentation. Keep this flag honest until a
                // native D3D11 texture/swap-chain path is implemented.
                _isHardwareAccelerated = false;

                _logger.LogInformation(
                    "Opened video media '{Path}': {Width}x{Height} @ {Fps:F2} FPS, Duration: {Duration}. Hardware acceleration is not reported for the CPU RGB32 presentation path.",
                    mediaPath, _videoWidth, _videoHeight, _nativeFps, _duration);
            }
            catch (Exception ex)
            {
                ReleaseReader_NoLock();
                _logger.LogError(ex,
                    "Failed to initialize MediaFoundationPlaybackEngine for '{Path}'.",
                    mediaPath);
                throw;
            }
        }
    }

    /// <summary>
    /// Reads the next decoded video frame sample.
    /// Handles end-of-stream and seamless rewinding when looping is enabled.
    /// </summary>
    public DecodedVideoFrame? ReadNextFrame(bool isLooping)
    {
        ThrowIfDisposed();

        MediaFoundationNativeMethods.IMFSourceReader? reader;
        lock (_syncLock)
        {
            reader = _reader;
        }

        if (reader == null)
        {
            return null;
        }

        int hr = reader.ReadSample(
            MediaFoundationNativeMethods.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
            0,
            out _,
            out uint streamFlags,
            out long timestampHns,
            out var sample);

        if (hr != 0)
        {
            return null;
        }

        if ((streamFlags & MediaFoundationNativeMethods.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
        {
            if (isLooping)
            {
                SeekToStart();
            }
            return null;
        }

        if (sample == null)
        {
            return null;
        }

        try
        {
            sample.ConvertToContiguousBuffer(out var buffer);
            if (buffer == null)
            {
                Marshal.ReleaseComObject(sample);
                return null;
            }

            buffer.Lock(out IntPtr pData, out _, out uint currentLength);

            return new DecodedVideoFrame(
                pData,
                _videoWidth > 0 ? _videoWidth : 1920,
                _videoHeight > 0 ? _videoHeight : 1080,
                TimeSpan.FromTicks(timestampHns),
                new BufferReleaser(sample, buffer));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading decoded video sample buffer.");
            Marshal.ReleaseComObject(sample);
            return null;
        }
    }

    /// <summary>
    /// Rewinds the media reader to the beginning for seamless looping.
    /// </summary>
    public void SeekToStart()
    {
        ThrowIfDisposed();

        lock (_syncLock)
        {
            if (_reader != null)
            {
                var guid = Guid.Empty;
                var prop = new MediaFoundationNativeMethods.PROPVARIANT { vt = 20 /* VT_I8 */, hVal = 0 };
                _reader.SetCurrentPosition(ref guid, ref prop);
                _logger.LogDebug("Media reader rewound to start.");
            }
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

            ReleaseReader_NoLock();

            if (_isMfStarted)
            {
                MediaFoundationNativeMethods.MFShutdown();
                _isMfStarted = false;
            }
        }

        _logger.LogDebug("MediaFoundationPlaybackEngine disposed.");
    }

    private void ReleaseReader_NoLock()
    {
        if (_reader != null)
        {
            try
            {
                _reader.Flush(MediaFoundationNativeMethods.MF_SOURCE_READER_ALL_STREAMS);
            }
            catch
            {
                // Ignore flush failures while replacing/disposing the reader.
            }

            Marshal.ReleaseComObject(_reader);
            _reader = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(MediaFoundationPlaybackEngine));
        }
    }

    private sealed class BufferReleaser : IDisposable
    {
        private MediaFoundationNativeMethods.IMFSample? _sample;
        private MediaFoundationNativeMethods.IMFMediaBuffer? _buffer;
        private bool _disposed;

        public BufferReleaser(MediaFoundationNativeMethods.IMFSample sample, MediaFoundationNativeMethods.IMFMediaBuffer buffer)
        {
            _sample = sample;
            _buffer = buffer;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _buffer?.Unlock();
            }
            catch
            {
                // Ignored during cleanup
            }

            if (_buffer != null)
            {
                Marshal.ReleaseComObject(_buffer);
                _buffer = null;
            }

            if (_sample != null)
            {
                Marshal.ReleaseComObject(_sample);
                _sample = null;
            }
        }
    }
}

/// <summary>
/// Encapsulates a decoded frame buffer and presentation metadata.
/// </summary>
public sealed record DecodedVideoFrame(
    IntPtr DataPointer,
    int Width,
    int Height,
    TimeSpan Timestamp,
    IDisposable Releaser) : IDisposable
{
    public void Dispose() => Releaser.Dispose();
}
