using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Metadata;
using ObjectViewer.Core.Paths;

namespace ObjectViewer.Core.Adapters;

/// <summary>
/// Arrays and IList with O(1) index paging (PRD FR-050, Proposal §13.1).
/// Multi-dimensional arrays page over a linear offset, mapping to multi-indices.
/// byte[] is intercepted earlier by ByteArrayAdapter.
/// </summary>
public sealed class ListAdapter : IObjectAdapter
{
    public int Priority => 55;
    public string Name => "List";

    public bool CanHandle(in ObjectAdapterContext context)
        => context.Value is Array or System.Collections.IList;

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var value = context.Value!;
        var typeText = TypeNames.Get(value.GetType());
        string text;
        bool canExpand;

        if (value is Array array)
        {
            if (array.Rank == 1)
            {
                text = $"Length = {array.Length}";
                canExpand = array.Length > 0;
            }
            else
            {
                var dims = string.Join(",", Enumerable.Range(0, array.Rank).Select(array.GetLength));
                text = $"{{{TypeNames.Get(array.GetType().GetElementType())}[{dims}]}} Length = {array.Length}";
                canExpand = array.Length > 0;
            }
        }
        else
        {
            var list = (System.Collections.IList)value;
            text = $"Count = {list.Count}";
            canExpand = list.Count > 0;
        }

        return new(ObjectValueKind.Collection, new ValueDisplay(text, typeText), canExpand);
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
    {
        var pageSize = session.Options.CollectionPageSize;
        var cursor = node.Continuation as CollectionCursor ?? new CollectionCursor(0, pageSize);

        if (node.RawValue is Array { Rank: > 1 } multidim)
            return Task.FromResult(GetMultidimensionalPage(multidim, cursor));

        return Task.FromResult(GetListPage(node, cursor));
    }

    private static ChildPageResult GetListPage(ObjectNode node, CollectionCursor cursor)
    {
        var list = (System.Collections.IList)node.RawValue!;
        var elementType = node.RuntimeType!.IsArray
            ? node.RuntimeType.GetElementType()
            : typeof(object);
        var end = Math.Min(cursor.Offset + cursor.PageSize, list.Count);

        var children = new List<ObjectMemberDescriptor>(Math.Max(0, end - cursor.Offset));
        for (var i = cursor.Offset; i < end; i++)
        {
            children.Add(new ObjectMemberDescriptor(
                Name: $"[{i}]",
                Value: list[i],
                DeclaredType: elementType,
                Kind: ObjectNodeKind.CollectionItem,
                PathSegment: new IndexSegment([i])));
        }

        var next = end < list.Count ? new CollectionCursor(end, cursor.PageSize) : null;
        return new ChildPageResult(children, next);
    }

    private static ChildPageResult GetMultidimensionalPage(Array array, CollectionCursor cursor)
    {
        var lengths = new int[array.Rank];
        for (var d = 0; d < array.Rank; d++)
            lengths[d] = array.GetLength(d);

        var elementType = array.GetType().GetElementType()!;
        var end = Math.Min(cursor.Offset + cursor.PageSize, array.Length);

        var children = new List<ObjectMemberDescriptor>(Math.Max(0, end - cursor.Offset));
        for (var linear = cursor.Offset; linear < end; linear++)
        {
            var indices = DecodeLinearIndex(linear, lengths);
            children.Add(new ObjectMemberDescriptor(
                Name: $"[{string.Join(",", indices)}]",
                Value: array.GetValue(indices),
                DeclaredType: elementType,
                Kind: ObjectNodeKind.CollectionItem,
                PathSegment: new IndexSegment(indices)));
        }

        var next = end < array.Length ? new CollectionCursor(end, cursor.PageSize) : null;
        return new ChildPageResult(children, next);
    }

    private static int[] DecodeLinearIndex(long linear, int[] lengths)
    {
        var indices = new int[lengths.Length];
        for (var d = lengths.Length - 1; d >= 0; d--)
        {
            indices[d] = (int)(linear % lengths[d]);
            linear /= lengths[d];
        }
        return indices;
    }
}
