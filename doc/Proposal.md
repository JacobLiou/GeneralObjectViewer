# 通用 Object Viewer 技术方案（C# / WPF）

- **方案名称**：Generic Object Viewer for .NET
- **文档版本**：1.0
- **文档状态**：Proposed
- **更新时间**：2026-11-12
- **对应需求**：`PRD.md`
- **目标读者**：架构师、C# / WPF 开发工程师、测试工程师、代码评审人员

---

## 1. 摘要

本方案提出一个面向任意 .NET 对象的通用运行时浏览器。核心设计不是“在 WPF 控件里直接反射对象”，而是把问题拆成以下可独立演进的部分：

```text
WPF Object Viewer Control
        ↓
Flattened Visible Node Projection
        ↓
ObjectNode Presentation Model
        ↓
Object Graph Explorer / Inspection Session
        ↓
Adapter Registry + Policies + Formatters
        ↓
Metadata / Accessor Cache
        ↓
Reflection / TypeDescriptor / DynamicObject / Generated Adapter
```

建议采用以下技术决策：

1. **Core 与 WPF 完全解耦**，Core 不引用任何 WPF 类型。
2. **所有子节点懒加载**，绝不在设置根对象时递归遍历完整对象图。
3. **类型处理 Adapter 化**，避免形成不可维护的巨大 `if/else`。
4. **使用引用相等跟踪对象身份**，同时支持仅检测循环和显示共享引用。
5. **对象访问、值格式化、脱敏、编辑转换分别建模**。
6. **WPF 使用“逻辑树 + 可见节点扁平投影 + 虚拟化 DataGrid”**，而非直接依赖传统 `TreeView`。
7. **所有加载都具有会话、代次、取消和结果过期保护**。
8. **同步 Getter 的“超时”只能隔离等待，不能安全强杀线程**；高风险场景应使用进程隔离。
9. **默认只读、安全模式**；未知序列和 `IQueryable` 不自动执行。
10. **先交付 V0.1 浏览核心，再逐步加入搜索、刷新、快照、Diff 和编辑**。

---

## 2. 设计目标与约束

### 2.1 设计目标

- 支持任意 `object?` 作为入口。
- 支持深层嵌套、集合、字典、异常、Task、动态对象和业务类型。
- 大对象图下保持 UI 响应。
- 单个 Getter、Formatter、Adapter 或枚举器失败不影响其他节点。
- 保留声明类型和运行时类型。
- 提供稳定、可复制、可比较的路径。
- 支持业务扩展，但不要求修改核心代码。
- 对敏感值、未知枚举和可写成员建立安全边界。
- 为未来 WinUI、Web、远程诊断和 Source Generator 保留复用空间。

### 2.2 技术约束

- 首个 UI 实现为 C# + WPF。
- 建议使用受支持的现代 .NET LTS；具体 TFM 由宿主产品基线确定。
- Core 不应依赖 `PresentationFramework`、Dispatcher 或 WPF Binding。
- 不执行任意方法，不实现通用表达式求值器。
- 不假设 Getter、`ToString()`、枚举器或插件是安全的。
- 不依赖对象的值相等语义判断身份。
- 所有边界必须可配置且在 UI 上可解释。

---

## 3. 方案总览

### 3.1 组件关系

```text
┌──────────────── ObjectViewer.Wpf ────────────────┐
│ ObjectViewerControl                              │
│ ObjectViewerViewModel                            │
│ VisibleNodeCollection                            │
│ Row templates / editors / commands / themes      │
└───────────────────────┬───────────────────────────┘
                        │ consumes
┌──────────────── ObjectViewer.Core ────────────────┐
│ InspectionSession / ObjectGraphExplorer           │
│ ObjectNode / ObjectPath / NodeLoadResult          │
│ AdapterRegistry / FormatterRegistry               │
│ MemberPolicy / SensitiveDataPolicy / EditPolicy   │
│ TypeMetadataCache / MemberAccessor                │
│ ObjectIdentityMap / Snapshot / Diff               │
└───────────────────────┬───────────────────────────┘
                        │ extension contracts
┌──────────── ObjectViewer.Extensions ──────────────┐
│ DataTable / JSON / XML / Device / domain adapters │
│ Specialized formatters and renderers              │
└───────────────────────────────────────────────────┘
```

### 3.2 核心数据流

```text
任意 object
    ↓
创建 InspectionSession 与根 ObjectNode
    ↓
AdapterRegistry 根据优先级选择 Adapter
    ↓
Adapter 生成节点摘要（不递归）
    ↓
用户展开节点
    ↓
ObjectGraphExplorer 请求 Adapter 枚举下一层描述
    ↓
读取策略 + 敏感策略 + Getter 异常隔离
    ↓
ObjectIdentityMap 判断循环或共享引用
    ↓
Formatter 生成 Value / Type 展示数据
    ↓
逻辑子节点写入 ObjectNode
    ↓
VisibleNodeCollection 增量更新扁平可见行
    ↓
WPF DataGrid 虚拟化显示
```

### 3.3 为什么不直接使用 PropertyGrid 或 TreeView

- PropertyGrid 通常不能完整表达分页、引用关系、异步加载、受限枚举和快照差异。
- 标准 WPF `TreeView` 的多列能力弱；深层树和大量节点时容器虚拟化容易失效。
- 直接将反射写进控件会让 UI、对象生命周期、错误处理和扩展机制强耦合。
- 将展开后的逻辑树投影为扁平行列表，可以稳定获得多列、滚动、键盘导航和 UI 虚拟化。

---

## 4. 建议的解决方案结构

```text
ObjectViewer.sln
│
├─ src/
│  ├─ ObjectViewer.Core/
│  │  ├─ Abstractions/
│  │  │  ├─ IObjectAdapter.cs
│  │  │  ├─ IObjectValueFormatter.cs
│  │  │  ├─ IObjectValueConverter.cs
│  │  │  ├─ IMemberPolicy.cs
│  │  │  ├─ ISensitiveDataPolicy.cs
│  │  │  └─ IEditPolicy.cs
│  │  ├─ Graph/
│  │  │  ├─ InspectionSession.cs
│  │  │  ├─ ObjectGraphExplorer.cs
│  │  │  ├─ ObjectNode.cs
│  │  │  ├─ ObjectPath.cs
│  │  │  ├─ ObjectIdentityMap.cs
│  │  │  └─ InspectionLimits.cs
│  │  ├─ Metadata/
│  │  │  ├─ TypeMetadata.cs
│  │  │  ├─ TypeMetadataCache.cs
│  │  │  ├─ MemberAccessor.cs
│  │  │  └─ ReflectionAccessorFactory.cs
│  │  ├─ Adapters/
│  │  │  ├─ NullAdapter.cs
│  │  │  ├─ SensitiveValueAdapter.cs
│  │  │  ├─ PrimitiveAdapter.cs
│  │  │  ├─ StringAdapter.cs
│  │  │  ├─ EnumAdapter.cs
│  │  │  ├─ ByteArrayAdapter.cs
│  │  │  ├─ TaskAdapter.cs
│  │  │  ├─ ExceptionAdapter.cs
│  │  │  ├─ DictionaryAdapter.cs
│  │  │  ├─ ListAdapter.cs
│  │  │  ├─ EnumerableAdapter.cs
│  │  │  ├─ QueryableAdapter.cs
│  │  │  ├─ DynamicObjectAdapter.cs
│  │  │  └─ DefaultObjectAdapter.cs
│  │  ├─ Formatting/
│  │  │  ├─ FormatterRegistry.cs
│  │  │  ├─ DefaultValueFormatter.cs
│  │  │  └─ NumericValueFormatter.cs
│  │  ├─ Editing/
│  │  │  ├─ ConverterRegistry.cs
│  │  │  └─ EditService.cs
│  │  └─ Snapshots/
│  │     ├─ ObjectSnapshot.cs
│  │     ├─ SnapshotBuilder.cs
│  │     └─ ObjectGraphComparer.cs
│  │
│  ├─ ObjectViewer.Wpf/
│  │  ├─ Controls/
│  │  │  ├─ ObjectViewerControl.xaml
│  │  │  └─ ObjectViewerControl.xaml.cs
│  │  ├─ ViewModels/
│  │  │  ├─ ObjectViewerViewModel.cs
│  │  │  ├─ ObjectNodeViewModel.cs
│  │  │  └─ VisibleObjectNode.cs
│  │  ├─ Collections/
│  │  │  └─ VisibleNodeCollection.cs
│  │  ├─ Commands/
│  │  ├─ Behaviors/
│  │  ├─ Converters/
│  │  ├─ Editors/
│  │  └─ Themes/
│  │
│  ├─ ObjectViewer.Extensions/
│  │  ├─ DataTableAdapter.cs
│  │  ├─ JsonAdapter.cs
│  │  ├─ XmlAdapter.cs
│  │  └─ DeviceDataAdapter.cs
│  │
│  └─ ObjectViewer.Demo/
│
├─ tests/
│  ├─ ObjectViewer.Core.Tests/
│  ├─ ObjectViewer.Wpf.Tests/
│  ├─ ObjectViewer.PerformanceTests/
│  └─ ObjectViewer.TestModels/
│
└─ benchmarks/
   └─ ObjectViewer.Benchmarks/
```

