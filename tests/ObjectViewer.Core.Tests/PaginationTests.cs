using ObjectViewer.Core.Graph;
using ObjectViewer.TestModels;
using Xunit;

namespace ObjectViewer.Core.Tests;

public sealed class PaginationTests
{
    [Fact]
    public async Task LargeList_FirstPageOnly_ThenLoadMoreWithoutDuplicates()
    {
        await using var session = Viewer.Start(SampleData.CreateOrder(itemCount: 12_500));
        var root = await Viewer.Expand(session, session.Root!);
        var items = await Viewer.Expand(session, Viewer.Child(root, "Items"));

        Assert.Equal("Count = 12500", items.Display.Text);
        Assert.Equal(101, items.Children.Count); // 100 items + LoadMore marker
        Assert.Equal(ObjectNodeKind.LoadMore, items.Children[^1].Kind);

        await Viewer.LoadMore(session, items);
        Assert.Equal(201, items.Children.Count); // 200 items + marker
        Assert.Equal(ObjectNodeKind.LoadMore, items.Children[^1].Kind);

        var indices = items.Children
            .Where(c => c.Kind == ObjectNodeKind.CollectionItem)
            .Select(c => c.Name)
            .ToArray();
        Assert.Equal(200, indices.Length);
        Assert.Equal(indices.Length, indices.Distinct().Count()); // no duplicates (FR-044)
        Assert.Equal("[199]", indices[^1]);

        var first = Viewer.Item(items, 0);
        Assert.Equal("$.Items[0]", first.Path.ToDisplayPath());
        Assert.Equal(ObjectValueKind.Object, first.ValueKind);
    }

    [Fact]
    public async Task ListBoundaries_ZeroOneHundredOneHundredOne()
    {
        foreach (var (count, expectedChildren, expectedMarker) in new[]
        {
            (0, 0, false),
            (1, 1, false),
            (100, 100, false),
            (101, 100, true),
        })
        {
            await using var session = Viewer.Start(Enumerable.Range(0, count).ToList());
            var root = count > 0
                ? await Viewer.Expand(session, session.Root!)
                : session.Root!;

            Assert.Equal(count == 0 ? "Count = 0" : $"Count = {count}", root.Display.Text);
            Assert.Equal(count > 0, root.CanExpand);
            Assert.Equal(expectedChildren, root.Children.Count(c => c.Kind == ObjectNodeKind.CollectionItem));
            Assert.Equal(expectedMarker, root.Children.Count(c => c.Kind == ObjectNodeKind.LoadMore) == 1);
        }

        // 101 items: LoadMore appends the final single item, then the marker disappears.
        await using var session101 = Viewer.Start(Enumerable.Range(0, 101).ToList());
        var root101 = await Viewer.Expand(session101, session101.Root!);
        await Viewer.LoadMore(session101, root101);
        Assert.Equal(101, root101.Children.Count(c => c.Kind == ObjectNodeKind.CollectionItem));
        Assert.DoesNotContain(root101.Children, c => c.Kind == ObjectNodeKind.LoadMore);
    }

    [Fact]
    public async Task MultiDimensionalArray_ShowsRankAndMultiIndices()
    {
        var array = SampleData.CreateMultiDimensionalArray();
        await using var session = Viewer.Start(array);
        var root = await Viewer.Expand(session, session.Root!);

        Assert.Contains("int[3,4]", root.Display.Text);
        Assert.Equal(12, root.Children.Count(c => c.Kind == ObjectNodeKind.CollectionItem));
        Assert.Equal("[1,2]", Viewer.Child(root, "[1,2]").Name);
        Assert.Equal(12, Viewer.Child(root, "[1,2]").RawValue);
        Assert.Equal("$[2,3]", Viewer.Child(root, "[2,3]").Path.ToDisplayPath());
    }

    [Fact]
    public async Task Dictionary_EntriesPagedAndExpandable_KeyValueChildren()
    {
        await using var session = Viewer.Start(SampleData.CreateSpecialDictionary());
        var root = await Viewer.Expand(session, session.Root!);

        Assert.Equal("Count = 5", root.Display.Text);
        Assert.Equal(5, root.Children.Count(c => c.Kind == ObjectNodeKind.DictionaryEntry));

        var quoteEntry = root.Children.First(c => c.Name.StartsWith("['with"));
        Assert.Equal("$['with\\'quote']", quoteEntry.Path.ToDisplayPath());
        Assert.Contains("Key = \"with'quote\"", quoteEntry.Display.Text);

        var entry = await Viewer.Expand(session, quoteEntry);
        Assert.Equal(["Key", "Value"], entry.Children.Select(c => c.Name).ToArray());
        Assert.Equal(42, Viewer.Child(entry, "Value").RawValue);
        Assert.Equal("$['with\\'quote'].Value", Viewer.Child(entry, "Value").Path.ToDisplayPath());
    }

    [Fact]
    public async Task Dictionary_SensitiveKey_MasksWholeEntry()
    {
        await using var session = Viewer.Start(SampleData.CreateSpecialDictionary());
        var root = await Viewer.Expand(session, session.Root!);

        var passwordEntry = root.Children.Single(c => c.Name == "['password']");
        Assert.True(passwordEntry.IsSensitive);
        Assert.True(passwordEntry.Display.IsMasked);
        Assert.False(passwordEntry.CanExpand);
        Assert.DoesNotContain("super-secret", passwordEntry.Display.Text);
    }

    [Fact]
    public async Task ByteArray_ShowsHexPrefix_PagedItemsUsePresetDisplay()
    {
        await using var session = Viewer.Start(SampleData.CreateBytes(length: 250));
        var root = session.Root!;

        Assert.StartsWith("byte[250]", root.Display.Text);
        await Viewer.Expand(session, root);

        Assert.Equal(100, root.Children.Count(c => c.Kind == ObjectNodeKind.CollectionItem));
        Assert.Equal(ObjectNodeKind.LoadMore, root.Children[^1].Kind);
        Assert.StartsWith("0x", Viewer.Item(root, 0).Display.Text);
    }

    [Fact]
    public async Task EnumerableLimit_AddsExplicitTruncatedNode()
    {
        var dictionary = SampleData.CreateSpecialDictionary();
        await using var session = Viewer.Start(dictionary, options: Viewer.Invariant(o => o with { MaximumEnumerableItems = 3 }));
        var root = await Viewer.Expand(session, session.Root!);

        Assert.Equal(3, root.Children.Count(c => c.Kind == ObjectNodeKind.DictionaryEntry));
        var truncated = root.Children.Single(c => c.Kind == ObjectNodeKind.Truncated);
        Assert.Contains("Enumerable limit (3)", truncated.Display.Text);
    }
}
