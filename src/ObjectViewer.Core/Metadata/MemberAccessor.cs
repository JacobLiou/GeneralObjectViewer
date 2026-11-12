namespace ObjectViewer.Core.Metadata;

public enum MemberKind
{
    Property,
    Field,
}

/// <summary>
/// Cached member accessor with a compiled getter/setter (Proposal §10).
/// Carries only type structure, never object instances.
/// </summary>
public sealed class MemberAccessor
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required Type ValueType { get; init; }
    public required System.Reflection.MemberInfo MemberInfo { get; init; }
    public required MemberKind Kind { get; init; }
    public required bool IsPublic { get; init; }
    public required bool IsStatic { get; init; }
    public required int Order { get; init; }
    public required bool IsBrowsable { get; init; }
    public required Func<object, object?> Getter { get; init; }
    public Action<object, object?>? Setter { get; init; }

    public bool CanWrite => Setter is not null;
}

public sealed record TypeMetadata(Type Type, IReadOnlyList<MemberAccessor> Members);
