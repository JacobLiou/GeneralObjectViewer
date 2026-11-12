using ObjectViewer.Core.Graph;
using ObjectViewer.TestModels;
using Xunit;

namespace ObjectViewer.Core.Tests;

public sealed class SessionAndLimitsTests
{
    [Fact]
    public async Task NullRoot_ShowsDeclaredTypeAndNull()
    {
        await using var session = Viewer.Start(null, rootName: "order", declaredType: typeof(IAnimal));
        var root = session.Root!;

        Assert.Equal(ObjectValueKind.Null, root.ValueKind);
        Assert.Equal("null", root.Display.Text);
        Assert.Equal("IAnimal", root.Display.TypeText);
        Assert.Equal(typeof(IAnimal), root.DeclaredType);
        Assert.Null(root.RuntimeType);
        Assert.False(root.CanExpand);
    }

    [Fact]
    public async Task DeclaredTypeAndRuntimeType_AreBothAvailable()
    {
        await using var session = Viewer.Start(SampleData.CreateDog(), declaredType: typeof(IAnimal));
        var root = session.Root!;

        Assert.Equal(typeof(IAnimal), root.DeclaredType);
        Assert.Equal(typeof(Dog), root.RuntimeType);
        Assert.Equal("{Dog}", root.Display.Text);
        Assert.Equal("Dog", root.Display.TypeText);

        await Viewer.Expand(session, root);
        var name = Viewer.Child(root, "Name");
        Assert.Equal(typeof(string), name.DeclaredType);
        Assert.Equal("string", name.Display.TypeText);
        Assert.Equal("\"Lucky\"", name.Display.Text);
    }

    [Fact]
    public async Task NodeBudget_ExhaustionProducesTruncatedNode()
    {
        await using var session = Viewer.Start(
            Enumerable.Range(0, 12).ToList(),
            options: Viewer.Invariant(o => o with { MaximumNodesPerSession = 5 }));
        var root = await Viewer.Expand(session, session.Root!);

        Assert.Equal(5, root.Children.Count(c => c.Kind == ObjectNodeKind.CollectionItem));
        var truncated = root.Children.Single(c => c.Kind == ObjectNodeKind.Truncated);
        Assert.Contains("Node budget (5)", truncated.Display.Text);
        Assert.DoesNotContain(root.Children, c => c.Kind == ObjectNodeKind.LoadMore);
    }

    [Fact]
    public async Task DisposedSession_LoadIsCancelled()
    {
        var session = Viewer.Start(SampleData.CreateOrder(itemCount: 1));
        await session.DisposeAsync();

        var result = await session.Explorer.LoadChildrenAsync(session.Root!, CancellationToken.None);
        Assert.Equal(NodeLoadOutcome.Cancelled, result.Outcome);
        Assert.Equal(NodeLoadState.Cancelled, session.Root!.LoadState);
    }

    [Fact]
    public async Task SessionDispose_RecordsDiagnosticAndCancelsToken()
    {
        var session = Viewer.Start(new object());
        Assert.False(session.SessionToken.IsCancellationRequested);

        await session.DisposeAsync();

        Assert.True(session.IsDisposed);
        Assert.True(session.SessionToken.IsCancellationRequested);
        Assert.Contains(session.Diagnostics.Events, e => e.Kind == "SessionStarted");
        Assert.Contains(session.Diagnostics.Events, e => e.Kind == "SessionDisposed");
    }

    [Fact]
    public async Task RootSwitch_CreatesIndependentSessions()
    {
        var first = Viewer.Start(SampleData.CreateDog());
        await Viewer.Expand(first, first.Root!);

        var second = Viewer.Start(SampleData.CreateCredentials());
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.NotSame(first.Root, second.Root);
        Assert.DoesNotContain(second.Diagnostics.Events, e => e.Kind == "SensitiveValueMasked");

        await first.DisposeAsync();
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public async Task SensitiveMasking_IsRecordedInDiagnostics()
    {
        await using var session = Viewer.Start(SampleData.CreateCredentials());
        var root = await Viewer.Expand(session, session.Root!);

        var masked = session.Diagnostics.Events.Count(e => e.Kind == "SensitiveValueMasked");
        Assert.Equal(4, masked); // Password, ApiToken, ConnectionString, CustomSecret
    }
}
