# ObjectViewer — 项目要点

## 构建 / 测试
- 全解决方案: `dotnet build ObjectViewer.sln` / `dotnet test ObjectViewer.sln`(xUnit;Core.Tests + Extensions.Tests)
- Demo: `src/ObjectViewer.Demo`(net8.0-windows, WPF)

## Demo(Studio 外壳)— 关键设计
- 界面: 无边框窗口(WindowChrome)+ 深色标题栏/侧栏(示例库,分组+搜索)+ 工具栏 + 文档标签(RadioButton 样式,脏状态 ●)+ 状态栏;`Theme/Theme.xaml` 全部样式
- 示例: `src/ObjectViewer.Demo/Samples/*.json`(构建复制到输出);manifest 字段: title/description/category/icon/sort/typeName/declaredTypeName/rootName/editPolicy/referenceMode/preserveReferences/collectionPageSize/data
- 加载: `SampleLibrary.Load` → JsonNode 解析 manifest → `Type.GetType`(含 Assembly.Load 兜底)→ data 反序列化为强类型实例(枚举字符串、camelCase、preserve 时 $id/$ref)
- 编辑: 每个文档独立 `ObjectViewerControl` 实例(标签切换保状态);脏跟踪 = 监听 ViewModel.StatusText 以"已写入"开头
- 保存: `SampleLibrary.SaveAsync` — 保留 manifest 元数据、只替换 data 节点 + 写 updatedAt;`SaveAsAsync` 另存副本
- 外部文件: JSON 含 manifest 字段则按示例加载,否则走 `DataSourceImportService` 只读导入(不可保存)
- 注意: 会抛异常的 setter(如 `EditableMachine.ThrowingSetter`)必须标 `[JsonIgnore]`,否则保存→重载会失败(FR-124 演示成员不参与 JSON 往返)

## 编辑(可写)能力 — 关键链路
- 开关: `InspectionOptions.EditPolicy`(`ReadOnly` 默认 / `PublicSetters` / `ExplicitlyAllowedMembers`([ObjectViewerEditable] 白名单) / `FullTrust`)
- 门控: `DefaultMemberPolicy.IsEditAllowed`(敏感成员任何模式下都不可编辑;PublicSetters 校验 setter 本身可见性,表达式树 setter 可调私有 setter)
- 节点: `ObjectNode.CanEdit` 由 `CreateChildNode` 防御性计算;写前 `ObjectGraphExplorer.WriteAsync` 再次校验
- 转换: `Editing/EditValueParser`(null 关键字 / Nullable / 枚举 / 数值按 Culture / DateTime / Guid / TypeConverter;自定义 `IEditValueConverter` 经 `InspectionRequest.CustomEditConverters` 注册,优先于内置)
- 写入: 校验 → 转换 → Setter(异常只落在该节点, FR-124)→ 重读 + `ApplyDescription` → 有子节点则失效待重载;审计事件不记录原值(FR-126)
- UI: 双击/F2/右键 → Popup 内联编辑(`ObjectViewerControl`);可编辑值带 ✎ 标记;Enter 确认、Esc 取消、失败红字保持弹层
  - 双击路径必须在 `DispatcherPriority.Background` 延迟打开: `MouseDoubleClick` 在第二次 MouseDown 时触发,若立即打开 `StaysOpen=False` 弹层,尾随的 MouseUp 会被判为外部点击导致弹层闪关(FR-122)
