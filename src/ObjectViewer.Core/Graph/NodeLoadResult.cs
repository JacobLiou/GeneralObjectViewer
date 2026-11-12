namespace ObjectViewer.Core.Graph;

public sealed record NodeLoadResult(
    NodeLoadOutcome Outcome,
    IReadOnlyList<ObjectNode> Children,
    NodeError? Error = null)
{
    public static NodeLoadResult Success(IReadOnlyList<ObjectNode> children)
        => new(NodeLoadOutcome.Success, children);

    public static NodeLoadResult NotExpandable()
        => new(NodeLoadOutcome.NotExpandable, []);

    public static NodeLoadResult AlreadyLoading()
        => new(NodeLoadOutcome.AlreadyLoading, []);

    public static NodeLoadResult Stale()
        => new(NodeLoadOutcome.Stale, []);

    public static NodeLoadResult Cancelled()
        => new(NodeLoadOutcome.Cancelled, []);
}
