using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTRCC.Core.Themes;

namespace OpenTRCC.App.Services;

/// <summary>Loads the built-in theme JSON files shipped next to the executable.</summary>
public sealed class ThemeLibrary
{
    public ThemeLibrary(string? directory = null)
    {
        Directory = directory ?? ResolveDirectory();
        var themes = new List<ThemeDocument>();
        var errors = new List<string>();

        if (System.IO.Directory.Exists(Directory))
        {
            foreach (string file in System.IO.Directory.GetFiles(Directory, "*.json").OrderBy(f => f))
            {
                try
                {
                    themes.Add(ThemeJson.Load(file));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
                {
                    errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        Themes = themes;
        Errors = errors;
    }

    public string Directory { get; }

    public IReadOnlyList<ThemeDocument> Themes { get; }

    public IReadOnlyList<string> Errors { get; }

    public ThemeDocument? Find(string? id)
        => string.IsNullOrEmpty(id) ? null : Themes.FirstOrDefault(t => t.Id == id);

    public ThemeDocument Default => Themes.FirstOrDefault() ?? new ThemeDocument();

    private static string ResolveDirectory()
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
