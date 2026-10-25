using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenTRCC.App.Services;

/// <summary>Append-only diagnostics log with an in-memory tail for the Logs page.</summary>
public static class Log
{
    private const int BufferLines = 1000;

    private static readonly object Gate = new();
    private static readonly LinkedList<string> Buffer = new();
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTRCC",
        "app.log");

    public static string FilePath => Path;

    public static event Action<string>? Written;

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";

        lock (Gate)
        {
            Buffer.AddLast(line);
            while (Buffer.Count > BufferLines)
            {
                Buffer.RemoveFirst();
            }
        }

        try
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(Path, line + Environment.NewLine);
        }
        catch (IOException)
        {
        }

        Written?.Invoke(line);
    }

    public static IReadOnlyList<string> Tail(int count)
    {
        lock (Gate)
        {
            int skip = Math.Max(0, Buffer.Count - count);
            return new List<string>(Buffer).GetRange(skip, Math.Min(count, Buffer.Count));
        }
    }

    public static string TailText(int count)
    {
        var builder = new StringBuilder();
        foreach (string line in Tail(count))
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }
}
