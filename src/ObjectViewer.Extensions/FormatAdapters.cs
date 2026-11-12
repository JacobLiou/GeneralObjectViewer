using System.Text.Json.Nodes;
using System.Xml.Linq;
using ObjectViewer.Core.Adapters;
using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Members;
using ObjectViewer.Core.Paths;
using ObjectViewer.Extensions.Yaml;

namespace ObjectViewer.Extensions;

/// <summary>Display adapter for System.Text.Json JsonNode values (Proposal §38.3).</summary>
public sealed class JsonNodeAdapter : IObjectAdapter
{
    public int Priority => 78;
    public string Name => "JsonNode";

    public bool CanHandle(in ObjectAdapterContext context)
        => context.Value is JsonNode;

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var node = (JsonNode)context.Value!;
        return node switch
        {
            JsonObject obj => new(
                ObjectValueKind.Dictionary,
                new ValueDisplay($"Count = {obj.Count}", $"JsonObject ({obj.Count})"),
                CanExpand: obj.Count > 0),
            JsonArray array => new(
                ObjectValueKind.Collection,
                new ValueDisplay($"Count = {array.Count}", $"JsonArray ({array.Count})"),
                CanExpand: array.Count > 0),
            _ => new(ObjectValueKind.Primitive, SafeJsonText(node), CanExpand: false),
        };
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
        => Task.FromResult(node.RawValue switch
        {
            JsonObject obj => GetObjectEntries(obj, node, session),
            JsonArray array => GetArrayItems(array, node, session),
            _ => ChildPageResult.Empty,
        });

    private static ChildPageResult GetObjectEntries(JsonObject obj, ObjectNode node, InspectionSession session)
    {
        var state = node.Continuation as JsonEntryState;
        if (state is null)
        {
            state = new JsonEntryState([.. obj]);
            node.Continuation = state;
        }

        var pageSize = session.Options.CollectionPageSize;
        var end = Math.Min(state.Offset + pageSize, state.Entries.Count);
        var children = new List<ObjectMemberDescriptor>(Math.Max(0, end - state.Offset));
        for (var i = state.Offset; i < end; i++)
        {
            var entry = state.Entries[i];
            children.Add(new ObjectMemberDescriptor(
                Name: entry.Key,
                Value: entry.Value,
                DeclaredType: typeof(JsonNode),
                Kind: ObjectNodeKind.Property,
                PathSegment: new DictionaryKeySegment(PathRenderer.EscapeKey(entry.Key)),
                IsSensitive: session.SensitivePolicy.IsSensitive(entry.Key)));
        }

        state.Offset = end;
        return new ChildPageResult(children, end < state.Entries.Count ? state : null);
    }

    private static ChildPageResult GetArrayItems(JsonArray array, ObjectNode node, InspectionSession session)
    {
        var pageSize = session.Options.CollectionPageSize;
        var cursor = node.Continuation as CollectionCursor ?? new CollectionCursor(0, pageSize);
        var end = Math.Min(cursor.Offset + pageSize, array.Count);

        var children = new List<ObjectMemberDescriptor>(Math.Max(0, end - cursor.Offset));
        for (var i = cursor.Offset; i < end; i++)
        {
            children.Add(new ObjectMemberDescriptor(
                Name: $"[{i}]",
                Value: array[i],
                DeclaredType: typeof(JsonNode),
                Kind: ObjectNodeKind.CollectionItem,
                PathSegment: new IndexSegment([i])));
        }

        var next = end < array.Count ? new CollectionCursor(end, pageSize) : null;
        return new ChildPageResult(children, next);
    }

    private static ValueDisplay SafeJsonText(JsonNode node)
    {
        try
        {
            return new ValueDisplay(node.ToJsonString() ?? "null", "JsonValue");
        }
        catch
        {
            return new ValueDisplay("<json>", "JsonValue");
        }
    }

    private sealed class JsonEntryState(List<KeyValuePair<string, JsonNode?>> entries)
    {
        public List<KeyValuePair<string, JsonNode?>> Entries { get; } = entries;

        public int Offset { get; set; }
    }
}

/// <summary>Display adapter for materialized YAML values with alias-preserving identity.</summary>
public sealed class YamlValueAdapter : IObjectAdapter
{
    public int Priority => 78;
    public string Name => "YamlValue";

    public bool CanHandle(in ObjectAdapterContext context)
        => context.Value is YamlValue;

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var value = (YamlValue)context.Value!;
        return value.Kind switch
        {
            YamlNodeKind.Mapping => new(
                ObjectValueKind.Dictionary,
                new ValueDisplay($"Count = {value.Count}", $"YamlMapping ({value.Count})"),
                CanExpand: value.Count > 0),
            YamlNodeKind.Sequence => new(
                ObjectValueKind.Collection,
                new ValueDisplay($"Count = {value.Count}", $"YamlSequence ({value.Count})"),
                CanExpand: value.Count > 0),
            _ => new(ObjectValueKind.String, DescribeScalar(value, session), CanExpand: false),
        };
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
        => Task.FromResult(node.RawValue switch
        {
            YamlValue { Kind: YamlNodeKind.Mapping, Mapping: { } mapping } => GetMappingEntries(mapping, node, session),
            YamlValue { Kind: YamlNodeKind.Sequence, Sequence: { } sequence } => GetSequenceItems(sequence, node, session),
            _ => ChildPageResult.Empty,
        });

