using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ObjectViewer.Core.DataSources;
using ObjectViewer.Core.Graph;
using ObjectViewer.Extensions;
using ObjectViewer.TestModels;

namespace ObjectViewer.Demo;

public sealed record Sample(string Title, string Description, Func<InspectionRequest> Factory);

public partial class MainWindow : Window
{
    private static readonly DataSourceImportService ImportService = new();

    private const string SampleJsonText = """
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

    private const string SampleYamlText = """
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
          timeout: 5
        refA: *common
        refB: *common
        """;

    private const string SampleXmlText = """
        <?xml version="1.0" encoding="utf-8"?>
        <Device name="RobotArm" version="2">
          <Axes count="3">
            <Axis index="0">12.5</Axis>
            <Axis index="1">-3.25</Axis>
            <Axis index="2">0</Axis>
          </Axes>
          <Credentials password="p@ss" token="abc123"/>
          <Notes>Imported from an XML file.</Notes>
        </Device>
        """;

    private const string SampleCsvText = """
        Name,Age,City,ApiKey
        Alice,34,Shanghai,sk-123
        Bob,28,Beijing,sk-456
        Carol,41,"Suzhou, Industrial Park",sk-789
        """;

    private static readonly Sample[] Samples =
    [
        new("Dog (接口/循环引用)", "IAnimal 声明类型 + Dog 运行时类型;Dog.Owner.Pet 构成环,展开到 Pet 时显示 ↩ reference。",
            () => new InspectionRequest { RootName = "animal", Value = SampleData.CreateDog(), DeclaredType = typeof(IAnimal) }),
        new("Order (12,500 项大集合)", "大集合分页:首次只加载 100 项 + Load more;OrderId/Customer/Parent 自引用。",
            () => new InspectionRequest { RootName = "order", Value = SampleData.CreateOrder() }),
        new("Device (Getter 异常隔离)", "ThrowingProp 抛异常只影响该成员;LongStringProp 超长截断;其余成员正常。",
            () => new InspectionRequest { RootName = "device", Value = SampleData.CreateFailingDevice() }),
        new("Credentials (敏感字段脱敏)", "Password/ApiToken/ConnectionString 默认名称命中 + CustomSecret 特性标注,复制拿不到原值。",
            () => new InspectionRequest { RootName = "credentials", Value = SampleData.CreateCredentials() }),
        new("Dictionary (特殊字符 Key)", "引号、空格、中文 Key 的路径转义;password 条目整体脱敏;条目展开为 Key/Value。",
            () => new InspectionRequest { RootName = "config", Value = SampleData.CreateSpecialDictionary() }),
        new("Deep chain (40 层深度链)", "默认最大深度 32,继续展开显示 limit reached 而非静默停止。",
            () => new InspectionRequest { RootName = "chain", Value = SampleData.CreateDeepChain(40) }),
        new("多维数组 int[3,4]", "Rank 与各维长度摘要;多维索引 [i,j] 分页展开。",
            () => new InspectionRequest { RootName = "matrix", Value = SampleData.CreateMultiDimensionalArray() }),
        new("byte[4096] (Hex)", "长度 + Hex 前缀摘要;显式展开按页浏览字节(0xXX 'M')。",
            () => new InspectionRequest { RootName = "payload", Value = SampleData.CreateBytes() }),
        new("共享引用 (ShowSharedReferences)", "两个 Customer 共享同一 Address;第二次出现显示为引用节点。",
            () => new InspectionRequest
            {
                RootName = "customers",
                Value = SampleData.CreateSharedReferenceGraph(),
                Options = InspectionOptions.SafeDefault with { ReferenceMode = ReferenceMode.ShowSharedReferences },
            }),
        new("null 根 (声明类型)", "根为 null 仍显示声明类型与 null 状态。",
            () => new InspectionRequest { RootName = "nothing", Value = null, DeclaredType = typeof(IAnimal) }),
        new("编辑: PublicSetters", "双击或 F2 编辑带 ✎ 的成员:数值/字符串/枚举/可空/DateTime/Guid/自定义 TypeConverter;getter-only、私有 setter、敏感成员不可编辑;输入 null 置空;写错显示红字且原值不动。",
            () => new InspectionRequest
            {
                RootName = "machine",
                Value = new EditableMachine(),
                Options = InspectionOptions.SafeDefault with { EditPolicy = EditPolicyMode.PublicSetters },
            }),
        new("编辑: 白名单 [Editable]", "ExplicitlyAllowedMembers 策略:仅标注 [ObjectViewerEditable] 的 Whitelisted 可编辑,其余一律只读。",
            () => new InspectionRequest
            {
                RootName = "machine",
                Value = new EditableMachine(),
                Options = InspectionOptions.SafeDefault with { EditPolicy = EditPolicyMode.ExplicitlyAllowedMembers },
            }),
        new("导入:JSON 配置", "外部数据源导入:password 键脱敏;数值/布尔/数组保留 JSON 类型语义;子节点分页。",
            () => ImportSample("json", "config.json", SampleJsonText)),
        new("导入:YAML(锚点/别名)", "YAML 导入:&common 锚点被 refA/refB 引用 — 物化为同一实例,共享引用检测直接生效。",
            () => ImportSample("yaml", "devices.yaml", SampleYamlText)),
        new("导入:XML 设备描述", "XML 导入:属性显示为 @name 成员;重复同名元素用索引路径保持稳定路径唯一;文本叶子直接显示。",
            () => ImportSample("xml", "device.xml", SampleXmlText)),
        new("导入:CSV 表", "CSV 导入:首行为表头,数据行物化为字典;ApiKey 列脱敏;含引号转义的单元格。",
            () => ImportSample("csv", "people.csv", SampleCsvText)),
    ];

    public MainWindow()
    {
        InitializeComponent();
        SampleList.ItemsSource = Samples;
    }

    private static InspectionRequest ImportSample(string format, string rootName, string content)
    {
        var result = ImportService.ImportTextAsync(format, content, rootName).GetAwaiter().GetResult();
        return new InspectionRequest
        {
            RootName = result.RootName,
            Value = result.Value,
            DeclaredType = result.DeclaredType,
            CustomAdapters = ObjectViewerFormatAdapters.All,
        };
    }

    private async void OnSampleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SampleList.SelectedItem is not Sample sample)
            return;
        DescriptionText.Text = sample.Description;
        await Viewer.SetRootAsync(sample.Factory());
    }

    private async void OnOpenDataSourceClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开数据源",
            Filter = "数据源文件 (*.json;*.yaml;*.yml;*.xml;*.csv)|*.json;*.yaml;*.yml;*.xml;*.csv|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var result = await ImportService.ImportFileAsync(dialog.FileName);
            await Viewer.SetRootAsync(new InspectionRequest
            {
                RootName = result.RootName,
                Value = result.Value,
                DeclaredType = result.DeclaredType,
                CustomAdapters = ObjectViewerFormatAdapters.All,
            });
            DescriptionText.Text = $"已导入 {result.Origin.Format}: {dialog.FileName}({result.Origin.SizeBytes:N0} 字节,一次性快照)";
        }
        catch (DataSourceException ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
