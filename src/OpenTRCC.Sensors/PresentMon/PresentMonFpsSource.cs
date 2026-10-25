using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenTRCC.Core.Data;

namespace OpenTRCC.Sensors.PresentMon;

/// <summary>One PresentMon frame row, reduced to the columns the FPS maths needs.</summary>
public readonly record struct PresentMonFrame(
    string Application,
    int ProcessId,
    string SwapChainAddress,
    double MsBetweenPresents,
    int? VidPnSourceId);

/// <summary>Header-driven CSV parser for PresentMon's <c>--output_stdout</c>.</summary>
public sealed class PresentMonCsvParser
{
    private readonly Dictionary<string, int> _columns = new(StringComparer.OrdinalIgnoreCase);

    public bool HasHeader => _columns.Count > 0;

    /// <summary>Parses a header row; returns false for data rows or unrecognised lines.</summary>
    public bool TryReadHeader(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("Application", StringComparison.Ordinal))
        {
            return false;
        }

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string[] headers = SplitCsv(line);
        for (int i = 0; i < headers.Length; i++)
        {
            columns[headers[i].Trim()] = i;
        }

        if (!columns.ContainsKey("ProcessID") || !columns.ContainsKey("MsBetweenPresents"))
        {
            return false;
        }

        _columns.Clear();
        foreach ((string name, int index) in columns)
        {
            _columns[name] = index;
        }

