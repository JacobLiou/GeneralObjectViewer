# ObjectViewer — 项目要点

## 构建 / 测试
- 全解决方案: `dotnet build ObjectViewer.sln` / `dotnet test ObjectViewer.sln`(xUnit;Core.Tests + Extensions.Tests)
- Demo: `src/ObjectViewer.Demo`(net8.0-windows, WPF)

## 编辑(可写)能力 — 关键链路
- 开关: `InspectionOptions.EditPolicy`(`ReadOnly` 默认 / `PublicSetters` / `ExplicitlyAllowedMembers`([ObjectViewerEditable] 白名单) / `FullTrust`)
- 门控: `DefaultMemberPolicy.IsEditAllowed`(敏感成员任何模式下都不可编辑;PublicSetters 校验 setter 本身可见性,表达式树 setter 可调私有 setter)
- 节点: `ObjectNode.CanEdit` 由 `CreateChildNode` 防御性计算;写前 `ObjectGraphExplorer.WriteAsync` 再次校验
- 转换: `Editing/EditValueParser`(null 关键字 / Nullable / 枚举 / 数值按 Culture / DateTime / Guid / TypeConverter;自定义 `IEditValueConverter` 经 `InspectionRequest.CustomEditConverters` 注册,优先于内置)
- 写入: 校验 → 转换 → Setter(异常只落在该节点, FR-124)→ 重读 + `ApplyDescription` → 有子节点则失效待重载;审计事件不记录原值(FR-126)
- UI: 双击/F2/右键 → Popup 内联编辑(`ObjectViewerControl`);可编辑值带 ✎ 标记;Enter 确认、Esc 取消、失败红字保持弹层
