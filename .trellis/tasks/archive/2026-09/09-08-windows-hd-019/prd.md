# HD-019 · 本地 MVP 综合验收

## 目标与事实

对完整本地 Windows 路径做端到端验收：app、renderer、CLI transport、control lease、
恢复、导航/通知和 workspace/agent 操作。它依赖 HD-012、015、017、018：
`tasks/HD-019.md:11-24`；当前仓库没有 App/Infrastructure/renderer，现有 smoke
明确不测 Windows 或 daemon：`tests/HerdDesk.Core.SmokeTests/Program.cs:6-7`。

## 需求

- R1：在可丢弃本地 pane，覆盖 PowerShell 与三类 agent 文本 TUI 的 observe、
  control、中文 IME、输入、resize、滚动、release 与 GUI 关闭。
- R2：记录实际 agent/version/renderer/runtime/Windows/WebView2/DPI，图形 agent
  必须单独列为支持矩阵，不能借文本 TUI 通过。
- R3：验证一次输入仅一次到达、双 observer/控制者互斥、未授权 takeover 拒绝、
  断开后无输入重放、GUI 不杀 daemon/agent。
- R4：对本地 renderer restart、bridge EOF、daemon exit、pane exit 注入，
  验证状态和恢复路径不同且可解释；真实 SSH 断网由 HD-026 补齐。

## 原 AC 映射

- AC06/07/10/15-C1：本地端到端场景和证据归档。
- AC06（输入/resize/release）、AC07（互斥）、AC10（文本 TUI）由 HD-019 汇总。
- AC13/14/15 在本任务只提交 P2 本地恢复、无重放和进程所有权贡献；HD-026 补齐真实
  SSH 网络与 SSH 子进程证据后最终汇总。HD-019 不以完整 AC13/14/15 通过为完成条件，
  也不等待 P3，避免 HD-020 依赖本任务产生逆向循环。

## 边界和撤销

不得在生产/训练 pane 自动输入或 takeover。任一场景失败则标明 agent/mode 不支持，
停用写入或退回 observe；不为通过而忽略失败注入。
