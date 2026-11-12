using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Paths;
using Xunit;

namespace ObjectViewer.Core.Tests;

internal static class Viewer
{
    public static InspectionOptions Invariant(Func<InspectionOptions, InspectionOptions>? configure = null)
    {
        var options = InspectionOptions.SafeDefault with { Culture = System.Globalization.CultureInfo.InvariantCulture };
        return configure?.Invoke(options) ?? options;
    }

    public static InspectionSession Start(
        object? value,
        string rootName = "root",
        InspectionOptions? options = null,
        Type? declaredType = null)
        => InspectionSessionFactory.Start(new InspectionRequest
        {
            RootName = rootName,
            Value = value,
            DeclaredType = declaredType,
            Options = options ?? InspectionOptions.SafeDefault,
        });

    public static async Task<ObjectNode> Expand(InspectionSession session, ObjectNode node)
    {
        var result = await session.Explorer.LoadChildrenAsync(node, CancellationToken.None);
        Assert.Equal(NodeLoadOutcome.Success, result.Outcome);
        return node;
    }

    public static async Task<ObjectNode> LoadMore(InspectionSession session, ObjectNode collectionNode)
    {
        var result = await session.Explorer.LoadMoreAsync(collectionNode, CancellationToken.None);
        Assert.Equal(NodeLoadOutcome.Success, result.Outcome);
        return collectionNode;
    }

    public static ObjectNode Child(ObjectNode node, string name)
        => node.Children.First(c => c.Name == name);

    public static ObjectNode Item(ObjectNode node, int index)
        => node.Children.First(c => c.Name == $"[{index}]");

    public static ObjectNode Entry(ObjectNode node, string encodedKey)
        => node.Children.First(c => c.Name == $"['{encodedKey}']");
}

public sealed class PathTests
{
    [Fact]
    public void DisplayPath_MembersIndexesAndKeys()
    {
        var path = ObjectPath.RootPath
            .Append(new MemberSegment("Items"))
            .Append(new IndexSegment([12]))
            .Append(new MemberSegment("SerialNumber"));

        Assert.Equal("$.Items[12].SerialNumber", path.ToDisplayPath());
        Assert.Equal("ovp1:/root/member:Items/index:12/member:SerialNumber", path.ToStablePath());
    }

    [Fact]
    public void DisplayPath_NonIdentifierMemberIsEscaped()
    {
        var path = ObjectPath.RootPath.Append(new MemberSegment("with space"));
        Assert.Equal("$['with space']", path.ToDisplayPath());
    }

    [Fact]
    public void DisplayPath_DictionaryKey_EscapesQuotesAndBackslashes()
    {
        var path = ObjectPath.RootPath.Append(new DictionaryKeySegment(PathRenderer.EscapeKey("a'b\\c")));
        Assert.Equal("$['a\\'b\\\\c']", path.ToDisplayPath());
        Assert.Equal("ovp1:/root/dkey:a\\'b\\\\c", path.ToStablePath());
    }

    [Fact]
    public void DisplayPath_MultiDimensionalIndex()
    {
        var path = ObjectPath.RootPath.Append(new IndexSegment([1, 2]));
        Assert.Equal("$[1,2]", path.ToDisplayPath());
    }

    [Fact]
    public void DisplayPath_RootIsDollar()
    {
        Assert.Equal("$", ObjectPath.RootPath.ToDisplayPath());
        Assert.Equal("ovp1:/root", ObjectPath.RootPath.ToStablePath());
    }

    [Fact]
    public void EscapeKey_EscapesControlCharacters()
    {
        var escaped = PathRenderer.EscapeKey("a\nb");
        Assert.Equal("a\\nb", escaped);
    }
}
