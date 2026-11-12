using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Metadata;
using ObjectViewer.Core.Paths;

namespace ObjectViewer.Core.Adapters;

/// <summary>null values: shows "null" plus the declared type, never expandable (PRD §9).</summary>
public sealed class NullAdapter : IObjectAdapter
{
    public int Priority => 100;
    public string Name => "Null";

    public bool CanHandle(in ObjectAdapterContext context)
        => context.Value is null;

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
        => new(ObjectValueKind.Null, new ValueDisplay("null", TypeNames.Get(context.DeclaredType)), CanExpand: false);

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
        => Task.FromResult(ChildPageResult.Empty);
}

/// <summary>Primitive numeric/boolean/char values.</summary>
public sealed class PrimitiveAdapter : IObjectAdapter
{
    public int Priority => 90;
    public string Name => "Primitive";

    public bool CanHandle(in ObjectAdapterContext context)
        => context.Value is bool or char or byte or sbyte or short or ushort or int or uint
            or long or ulong or float or double or decimal;

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var display = session.Formatters.Format(context.DeclaredType, context.Value, ValueFormattingContext.From(session.Options));
        return new(ObjectValueKind.Primitive, display, CanExpand: false);
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
        => Task.FromResult(ChildPageResult.Empty);
}

/// <summary>Strings: quoted, escaped, truncation-marked; not expandable per character by default.</summary>
public sealed class StringAdapter : IObjectAdapter
{
    public int Priority => 85;
    public string Name => "String";

    public bool CanHandle(in ObjectAdapterContext context)
        => context.Value is string;

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var display = session.Formatters.Format(context.DeclaredType, context.Value, ValueFormattingContext.From(session.Options));
        return new(ObjectValueKind.String, display, CanExpand: false);
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
        => Task.FromResult(ChildPageResult.Empty);
}

/// <summary>Enums: name plus underlying numeric value, e.g. "Running (2)".</summary>
public sealed class EnumAdapter : IObjectAdapter
{
    public int Priority => 80;
    public string Name => "Enum";

    public bool CanHandle(in ObjectAdapterContext context)
        => context.Value is not null && context.Value.GetType().IsEnum;

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var display = session.Formatters.Format(context.DeclaredType, context.Value, ValueFormattingContext.From(session.Options));
        return new(ObjectValueKind.Enum, display, CanExpand: false);
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
        => Task.FromResult(ChildPageResult.Empty);
}

/// <summary>
/// byte[]: length + hex prefix summary. Explicit expansion yields paged byte items
/// (user-initiated, so it does not violate "no automatic plain-list expansion").
/// </summary>
public sealed class ByteArrayAdapter : IObjectAdapter
{
    public int Priority => 75;
    public string Name => "ByteArray";

    public bool CanHandle(in ObjectAdapterContext context)
        => context.Value is byte[];

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var bytes = (byte[])context.Value!;
        return new(ObjectValueKind.ByteArray, ByteArrayDisplay.Build(bytes, TypeNames.Get(bytes.GetType())), CanExpand: bytes.Length > 0);
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
    {
        var bytes = (byte[])node.RawValue!;
        var pageSize = session.Options.CollectionPageSize;
        var cursor = node.Continuation as CollectionCursor ?? new CollectionCursor(0, pageSize);
        var end = Math.Min(cursor.Offset + pageSize, bytes.Length);

        var children = new List<ObjectMemberDescriptor>(end - cursor.Offset);
        for (var i = cursor.Offset; i < end; i++)
        {
            var b = bytes[i];
            var printable = b is >= 0x20 and < 0x7F ? ((char)b).ToString() : ".";
            children.Add(new ObjectMemberDescriptor(
                Name: $"[{i}]",
                Value: b,
                DeclaredType: typeof(byte),
                Kind: ObjectNodeKind.CollectionItem,
                PathSegment: new IndexSegment([i]),
                PresetDisplay: new ValueDisplay($"0x{b:X2}  '{printable}'", "byte")));
        }

        var next = end < bytes.Length ? new CollectionCursor(end, pageSize) : null;
        return Task.FromResult(new ChildPageResult(children, next));
    }
}
