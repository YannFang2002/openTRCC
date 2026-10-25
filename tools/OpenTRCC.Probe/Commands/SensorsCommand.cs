using System.Diagnostics;
using System.Text;
using OpenTRCC.Core.Data;
using OpenTRCC.Sensors;
using OpenTRCC.Sensors.PresentMon;

namespace OpenTRCC.Probe.Commands;

/// <summary>Samples the real sensors (system counters, LHM, PresentMon) for a few seconds.</summary>
internal static class SensorsCommand
{
    public static int Run(CommandLine line)
    {
        double seconds = line.GetDouble("seconds", 8);
        bool withLhm = line.Has("lhm");
        bool withFps = !line.Has("no-fps");
        int intervalMs = line.GetInt("interval", 1000);

        var sources = new List<ISensorSource> { new SystemSensorSource() };

        if (withLhm)
        {
            sources.Add(new LhmSensorSource());
        }

        PresentMonFpsSource? fps = null;
        if (withFps)
        {
            fps = new PresentMonFpsSource();
            sources.Add(fps);
        }

        using var aggregator = new SensorAggregator(sources);
        var rows = new List<SensorSnapshot>();
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine($"== OpenTRCC sensors ({seconds:F0}s) ==");

        foreach (ISensorSource source in sources)
        {
            Console.WriteLine($"source {source.Id,-10} available={source.IsAvailable}" +
                              (source.IsAvailable ? string.Empty : $" ({source.UnavailableReason})"));
        }

        Console.WriteLine();

        while (stopwatch.Elapsed.TotalSeconds < seconds)
        {
            SensorSnapshot snapshot = aggregator.PollOnce();
            rows.Add(snapshot);
            PrintRow(snapshot, stopwatch.Elapsed);
            Thread.Sleep(intervalMs);
        }

        Evidence evidence = Evidence.Create(line);
        evidence.WriteText("sensors.csv", BuildCsv(rows));
        Console.WriteLine();
        Console.WriteLine($"saved: {evidence.PathFor("sensors.csv")}");

        return ProbeExit.Ok;
    }

    private static void PrintRow(SensorSnapshot snapshot, TimeSpan elapsed)
    {
        static string Num(SensorSnapshot s, string id, string format = "0.0")
        {
            SensorValue value = s.Find(id);
            return value.IsMissing ? "--" : value.Display(format);
        }

        Console.WriteLine(
            $"{elapsed.TotalSeconds,4:F0}s  cpu {Num(snapshot, "cpu.load.total"),5}%  " +
            $"gpu {Num(snapshot, "gpu.temp.core"),4}°C {Num(snapshot, "gpu.load.core"),4}%  " +
            $"mem {Num(snapshot, "mem.load"),5}%  " +
            $"net ↓{Num(snapshot, "net.down"),5} ↑{Num(snapshot, "net.up"),4} MB/s  " +
            $"fps {Num(snapshot, "fps.current", "0"),4} (1% {Num(snapshot, "fps.low1", "0"),4}) " +
            $"[{snapshot.Find("fps.process").Text ?? "--"}]  " +
            $"fg {snapshot.Find("app.foreground").Text ?? "--"}");
    }

    private static string BuildCsv(IReadOnlyList<SensorSnapshot> rows)
    {
        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SensorSnapshot row in rows)
        {
            foreach (string id in row.Values.Keys)
            {
                ids.Add(id);
            }
        }

        var builder = new StringBuilder();
        builder.Append("timestamp");
        foreach (string id in ids)
        {
            builder.Append(',').Append(id);
        }

        builder.AppendLine();

        foreach (SensorSnapshot row in rows)
        {
            builder.Append(row.Timestamp.ToString("O"));
            foreach (string id in ids)
            {
                builder.Append(',');
                SensorValue value = row.Find(id);
                if (value.Number is double number)
                {
                    builder.Append(number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (value.Text is not null)
                {
                    builder.Append('"').Append(value.Text.Replace("\"", "\"\"")).Append('"');
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }
}
