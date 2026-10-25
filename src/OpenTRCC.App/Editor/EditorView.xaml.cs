using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private bool _syncing;
    private TextBox? _bindTarget;
    private Action<string>? _bindApply;
    private IBackgroundFrameProvider? _backgroundFrames;
    private IDisposable? _backgroundDisposable;

    public EditorView(DisplayService display, ThemeLibrary themes)
    {
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

        _syncing = true;
        ThemePicker.SelectedItem = _themes.Find(theme.Id) ?? theme;
        _syncing = false;

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
            }));

        AddChoice(
            "动态预设",
            ProceduralBackgrounds.Presets,
            background.Preset,
            value => SetBackground(b => b with { Kind = BackgroundKind.Procedural, Preset = value }));

        AddField(
            "动态速度",
            background.Speed.ToString("0.##"),
            value => SetBackground(b => b with { Speed = Math.Clamp(ParseNumber(value, b.Speed), 0.05, 5) }));

        AddField(
            "颜色 1",
            background.Colors.FirstOrDefault() ?? "#0B1020",
            value => SetBackground(b => b with { Colors = WithColor(b.Colors, 0, value) }));

        AddField(
            "颜色 2（渐变）",
            background.Colors.Skip(1).FirstOrDefault() ?? "#1B2A4A",
            value => SetBackground(b => b with { Colors = WithColor(b.Colors, 1, value) }));

        AddField(
            "渐变角度",
            background.Angle.ToString("0"),
            value => SetBackground(b => b with { Angle = ParseNumber(value, b.Angle) }));

        AddField(
            "图片 / 动图 / 视频路径",
            background.Asset ?? string.Empty,
            value => SetBackground(b => b with { Kind = BackgroundKind.Image, Asset = value }));

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

            SetBackground(b => b with { Kind = BackgroundKind.Image, Asset = dialog.FileName });
        };

        PropsPanel.Children.Add(pick);

        AddChoice(
            "适应",
            new[] { "cover", "contain", "stretch" },
            background.Fit,
            value => SetBackground(b => b with { Fit = value }));

        AddField(
            "暗化 (0-1)",
            background.Dim.ToString("0.##"),
            value => SetBackground(b => b with { Dim = Math.Clamp(ParseNumber(value, b.Dim), 0, 1) }));
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
            SetBackground(b => b with { Kind = BackgroundKind.Image, Asset = meta });
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
        double newX = Math.Round(_dragOriginX + (point.X - _dragStart.X) / Zoom);
        double newY = Math.Round(_dragOriginY + (point.Y - _dragStart.Y) / Zoom);

        ReplaceLayer(_selected with { X = newX, Y = newY }, pushUndo: false, rebuildProperties: false);
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
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

        AddField("X", layer.X.ToString("0"), value => UpdateLayer(l => l with { X = ParseNumber(value, l.X) }));
        AddField("Y", layer.Y.ToString("0"), value => UpdateLayer(l => l with { Y = ParseNumber(value, l.Y) }));
        AddField("宽", layer.W.ToString("0"), value => UpdateLayer(l => l with { W = Math.Max(1, ParseNumber(value, l.W)) }));
        AddField("高", layer.H.ToString("0"), value => UpdateLayer(l => l with { H = Math.Max(1, ParseNumber(value, l.H)) }));
        AddField("不透明度", layer.Opacity.ToString("0.##"), value => UpdateLayer(l => l with { Opacity = Math.Clamp(ParseNumber(value, l.Opacity), 0, 1) }));

        switch (layer)
        {
            case TextLayer text:
                AddField("文本", text.Text, value => UpdateLayer(l => l is TextLayer t ? (Layer)(t with { Text = value }) : l), isBinding: true);
                AddField("字号", text.Font.Size.ToString("0"), value => UpdateLayer(l => l is TextLayer t ? (Layer)(t with { Font = t.Font with { Size = Math.Max(4, ParseNumber(value, t.Font.Size)) } }) : l));
                AddField("颜色", text.Color, value => UpdateLayer(l => l is TextLayer t ? (Layer)(t with { Color = value }) : l));
                AddChoice("对齐", new[] { "left", "center", "right" }, text.Align, value => UpdateLayer(l => l is TextLayer t ? (Layer)(t with { Align = value }) : l));
                AddChoice("垂直", new[] { "top", "middle", "bottom" }, text.VerticalAlign, value => UpdateLayer(l => l is TextLayer t ? (Layer)(t with { VerticalAlign = value }) : l));
                break;

            case BarLayer bar:
                AddField("值", bar.Value, value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Value = value }) : l), isBinding: true);
                AddField("最小", bar.Min.ToString("0.##"), value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Min = ParseNumber(value, b.Min) }) : l));
                AddField("最大", bar.Max.ToString("0.##"), value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Max = ParseNumber(value, b.Max) }) : l));
                AddField("填充色", bar.Fill, value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Fill = value }) : l));
                AddField("轨道色", bar.Track, value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Track = value }) : l));
                AddField("圆角", bar.Radius.ToString("0"), value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Radius = Math.Max(0, ParseNumber(value, b.Radius)) }) : l));
                AddChoice("方向", new[] { "horizontal", "vertical" }, bar.Orientation, value => UpdateLayer(l => l is BarLayer b ? (Layer)(b with { Orientation = value }) : l));
                break;

            case RingLayer ring:
                AddField("值", ring.Value, value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Value = value }) : l), isBinding: true);
                AddField("最小", ring.Min.ToString("0.##"), value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Min = ParseNumber(value, r.Min) }) : l));
                AddField("最大", ring.Max.ToString("0.##"), value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Max = ParseNumber(value, r.Max) }) : l));
                AddField("粗细", ring.Thickness.ToString("0"), value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Thickness = Math.Max(1, ParseNumber(value, r.Thickness)) }) : l));
                AddField("颜色", ring.Fill.FirstOrDefault() ?? "#5EEAD4", value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Fill = new[] { value } }) : l));
                AddField("轨道色", ring.Track, value => UpdateLayer(l => l is RingLayer r ? (Layer)(r with { Track = value }) : l));
                break;

            case SparklineLayer spark:
                AddField("来源", spark.Source, value => UpdateLayer(l => l is SparklineLayer s ? (Layer)(s with { Source = value }) : l), isBinding: true);
                AddField("采样数", spark.Samples.ToString(), value => UpdateLayer(l => l is SparklineLayer s ? (Layer)(s with { Samples = (int)Math.Max(2, ParseNumber(value, s.Samples)) }) : l));
                AddField("最小", spark.Min.ToString("0.##"), value => UpdateLayer(l => l is SparklineLayer s ? (Layer)(s with { Min = ParseNumber(value, s.Min) }) : l));
                AddField("最大", spark.Max.ToString("0.##"), value => UpdateLayer(l => l is SparklineLayer s ? (Layer)(s with { Max = ParseNumber(value, s.Max) }) : l));
                AddField("颜色", spark.Stroke, value => UpdateLayer(l => l is SparklineLayer s ? (Layer)(s with { Stroke = value }) : l));
                break;

            case ImageLayer image:
                AddField("资源", image.Asset, value => UpdateLayer(l => l is ImageLayer i ? (Layer)(i with { Asset = value }) : l));
                AddChoice("适应", new[] { "contain", "cover", "stretch" }, image.Fit, value => UpdateLayer(l => l is ImageLayer i ? (Layer)(i with { Fit = value }) : l));
                break;
        }

        AddBindingPicker();
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

    private void AddChoice(string label, string[] items, string current, Action<string> apply)
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
            combo.Items.Add(item);
        }

        combo.SelectedItem = items.Contains(current) ? current : items.FirstOrDefault();
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string selected)
            {
                apply(selected);
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
        string path = Path.Combine(_themes.Directory, _theme.Id + ".json");
        ThemeJson.Save(path, _theme);
        EditorStatus.Text = $"已保存：{path}";
        Log.Write($"editor saved {path}");
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
