namespace ObjectViewer.Core.Formatting;

/// <summary>
/// The final, safe presentation result of a node value (PRD FR-080/084).
/// Search, copy, snapshot and log summaries must only ever consume this data;
/// raw values must never leak through them (NFR-023).
/// </summary>
public sealed record ValueDisplay(
    string Text,
    string TypeText,
    bool IsTruncated = false,
    bool IsMasked = false,
    string? FormatName = null,
    string? ToolTip = null)
{
    public const string MaskedText = "••••••";

    public static ValueDisplay Empty { get; } = new(string.Empty, string.Empty);

    public static ValueDisplay Masked(string typeText)
        => new(MaskedText, typeText, IsMasked: true);
}