依赖方向必须保持：

```text
ObjectViewer.Wpf ───────→ ObjectViewer.Core
ObjectViewer.Extensions ─→ ObjectViewer.Core
ObjectViewer.Core ───────→ 无 UI 依赖
```

---

## 5. 核心领域模型

### 5.1 节点类型

```csharp
public enum ObjectNodeKind
{
    Root,
    Null,
    Primitive,
    String,
    Enum,
    Object,
    Property,
    Field,
    Collection,
    CollectionItem,
    Dictionary,
    DictionaryEntry,
    Task,
    Exception,
    Reference,
    Error,
    Loading,
    LoadMore,
    Truncated
}

public enum NodeLoadState
{
    NotLoaded,
    Loading,
    Loaded,
    Cancelled,
    Failed,
    Truncated
}

public enum ReferenceMode
{
    DetectCyclesOnly,
    ShowSharedReferences
}
```

### 5.2 ObjectNode

Core 节点是“观察结果”的模型，不是原始实体的镜像。它应携带数据来源、访问方式、路径、状态和可扩展信息。

```csharp
public sealed class ObjectNode
{
    public required long NodeId { get; init; }
    public required string Name { get; init; }
    public required ObjectPath Path { get; init; }
    public required ObjectNodeKind Kind { get; init; }

    public object? RawValue { get; internal set; }
    public Type? DeclaredType { get; init; }
    public Type? RuntimeType => RawValue?.GetType();

    public ValueDisplay Display { get; internal set; } = ValueDisplay.Empty;
    public NodeLoadState LoadState { get; internal set; }
    public NodeError? Error { get; internal set; }

    public bool CanExpand { get; internal set; }
    public bool CanEdit { get; internal set; }
    public int Depth { get; init; }

    public MemberAccessor? Accessor { get; init; }
    public object? Owner { get; init; }
    public ObjectReferenceIdentity? Reference { get; internal set; }

    public IReadOnlyList<ObjectNode> Children => _children;
    private readonly List<ObjectNode> _children = [];

    internal void ReplaceChildren(IEnumerable<ObjectNode> children)
    {
        _children.Clear();
        _children.AddRange(children);
    }

    internal void AppendChildren(IEnumerable<ObjectNode> children)
        => _children.AddRange(children);
}
```

设计注意：

- `RawValue` 不能直接绑定到 UI，也不应被日志记录。
- Core 使用只读视图暴露 Children，避免 WPF 任意修改逻辑树。
- WPF 层可以用 `ObjectNodeViewModel` 包装并发送属性变更通知。
- `Owner + Accessor` 仅用于可刷新/可编辑成员；根节点或计算节点可为空。
- 若担心节点长期持有整个对象图，可将 Owner 的持有策略抽象为 `IValueSource`，由会话统一释放。

### 5.3 值展示

```csharp
public sealed record ValueDisplay(
    string Text,
    string TypeText,
    bool IsTruncated = false,
    bool IsMasked = false,
    string? FormatName = null,
    string? ToolTip = null)
{
    public static ValueDisplay Empty { get; } = new("", "");
}
```

`ValueDisplay` 必须是安全后的最终展示结果。搜索、复制、快照和日志摘要只能使用经过敏感策略处理的展示数据，除非调用方通过单独授权 API 请求原值。

### 5.4 错误模型

```csharp
public sealed record NodeError(
    string ErrorCode,
    string ExceptionType,
    string Message,
    bool IsTimeout = false,
    bool IsCancellation = false,
    Exception? DiagnosticException = null);
```

生产 UI 不应默认显示完整异常对象或 StackTrace；完整信息可交给宿主诊断通道，并遵循数据安全策略。

---

## 6. 路径模型

### 6.1 三种路径

1. **Display Path**：面向用户，例如 `$.Items[12].SerialNumber`。
2. **Member Path**：面向类型结构，例如 `Items.Item.SerialNumber`。
3. **Stable Identity Path**：面向程序匹配，例如 `/root/member:Items/index:12/member:SerialNumber`。

### 6.2 结构化路径段

不要在核心中仅使用字符串拼接；应使用结构化段，最后再编码：

```csharp
public abstract record ObjectPathSegment;
public sealed record RootSegment : ObjectPathSegment;
public sealed record MemberSegment(string Name) : ObjectPathSegment;
public sealed record IndexSegment(IReadOnlyList<int> Indices) : ObjectPathSegment;
public sealed record DictionaryKeySegment(string EncodedKey) : ObjectPathSegment;
public sealed record SyntheticSegment(string Kind, string Id) : ObjectPathSegment;

public sealed record ObjectPath(IReadOnlyList<ObjectPathSegment> Segments)
{
    public ObjectPath Append(ObjectPathSegment segment)
        => new([.. Segments, segment]);
}
```

### 6.3 路径规则

- C# 标识符形式的成员使用 `.Member`。
- 非标识符成员使用 `['escaped name']`。
- 列表使用 `[index]`，多维数组使用 `[i,j]`。
- 字符串 Key 使用 `['escaped key']`。
- 非稳定、不可安全序列化的 Dictionary Key 使用会话内条目 ID，并标记该路径不可跨会话恢复。
- 稳定路径编码必须版本化，例如 `ovp1:/root/member:Items/index:12`。

---

## 7. InspectionSession 与会话生命周期

### 7.1 为什么需要会话

一个 Viewer 打开期间需要共享：

- 根对象和配置快照
- 对象身份表
- 节点 ID 生成器
- 加载并发控制
- 会话取消令牌
- Adapter/Formatter/Policy 注册表
- 已订阅事件
- 统计与诊断信息
- 当前代次（generation）

这些状态不应散落在节点或 ViewModel 中。

```csharp
public sealed class InspectionSession : IAsyncDisposable
{
    public Guid SessionId { get; } = Guid.NewGuid();
    public long Generation { get; private set; }
    public InspectionOptions Options { get; }
    public ObjectIdentityMap IdentityMap { get; }
    public CancellationToken SessionToken => _sessionCts.Token;

    private readonly CancellationTokenSource _sessionCts = new();
    private readonly SemaphoreSlim _loadGate;
    private readonly List<IDisposable> _subscriptions = [];

    public InspectionSession(InspectionOptions options)
    {
        Options = options;
        _loadGate = new SemaphoreSlim(options.MaximumConcurrentLoads);
        IdentityMap = new ObjectIdentityMap(options.ReferenceMode);
    }

    public long BeginNewGeneration() => Interlocked.Increment(ref Generation);

    public async ValueTask DisposeAsync()
    {
        _sessionCts.Cancel();
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
        _sessionCts.Dispose();
        _loadGate.Dispose();
        await ValueTask.CompletedTask;
    }
}
```

### 7.2 代次保护

用户可能在节点加载期间切换根对象、刷新或折叠。异步结果返回时必须校验：

- SessionId 仍相同；
- Generation 仍相同；
- 节点仍属于当前会话；
- 请求未取消；
- 节点没有被更新请求替换。

否则丢弃结果，避免旧数据覆盖新状态。

---

