using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenTRCC.App.Services;

/// <summary>Small progress window shown while a video is converted to frames.</summary>
public sealed class VideoImportWindow : Window
{
    private readonly ProgressBar _bar;
    private readonly TextBlock _text;

    public VideoImportWindow()
    {
        Title = "导入视频";
        Width = 380;
        Height = 132;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20));

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            Margin = new Thickness(16, 16, 16, 8),
            Text = "正在提取视频帧…",
        };

        _bar = new ProgressBar
        {
            Height = 6,
            Margin = new Thickness(16, 0, 16, 0),
            Minimum = 0,
            Maximum = 1,
        };

        Content = new StackPanel { Children = { _text, _bar } };
    }

    public void Report(double value)
    {
        _bar.Value = value;
        _text.Text = $"正在提取视频帧… {value:P0}";
    }
}
