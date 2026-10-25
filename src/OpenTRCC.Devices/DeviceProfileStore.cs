using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTRCC.Devices;

/// <summary>Loads/saves <see cref="DeviceProfile"/> as JSON with atomic replacement.</summary>
public sealed class DeviceProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public DeviceProfileStore(string? path = null)
    {
        FilePath = path ?? DefaultPath;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTRCC",
        "device-profile.json");

    public string FilePath { get; }

    /// <summary>Returns null when the file is absent or unreadable; never throws on bad JSON.</summary>
    public DeviceProfile? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<DeviceProfile>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Writes to a temp file next to the target, then swaps it in.</summary>
    public void Save(DeviceProfile profile)
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = FilePath + ".tmp";
        string json = JsonSerializer.Serialize(profile, SerializerOptions);
        File.WriteAllText(temp, json);

        if (File.Exists(FilePath))
        {
            File.Replace(temp, FilePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, FilePath);
        }
    }
}
