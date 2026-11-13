using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ObjectViewer.Core.Graph;
using ObjectViewer.Extensions;
using ObjectViewer.Wpf.Controls;

namespace ObjectViewer.Demo;

/// <summary>Metadata + data persisted as one JSON file inside the Samples folder.</summary>
public sealed record SampleManifest(
    string Title,
    string Description,
    string Category,
    string Icon,
    int Sort,
    string TypeName,
    string? DeclaredTypeName,
    string RootName,
    EditPolicyMode EditPolicy,
    ReferenceMode ReferenceMode,
    bool PreserveReferences,
    int? CollectionPageSize);

/// <summary>One discovered sample file in the library.</summary>
public sealed class SampleFile
{
    public required string FilePath { get; init; }

    public required SampleManifest Manifest { get; init; }

    public string FileName => Path.GetFileNameWithoutExtension(FilePath);

    public override string ToString() => Manifest.Title;

    public string FileSizeText
    {
        get
        {
            try
            {
                var bytes = new FileInfo(FilePath).Length;
                return bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:F1} KB";
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}

/// <summary>An open document: the live object graph + its viewer instance.</summary>
public sealed class SampleDocument : INotifyPropertyChanged
{
    private bool _isDirty;
    private bool _isCurrent;
    private string _filePath = string.Empty;

    public required SampleFile Sample { get; init; }

    /// <summary>Live root object; edits mutate this instance (in place, via the viewer).</summary>
    public object? Data { get; set; }

    public required Type DataType { get; set; }

    /// <summary>True for external data-source imports: rendered via format adapters, view-only.</summary>
    public bool IsImported { get; init; }

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value)
                return;
            _filePath = value;
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>False for external data-source imports (view-only, not savable).</summary>
    public bool CanSave => !IsImported;

    public string Title => Path.GetFileNameWithoutExtension(FilePath);

    public ObjectViewerControl? Viewer { get; set; }

    /// <summary>Per-document shell view (banner + viewer); kept alive across tab switches.</summary>
    public Controls.DocumentView? View { get; set; }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value)
                return;
            _isDirty = value;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
                return;
            _isCurrent = value;
            OnPropertyChanged(nameof(IsCurrent));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Discovers, loads and saves JSON sample files.</summary>
public static class SampleLibrary
{
    public const string FolderName = "Samples";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions PlainDataOptions = CreateDataOptions(preserve: false);

    private static readonly JsonSerializerOptions PreserveDataOptions = CreateDataOptions(preserve: true);

    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, FolderName);

    /// <summary>Scans a folder for sample manifests; invalid files are skipped and reported.</summary>
    public static (IReadOnlyList<SampleFile> Samples, IReadOnlyList<string> Errors) Discover(string folder)
    {
        var samples = new List<SampleFile>();
        var errors = new List<string>();
        if (!Directory.Exists(folder))
            return (samples, [$"示例文件夹不存在: {folder}"]);

        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                samples.Add(new SampleFile { FilePath = path, Manifest = ParseManifest(path) });
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return (samples, errors);
    }

    /// <summary>Loads one sample file into a live, editable document.</summary>
    public static SampleDocument Load(string path)
    {
        var manifest = ParseManifest(path);
        var dataType = ResolveType(manifest.TypeName);
        var data = DeserializeData(path, dataType, manifest.PreserveReferences);
        return new SampleDocument
        {
            Sample = new SampleFile { FilePath = path, Manifest = manifest },
            Data = data,
            DataType = dataType,
            FilePath = path,
        };
    }

