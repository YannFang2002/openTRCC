namespace OpenTRCC.Core.Data;

/// <summary>Metadata for one sensor id: what it is, where it came from, how to label it.</summary>
public sealed record SensorDescriptor(
    string Id,
    string Name,
    string Unit,
    string Source,
    string Category);
