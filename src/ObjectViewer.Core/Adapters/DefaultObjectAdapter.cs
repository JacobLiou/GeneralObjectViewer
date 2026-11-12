using System.Diagnostics;
using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Members;
using ObjectViewer.Core.Metadata;
using ObjectViewer.Core.Paths;

namespace ObjectViewer.Core.Adapters;

/// <summary>
/// Fallback adapter for ordinary objects: enumerates members through the member policy,
/// isolating every single getter (PRD FR-030/033, Proposal §9.2).
/// </summary>
public sealed class DefaultObjectAdapter : IObjectAdapter
{
    public int Priority => 0;
    public string Name => "Default";

    public bool CanHandle(in ObjectAdapterContext context)
        => true;

    public ObjectNodeDescriptor Describe(in ObjectAdapterContext context, InspectionSession session)
    {
        var typeText = TypeNames.Get(context.Value!.GetType());
        return new(ObjectValueKind.Object, new ValueDisplay("{" + typeText + "}", typeText), CanExpand: true);
    }

    public Task<ChildPageResult> GetChildrenAsync(ObjectNode node, InspectionSession session, CancellationToken cancellationToken)
    {
        var owner = node.RawValue!;
        var metadata = TypeMetadataCache.Shared.Get(owner.GetType());
        var children = new List<ObjectMemberDescriptor>(metadata.Members.Count);

        foreach (var member in metadata.Members)
        {
            var decision = session.MemberPolicy.Evaluate(member, session.Options);
            if (!decision.Include)
                continue;

            var (value, error) = MemberReader.Read(member, owner, session, node.NodeId);
            children.Add(new ObjectMemberDescriptor(
                Name: decision.DisplayName,
                Value: value,
                DeclaredType: member.ValueType,
                Kind: member.Kind == MemberKind.Property ? ObjectNodeKind.Property : ObjectNodeKind.Field,
                PathSegment: new MemberSegment(member.Name),
                Accessor: member,
                Owner: owner,
                ReadError: error,
                IsSensitive: decision.IsSensitive));
        }

        return Task.FromResult(new ChildPageResult(children));
    }
}
