using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTRCC.Core.Themes;

namespace OpenTRCC.App.Services;

/// <summary>
/// Theme catalog: bundled JSON next to the executable plus user themes in
/// %LocalAppData%\OpenTRCC\themes. User files override a bundled theme with the
/// same id and are the only writable location, so edits survive updates.
/// </summary>
public sealed class ThemeLibrary
{
    public ThemeLibrary(string? bundledDirectory = null, string? userDirectory = null)
    {
        BundledDirectory = bundledDirectory ?? ResolveBundledDirectory();
        UserDirectory = userDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenTRCC",
            "themes");

        System.IO.Directory.CreateDirectory(UserDirectory);
        Reload();
    }

    /// <summary>Read-only themes shipped with the app.</summary>
    public string BundledDirectory { get; }

    /// <summary>Writable themes and imported media.</summary>
    public string UserDirectory { get; }

    /// <summary>Shorthand used by callers that need the writable theme root.</summary>
    public string Directory => UserDirectory;

    public IReadOnlyList<ThemeDocument> Themes { get; private set; } = Array.Empty<ThemeDocument>();

    public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();

    public void Reload()
    {
        var themes = new List<ThemeDocument>();
        var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        LoadFrom(BundledDirectory, themes, byId, errors);
        LoadFrom(UserDirectory, themes, byId, errors);

        Themes = themes;
        Errors = errors;
    }

    public ThemeDocument? Find(string? id)
        => string.IsNullOrEmpty(id) ? null : Themes.FirstOrDefault(t => t.Id == id);

    public ThemeDocument Default => Themes.FirstOrDefault() ?? new ThemeDocument();

    public bool IsUserTheme(ThemeDocument theme)
        => File.Exists(Path.Combine(UserDirectory, theme.Id + ".json"));

    public string PathFor(ThemeDocument theme) => Path.Combine(UserDirectory, theme.Id + ".json");

    public void Save(ThemeDocument theme)
    {
        ThemeJson.Save(PathFor(theme), theme);
    }

    public bool Delete(ThemeDocument theme)
    {
        string path = PathFor(theme);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>Builds a unique, filesystem-safe id for a new theme name.</summary>
    public string NewId(string name)
    {
        var slug = new string(name
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');

        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        if (slug.Length == 0)
        {
            slug = "theme";
        }

        string baseId = $"{slug}-320x240";
        string id = baseId;
        int suffix = 2;
        while (Themes.Any(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            id = $"{baseId}-{suffix}";
            suffix++;
        }

        return id;
    }

    private static void LoadFrom(
        string directory,
        List<ThemeDocument> themes,
        Dictionary<string, int> byId,
        List<string> errors)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in System.IO.Directory.GetFiles(directory, "*.json").OrderBy(f => f))
        {
            try
            {
                ThemeDocument theme = ThemeJson.Load(file);
                if (byId.TryGetValue(theme.Id, out int existing))
                {
                    // Later directories (user) replace earlier (bundled) entries.
                    themes[existing] = theme;
                }
                else
                {
                    byId[theme.Id] = themes.Count;
                    themes.Add(theme);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    private static string ResolveBundledDirectory()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "themes");
        if (System.IO.Directory.Exists(local))
        {
            return local;
        }

        string working = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "themes");
        return System.IO.Directory.Exists(working) ? working : local;
    }
}
