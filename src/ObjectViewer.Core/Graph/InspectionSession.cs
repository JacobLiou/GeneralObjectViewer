using ObjectViewer.Core.Adapters;
using ObjectViewer.Core.Diagnostics;
using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Members;

namespace ObjectViewer.Core.Graph;

/// <summary>
/// A viewer session: shares the root object, options snapshot, identity map,
/// node id generator, load concurrency gate, registries and resources
/// (Proposal §7.1). Sessions are cheap and short-lived; switching the root
/// object creates a new session and disposes the old one (PRD FR-002).
/// </summary>
public sealed class InspectionSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _sessionCts = new();
    private readonly SemaphoreSlim _loadGate;
    private readonly List<IDisposable> _resources = [];
    private readonly object _resourceLock = new();
    private long _nodeIdSeed;
    private long _generation;
    private int _createdNodes;
    private volatile bool _disposed;

    public InspectionSession(
        InspectionOptions options,
        IReadOnlyList<IObjectAdapter>? customAdapters = null,
        IReadOnlyList<IObjectValueFormatter>? customFormatters = null,
        IReadOnlyList<Editing.IEditValueConverter>? customEditConverters = null)
    {
        Options = options;
        IdentityMap = new ObjectIdentityMap(options.ReferenceMode);
        _loadGate = new SemaphoreSlim(Math.Max(1, options.MaximumConcurrentLoads));
        Adapters = new ObjectAdapterRegistry(
            ObjectViewerDefaults.CreateDefaultAdapters().Concat(customAdapters ?? []));
        Formatters = new FormatterRegistry(
            ObjectViewerDefaults.CreateDefaultFormatters().Concat(customFormatters ?? []));
        EditConverters = (customEditConverters ?? [])
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();
        SensitivePolicy = new DefaultSensitiveDataPolicy();
        MemberPolicy = new DefaultMemberPolicy(SensitivePolicy);
        Explorer = new ObjectGraphExplorer(this);
        Diagnostics.Record("SessionStarted");
    }

    public Guid SessionId { get; } = Guid.NewGuid();

    public long Generation => _generation;

    public InspectionOptions Options { get; }

    public ObjectIdentityMap IdentityMap { get; }

    public ObjectAdapterRegistry Adapters { get; }

    public FormatterRegistry Formatters { get; }

    /// <summary>Priority-ordered custom text→value converters consulted before the built-in parser (FR-123).</summary>
    public IReadOnlyList<Editing.IEditValueConverter> EditConverters { get; }

    public ISensitiveDataPolicy SensitivePolicy { get; }

    public IMemberPolicy MemberPolicy { get; }

    public DiagnosticsCollector Diagnostics { get; } = new();

    public CancellationToken SessionToken => _sessionCts.Token;

    public bool IsDisposed => _disposed;

    public ObjectNode? Root { get; internal set; }

    public ObjectGraphExplorer Explorer { get; }

    public int CreatedNodes => _createdNodes;

    /// <summary>Advances the generation; in-flight results with an older generation are discarded (Proposal §7.2).</summary>
    internal long BeginNewGeneration() => Interlocked.Increment(ref _generation);

    internal long NextNodeId() => Interlocked.Increment(ref _nodeIdSeed);

    /// <summary>Consumes one unit of the session node budget (PRD FR-045).</summary>
    internal bool TryConsumeNodeBudget()
        => Interlocked.Increment(ref _createdNodes) <= Options.MaximumNodesPerSession;

    internal CancellationTokenSource CreateLinkedSource(CancellationToken external)
    {
        if (_disposed)
        {
            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            return cancelled;
        }
        return CancellationTokenSource.CreateLinkedTokenSource(external, _sessionCts.Token);
    }

    internal Task WaitLoadGateAsync(CancellationToken cancellationToken)
        => _loadGate.WaitAsync(cancellationToken);

    internal void ReleaseLoadGate()
    {
        if (!_disposed)
        {
            try { _loadGate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>Registers a disposable resource (e.g. enumerator sessions) for collective release (Proposal §25).</summary>
    public void RegisterResource(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_resourceLock)
        {
            if (!_disposed)
                _resources.Add(resource);
            else
                resource.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        Diagnostics.Record("SessionDisposed");
        try { _sessionCts.Cancel(); }
        catch { }

        lock (_resourceLock)
        {
            foreach (var resource in _resources)
            {
                try { resource.Dispose(); }
                catch { }
            }
            _resources.Clear();
        }

        // The session CTS is intentionally left cancelled but not disposed: readers may
        // still ask for SessionToken after disposal, and a cancelled CTS without a wait
        // handle holds no unmanaged resources.
        await ValueTask.CompletedTask;
    }
}

public static class InspectionSessionFactory
{
    /// <summary>Creates a session and its root node; the root is described lazily, never traversed (PRD FR-040).</summary>
    public static InspectionSession Start(InspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = new InspectionSession(
            request.Options, request.CustomAdapters, request.CustomFormatters, request.CustomEditConverters);
        _ = session.Explorer.CreateRoot(request);
        return session;
    }
}
