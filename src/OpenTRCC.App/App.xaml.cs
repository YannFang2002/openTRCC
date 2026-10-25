using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using OpenTRCC.App.Services;

namespace OpenTRCC.App;

public partial class App : Application
{
    private static readonly nint PerMonitorV2 = new(-4);
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // WPF can't declare DPI awareness without the manifest (the WinForms
        // analyzer owns that there), so set it before any window exists.
        SetProcessDpiAwarenessContext(PerMonitorV2);

        // Diagnostics/support path: pre-extract a video without opening the UI.
        if (TryRunVideoImport())
        {
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(initiallyOwned: true, "OpenTRCC.SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("OpenTRCC 已经在运行。", "OpenTRCC", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        base.OnStartup(e);

        // Brand accent instead of the system blue.
        try
        {
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
                System.Windows.Media.Color.FromRgb(0x2D, 0xD4, 0xBF));
        }
        catch (Exception)
        {
            // Older/newer WPF-UI builds may not expose the manager; system accent is fine.
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    /// <summary>Supports <c>OpenTRCC.exe --import-video &lt;file&gt;</c> for batch imports and tests.</summary>
    private static bool TryRunVideoImport()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.FindIndex(args, a => string.Equals(a, "--import-video", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
        {
            return false;
        }

        string video = args[index + 1];
        string themes = Path.Combine(AppContext.BaseDirectory, "themes");

        try
        {
            string meta = VideoFrameExtractor.Extract(video, themes);
            Log.Write($"import-video ok: {meta}");
        }
        catch (Exception ex)
        {
            Log.Write($"import-video failed: {ex.GetType().Name}: {ex.Message}");
        }

        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"发生未处理的异常：\n{e.Exception.Message}",
            "OpenTRCC",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
