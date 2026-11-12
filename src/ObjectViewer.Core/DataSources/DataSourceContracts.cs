namespace ObjectViewer.Core.DataSources;

/// <summary>
/// External data source provider contract (PRD FR-140, Proposal §38.2).
/// Implementations live in the extension layer; the core stays format-agnostic.
/// </summary>
public interface IObjectDataSourceProvider
{
    string Name { get; }

    /// <summary>Supported file extensions including the leading dot, e.g. ".json".</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    bool CanHandle(DataSourceDescriptor descriptor);

    Task<DataSourceLoadResult> LoadAsync(
        DataSourceDescriptor descriptor,
        DataSourceLoadOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Describes a source to import. SourceKind "file" reads Location as a path;
/// "text" carries the raw content in Payload with Location as a display label (FR-142).
/// </summary>
public sealed record DataSourceDescriptor(string SourceKind, string Location, object? Payload = null);

public sealed record DataSourceLoadOptions(
    long MaximumSourceSizeBytes = 64 * 1024 * 1024,
    int MaximumItems = 1_000_000)
{
    public static DataSourceLoadOptions Default { get; } = new();
}

/// <summary>Import outcome: the materialized in-memory representation plus origin metadata.</summary>
public sealed record DataSourceLoadResult(
    string RootName,
    object? Value,
    Type? DeclaredType,
    DataSourceOrigin Origin);

public sealed record DataSourceOrigin(string Format, string Location, long SizeBytes, DateTimeOffset LoadedAt);

/// <summary>Raised when a source exceeds its configured budget (PRD FR-144) or cannot be parsed.</summary>
public sealed class DataSourceException(string message, Exception? inner = null)
    : Exception(message, inner);
