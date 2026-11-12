using ObjectViewer.Core.Graph;

namespace ObjectViewer.Core.Adapters;

/// <summary>
/// Deterministic, priority-ordered adapter resolution (PRD FR-134, Proposal §8.3):
/// descending Priority, then stable ordinal Name ordering. A failing CanHandle
/// never aborts resolution; DefaultObjectAdapter always remains as the fallback.
/// </summary>
public sealed class ObjectAdapterRegistry
{
    private readonly IObjectAdapter[] _adapters;
    private readonly IObjectAdapter _fallback;

    public ObjectAdapterRegistry(IEnumerable<IObjectAdapter> adapters)
    {
        _adapters = adapters
            .OrderByDescending(a => a.Priority)
            .ThenBy(a => a.Name, StringComparer.Ordinal)
            .ToArray();
        _fallback = _adapters.FirstOrDefault(a => a.Priority == 0)
            ?? throw new InvalidOperationException("A DefaultObjectAdapter with priority 0 is required as the fallback.");
    }

    public IReadOnlyList<IObjectAdapter> Adapters => _adapters;

    public IObjectAdapter Resolve(in ObjectAdapterContext context, InspectionSession session)
    {
        foreach (var adapter in _adapters)
        {
            try
            {
                if (adapter.CanHandle(context))
                    return adapter;
            }
            catch (Exception ex)
            {
                session.Diagnostics.Record("AdapterMatchFailed", $"{adapter.Name}: {ex.GetType().Name}");
            }
        }

        return _fallback;
    }
}
