using System.Windows;
using System.Windows.Controls;

namespace OpenTRCC.App.Services;

/// <summary>Minimal text prompt used for "new theme" / "save as".</summary>
public static class TextInputDialog
{
    public static string? Show(Window? owner, string title, string label, string initial = "")
    {
        var window = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x0B, 0x12, 0x20)),
            Owner = owner,
        };

        var text = new TextBox
        {
            Text = initial,
            Margin = new Thickness(0, 0, 0, 14),
        };

        var ok = new Button
        {
            Content = "确定",
            Width = 88,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var cancel = new Button
        {
            Content = "取消",
            Width = 88,
            IsCancel = true,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(text);
        panel.Children.Add(buttons);

        window.Content = panel;

        string? result = null;
        ok.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(text.Text))
            {
                result = text.Text.Trim();
                window.Close();
            }
        };

        text.Focus();
        text.SelectAll();
        window.ShowDialog();
        return result;
    }
}
