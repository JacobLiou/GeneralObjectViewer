using ObjectViewer.Core.Graph;

namespace ObjectViewer.Core.Snapshots;

public enum SnapshotNodeState
{
    Unchanged,
    Added,
    Removed,
    Changed,
    TypeChanged,
    Error,
}

/// <summary>Records the limits under which the snapshot was taken, so diffs are explainable (PRD FR-113).</summary>
public sealed record SnapshotOptions(
    string? CultureName,
    int CollectionPageSize,
    int MaximumStringLength);

public sealed record ObjectSnapshot(
    string Name,
    DateTimeOffset CreatedAt,
    SnapshotOptions Options,
    ObjectSnapshotNode Root);

/// <summary>
/// A snapshot node: descriptions and formatted (masked-safe) values only.
/// Raw object graphs are never serialized into a snapshot (PRD FR-111/114).
/// </summary>
public sealed record ObjectSnapshotNode(
    string StablePath,
    string DisplayPath,
    string Name,
    string TypeName,
    string? FormattedValue,
    bool IsMasked,
    bool IsError,
    IReadOnlyList<ObjectSnapshotNode> Children);

public sealed record SnapshotDifference(
    SnapshotNodeState State,
    string StablePath,
    string DisplayPath,
    string Name,
    string? Before,
    string? After,
    string? TypeName);

public sealed record SnapshotDiffResult(
    IReadOnlyList<SnapshotDifference> Differences,
    int UnchangedCount)
{
    public int Count(SnapshotNodeState state)
        => Differences.Count(d => d.State == state);
}

public static class SnapshotBuilder
{
    /// <summary>
    /// Captures the currently loaded portion of the tree (Proposal §20.2): fast, side-effect
    /// free, and sensitive values only ever appear in their masked form. Synthetic paging
    /// markers (LoadMore/Truncated/Loading) are excluded because their ids are not stable
    /// across sessions.
    /// </summary>
    public static ObjectSnapshot Capture(string name, ObjectNode root, InspectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(root);
        var snapshotOptions = new SnapshotOptions(
            options.Culture.Name,
            options.CollectionPageSize,
            options.MaximumStringLength);

        return new ObjectSnapshot(name, DateTimeOffset.UtcNow, snapshotOptions, BuildNode(root));
    }

    private static ObjectSnapshotNode BuildNode(ObjectNode node)
    {
        var children = new List<ObjectSnapshotNode>(node.Children.Count);
        foreach (var child in node.Children)
        {
            if (child.Kind is ObjectNodeKind.LoadMore or ObjectNodeKind.Truncated or ObjectNodeKind.Loading)
                continue;
            children.Add(BuildNode(child));
        }

        return new ObjectSnapshotNode(
            StablePath: node.Path.ToStablePath(),
            DisplayPath: node.Path.ToDisplayPath(),
            Name: node.Name,
            TypeName: node.Display.TypeText,
            // Masked nodes store the mask text only (PRD FR-114: masked marker + mask text,
            // never the raw value).
            FormattedValue: node.Display.Text,
            IsMasked: node.Display.IsMasked,
            IsError: node.Error is not null || node.Kind == ObjectNodeKind.Error,
            Children: children);
    }
}

public static class SnapshotDiffer
{
    /// <summary>
    /// Matches nodes by stable path and reports Added/Removed/Changed/TypeChanged
    /// (Proposal §20.3). Masked values compare as equal masks — raw values are
    /// never stored, so they can never leak through a diff.
    /// </summary>
    public static SnapshotDiffResult Diff(ObjectSnapshot before, ObjectSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeIndex = IndexByPath(before.Root);
        var afterIndex = IndexByPath(after.Root);
        var differences = new List<SnapshotDifference>();
        var unchanged = 0;

        foreach (var (path, afterNode) in afterIndex)
        {
            if (!beforeIndex.TryGetValue(path, out var beforeNode))
            {
                differences.Add(new SnapshotDifference(
                    SnapshotNodeState.Added, path, afterNode.DisplayPath, afterNode.Name,
                    Before: null, After: Describe(afterNode), afterNode.TypeName));
                continue;
            }

            if (beforeNode.TypeName != afterNode.TypeName)
            {
                differences.Add(new SnapshotDifference(
                    SnapshotNodeState.TypeChanged, path, afterNode.DisplayPath, afterNode.Name,
                    Describe(beforeNode), Describe(afterNode), afterNode.TypeName));
            }
            else if (IsDifferent(beforeNode, afterNode))
            {
                differences.Add(new SnapshotDifference(
                    SnapshotNodeState.Changed, path, afterNode.DisplayPath, afterNode.Name,
                    Describe(beforeNode), Describe(afterNode), afterNode.TypeName));
            }
            else
            {
                unchanged++;
            }
        }

        foreach (var (path, beforeNode) in beforeIndex)
        {
            if (!afterIndex.ContainsKey(path))
            {
                differences.Add(new SnapshotDifference(
                    SnapshotNodeState.Removed, path, beforeNode.DisplayPath, beforeNode.Name,
                    Describe(beforeNode), After: null, beforeNode.TypeName));
            }
        }

        differences.Sort((x, y) => string.CompareOrdinal(x.DisplayPath, y.DisplayPath));
        return new SnapshotDiffResult(differences, unchanged);
    }

    private static Dictionary<string, ObjectSnapshotNode> IndexByPath(ObjectSnapshotNode root)
    {
        var index = new Dictionary<string, ObjectSnapshotNode>(StringComparer.Ordinal);
        Walk(root, index);
        return index;

        static void Walk(ObjectSnapshotNode node, Dictionary<string, ObjectSnapshotNode> index)
        {
            index[node.StablePath] = node;
            foreach (var child in node.Children)
                Walk(child, index);
        }
    }

    private static bool IsDifferent(ObjectSnapshotNode before, ObjectSnapshotNode after)
        => before.FormattedValue != after.FormattedValue
            || before.IsError != after.IsError
            || before.IsMasked != after.IsMasked;

    private static string? Describe(ObjectSnapshotNode node)
        => node.IsError ? "<error>" : node.FormattedValue;
}
