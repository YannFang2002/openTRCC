using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenTRCC.App.Services;
using OpenTRCC.Core.Data;
using OpenTRCC.Core.Imaging;
using OpenTRCC.Core.Rendering;
using OpenTRCC.Core.Themes;

namespace OpenTRCC.App.Editor;

/// <summary>
/// Canvas editor: layer list on the right, drag-to-move on a 2x canvas, property
/// fields below, JSON snapshot undo/redo, and every change is pushed to the panel.
/// </summary>
public partial class EditorView : UserControl
{
    private const int Zoom = 2;

    private readonly DisplayService _display;
    private readonly ThemeLibrary _themes;
    private readonly ThemeRenderer _renderer = new();
    private readonly ObservableCollection<Layer> _layers = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly DispatcherTimer _refresh;

    private ThemeDocument _theme = new();
    private Layer? _selected;
    private WriteableBitmap? _canvasBitmap;
    private Point _dragStart;
    private double _dragOriginX;
    private double _dragOriginY;
    private bool _dragging;
    private bool _panningBackground;
    private Point _panStart;
    private double _panOriginX;
    private double _panOriginY;
    private bool _syncing;
    private TextBox? _bindTarget;
    private Action<string>? _bindApply;
    private IBackgroundFrameProvider? _backgroundFrames;
    private IDisposable? _backgroundDisposable;

    private readonly Action? _themesChanged;

    public EditorView(DisplayService display, ThemeLibrary themes, Action? themesChanged = null)
    {
        _themesChanged = themesChanged;
        InitializeComponent();

        _display = display;
        _themes = themes;
        _renderer.BaseDirectory = themes.Directory;

        LayerList.ItemsSource = _layers;
        _canvasBitmap = new WriteableBitmap(320, 240, 96, 96, PixelFormats.Bgra32, null);
        CanvasImage.Source = _canvasBitmap;

        foreach (ThemeDocument theme in themes.Themes)
        {
            ThemePicker.Items.Add(theme);
        }

        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refresh.Tick += (_, _) =>
        {
            if (IsVisible)
            {
                RenderCanvas();
            }
        };
        _refresh.Start();

        LoadTheme(themes.Default);
    }

    public void LoadTheme(ThemeDocument theme)
    {
        _theme = theme;
        _renderer.ForgetHistory();
        _undo.Clear();
        _redo.Clear();
        _selected = null;

        RefreshThemes();

        SyncBackground();
        RefreshLayerList();
        BuildProperties(null);
        RenderCanvas();
        EditorStatus.Text = $"正在编辑：{theme.Name}（{theme.Id}）";
    }

    /// <summary>Rebuilds the (optional) animated background provider for the current theme.</summary>
    private void SyncBackground()
    {
        _backgroundDisposable?.Dispose();
        _backgroundFrames = BackgroundMedia.CreateFor(_theme, _themes.Directory);
        _backgroundDisposable = _backgroundFrames as IDisposable;
        _renderer.BackgroundFrames = _backgroundFrames;
    }

    private void SetBackground(Func<ThemeBackground, ThemeBackground> change)
    {
        PushUndo();
        _theme = _theme with
        {
            Canvas = _theme.Canvas with { Background = change(_theme.Canvas.Background) },
        };

        SyncBackground();
        RenderCanvas();
        CommitToDisplay();
        BuildProperties(null);
        EditorStatus.Text = "背景已更新";
    }

    private void AddBackgroundEditor()
    {
        AddHeader("背景");

        ThemeBackground background = _theme.Canvas.Background;

        AddChoice(
            "类型",
            new[] { "solid", "gradient", "image", "procedural" },
            background.Kind switch
            {
                BackgroundKind.Gradient => "gradient",
                BackgroundKind.Image => "image",
                BackgroundKind.Procedural => "procedural",
                _ => "solid",
            },
            value => SetBackground(b => b with
            {
                Kind = value switch
                {
                    "gradient" => BackgroundKind.Gradient,
                    "image" => BackgroundKind.Image,
                    "procedural" => BackgroundKind.Procedural,
                    _ => BackgroundKind.Solid,
                },
            }),
            display: value => Label(KindNames, value));

        switch (background.Kind)
        {
            case BackgroundKind.Gradient:
                AddColorFields(background, 2);
                AddField(
                    "渐变角度",
                    background.Angle.ToString("0"),
                    value => SetBackground(b => b with { Angle = ParseNumber(value, b.Angle) }));
                break;

            case BackgroundKind.Image:
                AddImageFields(background);
                break;

            case BackgroundKind.Procedural:
                AddChoice(
                    "动态预设",
                    ProceduralBackgrounds.Presets,
                    background.Preset,
                    value => SetBackground(b => b with { Preset = value }),
                    display: value => Label(PresetNames, value));

                AddField(
                    "动态速度",
                    background.Speed.ToString("0.##"),
                    value => SetBackground(b => b with { Speed = Math.Clamp(ParseNumber(value, b.Speed), 0.05, 5) }));

                AddColorFields(background, 4);
                AddField(
                    "暗化 (0-1)",
                    background.Dim.ToString("0.##"),
                    value => SetBackground(b => b with { Dim = Math.Clamp(ParseNumber(value, b.Dim), 0, 1) }));
                break;

            default:
                AddColorFields(background, 1);
                break;
        }
    }

