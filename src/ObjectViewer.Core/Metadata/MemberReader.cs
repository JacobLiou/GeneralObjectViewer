using System.Diagnostics;
using ObjectViewer.Core.Graph;

namespace ObjectViewer.Core.Metadata;

/// <summary>
/// Isolated member read shared by adapters and refresh (PRD FR-030/033/035):
/// every getter is individually guarded, timed and unwrapped.
/// </summary>
internal static class MemberReader
{
    public static (object? Value, Exception? Error) Read(
        MemberAccessor member, object owner, InspectionSession session, long? nodeId = null)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = member.Getter(owner);
            stopwatch.Stop();
            if (stopwatch.Elapsed >= session.Options.GetterWarningThreshold)
                session.Diagnostics.Record("GetterSlow", $"{member.Name} took {stopwatch.ElapsedMilliseconds}ms", nodeId);
            return (value, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var actual = NodeError.Unwrap(ex);
            session.Diagnostics.Record("GetterFailed", $"{member.Name}: {actual.GetType().Name}", nodeId);
            return (null, actual);
        }
    }
}
