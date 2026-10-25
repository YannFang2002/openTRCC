namespace OpenTRCC.Probe;

/// <summary>Minimal `--name value` / `--name=value` / `--flag` parser. No external CLI dependency.</summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positionals = new();

    private CommandLine()
    {
    }

    public IReadOnlyList<string> Positionals => _positionals;

    public static CommandLine Parse(string[] args)
    {
        var result = new CommandLine();

        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                result._positionals.Add(token);
                continue;
            }

            string name = token[2..];
            string? value = null;
            int equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            if (name.Length == 0)
            {
                throw new ArgumentException($"Empty option name in '{token}'.");
            }

            result._options[name] = value;
        }

        return result;
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.TryGetValue(name, out string? value) ? value : null;

    public int GetInt(string name, int fallback)
        => int.TryParse(Get(name), out int value) ? value : fallback;

    public double GetDouble(string name, double fallback)
        => double.TryParse(Get(name), out double value) ? value : fallback;
}
