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
        UserDirectory = userDirectory ?? ResolveWritableDirectory(BundledDirectory);

        System.IO.Directory.CreateDirectory(UserDirectory);
        MigrateLegacyThemes();
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
        if (!string.Equals(
                Path.GetFullPath(BundledDirectory),
                Path.GetFullPath(UserDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            LoadFrom(UserDirectory, themes, byId, errors);
        }

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

    /// <summary>
    /// Prefer the install directory next to the executable (portable, keeps
    /// imports on the same drive); fall back to LocalAppData when it is read-only.
    /// </summary>
    private static string ResolveWritableDirectory(string bundled)
    {
        try
        {
            string probe = Path.Combine(bundled, ".write-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return bundled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenTRCC",
                "themes");
        }
    }

    /// <summary>Copies themes/assets created by older versions that lived in LocalAppData.</summary>
    private void MigrateLegacyThemes()
    {
        string legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenTRCC",
            "themes");

        if (!System.IO.Directory.Exists(legacy)
            || string.Equals(Path.GetFullPath(legacy), Path.GetFullPath(UserDirectory), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (string file in System.IO.Directory.GetFiles(legacy, "*.json"))
        {
            string target = Path.Combine(UserDirectory, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }

        string legacyAssets = Path.Combine(legacy, "assets");
        string targetAssets = Path.Combine(UserDirectory, "assets");
        if (System.IO.Directory.Exists(legacyAssets) && !System.IO.Directory.Exists(targetAssets))
        {
            CopyDirectory(legacyAssets, targetAssets);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        System.IO.Directory.CreateDirectory(target);
        foreach (string file in System.IO.Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (string directory in System.IO.Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
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
