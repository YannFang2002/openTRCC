using OpenTRCC.Core.Data;
using OpenTRCC.Sensors;
using Xunit;

namespace OpenTRCC.Sensors.Tests;

public sealed class SensorAggregatorTests
{
    private sealed class FakeSource : ISensorSource
    {
        private int _tick;

        public string Id => "fake";

        public string DisplayName => "Fake";

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public IReadOnlyList<string> DeclaredSensors { get; } = new[] { "cpu.load.total" };

        public IReadOnlyList<OpenTRCC.Core.Data.SensorDescriptor> Describe() => new[]
        {
            new OpenTRCC.Core.Data.SensorDescriptor("cpu.load.total", "CPU load", "%", "fake", "CPU"),
        };

        public void Poll(SensorSnapshotBuilder builder)
        {
            _tick++;
            builder.Add("cpu.load.total", _tick * 10, "%");
            builder.Add("app.foreground", "game.exe");
        }
    }

    [Fact]
    public void PollOnce_Builds_Snapshot_From_Sources()
    {
        using var aggregator = new SensorAggregator(new ISensorSource[] { new FakeSource() });

        SensorSnapshot snapshot = aggregator.PollOnce();

        Assert.Equal(10, snapshot.Find("cpu.load.total").Number);
        Assert.Equal("game.exe", snapshot.Find("app.foreground").Text);
        Assert.Empty(snapshot.Unavailable);
    }

    [Fact]
    public void History_Keeps_Ordered_Values()
    {
        using var aggregator = new SensorAggregator(new ISensorSource[] { new FakeSource() });
        aggregator.PollOnce();
        aggregator.PollOnce();
        aggregator.PollOnce();

        IReadOnlyList<double> history = aggregator.History("cpu.load.total");

        Assert.Equal(new double[] { 10, 20, 30 }, history);
    }

    [Fact]
    public void History_Is_Capped()
    {
        using var aggregator = new SensorAggregator(new ISensorSource[] { new FakeSource() });
        for (int i = 0; i < SensorAggregator.HistoryLength + 25; i++)
        {
            aggregator.PollOnce();
        }

        Assert.Equal(SensorAggregator.HistoryLength, aggregator.History("cpu.load.total").Count);
    }

    [Fact]
    public void Source_Exceptions_Do_Not_Break_The_Poll()
    {
        using var aggregator = new SensorAggregator(new ISensorSource[]
        {
            new ThrowingSource(),
            new FakeSource(),
        });

        SensorSnapshot snapshot = aggregator.PollOnce();

        Assert.Equal(10, snapshot.Find("cpu.load.total").Number);
    }

    private sealed class ThrowingSource : ISensorSource
    {
        public string Id => "boom";

        public string DisplayName => "Boom";

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public IReadOnlyList<string> DeclaredSensors => Array.Empty<string>();

        public IReadOnlyList<OpenTRCC.Core.Data.SensorDescriptor> Describe()
            => Array.Empty<OpenTRCC.Core.Data.SensorDescriptor>();

        public void Poll(SensorSnapshotBuilder builder) => throw new InvalidOperationException("kaboom");
    }
}

public sealed class SystemSensorSourceTests
{
    [Fact]
    public void Cpu_Usage_Maps_Deltas()
    {
        // 100% busy: no idle ticks at all.
        Assert.Equal(100, SystemSensorSource.ComputeCpuUsage(0, 500, 500));

        // 50% busy: half of the kernel+user time was idle.
        Assert.Equal(50, SystemSensorSource.ComputeCpuUsage(500, 500, 500));

        // No elapsed time reports 0 rather than NaN.
        Assert.Equal(0, SystemSensorSource.ComputeCpuUsage(0, 0, 0));
    }

    [Fact]
    public void Poll_Produces_Cpu_And_Memory()
    {
        using var source = new SystemSensorSource();
        var builder = new SensorSnapshotBuilder();

        source.Poll(builder);
        SensorSnapshot snapshot = builder.Build(DateTimeOffset.Now);

        // First call primes the CPU delta; memory must already be there.
        Assert.False(snapshot.Find("mem.load").IsMissing);
        Assert.True(snapshot.Find("mem.total.gb").Number > 1);
    }
}
