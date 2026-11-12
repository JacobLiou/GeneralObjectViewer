# 通用 Object Viewer 需求确认书

- **文档名称**:需求确认书(Requirements Confirmation)
- **文档版本**:1.0
- **文档状态**:待评审
- **更新时间**:2026-11-12
- **依据文档**:`PRD.md`(v1.0 Draft)、`Proposal.md`(v1.0 Proposed)
- **目的**:在开工前固化对需求的理解、范围边界、关键决策和任务拆解,由需求方评审确认后作为实施基线

---

## 1. 已确认的关键决策

| # | 决策项 | 结论 | 状态 |
|---|---|---|---|
| D-01 | .NET 基线(PRD 待决策 #1) | **.NET 8 LTS** | 已确认 |
| D-02 | V0.1 成员展示范围(PRD 待决策 #4) | **公共实例 Properties + Fields** 一步到位,`ShowNonPublicMembers`/`ShowStaticMembers` 保持默认关闭 | 已确认 |
| D-03 | 实施节奏 | 先输出本确认书,评审通过后再进入开发 | 已确认 |

### 1.1 建议采纳的技术决策(需随本确认书一并确认)

| # | 决策项 | 建议 | 理由 |
|---|---|---|---|
| D-04 | WPF 树表格(PRD 待决策 #2) | **自研**:DataGrid + 扁平可见投影(Proposal §16) | Proposal 已给出完整方案;第三方组件引入依赖与主题/可访问性风险 |
| D-05 | 深度搜索首发版本(PRD 待决策 #5) | **V0.2** | PRD §7.2 已将其列入 V0.2 |
| D-06 | 敏感原值受控查看(PRD 待决策 #6) | V0.1 **不提供**,始终脱敏 | 安全优先;后续版本经授权机制再议 |
| D-07 | 同步 Getter 进程隔离(PRD 待决策 #3) | V0.1 **不做**,仅实现警告阈值 + 并发上限 + "停止等待"语义 | Proposal §9.3 分级策略,高隔离属后续版本 |
| D-08 | 快照持久化格式(PRD 待决策 #7) | 延后至 V0.2 设计快照功能时决策 | 不阻塞 V0.1 |
| D-09 | 插件签名/独立 ALC(PRD 待决策 #8) | V1.0;V0.1 仅宿主显式注册 | PRD §7.3 / NFR-025 |
| D-10 | 审计事件归属(PRD 待决策 #9) | V1.0 随编辑功能决策 | V0.1 无编辑能力 |

### 1.2 追加需求决策:外部数据源导入(2026-11-12 确认)

| # | 决策项 | 结论 | 状态 |
|---|---|---|---|
| D-11 | 外部数据源版本落点 | **V0.2 交付文件类导入**(JSON/YAML/XML/CSV,经 `IDataSourceProvider` 抽象物化进内存);**V1.0 交付数据库导入** | 已确认 |
| D-12 | 数据库交互语义 | **查询结果导入**:用户输入 SQL/选择查询 → 结果行物化为对象集合展示(不做表结构浏览) | 已确认 |
| D-13 | 技术选型授权 | C# / .NET 8 LTS / WPF;MVVM 库、测试框架、解析库等由实施方自由裁量 | 已确认 |
| D-14 | 实施方选型(基于 D-13 授权) | MVVM = CommunityToolkit.Mvvm(仅 Wpf 层);测试 = xUnit;JSON = System.Text.Json(内置);YAML = YamlDotNet(V0.2,置于 Extensions 层以保 Core 零依赖) | 已确认 |

> 外部数据源导入复用既有管线:导入 → 物化为内存表示(`JsonNode`/`YamlNode`/`XElement`/行对象) → 走 InspectionSession → Adapter → 脱敏 → 懒加载 → 分页。脱敏按成员名对外部数据同样生效(如 JSON 的 `"password"` 字段)。详见 PRD §10.15 与 Proposal §38。

---

## 2. 需求理解摘要(一句话)

构建一个类似 Visual Studio Locals/Watch 的**通用 .NET 运行时对象查看器**:接收任意 `object?`,以懒加载、错误隔离、循环检测、脱敏和分页为边界,将对象呈现为一棵**可控、可解释、可导航的观察结果树**,并以此为基座演进为支持搜索、刷新、快照 Diff、受控编辑和插件扩展的诊断平台。

### 2.1 必须刻进实现的七条产品规则(源自 PRD §14)

1. **懒加载优先** — 无用户动作或明确任务,不读取下一层
2. **安全优先于"看得更多"** — 未知序列、查询和敏感值不自动展开
3. **引用身份优先于值相等** — 循环检测使用 `ReferenceEquals`
4. **局部失败** — 成员读取失败只影响当前节点
5. **稳定路径** — 展示路径给人读,内部路径给机器匹配
6. **默认只读** — 编辑是显式能力,不是浏览的副作用
7. **结果边界透明** — 截断、取消、超时、未枚举、未授权均须显示

### 2.2 核心抽象认知

- **观察树 ≠ 对象图镜像**(FR-014):节点是观察结果的投影,不将原对象图直接序列化到 UI
- **Core 与 WPF 完全解耦**(ADR-001):Core 不引用 `PresentationFramework`/`Dispatcher`/Binding
- **Adapter Registry 代替类型判断链**(ADR-004):核心稳定、扩展开放、优先级可测试
- **超时仅停止等待,不承诺终止 Getter**(ADR-007)

---

## 3. V0.1 范围界定

### 3.1 范围内(依据 PRD §7.1 + Proposal 阶段 1)

**对象输入与会话**
- 任意 `object?` 根值,可指定根名称与声明类型(FR-001/002/003/005)
- 会话选项、权限策略、Adapter、Formatter 可由调用方传入(FR-004)
- 切换根对象:取消未完成加载、释放订阅、清空投影(NFR-012)

**节点模型与展示**
- 三列视图:Name / Value / Type;DeclaredType/RuntimeType 区分(FR-011)
- 节点类型覆盖 V0.1 所需:Root、Null、Primitive、String、Enum、Object、Property、Field、Collection、CollectionItem、Dictionary、DictionaryEntry、Reference、Error、Loading、LoadMore、Truncated(FR-013 子集)
- 每节点具备 Display Path 与稳定身份路径(FR-070/071/072/073)

**成员发现**
- 公共实例 Properties + Fields(D-02);默认跳过索引器、方法、事件
- `[Browsable(false)]`、`[DisplayName]`、`[Display]` 元数据支持(FR-023)
- Property/Field 区分并展示访问性、可写性(FR-024)

**值读取与错误隔离**
- 全部 Getter/字段访问/格式化独立隔离(FR-030)
- Error 节点含异常类型与安全处理后的消息;`TargetInvocationException` 解包内部异常(FR-031/032)
- 读取支持取消信号;"取消不保证终止正在执行的 Getter"语义(FR-034)
- 慢 Getter 警告阈值(2s,默认配置)(FR-035)

**类型处理(V0.1 Adapter 集)**
- `null`、基元/数值/布尔、`char`、`string`(引号/转义/长度截断)、`enum`(`Running (2)`)、`DateTime`/`DateTimeOffset`/`TimeSpan`/`Guid`/`Uri`、`byte[]`(长度+Hex前缀,不按普通集合展开)、普通对象、数组/`IList`(分页)、多维数组(Rank/各维长度/多维索引)、`Dictionary`(Key/Value/条目索引,字符串 Key 路径转义)
- `IEnumerable`/`IQueryable`/Task/Exception/Dynamic → **V0.2**(PRD §7.2)

**懒加载与分页**
- 设根不递归构建(FR-040);展开才加载(FR-041);未加载显示占位(FR-042)
- 集合分页,默认页 100,可配置(FR-043);LoadMore 不重复添加(FR-044)
- 限制:最大深度 32、最大字符串长度 2000、单次枚举上限 10000;达限显示 LimitReached/Truncated 节点(FR-045/046)

**引用与循环**
- 引用相等身份;DetectCyclesOnly 默认 + ShowSharedReferences 可选(FR-060/061)
- Reference 节点显示目标路径或身份占位(FR-062);默认不可递归展开(FR-064)

**格式化与脱敏**
- Formatter 独立于读取与 UI,按优先级选择(FR-080/081)
- Default/Decimal/Hex/Binary 显示模式(FR-083);截断/脱敏/格式化失败显式标记(FR-084)
- Culture 可指定;诊断路径不依赖 UI Culture(FR-085)
- 默认敏感名称:Password、Secret、Token、ApiKey、ConnectionString、Credential、PrivateKey、AccessToken(NFR-021)
- 脱敏在进入 Search/Copy/Snapshot/UI 之前完成,无旁路(NFR-023)

**UI 交互(V0.1 子集)**
- 单击展开触发加载,Loading 占位;展开失败保留父节点+局部 Error(PRD §8.4)
- 上下文菜单:复制显示值、复制显示路径、复制稳定路径、查看错误详情
- 长字符串默认截断,可查看全文/复制
- 键盘:方向键、Home/End、PageUp/Down、Ctrl+C

### 3.2 范围外(V0.1 明确不做)

- 搜索(已加载/深度)→ V0.2
- 刷新/轮询/`INotifyPropertyChanged`/`INotifyCollectionChanged` → V0.2
- 快照与 Diff → V0.2
- `IEnumerable` 显式枚举、`IQueryable`、Task、Exception 专用 Adapter、动态对象 → V0.2
- 受控编辑、Watch、插件系统、专用查看器、导出、Source Generator、远程代理 → V1.0
- DebuggerDisplay 基础支持 → V0.2(PRD §7.2)

### 3.3 采用的默认配置(不可偏离,PRD §12)

```text
CollectionPageSize        = 100
MaximumDepth              = 32
MaximumStringLength       = 2,000 characters
MaximumEnumerableItems    = 10,000
MaximumNodesPerSession    = 100,000      (Proposal §23.3)
MaximumConcurrentLoads    = 4           (Proposal §23.3)
GetterWarningThreshold    = 2 seconds
ReferenceMode             = DetectCyclesOnly
EditPolicy                = ReadOnly
ShowNonPublicMembers      = false
ShowStaticMembers         = false
AutoEnumerateIEnumerable  = false
ExecuteIQueryable         = false
```

---

## 4. 任务拆解(V0.1 实施计划)

> 依赖方向铁律:`ObjectViewer.Wpf → ObjectViewer.Core`,`ObjectViewer.Core → 无 UI 依赖`。
> 每个里程碑含退出条件,未达退出条件不进入下一里程碑。

### M0 — 解决方案骨架

**交付**
- `ObjectViewer.sln` + 项目结构(按 Proposal §4):
  - `src/ObjectViewer.Core`
  - `src/ObjectViewer.Wpf`
  - `src/ObjectViewer.Demo`
  - `tests/ObjectViewer.Core.Tests`
  - `tests/ObjectViewer.TestModels`
- `net8.0-windows` TFM(Wpf/Demo);Core 为纯 `net8.0`
- NuGet:`CommunityToolkit.Mvvm` 仅限 Wpf 层;Core 零第三方依赖
- CI 可执行 `dotnet build` + `dotnet test` 通过

**退出条件**:空项目编译通过,依赖方向经编译验证(Core 无法引用 WPF)

### M1 — Core 领域模型

**交付**
- 枚举:`ObjectNodeKind`、`NodeLoadState`、`ReferenceMode`、`NodeLoadOutcome`
- 记录/类:`ObjectNode`、`ValueDisplay`、`NodeError`、`NodeLoadResult`
- 路径体系:结构化段(`Root/Member/Index/DictionaryKey/Synthetic`)+ `ObjectPath` + 编码/转义规则 + 版本化稳定路径(`ovp1:` 前缀)
- `InspectionOptions`(含 SafeDefault,§3.3 数值)+ `InspectionRequest`

**退出条件**:路径编码/解码往返单测通过(特殊字符、字符串 Key、多维索引);Options 不可变快照语义成立

### M2 — 元数据与访问器

**交付**
- `TypeMetadata`、`MemberAccessor`(Expression 编译 Getter/Setter,失败回退 `GetValue/SetValue`)
- `TypeMetadataCache`(ConcurrentDictionary;仅存类型结构不存实例)
- 成员发现:公共实例 Property+Field;跳过索引器/方法/事件/编译器生成后备字段;`[Browsable(false)]` 隐藏;`[DisplayName]`/`[Display]` 名称与顺序
- `ReadSafely`:Getter 异常隔离 + `TargetInvocationException` 解包 + 慢调用计时
- 敏感策略:`DefaultSensitiveDataPolicy`(名称列表 + `[ObjectViewerSensitive]`)+ `ObjectViewerSensitive/Editable/Ignore` Attribute 定义

**退出条件**:成员发现规则单测全绿;异常解包正确;敏感判定覆盖默认名称列表

### M3 — Adapter 体系与格式化

**交付**
- `IObjectAdapter` + 上下文/描述符类型 + `ObjectAdapterRegistry`(优先级+名称稳定排序+兜底+CanHandle 异常隔离)
- Adapter 实现顺序:Null → Sensitive → Primitive → String → Enum → ByteArray → Dictionary → List/Array(含多维) → DefaultObject
- `IObjectValueFormatter` + `FormatterRegistry` + 默认格式化实现(基元/数值多进制/string 转义/DateTime Kind/byte[] Hex/enum 名+值/对象 `{TypeName}`/集合 `Count = n` 不枚举)
- `ValueDisplay` 生产链:敏感判定 → 脱敏 → 截断标记

**退出条件**:Adapter 优先级确定性测试(含 string 先于 IEnumerable、Dictionary 先于 Enumerable 的隐患验证);格式化异常不外泄;敏感值仅存在于 Masked Display

### M4 — 会话与对象图探索

**交付**
- `InspectionSession`:SessionId、Generation、SessionToken、并发门(SemaphoreSlim)、资源登记表、`IAsyncDisposable`
- `ObjectIdentityMap`:`ReferenceEqualityComparer`(`RuntimeHelpers.GetHashCode`);祖先链循环检测 + 共享引用首次出现表
- `ObjectGraphExplorer.CreateRoot` + `LoadChildrenAsync`:代次校验、取消、节点预算、Truncated 节点、集合分页游标(`CollectionCursor` + LoadMore 节点)
- 全链路 `ConfigureAwait(false)`

**退出条件**:循环引用/双向引用/菱形引用测试不爆栈;12,500 项 IList 首页+LoadMore 无重复索引;代次过期结果被丢弃;取消后无回写

### M5 — WPF 表现层

**交付**
- `ObjectViewerControl`(DependencyProperty:Value/RootName/DeclaredType/Options/IsReadOnly/SelectedPath);code-behind 不做反射
- `ObjectViewerViewModel` / `ObjectNodeViewModel`(包装 Core 节点,INPC)
- `VisibleNodeCollection`:展开插入后代行、折叠移除 `IndentLevel > parent` 行、LoadMore 局部替换、复杂度与受影响子树相关
- 虚拟化 DataGrid:三列、Name 列模板(缩进+展开按钮+图标+状态)、Recycling、固定行高、无嵌套 ItemsControl
- 交互:Loading 占位、Error 节点、上下文菜单(复制值/显示路径/稳定路径/错误详情)、长字符串弹窗全文、键盘导航
- `IUiDispatcher` 抽象注入;UI 集合仅 UI 线程修改

**退出条件**:10 万逻辑节点滚动仅为视口创建容器;展开/折叠增量更新不整表刷新;选中行在后台加载完成后不跳变

### M6 — Demo 宿主与验收

**交付**
- `ObjectViewer.Demo`:侧栏可选示例对象(对应 PRD §19 DoD:普通对象、大集合 12,500+、异常 Getter、循环引用、敏感字段)+ 根对象切换
- `ObjectViewer.TestModels`:PRD §16 中 V0.1 相关数据集(深链 32+ 层、自引用/父子双向/菱形、Equals 重写对象、Getter 异常/超长字符串、0/1/100/101/10000/100000 项集合、多维数组、特殊字符 Key、敏感字段对象)
- V0.1 验收清单逐项执行并记录(见 §5)
- 内存释放验证:切换根对象后 WeakReference + GC 不存活

**退出条件**:PRD §15.1 十条验收全部通过并留档

### 里程碑依赖图

```text
M0 骨架 ──→ M1 领域模型 ──→ M2 元数据/访问器 ──→ M3 Adapter/格式化 ──→ M4 会话/图探索 ──→ M5 WPF 表现层 ──→ M6 Demo/验收
                     └──────────── TestModels 可自 M1 后并行建设,M2–M4 每步配套单测 ────────────┘
```

---

## 5. V0.1 验收标准(PRD §15.1,逐条对应)

| # | 验收项 | 验证方式 |
|---|---|---|
| A1 | 基元/字符串/枚举/普通对象/null 三列正确 | Demo + 单测 |
| A2 | 声明接口 + 运行时实现类双类型显示 | `IAnimal`/`Dog` 用例 |
| A3 | 自引用/双向引用不无限递归,Reference 节点指向正确路径 | TestModels 图 |
| A4 | Getter 抛异常仅该成员显示 Error,其余正常 | TestModels |
| A5 | 12,500 项 IList 首页加载 + 连续 LoadMore,索引不重复 | 自动化测试 |
| A6 | Dictionary Key/Value 可浏览,字符串 Key 路径转义正确 | 特殊字符 Key 用例 |
| A7 | 切换根对象旧任务取消,旧结果不写入新页面 | 并发切换测试 |
| A8 | Secret/Token/Password 默认脱敏,复制拿不到原值 | 安全测试 |
| A9 | 最大深度/节点数显示 LimitReached,不静默结束 | 32+ 层深链 |
| A10 | 关闭 Viewer 后 GC 验证不因订阅持有旧根对象 | WeakReference + GC |

**性能门槛**(NFR-001/002,基准硬件复测):根节点展示 P95 < 100ms;缓存命中展开 ≤50 成员 P95 < 200ms(不含业务 Getter 自身阻塞)。

---

## 6. 测试策略要点(V0.1)

- **单测(Core)**:Adapter 优先级确定性、双类型显示、异常解包、引用相等/循环/共享、路径编码解码、分页边界(0/1/99/100/101)、敏感值不进入 Display、Formatter 异常与截断、Generation 过期丢弃、Session 释放
- **WPF 测试**:展开/折叠行数正确、LoadMore 无重复、选中保持、UI 线程约束
- **安全测试**:敏感值在 UI/复制/日志/异常消息中无泄漏;非公共/静态默认不可见
- **性能**:BenchmarkDotNet(元数据缓存、编译 Getter vs 反射、节点创建);UI 用 Demo + Profiler 观察视口容器数与帧时间

---

## 7. 关键风险与对策(实施期重点监控)

| 风险 | 对策(V0.1 落地动作) |
|---|---|
| Getter 阻塞/副作用 | 并发门=4、警告阈值 2s、文案只承诺"停止等待" |
| DataGrid 虚拟化失效 | 扁平列表 + Recycling + 固定行高 + 禁止嵌套 ItemsControl,M5 设 10 万节点滚动门槛 |
| 循环/共享引用错误 | ReferenceEqualityComparer + 两种模式 + 专门测试图 |
| 脱敏旁路 | 敏感判定置于 ValueDisplay 生成前,Search/Copy/Snapshot 统一消费 Display |
| 异步旧结果覆盖 | SessionId + Generation + 请求版本三重校验 |
| 事件订阅泄漏 | 会话统一登记、切换根对象先建新会话再释放旧会话、WeakReference+GC 验证 |
| 缓存阻止 ALC 卸载 | V0.1 用 ConcurrentDictionary,接口预留 ConditionalWeakTable 切换点 |

---

## 8. 遗留待决策事项(不阻塞 V0.1)

| PRD # | 事项 | 建议决策时点 |
|---|---|---|
| #2 | 树表格自研 vs 第三方 | 随本确认书确认(建议自研,D-04) |
| #3 | Getter 进程隔离 | V1.0 前 |
| #5 | 深度搜索首发 | V0.2 设计时(建议 V0.2) |
| #6 | 敏感原值受控查看 | V0.2+ 授权机制设计时 |
| #7 | 快照持久化格式/加密/保留期 | V0.2 |
| #8 | 插件签名/ALC | V1.0 |
| #9 | 审计事件归属 | V1.0 |

---

## 9. 实施进度记录(2026-11-12 初次构建)

### 9.1 交付状态(V0.1 基础框架)

| 里程碑 | 状态 | 说明 |
|---|---|---|
| M0 解决方案骨架 | ✅ | `ObjectViewer.sln` + Core(net8.0,零依赖)/ Wpf(net8.0-windows)/ Demo / TestModels / Core.Tests,依赖方向经编译验证 |
| M1 领域模型 | ✅ | ObjectNode、双轴 Kind、结构化路径(版本化 ovp1 稳定路径)、InspectionOptions(PRD §12 默认值)、NodeError |
| M2 元数据与成员发现 | ✅ | Expression 编译 Getter/Setter + 反射回退、TypeMetadataCache、DefaultMemberPolicy、DefaultSensitiveDataPolicy(PRD 8 名称 + 特性) |
| M3 Adapter 与格式化 | ✅ | Registry(优先级+名称稳定排序+兜底)+ 9 个 V0.1 Adapter + FormatterRegistry(Default/Numeric/ByteArray) |
| M4 会话与图探索 | ✅ | InspectionSession(并发门/取消/资源登记/代次)、ObjectGraphExplorer(懒加载/分页/LoadMore/循环检测/错误隔离/预算限制) |
| V0.1 单元测试 | ✅ | 45 个测试全绿:路径、成员、脱敏、错误隔离、分页边界、循环/共享引用、会话限制、格式化 |
| M5 WPF 表现层 | ✅ | ObjectViewerControl + ViewModel + 扁平可见投影 + 虚拟化 DataGrid(Recycling/固定行高)+ 上下文菜单 + Enter/双击展开 + Ctrl+C |
| M6 Demo 宿主 | ✅ | 10 个 PRD §16 场景(Dog 循环/12500 项/异常 Getter/敏感字段/特殊 Key 字典/40 层深链/多维数组/byte[]/共享引用/null 根) |

构建验证:`dotnet build` 0 警告 0 错误;`dotnet test` 45/45 通过。

### 9.2 实施期补充决策

| # | 决策 | 说明 |
|---|---|---|
| D-15 | **节点类型双轴设计**:PRD FR-013 的单一枚举列表以"角色(ObjectNodeKind)× 值性质(ObjectValueKind)"两轴覆盖 | 角色轴:Root/Property/Field/CollectionItem/DictionaryEntry/LoadMore/Loading/Error/Truncated/Reference;性质轴:Null/Primitive/String/Enum/ByteArray/Object/Collection/Dictionary/Enumerable/Task/Exception/Dynamic/Reference。两轴并集恰好覆盖 FR-013 列表,且图标/行为判定更清晰 |
| D-16 | **字典分页 V0.1 采用一次性物化**(上限 `MaximumEnumerableItems`) | Proposal §13.3 的连续 Enumerator 会话保留给 V0.2 未知 `IEnumerable`;字典 Count 已知,显式展开时一次物化更简单且避免分页间枚举器失步 |
| D-17 | **Session 取消令牌保持 cancelled 不 Dispose** | Dispose 后仍可能有读者访问 `SessionToken`;已取消且无等待句柄的 CTS 不持有非托管资源 |
| D-18 | **byte[] 允许显式分页展开**(每项 `0xXX 'M'` 预设显示) | PRD "不作为普通集合自动展开"指父级展开时不自动倾倒字节行;懒加载保证只有用户显式展开 byte[] 节点才分页加载 |
| D-19 | **VM 通过 `Task.Run` 派发 Core 调用** | 同步 Getter 可能阻塞;派发职责收敛在 ViewModel 一处,Core 保持纯 async 语义(Proposal §24 线程模型) |

### 9.3 待办(进入 V0.2 前需要补齐的 V0.1 项)

- WPF 层自动化测试(展开/折叠行数、LoadMore 不重复、选中保持)— 当前以 Core 测试 + Demo 人工验收覆盖
- 内存释放验证(WeakReference + GC,验收项 A10)
- 性能基准(BenchmarkDotNet + 10 万节点滚动)
- CI 管道(GitHub Actions / Azure DevOps)

---

## 10. 评审确认

- [ ] 范围界定(§3)符合预期
- [ ] 任务拆解与里程碑顺序(§4)可接受
- [ ] 验收标准(§5)作为 V0.1 完成定义
- [ ] 建议决策 D-04~D-10(§1.1)认可或修改
- [ ] 确认后进入 M0 开发

| 角色 | 姓名 | 日期 | 结论 |
|---|---|---|---|
| 需求方 | | | |
| 实施 | Devin | | |
