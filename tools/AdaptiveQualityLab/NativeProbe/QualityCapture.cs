using System.Runtime.InteropServices;
using XenMotion.UI.Rendering.Direct3D;

internal static class QualityCapture
{
    internal const int Width = 256, Height = 144;
    // Captures the benchmark's own render surface, never another app or the desktop.
    internal static uint[] Capture(DesktopSwapChain chain, int sourceWidth, int sourceHeight)
    {
        IntPtr source = chain.GetDc(false), target = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            target = CreateCompatibleDC(source);
            var header = new Header { Size = 40, Width = Width, Height = -Height, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(target, ref header, 0, out var pixels, IntPtr.Zero, 0);
            if (target == IntPtr.Zero || bitmap == IntPtr.Zero) throw new InvalidOperationException("Quality readback unavailable.");
            previous = SelectObject(target, bitmap);
            SetStretchBltMode(target, 4); // HALFTONE; identical reference/candidate reconstruction.
            SetBrushOrgEx(target, 0, 0, IntPtr.Zero);
            if (!StretchBlt(target, 0, 0, Width, Height, source, 0, 0, sourceWidth, sourceHeight, 0x00CC0020))
                throw new InvalidOperationException("Quality blit failed.");
            GdiFlush();
            int[] raw = new int[Width * Height]; Marshal.Copy(pixels, raw, 0, raw.Length);
            return raw.Select(value => { uint v = (uint)value; return ((v >> 16) & 255) | (v & 0xff00) | ((v & 255) << 16); }).ToArray();
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(target, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (target != IntPtr.Zero) DeleteDC(target);
            chain.Present();
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int Xppm, Yppm; public uint Used, Important; }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref Header info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr old);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, uint operation);
}
