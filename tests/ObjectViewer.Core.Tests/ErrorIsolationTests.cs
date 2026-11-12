using ObjectViewer.Core.Graph;
using ObjectViewer.TestModels;
using Xunit;

namespace ObjectViewer.Core.Tests;

public sealed class ErrorIsolationTests
{
    [Fact]
    public async Task ThrowingGetter_ProducesLocalErrorNode_SiblingsSurvive()
    {
        await using var session = Viewer.Start(SampleData.CreateFailingDevice());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var throwing = Viewer.Child(root, "ThrowingProp");
        Assert.Equal(NodeLoadState.Failed, throwing.LoadState);
        Assert.NotNull(throwing.Error);
        Assert.Equal("InvalidOperationException", throwing.Error.ExceptionType);
        Assert.Contains("offline", throwing.Error.Message);
        Assert.Contains("InvalidOperationException", throwing.Display.Text);
        Assert.False(throwing.CanExpand);

        var ok = Viewer.Child(root, "OkProp");
        Assert.Equal(NodeLoadState.NotLoaded, ok.LoadState);
        Assert.Equal("\"ok\"", ok.Display.Text);
        Assert.Null(ok.Error);

        Assert.Equal(4, root.Children.Count); // all members present despite the failure
    }

    [Fact]
    public async Task LongString_IsTruncatedWithMarker()
    {
        await using var session = Viewer.Start(SampleData.CreateFailingDevice());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var longString = Viewer.Child(root, "LongStringProp");
        Assert.True(longString.Display.IsTruncated);
        Assert.EndsWith("…\"", longString.Display.Text);
        Assert.Contains("Length = 5000", longString.Display.ToolTip);
    }

    [Fact]
    public async Task NullableMember_ShowsNullWithDeclaredType()
    {
        await using var session = Viewer.Start(SampleData.CreateFailingDevice());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var nullable = Viewer.Child(root, "NullableProp");
        Assert.Equal(ObjectValueKind.Null, nullable.ValueKind);
        Assert.Equal("null", nullable.Display.Text);
        Assert.Equal("int?", nullable.Display.TypeText);
        Assert.False(nullable.CanExpand);
    }

    [Fact]
    public void NodeError_UnwrapsTargetInvocationException()
    {
        var inner = new InvalidOperationException("inner failure");
        var wrapped = new System.Reflection.TargetInvocationException(inner);

        var error = NodeError.FromException(wrapped);

        Assert.Equal("InvalidOperationException", error.ExceptionType);
        Assert.Equal("inner failure", error.Message);
    }
}