    private void AddColorFields(ThemeBackground background, int count)
    {
        string[] fallback = { "#0B1020", "#1B2A4A", "#2DD4BF", "#A855F7" };

        for (int index = 0; index < count; index++)
        {
            int captured = index;
            AddField(
                $"颜色 {index + 1}",
                background.Colors.Skip(index).FirstOrDefault() ?? fallback[Math.Min(index, fallback.Length - 1)],
                value => SetBackground(b => b with { Colors = WithColor(b.Colors, captured, value) }));
        }
    }

    private void AddImageFields(ThemeBackground background)
    {
        AddField(
            "图片 / 动图 / 视频路径",
            background.Asset ?? string.Empty,
            value => SetBackground(b => b with { Asset = value }));

        var pick = new Wpf.Ui.Controls.Button
        {
            Content = "选择图片 / 动图 / 视频…",
            Margin = new Thickness(0, 0, 0, 8),
        };
        pick.Click += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "图片 / 动图 / 视频|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm|所有文件|*.*",
                Title = "选择背景图片、GIF 或视频",
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (VideoBackground.IsVideo(dialog.FileName))
            {
                await ImportVideoAsync(dialog.FileName);
                return;
            }

            SetBackground(b => b with { Asset = dialog.FileName, Fit = "cover", Zoom = 1 });
        };

        PropsPanel.Children.Add(pick);

        AddChoice(
            "适应",
            new[] { "cover", "contain", "stretch" },
            background.Fit,
            value => SetBackground(b => b with { Fit = value }),
            display: value => Label(FitNames, value));

        AddField(
            "缩放（1 = 铺满，越大越裁切）",
            background.Zoom.ToString("0.##"),
            value => SetBackground(b => b with { Zoom = Math.Clamp(ParseNumber(value, b.Zoom), 0.2, 4) }));

        AddField(
            "水平偏移",
            background.OffsetX.ToString("0"),
            value => SetBackground(b => b with { OffsetX = ParseNumber(value, b.OffsetX) }));

        AddField(
            "垂直偏移",
            background.OffsetY.ToString("0"),
            value => SetBackground(b => b with { OffsetY = ParseNumber(value, b.OffsetY) }));

        AddNudgePad();

