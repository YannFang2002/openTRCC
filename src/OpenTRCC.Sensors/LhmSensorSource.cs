using System.Globalization;
using LibreHardwareMonitor.Hardware;
using OpenTRCC.Core.Data;

namespace OpenTRCC.Sensors;

/// <summary>
/// LibreHardwareMonitor bridge. Publishes every sensor LHM exposes (per core, per
/// fan, per rail, …) under a stable <c>lhm.&lt;kind&gt;.&lt;index&gt;.&lt;type&gt;.&lt;slug&gt;</c> id,
/// plus the canonical ids the themes bind to. Admin is needed for most CPU/board
/// sensors; without it the source still reports GPU values instead of failing.
/// </summary>
public sealed class LhmSensorSource : ISensorSource, IDisposable
{
    private static readonly SensorDescriptor[] Canonical =
    {
        new("cpu.temp.package", "CPU 封装温度", "°C", "lhm", "CPU"),
        new("cpu.load.total", "CPU 总占用", "%", "lhm", "CPU"),
        new("cpu.power.package", "CPU 封装功耗", "W", "lhm", "CPU"),
        new("gpu.temp.core", "GPU 核心温度", "°C", "lhm", "GPU"),
        new("gpu.temp.hotspot", "GPU 热点温度", "°C", "lhm", "GPU"),
        new("gpu.load.core", "GPU 核心占用", "%", "lhm", "GPU"),
        new("gpu.power.package", "GPU 功耗", "W", "lhm", "GPU"),
        new("gpu.mem.used", "显存已用", "GB", "lhm", "GPU"),
        new("gpu.fan.rpm", "GPU 风扇", "RPM", "lhm", "GPU"),
    };

    private Computer? _computer;
    private bool _attempted;
    private bool _available = true;
    private string? _reason;

    public string Id => "lhm";

    public string DisplayName => "LibreHardwareMonitor";

    public bool IsAvailable => _available;

    public string? UnavailableReason => _reason;

    public IReadOnlyList<string> DeclaredSensors { get; } =
        Canonical.Select(d => d.Id).ToArray();

    /// <summary>Every sensor LHM exposes; rebuilt on demand as hardware comes online.</summary>
    public IReadOnlyList<SensorDescriptor> Describe()
    {
        if (!_attempted)
        {
            TryOpen();
        }

        if (_computer is null)
        {
            return Canonical;
        }

        var list = new List<SensorDescriptor>(Canonical);
        int hardwareIndex = 0;

        foreach (IHardware hardware in _computer.Hardware)
        {
            if (IsNoisyHardware(hardware.HardwareType))
            {
                continue;
            }

            hardware.Update();
            AddDescriptors(hardware, hardwareIndex, list);

            int subIndex = 0;
            foreach (IHardware sub in hardware.SubHardware)
            {
                sub.Update();
                AddDescriptors(sub, hardwareIndex * 100 + subIndex, list);
                subIndex++;
            }

            hardwareIndex++;
        }

        return list;
    }

    public void Poll(SensorSnapshotBuilder builder)
    {
        if (!_attempted)
        {
            TryOpen();
        }

        if (!_available || _computer is null)
        {
            foreach (string id in DeclaredSensors)
            {
                builder.MarkUnavailable(id);
            }

            return;
        }

        bool anyTemperature = false;
        int hardwareIndex = 0;

        foreach (IHardware hardware in _computer.Hardware)
        {
            // Network adapters flood the list with per-interface counters; the
            // system source already publishes one upload/download pair.
            if (IsNoisyHardware(hardware.HardwareType))
            {
                continue;
            }

            hardware.Update();
            Publish(builder, hardware, hardwareIndex, ref anyTemperature);

            int subIndex = 0;
            foreach (IHardware sub in hardware.SubHardware)
            {
                sub.Update();
                Publish(builder, sub, hardwareIndex * 100 + subIndex, ref anyTemperature);
                subIndex++;
            }

            hardwareIndex++;
        }

        if (!anyTemperature)
        {
            // The first poll can land before LHM has enumerated the GPU; report
            // the ids as missing for this snapshot but keep the source open.
            foreach (string id in DeclaredSensors)
            {
                builder.MarkUnavailable(id);
            }
        }
    }

    public void Dispose()
    {
        _computer?.Close();
        _computer = null;
    }

