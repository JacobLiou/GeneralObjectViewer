using System.Collections.Concurrent;

namespace ObjectViewer.Core.Diagnostics;

public sealed record DiagnosticEvent(DateTimeOffset Timestamp, string Kind, string? Detail, long? NodeId);

/// <summary>
/// Anonymous in-session diagnostics (NFR-040/041): no raw values are ever recorded.
/// </summary>
public sealed class DiagnosticsCollector
{
    private readonly ConcurrentQueue<DiagnosticEvent> _events = new();

    public IReadOnlyList<DiagnosticEvent> Events => _events.ToArray();

    public void Record(string kind, string? detail = null, long? nodeId = null)
        => _events.Enqueue(new DiagnosticEvent(DateTimeOffset.UtcNow, kind, detail, nodeId));

    public int Count => _events.Count;
}