## 8. Adapter 体系

### 8.1 Adapter 接口

```csharp
public interface IObjectAdapter
{
    int Priority { get; }
    string Name { get; }

    bool CanHandle(in ObjectAdapterContext context);

    ObjectNodeDescriptor Describe(in ObjectAdapterContext context);

    IAsyncEnumerable<ObjectMemberDescriptor> GetChildrenAsync(
        ObjectNode node,
        ObjectInspectionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken);
}
```

上下文：

```csharp
public readonly record struct ObjectAdapterContext(
    object? Value,
    Type? DeclaredType,
    Type? RuntimeType,
    MemberInfo? SourceMember,
    ObjectInspectionContext InspectionContext);

public sealed record ObjectNodeDescriptor(
    ObjectNodeKind Kind,
    ValueDisplay Display,
    bool CanExpand,
    bool IsSensitive = false);

public sealed record ObjectMemberDescriptor(
    string Name,
    object? Value,
    Type? DeclaredType,
    ObjectNodeKind SuggestedKind,
    ObjectPathSegment PathSegment,
    MemberAccessor? Accessor = null,
    object? Owner = null,
    bool IsSynthetic = false);
```

### 8.2 Adapter 优先级

推荐默认顺序：

```text
1. NullAdapter
2. SensitiveValueAdapter
3. PrimitiveAdapter
4. StringAdapter
5. EnumAdapter
6. ByteArrayAdapter
7. TaskAdapter
8. ExceptionAdapter
9. QueryableAdapter
10. DictionaryAdapter
11. List/ArrayAdapter
12. EnumerableAdapter
13. DynamicObjectAdapter
14. Custom Domain Adapter
15. DefaultObjectAdapter
```

需要注意：

- `string` 实现 `IEnumerable<char>`，必须先于 Enumerable。
- Dictionary 也可枚举，必须先于 Enumerable。
- `byte[]` 应优先显示为二进制摘要，而不是普通列表。
- `IQueryable` 必须先于 Enumerable，以避免意外执行查询。
- Adapter 选择结果应缓存到“类型 + 上下文特征”的匹配缓存，但包含成员敏感策略的结果不能只按 Type 缓存。

### 8.3 Registry

```csharp
public sealed class ObjectAdapterRegistry
{
    private readonly IReadOnlyList<IObjectAdapter> _adapters;

    public ObjectAdapterRegistry(IEnumerable<IObjectAdapter> adapters)
    {
        _adapters = adapters
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public IObjectAdapter Resolve(in ObjectAdapterContext context)
    {
        foreach (var adapter in _adapters)
        {
            try
            {
                if (adapter.CanHandle(context))
                    return adapter;
            }
            catch (Exception ex)
            {
                // 记录插件匹配错误，继续匹配其他 Adapter。
                context.InspectionContext.Diagnostics.AdapterMatchFailed(
                    adapter.Name, ex);
            }
        }

        throw new InvalidOperationException("No object adapter matched.");
    }
}
```

必须确保 `DefaultObjectAdapter` 永远可兜底。对相同 Priority 使用稳定的名称排序，并在诊断页报告潜在冲突。

---

## 9. 普通对象与成员策略

### 9.1 成员策略

```csharp
public interface IMemberPolicy
{
    MemberDecision Evaluate(
        MemberInfo member,
        Type ownerType,
        ObjectInspectionContext context);
}

public sealed record MemberDecision(
    bool Include,
    bool CanRead,
    bool CanWrite,
    bool IsSensitive,
    string DisplayName,
    int Order,
    string? ExclusionReason = null);
```

默认规则：

- 包含 Public Instance Property 和 Field。
- 跳过索引器、方法、事件、编译器生成后备字段。
- `[Browsable(false)]` 默认隐藏。
- 使用 `[DisplayName]` / `[Display]` 作为名称和顺序元数据。
- NonPublic、Static 由选项控制。
- 名称或 Attribute 命中敏感规则时，在值进入 Format/Search/Copy 前脱敏。
- `DebuggerBrowsable(Never)` 可配置为隐藏。

### 9.2 Getter 异常隔离

```csharp
public static ValueReadResult ReadSafely(
    MemberAccessor accessor,
    object owner)
{
    try
    {
        return ValueReadResult.Success(accessor.Getter(owner));
    }
    catch (Exception ex)
    {
        var actual = ex is TargetInvocationException { InnerException: not null }
            ? ex.InnerException
            : ex;

        return ValueReadResult.Failure(actual);
    }
}
```

异常必须转换为当前成员的 Error 节点，不能中断兄弟节点加载。

### 9.3 同步 Getter 超时的边界

C# 无法安全强制中止任意同步 Getter。以下做法均有明显风险：

- `Task.Run` + `WhenAny` 只能停止等待，后台 Getter 仍会继续执行；
- 取消令牌对不接受令牌的 Getter 无效；
- 中止线程不安全，也不被现代 .NET 支持；
- 每个 Getter 启动一个线程会造成线程泄漏和资源耗尽。

因此建议分级：

1. **默认进程内模式**：读取公共 Getter；设置警告阈值、并发上限和慢 Getter 诊断。超时后 UI 可返回，但后台工作仍需被限流和监控。
2. **安全模式**：允许策略将未知或标记为危险的 Getter 改为“点击后读取”。
3. **高隔离模式（后续）**：在独立进程中构建可传输的对象代理或执行特定读取；只有进程边界才能可靠回收永久阻塞的执行。

产品文案不能承诺“2 秒后终止 Getter”，只能承诺“2 秒后停止等待并标记超时”。

---

## 10. 元数据与访问器缓存

### 10.1 TypeMetadata

```csharp
public sealed record TypeMetadata(
    Type Type,
    IReadOnlyList<MemberAccessor> Members,
    string DisplayName,
    bool IsValueType,
    bool IsCompilerGenerated);

public sealed class MemberAccessor
{
    public required string Name { get; init; }
    public required Type ValueType { get; init; }
    public required MemberInfo MemberInfo { get; init; }
    public required MemberKind Kind { get; init; }
    public required Func<object, object?> Getter { get; init; }
    public Action<object, object?>? Setter { get; init; }
}
```

### 10.2 Getter 编译

```csharp
private static Func<object, object?> CreateGetter(PropertyInfo property)
{
    var owner = Expression.Parameter(typeof(object), "owner");
    var typedOwner = Expression.Convert(owner, property.DeclaringType!);
    var read = Expression.Property(typedOwner, property);
    var boxed = Expression.Convert(read, typeof(object));
    return Expression.Lambda<Func<object, object?>>(boxed, owner).Compile();
}
```

对字段可用 `Expression.Field`。Setter 采用相同方式生成。

### 10.3 缓存策略

- 普通应用可使用 `ConcurrentDictionary<Type, Lazy<TypeMetadata>>`。
- 若宿主使用可卸载 `AssemblyLoadContext`，强引用 Type 的全局字典会阻止卸载；应改用 `ConditionalWeakTable<Type, ...>`，或为每个会话/插件上下文维护可释放缓存。
- 元数据缓存只存类型结构，不存对象实例。
- 生成 Getter 失败时回退到 `PropertyInfo.GetValue()`，并记录诊断。
- AOT/Trim 模式下优先使用生成式 Adapter；反射路径标记所需的 DynamicDependency 或保留配置。

---

## 11. 对象身份与循环引用

### 11.1 引用相等比较器

```csharp
public sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static ReferenceEqualityComparer Instance { get; } = new();

    public new bool Equals(object? x, object? y)
        => ReferenceEquals(x, y);

    public int GetHashCode(object obj)
        => RuntimeHelpers.GetHashCode(obj);
}
```

不能使用普通 `HashSet<object>` 的默认值相等，因为业务对象可能重写 `Equals()`。

### 11.2 两种模式

#### DetectCyclesOnly

只检查当前祖先链。相同对象可在不同分支独立展开，但当前路径中的回边会成为 Reference 节点。

实现方式：每个加载请求携带不可变祖先身份链：

```csharp
public sealed record TraversalContext(
    ImmutableHashSet<object> Ancestors,
    ReferenceMode ReferenceMode);
```

#### ShowSharedReferences

