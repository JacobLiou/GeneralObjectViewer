using System.Globalization;
using System.Text;
using ObjectViewer.Core.Metadata;

namespace ObjectViewer.Core.Formatting;

/// <summary>
/// Fallback formatter for well-known atomic types. Unknown objects are shown as
/// {TypeName} — ToString() is never called automatically (Proposal §14.3).
/// </summary>
public sealed class DefaultValueFormatter : IObjectValueFormatter
{
    public int Priority => 0;
    public string Name => "Default";

    public bool CanFormat(Type? declaredType, Type? runtimeType, object? value)
        => value is not null;

    public ValueDisplay Format(Type? declaredType, object? value, ValueFormattingContext context)
    {
        var type = value!.GetType();
        var typeText = TypeNames.Get(type);
        return value switch
        {
            bool b => new ValueDisplay(b ? "true" : "false", typeText),
            char c => new ValueDisplay($"'{EscapeChar(c)}' (U+{(int)c:X4})", typeText),
            string s => StringDisplay.Build(s, typeText, context),
            DateTime dt => new ValueDisplay(dt.ToString("yyyy-MM-ddTHH:mm:ss.ffffff", CultureInfo.InvariantCulture) + $" ({dt.Kind})", typeText),
            DateTimeOffset dto => new ValueDisplay(dto.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture), typeText),
            TimeSpan ts => new ValueDisplay(ts.ToString("c", CultureInfo.InvariantCulture), typeText),
            Guid guid => new ValueDisplay(guid.ToString("D"), typeText),
            Uri uri => SafeUri(uri, typeText),
            Enum => new ValueDisplay($"{Enum.GetName(type, value)} ({Convert.ToInt64(value)})", typeText),
            byte[] bytes => ByteArrayDisplay.Build(bytes, typeText),
            _ => new ValueDisplay("{" + typeText + "}", typeText),
        };
    }

    private static ValueDisplay SafeUri(Uri uri, string typeText)
    {
        try
        {
            return new ValueDisplay(uri.ToString(), typeText);
        }
        catch
        {
            return new ValueDisplay("{Uri}", typeText);
        }
    }

    internal static string EscapeChar(char c) => c switch
    {
        '\'' => "\\'",
        '\\' => "\\\\",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\0' => "\\0",
        _ => char.IsControl(c) ? "\\u" + ((int)c).ToString("X4") : c.ToString(),
    };
}

/// <summary>String display: quoting, escaping, length-limited truncation with an explicit marker (PRD §9).</summary>
public static class StringDisplay
{
    public static ValueDisplay Build(string s, string typeText, ValueFormattingContext context)
    {
        var escaped = Escape(s);
        var truncated = escaped.Length > context.MaximumStringLength;
        if (truncated)
            escaped = escaped[..context.MaximumStringLength];

        return new ValueDisplay(
            Text: "\"" + escaped + (truncated ? "…\"" : "\""),
            TypeText: typeText,
            IsTruncated: truncated,
            ToolTip: $"Length = {s.Length}");
    }

    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                case '\a': sb.Append("\\a"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\v': sb.Append("\\v"); break;
                default:
                    if (char.IsControl(c))
                        sb.Append("\\u").Append(((int)c).ToString("X4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}

/// <summary>byte[] summary: length plus a hex prefix, never a plain list (PRD §9).</summary>
public static class ByteArrayDisplay
{
    public static ValueDisplay Build(byte[] bytes, string typeText)
    {
        var prefixLength = Math.Min(bytes.Length, 8);
        var sb = new StringBuilder();
        for (var i = 0; i < prefixLength; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(bytes[i].ToString("X2"));
        }
        if (bytes.Length > prefixLength)
            sb.Append(" …");

        return new ValueDisplay($"byte[{bytes.Length}] {sb}", typeText);
    }
}
