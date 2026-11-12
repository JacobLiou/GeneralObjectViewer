using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Metadata;

namespace ObjectViewer.Core.Members;

/// <summary>Per-member inclusion/behavior decision (PRD FR-025).</summary>
public sealed record MemberDecision(
    bool Include,
    bool CanRead,
    bool CanWrite,
    bool IsSensitive,
    string DisplayName,
    int Order,
    string? ExclusionReason = null)
{
    public static MemberDecision Exclude(string reason) => new(false, false, false, false, string.Empty, 0, reason);
}

/// <summary>Member visibility and behavior policy applied during member enumeration.</summary>
public interface IMemberPolicy
{
    MemberDecision Evaluate(MemberAccessor member, InspectionOptions options);
}

/// <summary>
/// Default member policy: public instance properties and fields, indexers/methods/events
/// skipped upstream, [Browsable(false)] hidden, non-public/static members behind options
/// (PRD FR-020/021/023, NFR-020).
/// </summary>
public sealed class DefaultMemberPolicy(ISensitiveDataPolicy sensitivePolicy) : IMemberPolicy
{
    public MemberDecision Evaluate(MemberAccessor member, InspectionOptions options)
    {
        if (member.MemberInfo.GetCustomAttributes(typeof(ObjectViewerIgnoreAttribute), inherit: true).Any())
            return MemberDecision.Exclude("Ignored by ObjectViewerIgnore");

        if (!member.IsBrowsable)
            return MemberDecision.Exclude("Browsable(false)");

        if (member.IsStatic && !options.ShowStaticMembers)
            return MemberDecision.Exclude("Static members disabled");

        if (!member.IsPublic && !options.ShowNonPublicMembers)
            return MemberDecision.Exclude("Non-public members disabled");

        var isSensitive = sensitivePolicy.IsSensitive(member.MemberInfo);

        return new MemberDecision(
            Include: true,
            CanRead: true,
            CanWrite: member.CanWrite,
            IsSensitive: isSensitive,
            DisplayName: member.DisplayName,
            Order: member.Order);
    }
}
