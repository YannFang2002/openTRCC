using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace OpenTRCC.Sensors;

/// <summary>
/// Always-available baseline: CPU load, memory, network throughput, uptime and
/// the foreground application. No admin rights and no native dependencies.
/// </summary>
public sealed class SystemSensorSource : ISensorSource, IDisposable
{
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private long _lastReceived;
    private long _lastSent;
    private long _lastTimestamp;
    private bool _primed;

    public string Id => "system";

    public string DisplayName => "Windows 系统计数器";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public IReadOnlyList<string> DeclaredSensors { get; } = new[]
    {
        "cpu.load.total", "mem.load", "mem.used.gb", "mem.total.gb",
        "net.down", "net.up", "uptime.hours", "app.foreground",
    };

    public IReadOnlyList<Core.Data.SensorDescriptor> Describe() => new[]
    {
        new Core.Data.SensorDescriptor("cpu.load.total", "CPU 总占用", "%", Id, "CPU"),
        new Core.Data.SensorDescriptor("mem.load", "内存占用", "%", Id, "内存"),
        new Core.Data.SensorDescriptor("mem.used.gb", "内存已用", "GB", Id, "内存"),
        new Core.Data.SensorDescriptor("mem.total.gb", "内存总量", "GB", Id, "内存"),
        new Core.Data.SensorDescriptor("net.down", "网络下行", "MB/s", Id, "网络"),
        new Core.Data.SensorDescriptor("net.up", "网络上行", "MB/s", Id, "网络"),
        new Core.Data.SensorDescriptor("uptime.hours", "开机时长", "h", Id, "系统"),
        new Core.Data.SensorDescriptor("app.foreground", "前台应用", string.Empty, Id, "系统"),
    };

    public void Poll(SensorSnapshotBuilder builder)
    {
        builder.AddNumber("cpu.load.total", ReadCpuUsage(), "%");

        if (TryReadMemory(out double load, out double usedGb, out double totalGb))
        {
            builder.Add("mem.load", load, "%");
            builder.Add("mem.used.gb", usedGb, "GB");
            builder.Add("mem.total.gb", totalGb, "GB");
        }
        else
        {
            builder.MarkUnavailable("mem.load");
        }

        if (TryReadNetwork(out double down, out double up))
        {
            builder.Add("net.down", down, "MB/s");
            builder.Add("net.up", up, "MB/s");
        }

        builder.Add("uptime.hours", Math.Round(Environment.TickCount64 / 3_600_000.0, 1), "h");

        string? foreground = ForegroundApplication();
        if (foreground is not null)
        {
            builder.Add("app.foreground", foreground);
        }
    }

    public void Dispose()
    {
    }

    /// <summary>CPU usage from two GetSystemTimes samples; idle is part of kernel.</summary>
    public static double ComputeCpuUsage(ulong idleDelta, ulong kernelDelta, ulong userDelta)
    {
        ulong total = kernelDelta + userDelta;
        if (total == 0)
        {
            return 0;
        }

        double busy = 1.0 - (double)idleDelta / total;
        return Math.Clamp(busy * 100.0, 0, 100);
    }

    private double ReadCpuUsage()
    {
        if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            return double.NaN;
        }

        ulong idleTicks = idle.ToUInt64();
        ulong kernelTicks = kernel.ToUInt64();
        ulong userTicks = user.ToUInt64();

        if (!_primed)
        {
            _primed = true;
            _lastIdle = idleTicks;
            _lastKernel = kernelTicks;
            _lastUser = userTicks;
            return double.NaN;
        }

        ulong idleDelta = idleTicks - _lastIdle;
        ulong kernelDelta = kernelTicks - _lastKernel;
        ulong userDelta = userTicks - _lastUser;

        _lastIdle = idleTicks;
        _lastKernel = kernelTicks;
        _lastUser = userTicks;

        return ComputeCpuUsage(idleDelta, kernelDelta, userDelta);
    }

    private bool TryReadMemory(out double load, out double usedGb, out double totalGb)
    {
        load = 0;
        usedGb = 0;
        totalGb = 0;

        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return false;
        }

        load = status.MemoryLoad;
        totalGb = status.TotalPhys / 1024.0 / 1024.0 / 1024.0;
        usedGb = (status.TotalPhys - status.AvailablePhys) / 1024.0 / 1024.0 / 1024.0;
        return true;
    }

    private bool TryReadNetwork(out double downMb, out double upMb)
    {
        downMb = 0;
        upMb = 0;

        long received = 0;
        long sent = 0;

        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            if (IsLikelyVirtual(nic))
            {
                continue;
            }

            try
            {
                IPv4InterfaceStatistics stats = nic.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
            }
        }

        long now = Environment.TickCount64;
        if (_lastTimestamp == 0)
        {
            _lastTimestamp = now;
            _lastReceived = received;
            _lastSent = sent;
            return false;
        }

        double seconds = (now - _lastTimestamp) / 1000.0;
        if (seconds <= 0)
        {
            return false;
        }

        downMb = (received - _lastReceived) / seconds / 1024.0 / 1024.0;
        upMb = (sent - _lastSent) / seconds / 1024.0 / 1024.0;

        _lastTimestamp = now;
        _lastReceived = received;
        _lastSent = sent;
        return true;
    }

    private static bool IsLikelyVirtual(NetworkInterface nic)
    {
        string description = nic.Description;
        string name = nic.Name;
        string[] markers = { "Virtual", "VMware", "Hyper-V", "VPN", "TAP", "TUN", "Loopback", "Bluetooth", "GameViewer" };

        foreach (string marker in markers)
        {
            if (description.Contains(marker, StringComparison.OrdinalIgnoreCase)
                || name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ForegroundApplication()
    {
        nint window = GetForegroundWindow();
        if (window == nint.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailablePhys;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
