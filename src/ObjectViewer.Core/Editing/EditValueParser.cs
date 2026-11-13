using System.ComponentModel;
using System.Globalization;
using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Metadata;

namespace ObjectViewer.Core.Editing;

/// <summary>
/// Custom text→value conversion for controlled editing (PRD FR-123).
/// Return false when this converter does not handle the target type; the next
/// converter (and finally the built-in parser) is tried instead.
/// </summary>
public interface IEditValueConverter
{
    int Priority { get; }

    string Name { get; }

    bool TryConvert(string text, Type targetType, CultureInfo culture, out object? value, out string? error);
}

/// <summary>
/// Built-in text→value conversion for the edit pipeline (PRD FR-122/123):
/// Nullable, Enum, Boolean, char, all numerics, DateTime/DateTimeOffset/TimeSpan,
/// Guid, string and any type with a string TypeConverter. Parsing is culture-aware
/// (session culture), never throws, and reports a user-presentable error instead.
///
/// Input conventions: the keyword <c>null</c> (case-insensitive) assigns null;
/// for string members the raw text is the value, everything else is trimmed.
/// </summary>
public static class EditValueParser
{
    public const string NullKeyword = "null";

    /// <summary>Never throws; returns false with a display-ready error on failure.</summary>
    public static bool TryParse(Type targetType, string? text, CultureInfo culture, out object? value, out string? error)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(culture);

        value = null;
        error = null;

        var underlying = Nullable.GetUnderlyingType(targetType);
        var effectiveType = underlying ?? targetType;
        var acceptsNull = underlying is not null || !targetType.IsValueType;

        if (effectiveType == typeof(string))
        {
            // The raw text is the value; only the null keyword clears it.
            if (string.Equals(text, NullKeyword, StringComparison.OrdinalIgnoreCase))
            {
                if (acceptsNull)
                    return true;
                error = $"{TypeNames.Get(targetType)} does not accept null.";
                return false;
            }
            value = text ?? string.Empty;
            return true;
        }

        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, NullKeyword, StringComparison.OrdinalIgnoreCase))
        {
            if (acceptsNull)
                return true;
            error = $"{TypeNames.Get(targetType)} does not accept null.";
            return false;
        }

        if (TryParseWellKnown(effectiveType, trimmed, culture, out value))
            return true;

        if (TryParseWithConverter(effectiveType, trimmed, culture, out value, out error))
            return true;

        error ??= $"Cannot convert \"{trimmed}\" to {TypeNames.Get(targetType)}.";
        return false;
    }

    /// <summary>Pre-fills the edit box with the current value in editable form (no display quoting).</summary>
    public static string ToEditString(object? value, CultureInfo culture)
    {
        try
        {
            return value switch
            {
                null => NullKeyword,
                string s => s,
                bool b => b ? "true" : "false",
                char c => c.ToString(),
                Enum e => e.ToString(),
                DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
                Guid g => g.ToString("D"),
                IFormattable f => f.ToString(null, culture),
                _ => value.ToString() ?? string.Empty,
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryParseWellKnown(Type type, string text, CultureInfo culture, out object? value)
    {
        value = null;

        if (type == typeof(Guid))
            return Guid.TryParse(text, out var guid) && Assign(out value, guid);
        if (type.IsEnum)
            return Enum.TryParse(type, text, ignoreCase: true, out var parsed) && Assign(out value, parsed);

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean:
                return bool.TryParse(text, out var b) && Assign(out value, b);
            case TypeCode.Char:
                return text.Length == 1 && Assign(out value, text[0]);
            case TypeCode.SByte:
                return sbyte.TryParse(text, NumberStyles.Integer, culture, out var sb) && Assign(out value, sb);
            case TypeCode.Byte:
                return byte.TryParse(text, NumberStyles.Integer, culture, out var by) && Assign(out value, by);
            case TypeCode.Int16:
                return short.TryParse(text, NumberStyles.Integer, culture, out var s16) && Assign(out value, s16);
            case TypeCode.UInt16:
                return ushort.TryParse(text, NumberStyles.Integer, culture, out var u16) && Assign(out value, u16);
            case TypeCode.Int32:
                return int.TryParse(text, NumberStyles.Integer, culture, out var s32) && Assign(out value, s32);
            case TypeCode.UInt32:
                return uint.TryParse(text, NumberStyles.Integer, culture, out var u32) && Assign(out value, u32);
            case TypeCode.Int64:
                return long.TryParse(text, NumberStyles.Integer, culture, out var s64) && Assign(out value, s64);
            case TypeCode.UInt64:
                return ulong.TryParse(text, NumberStyles.Integer, culture, out var u64) && Assign(out value, u64);
            case TypeCode.Single:
                return float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var f32) && Assign(out value, f32);
            case TypeCode.Double:
                return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var f64) && Assign(out value, f64);
            case TypeCode.Decimal:
                return decimal.TryParse(text, NumberStyles.Number, culture, out var dec) && Assign(out value, dec);
            case TypeCode.DateTime:
                return DateTime.TryParse(text, culture, DateTimeStyles.None, out var dt) && Assign(out value, dt);
            default:
                if (type == typeof(TimeSpan))
                    return TimeSpan.TryParse(text, culture, out var ts) && Assign(out value, ts);
                if (type == typeof(DateTimeOffset))
                    return DateTimeOffset.TryParse(text, culture, DateTimeStyles.None, out var dto) && Assign(out value, dto);
                return false;
        }
    }

    private static bool TryParseWithConverter(Type type, string text, CultureInfo culture, out object? value, out string? error)
    {
        value = null;
        error = null;
        try
        {
            var converter = TypeDescriptor.GetConverter(type);
            if (converter is null || !converter.CanConvertFrom(null, typeof(string)))
                return false;
            value = converter.ConvertFromString(null, culture, text);
            return true;
        }
        catch (Exception ex)
        {
            error = NodeError.Unwrap(ex).Message;
            return false;
        }
    }

    private static bool Assign(out object? slot, object parsed)
    {
        slot = parsed;
        return true;
    }
}
