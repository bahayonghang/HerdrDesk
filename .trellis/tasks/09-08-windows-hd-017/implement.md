# HD-017 实施计划

## 开始前

- [ ] 用户批准实施且任务已 start。
- [ ] HD-008/HD-009 的真实 schema fixture 已证明每个 operation 和 required 字段；不从旧文档猜 RPC。
- [ ] HD-016（`.trellis/tasks/09-08-windows-hd-016`）提供 pane lease/input policy，但不被扩成 CRUD 权限系统。
- [ ] 准备 disposable session/workspace/pane；close 测试不得针对用户真实任务。

## 顺序清单

- [ ] 从 schema 证据建立 verified operation capability，标出 unsupported/unknown。
- [ ] 定义最小领域 intent/result/operation state，无 dynamic RPC/argv。
- [ ] 实现 Coordinator freshness/capability/confirmation/one-send gate。
- [ ] 实现 adapter 到实际 RPC 的显式映射和错误分类。
- [ ] 实现 timeout→read-only query→observed/absent/unknown 收敛。
- [ ] 实现 NewResource/Rename/Close dialogs 的全状态与键盘/辅助功能。
- [ ] 实现普通 shell 与 agent 的明确分流、working directory 和 verified options。
- [ ] 接入 HD-011 选择/聚焦，但成功只以 Store 后置条件为准。
- [ ] 跑 L1 fake RPC/Store/timeout/old epoch/重复点击测试。
- [ ] 跑 L2 真实 Windows + disposable herdr 的创建/重命名/关闭与 GUI 退出。
- [ ] L3 人工复核目标可见、确认、permission/unknown outcome 和 screen reader。

## 拟建测试路径

- `tests/Unit/HerdDesk.Core.Tests/Commands/ResourceCommandGateTests.cs`：capability、freshness、confirmation。
- `tests/Unit/HerdDesk.Core.Tests/Commands/UnknownOutcomeTests.cs`：timeout、查询、late response、不盲重试。
- `tests/Contract/ResourceCommands/ResourceCommandSchemaTests.cs`：实际 schema 字段映射。
- `tests/Unit/HerdDesk.App.Tests/Commands/ResourceCommandViewModelTests.cs`：表单/disabled/error/cancel。
- `tests/Integration.Windows/Components/ResourceCommands/DialogStateTests.cs`：shell/agent/working dir/close confirmation。
- `tests/Integration.Windows/Resources/CrudScenarios.md`：disposable 资源的 create/rename/close。

## 命令和证据

- [ ] `[现有] rtk proxy just ci`：当前 G0 gate；不证明未来 mutation。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj -c Release --filter ResourceCommand`：L1。
- [ ] `[拟建] dotnet test tests/Contract/HerdDesk.ContractTests.csproj -c Release --filter ResourceCommand`：schema contract。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.App.Tests/HerdDesk.App.Tests.csproj -c Release --filter ResourceCommand`：ViewModel L1。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter ResourceCrud`：L2 component/disposable target。
- [ ] L3 记录 actual schema hash、CLI/server、目标、步骤、实际 Store 后置条件和人工确认；不记录敏感路径正文。

## 回滚与结束门

- [ ] 单项 capability 可关闭并回退只读浏览；不需要回滚/停止 daemon。
- [ ] timeout 后不能确认结果时保留 UnknownOutcome，阻止重复 destructive 操作。
- [ ] 任意 generic exec/dynamic method 路径为阻断缺陷，必须移除后再验收。
- [ ] AC20 需要 schema、L1、L2 和 UI 确认证据；AC10 留 HD-019 最终汇总。
