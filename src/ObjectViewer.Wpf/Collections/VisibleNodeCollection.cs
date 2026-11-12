using System.Collections.ObjectModel;
using ObjectViewer.Core.Graph;
using ObjectViewer.Wpf.ViewModels;

namespace ObjectViewer.Wpf.Collections;

/// <summary>
/// Flattened visible-row projection of the logical node tree (Proposal §16.3):
/// expanding inserts descendant rows after the node's row, collapsing removes
/// all rows with a greater depth. Update cost is proportional to the affected
/// visible subtree, never the whole tree.
/// </summary>
public sealed class VisibleNodeCollection : ObservableCollection<VisibleObjectNode>
{
    public void Reset(ObjectNodeViewModel root)
    {
        Clear();
        Add(new VisibleObjectNode(root));
    }

    public int Expand(ObjectNodeViewModel node)
    {
        var index = RowIndexOf(node);
        if (index < 0)
            return 0;
        var insertAt = index + 1;
        return InsertSubtree(node, ref insertAt);
    }

    private int InsertSubtree(ObjectNodeViewModel node, ref int index)
    {
        var added = 0;
        foreach (var child in node.ChildVms)
        {
            Insert(index++, new VisibleObjectNode(child));
            added++;
            if (child.IsExpanded)
                added += InsertSubtree(child, ref index);
        }
        return added;
    }

    public int Collapse(ObjectNodeViewModel node)
    {
        var index = RowIndexOf(node) + 1;
        var removed = 0;
        while (index < Count && this[index].Node.Node.Depth > node.Node.Depth)
        {
            RemoveAt(index);
            removed++;
        }
        return removed;
    }

    /// <summary>Replaces a LoadMore marker row with the rows of the next page.</summary>
    public void ReplaceMarker(ObjectNodeViewModel marker, IReadOnlyList<ObjectNodeViewModel> newRows)
    {
        var index = RowIndexOf(marker);
        if (index < 0)
            return;
        RemoveAt(index);
        foreach (var row in newRows)
            Insert(index++, new VisibleObjectNode(row));
    }

    public int RowIndexOf(ObjectNodeViewModel node)
    {
        for (var i = 0; i < Count; i++)
        {
            if (ReferenceEquals(this[i].Node, node))
                return i;
        }
        return -1;
    }
}

/// <summary>One flat grid row wrapping a node view model (Proposal §16.3).</summary>
public sealed class VisibleObjectNode
{
    public VisibleObjectNode(ObjectNodeViewModel node) => Node = node;

    public ObjectNodeViewModel Node { get; }

    public int Indent => Node.Node.Depth;

    public double IndentWidth => 14.0 * Indent;

    public string Name => Node.Name;

    public string ValueText => Node.ValueText;

    public string TypeText => Node.TypeText;

    public string DeclaredTypeText => Node.DeclaredTypeText;

    public string ToolTip => Node.ToolTip;

    public bool ShowToggle => Node.Node.CanExpand || Node.IsLoadMore;

    public string ToggleGlyph => Node.IsExpanded ? "▾" : "▸";

    public string KindGlyph => Glyphs.For(Node.Node);

    public string StateGlyph
    {
        get
        {
            if (Node.IsBusy)
                return "…";
            if (Node.Node.Error is not null || Node.Node.Kind == ObjectNodeKind.Error)
                return "⚠";
            if (Node.Node.Display.IsTruncated)
                return "…";
            return string.Empty;
        }
    }

    public bool IsMasked => Node.Node.Display.IsMasked;

    public bool HasError => Node.Node.Error is not null || Node.Node.Kind == ObjectNodeKind.Error;

    public string AutomationDescription
        => $"{Name}, value {ValueText}, type {TypeText}, depth {Indent}";
}

internal static class Glyphs
{
    public static string For(Core.Graph.ObjectNode node) => node.Kind switch
    {
        Core.Graph.ObjectNodeKind.LoadMore => "…",
        Core.Graph.ObjectNodeKind.Truncated => "…",
        Core.Graph.ObjectNodeKind.Error => "⚠",
        Core.Graph.ObjectNodeKind.Reference => "↩",
        _ => node.ValueKind switch
        {
            Core.Graph.ObjectValueKind.Null => "∅",
            Core.Graph.ObjectValueKind.Primitive => "●",
            Core.Graph.ObjectValueKind.String => "”",
            Core.Graph.ObjectValueKind.Enum => "⊙",
            Core.Graph.ObjectValueKind.ByteArray => "▒",
            Core.Graph.ObjectValueKind.Collection => "▦",
            Core.Graph.ObjectValueKind.Dictionary => "▤",
            Core.Graph.ObjectValueKind.Reference => "↩",
            _ => "◆",
        },
    };
}
