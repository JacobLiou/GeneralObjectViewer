using System.Text;
using System.Text.RegularExpressions;

namespace ObjectViewer.Core.Paths;

public abstract record ObjectPathSegment;

public sealed record RootSegment : ObjectPathSegment;

public sealed record MemberSegment(string Name) : ObjectPathSegment;

public sealed record IndexSegment(IReadOnlyList<int> Indices) : ObjectPathSegment;

/// <summary>Carries an already-escaped dictionary key (PRD FR-072).</summary>
public sealed record DictionaryKeySegment(string EncodedKey) : ObjectPathSegment;

public sealed record SyntheticSegment(string Kind, string Id) : ObjectPathSegment;

/// <summary>
/// Structured, immutable object path. Never assembled by string concatenation in core code;
/// rendering happens at the boundary through <see cref="ToDisplayPath"/> / <see cref="ToStablePath"/>.
/// </summary>
public sealed record ObjectPath(IReadOnlyList<ObjectPathSegment> Segments)
{
    public static ObjectPath RootPath { get; } = new([]);

    public ObjectPath Append(ObjectPathSegment segment)
        => new([.. Segments, segment]);

    public string ToDisplayPath() => PathRenderer.ToDisplayPath(this);

    /// <summary>Versioned stable identity path, e.g. <c>ovp1:/root/member:Items/index:12</c> (PRD FR-071/074).</summary>
    public string ToStablePath() => PathRenderer.ToStablePath(this);

    public override string ToString() => ToDisplayPath();
}

public static partial class PathRenderer
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierRegex();

    public static string EscapeKey(string key)
    {
        var sb = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\'': sb.Append("\\'"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                        sb.Append("\\u").Append(((int)c).ToString("X4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    public static bool IsIdentifier(string name)
        => name.Length > 0 && IdentifierRegex().IsMatch(name);

    public static string ToDisplayPath(ObjectPath path)
    {
        if (path.Segments.Count == 0)
            return "$";

        var sb = new StringBuilder("$");
        foreach (var segment in path.Segments)
            AppendDisplaySegment(sb, segment);
        return sb.ToString();
    }

    private static void AppendDisplaySegment(StringBuilder sb, ObjectPathSegment segment)
    {
        switch (segment)
        {
            case MemberSegment member:
                if (IsIdentifier(member.Name))
                    sb.Append('.').Append(member.Name);
                else
                    sb.Append("['").Append(EscapeKey(member.Name)).Append("']");
                break;
            case IndexSegment index:
                sb.Append('[').Append(string.Join(",", index.Indices)).Append(']');
                break;
            case DictionaryKeySegment key:
                sb.Append("['").Append(key.EncodedKey).Append("']");
                break;
            case SyntheticSegment synthetic:
                sb.Append('[').Append(synthetic.Kind).Append(':').Append(synthetic.Id).Append(']');
                break;
            case RootSegment:
            default:
                break;
        }
    }

    public static string ToStablePath(ObjectPath path)
    {
        var sb = new StringBuilder("ovp1:/root");
        foreach (var segment in path.Segments)
        {
            switch (segment)
            {
                case MemberSegment member:
                    sb.Append("/member:").Append(member.Name);
                    break;
                case IndexSegment index:
                    sb.Append("/index:").Append(string.Join(",", index.Indices));
                    break;
                case DictionaryKeySegment key:
                    sb.Append("/dkey:").Append(key.EncodedKey);
                    break;
                case SyntheticSegment synthetic:
                    sb.Append("/syn:").Append(synthetic.Kind).Append(':').Append(synthetic.Id);
                    break;
                case RootSegment:
                default:
                    break;
            }
        }
        return sb.ToString();
    }
}
