using YamlDotNet.RepresentationModel;

namespace ObjectViewer.Extensions.Yaml;

public enum YamlNodeKind
{
    Scalar,
    Mapping,
    Sequence,
}

/// <summary>
/// YAML materialized into a small in-memory model where anchors/aliases resolve to
/// the SAME instance, so the viewer's reference identity detection sees shared nodes
/// naturally (Proposal §38.3). Immutable once built.
/// </summary>
public sealed class YamlValue
{
    private YamlValue(YamlNodeKind kind)
    {
        Kind = kind;
        if (kind == YamlNodeKind.Mapping)
            Mapping = [];
        else if (kind == YamlNodeKind.Sequence)
            Sequence = [];
    }

    public YamlNodeKind Kind { get; }
    public string? Scalar { get; private init; }
    public List<KeyValuePair<string, YamlValue?>>? Mapping { get; }
    public List<YamlValue?>? Sequence { get; }

    public int Count => (Mapping?.Count) ?? (Sequence?.Count) ?? 0;

    internal static YamlValue ScalarNode(string? text)
        => new(YamlNodeKind.Scalar) { Scalar = text };

    internal static YamlValue MappingNode()
        => new(YamlNodeKind.Mapping);

    internal static YamlValue SequenceNode()
        => new(YamlNodeKind.Sequence);
}

public static class YamlModelBuilder
{
    // YamlDotNet 16 marks YamlAliasNode internal, so aliases are detected by type name.
    private static bool IsAlias(YamlNode node)
        => node.GetType().Name == "YamlAliasNode";

    /// <summary>Converts a YamlDotNet graph into <see cref="YamlValue"/>, resolving aliases to shared instances.</summary>
    public static YamlValue? Build(YamlNode root, int maximumItems)
    {
        var anchors = new Dictionary<string, YamlNode>();
        CollectAnchors(root, anchors);
        var memo = new Dictionary<YamlNode, YamlValue>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var builder = new Builder(anchors, memo, maximumItems);
        return builder.Convert(root);
    }

    private static void CollectAnchors(YamlNode node, Dictionary<string, YamlNode> anchors)
    {
        if (IsAlias(node))
            return;

        if (!node.Anchor.IsEmpty && node.Anchor.Value is { Length: > 0 } anchorName)
            anchors.TryAdd(anchorName, node);

        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var pair in mapping.Children)
                {
                    CollectAnchors(pair.Key, anchors);
                    CollectAnchors(pair.Value, anchors);
                }
                break;
            case YamlSequenceNode sequence:
                foreach (var item in sequence.Children)
                    CollectAnchors(item, anchors);
                break;
        }
    }

    private sealed class Builder(
        Dictionary<string, YamlNode> anchors,
        Dictionary<YamlNode, YamlValue> memo,
        int maximumItems)
    {
        private int _created;

        public YamlValue? Convert(YamlNode node)
        {
            if (IsAlias(node) && node.Anchor.Value is { } aliasName)
                return anchors.TryGetValue(aliasName, out var target)
                    ? Convert(target)
                    : null;

            if (memo.TryGetValue(node, out var cached))
                return cached;

            switch (node)
            {
                case YamlScalarNode scalar:
                {
                    if (scalar.Value is null)
                        return null;
                    var value = YamlValue.ScalarNode(scalar.Value);
                    memo[node] = value;
                    return value;
                }
                case YamlMappingNode mapping:
                {
                    var value = YamlValue.MappingNode();
                    memo[node] = value; // registered before recursion: cycles resolve to this instance
                    foreach (var pair in mapping.Children)
                    {
                        if (!ReserveItem())
                            break;
                        value.Mapping!.Add(new KeyValuePair<string, YamlValue?>(KeyText(pair.Key), Convert(pair.Value)));
                    }
                    return value;
                }
                case YamlSequenceNode sequence:
                {
                    var value = YamlValue.SequenceNode();
                    memo[node] = value;
                    foreach (var item in sequence.Children)
                    {
                        if (!ReserveItem())
                            break;
                        value.Sequence!.Add(Convert(item));
                    }
                    return value;
                }
                default:
                    return null;
            }
        }

        private bool ReserveItem()
            => _created++ < maximumItems;

        private static string KeyText(YamlNode key)
            => key is YamlScalarNode scalar ? (scalar.Value ?? string.Empty) : key.ToString();
    }
}
