using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace OpenTRCC.Core.Themes;

/// <summary>Theme JSON serialization and colour parsing.</summary>
public static class ThemeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static ThemeDocument Load(string path)
    {
        string json = File.ReadAllText(path);
        ThemeDocument theme = Deserialize(json);
        return theme with { Id = string.IsNullOrEmpty(theme.Id) ? Path.GetFileNameWithoutExtension(path) : theme.Id };
    }

    public static void Save(string path, ThemeDocument theme)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(theme));
    }

    public static ThemeDocument Deserialize(string json)
    {
        ThemeDocument? theme = JsonSerializer.Deserialize<ThemeDocument>(json, Options);
        return theme ?? throw new InvalidDataException("Theme JSON deserialised to null.");
    }

    public static string Serialize(ThemeDocument theme) => JsonSerializer.Serialize(theme, Options);
}

/// <summary>Parses theme colour strings (#RGB, #RRGGBB, #AARRGGBB, a few names).</summary>
public static class ColorSpec
{
    private static readonly Dictionary<string, SKColor> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["transparent"] = SKColors.Transparent,
        ["black"] = SKColors.Black,
        ["white"] = SKColors.White,
        ["red"] = new SKColor(255, 0, 0),
        ["green"] = new SKColor(0, 200, 0),
        ["blue"] = new SKColor(0, 0, 255),
        ["gray"] = SKColors.Gray,
        ["grey"] = SKColors.Gray,
    };

    public static SKColor Parse(string? text, SKColor fallback = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback == default ? SKColors.White : fallback;
        }

        string value = text.Trim();
        if (Named.TryGetValue(value, out SKColor named))
        {
            return named;
        }

        if (!value.StartsWith('#'))
        {
            return fallback == default ? SKColors.White : fallback;
        }

        string hex = value[1..];
        try
        {
            return hex.Length switch
            {
                3 => new SKColor(
                    (byte)(Convert.ToByte(hex[..1] + hex[..1], 16)),
                    (byte)(Convert.ToByte(hex[1..2] + hex[1..2], 16)),
                    (byte)(Convert.ToByte(hex[2..3] + hex[2..3], 16))),
                6 => new SKColor(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16)),
                8 => new SKColor(
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    Convert.ToByte(hex[6..8], 16),
                    Convert.ToByte(hex[..2], 16)),
                _ => SKColors.White,
            };
        }
        catch (FormatException)
        {
            return fallback == default ? SKColors.White : fallback;
        }
    }
}
