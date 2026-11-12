namespace ObjectViewer.Core.Graph;

public enum RefreshScope
{
    /// <summary>Refresh only the selected node.</summary>
    Node,

    /// <summary>Refresh the selected node and its loaded descendants.</summary>
    Subtree,

    /// <summary>Refresh every loaded node from the root (PRD FR-100).</summary>
    LoadedTree,
}

public sealed record NodeRefreshResult(int RefreshedCount, int ChangedCount, int InvalidatedCount);
