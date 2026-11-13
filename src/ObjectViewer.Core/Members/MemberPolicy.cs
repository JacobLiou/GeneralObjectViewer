using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Metadata;

namespace ObjectViewer.Core.Members;

/// <summary>
/// Per-member inclusion/behavior decision (PRD FR-025). CanWrite is the
/// policy-effective editability: the member must have a setter AND pass the
/// session edit policy (FR-121); sensitive members are never writable.
/// </summary>
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
            CanWrite: member.CanWrite && !isSensitive && IsEditAllowed(member, options),
            IsSensitive: isSensitive,
            DisplayName: member.DisplayName,
            Order: member.Order);
    }

    /// <summary>
    /// Edit policy gate (PRD FR-120/121): ReadOnly blocks everything; PublicSetters
    /// requires a public setter (the compiled setter can invoke private setters,
    /// so the setter's own accessibility must be checked, not the member's);
    /// ExplicitlyAllowedMembers requires [ObjectViewerEditable]; FullTrust only
    /// requires that a setter exists.
    /// </summary>
    private static bool IsEditAllowed(MemberAccessor member, InspectionOptions options)
        => options.EditPolicy switch
        {
            EditPolicyMode.ReadOnly => false,
            EditPolicyMode.PublicSetters => HasPublicSetter(member),
            EditPolicyMode.ExplicitlyAllowedMembers
                => member.MemberInfo.IsDefined(typeof(ObjectViewerEditableAttribute), inherit: true),
            EditPolicyMode.FullTrust => true,
            _ => false,
        };

    private static bool HasPublicSetter(MemberAccessor member)
        => member.MemberInfo switch
        {
            System.Reflection.PropertyInfo property => property.GetSetMethod() is not null,
            System.Reflection.FieldInfo field => field.IsPublic,
            _ => false,
        };
}
