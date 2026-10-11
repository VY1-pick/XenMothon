using System.Diagnostics;
using System.Runtime.InteropServices;

// Native APIs used only by this disposable benchmark process.
internal static class NativeMetrics
{
    internal static T Method<T>(IntPtr obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));

    internal static object Inspect()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool memoryAvailable = GlobalMemoryStatusEx(ref memory);
        bool powerAvailable = GetSystemPowerStatus(out var power);
        return new {
            logicalProcessors = Environment.ProcessorCount,
            availableRamMiB = memoryAvailable ? (double?)memory.AvailablePhysical / 1048576 : null,
            availableCommitMiB = memoryAvailable ? (double?)memory.AvailablePageFile / 1048576 : null,
            totalRamMiB = memoryAvailable ? (double?)memory.TotalPhysical / 1048576 : null,
            width = GetSystemMetrics(0), height = GetSystemMetrics(1),
            power = !powerAvailable ? "Unknown" : power.Ac == 1 ? "AC" : power.Ac == 0 ? "DC" : "Unknown",
            interactive = Environment.UserInteractive,
            remote = GetSystemMetrics(0x1000) != 0,
            os = Environment.OSVersion.VersionString,
            baselineCpuPercent = BaselineCpu()
        };
    }

    private static double? BaselineCpu()
    {
        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1)) return null;
        Thread.Sleep(1000);
        if (!GetSystemTimes(out var idle2, out var kernel2, out var user2)) return null;
        ulong total = (kernel2.Value - kernel1.Value) + (user2.Value - user1.Value);
        return total == 0 ? null : 100.0 * (total - Math.Min(total, idle2.Value - idle1.Value)) / total;
    }

    internal static object? MemoryBudget(IntPtr device)
    {
        IntPtr dxgi = IntPtr.Zero, adapter = IntPtr.Zero, adapter3 = IntPtr.Zero;
        try
        {
            var id = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in id, out dxgi));
            Marshal.ThrowExceptionForHR(Method<GetAdapter>(dxgi, 7)(dxgi, out adapter));
            string? name = Method<GetDescription>(adapter, 10)(adapter, out var desc) == 0 ? desc.Description : null;
            id = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");
            if (Marshal.QueryInterface(adapter, in id, out adapter3) < 0) return new { adapter = name, local = (object?)null, nonLocal = (object?)null };
            object? Read(uint segment)
            {
                if (Method<QueryMemory>(adapter3, 14)(adapter3, 0, segment, out var info) < 0) return null;
                return new { budgetMiB = info.Budget / 1048576.0, usageMiB = info.CurrentUsage / 1048576.0 };
            }
            return new { adapter = name, local = Read(0), nonLocal = Read(1) };
        }
        catch { return null; }
        finally { foreach (var ptr in new[] { adapter3, adapter, dxgi }) if (ptr != IntPtr.Zero) Marshal.Release(ptr); }
    }

    internal sealed class GpuClock : IDisposable
    {
        private IntPtr _context;
        private readonly IntPtr _device;
        private readonly Queue<(IntPtr Disjoint, IntPtr Start, IntPtr End)> _pending = new();
        private (IntPtr Disjoint, IntPtr Start, IntPtr End)? _active;
        internal List<double> Samples { get; } = [];
        internal GpuClock(IntPtr device)
        {
            _device = device;
            Method<GetContext>(device, 40)(device, out _context);
        }
        private IntPtr Create(uint type)
        {
            var desc = new QueryDesc { Type = type };
            Marshal.ThrowExceptionForHR(Method<CreateQuery>(_device, 24)(_device, ref desc, out var query));
            return query;
        }
        internal void Begin()
        {
            ReadReady();
            if (_pending.Count >= 8) return;
            IntPtr disjoint = IntPtr.Zero, start = IntPtr.Zero, end = IntPtr.Zero;
            try
            {
                disjoint = Create(3); start = Create(2); end = Create(2);
                _active = (disjoint, start, end);
                Method<QueryAction>(_context, 27)(_context, disjoint);
                Method<QueryAction>(_context, 28)(_context, start);
            }
            catch
            {
                foreach (var p in new[] { disjoint, start, end }) if (p != IntPtr.Zero) Marshal.Release(p);
                throw;
            }
        }
        internal void End()
        {
            if (_active is not { } q) return;
            Method<QueryAction>(_context, 28)(_context, q.End);
            Method<QueryAction>(_context, 28)(_context, q.Disjoint);
            _pending.Enqueue(q); _active = null;
        }
        internal void ReadReady()
        {
            while (_pending.TryPeek(out var q))
            {
                int hr = Method<GetDisjoint>(_context, 29)(_context, q.Disjoint, out var timing, 16, 1);
                Marshal.ThrowExceptionForHR(hr);
                if (hr != 0) break; // DONOTFLUSH: never wait/spin in the measured playback loop.
                if (timing.Disjoint == 0 && timing.Frequency > 0 &&
                    Method<GetTimestamp>(_context, 29)(_context, q.Start, out var start, 8, 1) == 0 &&
                    Method<GetTimestamp>(_context, 29)(_context, q.End, out var end, 8, 1) == 0 && end >= start)
                    Samples.Add((end - start) * 1000.0 / timing.Frequency);
                _pending.Dequeue();
                foreach (var p in new[] { q.Disjoint, q.Start, q.End }) Marshal.Release(p);
            }
        }
        internal void Finish()
        {
            // Only after measurement; no forced per-frame synchronization overhead.
            Method<Flush>(_context, 111)(_context);
            var watch = Stopwatch.StartNew();
            while (_pending.Count > 0 && watch.ElapsedMilliseconds < 250) { ReadReady(); Thread.Sleep(1); }
        }
        public void Dispose()
        {
            if (_active is { } active) foreach (var p in new[] { active.Disjoint, active.Start, active.End }) Marshal.Release(p);
            while (_pending.TryDequeue(out var q)) foreach (var p in new[] { q.Disjoint, q.Start, q.End }) Marshal.Release(p);
            if (_context != IntPtr.Zero) Marshal.Release(_context);
            _context = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte Ac, BatteryFlag, BatteryPercent, Saver; public uint Life, FullLife; }
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct VideoMemory { public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDescription {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Vendor, Device, Subsystem, Revision; public UIntPtr DedicatedVideo, DedicatedSystem, Shared; public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueryDesc { public uint Type, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct DisjointData { public ulong Frequency; public int Disjoint; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetContext(IntPtr self, out IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateQuery(IntPtr self, ref QueryDesc desc, out IntPtr query);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void QueryAction(IntPtr self, IntPtr query);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Flush(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDisjoint(IntPtr self, IntPtr query, out DisjointData data, uint size, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetTimestamp(IntPtr self, IntPtr query, out ulong data, uint size, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAdapter(IntPtr self, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr self, out AdapterDescription desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryMemory(IntPtr self, uint node, uint segment, out VideoMemory data);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus power);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] internal static extern IntPtr GetDesktopWindow();
    [DllImport("gdi32.dll")] internal static extern uint GetPixel(IntPtr dc, int x, int y);
}
