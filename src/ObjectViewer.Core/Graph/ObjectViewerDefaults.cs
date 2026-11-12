using ObjectViewer.Core.Adapters;
using ObjectViewer.Core.Formatting;

namespace ObjectViewer.Core.Graph;

/// <summary>Default extension registrations (PRD FR-133). Hosts may register custom adapters on top.</summary>
public static class ObjectViewerDefaults
{
    public static IReadOnlyList<IObjectAdapter> CreateDefaultAdapters() =>
    [
        new NullAdapter(),
        new PrimitiveAdapter(),
        new StringAdapter(),
        new EnumAdapter(),
        new ByteArrayAdapter(),
        new DictionaryEntryAdapter(),
        new DictionaryAdapter(),
        new ListAdapter(),
        new DefaultObjectAdapter(),
    ];

    public static IReadOnlyList<IObjectValueFormatter> CreateDefaultFormatters() =>
    [
        new NumericValueFormatter(),
        new DefaultValueFormatter(),
    ];
}
