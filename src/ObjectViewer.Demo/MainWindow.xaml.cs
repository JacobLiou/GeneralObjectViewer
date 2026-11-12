using System.Windows;
using System.Windows.Controls;
using ObjectViewer.Core.Graph;
using ObjectViewer.TestModels;

namespace ObjectViewer.Demo;

public sealed record Sample(string Title, string Description, Func<InspectionRequest> Factory);

public partial class MainWindow : Window
{
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
    ];

    public MainWindow()
    {
        InitializeComponent();
        SampleList.ItemsSource = Samples;
    }

    private async void OnSampleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SampleList.SelectedItem is not Sample sample)
            return;
        DescriptionText.Text = sample.Description;
        await Viewer.SetRootAsync(sample.Factory());
    }
}
