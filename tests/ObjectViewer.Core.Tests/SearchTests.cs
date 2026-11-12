using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Search;
using ObjectViewer.TestModels;
using Xunit;

namespace ObjectViewer.Core.Tests;

public sealed class SearchTests
{
    [Fact]
    public async Task LoadedSearch_FindsNamesWithoutTriggeringLoads()
    {
        await using var session = Viewer.Start(SampleData.CreateOrder(itemCount: 10));
        var root = await Viewer.Expand(session, session.Root!);

        // Only the root level is loaded at this point.
        var results = NodeSearch.SearchLoaded(root, new SearchQuery("Items"));
        Assert.Single(results);
        Assert.Equal("Items", results[0].Node.Name);
        Assert.Equal(SearchField.Name, results[0].MatchedField);
        Assert.Equal(NodeLoadState.NotLoaded, Viewer.Child(root, "Items").LoadState); // no getters triggered
    }

    [Fact]
    public async Task LoadedSearch_FindsValuesAndPaths()
    {
        await using var session = Viewer.Start(SampleData.CreateOrder(itemCount: 10));
        var root = await Viewer.Expand(session, session.Root!);

        var byValue = NodeSearch.SearchLoaded(root, new SearchQuery("20261112001"));
        Assert.Single(byValue);
        Assert.Equal("OrderId", byValue[0].Node.Name);

        var byPath = NodeSearch.SearchLoaded(root, new SearchQuery("$.Customer"));
        Assert.Contains(byPath, r => r.Node.Name == "Customer");
    }

    [Fact]
    public async Task LoadedSearch_MaskedValuesNeverMatchRawContent()
    {
        await using var session = Viewer.Start(SampleData.CreateCredentials());
        var root = await Viewer.Expand(session, session.Root!);

        Assert.Empty(NodeSearch.SearchLoaded(root, new SearchQuery("P@ssw0rd")));
        Assert.Empty(NodeSearch.SearchLoaded(root, new SearchQuery("eyJhbGci")));
        Assert.Single(NodeSearch.SearchLoaded(root, new SearchQuery("Password"))); // name is still searchable
    }

    [Fact]
    public async Task LoadedSearch_IgnoresPagingMarkers()
    {
        await using var session = Viewer.Start(Enumerable.Range(0, 150).ToList());
        var root = await Viewer.Expand(session, session.Root!);

        Assert.Equal(100, root.Children.Count(c => c.Kind == ObjectNodeKind.CollectionItem));
        var results = NodeSearch.SearchLoaded(root, new SearchQuery("[1")); // would match "[1xx]" items
        Assert.All(results, r => Assert.NotEqual(ObjectNodeKind.LoadMore, r.Node.Kind));
    }

    [Fact]
    public async Task DeepSearch_FindsUnloadedContent_WithoutLoadingTree()
    {
        await using var session = Viewer.Start(SampleData.CreateOrder(itemCount: 10));
        var root = await Viewer.Expand(session, session.Root!); // root only

        var service = new DeepSearchService(session);
        var result = await service.SearchAsync(new SearchQuery("SerialNumber"), DeepSearchOptions.Default, CancellationToken.None);

        Assert.NotEmpty(result.Matches);
        Assert.Contains(result.Matches, m => m.Name == "SerialNumber");
        Assert.All(result.Matches, m => Assert.StartsWith("$.Items", m.Path.ToDisplayPath()));

        // The live tree must stay untouched.
        Assert.Equal(NodeLoadState.NotLoaded, Viewer.Child(root, "Items").LoadState);
    }

    [Fact]
    public async Task DeepSearch_RespectsNodeBudgetAndReportsTruncation()
    {
        await using var session = Viewer.Start(SampleData.CreateOrder(itemCount: 500));
        _ = await Viewer.Expand(session, session.Root!);

        var service = new DeepSearchService(session);
        var options = new DeepSearchOptions(MaximumDepth: 8, MaximumNodes: 5, MaximumItemsPerCollection: 100);
        var result = await service.SearchAsync(new SearchQuery("a"), options, CancellationToken.None);

        Assert.True(result.Truncated);
        Assert.True(result.NodesVisited <= 6);
    }

    [Fact]
    public async Task DeepSearch_SensitiveValuesAreNotMatched()
    {
        await using var session = Viewer.Start(SampleData.CreateCredentials());
        _ = await Viewer.Expand(session, session.Root!);

        var service = new DeepSearchService(session);
        var result = await service.SearchAsync(new SearchQuery("P@ssw0rd"), DeepSearchOptions.Default, CancellationToken.None);
        Assert.DoesNotContain(result.Matches, m => m.ValueText.Contains("P@ssw0rd", StringComparison.Ordinal));

        result = await service.SearchAsync(new SearchQuery("Password"), DeepSearchOptions.Default, CancellationToken.None);
        Assert.Contains(result.Matches, m => m.Name == "Password"); // name matching is allowed
    }

    [Fact]
    public async Task DeepSearch_DoesNotRecurseInfinitelyOnCycles()
    {
        await using var session = Viewer.Start(SampleData.CreateDog());
        var service = new DeepSearchService(session);
        var result = await service.SearchAsync(new SearchQuery("Lucky"), DeepSearchOptions.Default, CancellationToken.None);

        Assert.NotEmpty(result.Matches);
        Assert.True(result.NodesVisited < 20); // dog <-> owner cycle terminates quickly
    }
}
