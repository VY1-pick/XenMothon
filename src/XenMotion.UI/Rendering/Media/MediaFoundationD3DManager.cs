using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace XenMotion.UI.Rendering.Media;

/// <summary>Gives the Source Reader the same device used to present its GPU frames.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class MediaFoundationD3DManager : IDisposable
{
    public object ComObject { get; }
    public MediaFoundationD3DManager(IntPtr device)
    {
        Marshal.ThrowExceptionForHR(MFCreateDXGIDeviceManager(out uint token, out var manager));
        try
        {
            var reset = Marshal.GetDelegateForFunctionPointer<ResetDevice>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(manager), 7 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(reset(manager, device, token));
            ComObject = Marshal.GetObjectForIUnknown(manager);
        }
        finally { Marshal.Release(manager); }
    }
    public void Dispose() => Marshal.ReleaseComObject(ComObject);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResetDevice(IntPtr self, IntPtr device, uint token);
    [DllImport("mfplat.dll")] private static extern int MFCreateDXGIDeviceManager(out uint token, out IntPtr manager);
}
