using ObjectViewer.Core.Adapters;
using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Metadata;
using ObjectViewer.Core.Paths;

namespace ObjectViewer.Core.Search;

/// <summary>Explicit, bounded deep-search configuration (PRD FR-092, defaults from PRD §12).</summary>
public sealed record DeepSearchOptions(
    int MaximumDepth = 8,
    int MaximumNodes = 10_000,
    int MaximumItemsPerCollection = 100,
    TimeSpan? Timeout = null,
    int MaximumMatches = 500)
{
    public TimeSpan TimeoutValue => Timeout ?? TimeSpan.FromSeconds(10);

    public static DeepSearchOptions Default { get; } = new();
}

/// <summary>A match produced by a deep search; the live tree is untouched.</summary>
public sealed record DeepSearchMatch(
    ObjectPath Path,
    string Name,
    string TypeText,
    string ValueText);

public sealed record DeepSearchResult(
    IReadOnlyList<DeepSearchMatch> Matches,
    bool Truncated,
    int NodesVisited)
{
    public static DeepSearchResult Empty { get; } = new([], Truncated: false, NodesVisited: 0);
}

/// <summary>
/// Bounded graph traversal for deep search (PRD FR-092~095): reuses the session's
/// adapter pipeline so member policies, sensitive masking and paging rules apply to
/// imported formats exactly as to CLR objects. The live tree is never modified.
/// </summary>
public sealed class DeepSearchService(InspectionSession session)
{
    public async Task<DeepSearchResult> SearchAsync(SearchQuery query, DeepSearchOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrEmpty(query.Text) || session.Root?.RawValue is null)
            return DeepSearchResult.Empty;

        var matches = new List<DeepSearchMatch>();
        var visited = new HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var truncated = false;
        var nodesVisited = 0;

        var queue = new Queue<ObjectNode>();
        queue.Enqueue(CreateTransientNode(session.Root.Name, session.Root.RawValue, session.Root.DeclaredType, ObjectPath.RootPath, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stopwatch.Elapsed > options.TimeoutValue || nodesVisited >= options.MaximumNodes)
            {
                truncated = true;
                break;
            }

            var node = queue.Dequeue();
            nodesVisited++;

            var value = node.RawValue;
            if (value is null)
                continue;
            if (ObjectIdentityMap.IsTrackable(value) && !visited.Add(value))
                continue; // cycle or already-visited shared object

            TryMatch(node, query, matches, ref truncated, options);

            if (node.Depth >= options.MaximumDepth)
                continue;

            IReadOnlyList<ObjectMemberDescriptor> children;
            try
            {
                var adapterContext = new ObjectAdapterContext(value, node.DeclaredType, SourceMember: null);
                var adapter = session.Adapters.Resolve(adapterContext, session);
                var page = await adapter.GetChildrenAsync(node, session, cancellationToken).ConfigureAwait(false);
                children = page.Children;
            }
            catch (Exception ex)
            {
                session.Diagnostics.Record("DeepSearchChildLoadFailed", ex.GetType().Name);
                continue;
            }

            foreach (var descriptor in children.Take(options.MaximumItemsPerCollection))
            {
                var child = new ObjectNode(
                    nodeId: 0,
                    name: descriptor.Name,
                    kind: descriptor.Kind,
                    path: node.Path.Append(descriptor.PathSegment),
                    depth: node.Depth + 1)
                {
                    DeclaredType = descriptor.DeclaredType ?? descriptor.Value?.GetType(),
                    RawValue = descriptor.ReadError is null ? descriptor.Value : null,
                    IsSensitive = descriptor.IsSensitive,
                };

                TryMatch(child, query, matches, ref truncated, options);

                if (IsExpandable(child, descriptor))
                    queue.Enqueue(child);
            }
        }

        return new DeepSearchResult(matches, truncated, nodesVisited);
    }

    private static bool IsExpandable(ObjectNode child, ObjectMemberDescriptor descriptor)
        => descriptor.ReadError is null
            && child.RawValue is not null
            && !child.IsSensitive
            && child.RawValue is not string
            && !child.RawValue.GetType().IsValueType;

    private void TryMatch(ObjectNode node, SearchQuery query, List<DeepSearchMatch> matches, ref bool truncated, DeepSearchOptions options)
    {
        var typeText = SafeTypeText(node);
        var matched =
            (query.Fields.HasFlag(SearchField.Name) && node.Name.Contains(query.Text, query.Comparison))
            || (query.Fields.HasFlag(SearchField.Type) && typeText.Contains(query.Text, query.Comparison))
            || (query.Fields.HasFlag(SearchField.Path) && node.Path.ToDisplayPath().Contains(query.Text, query.Comparison));

        var valueText = node.IsSensitive ? Formatting.ValueDisplay.MaskedText : DescribeValueText(node);
        if (!matched && query.Fields.HasFlag(SearchField.Value) && valueText.Contains(query.Text, query.Comparison))
            matched = true;

        if (!matched)
            return;

        if (matches.Count >= options.MaximumMatches)
        {
            truncated = true;
            return;
        }

        matches.Add(new DeepSearchMatch(node.Path, node.Name, typeText, valueText));
    }

    private string DescribeValueText(ObjectNode node)
    {
        var value = node.RawValue;
        if (value is null)
            return "null";
        try
        {
            var context = new ObjectAdapterContext(value, node.DeclaredType, SourceMember: null);
            var adapter = session.Adapters.Resolve(context, session);
            return adapter.Describe(context, session).Display.Text;
        }
        catch
        {
            return "{" + TypeNames.Get(value.GetType()) + "}";
        }
    }

    private static string SafeTypeText(ObjectNode node)
        => node.DeclaredType is null ? string.Empty : TypeNames.Get(node.DeclaredType);

    private static ObjectNode CreateTransientNode(string name, object? value, Type? declaredType, ObjectPath path, int depth)
        => new(0, name, ObjectNodeKind.Root, path, depth)
        {
            DeclaredType = declaredType,
            RawValue = value,
        };
}
