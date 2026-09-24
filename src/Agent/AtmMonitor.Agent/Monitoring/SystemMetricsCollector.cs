using System.Globalization;
using System.Runtime.InteropServices;
using AtmMonitor.Contracts;

namespace AtmMonitor.Agent.Monitoring;

public interface ISystemMetricsCollector
{
    SystemMetricsDto Collect();
}

/// <summary>Whole-machine CPU/memory/disk without performance-counter dependencies (Windows via Win32, Linux via /proc).</summary>
public sealed partial class SystemMetricsCollector : ISystemMetricsCollector
{
    private (ulong Idle, ulong Total)? _lastCpu;

    public SystemMetricsDto Collect() => new(
        CpuPercent: Math.Round(SampleCpu(), 1),
        MemoryUsedPercent: Math.Round(MemoryUsedPercent(), 1),
        DiskUsedPercent: Math.Round(DiskUsage().UsedPercent, 1),
        DiskFreeBytes: DiskUsage().Free,
        Uptime: TimeSpan.FromMilliseconds(Environment.TickCount64),
        OsDescription: RuntimeInformation.OSDescription,
        MachineName: Environment.MachineName);

    /// <summary>CPU usage since the previous call (first call returns 0).</summary>
    private double SampleCpu()
    {
        var now = ReadCpuTimes();
        if (now is null)
        {
            return 0;
        }

        var prev = _lastCpu;
        _lastCpu = now;
        if (prev is null)
        {
            return 0;
        }

        var totalDelta = now.Value.Total - prev.Value.Total;
        var idleDelta = now.Value.Idle - prev.Value.Idle;
        return totalDelta == 0 ? 0 : Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
    }

    private static (ulong Idle, ulong Total)? ReadCpuTimes()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Kernel time includes idle time.
                return GetSystemTimes(out var idle, out var kernel, out var user) ? (idle, kernel + user) : null;
            }

            if (OperatingSystem.IsLinux())
            {
                var parts = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                    .Select(v => ulong.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                var idleAll = parts[3] + (parts.Length > 4 ? parts[4] : 0);
                return (idleAll, parts.Aggregate(0UL, (a, b) => a + b));
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static double MemoryUsedPercent()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                return GlobalMemoryStatusEx(ref status) ? status.MemoryLoad : 0;
            }

            if (OperatingSystem.IsLinux())
            {
                var info = File.ReadLines("/proc/meminfo")
                    .Select(l => l.Split(':', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0], p => double.Parse(p[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture));
                if (info.TryGetValue("MemTotal", out var total) && info.TryGetValue("MemAvailable", out var available) && total > 0)
                {
                    return 100.0 * (total - available) / total;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
        }

        var gc = GC.GetGCMemoryInfo();
        return gc.TotalAvailableMemoryBytes == 0 ? 0 : 100.0 * gc.MemoryLoadBytes / gc.TotalAvailableMemoryBytes;
    }

    private static (double UsedPercent, long Free) DiskUsage()
    {
        var root = OperatingSystem.IsWindows() ? Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\" : "/";
        try
        {
            var d = new DriveInfo(root);
            return d.TotalSize == 0 ? (0, 0) : (100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize, d.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (0, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);
}
