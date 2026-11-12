namespace ObjectViewer.Core.Graph;

/// <summary>
/// The structural role of a node inside the observation tree.
/// PRD FR-013 is covered by the union of <see cref="ObjectNodeKind"/> (role) and
/// <see cref="ObjectValueKind"/> (value nature): e.g. PRD's "Property" is a role,
/// while "String" / "Collection" describe the observed value.
/// </summary>
public enum ObjectNodeKind
{
    Root,
    Property,
    Field,
    CollectionItem,
    DictionaryEntry,
    LoadMore,
    Loading,
    Error,
    Truncated,
    Reference,
}

/// <summary>The observed value nature of a node, resolved by the adapter registry.</summary>
public enum ObjectValueKind
{
    Unknown,
    Null,
    Primitive,
    String,
    Enum,
    ByteArray,
    Object,
    Collection,
    Dictionary,
    Enumerable,
    Task,
    Exception,
    Dynamic,
    Reference,
}

public enum NodeLoadState
{
    NotLoaded,
    Loading,
    Loaded,
    Cancelled,
    Failed,
    Truncated,
}

public enum NodeLoadOutcome
{
    Success,
    NotExpandable,
    AlreadyLoading,
    Stale,
    Cancelled,
    Failed,
}

/// <summary>
/// Reference tracking mode (PRD FR-061). DetectCyclesOnly checks the ancestor chain only;
/// ShowSharedReferences maintains a session-wide first-seen table.
/// </summary>
public enum ReferenceMode
{
    DetectCyclesOnly,
    ShowSharedReferences,
}

public enum EditPolicyMode
{
    ReadOnly,
    PublicSetters,
    ExplicitlyAllowedMembers,
    FullTrust,
}