    private static ChildPageResult GetMappingEntries(List<KeyValuePair<string, YamlValue?>> mapping, ObjectNode node, InspectionSession session)
    {
        var state = node.Continuation as YamlEntryState;
        if (state is null)
        {
            state = new YamlEntryState(mapping);
            node.Continuation = state;
        }

        var pageSize = session.Options.CollectionPageSize;
        var end = Math.Min(state.Offset + pageSize, state.Entries.Count);
        var children = new List<ObjectMemberDescriptor>(Math.Max(0, end - state.Offset));
        for (var i = state.Offset; i < end; i++)
        {
            var entry = state.Entries[i];
            children.Add(new ObjectMemberDescriptor(
                Name: entry.Key,
                Value: entry.Value,
                DeclaredType: typeof(YamlValue),
                Kind: ObjectNodeKind.Property,
                PathSegment: new DictionaryKeySegment(PathRenderer.EscapeKey(entry.Key)),
                IsSensitive: session.SensitivePolicy.IsSensitive(entry.Key)));
        }

        state.Offset = end;
        return new ChildPageResult(children, end < state.Entries.Count ? state : null);
    }

    private static ChildPageResult GetSequenceItems(List<YamlValue?> sequence, ObjectNode node, InspectionSession session)
    {
        var pageSize = session.Options.CollectionPageSize;
        var cursor = node.Continuation as CollectionCursor ?? new CollectionCursor(0, pageSize);
        var end = Math.Min(cursor.Offset + pageSize, sequence.Count);

        var children = new List<ObjectMemberDescriptor>(Math.Max(0, end - cursor.Offset));
        for (var i = cursor.Offset; i < end; i++)
        {
            children.Add(new ObjectMemberDescriptor(
                Name: $"[{i}]",
                Value: sequence[i],
                DeclaredType: typeof(YamlValue),
                Kind: ObjectNodeKind.CollectionItem,
                PathSegment: new IndexSegment([i])));
        }

        var next = end < sequence.Count ? new CollectionCursor(end, pageSize) : null;
        return new ChildPageResult(children, next);
    }

    private static ValueDisplay DescribeScalar(YamlValue value, InspectionSession session)
    {
        var text = value.Scalar ?? string.Empty;
        var maximum = session.Options.MaximumStringLength;
        var truncated = text.Length > maximum;
        if (truncated)
            text = text[..maximum];
        return new ValueDisplay(text, "YamlScalar", IsTruncated: truncated);
    }

    private sealed class YamlEntryState(List<KeyValuePair<string, YamlValue?>> entries)
    {
        public List<KeyValuePair<string, YamlValue?>> Entries { get; } = entries;

        public int Offset { get; set; }
    }
}

/// <summary>Display adapter for XML elements: attributes as @name members, child elements as members.</summary>
public sealed class XElementAdapter : IObjectAdapter
{
    public int Priority => 77;
    public string Name => "XElement";

    public bool CanHandle(in ObjectAdapterContext context)
        => context.Value is XElement;

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var element = (XElement)context.Value!;
        var typeText = element.Name.LocalName;
        var hasAttributes = element.Attributes().Any(a => !a.IsNamespaceDeclaration);
        var hasElements = element.HasElements;

        if (!hasElements)
        {
            var text = element.Value.Trim();
            if (!hasAttributes && text.Length == 0)
                return new(ObjectValueKind.Null, new ValueDisplay(string.Empty, typeText), CanExpand: false);

            if (!hasAttributes)
            {
                var maximum = session.Options.MaximumStringLength;
                var truncated = text.Length > maximum;
                if (truncated)
                    text = text[..maximum];
                return new(ObjectValueKind.String, new ValueDisplay(text, typeText, IsTruncated: truncated), CanExpand: false);
            }

            return new(ObjectValueKind.Object, new ValueDisplay("{" + typeText + "}", typeText), CanExpand: true);
        }

        return new(ObjectValueKind.Object, new ValueDisplay("{" + typeText + "}", typeText), CanExpand: true);
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
    {
        var element = (XElement)node.RawValue!;
        var children = new List<ObjectMemberDescriptor>();

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            var name = "@" + attribute.Name.LocalName;
            children.Add(new ObjectMemberDescriptor(
                Name: name,
                Value: attribute.Value,
                DeclaredType: typeof(string),
                Kind: ObjectNodeKind.Property,
                PathSegment: new MemberSegment(name),
                IsSensitive: session.SensitivePolicy.IsSensitive(attribute.Name.LocalName)));
        }

        // Repeated sibling names fall back to index segments so stable paths stay unique.
        var nameCounts = element.Elements()
            .GroupBy(e => e.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var childIndex = 0;
        foreach (var childElement in element.Elements())
        {
            var localName = childElement.Name.LocalName;
            ObjectPathSegment segment = nameCounts[localName] > 1
                ? new IndexSegment([childIndex])
                : new MemberSegment(localName);
            children.Add(new ObjectMemberDescriptor(
                Name: localName,
                Value: childElement,
                DeclaredType: typeof(XElement),
                Kind: ObjectNodeKind.Property,
                PathSegment: segment,
                IsSensitive: session.SensitivePolicy.IsSensitive(localName)));
            childIndex++;
        }

        return Task.FromResult(new ChildPageResult(children));
    }
}

/// <summary>Format display adapters for imported data sources; pass via InspectionRequest.CustomAdapters.</summary>
public static class ObjectViewerFormatAdapters
{
    public static IReadOnlyList<IObjectAdapter> All { get; } =
    [
        new JsonNodeAdapter(),
        new YamlValueAdapter(),
        new XElementAdapter(),
    ];
}
