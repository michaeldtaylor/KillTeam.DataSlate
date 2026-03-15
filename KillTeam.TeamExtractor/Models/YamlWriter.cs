using System.Text;

namespace KillTeam.TeamExtractor.Models;

/// <summary>
/// Builds indented YAML strings for team output. Provides full control over block scalars,
/// field ordering, and quoting — avoiding YamlDotNet serialiser surprises.
/// </summary>
internal static class YamlWriter
{
    // YAML plain scalars that must be quoted to avoid misinterpretation
    private static readonly HashSet<string> ReservedScalars = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "null", "yes", "no", "on", "off", "~",
    };

    /// <summary>Returns a YAML-safe scalar representation of <paramref name="s"/>.</summary>
    internal static string Scalar(string s)
    {
        if (s.Length == 0)
        {
            return "''";
        }

        if (ReservedScalars.Contains(s))
        {
            return SingleQuote(s);
        }

        // Must quote if value starts with special YAML characters
        if (s[0] is '{' or '[' or '!' or '&' or '*' or '#' or '|' or '>' or '\'' or '"'
                       or '%' or '@' or '`' or '-' or '?' or ':')
        {
            return SingleQuote(s);
        }

        // Must quote if value contains ': ' (would be read as a mapping entry)
        if (s.Contains(": "))
        {
            return SingleQuote(s);
        }

        // Must quote if value contains '#' (comment start after whitespace)
        if (s.Contains(" #"))
        {
            return SingleQuote(s);
        }

        // Must quote if value ends with ':'
        if (s.EndsWith(':'))
        {
            return SingleQuote(s);
        }

        // Must quote if value looks purely numeric (int or float)
        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return SingleQuote(s);
        }

        return s;
    }

    /// <summary>Writes a simple key: scalar-value line.</summary>
    internal static void WriteKeyValue(StringBuilder sb, int indent, string key, string value)
    {
        sb.Append(' ', indent);
        sb.Append(key);
        sb.Append(": ");
        sb.AppendLine(Scalar(value));
    }

    /// <summary>Writes a key: integer-value line (no quoting needed for integers).</summary>
    internal static void WriteKeyInt(StringBuilder sb, int indent, string key, int value)
    {
        sb.Append(' ', indent);
        sb.Append(key);
        sb.Append(": ");
        sb.AppendLine(value.ToString());
    }

    /// <summary>
    /// Writes a key with a literal block scalar value using <c>|-</c> chomping
    /// (strips all trailing newlines so round-trip is exact).
    /// </summary>
    internal static void WriteLiteralBlock(StringBuilder sb, int indent, string key, string text)
    {
        var innerIndent = new string(' ', indent + 2);
        sb.Append(' ', indent);
        sb.Append(key);
        sb.AppendLine(": |-");

        foreach (var line in text.Split('\n'))
        {
            sb.Append(innerIndent);
            sb.AppendLine(line.TrimEnd());
        }
    }

    /// <summary>
    /// Writes a text field. Uses a literal block scalar when the value contains newlines;
    /// otherwise uses an inline scalar.
    /// </summary>
    internal static void WriteTextField(StringBuilder sb, int indent, string key, string text)
    {
        if (text.Contains('\n'))
        {
            WriteLiteralBlock(sb, indent, key, text);
        }
        else
        {
            WriteKeyValue(sb, indent, key, text);
        }
    }

    private static string SingleQuote(string s) => "'" + s.Replace("'", "''") + "'";
}
