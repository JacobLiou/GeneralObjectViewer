using System.Collections.ObjectModel;
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

/// <summary>Thin grid row: the node view model plus its visual indent (Proposal §16.3).</summary>
public sealed class VisibleObjectNode
{
    public VisibleObjectNode(ObjectNodeViewModel node) => Node = node;

    public ObjectNodeViewModel Node { get; }

    public int Indent => Node.Node.Depth;

    public double IndentWidth => 14.0 * Indent;
}