维护会话级首次出现表。对象第一次出现后，其他位置显示引用目标。

```csharp
public sealed record ObjectReferenceIdentity(
    long IdentityId,
    ObjectPath FirstPath);
```

### 11.3 身份跟踪范围

默认不跟踪：

- `null`
- 值类型
- `string`
- 由策略标记为 Atomic 的不可变类型

默认跟踪普通引用对象、集合、字典和异常。

### 11.4 身份表生命周期

身份表只能存活于 InspectionSession。切换根对象或关闭 Viewer 后必须释放。若需要在会话中允许对象被回收，应考虑弱引用表；但只要节点自身持有对象，弱表不能单独解决保活问题，因此更重要的是及时释放节点和订阅。

---

## 12. 懒加载实现

### 12.1 加载算法

```csharp
public async Task<NodeLoadResult> LoadChildrenAsync(
    ObjectNode node,
    TraversalContext traversal,
    long requestGeneration,
    CancellationToken cancellationToken)
{
    if (!node.CanExpand)
        return NodeLoadResult.NotExpandable();

    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        _session.SessionToken);

    await _loadGate.WaitAsync(linked.Token).ConfigureAwait(false);
    try
    {
        if (requestGeneration != _session.Generation)
            return NodeLoadResult.Stale();

        var context = CreateAdapterContext(node, traversal);
        var adapter = _registry.Resolve(context);
        var children = new List<ObjectNode>();

        await foreach (var member in adapter.GetChildrenAsync(
            node,
            _inspectionContext,
            linked.Token).ConfigureAwait(false))
        {
            linked.Token.ThrowIfCancellationRequested();

            if (_limits.WouldExceedNodeBudget(children.Count))
            {
                children.Add(CreateTruncatedNode(node, "Node limit reached"));
                break;
            }

            children.Add(CreateChildNode(node, member, traversal));
        }

        return NodeLoadResult.Success(children);
    }
    catch (OperationCanceledException) when (linked.IsCancellationRequested)
    {
        return NodeLoadResult.Cancelled();
    }
    catch (Exception ex)
    {
        return NodeLoadResult.Failed(ex);
    }
    finally
    {
        _loadGate.Release();
    }
}
```

### 12.2 并发与幂等性

每个节点需维护一个加载互斥或正在进行的 Task：

- 同一节点多次快速展开只共享一次加载；
- 折叠可取消“非必需”加载；
- LoadMore 请求携带页游标；
- 刷新提升节点版本，旧结果不得覆盖；
- 失败后允许显式重试；
- UI 不直接修改 `LoadState`，由 ViewModel 调用服务后反映 Core 结果。

### 12.3 UI 线程切换

Core 内部全部使用 `ConfigureAwait(false)`，不依赖 UI SynchronizationContext。WPF ViewModel 在应用结果时使用注入的调度器抽象：

```csharp
public interface IUiDispatcher
{
    bool CheckAccess();
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
}
```

这样可测试，也避免 Core 引用 `Dispatcher`。

---

## 13. 集合、字典与序列

### 13.1 数组和 IList

- 数组和 `IList` 支持 O(1) 索引，适合基于 offset/pageSize 分页。
- 页面节点范围为 `[offset, min(offset + pageSize, count))`。
- 下一页使用合成 `LoadMore` 节点，携带 `CollectionCursor`。
- 多维数组需将线性 offset 映射为多维索引，不一次性生成全部索引组合。

```csharp
public sealed record CollectionCursor(int Offset, int PageSize);
```

### 13.2 Dictionary

Dictionary 条目可展示为：

```text
["StationId"]     Key = "StationId", Value = "S01"
├─ Key             "StationId"
└─ Value           "S01"
```

对于通用非泛型 `IDictionary` 可直接访问 `DictionaryEntry`。对于只实现 `IReadOnlyDictionary<TKey,TValue>` 的类型，使用缓存后的接口访问器。Key 的 `ToString()` 仍须通过安全 Formatter。

### 13.3 IEnumerable

`IEnumerable` 存在一次性、无限、远程、易变和有副作用等风险。默认行为：

```text
Sequence    Enumeration not started    IEnumerable<T>
└─ Start enumeration...
```

开始后维护 Enumerator 会话：

```csharp
public sealed class EnumerationSession : IDisposable
{
    public required IEnumerator Enumerator { get; init; }
    public int CurrentIndex { get; set; }
    public bool IsCompleted { get; set; }
    public void Dispose() => (Enumerator as IDisposable)?.Dispose();
}
```

注意：

- Enumerator 必须在节点折叠释放、根对象切换或会话结束时释放。
- 不能用 `Skip(offset).Take(pageSize)` 重复枚举一次性序列。
- 每次分页从同一个 Enumerator 继续推进。
- 枚举中途异常显示 Error 节点，并保留已读取项。
- 达到 `MaximumEnumerableItems` 后显示 Truncated。
- 刷新一次性序列默认不重新枚举，需用户明确确认。

### 13.4 IQueryable

`QueryableAdapter` 必须优先匹配，并且只展示：

- ElementType
- Provider 类型
- Expression 文本的安全摘要
- “Execute query”受控命令（若宿主明确启用，后续版本）

V0.1/V0.2 不调用 `ToList()`、`Count()` 或任何会执行 Provider 的操作。

---

## 14. Formatter 与安全展示

### 14.1 Formatter 接口

```csharp
public interface IObjectValueFormatter
{
    int Priority { get; }
    string Name { get; }

    bool CanFormat(Type? declaredType, Type? runtimeType, object? value);

    ValueDisplay Format(
        Type? declaredType,
        object? value,
        ValueFormattingContext context);
}
```

### 14.2 默认格式

- `string`：引号、转义、长度限制。
- `char`：字符字面量和可选码点。
- 整数：Decimal / Hex / Binary。
- 浮点数：Round-trip 或配置格式。
- `decimal`：保留精度，不经过 double。
- `DateTime`：显示到毫秒/更高精度，并明确 Kind。
- `DateTimeOffset`：保留 Offset。
- `Guid`：标准 D 格式。
- `byte[]`：`byte[4096] 4D 5A 90 00…`。
- Enum：`Running (2)`。
- 普通对象：`{TypeName}` 或安全的 DebuggerDisplay。
- 集合：`Count = n`，不为获取 Count 而枚举。
- Exception：`InvalidOperationException: message`。

### 14.3 ToString 安全

默认 Formatter 不应对所有对象无条件调用 `ToString()`。策略建议：

1. 已知原子类型使用专用 Formatter。
2. 可通过 Attribute 或 Adapter 明确允许业务 `ToString()`。
3. 未知对象默认显示 `{RuntimeType.Name}`。
4. 调用 `ToString()` 时捕获异常、截断结果、应用脱敏规则。

### 14.4 敏感值处理顺序

```text
成员发现
  → SensitiveDataPolicy 判定
  → 若敏感：产生 Masked ValueDisplay
  → 仅 Masked Display 进入 Search / Copy / Snapshot / UI
  → RawValue 只在受控会话内部保留
```

敏感判断不能仅作为 UI Converter，否则复制、搜索和快照会绕过脱敏。

---

## 15. DebuggerDisplay 支持

V0.2 可实现受限解析器，只支持：

```csharp
[DebuggerDisplay("{Name,nq} ({Id})")]
```

建议只允许：

- 简单成员名；
- 由点连接的受限成员链；
- `nq` 等少量格式标记；
- 不允许方法调用、索引器、赋值和任意表达式。

解析结果应编译为受限访问计划，并通过相同的成员策略、异常隔离、脱敏和长度限制。不要使用通用 C# 动态编译执行 Attribute 文本。

`DebuggerTypeProxyAttribute` 可延后；它可能实例化代理并执行未知逻辑，必须有单独策略。

---

## 16. WPF 表现层方案

### 16.1 控件外观

建议 `ObjectViewerControl` 作为可复用 `Control` 或 `UserControl`，暴露依赖属性：

```csharp
public static readonly DependencyProperty ValueProperty =
    DependencyProperty.Register(
        nameof(Value),
        typeof(object),
        typeof(ObjectViewerControl),
        new PropertyMetadata(null, OnValueChanged));

public object? Value
{
    get => GetValue(ValueProperty);
    set => SetValue(ValueProperty, value);
}
```