    /// <summary>True when the JSON file looks like a sample manifest (has title + typeName + data).</summary>
    public static bool LooksLikeSample(string path)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path));
            return node is JsonObject obj
                && obj.ContainsKey("title")
                && obj.ContainsKey("typeName")
                && obj.ContainsKey("data");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Builds the inspection request for a document (policy/options come from the manifest).</summary>
    public static InspectionRequest BuildRequest(SampleDocument document)
    {
        var manifest = document.Sample.Manifest;
        var options = InspectionOptions.SafeDefault with
        {
            EditPolicy = manifest.EditPolicy,
            ReferenceMode = manifest.ReferenceMode,
        };
        if (manifest.CollectionPageSize is { } pageSize)
            options = options with { CollectionPageSize = pageSize };

        return new InspectionRequest
        {
            RootName = manifest.RootName,
            Value = document.Data,
            DeclaredType = document.IsImported ? null : ResolveType(manifest.DeclaredTypeName ?? manifest.TypeName),
            Options = options,
            CustomAdapters = ObjectViewerFormatAdapters.All,
        };
    }

    /// <summary>Serializes the live object graph back into the sample file, preserving metadata.</summary>
    public static async Task SaveAsync(SampleDocument document)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(document.FilePath).ConfigureAwait(false)) as JsonObject
            ?? throw new InvalidOperationException("示例文件不是有效的 JSON 对象。");
        var options = document.Sample.Manifest.PreserveReferences ? PreserveDataOptions : PlainDataOptions;
        root["data"] = JsonSerializer.SerializeToNode(document.Data, document.DataType, options);
        root["updatedAt"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await File.WriteAllTextAsync(document.FilePath, root.ToJsonString(WriteOptions)).ConfigureAwait(false);
    }

    /// <summary>Saves a copy under a new path; the document then points at the copy.</summary>
    public static async Task SaveAsAsync(SampleDocument document, string newPath)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(document.FilePath).ConfigureAwait(false)) as JsonObject
            ?? throw new InvalidOperationException("示例文件不是有效的 JSON 对象。");
        var options = document.Sample.Manifest.PreserveReferences ? PreserveDataOptions : PlainDataOptions;
        root["data"] = JsonSerializer.SerializeToNode(document.Data, document.DataType, options);
        root["updatedAt"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await File.WriteAllTextAsync(newPath, root.ToJsonString(WriteOptions)).ConfigureAwait(false);
        document.FilePath = newPath;
    }

    private static SampleManifest ParseManifest(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException("示例文件不是有效的 JSON 对象。");

        return new SampleManifest(
            Title: RequiredString(node, "title"),
            Description: RequiredString(node, "description"),
            Category: OptionalString(node, "category") ?? "未分类",
            Icon: OptionalString(node, "icon") ?? "📄",
            Sort: (int?)node["sort"] ?? 100,
            TypeName: RequiredString(node, "typeName"),
            DeclaredTypeName: OptionalString(node, "declaredTypeName"),
            RootName: OptionalString(node, "rootName") ?? "root",
            EditPolicy: ParseEnum<EditPolicyMode>(node, "editPolicy") ?? EditPolicyMode.ReadOnly,
            ReferenceMode: ParseEnum<ReferenceMode>(node, "referenceMode") ?? ReferenceMode.DetectCyclesOnly,
            PreserveReferences: (bool?)node["preserveReferences"] ?? false,
            CollectionPageSize: (int?)node["collectionPageSize"]);
    }

    private static object? DeserializeData(string path, Type dataType, bool preserve)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))?["data"];
        var options = preserve ? PreserveDataOptions : PlainDataOptions;
        return node is null
            ? null
            : node.Deserialize(dataType, options)
                ?? throw new InvalidOperationException($"示例数据无法反序列化为 {dataType.Name}。");
    }

    private static Type ResolveType(string name)
    {
        var direct = Type.GetType(name, throwOnError: false);
        if (direct is not null)
            return direct;

        // The assembly may not be loaded yet (e.g. TestModels is only touched lazily);
        // load it by simple name from the application's probing path and retry.
        var comma = name.IndexOf(',');
        if (comma > 0)
        {
            var assemblyName = name[(comma + 1)..].Trim();
            try
            {
                var assembly = Assembly.Load(assemblyName);
                var fromAssembly = assembly.GetType(name[..comma].Trim(), throwOnError: false);
                if (fromAssembly is not null)
                    return fromAssembly;
            }
            catch
            {
                // fall through to the explicit failure below
            }
        }

        throw new InvalidOperationException($"无法解析类型: {name}");
    }

    private static T? ParseEnum<T>(JsonObject node, string property) where T : struct, Enum
    {
        var value = OptionalString(node, property);
        return value is not null && Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : null;
    }

    private static string RequiredString(JsonObject node, string property)
        => OptionalString(node, property)
            ?? throw new InvalidOperationException($"示例文件缺少必需字段 \"{property}\"。");

    private static string? OptionalString(JsonObject node, string property)
        => node[property]?.GetValue<string>();

    private static JsonSerializerOptions CreateDataOptions(bool preserve)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
        };
        if (preserve)
            options.ReferenceHandler = ReferenceHandler.Preserve;
        return options;
    }
}
