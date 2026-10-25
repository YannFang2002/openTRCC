using OpenTRCC.Devices;
using Xunit;

namespace OpenTRCC.Devices.Tests;

public sealed class DeviceProfileStoreTests : IDisposable
{
    private readonly string _directory;

    public DeviceProfileStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "opentrcc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Save_Then_Load_Roundtrips()
    {
        var store = new DeviceProfileStore(Path.Combine(_directory, "device-profile.json"));
        DeviceProfile profile = DeviceCatalog.ResolveOrDefault(50, 1) with
        {
            SerialNumber = "SN123",
            LineRotation = 90,
            ChunkSize = 512,
            ConfirmedAt = DateTimeOffset.Parse("2026-10-24T20:00:00+08:00"),
        };

        store.Save(profile);
        DeviceProfile? loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(profile, loaded);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Save_Overwrites_Existing_File()
    {
        var store = new DeviceProfileStore(Path.Combine(_directory, "device-profile.json"));
        store.Save(DeviceCatalog.ResolveOrDefault(50, 1));
        store.Save(DeviceCatalog.ResolveOrDefault(49, 2));

        Assert.Equal(49, store.Load()!.ProductId);
    }

    [Fact]
    public void Load_Returns_Null_For_Missing_File()
    {
        var store = new DeviceProfileStore(Path.Combine(_directory, "nope.json"));
        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_Returns_Null_For_Corrupt_Json()
    {
        string path = Path.Combine(_directory, "corrupt.json");
        File.WriteAllText(path, "{ not json");

        Assert.Null(new DeviceProfileStore(path).Load());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
