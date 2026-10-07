using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MF = XenMotion.UI.Rendering.Media.MediaFoundationNativeMethods;

namespace XenMotion.UI.Rendering.Media;

/// <summary>Decodes only the audio stream and queues PCM to the Windows audio device.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WallpaperAudioPlayer(ILogger logger) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _resume = new(false);
    private readonly List<Buffer> _buffers = [];
    private MF.IMFSourceReader? _reader;
    private IntPtr _output;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private bool _mfStarted, _eof, _playing;
    private int _bytesPerSecond;
    private float _volume;
    private long _queuedBytes;
    public bool HasAudio => _reader != null && _output != IntPtr.Zero;
    public long QueuedBytes => Interlocked.Read(ref _queuedBytes);
    private static readonly Guid Pcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid Channels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid SampleRate = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid Bits = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

    public async Task OpenAsync(string path, float volume, float playbackSpeed = 1f)
    {
        await CloseAsync();
        lock (_gate)
        {
            try
            {
                Marshal.ThrowExceptionForHR(MF.MFStartup(MF.MF_VERSION, 0)); _mfStarted = true;
                MF.IMFAttributes? attributes = null;
                try
                {
                    Marshal.ThrowExceptionForHR(MF.MFCreateAttributes(out attributes, 1));
                    Marshal.ThrowExceptionForHR(MF.MFCreateSourceReaderFromURL(path, attributes, out _reader));
                }
                finally { if (attributes != null) Marshal.ReleaseComObject(attributes); }
                int hr = _reader.GetNativeMediaType(MF.MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, out var native);
                if (hr == unchecked((int)0xC00D36B3)) { Release_NoLock(); return; } // No audio track.
                Marshal.ThrowExceptionForHR(hr);
                Marshal.ReleaseComObject(native);
                MF.IMFMediaType? type = null;
                try
                {
                    Marshal.ThrowExceptionForHR(MF.MFCreateMediaType(out type));
                    Marshal.ThrowExceptionForHR(type.SetGUID(MF.MF_MT_MAJOR_TYPE, MF.MFMediaType_Audio));
                    Marshal.ThrowExceptionForHR(type.SetGUID(MF.MF_MT_SUBTYPE, Pcm));
                    Marshal.ThrowExceptionForHR(type.SetUINT32(Bits, 16));

                    Marshal.ThrowExceptionForHR(_reader.SetStreamSelection(MF.MF_SOURCE_READER_ALL_STREAMS, false));
                    Marshal.ThrowExceptionForHR(_reader.SetStreamSelection(MF.MF_SOURCE_READER_FIRST_AUDIO_STREAM, true));
                    Marshal.ThrowExceptionForHR(_reader.SetMediaType(MF.MF_SOURCE_READER_FIRST_AUDIO_STREAM, IntPtr.Zero, type));
                }
                finally { if (type != null) Marshal.ReleaseComObject(type); }
                Marshal.ThrowExceptionForHR(_reader.GetCurrentMediaType(MF.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out var current));
                WaveFormat format;
                try
                {
                    Marshal.ThrowExceptionForHR(current.GetUINT32(Channels, out uint channels));
                    Marshal.ThrowExceptionForHR(current.GetUINT32(SampleRate, out uint rate));
                    Marshal.ThrowExceptionForHR(current.GetUINT32(Bits, out uint bits));
                    format = new() { Tag = 1, Channels = (ushort)channels, Rate = rate, Bits = (ushort)bits,
                        BlockAlign = (ushort)(channels * bits / 8), BytesPerSecond = rate * channels * bits / 8 };
                }
                finally { Marshal.ReleaseComObject(current); }
                format.Rate = checked((uint)Math.Round(format.Rate * Math.Clamp(playbackSpeed, 0.25f, 2f)));
                format.BytesPerSecond = format.Rate * format.BlockAlign;
                Check(waveOutOpen(out _output, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
                _bytesPerSecond = (int)format.BytesPerSecond;
                _eof = false; _playing = false; _queuedBytes = 0;
                SetVolume(volume);
                Check(waveOutPause(_output));
                _cts = new();
                var token = _cts.Token;
                _task = Task.Run(() => PumpAsync(token));
                logger.LogInformation("Audio output ready: {Rate} Hz, {Channels} channels, {Bits}-bit PCM.", format.Rate, format.Channels, format.Bits);
            }
            catch (Exception ex)
            {
                Release_NoLock();
                logger.LogWarning(ex, "Wallpaper audio could not be opened; video playback will continue.");
            }
        }
    }
    public void SetVolume(float volume)
    {
        lock (_gate)
        {
            _volume = Math.Clamp(volume, 0, 1);

        }
    }
    public void Play()
    {
        lock (_gate) { if (!HasAudio) return; _playing = true; Check(waveOutRestart(_output)); _resume.Set(); }
    }
    public void Pause()
    {
        lock (_gate) { _playing = false; _resume.Reset(); if (_output != IntPtr.Zero) Check(waveOutPause(_output)); }
    }
    public void Rewind()
    {
        lock (_gate)
        {
            if (!HasAudio) return;
            Check(waveOutReset(_output)); ClearBuffers_NoLock();
            var format = Guid.Empty; var pos = new MF.PROPVARIANT { vt = 20, hVal = 0 };
            Marshal.ThrowExceptionForHR(_reader!.SetCurrentPosition(ref format, ref pos));
            _eof = false;
            if (!_playing) Check(waveOutPause(_output));
        }
    }
    private async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                _resume.Wait(ct);
                lock (_gate)
                {
                    if (!_playing) continue;
                    for (int i = _buffers.Count - 1; i >= 0; i--)
                        if ((_buffers[i].Header.Flags & 1) != 0) { _buffers[i].Dispose(_output); _buffers.RemoveAt(i); }
                    int pending = _buffers.Sum(b => (int)b.Header.Length);
                    if (!_eof && pending < _bytesPerSecond / 5) ReadAndQueue_NoLock();
                }
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Wallpaper audio playback failed."); Pause(); }
    }
    private void ReadAndQueue_NoLock()
    {
        Marshal.ThrowExceptionForHR(_reader!.ReadSample(MF.MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, out _, out uint flags, out _, out var sample));
        if ((flags & MF.MF_SOURCE_READERF_ENDOFSTREAM) != 0) _eof = true;
        if (sample == null) return;
        MF.IMFMediaBuffer? buffer = null;
        bool locked = false;
        try
        {
            Marshal.ThrowExceptionForHR(sample.ConvertToContiguousBuffer(out buffer));
            Marshal.ThrowExceptionForHR(buffer.Lock(out var data, out _, out uint length)); locked = true;
            if (length == 0) return;
            var pcm = new byte[length]; Marshal.Copy(data, pcm, 0, pcm.Length);
            for (int i = 0; i + 1 < pcm.Length; i += 2)
            {
                short value = (short)(pcm[i] | (pcm[i + 1] << 8));
                short scaled = (short)Math.Round(value * _volume);
                pcm[i] = (byte)scaled; pcm[i + 1] = (byte)(scaled >> 8);
            }
            var queued = new Buffer(pcm);
            try { queued.Queue(_output); _buffers.Add(queued); Interlocked.Add(ref _queuedBytes, pcm.Length); }
            catch { queued.Dispose(_output); throw; }
        }
        finally
        {
            if (buffer != null) { if (locked) buffer.Unlock(); Marshal.ReleaseComObject(buffer); }
            Marshal.ReleaseComObject(sample);
        }
    }
    private void ClearBuffers_NoLock() { foreach (var buffer in _buffers) buffer.Dispose(_output); _buffers.Clear(); }
    private void Release_NoLock()
    {
        if (_output != IntPtr.Zero) { waveOutReset(_output); ClearBuffers_NoLock(); waveOutClose(_output); _output = IntPtr.Zero; }
        if (_reader != null) { Marshal.ReleaseComObject(_reader); _reader = null; }
        if (_mfStarted) { MF.MFShutdown(); _mfStarted = false; }
    }
    private async Task CloseAsync()
    {
        _cts?.Cancel(); _resume.Set();
        if (_task != null) await _task.ConfigureAwait(false);
        lock (_gate) { Release_NoLock(); _resume.Reset(); _playing = false; }
        _cts?.Dispose(); _cts = null; _task = null;
    }
    public async ValueTask DisposeAsync() { await CloseAsync(); _resume.Dispose(); }
    private static void Check(uint result) { if (result != 0) throw new InvalidOperationException($"Windows audio output error {result}."); }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader { public IntPtr Data; public uint Length, Recorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }
    private sealed class Buffer
    {
        private readonly GCHandle _data;
        private readonly IntPtr _header;
        private bool _prepared;
        public WaveHeader Header => Marshal.PtrToStructure<WaveHeader>(_header);
        public Buffer(byte[] data)
        {
            _data = GCHandle.Alloc(data, GCHandleType.Pinned);
            _header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
            Marshal.StructureToPtr(new WaveHeader { Data = _data.AddrOfPinnedObject(), Length = (uint)data.Length }, _header, false);
        }
        public void Queue(IntPtr output)
        {
            Check(waveOutPrepareHeader(output, _header, (uint)Marshal.SizeOf<WaveHeader>())); _prepared = true;
            Check(waveOutWrite(output, _header, (uint)Marshal.SizeOf<WaveHeader>()));
        }
        public void Dispose(IntPtr output)
        {
            if (_prepared) Check(waveOutUnprepareHeader(output, _header, (uint)Marshal.SizeOf<WaveHeader>()));
            Marshal.FreeHGlobal(_header); if (_data.IsAllocated) _data.Free();
        }
    }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr output, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr output, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr output, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr output, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutPause(IntPtr output);
    [DllImport("winmm.dll")] private static extern uint waveOutRestart(IntPtr output);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr output);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr output);
}
