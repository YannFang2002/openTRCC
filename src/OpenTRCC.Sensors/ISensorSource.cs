namespace OpenTRCC.Sensors;

/// <summary>A source of sensor readings (system APIs, LHM, PresentMon, …).</summary>
public interface ISensorSource
{
    /// <summary>Stable id, e.g. "system" or "lhm".</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>False when the source cannot run here (no admin, binary missing, …).</summary>
    bool IsAvailable { get; }

    /// <summary>Why the source is unavailable, for the UI and the logs.</summary>
    string? UnavailableReason { get; }

    /// <summary>Sensor ids this source can publish, for the sensor browser.</summary>
    IReadOnlyList<string> DeclaredSensors { get; }

    /// <summary>Describes every sensor this source can currently publish (may open hardware lazily).</summary>
    IReadOnlyList<Core.Data.SensorDescriptor> Describe();

    /// <summary>Fills the builder with the current readings. Must not throw.</summary>
    void Poll(SensorSnapshotBuilder builder);
}

/// <summary>Mutable accumulator for one snapshot.</summary>
public sealed class SensorSnapshotBuilder
{
    private readonly Dictionary<string, Core.Data.SensorValue> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unavailable = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string id, double value, string unit = "")
        => _values[id] = Core.Data.SensorValue.FromNumber(value, unit);

    public void Add(string id, string text, string unit = "")
        => _values[id] = Core.Data.SensorValue.FromText(text, unit);

    public void AddNumber(string id, double? value, string unit = "")
    {
        if (value is double number && double.IsFinite(number))
        {
            Add(id, number, unit);
        }
        else
        {
            _unavailable.Add(id);
        }
    }

    public void MarkUnavailable(string id) => _unavailable.Add(id);

    public Core.Data.SensorSnapshot Build(DateTimeOffset timestamp)
        => new(timestamp, _values, _unavailable);
}
