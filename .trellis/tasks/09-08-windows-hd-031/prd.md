# HD-031 剪贴板与缓存策略

## 目标

仅在用户明确粘贴/投入时读取系统剪贴板，区分文字、文件列表与图像意图，拒绝终端输出驱动的 OSC 52 读取，并以有界 TTL/容量/租约管理本应用临时缓存和高风险多行粘贴确认。

## 当前事实与边界

- OSC 52 读默认禁用，写剪贴板和受控链接须走 host policy（`docs/plan/docs/06_终端与输入法.md:50-54`）。
- 三种投入意图和 agent capability 不能互相冒充（`docs/plan/docs/07_多设备SSH与文件.md:43-49`）。
- 本任务来源 `tasks/HD-031.md:5-13`，依赖 HD-030；实际 terminal input gate 归 HD-015/016，文件传输归 HD-028/029。
- terminal 输出、文件名与 agent 提示不可信，默认不记录 clipboard/file content（`docs/plan/docs/08_安全与威胁模型.md:3-18,26-32`）。

## 需求

- R1：没有后台 clipboard watcher；只在用户调用 Paste/Attach 后读取当前剪贴板快照。
- R2：解析结果为 `Text | FileList | Image | Empty | Unsupported | AccessDenied`；多格式时要求明确意图，不按隐式优先级发送。
- R3：文字 paste 显示目标 breadcrumb、行数、字符/byte 数和截断预览；任何多行 paste 默认确认，不用内容猜测“安全命令”。
- R4：paste 不自动附加 Enter；提交时冻结 `PaneKey + Epoch + ControlLease`，变化后取消并提示 stale。
- R5：OSC 52 read 永久默认拒绝；terminal 输出不能触发 clipboard read/write、文件落盘或系统 URI。
- R6：OSC 52 write 若未来支持，必须作为单独、用户可见、可撤销策略；本任务默认拒绝并记录稳定 reason。
- R7：图像/文件只交给 HD-030 draft；不把 preview/direct clipboard 成功外推为 agent 已支持附件。
- R8：缓存只位于本应用私有目录，按对象 ID、size、createdAt、expiresAt、lease count 管理；不得按通配符清理。
- R9：TTL/容量淘汰不删除 active transfer/attachment lease；达到容量先拒绝新缓存并给用户动作，不删任意源文件。
- R10：覆盖 empty、unsupported、access denied、oversize、multiline confirmation、stale target、cache full、cleanup failed、cancelled 状态。

## 子任务验收

- [ ] AC1（R1, R2）：显式 paste 对文字/文件/图像/混合/空/不支持格式输出确定意图或选择，不后台读取。
- [ ] AC2（R3, R4）：所有多行文字在发送前显示确认，Cancel 发送 0 bytes，Confirm 发送原 bytes 且 0 个额外 Enter。
- [ ] AC3（R5, R6）：OSC 52 read fixture 100% 被拒绝；恶意 ANSI 不能读取/改写剪贴板或创建缓存文件。
- [ ] AC4（R4）：切 pane/重连/lease revoke 后待粘贴快照 stale，不能发送到新旧错误目标。
- [ ] AC5（R8, R9）：TTL/容量/并发 lease 测试只清理本应用过期、未租用对象；源文件和其他 job 对象保留。
- [ ] AC6（R9, R10）：缓存满、磁盘不足、权限拒绝、cleanup failed 不显示假成功，并提供重试/打开诊断。
- [ ] AC7（R7, R10）：PastePreview dialog 通过键盘和 screen reader 明确读出目标、多行、大小与发送/取消。
- [ ] AC8（R7, R8）：诊断与导出不含 clipboard text/image/file content、完整路径或 terminal 正文。

## 与产品 AC 的映射

- AC36（`planning/acceptance.json:285-290`）：本任务最终拥有剪贴板意图区分、OSC52 默认拒绝和多行保护。
- AC35：贡献 clipboard/image intent 与能力边界，完整附件故障验收归 HD-032。
- AC14：贡献 pending paste 不跨重连重放，完整产品最终归 HD-026。
- AC32/AC34：贡献 cache lease/清理和私有目录边界，文件故障安全最终归 HD-032。

## 非目标

- 不实现全局 clipboard history、跨设备同步、秘密扫描器或基于命令内容的规则引擎。
- 不在日志/通知中存粘贴正文，不读取其他应用剪贴板直到用户明确操作。
- 不让 terminal/agent 自行授予剪贴板权限或绕开 Windows 用户权限。
