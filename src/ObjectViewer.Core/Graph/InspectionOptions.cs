using System.Globalization;

namespace ObjectViewer.Core.Graph;

/// <summary>
/// Immutable inspection options; defaults follow PRD §12.
/// A snapshot is taken per session so that mid-load configuration changes cannot
/// produce inconsistent results (Proposal §23.3).
/// </summary>
public sealed record InspectionOptions
{
    public int CollectionPageSize { get; init; } = 100;
    public int MaximumDepth { get; init; } = 32;
    public int MaximumStringLength { get; init; } = 2_000;
    public int MaximumEnumerableItems { get; init; } = 10_000;
    public int MaximumNodesPerSession { get; init; } = 100_000;
    public int MaximumConcurrentLoads { get; init; } = 4;
    public TimeSpan GetterWarningThreshold { get; init; } = TimeSpan.FromSeconds(2);
    public ReferenceMode ReferenceMode { get; init; } = ReferenceMode.DetectCyclesOnly;
    public EditPolicyMode EditPolicy { get; init; } = EditPolicyMode.ReadOnly;
    public bool ShowNonPublicMembers { get; init; }
    public bool ShowStaticMembers { get; init; }
    public bool AutoEnumerateUnknownSequences { get; init; }
    public bool ExecuteQueryable { get; init; }
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;
    public Formatting.ValueDisplayFormat ValueFormat { get; init; } = Formatting.ValueDisplayFormat.Default;

    public static InspectionOptions SafeDefault { get; } = new();
}

/// <summary>Root inspection request (PRD FR-001/003).</summary>
public sealed record InspectionRequest
{
    public required string RootName { get; init; }
    public object? Value { get; init; }
    public Type? DeclaredType { get; init; }
    public InspectionOptions Options { get; init; } = InspectionOptions.SafeDefault;

    /// <summary>
    /// Host-registered adapters (e.g. format adapters for imported data sources);
    /// merged with the defaults and resolved by the same deterministic priority (PRD FR-130/134).
    /// </summary>
    public IReadOnlyList<Adapters.IObjectAdapter>? CustomAdapters { get; init; }

    public IReadOnlyList<Formatting.IObjectValueFormatter>? CustomFormatters { get; init; }
}

/// <summary>Offset-based paging cursor for indexable collections.</summary>
public sealed record CollectionCursor(int Offset, int PageSize);
