using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using XenMotion.Core.Models;

namespace XenMotion.UI.Rendering.Direct3D;

/// <summary>Reuses a native-resolution texture; filtering and scaling run on D3D11.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class GpuFramePipeline : IDisposable
{
    private readonly IntPtr _device, _context;
    private IntPtr _vertex, _pixel, _sampler, _texture, _view, _target;
    private IntPtr _adjustments, _baseSampler;
    private readonly BindSlots _constantBuffers;
    private readonly IntPtr[] _adjustmentBinding = new IntPtr[1];
    private int _width, _height;
    private uint _format;
    public bool HighQualityFiltering { get; set; } = true;
    private readonly CopyRegion _copy;
    private readonly Update _upload;
    private readonly Bind _targets;
    private readonly BindSlots _resources, _samplers;
    private readonly Shader _setVertex, _setPixel;
    private readonly Mips _mips;
    private readonly Viewports _viewports;
    private readonly Clear _clear;
    private readonly Draw _draw;
    private readonly Topology _topology;
    private readonly IntPtr[] _resourceBinding = new IntPtr[1], _samplerBinding = new IntPtr[1], _targetBinding = new IntPtr[1];
    private static readonly IntPtr[] EmptyBinding = Array.Empty<IntPtr>(), NullBinding = new IntPtr[1];
    private static readonly float[] Black = { 0, 0, 0, 1 };
    private static readonly Lazy<byte[]> VertexCode = new(() => Compile("vs_4_0", "VS"));
    private static readonly Lazy<byte[]> PixelCode = new(() => Compile("ps_4_0", "PS"));

    public GpuFramePipeline(IntPtr device, IntPtr context)
    {
        _device = device; _context = context;
        _upload = Method<Update>(context, 48);
        _copy = Method<CopyRegion>(context, 46);
        _resources = Method<BindSlots>(context, 8); _samplers = Method<BindSlots>(context, 10);
        _targets = Method<Bind>(context, 33);
        _setVertex = Method<Shader>(context, 11); _setPixel = Method<Shader>(context, 9);
        _mips = Method<Mips>(context, 54); _viewports = Method<Viewports>(context, 44);
        _clear = Method<Clear>(context, 50); _draw = Method<Draw>(context, 13);
        _topology = Method<Topology>(context, 24);
        _constantBuffers = Method<BindSlots>(context, 16);
        try
        {
            var vs = VertexCode.Value; var ps = PixelCode.Value;
            Marshal.ThrowExceptionForHR(Method<CreateShader>(device, 12)(device, vs, (nuint)vs.Length, IntPtr.Zero, out _vertex));
            Marshal.ThrowExceptionForHR(Method<CreateShader>(device, 15)(device, ps, (nuint)ps.Length, IntPtr.Zero, out _pixel));
            var sampler = new SamplerDesc { Filter = 0x15, AddressU = 3, AddressV = 3, AddressW = 3, MaxAnisotropy = 1, Comparison = 1, MaxLod = float.MaxValue };
            Marshal.ThrowExceptionForHR(Method<CreateSampler>(device, 23)(device, ref sampler, out _sampler));
            _samplerBinding[0] = _sampler;
            sampler.MaxLod = 0;
            Marshal.ThrowExceptionForHR(Method<CreateSampler>(device, 23)(device, ref sampler, out _baseSampler));
            var buffer = new BufferDesc { ByteWidth = 16, BindFlags = 4 };
            Marshal.ThrowExceptionForHR(Method<CreateBuffer>(device, 3)(device, ref buffer, IntPtr.Zero, out _adjustments));
            _adjustmentBinding[0] = _adjustments;
            SetVisualAdjustments(1, 1, 1);
        }
        catch { Dispose(); throw; }
    }

    public void SetBackBuffer(IntPtr buffer)
    {
        ReleaseBackBuffer();
        Marshal.ThrowExceptionForHR(Method<CreateView>(_device, 9)(_device, buffer, IntPtr.Zero, out _target));
        _targetBinding[0] = _target;
    }

    public void SetVisualAdjustments(float brightness, float contrast, float saturation)
    {
        var values = GCHandle.Alloc(new[] { Math.Clamp(brightness, 0, 2), Math.Clamp(contrast, 0, 2), Math.Clamp(saturation, 0, 2), 0f }, GCHandleType.Pinned);
        try { _upload(_context, _adjustments, 0, IntPtr.Zero, values.AddrOfPinnedObject(), 0, 0); }
        finally { values.Free(); }
    }

    public void ReleaseBackBuffer()
    {
        _targets(_context, 0, EmptyBinding, IntPtr.Zero);
        Release(ref _target);
    }

    public void Render(IntPtr pixels, int width, int height, ViewportRectangle viewport)
    {
        EnsureTexture(width, height, 87);
        _upload(_context, _texture, 0, IntPtr.Zero, pixels, checked((uint)width * 4), 0);
        DrawUploaded(width, height, viewport);
    }

    public void RenderTexture(IntPtr texture, uint subresource, int width, int height, ViewportRectangle viewport)
    {
        Method<GetTextureDesc>(texture, 10)(texture, out var source);
        if (source.Format is not (87 or 88 or 28))
            throw new NotSupportedException($"The RGB decoder returned unexpected DXGI format {source.Format}.");
        EnsureTexture(width, height, source.Format);
        // Copy only the visible pixels: decoder surfaces can include codec-alignment padding.
        var box = new Box { Right = (uint)width, Bottom = (uint)height, Back = 1 };
        _copy(_context, _texture, 0, 0, 0, 0, texture, subresource, ref box);
        DrawUploaded(width, height, viewport);
    }

    private void EnsureTexture(int width, int height, uint format)
    {
        if (_width != width || _height != height || _format != format)
        {
            Release(ref _view); Release(ref _texture);
            var desc = new TextureDesc { Width = (uint)width, Height = (uint)height, MipLevels = 0, ArraySize = 1,
                Format = format, SampleCount = 1, BindFlags = 0x28, MiscFlags = 1 };
            Marshal.ThrowExceptionForHR(Method<CreateTexture>(_device, 5)(_device, ref desc, IntPtr.Zero, out _texture));
            Marshal.ThrowExceptionForHR(Method<CreateView>(_device, 7)(_device, _texture, IntPtr.Zero, out _view));
            _resourceBinding[0] = _view;
            _width = width; _height = height; _format = format;
        }
    }

    private void DrawUploaded(int width, int height, ViewportRectangle viewport)
    {
        // Mip filtering avoids aliasing when high-resolution sources are reduced for the display.
        if (HighQualityFiltering && (viewport.Width < width || viewport.Height < height)) _mips(_context, _view);
        _targets(_context, 1, _targetBinding, IntPtr.Zero);
        _clear(_context, _target, Black);
        var area = new Viewport { X = viewport.X, Y = viewport.Y, Width = viewport.Width, Height = viewport.Height, MaxDepth = 1 };
        _viewports(_context, 1, ref area);
        _topology(_context, 4); // Triangle list; SV_VertexID supplies vertices.
        _setVertex(_context, _vertex, IntPtr.Zero, 0); _setPixel(_context, _pixel, IntPtr.Zero, 0);
        _resources(_context, 0, 1, _resourceBinding);
        _samplerBinding[0] = HighQualityFiltering && (viewport.Width < width || viewport.Height < height) ? _sampler : _baseSampler;
        _samplers(_context, 0, 1, _samplerBinding);
        _constantBuffers(_context, 0, 1, _adjustmentBinding);
        _draw(_context, 3, 0);
        _resources(_context, 0, 1, NullBinding);
        _targets(_context, 0, EmptyBinding, IntPtr.Zero);
    }

    public void Dispose()
    {
        ReleaseBackBuffer();
        Release(ref _view); Release(ref _texture); Release(ref _sampler); Release(ref _pixel); Release(ref _vertex);
        Release(ref _adjustments);
        Release(ref _baseSampler);
    }
    private static void Release(ref IntPtr value) { if (value != IntPtr.Zero) Marshal.Release(value); value = IntPtr.Zero; }
    private static T Method<T>(IntPtr obj, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size));

    private static byte[] Compile(string profile, string entry)
    {
        const string source = "Texture2D image : register(t0); SamplerState filtering : register(s0); " +
            "cbuffer Adjustments : register(b0) { float brightness; float contrast; float saturation; float padding; }; " +
            "struct Vertex { float4 position : SV_POSITION; float2 uv : TEXCOORD0; }; " +
            "Vertex VS(uint id : SV_VertexID) { Vertex v; v.uv = float2((id << 1) & 2, id & 2); " +
            "v.position = float4(v.uv * float2(2,-2) + float2(-1,1),0,1); return v; } " +
            "float4 PS(Vertex v) : SV_TARGET { float3 rgb = image.Sample(filtering,v.uv).rgb; " +
            "rgb = lerp(dot(rgb,float3(0.2126,0.7152,0.0722)).xxx,rgb,saturation); " +
            "return float4(saturate(((rgb-0.5)*contrast+0.5)*brightness),1); }";
        byte[] bytes = Encoding.ASCII.GetBytes(source);
        IntPtr code = IntPtr.Zero, errors = IntPtr.Zero;
        try
        {
            int hr = D3DCompile(bytes, (nuint)bytes.Length, null, IntPtr.Zero, IntPtr.Zero, entry, profile, 0x8000, 0, out code, out errors);
            if (hr < 0) throw new InvalidOperationException(errors == IntPtr.Zero ? "D3D shader compilation failed." : Marshal.PtrToStringAnsi(Method<BlobPointer>(errors, 3)(errors)));
            var result = new byte[checked((int)Method<BlobSize>(code, 4)(code))];
            Marshal.Copy(Method<BlobPointer>(code, 3)(code), result, 0, result.Length);
            return result;
        }
        finally { Release(ref code); Release(ref errors); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct TextureDesc { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct SamplerDesc { public uint Filter, AddressU, AddressV, AddressW; public float MipLodBias; public uint MaxAnisotropy, Comparison; public float Border0, Border1, Border2, Border3, MinLod, MaxLod; }
    [StructLayout(LayoutKind.Sequential)] private struct Viewport { public float X, Y, Width, Height, MinDepth, MaxDepth; }
    [StructLayout(LayoutKind.Sequential)] private struct Box { public uint Left, Top, Front, Right, Bottom, Back; }
    [StructLayout(LayoutKind.Sequential)] private struct BufferDesc { public uint ByteWidth, Usage, BindFlags, CpuAccessFlags, MiscFlags, StructureByteStride; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateBuffer(IntPtr self, ref BufferDesc desc, IntPtr data, out IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyRegion(IntPtr self, IntPtr destination, uint destinationIndex, uint x, uint y, uint z, IntPtr source, uint sourceIndex, ref Box box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetTextureDesc(IntPtr self, out TextureDesc desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(IntPtr self, ref TextureDesc desc, IntPtr data, out IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateView(IntPtr self, IntPtr resource, IntPtr desc, out IntPtr view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateShader(IntPtr self, byte[] code, nuint size, IntPtr linkage, out IntPtr shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSampler(IntPtr self, ref SamplerDesc desc, out IntPtr sampler);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Update(IntPtr self, IntPtr resource, uint subresource, IntPtr box, IntPtr data, uint pitch, uint depthPitch);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Bind(IntPtr self, uint countOrSlot, IntPtr[] views, IntPtr depthOrCount);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void BindSlots(IntPtr self, uint slot, uint count, IntPtr[] views);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Shader(IntPtr self, IntPtr shader, IntPtr instances, uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Mips(IntPtr self, IntPtr view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Viewports(IntPtr self, uint count, ref Viewport viewport);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Clear(IntPtr self, IntPtr view, float[] color);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Draw(IntPtr self, uint count, uint start);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Topology(IntPtr self, uint topology);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate nuint BlobSize(IntPtr self);
    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)] private static extern int D3DCompile(byte[] data, nuint size, string? name, IntPtr defines, IntPtr include, string entry, string profile, uint flags, uint effectFlags, out IntPtr code, out IntPtr errors);
}
