using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Members;
using ObjectViewer.Core.Metadata;
using ObjectViewer.Core.Paths;

namespace ObjectViewer.Core.Graph;

/// <summary>
/// A node in the observation tree. It is a projection of an observation,
/// never a serialization of the live object graph (PRD FR-014).
/// RawValue is internal on purpose: it must not be bound to UI, logged, or
/// exposed through copy/search/snapshot (NFR-023).
/// </summary>
public sealed class ObjectNode
{
    private readonly List<ObjectNode> _children = [];

    internal ObjectNode(long nodeId, string name, ObjectNodeKind kind, ObjectPath path, int depth)
    {
        NodeId = nodeId;
        Name = name;
        Kind = kind;
        Path = path;
        Depth = depth;
    }

    public long NodeId { get; }
    public string Name { get; internal set; }
    public ObjectNodeKind Kind { get; internal set; }
    public ObjectValueKind ValueKind { get; internal set; } = ObjectValueKind.Unknown;
    public ObjectPath Path { get; internal set; }
    public int Depth { get; }
    public Type? DeclaredType { get; internal set; }
    public object? RawValue { get; internal set; }
    public Type? RuntimeType => RawValue?.GetType();
    public ValueDisplay Display { get; internal set; } = ValueDisplay.Empty;
    public NodeLoadState LoadState { get; internal set; } = NodeLoadState.NotLoaded;
    public NodeError? Error { get; internal set; }
    public bool CanExpand { get; internal set; }
    public bool IsSensitive { get; internal set; }

    /// <summary>Accessor/owner pair enabling refresh/edit in later versions; null for roots and synthetic nodes.</summary>
    public MemberAccessor? Accessor { get; internal set; }
    public object? Owner { get; internal set; }

    public ObjectNode? Parent { get; internal set; }
    public IReadOnlyList<ObjectNode> Children => _children;

    /// <summary>Bumped on every refresh; stale async results must be discarded (Proposal §12.2).</summary>
    public int Version { get; internal set; }

    /// <summary>Set when a refresh observed a changed value/type; consumed by change highlighting (PRD FR-105).</summary>
    public bool HasChanged { get; internal set; }

    /// <summary>
    /// Adapter-specific continuation state for paging (offset cursor, materialized
    /// dictionary state, format-specific entry caches). Part of the adapter contract:
    /// adapters are stateless, so per-node paging state lives here.
    /// </summary>
    public object? Continuation { get; set; }

    /// <summary>Ancestor reference chain used for cycle detection (DetectCyclesOnly mode).</summary>
    internal AncestorChain? Ancestors { get; set; }

    internal void ReplaceChildren(IReadOnlyList<ObjectNode> children)
    {
        _children.Clear();
        foreach (var child in children)
            AppendChildCore(child);
    }

    internal void AppendChildren(IEnumerable<ObjectNode> children)
    {
        foreach (var child in children)
            AppendChildCore(child);
    }

    internal void AppendChildCore(ObjectNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    internal void RemoveChildAt(int index)
    {
        var child = _children[index];
        child.Parent = null;
        _children.RemoveAt(index);
    }
}

/// <summary>
/// Immutable ancestor chain of reference identities, from root to the current node's parent.
/// Lookup is a linear scan; depth is bounded by <see cref="InspectionOptions.MaximumDepth"/>.
/// </summary>
public sealed class AncestorChain(AncestorChain? previous, object value, ObjectPath path)
{
    public AncestorChain? Previous { get; } = previous;
    public object Value { get; } = value;
    public ObjectPath Path { get; } = path;

    public ObjectPath? Find(object value)
    {
        for (var current = this; current is not null; current = current.Previous)
        {
            if (ReferenceEquals(current.Value, value))
                return current.Path;
        }
        return null;
    }
}
