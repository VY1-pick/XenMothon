using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace XenMotion.UI.Rendering.Direct3D;

/// <summary>A DXGI composition surface with a GDI-compatible back buffer.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class DesktopSwapChain : IDisposable
{
    private IntPtr _device, _context, _chain, _surface;
    private GpuFramePipeline? _frames;
    private IntPtr _multithread;
    public IntPtr Device => _device;
    public bool IsHardwareDevice { get; private set; } = true;
    public void SetFiltering(bool enabled) => _frames!.HighQualityFiltering = enabled;
    internal int TextureMipLevels => _frames!.TextureMipLevels;
    public DesktopSwapChain(IntPtr hwnd, int width, int height)
    {
        var desc = new D3D11NativeMethods.DXGI_SWAP_CHAIN_DESC {
            BufferDesc = new() { Width = (uint)width, Height = (uint)height, Format = 87 },
            SampleDesc = new() { Count = 1 }, BufferUsage = 0x20, BufferCount = 1,
            OutputWindow = hwnd, Windowed = true, SwapEffect = 0, Flags = 4 };
        try
        {
            int hr = D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1, IntPtr.Zero, 0x820,
                IntPtr.Zero, 0, 7, ref desc, out _chain, out _device, out _, out _context);
            if (hr < 0)
            {
                Dispose();
                hr = D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1, IntPtr.Zero, 0x20,
                    IntPtr.Zero, 0, 7, ref desc, out _chain, out _device, out _, out _context);
            }
            if (hr < 0)
            {
                Dispose();
                hr = D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 5, IntPtr.Zero, 0x20,
                    IntPtr.Zero, 0, 7, ref desc, out _chain, out _device, out _, out _context);
                IsHardwareDevice = false;
            }
            Marshal.ThrowExceptionForHR(hr);
            // Media Foundation workers and the presenter share the immediate context.
            var multithreadId = new Guid("9B7E4E00-342C-4106-A19F-4F2704F689F0");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(_context, ref multithreadId, out _multithread));
            Method<ProtectMultithread>(_multithread, 5)(_multithread, true);
            _frames = new GpuFramePipeline(_device, _context);
            AcquireSurface();
        }
        catch { Dispose(); throw; }
    }
    private static T Method<T>(IntPtr obj, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size));
    private void AcquireSurface()
    {
        var id = new Guid("4AE63092-6327-4C1B-80AE-BFE12EA32B86");
        Marshal.ThrowExceptionForHR(Method<GetBuffer>(_chain, 9)(_chain, 0, ref id, out _surface));
        id = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        Marshal.ThrowExceptionForHR(Method<GetBuffer>(_chain, 9)(_chain, 0, ref id, out var buffer));
        try { _frames!.SetBackBuffer(buffer); }
        finally { Marshal.Release(buffer); }
    }
    public void Render(IntPtr pixels, int width, int height, XenMotion.Core.Models.ViewportRectangle viewport)
    {
        Method<ContextLock>(_multithread, 3)(_multithread);
        try { _frames!.Render(pixels, width, height, viewport); }
        finally { Method<ContextLock>(_multithread, 4)(_multithread); }
    }
    public void RenderTexture(IntPtr texture, uint subresource, int width, int height, XenMotion.Core.Models.ViewportRectangle viewport)
    {
        Method<ContextLock>(_multithread, 3)(_multithread);
        try { _frames!.RenderTexture(texture, subresource, width, height, viewport); }
        finally { Method<ContextLock>(_multithread, 4)(_multithread); }
    }
    public void PresentGpu() => Marshal.ThrowExceptionForHR(Method<PresentMethod>(_chain, 8)(_chain, 0, 0));
    public void SetVisualAdjustments(float brightness, float contrast, float saturation, float sharpness = 0)
    {
        Method<ContextLock>(_multithread, 3)(_multithread);
        try { _frames!.SetVisualAdjustments(brightness, contrast, saturation, sharpness); }
        finally { Method<ContextLock>(_multithread, 4)(_multithread); }
    }
    // Read the GPU back buffer directly for native pixel verification. No GDI clipping/DWM state.
    internal void CompleteCalibrationFrame(CancellationToken ct)
    {
        IntPtr query = IntPtr.Zero;
        var description = new QueryDesc(); // D3D11_QUERY_EVENT: wait for actual GPU completion.
        Method<ContextLock>(_multithread, 3)(_multithread);
        try
        {
            Marshal.ThrowExceptionForHR(Method<CreateQuery>(_device, 24)(_device, ref description, out query));
            Method<EndQuery>(_context, 28)(_context, query);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int hr = Method<QueryData>(_context, 29)(_context, query, out _, 4, 0);
                Marshal.ThrowExceptionForHR(hr);
                if (hr == 0) break;
                if (watch.ElapsedMilliseconds > 200) throw new TimeoutException("GPU calibration exceeded 200ms per frame.");
                Thread.Sleep(1); // Only during opt-in loading calibration; never in playback.
            }
        }
        finally
        {
            if (query != IntPtr.Zero) Marshal.Release(query);
            Method<ContextLock>(_multithread, 4)(_multithread);
        }
    }
    internal uint ReadPixel(int x, int y, int width, int height)
    {
        var id = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        Marshal.ThrowExceptionForHR(Method<GetBuffer>(_chain, 9)(_chain, 0, ref id, out var source));
        IntPtr staging = IntPtr.Zero;
        try
        {
            var desc = new TextureDesc { Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = 87, SampleCount = 1, Usage = 3, CpuAccess = 0x20000 };
            Marshal.ThrowExceptionForHR(Method<CreateTexture>(_device, 5)(_device, ref desc, IntPtr.Zero, out staging));
            Method<Copy>(_context, 47)(_context, staging, source);
            Marshal.ThrowExceptionForHR(Method<Map>(_context, 14)(_context, staging, 0, 1, 0, out var mapped));
            try
            {
                uint bgra = (uint)Marshal.ReadInt32(mapped.Data, checked(y * (int)mapped.RowPitch + x * 4));
                return ((bgra & 0xff) << 16) | (bgra & 0xff00) | ((bgra >> 16) & 0xff);
            }
            finally { Method<Unmap>(_context, 15)(_context, staging, 0); }
        }
        finally { if (staging != IntPtr.Zero) Marshal.Release(staging); Marshal.Release(source); }
    }
    public IntPtr GetDc(bool discard = true)
    {
        Marshal.ThrowExceptionForHR(Method<GetDcMethod>(_surface, 11)(_surface, discard, out var dc));
        return dc;
    }
    public void Present()
    {
        Marshal.ThrowExceptionForHR(Method<ReleaseDc>(_surface, 12)(_surface, IntPtr.Zero));
        Marshal.ThrowExceptionForHR(Method<PresentMethod>(_chain, 8)(_chain, 0, 0));
    }
    public void Resize(int width, int height)
    {
        _frames!.ReleaseBackBuffer();
        if (_surface != IntPtr.Zero) { Marshal.Release(_surface); _surface = IntPtr.Zero; }
        Marshal.ThrowExceptionForHR(Method<ResizeMethod>(_chain, 13)(_chain, 1, (uint)width, (uint)height, 87, 4));
        AcquireSurface();
    }
    public void Dispose()
    {
        _frames?.Dispose(); _frames = null;
        foreach (var ptr in new[] { _surface, _chain, _multithread, _context, _device }) if (ptr != IntPtr.Zero) Marshal.Release(ptr);
        _surface = _chain = _multithread = _context = _device = IntPtr.Zero;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBuffer(IntPtr self, uint index, ref Guid id, out IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool ProtectMultithread(IntPtr self, [MarshalAs(UnmanagedType.Bool)] bool enabled);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ContextLock(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDcMethod(IntPtr self, [MarshalAs(UnmanagedType.Bool)] bool discard, out IntPtr dc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReleaseDc(IntPtr self, IntPtr dirtyRect);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PresentMethod(IntPtr self, uint interval, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResizeMethod(IntPtr self, uint count, uint width, uint height, uint format, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct TextureDesc { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccess, Misc; }
    [StructLayout(LayoutKind.Sequential)] private struct Mapped { public IntPtr Data; public uint RowPitch, DepthPitch; }
    [StructLayout(LayoutKind.Sequential)] private struct QueryDesc { public uint Query, MiscFlags; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateQuery(IntPtr self, ref QueryDesc desc, out IntPtr query);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void EndQuery(IntPtr self, IntPtr query);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryData(IntPtr self, IntPtr query, out int completed, uint size, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(IntPtr self, ref TextureDesc desc, IntPtr data, out IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Copy(IntPtr self, IntPtr target, IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Map(IntPtr self, IntPtr resource, uint index, uint type, uint flags, out Mapped mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Unmap(IntPtr self, IntPtr resource, uint index);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDeviceAndSwapChain(IntPtr adapter, int driver, IntPtr software, uint flags,
        IntPtr levels, uint count, uint sdk, ref D3D11NativeMethods.DXGI_SWAP_CHAIN_DESC desc,
        out IntPtr chain, out IntPtr device, out int level, out IntPtr context);
}
