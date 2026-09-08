# HD-017 设计

## 拟建文件责任

- `src/HerdDesk.Contracts/ResourceOperationPorts.cs`：拟建受限资源 operation request/result/port；只加 schema 已证实字段。
- `src/HerdDesk.Core/Commands/ResourceCommandCoordinator.cs`：拟建 validation、capability、目标 freshness 与 outcome 收敛。
- `src/HerdDesk.Core/Commands/ResourceOperation.cs`：拟建 operation 状态、local correlation 和错误类别。
- `src/HerdDesk.Infrastructure/Rpc/ResourceCommandAdapter.cs`：拟建 verified operation→实际 RPC 映射；无 generic method。
- `src/HerdDesk.App/ViewModels/ResourceCommandViewModel.cs`：拟建编辑/确认/提交/结果状态。
- `src/HerdDesk.App/Dialogs/NewResourceDialog.xaml`：拟建 workspace/shell/agent 创建表单。
- `src/HerdDesk.App/Dialogs/RenameResourceDialog.xaml`、`CloseResourceDialog.xaml`：拟建重命名/关闭确认。
- `tests/Unit/HerdDesk.Core.Tests/Commands/`、`tests/Integration.Windows/Components/ResourceCommands/`：拟建 unit/Windows component 测试。

## 领域请求

- `CreateWorkspaceRequest(SessionKey, Name, WorkingDirectory?)`。
- `CreateTerminalRequest(PaneParentKey, WorkingDirectory?)`，固定语义为普通 shell terminal；无 command text。
- `CreateAgentRequest(PaneParentKey, VerifiedAgentKind, WorkingDirectory?, VerifiedOptions)`；options 是由 capability 暴露的封闭结构，不是 argv。
- `RenameResourceRequest(ResourceKey, ExpectedProjectionStamp, NewName)`。
- `CloseResourceRequest(ResourceKey, ExpectedProjectionStamp, ConfirmationToken, CloseGroup?)`。`CloseGroup` 仅在用户明确确认关闭整个 worktree 组时为 true；缺省 false。Adapter 把 true 映射为 schema `close_group`。收到 `workspace_group_close_required` 时进入需确认的组关闭，不得自动重发。
- 最终字段名在 HD-008/009 读取真实 schema 后调整；这里是 HerdDesk 领域意图，不宣称上游 RPC 名称。

## Command gate

1. UI 从当前选择构造 intent，不携 renderer 自报 identity。
2. Coordinator 重新读取当前 Store，校验 target 存在、epoch/freshness、capability 与操作种类。
3. create/rename 校验字段；close 验证一次性 confirmation token 与当前 target snapshot。
4. Adapter 只映射到已验证 RPC 方法并发送一次；不在 UI 或 adapter 自动重试 mutation。
5. Coordinator 等待 Store 出现期望后置条件：新增 key、名称变化或目标消失。
6. timeout/EOF 进入 UnknownOutcome，触发只读查询；确认未发生且用户再次确认后才允许新操作。

HD-016 只提供 pane lease/input policy；资源 CRUD 不借 terminal control lease 充当通用授权。close/rename 的权限来自 capability + 明确用户意图 + freshness gate。

## ViewModel 与组件状态

- `ResourcePickerState`：可用 workspace/agent kinds、capability loading、empty、incompatible、offline。
- `FormState`：dirty、field errors、target breadcrumb、working directory support、submit availability/reason。
- `OperationState`：`Idle | Validating | AwaitingConfirmation | Submitting | Observing | Succeeded | Failed | Cancelled | UnknownOutcome | StaleTarget`。
- `NewResourceDialog` 先选 Workspace / Shell / Agent；Shell 不显示 agent profile，Agent 必须选明确 kind。
- target breadcrumb 固定在 dialog header；选择变化后旧 dialog 标 stale，需要重新打开，不静默更新目标。
- Close dialog 列出关闭对象和已知子项影响；不提供“不要再询问”的全局危险选项。

## 命令

- `OpenCreateDialog(kind,parent)`、`ValidateDraft`、`SubmitCreate`、`OpenRename`、`SubmitRename`、`OpenClose`、`ConfirmClose`。
- `CancelDraft` 只关本地编辑；`CancelPendingWait` 停止等待并进入 UnknownOutcome，不宣称远端 mutation 已取消。
- `RefreshOutcome` 发只读查询；`RetryAfterVerifiedAbsent` 只有已证实未发生且用户再次提交才可用。
- 成功后由 HD-011 router 选择新 key；如果 key 无法从实际 schema 确定，则等待 Store match 并要求唯一结果。

## 错误、安全与诊断

- 映射为 validation/capability/permission/conflict/not-found/timeout/transport/protocol/unknown，不把所有问题叫 RPC error。
- 诊断记录 operation kind、脱敏完整 identity、epoch、duration、outcome、query-after-timeout；不记录工作目录全文或 agent prompt。
- schema unknown/incompatible 时只保留浏览与诊断；按钮 disabled reason 可由 screen reader 读取。
- App 不拥有 shell executor，Infrastructure adapter 不接受动态 method/argv，符合 `docs/plan/docs/08_安全与威胁模型.md:34-38`。

## 并发与取消

- 同一资源只允许一个 destructive operation；不同资源可并行但都在各 DeviceSession actor 串行应用 Store mutation。
- 选择切换不取消已发 mutation，只取消 UI 路由等待；结果仍按原完整 key 收敛。
- late response 不覆盖新 epoch，UnknownOutcome 的查询也必须绑定原设备/session。
