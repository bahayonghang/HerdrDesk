# HD-017 workspace 与 agent 基本操作

## 目标

基于实际 herdr schema 提供创建 workspace、普通 shell terminal、明确 agent、重命名和关闭操作；所有副作用显示完整目标、保留 agent 审批，并在超时后回到权威 Store 收敛。

## 当前事实与边界

- 产品 0.1 需要基础创建/重命名/关闭，破坏操作二次确认（`docs/plan/docs/02_产品定义与名称.md:21-32`）。
- App/renderer 不能持有任意 shell 执行器，所有特权操作经 capability/ownership/policy（`docs/plan/docs/03_架构与数据流.md:56-62`）。
- 本任务来源 `tasks/HD-017.md:5-13`，依赖 HD-009 Store 与 HD-016 授权；实际 RPC 方法/required 字段由 HD-008/009 schema 证据提供。
- mutation 不能因 timeout 盲重试，应查询权威状态后决定（`docs/plan/docs/07_多设备SSH与文件.md:21-25`）。

## 需求

- R1：只开放实际 schema 已验证的 CreateWorkspace、CreateTerminal、CreateAgent、Rename、Close 能力；缺失能力禁用并显示原因。
- R2：创建普通 shell 与创建 agent 是两个明确资源种类；不得把任意 command line 伪装成“shell 类型”。
- R3：创建 agent 要求从验证过的 agent kind/profile 中选择，显示目标 device/session/workspace 与 working directory。
- R4：working directory 使用 server/schema 支持的结构化字段；不在 ViewModel 拼 shell 命令或远端引号。
- R5：默认保留上游 agent 审批/安全行为；不提供全局 bypass、自动批准或隐藏高危参数。
- R6：rename/close 目标使用完整身份与当前 Store 版本；关闭 workspace/pane 显示名称、device/session 及影响并二次确认。v0.9.0 `workspace.close` 在关联 worktree workspace 仍打开时，缺 `"close_group": true` 返回 `workspace_group_close_required`；UI 必须单独确认组关闭，默认不得静默带上 `close_group`。
- R7：operation 状态覆盖 editing、validating、confirming、submitting、observing、succeeded、failed、cancelled、unknown outcome、stale target。
- R8：提交后以权威投影中出现/消失/改名为完成；RPC response 只作相关证据，不创建第二状态源。
- R9：timeout/transport loss 时进入 UnknownOutcome，先读 snapshot/实体再允许重试；不假设 request ID 幂等。
- R10：disabled、permission denied、schema incompatible、validation error、conflict、not found 分开显示且可诊断。

## 子任务验收

- [ ] AC1（R1）：用已存档 schema 逐项证明支持的方法和 required 字段；未知 protocol 下所有写按钮 disabled。
- [ ] AC2（R2, R3, R4）：创建 workspace、普通 shell、三种已验证 agent 配置的 UI 目标/working directory 清晰且不带任意 exec 字段。
- [ ] AC3（R6）：rename/close 的确认包含完整 breadcrumb，快速切换目标后旧对话框提交被拒绝。
- [ ] AC4（R8, R9）：每种操作只发一次 mutation；timeout 后查询确认，不立即重发。
- [ ] AC5（R8）：创建/重命名/关闭完成只在 Store 收敛后显示，并聚焦新/剩余目标；目标消失显示 stale。
- [ ] AC6（R7, R9）：取消编辑不发送；提交后取消若结果未知则保留 UnknownOutcome，不宣称撤销服务器操作。
- [ ] AC7（R5）：审批/bypass 默认值不被 UI 静默更改；能力未知时无相应开关。
- [ ] AC8（R7, R10）：键盘、screen reader、错误、loading、empty、offline 和 permission 状态都有可达路径。

## 与产品 AC 的映射

- AC20（`planning/acceptance.json:157-162`）：本任务最终拥有资源操作、schema 验证和 timeout 后查询验收。
- AC16：贡献破坏操作目标确认与不自动 bypass，最终控制授权归 HD-016/HD-019。
- AC10：贡献 agent 创建选择和普通 shell 覆盖；文本 TUI 综合最终归 HD-019。
- AC11/AC12：依赖 capability/收敛并做 UI 降级回归，最终归 HD-009/HD-010。

## 非目标

- 不提供通用命令执行、启动参数自由文本、daemon stop、任意 process 或任意 RPC method 输入。
- 不实现 agent detection、调度后端、workspace 排序同步或 1.x 完整启动配置。
- 不因关闭 GUI、关闭视图或切 pane 自动关闭 server 资源。
