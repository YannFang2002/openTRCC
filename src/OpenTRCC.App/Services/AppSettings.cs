using System;
using System.IO;
using System.Text.Json;

namespace OpenTRCC.App.Services;

/// <summary>Small persisted settings file (%LocalAppData%\OpenTRCC\settings.json).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string? ThemeId { get; set; }

    public bool AutoStart { get; set; }

    public bool StartStreamingOnLaunch { get; set; } = true;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTRCC",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options)
                       ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (IOException)
        {
        }
    }
}
