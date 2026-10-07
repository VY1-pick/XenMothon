using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using XenMotion.Application.Services;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Rendering.Media;

/// <summary>
/// Video decoding engine powered by Windows Media Foundation with CPU-accessible RGB32 output.
/// Decodes MP4, WebM, WMV, and AVI containers, extracting frame buffers, durations, and native frame rates.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class MediaFoundationPlaybackEngine : IDisposable
{
    private readonly ILogger _logger;
    private readonly object _syncLock = new();

    private MediaFoundationNativeMethods.IMFSourceReader? _reader;
    private MediaFoundationD3DManager? _deviceManager;
    private string _mediaPath = string.Empty;
    private int _videoWidth;
    private int _videoHeight;
    private double _nativeFps = 30.0;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _isMfStarted;
    private bool _isHardwareAccelerated;
    private bool _isDisposed;
    private long _decodedFrameCount;
    private long _gpuDecodedFrameCount;
    public long DecodedFrameCount => Interlocked.Read(ref _decodedFrameCount);
    internal long GpuDecodedFrameCount => Interlocked.Read(ref _gpuDecodedFrameCount);

    public int VideoWidth => _videoWidth;
    public int VideoHeight => _videoHeight;
    public double NativeFps => _nativeFps;
    public TimeSpan Duration => _duration;
    public bool IsHardwareAccelerated => _isHardwareAccelerated;
    public bool EndOfStream { get; private set; }

    public MediaFoundationPlaybackEngine(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Opens and prepares the video media file through Windows Media Foundation.
    /// Hardware transforms are requested; the reader exposes RGB32 CPU buffers, so hardware decoding is not guaranteed.
    /// </summary>
    public void Open(string mediaPath, IntPtr d3dDevice = default, int heightLimit = 0)
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
            Interlocked.Exchange(ref _decodedFrameCount, 0);
            Interlocked.Exchange(ref _gpuDecodedFrameCount, 0);

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
                    // The basic processor converts YUV to RGB in software using a path
                    // intended for thumbnails, not real-time high-resolution playback.
                    Marshal.ThrowExceptionForHR(attributes.SetUINT32(
                        MediaFoundationNativeMethods.MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, 1));
                    if (d3dDevice != IntPtr.Zero)
                    {
                        _deviceManager = new MediaFoundationD3DManager(d3dDevice);
                        var managerKey = MediaFoundationNativeMethods.MF_SOURCE_READER_D3D_MANAGER;
                        Marshal.ThrowExceptionForHR(attributes.SetUnknown(ref managerKey, _deviceManager.ComObject));
                    }

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

                // Keep the source resolution. The presenter uploads RGB32 once and scales
                // on the GPU; requesting efficient conversion does not guarantee GPU decoding.
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
                    if (heightLimit > 0 && _reader.GetNativeMediaType(MediaFoundationNativeMethods.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, out var native) == 0)
                    {
                        try
                        {
                            if (native.GetUINT64(MediaFoundationNativeMethods.MF_MT_FRAME_SIZE, out ulong size) == 0 && (uint)size > heightLimit)
                            {
                                int width = Math.Max(2, (int)Math.Round((size >> 32) * (double)heightLimit / (uint)size) / 2 * 2);
                                int height = Math.Max(2, heightLimit / 2 * 2);
                                Marshal.ThrowExceptionForHR(targetType.SetUINT64(MediaFoundationNativeMethods.MF_MT_FRAME_SIZE, ((ulong)width << 32) | (uint)height));
                            }
                        }
                        finally { Marshal.ReleaseComObject(native); }
                    }

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

                if (_videoWidth is < 1 or > 16384 || _videoHeight is < 1 or > 16384 ||
                    (long)_videoWidth * _videoHeight > 40000000 || !double.IsFinite(_nativeFps) || _nativeFps <= 0 || _nativeFps > 1000)
                    throw new InvalidDataException("Unsupported or invalid video dimensions/frame rate.");

                var durationGuid = MediaFoundationNativeMethods.MF_PD_DURATION;
                if (_reader.GetPresentationAttribute(
                        MediaFoundationNativeMethods.MF_SOURCE_READER_ALL_STREAMS,
                        ref durationGuid,
                        out var prop) == 0)
                {
                    _duration = TimeSpan.FromTicks(prop.hVal);
                }

                // CPU-accessible RGB32 output does not establish whether decoding used a GPU.
                _isHardwareAccelerated = false;

                _logger.LogInformation(
                    "Opened video media '{Path}': {Width}x{Height} @ {Fps:F2} FPS, Duration: {Duration}; shared GPU device: {GpuDevice}.",
                    mediaPath, _videoWidth, _videoHeight, _nativeFps, _duration, _deviceManager != null);
            }
            catch (Exception ex)
            {
                ReleaseReader_NoLock();
                if (d3dDevice != IntPtr.Zero)
                {
                    _logger.LogWarning(ex, "GPU media setup failed; reopening with CPU RGB32 output.");
                    Open(mediaPath, heightLimit: heightLimit);
                    return;
                }
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
        EndOfStream = false;

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

        if (hr < 0 || (streamFlags & MediaFoundationNativeMethods.MF_SOURCE_READERF_ERROR) != 0)
        {
            if (sample != null) Marshal.ReleaseComObject(sample);
            throw new InvalidOperationException($"Video decoding failed (HRESULT: 0x{hr:X8}).");
        }

        if ((streamFlags & MediaFoundationNativeMethods.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
        {
            EndOfStream = true;
            if (sample != null) Marshal.ReleaseComObject(sample);
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

        MediaFoundationNativeMethods.IMFMediaBuffer? buffer = null;
        bool locked = false;
        try
        {
            Marshal.ThrowExceptionForHR(sample.GetBufferCount(out uint bufferCount));
            Marshal.ThrowExceptionForHR(bufferCount == 1 ? sample.GetBufferByIndex(0, out buffer) : sample.ConvertToContiguousBuffer(out buffer));
            if (buffer == null)
            {
                Marshal.ReleaseComObject(sample);
                return null;
            }

            // Locking a DXGI buffer would download a full 4K frame to CPU memory only
            // for the presenter to upload it again. Preserve the texture and array index.
            if (buffer is MediaFoundationNativeMethods.IMFDXGIBuffer dxgi)
            {
                var textureId = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
                Marshal.ThrowExceptionForHR(dxgi.GetSubresourceIndex(out uint subresource));
                Marshal.ThrowExceptionForHR(dxgi.GetResource(ref textureId, out var texture));
                Interlocked.Increment(ref _decodedFrameCount);
                Interlocked.Increment(ref _gpuDecodedFrameCount);
                return new DecodedVideoFrame(IntPtr.Zero, _videoWidth, _videoHeight,
                    TimeSpan.FromTicks(timestampHns), new BufferReleaser(sample, buffer, texture, false))
                    { TexturePointer = texture, TextureSubresource = subresource };
            }
            Marshal.ThrowExceptionForHR(buffer.Lock(out IntPtr pData, out _, out uint currentLength));
            locked = true;
            if (_videoWidth <= 0 || _videoHeight <= 0 || currentLength < (long)_videoWidth * _videoHeight * 4)
                throw new InvalidDataException("The decoder returned an incomplete RGB32 frame.");

            Interlocked.Increment(ref _decodedFrameCount);
            return new DecodedVideoFrame(
                pData,
                _videoWidth,
                _videoHeight,
                TimeSpan.FromTicks(timestampHns),
                new BufferReleaser(sample, buffer));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading decoded video sample buffer.");
            if (buffer != null)
            {
                if (locked) buffer.Unlock();
                Marshal.ReleaseComObject(buffer);
            }
            Marshal.ReleaseComObject(sample);
            throw;
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
        _deviceManager?.Dispose();
        _deviceManager = null;
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
        private IntPtr _texture;
        private readonly bool _locked;

        public BufferReleaser(MediaFoundationNativeMethods.IMFSample sample, MediaFoundationNativeMethods.IMFMediaBuffer buffer, IntPtr texture = default, bool locked = true)
        {
            _sample = sample;
            _buffer = buffer;
            _texture = texture;
            _locked = locked;
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
                if (_locked) _buffer?.Unlock();
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
            if (_texture != IntPtr.Zero) { Marshal.Release(_texture); _texture = IntPtr.Zero; }

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
    public IntPtr TexturePointer { get; init; }
    public uint TextureSubresource { get; init; }
    public void Dispose() => Releaser.Dispose();
}
