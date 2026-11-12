using System.Reflection;

namespace ObjectViewer.Core.Graph;

/// <summary>
/// Error information attached to a node (PRD FR-031).
/// Production UI must not display the full exception object or stack traces by default.
/// </summary>
public sealed record NodeError(
    string ErrorCode,
    string ExceptionType,
    string Message,
    bool IsTimeout = false,
    bool IsCancellation = false)
{
    public static NodeError FromException(Exception exception, string errorCode = "ReadFailed")
    {
        var actual = Unwrap(exception);
        return new NodeError(errorCode, actual.GetType().Name, actual.Message);
    }

    /// <summary>Unwraps <see cref="TargetInvocationException"/> to its inner exception (PRD FR-032).</summary>
    public static Exception Unwrap(Exception exception)
        => exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
}
