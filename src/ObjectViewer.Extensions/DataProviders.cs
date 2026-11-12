using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using ObjectViewer.Core.DataSources;
using ObjectViewer.Extensions.Yaml;
using YamlDotNet.RepresentationModel;

namespace ObjectViewer.Extensions;

public sealed class JsonDataSourceProvider : IObjectDataSourceProvider
{
    public string Name => "JSON";
    public IReadOnlyList<string> SupportedExtensions => [".json"];

    public bool CanHandle(DataSourceDescriptor descriptor)
        => descriptor.SourceKind is "file" or "text";

    public Task<DataSourceLoadResult> LoadAsync(DataSourceDescriptor descriptor, DataSourceLoadOptions options, CancellationToken cancellationToken)
    {
        var (content, rootName, sizeBytes) = SourceIO.Read(descriptor, options);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new DataSourceException($"JSON 解析失败: {ex.Message}", ex);
        }

        return Task.FromResult(new DataSourceLoadResult(
            rootName,
            root,
            typeof(JsonNode),
            new DataSourceOrigin("JSON", descriptor.Location, sizeBytes, DateTimeOffset.UtcNow)));
    }
}

public sealed class YamlDataSourceProvider : IObjectDataSourceProvider
{
    public string Name => "YAML";
    public IReadOnlyList<string> SupportedExtensions => [".yaml", ".yml"];

    public bool CanHandle(DataSourceDescriptor descriptor)
        => descriptor.SourceKind is "file" or "text";

    public Task<DataSourceLoadResult> LoadAsync(DataSourceDescriptor descriptor, DataSourceLoadOptions options, CancellationToken cancellationToken)
    {
        var (content, rootName, sizeBytes) = SourceIO.Read(descriptor, options);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(content));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new DataSourceException($"YAML 解析失败: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
            throw new DataSourceException("YAML 文档为空");

        var root = YamlModelBuilder.Build(stream.Documents[0].RootNode, options.MaximumItems);
        return Task.FromResult(new DataSourceLoadResult(
            rootName,
            root,
            typeof(YamlValue),
            new DataSourceOrigin("YAML", descriptor.Location, sizeBytes, DateTimeOffset.UtcNow)));
    }
}

public sealed class XmlDataSourceProvider : IObjectDataSourceProvider
{
    public string Name => "XML";
    public IReadOnlyList<string> SupportedExtensions => [".xml"];

    public bool CanHandle(DataSourceDescriptor descriptor)
        => descriptor.SourceKind is "file" or "text";

    public Task<DataSourceLoadResult> LoadAsync(DataSourceDescriptor descriptor, DataSourceLoadOptions options, CancellationToken cancellationToken)
    {
        var (content, rootName, sizeBytes) = SourceIO.Read(descriptor, options);
        XDocument document;
        try
        {
            document = XDocument.Parse(content);
        }
        catch (XmlException ex)
        {
            throw new DataSourceException($"XML 解析失败: {ex.Message}", ex);
        }

        if (document.Root is null)
            throw new DataSourceException("XML 没有根元素");

        return Task.FromResult(new DataSourceLoadResult(
            rootName,
            document.Root,
            typeof(XElement),
            new DataSourceOrigin("XML", descriptor.Location, sizeBytes, DateTimeOffset.UtcNow)));
    }
}

public sealed class CsvDataSourceProvider : IObjectDataSourceProvider
{
    public string Name => "CSV";
    public IReadOnlyList<string> SupportedExtensions => [".csv"];

    public bool CanHandle(DataSourceDescriptor descriptor)
        => descriptor.SourceKind is "file" or "text";

    public Task<DataSourceLoadResult> LoadAsync(DataSourceDescriptor descriptor, DataSourceLoadOptions options, CancellationToken cancellationToken)
    {
        var (content, rootName, sizeBytes) = SourceIO.Read(descriptor, options);
        var rows = CsvParser.Parse(content, options.MaximumItems);
        if (rows.Count == 0)
            throw new DataSourceException("CSV 内容为空");

        // The first row is treated as the header; data rows become string dictionaries
        // consumed by the existing dictionary adapter.
        var header = rows[0];
        var dataRows = rows.Skip(1)
            .Select(row => row.Select((value, index) => new KeyValuePair<string, string>(
                index < header.Length ? header[index] : $"Column{index}", value)))
            .Select(pairs => new Dictionary<string, string>(pairs, StringComparer.Ordinal))
            .Cast<object>()
            .ToList();

        return Task.FromResult(new DataSourceLoadResult(
            rootName,
            dataRows,
            typeof(List<object>),
            new DataSourceOrigin("CSV", descriptor.Location, sizeBytes, DateTimeOffset.UtcNow)));
    }
}

internal static class SourceIO
{
    public static (string Content, string RootName, long SizeBytes) Read(DataSourceDescriptor descriptor, DataSourceLoadOptions options)
    {
        if (descriptor.SourceKind == "text")
        {
            if (descriptor.Payload is not string content)
                throw new DataSourceException("文本数据源需要 string 载荷");
            return (content, descriptor.Location, content.Length);
        }

        var path = descriptor.Location;
        if (!File.Exists(path))
            throw new DataSourceException($"文件不存在: {path}");

        var info = new FileInfo(path);
        if (info.Length > options.MaximumSourceSizeBytes)
            throw new DataSourceException($"文件大小 {info.Length:N0} 字节超出限制 {options.MaximumSourceSizeBytes:N0} 字节");

        return (File.ReadAllText(path), Path.GetFileNameWithoutExtension(path), info.Length);
    }
}

/// <summary>Minimal RFC-4180-style CSV parser (quotes, escaped quotes, commas, newlines).</summary>
internal static class CsvParser
{
    public static List<string[]> Parse(string text, int maximumRows)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();

        void EndRow()
        {
            if (row.Count > 0 || field.Length > 0)
            {
                row.Add(field.ToString());
                field.Clear();
                if (row.Count > 1 || row[0].Length > 0)
                {
                    if (rows.Count >= maximumRows)
                        throw new DataSourceException($"CSV 行数超出限制 {maximumRows}");
                    rows.Add([.. row]);
                }
            }
            row.Clear();
        }

        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        row.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        EndRow();
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }
        }

        EndRow();
        return rows;
    }
}
