namespace ObjectViewer.Core.Members;

/// <summary>Determines sensitivity of a member by name or attribute (NFR-021/022).</summary>
public interface ISensitiveDataPolicy
{
    bool IsSensitive(string memberName);

    bool IsSensitive(System.Reflection.MemberInfo? member);
}

/// <summary>
/// Default sensitive-name rules. Matching is case-insensitive "contains"
/// against the PRD NFR-021 list, plus [ObjectViewerSensitive].
/// </summary>
public sealed class DefaultSensitiveDataPolicy : ISensitiveDataPolicy
{
    internal static readonly string[] SensitiveNameParts =
    [
        "password",
        "secret",
        "token",
        "apikey",
        "connectionstring",
        "credential",
        "privatekey",
        "accesstoken",
    ];

    public bool IsSensitive(string memberName)
        => SensitiveNameParts.Any(part => memberName.Contains(part, StringComparison.OrdinalIgnoreCase));

    public bool IsSensitive(System.Reflection.MemberInfo? member)
    {
        if (member is null)
            return false;
        if (member.GetCustomAttributes(typeof(ObjectViewerSensitiveAttribute), inherit: true).Any())
            return true;
        return IsSensitive(member.Name);
    }
}
