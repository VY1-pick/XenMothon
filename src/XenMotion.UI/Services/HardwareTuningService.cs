using System.Runtime.InteropServices;
using XenMotion.Core.Models;

namespace XenMotion.UI.Services;

public sealed class HardwareTuningService
{
    private readonly Lazy<HardwareSnapshot> _hardware = new(Detect);
    public HardwareSnapshot Hardware => _hardware.Value;
    public QualityPreset RecommendedPreset => QualityPresets.Recommend(Hardware);
    public string Summary => $"{Hardware.MemoryMb / 1024.0:0.#} GB RAM · {Hardware.LogicalProcessors} CPU threads · {Hardware.GraphicsName} · {Hardware.DedicatedGpuMb / 1024.0:0.#} GB dedicated graphics memory";

    private static HardwareSnapshot Detect()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        int memoryMb = GlobalMemoryStatusEx(ref memory) ? (int)(memory.TotalPhysical / 1024 / 1024) : 4096;
        int gpuMb = 0;
        string graphics = "Graphics information unavailable";
        IntPtr factory = IntPtr.Zero;
        try
        {
            var id = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387");
            Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref id, out factory));
            var enumerate = Method<EnumAdapter>(factory, 12);
            for (uint index = 0; index < 16 && enumerate(factory, index, out var adapter) == 0; index++)
            {
                try
                {
                    if (Method<GetDescription>(adapter, 10)(adapter, out var description) != 0 || (description.Flags & 2) != 0) continue;
                    int dedicated = (int)(description.DedicatedVideoMemory.ToUInt64() / 1024 / 1024);
                    gpuMb = dedicated; graphics = description.Description;
                    break; // Match the default D3D adapter, rather than an unused secondary GPU.
                }
                finally { Marshal.Release(adapter); }
            }
        }
        catch { /* Capability detection must never prevent startup. */ }
        finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
        return new(memoryMb, Environment.ProcessorCount, gpuMb,
            (long)Math.Max(1, GetSystemMetrics(78)) * Math.Max(1, GetSystemMetrics(79)), graphics);
    }
    private static T Method<T>(IntPtr obj, int index) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size));
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapter(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr self, out AdapterDescription description);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
}
