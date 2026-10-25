using System.Text;

namespace OpenTRCC.Core.Diagnostics;

/// <summary>Classic hex + ASCII dump, used for handshake captures and frame headers.</summary>
public static class HexDump
{
    public static string Format(ReadOnlySpan<byte> data, int bytesPerLine = 16, int baseOffset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerLine);

        var builder = new StringBuilder(data.Length * 4 + 32);
        for (int lineStart = 0; lineStart < data.Length; lineStart += bytesPerLine)
        {
            int count = Math.Min(bytesPerLine, data.Length - lineStart);
            builder.Append((baseOffset + lineStart).ToString("X8")).Append("  ");

            for (int i = 0; i < bytesPerLine; i++)
            {
                if (i < count)
                {
                    builder.Append(data[lineStart + i].ToString("X2")).Append(' ');
                }
                else
                {
                    builder.Append("   ");
                }

                if (i == bytesPerLine / 2 - 1)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(' ').Append('|');
            for (int i = 0; i < count; i++)
            {
                byte value = data[lineStart + i];
                builder.Append(value is >= 0x20 and < 0x7F ? (char)value : '.');
            }

            builder.Append('|').AppendLine();
        }

        return builder.ToString();
    }
}