        AddField(
            "暗化 (0-1)",
            background.Dim.ToString("0.##"),
            value => SetBackground(b => b with { Dim = Math.Clamp(ParseNumber(value, b.Dim), 0, 1) }));
    }

    /// <summary>Arrow pad that nudges the background by one canvas pixel per click.</summary>
    private void AddNudgePad()
    {
        AddHeader("位置微调（每次 1 像素）");

        var grid = new Grid { Width = 132, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
        }

        AddNudge(grid, "↑", 0, 0, 1);
        AddNudge(grid, "←", 1, 0, -1);
        AddNudge(grid, "→", 1, 2, 0);
        AddNudge(grid, "↓", 2, 1, -1);
    }

    private void AddNudge(Grid grid, string label, int row, int column, int delta)
    {
        var button = new Wpf.Ui.Controls.Button
        {
            Content = label,
            Padding = new Thickness(0),
            Margin = new Thickness(2),
            Width = 38,
            Height = 32,
        };

        if (label is "↑" or "↓")
        {
            button.Click += (_, _) => SetBackground(b => b with { OffsetY = b.OffsetY + delta });
        }
        else
        {
            button.Click += (_, _) => SetBackground(b => b with { OffsetX = b.OffsetX + delta });
        }

        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    /// <summary>
    /// Videos are converted once into a 320x240 JPEG frame sequence; playback then
    /// only decodes JPEGs instead of scaling 1080p frames every tick.
    /// </summary>
    private async Task ImportVideoAsync(string path)
    {
        var window = new VideoImportWindow { Owner = Window.GetWindow(this) };
        window.Show();

        try
        {
            var progress = new Progress<double>(window.Report);
            string meta = await VideoFrameExtractor.ExtractAsync(path, _themes.Directory, progress);
            SetBackground(b => b with { Kind = BackgroundKind.Image, Asset = meta, Fit = "cover", Zoom = 1 });
            EditorStatus.Text = $"视频已转成帧序列：{Path.GetFileName(Path.GetDirectoryName(meta))}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"视频导入失败：{ex.Message}", "OpenTRCC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            window.Close();
        }
    }

    private static IReadOnlyList<string> WithColor(IReadOnlyList<string> colors, int index, string value)
    {
        var list = colors.ToList();
        while (list.Count <= index)
        {
            list.Add("#000000");
        }

        list[index] = value;
        return list;
    }

    // ── 主题 / 图层列表 ─────────────────────────────────────────────

    private void OnThemePicked(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || ThemePicker.SelectedItem is not ThemeDocument theme)
        {
            return;
        }

        LoadTheme(theme);
    }

    private void RefreshLayerList()
    {
        _syncing = true;
        _layers.Clear();
        foreach (Layer layer in _theme.Layers)
        {
            _layers.Add(layer);
        }

        LayerList.SelectedItem = _selected is null ? null : _layers.FirstOrDefault(l => l.Id == _selected.Id);
        _syncing = false;
    }

    private void OnLayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || LayerList.SelectedItem is not Layer layer)
        {
            return;
        }

        _selected = layer;
        BuildProperties(layer);
        UpdateSelectionBox();
    }

    private void SelectLayer(Layer? layer)
    {
        _selected = layer;
        RefreshLayerList();
        BuildProperties(layer);
        UpdateSelectionBox();
    }

    // ── 画布交互 ────────────────────────────────────────────────────

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(CanvasHost);
        double x = point.X / Zoom;
        double y = point.Y / Zoom;

        Layer? hit = _theme.Layers
            .Where(l => l.Visible)
            .Reverse()
            .FirstOrDefault(l => x >= l.X && x <= l.X + l.W && y >= l.Y && y <= l.Y + l.H);

        SelectLayer(hit);

        if (hit is null)
        {
            // Nothing hit: dragging pans the background image/GIF/video.
            if (_theme.Canvas.Background.Kind == BackgroundKind.Image)
            {
                PushUndo();
                _panningBackground = true;
                _panStart = point;
                _panOriginX = _theme.Canvas.Background.OffsetX;
                _panOriginY = _theme.Canvas.Background.OffsetY;
                CanvasHost.CaptureMouse();
                e.Handled = true;
            }

            return;
        }

        PushUndo();
        _dragStart = point;
        _dragOriginX = hit.X;
        _dragOriginY = hit.Y;
        _dragging = true;
        CanvasHost.CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _selected is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point point = e.GetPosition(CanvasHost);

        if (_panningBackground)
        {
            double offsetX = Math.Round(_panOriginX + (point.X - _panStart.X) / Zoom);
            double offsetY = Math.Round(_panOriginY + (point.Y - _panStart.Y) / Zoom);
            _theme = _theme with
            {
                Canvas = _theme.Canvas with
                {
                    Background = _theme.Canvas.Background with { OffsetX = offsetX, OffsetY = offsetY },
                },
            };

            RenderCanvas();
            CommitToDisplay();
            return;
        }

        double newX = Math.Round(_dragOriginX + (point.X - _dragStart.X) / Zoom);
        double newY = Math.Round(_dragOriginY + (point.Y - _dragStart.Y) / Zoom);

        ReplaceLayer(_selected with { X = newX, Y = newY }, pushUndo: false, rebuildProperties: false);
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panningBackground)
        {
            _panningBackground = false;
            CanvasHost.ReleaseMouseCapture();
            BuildProperties(null);
            EditorStatus.Text = "背景位置已更新";
            return;
        }

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        CanvasHost.ReleaseMouseCapture();
        CommitToDisplay();
        RefreshLayerList();
    }

    // ── 增删改 ──────────────────────────────────────────────────────

    private int NextId()
    {
        int index = 1;
        while (_theme.Layers.Any(l => l.Id == $"layer-{index}"))
        {
            index++;
        }

        return index;
    }

    private void AddLayer(Layer layer)
    {
        PushUndo();
        var list = _theme.Layers.ToList();
        list.Add(layer);
        _theme = _theme with { Layers = list };
        _selected = layer;
        SyncFromTheme();
    }

    private void OnAddText(object sender, RoutedEventArgs e) => AddLayer(new TextLayer
    {
        Id = $"text-{NextId()}",
        X = 20, Y = 20, W = 200, H = 44,
        Text = "{cpu.load.total:0}%",
        Font = new FontSpec { Family = "Segoe UI", Size = 28, Weight = 700 },
        Color = "#E2E8F0",
    });

    private void OnAddBar(object sender, RoutedEventArgs e) => AddLayer(new BarLayer
    {
        Id = $"bar-{NextId()}",
        X = 20, Y = 100, W = 200, H = 10,
        Value = "{gpu.load.core}",
    });

    private void OnAddRing(object sender, RoutedEventArgs e) => AddLayer(new RingLayer
    {
        Id = $"ring-{NextId()}",
        X = 20, Y = 20, W = 120, H = 120,
        Value = "{cpu.load.total}",
        Thickness = 10,
    });

    private void OnAddSparkline(object sender, RoutedEventArgs e) => AddLayer(new SparklineLayer
    {
        Id = $"spark-{NextId()}",
        X = 20, Y = 190, W = 280, H = 30,
        Source = "{fps.current}",
        Min = 0, Max = 240,
    });

    private void OnAddImage(object sender, RoutedEventArgs e) => AddLayer(new ImageLayer
    {
        Id = $"image-{NextId()}",
        X = 20, Y = 20, W = 120, H = 120,
        Fit = "contain",
    });

    private void OnDeleteLayer(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        PushUndo();
        _theme = _theme with { Layers = _theme.Layers.Where(l => l.Id != _selected.Id).ToList() };
        _selected = null;
        SyncFromTheme();
    }

    private void OnDuplicateLayer(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        PushUndo();
        Layer copy = _selected with { Id = $"{_selected.Id}-copy-{NextId()}", X = _selected.X + 8, Y = _selected.Y + 8 };
        var list = _theme.Layers.ToList();
        list.Add(copy);
        _theme = _theme with { Layers = list };
        _selected = copy;
        SyncFromTheme();
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSelection(-1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSelection(1);

    private void MoveSelection(int delta)
    {
        if (_selected is null)
        {
            return;
        }

        var list = _theme.Layers.ToList();
        int index = list.FindIndex(l => l.Id == _selected.Id);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count)
        {
            return;
        }

        PushUndo();
        (list[index], list[target]) = (list[target], list[index]);
        _theme = _theme with { Layers = list };
        SyncFromTheme();
    }

    // ── 属性面板 ────────────────────────────────────────────────────

    private void BuildProperties(Layer? layer)
    {
        PropsPanel.Children.Clear();
        _bindTarget = null;
        _bindApply = null;

        if (layer is null)
        {
            PropsPanel.Children.Add(new TextBlock
            {
                Text = "选中画布上的图层可编辑属性；没选中时可以编辑背景。",
                Foreground = (Brush)FindResource("TextSecondary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 2, 12),
            });

            AddBackgroundEditor();
            return;
        }

        AddHeader($"图层 · {layer.Id}");

        // Text gets the friendly composer first: that is what users actually edit.
        if (layer is TextLayer textLayer)
        {
            BuildTextComposer(textLayer);
        }

        AddField("X", layer.X.ToString("0"), value => UpdateLayer(l => l with { X = ParseNumber(value, l.X) }));
        AddField("Y", layer.Y.ToString("0"), value => UpdateLayer(l => l with { Y = ParseNumber(value, l.Y) }));
        AddField("宽", layer.W.ToString("0"), value => UpdateLayer(l => l with { W = Math.Max(1, ParseNumber(value, l.W)) }));
        AddField("高", layer.H.ToString("0"), value => UpdateLayer(l => l with { H = Math.Max(1, ParseNumber(value, l.H)) }));
        AddField("不透明度", layer.Opacity.ToString("0.##"), value => UpdateLayer(l => l with { Opacity = Math.Clamp(ParseNumber(value, l.Opacity), 0, 1) }));

        switch (layer)
        {
            case TextLayer text:
                AddField("字号", text.Font.Size.ToString("0"), value => UpdateLayer(l => l is TextLayer t ? (Layer)(t with { Font = t.Font with { Size = Math.Max(4, ParseNumber(value, t.Font.Size)) } }) : l));
                AddField("颜色", text.Color, value => UpdateLayer(l => l is TextLayer t ? (Layer)(t with { Color = value }) : l));
                AddChoice("对齐", new[] { "left", "center", "right" }, text.Align, value => UpdateLayer(l => l is TextLayer t ? (Layer)(t with { Align = value }) : l), display: value => Label(AlignNames, value));
                AddChoice("垂直", new[] { "top", "middle", "bottom" }, text.VerticalAlign, value => UpdateLayer(l => l is TextLayer t ? (Layer)(t with { VerticalAlign = value }) : l), display: value => Label(VerticalNames, value));
                break;

            case BarLayer bar:
                AddSensorField("值", bar.Value, value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Value = value }) : l));
                AddField("最小", bar.Min.ToString("0.##"), value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Min = ParseNumber(value, b.Min) }) : l));
                AddField("最大", bar.Max.ToString("0.##"), value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Max = ParseNumber(value, b.Max) }) : l));
                AddField("填充色", bar.Fill, value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Fill = value }) : l));
                AddField("轨道色", bar.Track, value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Track = value }) : l));
                AddField("圆角", bar.Radius.ToString("0"), value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Radius = Math.Max(0, ParseNumber(value, b.Radius)) }) : l));
                AddChoice("方向", new[] { "horizontal", "vertical" }, bar.Orientation, value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Orientation = value }) : l), display: value => Label(OrientationNames, value));
                break;

            case RingLayer ring:
                AddSensorField("值", ring.Value, value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Value = value }) : l));
                AddField("最小", ring.Min.ToString("0.##"), value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Min = ParseNumber(value, r.Min) }) : l));
                AddField("最大", ring.Max.ToString("0.##"), value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Max = ParseNumber(value, r.Max) }) : l));
                AddField("粗细", ring.Thickness.ToString("0"), value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Thickness = Math.Max(1, ParseNumber(value, r.Thickness)) }) : l));
                AddField("颜色", ring.Fill.FirstOrDefault() ?? "#5EEAD4", value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Fill = new[] { value } }) : l));
                AddField("轨道色", ring.Track, value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Track = value }) : l));
                break;

            case SparklineLayer spark:
                AddSensorField("来源", spark.Source, value => UpdateLayer(l => l is SparklineLayer s ? (Layer)(s with { Source = value }) : l));
                AddField("采样数", spark.Samples.ToString(), value => UpdateLayer(l => l is SparklineLayer s ? (Layer)(s with { Samples = (int)Math.Max(2, ParseNumber(value, s.Samples)) }) : l));
                AddField("最小", spark.Min.ToString("0.##"), value => UpdateLayer(l => l is SparklineLayer s ? (Layer)(s with { Min = ParseNumber(value, s.Min) }) : l));
                AddField("最大", spark.Max.ToString("0.##"), value => UpdateLayer(l => l is SparklineLayer s ? (Layer)(s with { Max = ParseNumber(value, s.Max) }) : l));
                AddField("颜色", spark.Stroke, value => UpdateLayer(l => l is SparklineLayer s ? (Layer)(s with { Stroke = value }) : l));
                break;

            case ImageLayer image:
                AddField("资源", image.Asset, value => UpdateLayer(l => l is ImageLayer i ? (Layer)(i with { Asset = value }) : l));
                AddChoice("适应", new[] { "contain", "cover", "stretch" }, image.Fit, value => UpdateLayer(l => l is ImageLayer i ? (Layer)(i with { Fit = value }) : l), display: value => Label(FitNames, value));
                break;
        }

    }

    private static readonly Dictionary<string, string> KindNames = new()
    {
        ["solid"] = "纯色", ["gradient"] = "渐变", ["image"] = "图片 / 动图 / 视频", ["procedural"] = "动态背景",
    };

    private static readonly Dictionary<string, string> PresetNames = new()
    {
        ["aurora"] = "极光", ["waves"] = "波浪", ["particles"] = "粒子", ["stars"] = "星空",
        ["grid"] = "网格", ["plasma"] = "等离子", ["rain"] = "数字雨", ["ronin"] = "浪人", ["sprinter"] = "疾跑者",
    };

    private static readonly Dictionary<string, string> FitNames = new()
    {
        ["cover"] = "铺满裁切", ["contain"] = "完整显示", ["stretch"] = "拉伸",
    };

    private static readonly Dictionary<string, string> AlignNames = new()
    {
        ["left"] = "左", ["center"] = "中", ["right"] = "右",
    };

    private static readonly Dictionary<string, string> VerticalNames = new()
    {
        ["top"] = "上", ["middle"] = "中", ["bottom"] = "下",
    };

    private static readonly Dictionary<string, string> OrientationNames = new()
    {
        ["horizontal"] = "水平", ["vertical"] = "垂直",
    };

    private static string Label(Dictionary<string, string> map, string key)
        => map.TryGetValue(key, out string? value) ? value : key;
    private IReadOnlyList<SensorDescriptor>? _choiceList;

    private IReadOnlyList<SensorDescriptor> SensorChoices => _choiceList ??= BuildChoices();

    private List<SensorDescriptor> BuildChoices()
    {
        var list = new List<SensorDescriptor>
        {
            new("time", "当前时间", string.Empty, "builtin", "时间"),
            new("date", "日期", string.Empty, "builtin", "时间"),
        };

        list.AddRange(_display.Descriptors);
        return list;
    }

    private sealed class TextSegment
    {
        public bool IsBinding { get; init; }

        public string Text { get; set; } = string.Empty;

        public string Id { get; set; } = string.Empty;

        public string Format { get; set; } = string.Empty;
    }

    private static readonly Regex BindingPattern = new(@"\{([^}:]+)(?::([^}]*))?\}", RegexOptions.Compiled);

    private static List<TextSegment> ParseTemplate(string template)
    {
        var segments = new List<TextSegment>();
        int position = 0;

        foreach (Match match in BindingPattern.Matches(template))
        {
            if (match.Index > position)
            {
                segments.Add(new TextSegment { Text = template[position..match.Index] });
            }

            segments.Add(new TextSegment
            {
                IsBinding = true,
                Id = match.Groups[1].Value,
                Format = match.Groups[2].Value,
            });

            position = match.Index + match.Length;
        }

        if (position < template.Length)
        {
            segments.Add(new TextSegment { Text = template[position..] });
        }

        if (segments.Count == 0)
        {
            segments.Add(new TextSegment { Text = string.Empty });
        }

        return segments;
    }

    private static string ComposeTemplate(IEnumerable<TextSegment> segments)
        => string.Concat(segments.Select(s => s.IsBinding
            ? (s.Format.Length > 0 ? $"{{{s.Id}:{s.Format}}}" : $"{{{s.Id}}}")
            : s.Text));

    /// <summary>Friendly text editor: literal pieces plus sensor chips, never raw braces.</summary>
    private void BuildTextComposer(TextLayer layer)
    {
        AddHeader("显示文字");
        var segments = ParseTemplate(layer.Text);

        for (int index = 0; index < segments.Count; index++)
        {
            TextSegment segment = segments[index];
            int captured = index;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6),
            };

            if (segment.IsBinding)
            {
                var combo = new ComboBox
                {
                    ItemsSource = SensorChoices,
                    DisplayMemberPath = "Name",
                    Width = 180,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                combo.SelectedItem = SensorChoices.FirstOrDefault(d => d.Id == segment.Id);
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is SensorDescriptor descriptor)
                    {
                        segments[captured].Id = descriptor.Id;
                    }
                };

                var format = new TextBox
                {
                    Text = segment.Format,
                    Width = 56,
                    Margin = new Thickness(0, 0, 6, 0),
                    ToolTip = "数字格式，例如 0 / 0.0 / 0%",
                };
                format.TextChanged += (_, _) => segments[captured].Format = format.Text;

                row.Children.Add(combo);
                row.Children.Add(format);
            }
            else
            {
                var box = new TextBox
                {
                    Text = segment.Text,
                    Width = 242,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                box.TextChanged += (_, _) => segments[captured].Text = box.Text;
                row.Children.Add(box);
            }

            var remove = new Wpf.Ui.Controls.Button
            {
                Content = "×",
                Width = 32,
                Padding = new Thickness(0),
            };
            remove.Click += (_, _) =>
            {
                segments.RemoveAt(captured);
                if (segments.Count == 0)
                {
                    segments.Add(new TextSegment { Text = string.Empty });
                }

                ApplyText(segments);
            };

            row.Children.Add(remove);
            PropsPanel.Children.Add(row);
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 4),
        };

        var addText = new Wpf.Ui.Controls.Button { Content = "＋文字", Margin = new Thickness(0, 0, 6, 0) };
        addText.Click += (_, _) =>
        {
            segments.Add(new TextSegment { Text = "新文字" });
            ApplyText(segments);
        };

        var addSensor = new Wpf.Ui.Controls.Button { Content = "＋传感器", Margin = new Thickness(0, 0, 6, 0) };
        addSensor.Click += (_, _) =>
        {
            SensorDescriptor? first = _display.Descriptors.FirstOrDefault();
            if (first is not null)
            {
                segments.Add(new TextSegment { IsBinding = true, Id = first.Id });
                ApplyText(segments);
            }
        };

        var apply = new Wpf.Ui.Controls.Button { Content = "应用", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary };
        apply.Click += (_, _) => ApplyText(segments);

        actions.Children.Add(addText);
        actions.Children.Add(addSensor);
        actions.Children.Add(apply);
        PropsPanel.Children.Add(actions);

        PropsPanel.Children.Add(new TextBlock
        {
            Text = "每一行是一段文字或一个传感器；格式只对传感器生效。",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 8),
        });
    }

    private void ApplyText(List<TextSegment> segments)
    {
        string template = ComposeTemplate(segments);
        UpdateLayer(l => l is TextLayer t ? (Layer)(t with { Text = template }) : l);
    }

    /// <summary>Sensor picker for numeric layers (bar/ring/sparkline), with an optional format.</summary>
    private void AddSensorField(string label, string value, Action<string> apply)
    {
        Match match = BindingPattern.Match(value);
        string id = match.Success ? match.Groups[1].Value : string.Empty;
        string format = match.Success ? match.Groups[2].Value : string.Empty;

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondary"),
            Margin = new Thickness(2, 0, 0, 3),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var combo = new ComboBox
        {
            ItemsSource = SensorChoices,
            DisplayMemberPath = "Name",
            Width = 180,
            Margin = new Thickness(0, 0, 6, 0),
        };
        combo.SelectedItem = SensorChoices.FirstOrDefault(d => d.Id == id);

        var formatBox = new TextBox
        {
            Text = format,
            Width = 56,
            ToolTip = "数字格式，例如 0 / 0.0",
        };

        void Push()
        {
            if (combo.SelectedItem is SensorDescriptor descriptor)
            {
                apply(formatBox.Text.Length > 0
                    ? $"{{{descriptor.Id}:{formatBox.Text}}}"
                    : $"{{{descriptor.Id}}}");
            }
        }

        combo.SelectionChanged += (_, _) => Push();
        formatBox.LostFocus += (_, _) => Push();

        row.Children.Add(combo);
        row.Children.Add(formatBox);
        panel.Children.Add(row);
        PropsPanel.Children.Add(panel);
    }
    private void AddHeader(string text)
    {
        PropsPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 0, 8),
        });
    }

    private void AddField(string label, string value, Action<string> apply, bool isBinding = false)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondary"),
            Margin = new Thickness(2, 0, 0, 3),
        });

        var box = new TextBox { Text = value, FontFamily = new FontFamily("Consolas") };
        box.LostFocus += (_, _) => apply(box.Text);
        box.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                apply(box.Text);
                args.Handled = true;
            }
        };

        if (isBinding)
        {
            box.GotFocus += (_, _) =>
            {
                _bindTarget = box;
                _bindApply = apply;
            };
        }

        panel.Children.Add(box);
        PropsPanel.Children.Add(panel);
    }

    private void AddChoice(
        string label,
        string[] items,
        string current,
        Action<string> apply,
        Func<string, string>? display = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondary"),
            Margin = new Thickness(2, 0, 0, 3),
        });

        var combo = new ComboBox();
        foreach (string item in items)
        {
            combo.Items.Add(display is null ? item : display(item));
        }

        combo.SelectedIndex = Math.Max(0, Array.IndexOf(items, current));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < items.Length)
            {
                apply(items[combo.SelectedIndex]);
            }
        };

        panel.Children.Add(combo);
        PropsPanel.Children.Add(panel);
    }

    private void AddBindingPicker()
    {
        AddHeader("插入传感器");
        var combo = new ComboBox { MaxHeight = 140 };
        foreach (SensorDescriptor descriptor in _display.Descriptors)
        {
            combo.Items.Add(descriptor);
        }

        combo.DisplayMemberPath = "Name";

        var button = new Wpf.Ui.Controls.Button
        {
            Content = "插入到当前字段",
            Margin = new Thickness(0, 6, 0, 0),
        };
        button.Click += (_, _) =>
        {
            if (_bindTarget is null || combo.SelectedItem is not SensorDescriptor descriptor)
            {
                return;
            }

            _bindTarget.Text = _bindTarget.Text.Length == 0
                ? $"{{{descriptor.Id}}}"
                : $"{_bindTarget.Text} {{{descriptor.Id}}}";
            _bindApply?.Invoke(_bindTarget.Text);
        };

        PropsPanel.Children.Add(combo);
        PropsPanel.Children.Add(button);
    }

    // ── 变更与撤销 ──────────────────────────────────────────────────

    private void UpdateLayer(Func<Layer, Layer> change, bool skipRebuild = false)
    {
        if (_selected is null)
        {
            return;
        }

        Layer updated = change(_selected);
        ReplaceLayer(updated, pushUndo: true, skipRebuild);
    }

    private void ReplaceLayer(Layer updated, bool pushUndo, bool rebuildProperties = true)
    {
        var list = _theme.Layers.ToList();
        int index = list.FindIndex(l => l.Id == _selected!.Id);
        if (index < 0)
        {
            return;
        }

        if (pushUndo)
        {
            PushUndo();
        }

        list[index] = updated;
        _theme = _theme with { Layers = list };
        _selected = updated;

        _syncing = true;
        RefreshLayerList();
        _syncing = false;

        UpdateSelectionBox();
        RenderCanvas();
        CommitToDisplay();

        if (rebuildProperties)
        {
            BuildProperties(updated);
        }
    }

    private void CommitToDisplay() => _display.UpdateTheme(_theme);

    private void SyncFromTheme()
    {
        RefreshLayerList();
        UpdateSelectionBox();
        RenderCanvas();
        CommitToDisplay();
        BuildProperties(_selected);
        EditorStatus.Text = $"图层 {_theme.Layers.Count} 个 · 未保存的修改";
    }

    private void PushUndo()
    {
        _undo.Push(ThemeJson.Serialize(_theme));
        _redo.Clear();
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(ThemeJson.Serialize(_theme));
        LoadFromJson(_undo.Pop());
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(ThemeJson.Serialize(_theme));
        LoadFromJson(_redo.Pop());
    }

    private void LoadFromJson(string json)
    {
        _theme = ThemeJson.Deserialize(json);
        _selected = _theme.Layers.FirstOrDefault();
        RefreshLayerList();
        UpdateSelectionBox();
        RenderCanvas();
        CommitToDisplay();
        BuildProperties(_selected);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _themes.Save(_theme);
        EditorStatus.Text = $"已保存到用户主题：{_themes.PathFor(_theme)}";
        Log.Write($"editor saved {_themes.PathFor(_theme)}");
    }

    /// <summary>Rebuilds the theme picker after the catalog changed.</summary>
    public void RefreshThemes()
    {
        _syncing = true;
        ThemePicker.Items.Clear();
        foreach (ThemeDocument theme in _themes.Themes)
        {
            ThemePicker.Items.Add(theme);
        }

        ThemePicker.SelectedItem = _themes.Find(_theme.Id) ?? _theme;
        _syncing = false;
    }

    private void OnNewTheme(object sender, RoutedEventArgs e)
    {
        string? name = TextInputDialog.Show(
            Window.GetWindow(this), "新建主题", "主题名称", "我的主题");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var theme = new ThemeDocument
        {
            Id = _themes.NewId(name),
            Name = name.Trim(),
            Canvas = new ThemeCanvas
            {
                Background = new ThemeBackground
                {
                    Kind = BackgroundKind.Solid,
                    Colors = new[] { "#0B1020" },
                },
            },
        };

        _themes.Save(theme);
        _themes.Reload();
        _themesChanged?.Invoke();
        LoadTheme(theme);
        EditorStatus.Text = $"已创建：{theme.Name}（{_themes.PathFor(theme)}）";
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        string? name = TextInputDialog.Show(
            Window.GetWindow(this), "另存为主题", "主题名称", _theme.Name + " 副本");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        ThemeDocument copy = _theme with
        {
            Id = _themes.NewId(name),
            Name = name.Trim(),
        };

        _themes.Save(copy);
        _themes.Reload();
        _themesChanged?.Invoke();
        LoadTheme(copy);
        EditorStatus.Text = $"已另存为：{copy.Name}";
    }

    private void OnDeleteTheme(object sender, RoutedEventArgs e)
    {
        if (!_themes.IsUserTheme(_theme))
        {
            MessageBox.Show(
                "内置主题不能删除；可以「另存为」后修改自己的副本。",
                "OpenTRCC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"删除主题「{_theme.Name}」？此操作会删掉用户主题目录里的 JSON。",
            "OpenTRCC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _themes.Delete(_theme);
        _themes.Reload();
        _themesChanged?.Invoke();
        LoadTheme(_themes.Default);
        EditorStatus.Text = "主题已删除";
    }

    // ── 渲染 ────────────────────────────────────────────────────────

    private void RenderCanvas()
    {
        if (_canvasBitmap is null)
        {
            return;
        }

        SensorSnapshot snapshot = _display.CurrentSnapshot;
        if (snapshot.Values.Count == 0)
        {
            snapshot = SensorSnapshot.Sample();
        }

        using var bitmap = _renderer.Render(_theme, snapshot, TimeSpan.Zero);
        BgraImage bgra = TestPatternRenderer.ToBgra(bitmap);
        _canvasBitmap.WritePixels(new Int32Rect(0, 0, 320, 240), bgra.Pixels, 320 * 4, 0);
    }

    private void UpdateSelectionBox()
    {
        if (_selected is null)
        {
            SelectionBox.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionBox.Visibility = Visibility.Visible;
        SelectionBox.Width = _selected.W * Zoom;
        SelectionBox.Height = _selected.H * Zoom;
        Canvas.SetLeft(SelectionBox, _selected.X * Zoom);
        Canvas.SetTop(SelectionBox, _selected.Y * Zoom);
    }

    private static double ParseNumber(string text, double fallback)
        => double.TryParse(text, out double value) ? value : fallback;
}