其他依赖属性：

- `RootName`
- `DeclaredType`
- `Options`
- `IsReadOnly`
- `SelectedPath`
- `SelectedNode`
- `AdapterProvider`
- `Theme`

输入 `Value` 改变时，控件只把请求交给 ViewModel；不在 code-behind 中执行反射。

### 16.2 ViewModel 层

```csharp
public sealed partial class ObjectViewerViewModel : ObservableObject,
    IAsyncDisposable
{
    [ObservableProperty]
    private ObjectNodeViewModel? selectedNode;

    [ObservableProperty]
    private string searchText = string.Empty;

    public VisibleNodeCollection VisibleNodes { get; }

    public IAsyncRelayCommand<ObjectNodeViewModel> ToggleExpandCommand { get; }
    public IAsyncRelayCommand RefreshSelectedCommand { get; }
    public IRelayCommand CopyPathCommand { get; }

    // 会话创建、切换和释放逻辑
}
```

可使用 CommunityToolkit.Mvvm，但 Core 不依赖该包。

### 16.3 逻辑树与扁平可见投影

```csharp
public sealed class VisibleObjectNode
{
    public required ObjectNodeViewModel Node { get; init; }
    public required int IndentLevel { get; init; }
    public bool IsLastSibling { get; init; }
}
```

`VisibleNodeCollection` 维护当前展开状态下的行：

- 展开节点：将其已加载的直接子节点插入父行之后；递归插入已经展开的后代。
- 折叠节点：一次移除后续所有 `IndentLevel > parentLevel` 的行。
- LoadMore：替换自身或在前面插入新页，避免全表刷新。
- 搜索定位：确保祖先链被加载/展开后滚动到目标。

操作复杂度应与受影响的可见子树大小相关，而不是每次重新扁平化整棵逻辑树。

### 16.4 DataGrid 虚拟化

```xml
<DataGrid ItemsSource="{Binding VisibleNodes}"
          AutoGenerateColumns="False"
          EnableRowVirtualization="True"
          EnableColumnVirtualization="True"
          VirtualizingPanel.IsVirtualizing="True"
          VirtualizingPanel.VirtualizationMode="Recycling"
          ScrollViewer.CanContentScroll="True"
          HeadersVisibility="Column"
          SelectionMode="Single">
    <DataGrid.Columns>
        <DataGridTemplateColumn Header="Name" Width="2*" />
        <DataGridTextColumn Header="Value"
                            Binding="{Binding Node.DisplayValue}"
                            Width="3*" />
        <DataGridTextColumn Header="Type"
                            Binding="{Binding Node.DisplayType}"
                            Width="2*" />
    </DataGrid.Columns>
</DataGrid>
```

Name 列模板包含：

- 基于 `IndentLevel` 的左边距；
- 展开/折叠按钮；
- 节点类型图标；
- 名称文本；
- Loading / Error / Masked / Changed 状态图标。

注意事项：

- 避免在行模板中嵌套 ItemsControl，否则破坏虚拟化。
- 行高尽量固定；全文预览使用弹窗或详情面板。
- `DataGrid` 分组和复杂行详情可能关闭虚拟化，应谨慎使用。
- 大量变更使用批量集合通知或范围集合，避免每项触发完整布局。

### 16.5 主题与可访问性

- 颜色通过 DynamicResource，不写死。
- 状态同时使用图标/文本/AutomationProperties，不只依赖颜色。
- 展开按钮提供自动化名称和 ExpandCollapsePattern。
- 支持高 DPI、系统字体缩放和明暗主题。
- Name、Value、Type 和 Status 应形成清晰的屏幕阅读器行描述。

---

## 17. 搜索设计

### 17.1 已加载节点搜索

默认仅扫描逻辑树中已存在的节点，不触发新的 Getter：

```csharp
public IEnumerable<ObjectNode> SearchLoaded(
    SearchQuery query,
    ObjectNode root)
```

可匹配字段：Name、Display.Text、Display.TypeText、Display Path。搜索索引只包含已脱敏文本。

### 17.2 深度搜索

深度搜索是一次独立的有边界图遍历：

```csharp
public sealed record DeepSearchOptions(
    int MaximumDepth,
    int MaximumNodes,
    int MaximumItemsPerCollection,
    TimeSpan Timeout,
    bool EnumerateUnknownSequences = false);
```

要求：

- 与普通浏览使用同一 Adapter、Policy、脱敏和引用规则；
- 默认不执行 `IQueryable`，不枚举未知序列；
- 结果包含完整路径、匹配字段和是否截断；
- 允许用户取消；
- 不必把全部搜索经过节点永久加入 UI 逻辑树，可以保存轻量结果；
- 用户选择结果后，再按路径逐段展开到目标。

---

## 18. 刷新与事件订阅

### 18.1 手动刷新

刷新范围：

- 当前节点值；
- 当前节点已加载子树；
- 全部已加载叶节点。

刷新流程：

```text
提升节点版本
  → 取消该节点旧加载
  → 重新读取 IValueSource
  → 重新选择 Adapter / Formatter
  → 比较类型和值摘要
  → 若运行时类型或结构发生变化，失效子节点
  → 增量更新可见投影
```

### 18.2 定时轮询

- 使用单一调度器，不为每个节点创建 `DispatcherTimer`。
- 按刷新周期将节点分桶。
- 设置最大并发和每轮预算。
- 窗口隐藏、最小化或不活动时可降频。
- 只刷新可见或 Watch 节点，除非调用方另有配置。

### 18.3 事件驱动

对 `INotifyPropertyChanged` 和 `INotifyCollectionChanged`：

- 优先使用 WPF WeakEventManager 或 Core 自己的可释放订阅包装。
- 收到 `PropertyChanged` 后只刷新对应已加载成员。
- `PropertyChanged(null/"")` 视为全部属性失效，但仍按预算刷新。
- Collection Reset 不应无条件重建整个对象图；只失效当前集合页并保留选中路径的恢复尝试。
- 所有订阅登记到 InspectionSession，统一释放。

---

## 19. 编辑方案

### 19.1 默认策略

```csharp
public enum EditPolicyMode
{
    ReadOnly,
    PublicSetters,
    ExplicitlyAllowedMembers,
    FullTrust
}
```

推荐默认 `ReadOnly`。生产和设备控制场景推荐 `ExplicitlyAllowedMembers`，配合 `[ObjectViewerEditable]` 或宿主白名单。

### 19.2 转换接口

```csharp
public interface IObjectValueConverter
{
    int Priority { get; }
    bool CanConvert(Type targetType, string? formatName);

    ConversionResult Convert(
        string input,
        Type targetType,
        CultureInfo culture);
}

public sealed record ConversionResult(
    bool Success,
    object? Value,
    string? ErrorMessage);
```

支持顺序：Nullable → Enum → Boolean → Numeric → Date/Time → Guid → TypeConverter → 业务 Converter → 可选 JSON。

### 19.3 编辑事务

```text
进入编辑
  → 保存原 Display 与节点版本
  → 文本转换
  → 业务验证
  → EditPolicy 再校验
  → 调用 Setter
  → 重新读取成员
  → 格式化并刷新
  → 审计成功/失败
```

由于任意 Setter 可能有副作用，Viewer 不能承诺回滚。需要事务的业务对象应由自定义 Adapter/Editor 实现显式提交。

---

## 20. 快照与 Diff

### 20.1 快照模型

```csharp
public sealed record ObjectSnapshot(
    string Name,
    DateTimeOffset CreatedAt,
    SnapshotOptions Options,
    ObjectSnapshotNode Root);

public sealed record ObjectSnapshotNode(
    string StablePath,
    string DisplayPath,
    string Name,
    string TypeName,
    string? FormattedValue,
    SnapshotValueKind Kind,
    bool IsMasked,
    SnapshotNodeState State,
    IReadOnlyList<ObjectSnapshotNode> Children);
```

快照保存受控节点描述，不直接序列化原始对象。

### 20.2 构建策略

