using System.Text.Json.Nodes;
using ObjectViewer.Core.DataSources;
using ObjectViewer.Core.Graph;
using ObjectViewer.Extensions;
using ObjectViewer.Extensions.Yaml;
using Xunit;
using Xunit.Abstractions;

namespace ObjectViewer.Extensions.Tests;

internal static class ImportHarness
{
    public static InspectionSession Start(object? value, Type? declaredType = null)
        => InspectionSessionFactory.Start(new InspectionRequest
        {
            RootName = "imported",
            Value = value,
            DeclaredType = declaredType,
            CustomAdapters = ObjectViewerFormatAdapters.All,
        });

    public static async Task<ObjectNode> Expand(InspectionSession session, ObjectNode node)
    {
        var result = await session.Explorer.LoadChildrenAsync(node, CancellationToken.None);
        Assert.Equal(NodeLoadOutcome.Success, result.Outcome);
        return node;
    }
}

public sealed class JsonImportTests
{
    private const string Content = """
        {
          "station": "S01",
          "password": "hunter2",
          "timeoutMs": 3000,
          "enabled": true,
          "axes": [
            { "index": 0, "position": 12.5 },
            { "index": 1, "position": -3.25 }
          ],
          "tags": ["alpha", "beta"]
        }
        """;

    [Fact]
    public async Task Json_ParsesToNodes_WithSensitiveMaskingAndPaging()
    {
        var service = new DataSourceImportService();
        var result = await service.ImportTextAsync("json", Content, "config");
        Assert.Equal("JSON", result.Origin.Format);

        await using var session = ImportHarness.Start(result.Value);
        var root = session.Root!;
        Assert.Equal("Count = 6", root.Display.Text);
        Assert.True(root.CanExpand);

        var expanded = await ImportHarness.Expand(session, root);
        Assert.Equal(6, expanded.Children.Count(c => c.Kind == ObjectNodeKind.Property));

        var password = expanded.Children.Single(c => c.Name == "password");
        Assert.True(password.IsSensitive);
        Assert.True(password.Display.IsMasked);
        Assert.DoesNotContain("hunter2", password.Display.Text);

        var station = expanded.Children.Single(c => c.Name == "station");
        Assert.Equal("\"S01\"", station.Display.Text);
        Assert.Equal("$['station']", station.Path.ToDisplayPath());

        var axes = expanded.Children.Single(c => c.Name == "axes");
        var expandedAxes = await ImportHarness.Expand(session, axes);
        Assert.Equal(2, expandedAxes.Children.Count(c => c.Kind == ObjectNodeKind.CollectionItem));
        Assert.Equal("$['axes'][1]", expandedAxes.Children[1].Path.ToDisplayPath());
    }

    [Fact]
    public async Task Json_LargeArray_IsPaged()
    {
        var array = new JsonArray();
        for (var i = 0; i < 250; i++)
            array.Add(i);

        await using var session = ImportHarness.Start(array);
        var root = await ImportHarness.Expand(session, session.Root!);
        Assert.Equal(100, root.Children.Count(c => c.Kind == ObjectNodeKind.CollectionItem));
        Assert.Equal(ObjectNodeKind.LoadMore, root.Children[^1].Kind);
    }

    [Fact]
    public async Task Json_InvalidContent_ThrowsDataSourceException()
    {
        var service = new DataSourceImportService();
        await Assert.ThrowsAsync<DataSourceException>(() => service.ImportTextAsync("json", "{ not valid", "bad"));
    }
}

public sealed class YamlImportTests
{
    private const string Content = """
        server:
          host: 192.168.1.10
          port: 502
        auth:
          password: secret-yaml
        devices:
          - name: gripper
            address: 1
          - name: scanner
            address: 2
        shared: &common
          retries: 3
        refA: *common
        refB: *common
        """;

    [Fact]
    public async Task Yaml_ParsesWithAnchorsResolvingToSharedInstances()
    {
        var service = new DataSourceImportService();
        var result = await service.ImportTextAsync("yaml", Content, "devices");
        Assert.Equal("YAML", result.Origin.Format);
        Assert.IsType<YamlValue>(result.Value);

        await using var session = ImportHarness.Start(result.Value);
        var root = await ImportHarness.Expand(session, session.Root!);

        Assert.Equal("Count = 6", root.Display.Text);

        var server = root.Children.Single(c => c.Name == "server");
        var expandedServer = await ImportHarness.Expand(session, server);
        var port = expandedServer.Children.Single(c => c.Name == "port");
        Assert.Equal("502", port.Display.Text);

        var auth = root.Children.Single(c => c.Name == "auth");
        var expandedAuth = await ImportHarness.Expand(session, auth);
        var password = expandedAuth.Children.Single(c => c.Name == "password");
        Assert.True(password.IsSensitive);

        var devices = root.Children.Single(c => c.Name == "devices");
        var expandedDevices = await ImportHarness.Expand(session, devices);
        Assert.Equal(2, expandedDevices.Children.Count);
        Assert.Equal("[0]", expandedDevices.Children[0].Name);
    }

