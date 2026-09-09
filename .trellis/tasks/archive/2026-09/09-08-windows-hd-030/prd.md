# HD-030 文件与图片投入 agent

## 目标

为当前 agent pane 提供“粘贴文字、输入文件路径、发送图像附件”三种明确意图，按 agent+平台+版本展示已验证能力，并以不可变目标租约完成上传和路径输入；任何流程都不自动提交 prompt 或声称 agent 已接收附件。

## 当前事实与边界

- 三种意图必须区分；图片落成临时文件后输入路径只表示“路径已输入”（`docs/plan/docs/07_多设备SSH与文件.md:43-49`）。
- 本任务来源 `tasks/HD-030.md:5-13`，依赖 HD-017 agent/resource 模型与 HD-029 文件 UI。
- terminal 输入仍需完整 pane/epoch/控制 lease；当前 `InputPolicy` 已拒绝错 pane、旧 epoch 和未验证控制（`src/HerdDesk.Core/InputPolicy.cs:13-31`）。
- 传输完整性/安全最终由 HD-028/HD-032，剪贴板读取和 cache 策略由 HD-031。

## 需求

- R1：UI 始终显示意图类型、源、目标 Device→Session→Workspace→Pane、agent profile 和能力证据状态。
- R2：能力状态为 `Verified | Unsupported | Unknown`，按 agent exact version + platform + renderer/terminal path 识别；Unknown 不自动选择成功路径。
- R3：选择/拖入源后冻结 `AttachmentDraft`；确认时冻结目标 `PaneKey + ConnectionEpoch + capability profile + control lease`。
- R4：目标切换、重连、lease 撤销或 agent version 变化使 draft stale，不能自动重定向到新 pane。
- R5：路径输入流程区分 selected/uploading/uploaded/path-ready/path-inserted/agent-unconfirmed；不把 preview/upload 当 agent 接收。
- R6：图片需要远端文件时使用 HD-029/HD-028 的私有临时目标、hash/atomic rename 和 cache lease。
- R7：文本/路径通过 `ExplicitPaste` 或 `CommittedText` 的受控输入路径，不自动附加回车、空格或确认键。
- R8：direct clipboard/image attachment 只有 capability 为 Verified 时可用；否则使用路径或明确手动说明。
- R9：覆盖 loading、empty、unsupported、unknown、offline、permission denied、upload failed、cancelled、expired、disabled 和 completed-unconfirmed。
- R10：预览、诊断和缓存不记录/导出文件正文；文件名与远端路径视为不可信文本。

## 子任务验收

- [ ] AC1（R1, R2）：每个 agent+平台组合显示 capability 证据版本/日期；缺证据为 Unknown，不显示“支持附件”。
- [ ] AC2（R5, R6）：本地/远端图片完成 upload/hash/rename 后只显示“可输入路径”，插入后显示“路径已输入，尚未确认 agent 接收”。
- [ ] AC3（R7）：上传或路径插入从不自动发送 Enter；bytes trace 证明 0 个额外提交键。
- [ ] AC4（R3, R4）：切换设备/pane、重连或 lease revoke 100 次，stale draft 不向新/旧错误目标上传或输入。
- [ ] AC5（R6）：取消只释放本 draft/job/cache lease，不清理其他 job 或仍被引用对象。
- [ ] AC6（R2, R8）：Unknown/Unsupported 提供“复制路径/手动确认”降级，不绕过 agent 审批或限制。
- [ ] AC7（R1, R9）：文件/图像/文字意图通过键盘、screen reader、拖放均可辨识；状态不只靠图标。
- [ ] AC8（R5, R9, R10）：offline/permission/upload/path-input 失败分开显示，结果未知时不写完成，诊断不泄漏正文。

## 与产品 AC 的映射

- AC35（`planning/acceptance.json:277-282`）：本任务贡献 capability、target lease、上传与不自动提交；攻击/故障综合最终归 HD-032。
- AC31/AC32：消费 HD-029/HD-028 的 job 投影并回归目标/取消，最终归 HD-032。
- AC36：贡献意图模型，剪贴板与 cache 最终归 HD-031。
- AC14/AC16：贡献旧 input 不重放和明确目标授权；AC14 完整产品最终归 HD-026，AC16 按父 acceptance map 汇总。

## 非目标

- 不宣称所有 agent 支持图片，不解析 terminal 输出判断附件是否被模型接收。
- 不实现通用文件管理、自动 prompt 构造、自动 Enter、agent bypass 或任意命令执行。
- 不在本任务持续监控系统剪贴板或保存无租约的图片缓存。
