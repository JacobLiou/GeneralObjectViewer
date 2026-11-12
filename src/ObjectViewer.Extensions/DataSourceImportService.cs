using ObjectViewer.Core.DataSources;

namespace ObjectViewer.Extensions;

/// <summary>
/// Dispatches imports to registered providers by file extension or explicit format label.
/// One-shot snapshot semantics: after LoadAsync the materialized value is decoupled
/// from the source (PRD FR-142).
/// </summary>
public sealed class DataSourceImportService
{
    private readonly IReadOnlyList<IObjectDataSourceProvider> _providers;

    public DataSourceImportService()
        : this(CreateDefaultProviders())
    {
    }

    public DataSourceImportService(IEnumerable<IObjectDataSourceProvider> providers)
        => _providers = [.. providers];

    public IReadOnlyList<IObjectDataSourceProvider> Providers => _providers;

    public Task<DataSourceLoadResult> ImportFileAsync(string path, DataSourceLoadOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new DataSourceException("文件路径为空");
        return ImportCoreAsync(new DataSourceDescriptor("file", path), options, cancellationToken, Path.GetExtension(path));
    }

    /// <summary>Imports inline text content; formatLabel is e.g. "json", "yaml", "xml", "csv".</summary>
    public Task<DataSourceLoadResult> ImportTextAsync(string formatLabel, string content, string rootName, DataSourceLoadOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(formatLabel))
            throw new DataSourceException("格式标签为空");
        return ImportCoreAsync(new DataSourceDescriptor("text", rootName, content), options, cancellationToken, "." + formatLabel.TrimStart('.'));
    }

    private Task<DataSourceLoadResult> ImportCoreAsync(DataSourceDescriptor descriptor, DataSourceLoadOptions? options, CancellationToken cancellationToken, string extension)
    {
        options ??= DataSourceLoadOptions.Default;
        var provider = _providers.FirstOrDefault(p =>
            p.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
        if (provider is null)
            throw new DataSourceException($"不支持的数据源类型: {extension}(支持: {string.Join(", ", _providers.SelectMany(p => p.SupportedExtensions).Distinct())})");

        return provider.LoadAsync(descriptor, options, cancellationToken);
    }

    private static IObjectDataSourceProvider[] CreateDefaultProviders()
        =>
        [
            new JsonDataSourceProvider(),
            new YamlDataSourceProvider(),
            new XmlDataSourceProvider(),
            new CsvDataSourceProvider(),
        ];
}
