using ObjectViewer.Core.Graph;
using ObjectViewer.TestModels;
using Xunit;

namespace ObjectViewer.Core.Tests;

public sealed class RefreshTests
{
    [Fact]
    public async Task Refresh_RereadsValueAndFlagsChange()
    {
        var order = SampleData.CreateOrder(itemCount: 2);
        await using var session = Viewer.Start(order);
        var root = await Viewer.Expand(session, session.Root!);

        order.OrderId = 42;
        var result = await session.Explorer.RefreshAsync(root, RefreshScope.LoadedTree, CancellationToken.None);

        var orderId = Viewer.Child(root, "OrderId");
        Assert.True(orderId.HasChanged);
        Assert.Equal("42", orderId.Display.Text);
        Assert.True(result.ChangedCount >= 1);
    }

    [Fact]
    public async Task Refresh_UnchangedValuesAreNotFlagged()
    {
        var order = SampleData.CreateOrder(itemCount: 2);
        await using var session = Viewer.Start(order);
        var root = await Viewer.Expand(session, session.Root!);

        await session.Explorer.RefreshAsync(root, RefreshScope.LoadedTree, CancellationToken.None);

        Assert.False(Viewer.Child(root, "OrderId").HasChanged);
        Assert.False(Viewer.Child(root, "Customer").HasChanged);
    }

    [Fact]
    public async Task Refresh_TypeChange_InvalidatesLoadedChildren()
    {
        var holder = new RefreshTestHolder { Payload = "text" };
        await using var session = Viewer.Start(holder);
        var root = await Viewer.Expand(session, session.Root!);
        var payload = Viewer.Child(root, "Payload");
        Assert.Equal("\"text\"", payload.Display.Text);

        holder.Payload = 42;
        await session.Explorer.RefreshAsync(root, RefreshScope.Subtree, CancellationToken.None);

        Assert.Equal("42", payload.Display.Text);
        Assert.True(payload.HasChanged);
    }

    [Fact]
    public async Task Refresh_SameReference_KeepsLoadedChildren()
    {
        var order = SampleData.CreateOrder(itemCount: 2);
        await using var session = Viewer.Start(order);
        var root = await Viewer.Expand(session, session.Root!);
        var customer = Viewer.Child(root, "Customer");
        var expandedCustomer = await Viewer.Expand(session, customer);
        var childCount = expandedCustomer.Children.Count;

        await session.Explorer.RefreshAsync(root, RefreshScope.LoadedTree, CancellationToken.None);

        // The customer instance did not change: children stay loaded (FR-101).
        Assert.Equal(NodeLoadState.Loaded, customer.LoadState);
        Assert.Equal(childCount, customer.Children.Count);
    }

    [Fact]
    public async Task Refresh_CollectionWithoutAccessor_IsInvalidatedForReload()
    {
        var list = new List<string> { "a", "b" };
        await using var session = Viewer.Start(list);
        var root = await Viewer.Expand(session, session.Root!);
        Assert.Equal(2, root.Children.Count);

        await session.Explorer.RefreshAsync(root, RefreshScope.Node, CancellationToken.None);

        Assert.Equal(NodeLoadState.NotLoaded, root.LoadState);
        Assert.Empty(root.Children);
    }

    [Fact]
    public async Task Refresh_FailingGetter_ShowsErrorAndKeepsSiblings()
    {
        var device = SampleData.CreateFailingDevice();
        await using var session = Viewer.Start(device);
        var root = await Viewer.Expand(session, session.Root!);

        await session.Explorer.RefreshAsync(root, RefreshScope.Subtree, CancellationToken.None);

        var throwing = Viewer.Child(root, "ThrowingProp");
        Assert.NotNull(throwing.Error);
        Assert.Contains("offline", throwing.Display.Text);
        Assert.Null(Viewer.Child(root, "OkProp").Error);
    }
}

public sealed class RefreshTestHolder
{
    public object? Payload { get; set; }
}
