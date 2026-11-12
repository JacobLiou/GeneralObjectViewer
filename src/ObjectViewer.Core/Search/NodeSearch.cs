using ObjectViewer.Core.Graph;

namespace ObjectViewer.Core.Search;

public enum SearchField
{
    Name = 1,
    Value = 2,
    Type = 4,
    Path = 8,
    All = Name | Value | Type | Path,
}

/// <summary>Search input; matching is a case-insensitive contains on the chosen fields.</summary>
public sealed record SearchQuery(
    string Text,
    SearchField Fields = SearchField.All,
    StringComparison Comparison = StringComparison.OrdinalIgnoreCase);

/// <summary>A match inside the live, loaded tree (PRD FR-090/091).</summary>
public sealed record SearchResult(ObjectNode Node, SearchField MatchedField);

public static class NodeSearch
{
    /// <summary>
    /// Searches only already-loaded nodes; never triggers getters, loads or enumeration
    /// (V0.2 acceptance #1). Matching consumes masked display data only (NFR-023).
    /// </summary>
    public static IReadOnlyList<SearchResult> SearchLoaded(ObjectNode root, SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(root);
        var results = new List<SearchResult>();
        if (string.IsNullOrEmpty(query.Text))
            return results;

        Walk(root, query, results);
        return results;

        static void Walk(ObjectNode node, SearchQuery query, List<SearchResult> results)
        {
            foreach (var child in node.Children)
            {
                if (child.Kind is ObjectNodeKind.LoadMore or ObjectNodeKind.Truncated or ObjectNodeKind.Loading)
                    continue;

                var field = Match(child, query);
                if (field is not null)
                    results.Add(new SearchResult(child, field.Value));

                Walk(child, query, results);
            }
        }
    }

    private static SearchField? Match(ObjectNode node, SearchQuery query)
    {
        if (query.Fields.HasFlag(SearchField.Name)
            && node.Name.Contains(query.Text, query.Comparison))
            return SearchField.Name;

        if (query.Fields.HasFlag(SearchField.Value)
            && node.Display.Text.Contains(query.Text, query.Comparison))
            return SearchField.Value;

        if (query.Fields.HasFlag(SearchField.Type)
            && node.Display.TypeText.Contains(query.Text, query.Comparison))
            return SearchField.Type;

        if (query.Fields.HasFlag(SearchField.Path)
            && node.Path.ToDisplayPath().Contains(query.Text, query.Comparison))
            return SearchField.Path;

        return null;
    }
}
