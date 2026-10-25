using System;
using System.IO;
using Microsoft.Win32;

namespace OpenTRCC.App.Services;

/// <summary>
/// HKCU Run autostart: no UAC prompt, no admin. (A scheduled task with highest
/// privileges can come later for full LHM sensor access.)
/// </summary>
public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenTRCC";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "OpenTRCC.exe");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