- 默认仅快照已加载节点，快速且无额外副作用。
- “深度快照”必须明确配置边界，等价于受控深度遍历。
- 保存 MaximumDepth、MaximumNodes、CollectionPageSize、Formatter 和 Culture，以便解释差异。
- 敏感值只保存 Masked 标记和掩码文本。

### 20.3 Diff 算法

1. 以 StablePath 建立索引。
2. 同路径比较 Kind、TypeName、FormattedValue 和 Error 状态。
3. 路径只在 B 中存在：Added。
4. 路径只在 A 中存在：Removed。
5. 类型不同：TypeChanged。
6. 值或错误状态不同：Changed。
7. 对不可跨会话稳定的 Dictionary Key，标记低置信匹配。

大型快照可使用流式比较，避免同时建立两个巨大树的完整 UI。

---

## 21. Task、异步对象与动态对象

### 21.1 TaskAdapter

Task 节点展示：

- Status
- IsCompleted / IsCanceled / IsFaulted
- Exception（若 faulted）
- Result（仅当已完成且是 `Task<T>`）

不得通过 `.Result` 或 `.Wait()` 阻塞。未完成时可订阅完成并刷新节点，订阅必须跟随 Session 释放。

### 21.2 ValueTask

`ValueTask` 可能只能消费一次，不应被通用 Viewer 自动 await。V1.0 前建议仅显示类型和状态说明；若调用方明确提供安全的 Adapter，再执行受控查看。

### 21.3 DynamicObject

动态成员访问可能执行任意逻辑。默认：

- 优先展示静态可见成员；
- 只有宿主启用后才调用动态成员枚举；
- 动态 Getter 使用与反射 Getter 相同的隔离、并发和敏感规则；
- 对 `ExpandoObject` 可使用其字典接口安全浏览。

---

## 22. 插件与扩展模型

### 22.1 注册方式

首期采用宿主显式注册，不自动扫描任意目录：

```csharp
services.AddObjectViewer(options =>
{
    options.AddAdapter<DeviceDataAdapter>();
    options.AddFormatter<RegisterValueFormatter>();
    options.AddConverter<DeviceStateConverter>();
    options.AddMemberPolicy<DeviceMemberPolicy>();
});
```

### 22.2 插件约束

- Adapter 必须声明名称、版本和优先级。
- `CanHandle` 应快速、无副作用，不读取复杂对象成员。
- 插件抛出的异常必须转换为诊断事件或局部 Error。
- 插件不能绕过统一脱敏策略；若需要原值，必须由安全上下文显式授权。
- 插件卸载场景使用独立 `AssemblyLoadContext`，缓存不得强引用插件 Type。
- 后续可加入签名、允许列表和兼容性清单。

### 22.3 自定义 Renderer

Renderer 属于 WPF 层，不应进入 Core Adapter。Adapter 输出结构化节点和 `PresentationHint`；WPF 根据 Hint 选择 DataTemplate 或打开专用查看器。

```csharp
public sealed record PresentationHint(
    string? ViewerKind = null,
    string? EditorKind = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
```

---

## 23. 依赖注入与公共 API

### 23.1 服务注册

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddObjectViewer(
        this IServiceCollection services,
        Action<ObjectViewerBuilder>? configure = null)
    {
        services.AddSingleton<ITypeMetadataCache, TypeMetadataCache>();
        services.AddSingleton<IObjectAdapterRegistry, ObjectAdapterRegistry>();
        services.AddSingleton<IFormatterRegistry, FormatterRegistry>();
        services.AddSingleton<IMemberPolicy, DefaultMemberPolicy>();
        services.AddSingleton<ISensitiveDataPolicy, DefaultSensitiveDataPolicy>();
        services.AddTransient<IObjectGraphExplorer, ObjectGraphExplorer>();
        configure?.Invoke(new ObjectViewerBuilder(services));
        return services;
    }
}
```

### 23.2 Viewer 公共调用方式

```csharp
var request = new InspectionRequest
{
    RootName = "order",
    Value = order,
    DeclaredType = typeof(IOrder),
    Options = InspectionOptions.SafeDefault
};

await viewerViewModel.SetRootAsync(request, cancellationToken);
```

### 23.3 Options

```csharp
public sealed record InspectionOptions
{
    public int CollectionPageSize { get; init; } = 100;
    public int MaximumDepth { get; init; } = 32;
    public int MaximumStringLength { get; init; } = 2_000;
    public int MaximumEnumerableItems { get; init; } = 10_000;
    public int MaximumNodesPerSession { get; init; } = 100_000;
    public int MaximumConcurrentLoads { get; init; } = 4;
    public TimeSpan GetterWarningThreshold { get; init; } = TimeSpan.FromSeconds(2);
    public ReferenceMode ReferenceMode { get; init; } = ReferenceMode.DetectCyclesOnly;
    public EditPolicyMode EditPolicy { get; init; } = EditPolicyMode.ReadOnly;
    public bool ShowNonPublicMembers { get; init; }
    public bool ShowStaticMembers { get; init; }
    public bool AutoEnumerateUnknownSequences { get; init; }
    public bool ExecuteQueryable { get; init; }

    public static InspectionOptions SafeDefault { get; } = new();
}
```

Options 在会话创建时复制为不可变快照，避免加载过程中配置突变导致结果不一致。

---

## 24. 线程模型

1. WPF 命令从 UI 线程发起。
2. Core 的反射、格式化、枚举和插件调用在后台受限并发执行。
3. 普通、快速、已知安全的值格式化可直接执行，但应统一经过调度策略。
4. Core 不捕获 UI SynchronizationContext。
5. 结果通过 `IUiDispatcher` 一次性或分批写回 ViewModel。
6. UI 集合只能在 UI 线程修改。
7. 切换根对象时取消旧 Session，并增加 Generation。
8. 所有后台异常都被观察并转换为结果；禁止 fire-and-forget 未观察 Task。

对 `Dispatcher` 的使用集中在 WPF Infrastructure 中，不散布到 Adapter 或 Core。

---

## 25. 内存与资源管理

### 25.1 可能的泄漏源

- `INotifyPropertyChanged` / CollectionChanged 强事件订阅；
- Task continuation 捕获 ViewModel；
- 全局缓存强引用可卸载 Type；
- 节点持有 Owner 和 RawValue；
- Enumerator 未 Dispose；
- DispatcherTimer 持有节点；
- 插件静态事件；
- 复制/搜索索引长期缓存大字符串。

### 25.2 管理策略

- InspectionSession 实现 `IAsyncDisposable`。
- 所有订阅、Enumerator 和 CTS 登记到会话资源表。
- 切换根对象采用“先创建新会话标识，再取消并释放旧会话”的顺序，避免竞态。
- Formatter 结果限制长度；Tooltip 全文按需读取，不默认复制一份。
- 扁平可见集合只保存 ViewModel 引用，不复制 RawValue。
- 类型缓存支持卸载上下文。
- 性能测试包含 WeakReference + 强制 GC 的泄漏验证。

---

## 26. 诊断与遥测

定义内部诊断事件：

```text
SessionStarted / SessionDisposed
NodeLoadStarted / Completed / Cancelled / Failed
GetterSlow / GetterFailed
AdapterResolved / AdapterFailed
EnumerationStarted / Truncated / Failed
LimitReached
SensitiveValueMasked
EditSucceeded / EditFailed
SnapshotCreated / DiffCompleted
```

事件字段仅包含：

- 会话匿名 ID；
- 类型名称（可配置是否允许）；
- Adapter 名称；
- 节点数量；
- 耗时；
- 错误代码；
- 路径哈希或安全路径。

不得默认记录 RawValue、完整字符串、Token、Password 或 ConnectionString。

---

## 27. 测试策略

### 27.1 单元测试

Core：

- Adapter 优先级和确定性冲突处理；
- 声明类型/运行时类型；
- Getter 异常解包；
- 引用相等、循环和共享引用；
- 路径编码/解码与特殊字符；
- 集合分页边界 0/1/99/100/101；
- 一次性 Enumerable 连续分页；
- Queryable 不执行；
- 敏感值不进入 Display/Search/Snapshot；
- Formatter 异常与截断；
- Generation 过期结果丢弃；
- Session 释放 Enumerator 和订阅。

WPF：

- 展开后正确插入可见行；
- 折叠只删除后代行；
- LoadMore 不产生重复项；
- 选中行在增量更新后保持；
- UI 集合只从 UI 线程修改；
- 键盘操作和命令状态；
- DataTemplate 状态与 AutomationProperties。

### 27.2 集成测试

- Demo 宿主加载真实复杂对象；
- 高频属性变更与 CollectionChanged；
- 快速切换根对象和快速展开/折叠；
- Adapter 插件抛异常；
- 编辑成功、转换失败、Setter 失败；
- 快照与 Diff；
- 明暗主题和高 DPI。

### 27.3 性能测试

建议使用 BenchmarkDotNet 测试：

- 元数据首次构建与缓存命中；
- 反射 Getter vs 编译 Getter；
- 1,000/10,000/100,000 节点创建；
- 扁平集合展开/折叠；
- 100 页集合加载；
- 快照和 Diff；
- 搜索已加载节点。

WPF 性能使用独立 Demo + ETW/Visual Studio Profiler/WPA 观察：

- UI 线程帧时间；
- 视觉元素数量；
- GC 分配率；
- 滚动期间布局次数；
- 切换根对象后的存活对象。

### 27.4 安全测试

- 敏感属性在 UI、复制、搜索、快照、日志、异常中均不可泄漏；
- 非公共/静态成员默认不可见；
- `IQueryable` 和未知序列默认不执行；
- 恶意 Formatter/Adapter 抛异常或返回超长文本；
- DebuggerDisplay 不能执行方法或任意表达式；
- 编辑白名单不可绕过。

---

## 28. 性能预算

以下为初始工程目标，最终需在基准硬件上验证：

| 操作 | 目标 |
|---|---|
| 设置根对象并显示根行 | P95 < 100 ms |
| 缓存命中后展开 ≤ 50 个普通成员 | P95 < 200 ms，不计业务 Getter 自身阻塞 |
| 插入 100 个分页项到逻辑树 | P95 < 100 ms |
| WPF 可见行滚动 | 常规视口保持流畅，不为不可见行创建容器 |
| 搜索 10,000 个已加载节点 | P95 < 200 ms |
| 关闭/切换会话 | 立即取消；资源异步释放，不阻塞 UI |

节点和会话预算：

- 默认单页 100；
- 默认最大深度 32；
- 默认未知序列上限 10,000；
- 默认会话节点上限 100,000；
- 默认并发加载 4；
- 达到预算时产生显式 Truncated 节点。

---

## 29. 安全设计

### 29.1 默认安全模式

- 只读；
- 仅公共实例成员；
- 不执行方法、索引器、`IQueryable`；
- 未知 `IEnumerable` 需要确认；
- 敏感值脱敏；
- DebuggerDisplay 只支持受限成员表达式；
- 插件由宿主显式注册；
- 所有操作有节点、深度、条目和并发限制。

### 29.2 Attribute

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ObjectViewerSensitiveAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ObjectViewerEditableAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ObjectViewerIgnoreAttribute : Attribute;
```

