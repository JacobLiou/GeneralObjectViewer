using System.Globalization;
using ObjectViewer.Core.Graph;

namespace ObjectViewer.Core.Formatting;

public enum ValueDisplayFormat
{
    Default,
    Decimal,
    Hex,
    Binary,
    RawString,
}

public sealed record ValueFormattingContext(CultureInfo Culture, ValueDisplayFormat Format, int MaximumStringLength)
{
    public static ValueFormattingContext From(InspectionOptions options)
        => new(options.Culture, options.ValueFormat, options.MaximumStringLength);
}

/// <summary>Value formatter abstraction, independent from reading and UI (PRD FR-080).</summary>
public interface IObjectValueFormatter
{
    int Priority { get; }

    string Name { get; }

    bool CanFormat(Type? declaredType, Type? runtimeType, object? value);

    ValueDisplay Format(Type? declaredType, object? value, ValueFormattingContext context);
}

/// <summary>
/// Priority-ordered formatter registry with per-call exception isolation (PRD FR-081/082).
/// </summary>
public sealed class FormatterRegistry
{
    private readonly IObjectValueFormatter[] _formatters;

    public FormatterRegistry(IEnumerable<IObjectValueFormatter> formatters)
        => _formatters = formatters
            .OrderByDescending(f => f.Priority)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .ToArray();

    public ValueDisplay Format(Type? declaredType, object? value, ValueFormattingContext context)
    {
        var runtimeType = value?.GetType();
        foreach (var formatter in _formatters)
        {
            try
            {
                if (!formatter.CanFormat(declaredType, runtimeType, value))
                    continue;
                return formatter.Format(declaredType, value, context);
            }
            catch
            {
                // A failing formatter must never break the tree; try the next one.
            }
        }

        return new ValueDisplay("{" + Metadata.TypeNames.Get(runtimeType) + "}", Metadata.TypeNames.Get(runtimeType));
    }
}
