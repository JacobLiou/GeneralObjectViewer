using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Metadata;
using ObjectViewer.Core.Paths;

namespace ObjectViewer.Core.Adapters;

/// <summary>
/// Dictionaries: shows Count without enumeration (PRD FR-051) and pages entries after an
/// explicit user expansion. Entries are materialized once (bounded by MaximumEnumerableItems)
/// so paging never re-enumerates. Sensitive keys mask the whole entry.
/// </summary>
public sealed class DictionaryAdapter : IObjectAdapter
{
    public int Priority => 60;
    public string Name => "Dictionary";

    private static readonly ConcurrentDictionary<Type, (Func<object, object?> Key, Func<object, object?> Value)> KvpAccessors = new();

    public bool CanHandle(in ObjectAdapterContext context)
    {
        var value = context.Value;
        if (value is null)
            return false;
        if (value is IDictionary)
            return true;
        return value.GetType().GetInterfaces().Any(IsDictionaryInterface);
    }

    private static bool IsDictionaryInterface(Type type)
        => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var value = context.Value!;
        var typeText = TypeNames.Get(value.GetType());
        var count = TryGetCount(value);
        var text = count is null ? "Count = ?" : $"Count = {count}";
        return new(ObjectValueKind.Dictionary, new ValueDisplay(text, typeText), CanExpand: count is null or > 0);
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
    {
        var state = node.Continuation as DictionaryEnumerationState;
        if (state is null)
        {
            state = MaterializeEntries(node.RawValue!, session.Options.MaximumEnumerableItems);
            node.Continuation = state;
        }

        var pageSize = session.Options.CollectionPageSize;
        var end = Math.Min(state.Offset + pageSize, state.Entries.Count);
        var children = new List<ObjectMemberDescriptor>(Math.Max(0, end - state.Offset));

        for (var i = state.Offset; i < end; i++)
        {
            var (key, _, entryObject, keyText, escapedKey) = state.Entries[i];
            var isSensitive = session.SensitivePolicy.IsSensitive(keyText);
            children.Add(new ObjectMemberDescriptor(
                Name: $"['{escapedKey}']",
                Value: entryObject,
                DeclaredType: typeof(object),
                Kind: ObjectNodeKind.DictionaryEntry,
                PathSegment: new DictionaryKeySegment(escapedKey),
                IsSensitive: isSensitive));
        }

        state.Offset = end;
        var more = end < state.Entries.Count;
        return Task.FromResult(new ChildPageResult(children, more ? state : null, IsTruncated: !more && state.Truncated));
    }

    private static int? TryGetCount(object value)
    {
        if (value is ICollection collection)
            return collection.Count;
        var countProperty = value.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        return countProperty?.GetValue(value) as int?;
    }

    internal static (object? Key, object? Value) ReadPair(object entry)
    {
        if (entry is DictionaryEntry dictionaryEntry)
            return (dictionaryEntry.Key, dictionaryEntry.Value);
        var accessors = GetKvpAccessors(entry.GetType());
        return (accessors.Key(entry), accessors.Value(entry));
    }

    private static DictionaryEnumerationState MaterializeEntries(object dictionary, int maximumItems)
    {
        var state = new DictionaryEnumerationState();
        if (dictionary is IDictionary nonGeneric)
        {
            foreach (DictionaryEntry entry in nonGeneric)
            {
                if (state.Entries.Count >= maximumItems)
                {
                    state.Truncated = true;
                    break;
                }
                state.Entries.Add(MakeEntry(entry, entry.Key, entry.Value));
            }
        }
        else
        {
            foreach (var entry in (IEnumerable)dictionary)
            {
                if (state.Entries.Count >= maximumItems)
                {
                    state.Truncated = true;
                    break;
                }
                var (key, value) = ReadPair(entry);
                state.Entries.Add(MakeEntry(entry, key, value));
            }
        }
        return state;
    }

    private static (object? Key, object? Value, object EntryObject, string KeyText, string EscapedKey) MakeEntry(
        object entryObject, object? key, object? value)
    {
        var keyText = SafeKeyText(key);
        return (key, value, entryObject, keyText, PathRenderer.EscapeKey(keyText));
    }

    private static string SafeKeyText(object? key)
    {
        if (key is null)
            return "(null)";
        try
        {
            var text = key.ToString() ?? string.Empty;
            return text.Length > 100 ? text[..100] + "…" : text;
        }
        catch
        {
            return $"<{key.GetType().Name} threw>";
        }
    }

    private static (Func<object, object?> Key, Func<object, object?> Value) GetKvpAccessors(Type type)
        => KvpAccessors.GetOrAdd(type, static t =>
        {
            var keyProperty = t.GetProperty("Key", BindingFlags.Public | BindingFlags.Instance)!;
            var valueProperty = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)!;
            return (o => keyProperty.GetValue(o), o => valueProperty.GetValue(o));
        });
}

/// <summary>A single dictionary entry: expands into Key and Value children (PRD §13.2).</summary>
public sealed class DictionaryEntryAdapter : IObjectAdapter
{
    public int Priority => 70;
    public string Name => "DictionaryEntry";

    public bool CanHandle(in ObjectAdapterContext context)
    {
        var value = context.Value;
        if (value is DictionaryEntry)
            return true;
        return value is not null
            && value.GetType().IsGenericType
            && value.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
    }

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var (key, value) = DictionaryAdapter.ReadPair(context.Value!);
        var keyText = SafeText(key);
        var valueText = SafeValueText(value, session);
        return new(
            ObjectValueKind.Object,
            new ValueDisplay($"Key = {keyText}, Value = {valueText}", "Entry"),
            CanExpand: true);
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
    {
        var (key, value) = DictionaryAdapter.ReadPair(node.RawValue!);
        var keySensitive = key is string ? session.SensitivePolicy.IsSensitive(key.ToString()!) : false;

        IReadOnlyList<ObjectMemberDescriptor> children =
        [
            new ObjectMemberDescriptor(
                Name: "Key",
                Value: key,
                DeclaredType: key?.GetType(),
                Kind: ObjectNodeKind.Property,
                PathSegment: new MemberSegment("Key"),
                IsSensitive: keySensitive),
            new ObjectMemberDescriptor(
                Name: "Value",
                Value: value,
                DeclaredType: typeof(object),
                Kind: ObjectNodeKind.Property,
                PathSegment: new MemberSegment("Value")),
        ];
        return Task.FromResult(new ChildPageResult(children));
    }

    private static string SafeText(object? value)
    {
        if (value is null)
            return "null";
        if (value is string s)
            return "\"" + (s.Length > 60 ? s[..60] + "…" : s) + "\"";
        try
        {
            return value.GetType().Name;
        }
        catch
        {
            return "<error>";
        }
    }

    private static string SafeValueText(object? value, InspectionSession session)
    {
        if (value is null)
            return "null";
        try
        {
            var display = session.Formatters.Format(value.GetType(), value, ValueFormattingContext.From(session.Options));
            return display.Text;
        }
        catch
        {
            return "{" + TypeNames.Get(value.GetType()) + "}";
        }
    }
}

/// <summary>Materialized, session-bounded dictionary paging state.</summary>
internal sealed class DictionaryEnumerationState
{
    public List<(object? Key, object? Value, object EntryObject, string KeyText, string EscapedKey)> Entries { get; } = [];

    public int Offset { get; set; }

    public bool Truncated { get; set; }
}
