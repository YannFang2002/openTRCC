using OpenTRCC.Core.Data;

namespace OpenTRCC.Sensors;

/// <summary>
/// Polls every source on a timer and keeps an immutable latest snapshot plus a
/// short history per numeric sensor (300 points at 1 Hz).
/// </summary>
public sealed class SensorAggregator : IDisposable
{
    public const int HistoryLength = 300;

    private readonly IReadOnlyList<ISensorSource> _sources;
    private readonly Dictionary<string, Queue<double>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private SensorSnapshot _current = SensorSnapshot.Empty;
    private Timer? _timer;
    private bool _disposed;

    public SensorAggregator(IReadOnlyList<ISensorSource> sources)
    {
        _sources = sources;
    }

    public IReadOnlyList<ISensorSource> Sources => _sources;

    public SensorSnapshot Current => Volatile.Read(ref _current);

    /// <summary>Metadata for the sensor browser; filled on the first poll and augmented with any id we saw.</summary>
    public IReadOnlyList<Core.Data.SensorDescriptor> Descriptors { get; private set; } =
        Array.Empty<Core.Data.SensorDescriptor>();

    public event Action<SensorSnapshot>? Updated;

    public void Start(TimeSpan interval)
    {
        _timer ??= new Timer(_ => PollOnce(), null, TimeSpan.Zero, interval);
    }

    public SensorSnapshot PollOnce()
    {
        if (_disposed)
        {
            return _current;
        }

        var builder = new SensorSnapshotBuilder();
        var errors = new List<string>();

        foreach (ISensorSource source in _sources)
        {
            try
            {
                source.Poll(builder);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                errors.Add($"{source.Id}: {ex.Message}");
            }
        }

        SensorSnapshot snapshot = builder.Build(DateTimeOffset.Now);
        RememberHistory(snapshot);
        EnsureDescriptors(snapshot);

        LastErrors = errors;

        Volatile.Write(ref _current, snapshot);
        Updated?.Invoke(snapshot);
        return snapshot;
    }

    /// <summary>Exceptions swallowed by the last poll, one line per source.</summary>
    public IReadOnlyList<string> LastErrors { get; private set; } = Array.Empty<string>();

    /// <summary>Numeric history for a sensor, oldest first. Empty when never sampled.</summary>
    public IReadOnlyList<double> History(string id)
    {
        lock (_gate)
        {
            return _history.TryGetValue(id, out Queue<double>? queue) ? queue.ToArray() : Array.Empty<double>();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;

        foreach (ISensorSource source in _sources)
        {
            if (source is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// Builds the descriptor list once from the sources, then keeps it usable:
    /// any id that shows up without metadata still gets a row in the browser.
    /// </summary>
    private void EnsureDescriptors(SensorSnapshot snapshot)
    {
        var known = new Dictionary<string, Core.Data.SensorDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (Core.Data.SensorDescriptor descriptor in Descriptors)
        {
            known[descriptor.Id] = descriptor;
        }

        bool unknownSeen = snapshot.Values.Keys.Any(id => !known.ContainsKey(id));

        if (known.Count == 0 || unknownSeen)
        {
            // Hardware can come online after the first poll (per-core CPU sensors,
            // extra fans); re-ask the sources so new ids get real names, not "?".
            foreach (ISensorSource source in _sources)
            {
                try
                {
                    foreach (Core.Data.SensorDescriptor descriptor in source.Describe())
                    {
                        known[descriptor.Id] = descriptor;
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                }
            }
        }

        foreach ((string id, SensorValue value) in snapshot.Values)
        {
            if (!known.ContainsKey(id))
            {
                known[id] = new Core.Data.SensorDescriptor(id, id, value.Unit, "?", "其它");
            }
        }

        if (known.Count != Descriptors.Count)
        {
            Descriptors = known.Values
                .OrderBy(d => CategoryRank(d.Category))
                .ThenBy(d => d.Name, StringComparer.CurrentCulture)
                .ToArray();
        }
    }

    /// <summary>Puts the values people look at first: FPS, CPU, GPU, then the rest.</summary>
    private static int CategoryRank(string category) => category switch
    {
        "FPS" => 0,
        "CPU" => 1,
        "GPU" => 2,
        "内存" => 3,
        "主板" => 4,
        "存储" => 5,
        "网络" => 6,
        "系统" => 7,
        _ => 8,
    };

    private void RememberHistory(SensorSnapshot snapshot)
    {
        lock (_gate)
        {
            foreach ((string id, SensorValue value) in snapshot.Values)
            {
                if (value.Number is not double number)
                {
                    continue;
                }

                if (!_history.TryGetValue(id, out Queue<double>? queue))
                {
                    queue = new Queue<double>(HistoryLength);
                    _history[id] = queue;
                }

                queue.Enqueue(number);
                while (queue.Count > HistoryLength)
                {
                    queue.Dequeue();
                }
            }
        }
    }
}
