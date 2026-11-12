using System.Globalization;
using System.Text;
using ObjectViewer.Core.Metadata;

namespace ObjectViewer.Core.Formatting;

/// <summary>
/// Numeric formatting with Default/Decimal/Hex/Binary modes (PRD FR-083).
/// Floating point values use round-trip formatting and never fall back to hex/binary.
/// </summary>
public sealed class NumericValueFormatter : IObjectValueFormatter
{
    public int Priority => 100;
    public string Name => "Numeric";

    public bool CanFormat(Type? declaredType, Type? runtimeType, object? value)
    {
        if (value is null)
            return false;
        return value is byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal;
    }

    public ValueDisplay Format(Type? declaredType, object? value, ValueFormattingContext context)
    {
        var type = value!.GetType();
        var typeText = TypeNames.Get(type);
        return value switch
        {
            float f => new ValueDisplay(f.ToString("R", context.Culture), typeText, FormatName: "R"),
            double d => new ValueDisplay(d.ToString("R", context.Culture), typeText, FormatName: "R"),
            decimal m => new ValueDisplay(m.ToString(context.Culture), typeText),
            _ => FormatIntegral(value, typeText, context),
        };
    }

    private static ValueDisplay FormatIntegral(object value, string typeText, ValueFormattingContext context)
    {
        var text = context.Format switch
        {
            ValueDisplayFormat.Hex => "0x" + ToIntegralString(value, 16),
            ValueDisplayFormat.Binary => "0b" + ToIntegralString(value, 2),
            _ => Convert.ToString(value, context.Culture) ?? string.Empty,
        };
        var formatName = context.Format switch
        {
            ValueDisplayFormat.Hex => "hex",
            ValueDisplayFormat.Binary => "bin",
            _ => null as string,
        };
        return new ValueDisplay(text, typeText, FormatName: formatName);
    }

    private static string ToIntegralString(object value, int toBase)
        => value switch
        {
            ulong ul when toBase == 16 => ul.ToString("X"),
            ulong ul => Convert.ToString(unchecked((long)ul), toBase),
            _ => Convert.ToString(Convert.ToInt64(value), toBase),
        };
}
