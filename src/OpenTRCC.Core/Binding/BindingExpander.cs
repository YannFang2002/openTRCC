using System.Text;
using OpenTRCC.Core.Data;

namespace OpenTRCC.Core.Binding;

/// <summary>
/// Expands <c>{sensor.id}</c> / <c>{sensor.id:0.0}</c> / <c>{time:HH:mm}</c> tokens.
/// Unknown or unavailable sensors render as <c>--</c> instead of throwing.
/// </summary>
public static class BindingExpander
{
    public static string Expand(string template, SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (template.IndexOf('{') < 0)
        {
            return template;
        }

        var builder = new StringBuilder(template.Length + 16);
        int index = 0;

        while (index < template.Length)
        {
            char current = template[index];

            if (current == '{' && index + 1 < template.Length && template[index + 1] == '{')
            {
                builder.Append('{');
                index += 2;
                continue;
            }

            if (current == '}' && index + 1 < template.Length && template[index + 1] == '}')
            {
                builder.Append('}');
                index += 2;
                continue;
            }

            if (current != '{')
            {
                builder.Append(current);
                index++;
                continue;
            }

            int close = template.IndexOf('}', index + 1);
            if (close < 0)
            {
                builder.Append(template.AsSpan(index));
                break;
            }

            string token = template[(index + 1)..close];
            builder.Append(Resolve(token, snapshot));
            index = close + 1;
        }

        return builder.ToString();
    }

    public static string Resolve(string token, SensorSnapshot snapshot)
    {
        int colon = token.IndexOf(':');
        string name = colon >= 0 ? token[..colon] : token;
        string format = colon >= 0 ? token[(colon + 1)..] : string.Empty;
        name = name.Trim();

        if (name.Equals("time", StringComparison.OrdinalIgnoreCase)
            || name.Equals("date", StringComparison.OrdinalIgnoreCase))
        {
            DateTimeOffset local = snapshot.Timestamp.ToLocalTime();
            string effective = string.IsNullOrEmpty(format)
                ? (name.Equals("time", StringComparison.OrdinalIgnoreCase) ? "HH:mm" : "yyyy-MM-dd")
                : format;

            try
            {
                return local.ToString(effective, System.Globalization.CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                return local.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);
            }
        }

        SensorValue value = snapshot.Find(name);
        return value.IsMissing ? "\u2014" : value.Display(format);
    }

    /// <summary>Numeric value of a token such as <c>{fps.current}</c>, or null when missing.</summary>
    public static double? ResolveNumber(string tokenOrLiteral, SensorSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(tokenOrLiteral))
        {
            return null;
        }

        string text = tokenOrLiteral.Trim();
        if (text.StartsWith('{') && text.EndsWith('}'))
        {
            text = Resolve(text[1..^1], snapshot).Trim();
        }

        return double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }
}
