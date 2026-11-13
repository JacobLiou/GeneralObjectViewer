using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Metadata;
using ObjectViewer.Core.Paths;

namespace ObjectViewer.Core.Adapters;

/// <summary>Input context for adapter matching.</summary>
public readonly record struct ObjectAdapterContext(
    object? Value,
    Type? DeclaredType,
    System.Reflection.MemberInfo? SourceMember);

/// <summary>Summary description of a value, produced without any recursion.</summary>
public sealed record ObjectNodeDescriptor(
    ObjectValueKind ValueKind,
    ValueDisplay Display,
    bool CanExpand);

/// <summary>
/// A child candidate produced by an adapter. ReadError carries an isolated getter
/// failure (PRD FR-030/033); IsSensitive marks values that must be masked before
/// display (NFR-023). PresetDisplay lets adapters pre-format synthetic items.
/// CanEdit is the policy-effective editability (FR-121); the explorer re-checks
/// it before creating the node and again before every write.
/// </summary>
public sealed record ObjectMemberDescriptor(
    string Name,
    object? Value,
    Type? DeclaredType,
    ObjectNodeKind Kind,
    ObjectPathSegment PathSegment,
    MemberAccessor? Accessor = null,
    object? Owner = null,
    Exception? ReadError = null,
    bool IsSensitive = false,
    ValueDisplay? PresetDisplay = null,
    bool CanEdit = false);

/// <summary>
/// One page of children. NextCursor non-null means a LoadMore node must be appended;
/// IsTruncated means a hard limit was hit and a Truncated node must be shown (PRD FR-044/046).
/// </summary>
public sealed record ChildPageResult(
    IReadOnlyList<ObjectMemberDescriptor> Children,
    object? NextCursor = null,
    bool IsTruncated = false)
{
    public static ChildPageResult Empty { get; } = new([]);
}

/// <summary>
/// Type-specific inspection behavior (Proposal §8). Adapters are stateless;
/// paging state lives on the node (ObjectNode.Continuation).
/// </summary>
public interface IObjectAdapter
{
    int Priority { get; }

    string Name { get; }

    bool CanHandle(in ObjectAdapterContext context);

    ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session);

    Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken);
}
