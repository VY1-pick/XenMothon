using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace XenMotion.UI.Rendering.Direct3D;

[SupportedOSPlatform("windows10.0.19041.0")]
internal static class D3D11NativeMethods
{
    public const int D3D_DRIVER_TYPE_HARDWARE = 1;
    public const int D3D_DRIVER_TYPE_WARP = 5;

    public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x00000020;
    public const uint D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x00000800;
    public const uint D3D11_SDK_VERSION = 7;

    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const uint DXGI_FORMAT_R8G8B8A8_UNORM = 28;

    public const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x00000020;
    public const uint DXGI_SWAP_EFFECT_DISCARD = 0;
    public const uint DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL = 3;
    public const uint DXGI_SWAP_EFFECT_FLIP_DISCARD = 4;

    public const int S_OK = 0;
    public const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    public const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);

    public const uint DIB_RGB_COLORS = 0;
    public const uint SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_MODE_DESC
    {
        public uint Width;
        public uint Height;
        public DXGI_RATIONAL RefreshRate;
        public uint Format;
        public uint ScanlineOrdering;
        public uint Scaling;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SWAP_CHAIN_DESC
    {
        public DXGI_MODE_DESC BufferDesc;
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint BufferUsage;
        public uint BufferCount;
        public IntPtr OutputWindow;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Windowed;
        public uint SwapEffect;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("d3d11.dll", SetLastError = true)]
    public static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr software,
        uint flags,
        int[]? pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        out int pFeatureLevel,
        out IntPtr ppImmediateContext);

    [DllImport("dxgi.dll", SetLastError = true)]
    public static extern int CreateDXGIFactory1([In] ref Guid riid, out IntPtr ppFactory);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern int StretchDIBits(
        IntPtr hdc,
        int xDest,
        int yDest,
        int DestWidth,
        int DestHeight,
        int xSrc,
        int ySrc,
        int SrcWidth,
        int SrcHeight,
        IntPtr lpBits,
        [In] ref BITMAPINFO lpBitsInfo,
        uint iUsage,
        uint rop);
}