        return true;
    }

    public bool TryParse(string line, out PresentMonFrame frame)
    {
        frame = default;
        if (!HasHeader || string.IsNullOrEmpty(line))
        {
            return false;
        }

        string[] fields = SplitCsv(line);
        if (fields.Length <= Math.Max(Column("Application"), Column("MsBetweenPresents")))
        {
            return false;
        }

        if (!double.TryParse(fields[Column("MsBetweenPresents")], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double ms)
            || !int.TryParse(fields[Column("ProcessID")], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int pid))
        {
            return false;
        }

        string swapChain = Column("SwapChainAddress") >= 0 && Column("SwapChainAddress") < fields.Length
            ? fields[Column("SwapChainAddress")]
            : string.Empty;

        int? source = null;
        if (Column("VidPnSourceId") >= 0 && Column("VidPnSourceId") < fields.Length
            && int.TryParse(fields[Column("VidPnSourceId")], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int vidpn))
        {
            source = vidpn;
        }

        frame = new PresentMonFrame(fields[Column("Application")], pid, swapChain, ms, source);
        return true;
    }

    private int Column(string name) => _columns.TryGetValue(name, out int index) ? index : -1;

    /// <summary>Minimal CSV splitter that honours double quotes.</summary>
    private static string[] SplitCsv(string line)
    {
        var fields = new List<string>(16);
        var current = new StringBuilder(line.Length / 8);
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (c == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}

/// <summary>
/// Runs PresentMon as a child process and turns its stdout CSV into FPS samples.
/// PresentMon uses ETW only: no injection, no hooks, safe with anti-cheat.
/// </summary>
public sealed class PresentMonFpsSource : ISensorSource, IDisposable
{
    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "explorer", "SearchHost", "ShellExperienceHost",
        "ApplicationFrameHost", "StartMenuExperienceHost", "TextInputHost",
    };

    private readonly string? _executable;
    private readonly object _gate = new();
    private readonly Queue<FrameSample> _frames = new();
    private readonly HashSet<int> _displaySources = new();
    private readonly Process? _process;

    /// <summary>One parsed PresentMon row, with the display it presented to.</summary>
    private readonly record struct FrameSample(
        DateTimeOffset At, double Ms, int ProcessId, string Application, string SwapChain, int? VidPnSourceId);

    private readonly CancellationTokenSource _cts = new();
    private Task? _reader;
    private string? _unavailableReason;
    private string? _stderrTail;
    private double _lastFps;
    private double _lastLow1;
    private string? _lastProcessName;
    private DateTimeOffset _lastFrameAt;
    private int _lastPid;
    private double _smoothedFps;
    private double _smoothedLow1;

    public PresentMonFpsSource(string? executablePath = null, string sessionName = "OpenTRCC-FPS")
    {
        SessionName = sessionName;
        _executable = executablePath ?? FindExecutable();
        if (_executable is null)
        {
            _unavailableReason = "PresentMon.exe not found (expected third_party/presentmon/PresentMon.exe or on PATH).";
            return;
        }

        // A killed PresentMon leaves its ETW session behind; the next run then
        // starts against a stale session and silently captures nothing.
        StopLeftoverSession();

        try
        {
            var startInfo = new ProcessStartInfo(_executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in new[]
                     {
                         "--output_stdout", "--no_console_stats",
                         "--session_name", sessionName,
                         "--write_display_metadata",
                         // Our own session name only: cleans up a leftover from a
                         // previous crash instead of failing to start.
                         "--stop_existing_session",
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            _process = Process.Start(startInfo);
            if (_process is null)
            {
                _unavailableReason = "PresentMon process did not start.";
                return;
            }

            _reader = Task.Run(() => ReadLoop(_process, _cts.Token));
            _ = Task.Run(() => ReadStderr(_process));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _unavailableReason = $"PresentMon failed to start: {ex.Message}";
        }
    }

    public string Id => "presentmon";

    public string SessionName { get; }

    public string DisplayName => "PresentMon (ETW)";

    public bool IsAvailable => _process is not null && !_process.HasExited;

    public string? UnavailableReason
    {
        get
        {
            if (IsAvailable)
            {
                return null;
            }

            return _unavailableReason
                   ?? _stderrTail
                   ?? (_process is null ? "PresentMon not started." : "PresentMon exited.");
        }
    }

    public IReadOnlyList<string> DeclaredSensors { get; } = new[]
    {
        "fps.current", "fps.low1", "fps.stale", "fps.process", "fps.pinned",
    };

    public IReadOnlyList<Core.Data.SensorDescriptor> Describe()
    {
        var list = new List<Core.Data.SensorDescriptor>
        {
            new("fps.current", "当前 FPS", "FPS", Id, "FPS"),
            new("fps.low1", "1% Low", "FPS", Id, "FPS"),
            new("fps.process", "FPS 进程", string.Empty, Id, "FPS"),
            new("fps.stale", "FPS 是否过期", string.Empty, Id, "FPS"),
        };

        foreach (int source in _displaySources.OrderBy(s => s))
        {
            list.Add(new Core.Data.SensorDescriptor(
                $"fps.d{source}.current", $"显示器 {source + 1} · FPS", "FPS", Id, "FPS"));
            list.Add(new Core.Data.SensorDescriptor(
                $"fps.d{source}.process", $"显示器 {source + 1} · 进程", string.Empty, Id, "FPS"));
        }

        return list;
    }

    public string? PinnedProcess { get; set; }

    public void Poll(SensorSnapshotBuilder builder)
    {
        DateTimeOffset now = DateTimeOffset.Now;

        if (!IsAvailable)
        {
            if (TryPublishHeld(builder, now))
            {
                builder.Add("fps.debug", $"held:unavailable:{UnavailableReason}");
                return;
            }

            builder.Add("fps.debug", $"unavailable:{UnavailableReason}");
            builder.MarkUnavailable("fps.current");
            builder.MarkUnavailable("fps.low1");
            return;
        }

        double current;
        double low1;
        string processName;
        bool stale;
        var perDisplay = new List<(int Source, double Fps, string Process)>();

        lock (_gate)
        {
            Prune(now, TimeSpan.FromSeconds(30));

            int targetPid = SelectProcess(now);
            var relevant = new List<(DateTimeOffset At, double Ms)>();
            foreach (FrameSample sample in _frames)
            {
                if (sample.ProcessId == targetPid)
                {
                    relevant.Add((sample.At, sample.Ms));
                }
            }

            if (relevant.Count < 2)
            {
                // Hold the last reading instead of dropping to "unavailable":
                // a paused video or a bursty flush is not "no FPS", and a number
                // that vanishes every few seconds reads as a flicker on 320x240.
                if (TryPublishHeld(builder, now))
                {
                    builder.Add("fps.debug", $"held:frames={relevant.Count},age={(now - _lastFrameAt).TotalSeconds:F1}s");
                    return;
                }

                builder.Add("fps.debug",
                    $"noframes:count={relevant.Count},lastAge={(now - _lastFrameAt).TotalSeconds:F1}s,pid={targetPid}");
                builder.MarkUnavailable("fps.current");
                builder.MarkUnavailable("fps.low1");
                return;
            }

            processName = _frames.First(f => f.ProcessId == targetPid).Application;
            DateTimeOffset windowStart = now - TimeSpan.FromSeconds(1);
            int recent = relevant.Count(r => r.At >= windowStart);
            double span = Math.Max(0.2, (now - relevant[0].At).TotalSeconds);
            current = Math.Min(recent, span * 240); // guard against a burst inflating fps

            low1 = FpsMath.PercentileFps(relevant.Select(r => r.Ms), 0.99);
            if (double.IsNaN(low1))
            {
                low1 = 0;
            }

            // Display smoothing: a fresh 1 Hz sample still jitters; the panel
            // should read a steady number instead of blinking between 175 and 190.
            current = FpsMath.Smooth(_smoothedFps, current);
            if (low1 > 0)
            {
                low1 = FpsMath.Smooth(_smoothedLow1, low1);
            }

            _smoothedFps = current;
            _smoothedLow1 = low1;

            // Must be evaluated under the lock: the reader thread enqueues frames
            // concurrently, and enumerating _frames outside the lock threw
            // "Collection was modified" often enough to drop whole snapshots —
            // which is what made FPS fall back to "—" every few seconds.
            stale = !_frames.Any(f => f.At >= now - TimeSpan.FromSeconds(2));

            // Per-display FPS: PresentMon tags every frame with the VidPn source
            // that presented it, so each monitor gets its own number.
            foreach (var group in _frames
                         .Where(f => f.VidPnSourceId is not null
                                     && f.At >= windowStart
                                     && !IsIgnored(f.Application))
                         .GroupBy(f => f.VidPnSourceId!.Value))
            {
                var busiestProcess = group
                    .GroupBy(f => f.ProcessId)
                    .OrderByDescending(g => g.Count())
                    .First();

                perDisplay.Add((group.Key, busiestProcess.Count(), busiestProcess.First().Application));
                _displaySources.Add(group.Key);
            }
        }

        foreach ((int source, double fps, string process) in perDisplay)
        {
            builder.Add($"fps.d{source}.current", Math.Round(fps), "FPS");
            builder.Add($"fps.d{source}.process", process);
        }

        _lastFps = current;
        _lastLow1 = low1;
        _lastProcessName = processName;
        _lastFrameAt = now;

        builder.Add("fps.current", Math.Round(current, 0), "FPS");
        builder.Add("fps.low1", Math.Round(low1, 0), "FPS");
        builder.Add("fps.process", processName);
        builder.Add("fps.stale", stale ? 1 : 0);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException)
        {
        }

        _process?.Dispose();
        _cts.Dispose();
        StopLeftoverSession();
    }

    /// <summary>Removes a leftover ETW session with our name (best effort, no admin needed for our own).</summary>
    private void StopLeftoverSession()
    {
        try
        {
            var startInfo = new ProcessStartInfo("logman", $"stop {SessionName} -ets")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using Process? process = Process.Start(startInfo);
            process?.WaitForExit(3000);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Picks the process whose FPS the panel should show: the pinned one, else the
    /// busiest non-ignored presenter in the last second. Foreground alone is not
    /// enough — a paused/overlay window can own the foreground with almost no
    /// frames while a fullscreen video plays behind it.
    /// </summary>
    /// <summary>How long a lost reading is kept on screen before reporting "no FPS".</summary>
    private static readonly TimeSpan HoldLastReadingFor = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Republishes the last known FPS with stale=1. A short gap (paused video,
    /// bursty ETW flush) should not blank the number — on a 320x240 panel a value
    /// that disappears every few seconds is indistinguishable from a flicker.
    /// </summary>
    private bool TryPublishHeld(SensorSnapshotBuilder builder, DateTimeOffset now)
    {
        // Once a panel has shown a number, it keeps showing one: a stale reading
        // (flagged via fps.stale) is far better on a 320x240 display than a value
        // that vanishes and comes back. Only "never had a reading" reports missing.
        if (_lastFrameAt == default)
        {
            return false;
        }

        builder.Add("fps.current", Math.Round(_lastFps), "FPS");
        builder.Add("fps.low1", Math.Round(_lastLow1), "FPS");
        builder.Add("fps.process", _lastProcessName ?? string.Empty);
        builder.Add("fps.stale", now - _lastFrameAt >= HoldLastReadingFor ? 2 : 1);
        return true;
    }

    /// <summary>
    /// The ignore list is written without ".exe"; frames carry the resolved
    /// process name ("dwm.exe"), so compare the stem as well.
    /// </summary>
    private static bool IsIgnored(string application)
    {
        if (string.IsNullOrEmpty(application))
        {
            return false;
        }

        return IgnoredProcesses.Contains(application)
               || IgnoredProcesses.Contains(Path.GetFileNameWithoutExtension(application));
    }

    private int SelectProcess(DateTimeOffset now)
    {
        if (!string.IsNullOrEmpty(PinnedProcess)
            && _frames.Any(f => f.Application.Equals(PinnedProcess, StringComparison.OrdinalIgnoreCase)))
        {
            return _frames.First(f => f.Application.Equals(PinnedProcess, StringComparison.OrdinalIgnoreCase)).ProcessId;
        }

        DateTimeOffset since = now - TimeSpan.FromSeconds(1);
        int busiest = _frames
            .Where(f => f.At >= since && !IsIgnored(f.Application))
            .GroupBy(f => f.ProcessId)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        // Hysteresis: keep the current process while it is still presenting, so
        // the number doesn't jump between two apps (and doesn't drop to "—"
        // every time the foreground changes).
        if (_lastPid != 0 && busiest != 0 && busiest != _lastPid
            && _frames.Any(f => f.ProcessId == _lastPid && f.At >= now - TimeSpan.FromSeconds(2)))
        {
            return _lastPid;
        }

        if (busiest != 0)
        {
            _lastPid = busiest;
            return busiest;
        }

        int fallback = _frames
            .Where(f => !IsIgnored(f.Application))
            .GroupBy(f => f.ProcessId)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        if (fallback != 0)
        {
            _lastPid = fallback;
        }

        return fallback;
    }

    private void Prune(DateTimeOffset now, TimeSpan window)
    {
        DateTimeOffset cutoff = now - window;
        while (_frames.Count > 0 && _frames.Peek().At < cutoff)
        {
            _frames.Dequeue();
        }
    }

    private void ReadLoop(Process process, CancellationToken token)
    {
        var parser = new PresentMonCsvParser();
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? line = process.StandardOutput.ReadLine();
                if (line is null)
                {
                    break;
                }

                if (parser.TryReadHeader(line))
                {
                    continue;
                }

                if (!parser.TryParse(line, out PresentMonFrame frame))
                {
                    continue;
                }

                lock (_gate)
                {
                    string application = frame.Application;
                    if (string.IsNullOrEmpty(application) || application == "<unknown>")
                    {
                        application = ResolveProcessName(frame.ProcessId) ?? application;
                    }

                    _frames.Enqueue(new FrameSample(
                        DateTimeOffset.Now, frame.MsBetweenPresents, frame.ProcessId,
                        application, frame.SwapChainAddress, frame.VidPnSourceId));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    /// <summary>Keeps the last stderr line so a failed start explains itself.</summary>
    private void ReadStderr(Process process)
    {
        try
        {
            while (process.StandardError.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _stderrTail = line.Trim();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    /// <summary>PresentMon reports "&lt;unknown&gt;" without elevation; the PID still resolves.</summary>
    private static string? ResolveProcessName(int processId)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName + ".exe";
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? FindExecutable()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "PresentMon.exe"),
            Path.Combine(AppContext.BaseDirectory, "third_party", "presentmon", "PresentMon.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "third_party", "presentmon", "PresentMon.exe"),
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static int ForegroundProcessId()
    {
        nint window = GetForegroundWindow();
        if (window == nint.Zero)
        {
            return 0;
        }

        GetWindowThreadProcessId(window, out uint pid);
        return (int)pid;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
