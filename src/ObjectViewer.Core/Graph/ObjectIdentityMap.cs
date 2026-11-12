using System.Collections;
using ObjectViewer.Core.Paths;

namespace ObjectViewer.Core.Graph;

/// <summary>
/// Session-scoped object identity tracking based on reference equality (PRD FR-060/061/063).
/// Business Equals() overrides never participate in identity decisions.
/// </summary>
public sealed class ObjectIdentityMap
{
    private readonly ReferenceMode _mode;
    private readonly Dictionary<object, (long IdentityId, ObjectPath FirstPath)>? _firstSeen;
    private long _identitySeed;

    public ObjectIdentityMap(ReferenceMode mode)
    {
        _mode = mode;
        if (mode == ReferenceMode.ShowSharedReferences)
            _firstSeen = new Dictionary<object, (long, ObjectPath)>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// Only ordinary reference types are tracked. Strings, value types and null never
    /// join the identity graph (PRD FR-063).
    /// </summary>
    public static bool IsTrackable(object? value)
        => value is not null && !value.GetType().IsValueType && value is not string;

    public readonly record struct ReferenceEvaluation(bool IsReference, long IdentityId, ObjectPath? FirstPath);

    /// <summary>
    /// Evaluates a candidate value. In DetectCyclesOnly mode only the ancestor chain is consulted;
    /// in ShowSharedReferences mode the session-wide first-seen table decides.
    /// </summary>
    public ReferenceEvaluation Evaluate(object value, AncestorChain? ancestors, ObjectPath currentPath)
    {
        switch (_mode)
        {
            case ReferenceMode.DetectCyclesOnly:
            {
                var target = ancestors?.Find(value);
                return target is null
                    ? default
                    : new ReferenceEvaluation(true, 0, target);
            }
            case ReferenceMode.ShowSharedReferences:
            {
                if (_firstSeen!.TryGetValue(value, out var existing))
                    return new ReferenceEvaluation(true, existing.IdentityId, existing.FirstPath);
                var id = Interlocked.Increment(ref _identitySeed);
                _firstSeen[value] = (id, currentPath);
                return default;
            }
            default:
                return default;
        }
    }

    /// <summary>Registers the root value as first-seen at the root path (shared-references mode).</summary>
    public void RegisterRoot(object value, ObjectPath rootPath)
    {
        if (_mode == ReferenceMode.ShowSharedReferences)
            _firstSeen!.TryAdd(value, (Interlocked.Increment(ref _identitySeed), rootPath));
    }

    /// <summary>
    /// Returns the first-seen path for a value in shared-references mode, or null when
    /// the value is unregistered or was first seen at <paramref name="selfPath"/> itself.
    /// Used by refresh to re-render reference nodes without re-registering.
    /// </summary>
    public ObjectPath? FindShared(object value, ObjectPath selfPath)
    {
        if (_mode != ReferenceMode.ShowSharedReferences)
            return null;
        if (_firstSeen!.TryGetValue(value, out var existing) && !existing.FirstPath.Equals(selfPath))
            return existing.FirstPath;
        return null;
    }
}
