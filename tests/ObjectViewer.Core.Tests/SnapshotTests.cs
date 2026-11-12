using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Snapshots;
using ObjectViewer.TestModels;
using Xunit;

namespace ObjectViewer.Core.Tests;

public sealed class SnapshotTests
{
    [Fact]
    public async Task Snapshot_CapturesLoadedNodesOnly()
    {
        await using var session = Viewer.Start(SampleData.CreateCredentials(), rootName: "credentials");
        var root = await Viewer.Expand(session, session.Root!);

        var snapshot = SnapshotBuilder.Capture("before", root, session.Options);

        Assert.Equal("credentials", snapshot.Root.Name);
        // Credentials has 5 public members; paging markers are never snapshotted.
        Assert.Equal(5, snapshot.Root.Children.Count);
        Assert.Equal(
            ["UserName", "Password", "ApiToken", "ConnectionString", "CustomSecret"],
            snapshot.Root.Children.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task Snapshot_MaskedValuesStoreMaskOnly()
    {
        await using var session = Viewer.Start(SampleData.CreateCredentials());
        var root = await Viewer.Expand(session, session.Root!);

        var snapshot = SnapshotBuilder.Capture("masked", root, session.Options);
        var walk = string.Join("|", Flatten(snapshot.Root).Select(n => n.FormattedValue ?? ""));

        Assert.DoesNotContain("P@ssw0rd", walk);
        Assert.DoesNotContain("eyJhbGci", walk);
        Assert.Contains(ValueDisplay.MaskedText, walk);
    }

    [Fact]
    public async Task Diff_IdenticalTrees_ReportNoDifferences()
    {
        await using var session = Viewer.Start(SampleData.CreateCredentials());
        var root = await Viewer.Expand(session, session.Root!);

        var before = SnapshotBuilder.Capture("a", root, session.Options);
        var after = SnapshotBuilder.Capture("b", root, session.Options);

        var diff = SnapshotDiffer.Diff(before, after);
        Assert.Empty(diff.Differences);
        Assert.Equal(Flatten(before.Root).Count(), diff.UnchangedCount);
    }

    [Fact]
    public async Task Diff_ValueChange_IsReportedAsChanged()
    {
        var order = SampleData.CreateOrder(itemCount: 2);
        await using var session = Viewer.Start(order);
        var root = await Viewer.Expand(session, session.Root!);

        var before = SnapshotBuilder.Capture("before", root, session.Options);
        order.OrderId = 99;
        await session.Explorer.RefreshAsync(root, RefreshScope.LoadedTree, CancellationToken.None);
        var after = SnapshotBuilder.Capture("after", root, session.Options);

        var diff = SnapshotDiffer.Diff(before, after);
        var changed = Assert.Single(diff.Differences);
        Assert.Equal(SnapshotNodeState.Changed, changed.State);
        Assert.EndsWith("OrderId", changed.DisplayPath);
        Assert.Equal("20261112001", changed.Before);
        Assert.Equal("99", changed.After);
    }

    [Fact]
    public async Task Diff_AddedAndRemovedDictionaryEntries()
    {
        var dictionary = new Dictionary<string, object> { ["a"] = 1, ["b"] = 2 };
        await using var session = Viewer.Start(dictionary);
        var root = await Viewer.Expand(session, session.Root!);

        var before = SnapshotBuilder.Capture("before", root, session.Options);

        dictionary.Remove("b");
        dictionary["c"] = 3;
        root = await ResetAndReload(session, root);

        var after = SnapshotBuilder.Capture("after", root, session.Options);
        var diff = SnapshotDiffer.Diff(before, after);

        Assert.Contains(diff.Differences, d => d.State == SnapshotNodeState.Removed && d.DisplayPath.EndsWith("['b']"));
        Assert.Contains(diff.Differences, d => d.State == SnapshotNodeState.Added && d.DisplayPath.EndsWith("['c']"));
        Assert.DoesNotContain(diff.Differences, d => d.DisplayPath.EndsWith("['a']"));
    }

    [Fact]
    public async Task Diff_TypeChange_IsReportedSeparately()
    {
        var holder = new RefreshTestHolder { Payload = "text" };
        await using var session = Viewer.Start(holder);
        var root = await Viewer.Expand(session, session.Root!);

        var before = SnapshotBuilder.Capture("before", root, session.Options);
        holder.Payload = 42;
        await session.Explorer.RefreshAsync(root, RefreshScope.Subtree, CancellationToken.None);
        var after = SnapshotBuilder.Capture("after", root, session.Options);

        var diff = SnapshotDiffer.Diff(before, after);
        var typeChanged = Assert.Single(diff.Differences);
        Assert.Equal(SnapshotNodeState.TypeChanged, typeChanged.State);
    }

    [Fact]
    public async Task Snapshot_StablePathsWorkAcrossSessions()
    {
        // Two separate sessions over structurally identical graphs must produce
        // identical stable paths so cross-session diff remains meaningful (FR-113).
        await using var first = Viewer.Start(SampleData.CreateCredentials());
        var firstRoot = await Viewer.Expand(first, first.Root!);
        await using var second = Viewer.Start(SampleData.CreateCredentials());
        var secondRoot = await Viewer.Expand(second, second.Root!);

        var snapshotA = SnapshotBuilder.Capture("a", firstRoot, first.Options);
        var snapshotB = SnapshotBuilder.Capture("b", secondRoot, second.Options);
        var diff = SnapshotDiffer.Diff(snapshotA, snapshotB);

        Assert.Empty(diff.Differences);
    }

    private static async Task<ObjectNode> ResetAndReload(InspectionSession session, ObjectNode root)
    {
        await session.Explorer.RefreshAsync(root, RefreshScope.Node, CancellationToken.None);
        return await Viewer.Expand(session, root);
    }

    private static IEnumerable<ObjectSnapshotNode> Flatten(ObjectSnapshotNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }
}
