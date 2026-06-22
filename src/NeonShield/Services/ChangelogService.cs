using System.Reflection;
using System.Text;

namespace NeonShield.Services;

public static class ChangelogService
{
    private const string ResourceName = "NeonShield.CHANGELOG.md";

    public static string LoadDisplayText()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return "Der Änderungsverlauf ist in diesem Build nicht verfügbar.";
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return FormatMarkdown(reader.ReadToEnd());
        }
        catch
        {
            return "Der Änderungsverlauf konnte nicht geladen werden.";
        }
    }

    private static string FormatMarkdown(string markdown)
    {
        var output = new StringBuilder();
        foreach (var rawLine in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                if (output.Length > 0 && !output.ToString().EndsWith("\n\n", StringComparison.Ordinal))
                {
                    output.AppendLine();
                }

                continue;
            }

            if (line.Equals("# Changelog", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Alle wichtigen Änderungen", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (output.Length > 0)
                {
                    output.AppendLine();
                }

                output.AppendLine(line[3..]);
                output.AppendLine(new string('─', Math.Min(line.Length - 3, 42)));
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                output.AppendLine($"{line[4..]}:");
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                output.AppendLine($"  • {line[2..].Replace("`", string.Empty, StringComparison.Ordinal)}");
                continue;
            }

            output.AppendLine(line.Replace("`", string.Empty, StringComparison.Ordinal));
        }

        return output.ToString().Trim();
    }
}
