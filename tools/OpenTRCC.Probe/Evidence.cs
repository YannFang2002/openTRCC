namespace OpenTRCC.Probe;

/// <summary>Directory holding the artifacts of one probe run.</summary>
internal sealed class Evidence
{
    private Evidence(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public static Evidence Create(CommandLine line)
    {
        string root = line.Get("out")
            ?? Path.Combine("artifacts", "probe-runs", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        string full = Path.GetFullPath(root);
        Directory.CreateDirectory(full);
        return new Evidence(full);
    }

    public string PathFor(string fileName) => Path.Combine(Root, fileName);

    public void WriteText(string fileName, string text) => File.WriteAllText(PathFor(fileName), text);

    public void WriteBytes(string fileName, byte[] bytes) => File.WriteAllBytes(PathFor(fileName), bytes);
}