    [Fact]
    public async Task Yaml_AliasesBecomeSharedReferences()
    {
        var service = new DataSourceImportService();
        var result = await service.ImportTextAsync("yaml", Content, "devices");

        // Shared-references mode: refA/refB point to the same materialized instance,
        // so the second occurrence renders as a reference node.
        await using var session = InspectionSessionFactory.Start(new InspectionRequest
        {
            RootName = "yaml",
            Value = result.Value,
            Options = InspectionOptions.SafeDefault with { ReferenceMode = ReferenceMode.ShowSharedReferences },
            CustomAdapters = ObjectViewerFormatAdapters.All,
        });
        var root = await ImportHarness.Expand(session, session.Root!);

        var shared = root.Children.Single(c => c.Name == "shared");
        var refA = root.Children.Single(c => c.Name == "refA");
        var refB = root.Children.Single(c => c.Name == "refB");

        // The anchor's first occurrence stays a normal mapping node; both aliases render
        // as references to the same materialized instance.
        Assert.Equal(ObjectValueKind.Dictionary, shared.ValueKind);
        Assert.Equal(ObjectValueKind.Reference, refA.ValueKind);
        Assert.Equal(ObjectValueKind.Reference, refB.ValueKind);
        Assert.Contains("↩ reference to", refA.Display.Text);
        Assert.Contains("↩ reference to", refB.Display.Text);
    }
}

public sealed class XmlImportTests
{
    private const string Content = """
        <?xml version="1.0" encoding="utf-8"?>
        <Device name="RobotArm" version="2">
          <Axes count="3">
            <Axis index="0">12.5</Axis>
            <Axis index="1">-3.25</Axis>
          </Axes>
          <Auth password="p@ss"/>
          <Notes>Some note</Notes>
        </Device>
        """;

    [Fact]
    public async Task Xml_AttributesElementsAndSensitiveNames()
    {
        var service = new DataSourceImportService();
        var result = await service.ImportTextAsync("xml", Content, "device");
        Assert.Equal("XML", result.Origin.Format);

        await using var session = ImportHarness.Start(result.Value);
        var root = await ImportHarness.Expand(session, session.Root!);

        var nameAttribute = root.Children.Single(c => c.Name == "@name");
        Assert.Equal("\"RobotArm\"", nameAttribute.Display.Text);
        Assert.Equal("$['@name']", nameAttribute.Path.ToDisplayPath());

        var credentials = root.Children.Single(c => c.Name == "Auth");
        var expandedCredentials = await ImportHarness.Expand(session, credentials);
        Assert.True(expandedCredentials.Children.Single(c => c.Name == "@password").IsSensitive);

        var axes = root.Children.Single(c => c.Name == "Axes");
        var expandedAxes = await ImportHarness.Expand(session, axes);
        var axisItems = expandedAxes.Children.Where(c => c.Name == "Axis").ToArray();
        Assert.Equal(2, axisItems.Length);
        // Repeated sibling names get unique index-based paths.
        Assert.NotEqual(axisItems[0].Path.ToStablePath(), axisItems[1].Path.ToStablePath());

        var notes = root.Children.Single(c => c.Name == "Notes");
        Assert.Equal("Some note", notes.Display.Text);
    }
}

public sealed class CsvImportTests
{
    private const string Content = """
        Name,Age,City,ApiKey
        Alice,34,Shanghai,sk-123
        Bob,28,"Beijing, Haidian",sk-456
        """;

    [Fact]
    public async Task Csv_HeaderBecomesKeys_QuotedCellsParsed()
    {
        var service = new DataSourceImportService();
        var result = await service.ImportTextAsync("csv", Content, "people");
        Assert.Equal("CSV", result.Origin.Format);

        await using var session = ImportHarness.Start(result.Value);
        var root = session.Root!;
        Assert.Equal("Count = 2", root.Display.Text);

        var expanded = await ImportHarness.Expand(session, root);
        Assert.Equal(2, expanded.Children.Count(c => c.Kind == ObjectNodeKind.CollectionItem));

        var bob = expanded.Children[1];
        var expandedBob = await ImportHarness.Expand(session, bob);
        var cityEntry = expandedBob.Children.Single(c => c.Name == "['City']");
        Assert.Contains("Beijing, Haidian", cityEntry.Display.Text);
        Assert.True(expandedBob.Children.Single(c => c.Name == "['ApiKey']").IsSensitive);
        Assert.Equal("$[1]['Name']", expandedBob.Children.Single(c => c.Name == "['Name']").Path.ToDisplayPath());
    }
}

public sealed class ImportServiceTests
{
    [Fact]
    public async Task UnsupportedExtension_Throws()
    {
        var service = new DataSourceImportService();
        await Assert.ThrowsAsync<DataSourceException>(() => service.ImportTextAsync("ini", "x=1", "config"));
    }

    [Fact]
    public async Task MissingFile_Throws()
    {
        var service = new DataSourceImportService();
        await Assert.ThrowsAsync<DataSourceException>(
            () => service.ImportFileAsync(@"C:\definitely\not\a\real\file.json"));
    }
}
