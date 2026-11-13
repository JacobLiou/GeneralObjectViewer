namespace ObjectViewer.Core.Graph;

public enum NodeEditOutcome
{
    /// <summary>The value was converted, written, re-read and the node display updated.</summary>
    Applied,
    /// <summary>The node is not editable (policy, missing setter, sensitive, structural node...).</summary>
    NotEditable,
    /// <summary>The text could not be converted to the member type (FR-122).</summary>
    ConversionFailed,
    /// <summary>The setter threw; the error is surfaced on the node (FR-124).</summary>
    SetterFailed,
    /// <summary>The session was replaced or disposed before the write reached the object.</summary>
    Stale,
}

/// <summary>Outcome of a controlled edit (PRD FR-122); Error is display-ready text.</summary>
public sealed record NodeEditResult(NodeEditOutcome Outcome, string? Error = null)
{
    public bool Success => Outcome == NodeEditOutcome.Applied;

    public static NodeEditResult Applied()
        => new(NodeEditOutcome.Applied);

    public static NodeEditResult NotEditable(string reason)
        => new(NodeEditOutcome.NotEditable, reason);

    public static NodeEditResult ConversionFailed(string error)
        => new(NodeEditOutcome.ConversionFailed, error);

    public static NodeEditResult SetterFailed(string error)
        => new(NodeEditOutcome.SetterFailed, error);

    public static NodeEditResult Stale()
        => new(NodeEditOutcome.Stale);
}
