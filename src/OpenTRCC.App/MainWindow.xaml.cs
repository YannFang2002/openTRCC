using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenTRCC.App.Services;
using OpenTRCC.App.ViewModels;
using OpenTRCC.Core.Data;
using OpenTRCC.Core.Themes;
using OpenTRCC.Sensors;
using Forms = System.Windows.Forms;

namespace OpenTRCC.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly DisplayService _display;
    private readonly ThemeLibrary _themes;
    private readonly AppSettings _settings;
    private readonly Dictionary<string, SensorRow> _sensorRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<SensorRow> _visibleSensors = new();
    private WriteableBitmap? _preview;
    private Editor.EditorView? _editor;
    private bool _initializing;
    private Forms.NotifyIcon? _tray;
    private bool _exitRequested;
    private bool _trayHintShown;
    private int _descriptorCount;
    private int _logLines;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _themes = new ThemeLibrary();
        _display = new DisplayService(_themes.UserDirectory);

        _display.StatusChanged += OnStatusChanged;
        _display.PreviewReady += OnPreviewReady;
        _display.SensorsUpdated += OnSensorsUpdated;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _initializing = true;
        RefreshThemeList();
        ThemeDocument? selected = _themes.Find(_settings.ThemeId) ?? _themes.Default;
        ThemeList.SelectedItem = selected;

        SensorList.ItemsSource = _visibleSensors;

        bool autostart = AutostartService.IsEnabled();
        AutostartCheck.IsChecked = autostart;
        SettingsAutostartCheck.IsChecked = autostart;

        ThemeDirText.Text = _themes.UserDirectory;
        LogPathText.Text = Log.FilePath;
        ConfigPathText.Text = AppSettings.FilePath;

        _preview = new WriteableBitmap(320, 240, 96, 96, PixelFormats.Bgra32, null);
        PreviewImage.Source = _preview;

        Log.Written += OnLogLine;
        LoadLogTail();

        CreateTrayIcon();
        NavList.SelectedIndex = PageFromCommandLine();

        _initializing = false;

        if (_settings.StartStreamingOnLaunch && selected is not null)
        {
            StartStreaming(selected);
        }
    }

    /// <summary>Supports <c>OpenTRCC.exe --page sensors</c> for screenshots and manual checks.</summary>
    private static int PageFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--page", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return args[i + 1].ToLowerInvariant() switch
            {
                "sensors" => 1,
                "editor" => 2,
                "settings" => 3,
                "logs" => 4,
                _ => 0,
            };
        }

        return 0;
    }

    // ── 导航 ─────────────────────────────────────────────────────────

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        string page = (NavList.SelectedItem as ListBoxItem)?.Tag as string ?? "overview";

        OverviewPanel.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        SensorsPanel.Visibility = page == "sensors" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        LogsPanel.Visibility = page == "logs" ? Visibility.Visible : Visibility.Collapsed;
        EditorPanel.Visibility = page == "editor" ? Visibility.Visible : Visibility.Collapsed;

        switch (page)
        {
            case "sensors":
                RebuildSensorRows();
                break;
            case "editor":
                OpenEditor();
                break;
            case "settings":
                RefreshSettings();
                break;
            case "logs":
                LoadLogTail();
                break;
        }
    }

    private void RefreshThemeList()
    {
        foreach (string error in _themes.Errors)
        {
            Log.Write($"theme load failed: {error}");
        }

        string? selectedId = (ThemeList.SelectedItem as ThemeDocument)?.Id;
        ThemeList.ItemsSource = null;
        ThemeList.ItemsSource = _themes.Themes;

        ThemeDocument? target = _themes.Find(selectedId) ?? _themes.Default;
        ThemeList.SelectedItem = target;
    }

    /// <summary>Called by the editor after creating/saving/deleting a user theme.</summary>
    private void OnThemesChanged()
    {
        _themes.Reload();
        RefreshThemeList();
        ThemeDirText.Text = _themes.UserDirectory;

        if (ThemeList.SelectedItem is ThemeDocument theme
            && EditorPanel.Visibility == Visibility.Visible)
        {
            _editor?.RefreshThemes();
        }
    }

    private void OpenEditor()
    {
        if (_editor is null)
        {
            _editor = new Editor.EditorView(_display, _themes, OnThemesChanged);
            EditorPanel.Content = _editor;
        }

        if (ThemeList.SelectedItem is ThemeDocument theme)
        {
            _editor.LoadTheme(theme);
        }
    }

    // ── 概览 ─────────────────────────────────────────────────────────

    private void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (_display.IsRunning)
        {
            _display.Stop();
            StartButton.Content = "开始推流";
            StatusText.Text = "已停止";
            DeviceText.Text = "未连接";
            return;
        }

        if (ThemeList.SelectedItem is ThemeDocument theme)
        {
            StartStreaming(theme);
        }
    }

    private void StartStreaming(ThemeDocument theme)
    {
        Log.Write($"start requested: {theme.Id} ({theme.Layers.Count} layers)");
        _display.Start(theme);
        StartButton.Content = "停止推流";
        StatusText.Text = "正在连接…";
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ThemeList.SelectedItem is not ThemeDocument theme)
        {
            return;
        }

        _display.UpdateTheme(theme);
        _settings.ThemeId = theme.Id;
        _settings.Save();

        if (_editor is not null && EditorPanel.Visibility == Visibility.Visible)
        {
            _editor.LoadTheme(theme);
        }
    }

    private void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        SetAutostart(AutostartCheck.IsChecked == true);
    }

    private void OnSettingsAutostartClick(object sender, RoutedEventArgs e)
    {
        SetAutostart(SettingsAutostartCheck.IsChecked == true);
    }

    private void SetAutostart(bool enabled)
    {
        AutostartService.Set(enabled);
        _settings.AutoStart = enabled;
        _settings.Save();
        AutostartCheck.IsChecked = enabled;
        SettingsAutostartCheck.IsChecked = enabled;
    }

    // ── 传感器页 ─────────────────────────────────────────────────────

    private void OnSensorSearchChanged(object sender, TextChangedEventArgs e) => ApplySensorFilter();

    private void RebuildSensorRows()
    {
        int count = _display.Descriptors.Count;
        if (count == _descriptorCount && _sensorRows.Count > 0)
        {
            ApplySensorFilter();
            return;
        }

        _descriptorCount = count;
        _sensorRows.Clear();

        foreach (SensorDescriptor descriptor in _display.Descriptors)
        {
            if (IsHiddenSensor(descriptor.Id))
            {
                continue;
            }

            _sensorRows[descriptor.Id] = new SensorRow(
                descriptor.Id, descriptor.Name, descriptor.Unit, descriptor.Source, descriptor.Category);
        }

        SensorSnapshot snapshot = _display.CurrentSnapshot;
        UpdateSensorValues(snapshot);
        ApplySensorFilter();
    }

    private void ApplySensorFilter()
    {
        string query = SensorSearch.Text.Trim();

        var matching = _sensorRows.Values
            .Where(row => query.Length == 0
                          || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || row.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || row.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _visibleSensors.Clear();
        foreach (SensorRow row in matching)
        {
            _visibleSensors.Add(row);
        }

        SensorCountText.Text = $"{_visibleSensors.Count} / {_sensorRows.Count} 个传感器";
    }

    private static bool IsHiddenSensor(string id)
        => id.Equals("fps.debug", StringComparison.OrdinalIgnoreCase)
           || id.Equals("fps.stale", StringComparison.OrdinalIgnoreCase)
           || id.StartsWith("lhm.network.", StringComparison.OrdinalIgnoreCase);

    private void UpdateSensorValues(SensorSnapshot snapshot)
    {
        foreach (SensorDescriptor descriptor in _display.Descriptors)
        {
            if (IsHiddenSensor(descriptor.Id))
            {
                continue;
            }

            if (!_sensorRows.TryGetValue(descriptor.Id, out SensorRow? row))
            {
                row = new SensorRow(descriptor.Id, descriptor.Name, descriptor.Unit, descriptor.Source, descriptor.Category);
                _sensorRows[descriptor.Id] = row;
            }

            SensorValue value = snapshot.Find(descriptor.Id);
            row.Value = value.IsMissing ? "—" : value.Display(value.Number is double ? "0.#" : string.Empty);
        }

        foreach ((string id, SensorValue value) in snapshot.Values)
        {
            if (IsHiddenSensor(id))
            {
                continue;
            }

            if (!_sensorRows.TryGetValue(id, out SensorRow? row))
            {
                row = new SensorRow(id, id, value.Unit, "?", "其它");
                _sensorRows[id] = row;
            }

            row.Value = value.IsMissing ? "—" : value.Display(value.Number is double ? "0.#" : string.Empty);
        }

        if (SensorsPanel.Visibility == Visibility.Visible)
        {
            ApplySensorFilter();
        }
    }

    // ── 设置页 ───────────────────────────────────────────────────────

    private void RefreshSettings()
    {
        var lines = new List<string>();

        foreach (ISensorSource source in _display.ActiveSources)
        {
            lines.Add(source.IsAvailable
                ? $"{source.DisplayName} · 可用"
                : $"{source.DisplayName} · 不可用（{source.UnavailableReason ?? "原因未知"}）");
        }

        if (lines.Count == 0)
        {
            lines.Add("推流未启动");
        }

        SourceStatusList.ItemsSource = lines;
        SettingsDeviceText.Text = _display.DeviceSummary ?? "未连接";

        var displays = new System.Text.StringBuilder();
        int index = 0;
        foreach (Forms.Screen screen in Forms.Screen.AllScreens)
        {
            displays.AppendLine(
                $"显示器 {index + 1}: {screen.DeviceName}" +
                $"{(screen.Primary ? "（主屏）" : string.Empty)} " +
                $"{screen.Bounds.Width}x{screen.Bounds.Height}");
            index++;
        }

        DisplaysText.Text = displays.ToString().TrimEnd();
    }

    // ── 日志页 ───────────────────────────────────────────────────────

    private void OnLogLine(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (LogsPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
            _logLines++;

            if (_logLines > 400)
            {
                LoadLogTail();
            }
        });
    }

    private void LoadLogTail()
    {
        LogBox.Text = Log.TailText(300);
        LogBox.ScrollToEnd();
        _logLines = 300;
    }

    private void OnClearLogClick(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
        _logLines = 0;
    }

    private void OnOpenLogClick(object sender, RoutedEventArgs e) => OpenPath(Log.FilePath);

    private void OnOpenConfigClick(object sender, RoutedEventArgs e)
    {
        string? directory = Path.GetDirectoryName(AppSettings.FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            OpenPath(directory);
        }
    }

    private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("OpenTRCC diagnostics");
        text.AppendLine($"time: {DateTimeOffset.Now:O}");
        text.AppendLine($"device: {_display.DeviceSummary ?? "未连接"}");
        text.AppendLine($"theme: {(_display.ThemeId ?? "-")}");
        text.AppendLine($"fps: {_display.CurrentSnapshot.Find("fps.current").Display("0")}");
        text.AppendLine("sources:");

        foreach (ISensorSource source in _display.ActiveSources)
        {
            text.AppendLine($"  {source.Id}: available={source.IsAvailable} {source.UnavailableReason}");
        }

        text.AppendLine("log tail:");
        text.Append(Log.TailText(50));

        try
        {
            Clipboard.SetText(text.ToString());
            StatusText.Text = "诊断信息已复制";
        }
        catch (Exception)
        {
            StatusText.Text = "复制失败";
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            var info = new ProcessStartInfo(path) { UseShellExecute = true };
            Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    // ── 状态 / 预览 / 传感器 ─────────────────────────────────────────

    private void OnStatusChanged(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = message;
            DeviceText.Text = _display.DeviceSummary ?? (_display.IsRunning ? "已连接" : "未连接");

            if (!_display.IsRunning)
            {
                StartButton.Content = "开始推流";
            }

            if (SettingsPanel.Visibility == Visibility.Visible)
            {
                RefreshSettings();
            }
        });
    }

    private void OnPreviewReady(PreviewFrame frame)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_preview is null || frame.Width != _preview.PixelWidth || frame.Height != _preview.PixelHeight)
            {
                return;
            }

            _preview.WritePixels(
                new Int32Rect(0, 0, frame.Width, frame.Height),
                frame.Bgra,
                frame.Width * 4,
                0);
        });
    }

    private void OnSensorsUpdated(SensorSnapshot snapshot)
    {
        SensorValue fpsValue = snapshot.Find("fps.current");
        if (fpsValue.IsMissing)
        {
            Log.Write($"ui: fps missing ({snapshot.Find("fps.debug").Text ?? "no-debug"})");
        }

        Dispatcher.BeginInvoke(() =>
        {
            FpsText.Text = Format(snapshot, "fps.current", "0");
            CpuText.Text = Format(snapshot, "cpu.load.total", "0") + " %" + CpuTemperature(snapshot);

            string gpu = Format(snapshot, "gpu.temp.core", "0") + "°";
            gpu += "  " + Format(snapshot, "gpu.load.core", "0") + "%";
            GpuText.Text = gpu;

            MemText.Text = Format(snapshot, "mem.load", "0") + " %";
            NetText.Text = Format(snapshot, "net.down", "0.0") + " MB/s";

            UpdateSensorValues(snapshot);
        });
    }

    private static string CpuTemperature(SensorSnapshot snapshot)
    {
        SensorValue temperature = snapshot.Find("cpu.temp.package");
        return temperature.IsMissing ? string.Empty : $"  {temperature.Display("0")}°";
    }

    private static string Format(SensorSnapshot snapshot, string id, string format)
    {
        SensorValue value = snapshot.Find(id);
        return value.IsMissing ? "—" : value.Display(format);
    }

    // ── 托盘 ─────────────────────────────────────────────────────────

    private void CreateTrayIcon()
    {
        _tray = new Forms.NotifyIcon
        {
            Text = "OpenTRCC",
            Icon = LoadAppIcon(),
            Visible = true,
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => RestoreWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreWindow();
    }

    private void OnRestartAsAdminClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "OpenTRCC.exe",
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--page settings",
            };

            Process.Start(info);
            ExitApplication();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"提权失败或被取消：{ex.Message}",
                "OpenTRCC",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Loads the bundled .ico so the tray matches the window/exe icon.</summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/assets/icon/opentrcc.ico");
            using Stream stream = System.Windows.Application.GetResourceStream(uri)!.Stream;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return new System.Drawing.Icon(copy);
        }
        catch (Exception)
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitRequested)
        {
            return;
        }

        e.Cancel = true;
        Hide();

        if (!_trayHintShown && _tray is not null)
        {
            _trayHintShown = true;
            _tray.BalloonTipTitle = "OpenTRCC 仍在运行";
            _tray.BalloonTipText = "屏幕推流继续中，右键托盘图标可退出。";
            _tray.ShowBalloonTip(2500);
        }
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _display.Dispose();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        Application.Current.Shutdown();
    }
}
