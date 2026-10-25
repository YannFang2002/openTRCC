namespace OpenTRCC.Core.Data;

/// <summary>One sensor reading. Exactly one of <see cref="Number"/> and <see cref="Text"/> is set.</summary>
public readonly record struct SensorValue
{
    private SensorValue(double? number, string? text, string unit)
    {
        Number = number;
        Text = text;
        Unit = unit;
    }

    public double? Number { get; }

    public string? Text { get; }

    public string Unit { get; }

    public bool IsMissing => Number is null && Text is null;

    public static SensorValue FromNumber(double value, string unit = "") => new(value, null, unit);

    public static SensorValue FromText(string text, string unit = "") => new(null, text, unit);

    public static SensorValue Missing(string unit = "") => new(null, null, unit);

    public string Display(string format = "")
    {
        if (Number is double number)
        {
            return string.IsNullOrEmpty(format) ? number.ToString("0.#") : number.ToString(format);
        }

        return Text ?? "--";
    }
}

/// <summary>Immutable snapshot the renderer and the UI read together.</summary>
public sealed class SensorSnapshot
{
    public SensorSnapshot(
        DateTimeOffset timestamp,
        IReadOnlyDictionary<string, SensorValue> values,
        IReadOnlySet<string>? unavailable = null)
    {
        Timestamp = timestamp;
        Values = values;
        Unavailable = unavailable ?? new HashSet<string>();
    }

    public DateTimeOffset Timestamp { get; }

    public IReadOnlyDictionary<string, SensorValue> Values { get; }

    /// <summary>Sensors known but currently missing; the editor marks these red.</summary>
    public IReadOnlySet<string> Unavailable { get; }

    public SensorValue Find(string id)
        => Values.TryGetValue(id, out SensorValue value) ? value : SensorValue.Missing();

    public static SensorSnapshot Empty { get; } = new(
        DateTimeOffset.Now,
        new Dictionary<string, SensorValue>(),
        new HashSet<string>());

    /// <summary>Sample data used by the editor preview and the probe.</summary>
    public static SensorSnapshot Sample(DateTimeOffset? at = null)
    {
        var values = new Dictionary<string, SensorValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["cpu.temp.package"] = SensorValue.FromNumber(62, "°C"),
            ["cpu.load.total"] = SensorValue.FromNumber(37, "%"),
            ["cpu.power.package"] = SensorValue.FromNumber(88.4, "W"),
            ["gpu.temp.core"] = SensorValue.FromNumber(58, "°C"),
            ["gpu.load.core"] = SensorValue.FromNumber(74, "%"),
            ["gpu.mem.used"] = SensorValue.FromNumber(6.8, "GB"),
            ["mem.load"] = SensorValue.FromNumber(41, "%"),
            ["fps.current"] = SensorValue.FromNumber(144, "FPS"),
            ["fps.low1"] = SensorValue.FromNumber(118, "FPS"),
            ["net.down"] = SensorValue.FromNumber(12.4, "MB/s"),
        };

        return new SensorSnapshot(at ?? DateTimeOffset.Now, values);
    }
}