    private void Publish(
        SensorSnapshotBuilder builder, IHardware hardware, int hardwareIndex, ref bool anyTemperature)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.Value is not float value || !float.IsFinite(value))
            {
                continue;
            }

            // LHM reports 0 for sensors it cannot read without ring0 access.
            if (sensor.SensorType is SensorType.Temperature or SensorType.Power && value <= 0)
            {
                continue;
            }

            if (sensor.SensorType == SensorType.Temperature)
            {
                anyTemperature = true;
            }

            // LibreHardwareMonitor 0.9.7 reports "Data" in bytes (older builds used
            // GB for Data and MB for SmallData); normalise to GB once here.
            double normalized = sensor.SensorType == SensorType.Data
                ? value / 1024d / 1024d / 1024d
                : value;

            string id = IdFor(hardware.HardwareType, hardwareIndex, sensor);
            builder.Add(id, Math.Round(normalized, 1), UnitFor(sensor.SensorType));

            string? canonical = CanonicalId(hardware.HardwareType, sensor);
            if (canonical is not null)
            {
                double canonicalValue = canonical == "gpu.mem.used"
                    ? Math.Round(normalized, 1)
                    : Math.Round(value, 1);
                builder.Add(canonical, canonicalValue, UnitFor(sensor.SensorType));
            }
        }
    }

    private void AddDescriptors(IHardware hardware, int hardwareIndex, List<SensorDescriptor> list)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            string id = IdFor(hardware.HardwareType, hardwareIndex, sensor);
            list.Add(new SensorDescriptor(
                id,
                $"{hardware.Name} · {sensor.Name}",
                UnitFor(sensor.SensorType),
                Id,
                CategoryFor(hardware.HardwareType)));
        }
    }

    private static string IdFor(HardwareType type, int hardwareIndex, ISensor sensor)
        => $"lhm.{Kind(type)}.{hardwareIndex}.{sensor.SensorType.ToString().ToLowerInvariant()}.{Slug(sensor.Name)}";

    private static string Slug(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        int length = 0;

        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
            else if (length > 0 && buffer[length - 1] != '_')
            {
                buffer[length++] = '_';
            }
        }

        return new string(buffer[..length]).TrimEnd('_');
    }

    private static bool IsNoisyHardware(HardwareType type)
        => type is HardwareType.Network;

    private static string Kind(HardwareType type) => type switch
    {
        HardwareType.Cpu => "cpu",
        HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel => "gpu",
        HardwareType.Memory => "memory",
        HardwareType.Motherboard => "board",
        HardwareType.SuperIO => "superio",
        HardwareType.Storage => "storage",
        HardwareType.Network => "network",
        HardwareType.Cooler => "cooler",
        HardwareType.EmbeddedController => "ec",
        _ => "other",
    };

    private static string CategoryFor(HardwareType type) => type switch
    {
        HardwareType.Cpu => "CPU",
        HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel => "GPU",
        HardwareType.Memory => "内存",
        HardwareType.Motherboard or HardwareType.SuperIO => "主板",
        HardwareType.Storage => "存储",
        HardwareType.Network => "网络",
        _ => "其它",
    };

    private static string UnitFor(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Power => "W",
        SensorType.Fan => "RPM",
        SensorType.Clock => "MHz",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Data => "GB",
        SensorType.Throughput => "MB/s",
        _ => string.Empty,
    };

    /// <summary>Maps the panel's canonical ids onto LHM's hardware/sensor naming.</summary>
    private static string? CanonicalId(HardwareType hardware, ISensor sensor)
    {
        string lower = sensor.Name.ToLowerInvariant();
        bool cpu = hardware == HardwareType.Cpu;
        bool gpu = hardware is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel;

        if (cpu && sensor.SensorType == SensorType.Temperature && (lower.Contains("package") || lower.Contains("tctl")))
        {
            return "cpu.temp.package";
        }

        if (cpu && sensor.SensorType == SensorType.Power && lower.Contains("package"))
        {
            return "cpu.power.package";
        }

        if (cpu && sensor.SensorType == SensorType.Load && lower.Contains("total"))
        {
            return "cpu.load.total";
        }

        if (gpu && sensor.SensorType == SensorType.Temperature && lower.Contains("hot"))
        {
            return "gpu.temp.hotspot";
        }

        if (gpu && sensor.SensorType == SensorType.Temperature && (lower.Contains("core") || lower.Contains("edge")))
        {
            return "gpu.temp.core";
        }

        if (gpu && sensor.SensorType == SensorType.Load && lower.Contains("core"))
        {
            return "gpu.load.core";
        }

        if (gpu && sensor.SensorType == SensorType.Power && lower.Contains("package"))
        {
            return "gpu.power.package";
        }

        if (gpu && sensor.SensorType == SensorType.Data && lower.Contains("memory used"))
        {
            return "gpu.mem.used";
        }

        if (gpu && sensor.SensorType == SensorType.Fan)
        {
            return "gpu.fan.rpm";
        }

        return null;
    }

    /// <summary>
    /// LibreHardwareMonitor 0.9.7 reads CPU temperatures through PawnIO; the
    /// service is Manual, so make sure it is running before opening sensors.
    /// Needs admin — silently does nothing otherwise.
    /// </summary>
    private static void TryStartPawnIo()
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("sc.exe", "start PawnIO")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(start);
            process?.WaitForExit(4000);
        }
        catch (Exception)
        {
            // Not elevated or PawnIO not installed; GPU sensors still work.
        }
    }

    private void TryOpen()
    {
        _attempted = true;

        // LHM's first enumeration occasionally throws (GPU busy, driver race);
        // one retry is cheap and turns a transient failure into a working source.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMotherboardEnabled = true,
                    IsMemoryEnabled = true,
                    IsStorageEnabled = true,
                    IsNetworkEnabled = true,
                    IsControllerEnabled = true,
                };
                TryStartPawnIo();
                _computer.Open();
                _available = true;
                _reason = null;
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _computer?.Close();
                _computer = null;
                _reason = $"LHM 初始化失败：{ex.Message}";
                Thread.Sleep(150);
            }
        }

        _available = false;
    }
}