Attribute 只是策略输入；宿主策略拥有最终决定权。

### 29.3 威胁边界

Object Viewer 运行在宿主进程内时，无法把未知 Getter 变成完全安全的代码。该方案主要防止意外执行、无限遍历、泄漏和单点异常，不构成恶意代码沙箱。若需检查不可信程序集或对象，必须使用独立进程和受限 IPC DTO，不能仅依赖 try/catch。

---

## 30. AOT、Trim 与 Source Generator

### 30.1 反射路径

普通桌面 JIT 环境可采用反射 + Expression Compile。若目标是 Trim/AOT：

- 动态发现的成员可能被裁剪；
- `Expression.Compile()` 可能不可用或退化为解释执行；
- 插件发现和 DebuggerDisplay 解析受限制；
- 需要保留元数据声明。

### 30.2 生成式路径

保留以下抽象：

```csharp
public interface IGeneratedObjectAdapter<T> : IObjectAdapter
{
    ObjectNodeDescriptor Describe(T? value, ObjectInspectionContext context);
    IEnumerable<ObjectMemberDescriptor> GetChildren(
        T value,
        ObjectInspectionContext context);
}
```

Source Generator 可扫描标注类型并生成直接 Getter/Setter、成员元数据和 Formatter 提示。Registry 优先选择 Generated Adapter，反射 Adapter 作为后备。

V0.1 不必实现 Generator，但公共接口不要把 `PropertyInfo` 作为唯一成员来源。

---

## 31. 实施计划

### 阶段 0：技术验证（1–2 个迭代）

交付：

- Core 原型；
- ObjectNode、AdapterRegistry、DefaultObjectAdapter；
- 引用检测；
- WPF 扁平虚拟树 POC；
- 10 万逻辑节点滚动验证；
- Getter 异常和一次性 Enumerable 实验。

退出条件：

- 架构可行；
- DataGrid 虚拟化稳定；
- 无递归爆栈；
- 基础泄漏测试通过。

### 阶段 1：V0.1 浏览核心

交付：

- Primitive/String/Enum/Object/List/Dictionary Adapter；
- 懒加载、分页、错误节点；
- Name/Value/Type；
- 路径复制；
- 敏感脱敏；
- Safe 默认配置；
- Demo 和核心测试。

### 阶段 2：V0.2 工程增强

交付：

- Enumerable/Queryable/Task/Exception Adapter；
- 搜索、刷新、事件订阅；
- DebuggerDisplay 子集；
- 快照与 Diff；
- 性能、可访问性、主题完善；
- 插件诊断。

### 阶段 3：V1.0 平台化

交付：

- 编辑与审计；
- Watch；
- 专用查看器；
- 导出；
- Source Generator；
- 插件信任模型；
- 可选远程/进程隔离设计。

---

## 32. 关键里程碑和评审门

| 里程碑 | 必须评审的内容 |
|---|---|
| Architecture Review | Core/WPF 边界、Adapter 模型、线程模型、路径规范 |
| POC Review | 扁平虚拟树、大集合、循环引用、加载取消 |
| V0.1 Security Review | 脱敏、复制、错误信息、非公共成员策略 |
| V0.1 Performance Gate | 根展示、分页、滚动、内存释放 |
| V0.2 Reliability Review | 搜索边界、事件释放、快照一致性、插件隔离 |
| V1.0 Safety Review | 编辑白名单、审计、导出和远程能力 |

---

## 33. 关键风险与技术对策

| 风险 | 技术对策 |
|---|---|
| 同步 Getter 永久阻塞 | 并发门、慢调用告警、危险成员手动读取；高风险模式使用进程隔离 |
| UI 大数据卡顿 | 扁平可见列表、Recycling 虚拟化、批量通知、固定行高 |
| 循环/共享引用错误 | 引用相等 + 祖先链/全局身份两种模式 + 专门测试图 |
| Enumerable 重复消费 | 保存 Enumerator 连续分页；切换会话时 Dispose |
| Queryable 意外执行 | 专用 Adapter 高优先级拦截，禁止自动 Count/枚举 |
| 脱敏旁路 | 在 Display 建立前执行敏感策略，统一供搜索/复制/快照使用 |
| 异步旧结果覆盖 | SessionId + Generation + NodeVersion 校验 |
| 插件破坏稳定性 | 调用边界 try/catch、超长限制、冲突诊断、宿主显式注册 |
| 缓存阻止程序集卸载 | ConditionalWeakTable 或按插件上下文释放缓存 |
| 事件订阅泄漏 | Session 统一登记、弱事件、关闭时集中释放 |
| 编辑产生业务副作用 | 默认只读、白名单、重新读取、审计、明确不承诺回滚 |

---

## 34. 建议的架构决策记录（ADR）

### ADR-001：Core 不引用 WPF

**决定**：对象探索、Adapter、路径、格式化、快照全部位于独立 Core。

**理由**：便于测试、复用、未来 UI 迁移和进程外服务。

### ADR-002：使用逻辑树 + 扁平可见投影

**决定**：不直接将递归节点绑定到标准 TreeView。

**理由**：获得可靠多列和 UI 虚拟化；支持增量插入、键盘导航和搜索定位。

### ADR-003：所有子节点懒加载

**决定**：根对象设置时只构建根摘要。

**理由**：避免大对象、深对象、远程属性和循环图导致卡死。

### ADR-004：Adapter Registry 代替类型判断链

**决定**：每种类型行为由按优先级匹配的 Adapter 实现。

**理由**：核心稳定，业务扩展开放，优先级可测试。

### ADR-005：引用相等定义对象身份

**决定**：使用 `ReferenceEquals` 与 `RuntimeHelpers.GetHashCode`。

**理由**：业务 `Equals` 不代表对象身份，不能用于循环检测。

### ADR-006：默认只读和安全枚举

**决定**：编辑关闭；未知序列和 Queryable 不自动执行。

**理由**：通用工具不能假定访问行为无副作用。

### ADR-007：不承诺进程内强制终止 Getter

**决定**：超时仅停止等待，可靠终止依赖进程隔离。

**理由**：现代 .NET 没有安全的任意线程中止机制。

---

## 35. V0.1 最小接口草案

```csharp
public interface IObjectViewerSessionFactory
{
    InspectionSession Create(
        InspectionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record InspectionRequest
{
    public required string RootName { get; init; }
    public object? Value { get; init; }
    public Type? DeclaredType { get; init; }
    public InspectionOptions Options { get; init; } = InspectionOptions.SafeDefault;
}

public interface IObjectGraphExplorer
{
    ObjectNode CreateRoot(InspectionSession session, InspectionRequest request);

    Task<NodeLoadResult> LoadChildrenAsync(
        InspectionSession session,
        ObjectNode node,
        CancellationToken cancellationToken);

    Task<NodeRefreshResult> RefreshAsync(
        InspectionSession session,
        ObjectNode node,
        RefreshScope scope,
        CancellationToken cancellationToken);
}

public sealed record NodeLoadResult(
    NodeLoadOutcome Outcome,
    IReadOnlyList<ObjectNode> Children,
    NodeError? Error = null)
{
    public static NodeLoadResult Success(IReadOnlyList<ObjectNode> children)
        => new(NodeLoadOutcome.Success, children);
}
```

V0.1 先冻结较小公共 API；Adapter、Path 和 Node 的扩展接口应采用语义化版本和兼容性测试，避免后续插件生态被早期实现细节锁死。

---

## 36. 示例：展开一个普通对象

假设：

```csharp
public interface IAnimal
{
    string Name { get; }
}

public sealed class Dog : IAnimal
{
    public string Name { get; init; } = "Lucky";
    public int Age { get; init; } = 3;
    public Owner? Owner { get; init; }
}
```

处理过程：

1. Root 的声明类型为 `IAnimal`，运行时类型为 `Dog`。
2. Registry 选择 `DefaultObjectAdapter`。
3. 根节点摘要为 `{Dog}`，`CanExpand = true`。
4. 用户展开时，MetadataCache 返回 Name、Age、Owner 访问器。
5. 每个 Getter 独立执行并捕获异常。
6. Name 由 StringAdapter 格式化为 `"Lucky"`。
7. Age 由 PrimitiveAdapter 格式化为 `3`。
8. Owner 若为空，由 NullAdapter 显示 `null` 及其声明类型。
9. 结果写入逻辑树，VisibleNodeCollection 在根行后插入三行。
10. DataGrid 只创建视口所需的行容器。

---

## 37. 方案结论

推荐最终技术路线：

```text
WPF DataGrid / TreeList
        +
Flattened Visible Node Collection
        +
ObjectNode ViewModel
        +
InspectionSession / ObjectGraphExplorer
        +
Adapter Registry
        +
Formatter / Policy / Converter
        +
Type Metadata & Accessor Cache
        +
Reference Identity Map
```

这条路线将反射限制在基础设施层，将对象图风险封装在会话和策略中，并将 WPF 的职责收敛为虚拟化展示与交互。它既能满足类似 Visual Studio Locals/Watch 的日常浏览体验，又为集合分页、搜索、实时刷新、快照 Diff、受控编辑、业务插件、AOT 以及远程诊断保留清晰的演进路径。

首期应优先把以下五件事做正确：

1. 懒加载；
2. 引用身份与循环检测；
3. Getter/Formatter/Adapter 的局部错误隔离；
4. 扁平化虚拟树；
5. 默认安全和资源可释放。

只要这五个基础稳定，后续功能可以在不推翻架构的前提下逐步增加。

---

## 38. 外部数据源导入设计（V0.2 文件类 / V1.0 数据库）

> 2026-11-12 追加需求，对应 PRD §10.15 / 场景 I / 场景 J。决策记录见 `RequirementsConfirmation.md` D-11~D-14。

### 38.1 架构定位

导入层位于 Extensions，位于现有管线的最前端，物化结果进入既有观察管线后零改动复用全部能力：

```text
外部源（JSON/YAML/XML/CSV 文件、数据库查询）
        ↓  IDataSourceProvider.LoadAsync     ← 新增导入层（ObjectViewer.Extensions）
物化为内存表示（JsonNode / YamlNode / XElement / 行对象集合）
        ↓  既有管线（零改动复用）
InspectionSession → Adapter 选择 → 脱敏 → 懒加载 → 分页 → 虚拟化展示
```

### 38.2 契约草案

```csharp
public interface IObjectDataSourceProvider
{
    string Name { get; }

    bool CanHandle(DataSourceDescriptor descriptor);

    Task<DataSourceLoadResult> LoadAsync(
        DataSourceDescriptor descriptor,
        DataSourceLoadOptions options,
        CancellationToken cancellationToken);
}

public sealed record DataSourceDescriptor(
    string SourceKind,     // "file" | "database"
    string Location,       // 文件路径或连接标识（敏感：展示时脱敏）
    object? Payload);      // 如 SQL 文本、解析参数

public sealed record DataSourceLoadResult(
    string RootName,
    object? Value,         // 物化后的内存表示
    Type? DeclaredType,
    DataSourceOrigin Origin); // 来源元数据：格式、大小、加载时间

public sealed record DataSourceLoadOptions(
    long MaximumSourceSizeBytes,
    int MaximumItems,
    CultureInfo Culture);
```

### 38.3 物化矩阵

| 源 | 物化形态 | 展示 Adapter | 说明 |
|---|---|---|---|
| JSON | `System.Text.Json` 的 `JsonNode` | `JsonNodeAdapter`（新增） | 保留 number/string/bool/null 类型区分；零第三方依赖 |
| YAML | YamlDotNet 的 `YamlNode` | `YamlNodeAdapter`（新增） | anchors/aliases 物化为同一节点实例，天然进入共享引用检测 |
| XML | `XElement` | `XElementAdapter`（新增） | 元素/属性统一为子节点 |
| CSV | 行对象集合（表头→字典行） | 现有 `ListAdapter` | 无需新 Adapter |
| 数据库 | 查询结果行集合 | 现有 `ListAdapter` + 行对象 | V1.0；连接串按敏感值处理 |

### 38.4 关键规则

1. **Core 零依赖**：格式 Provider 与解析库（YamlDotNet 等）全部位于 Extensions；Core 只定义抽象。
2. **一次性快照**：导入后与源解耦；重载 = 显式重新导入，不承诺差异合并（与 FR-142 一致）。
3. **预算前置**：导入前检查文件大小/行数上限；超限显示 Truncated，不静默截断（FR-144）。
4. **脱敏同源**：敏感名称规则对键名/成员名同等生效——JSON 的 `"password"`、数据库列名 `ApiToken` 均被遮蔽（FR-145）。
5. **不可信输入**：外部数据视为不可信；解析全程在异常隔离与长度限制内进行。
6. **连接串处理**：连接字符串只存在于 Provider 会话内部；展示、复制、日志中一律脱敏（FR-146）。

### 38.5 实施排期

- **V0.2**：`IDataSourceProvider` 抽象 + JSON + YAML + XML + CSV 文件导入 + Demo"打开文件"入口 + 导入预算测试。
- **V1.0**：数据库查询结果导入（用户显式提交 SQL → 结果行物化）、连接管理、审计事件、可选凭证安全存储。
