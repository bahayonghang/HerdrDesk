# HD-015 输入法、键盘与选择

## 目标

在 HD-014 可交付 renderer 上完成中文 IME、键盘、焦点、鼠标/滚动和选择复制契约，并以真实 Windows 交互桌面验证输入恰好一次、目标不串写和首批 agent 文本 TUI 行为。

## 当前事实与边界

- 产品快捷键要求 composition 期间 IME 优先，Ctrl+C 保留 SIGINT，复制/粘贴用 Ctrl+Shift+C/V（`docs/plan/docs/02_产品定义与名称.md:43-49`）。
- 真实验收矩阵已定义预编辑、提交、修饰键、agent 差异和 DPI（`docs/plan/docs/06_终端与输入法.md:36-48`）。
- 本任务来源 `tasks/HD-015.md:5-13`，依赖 HD-014；控制权由 HD-016 证明，transport 由 HD-013 提供。
- 已编译 `InputPolicy` 只允许当前 pane/epoch、已验证控制权与真实用户来源（`src/HerdDesk.Core/InputPolicy.cs:13-31`）。

## 需求

- R1：IME 预编辑只更新本地 composition UI，不产生 transport bytes；commit 只编码和发送一次。
- R2：composition 期间 Ctrl+K、Enter、Esc 和全局 accelerator 让位给 IME；显式鼠标切 pane 时取消本地预编辑且不发送半成品。
- R3：键盘映射区分 committed text 与 physical/control key，保留左右 Ctrl/Alt、AltGr、Caps、Ctrl+C/Z、Tab、Esc、方向/PageUp。
- R4：输入事件绑定完整 `PaneKey + ConnectionEpoch`；focus 变化不等于控制权，失焦不自动 release。
- R5：renderer Ready 后才能聚焦；搜索/通知跳转、renderer reset、WebView focus 丢失都有确定 focus restore 或失败状态。
- R6：控制中滚轮按验证过的模式转 server scroll；观察中不改变他人 viewport，显示只读说明。
- R7：拖选默认本地文字选择；明确修饰键才进入应用鼠标模式；复制不包含 ANSI 隐藏字节。
- R8：agent key profile 只列实际版本验证过的 Shift+Enter/Tab/Esc/确认行为；Unknown 不提供虚假统一映射。
- R9：paste 以 `ExplicitPaste` 进入同一 policy，不默认附加 Enter；多行安全提示最终由 HD-031 叠加。
- R10：鼠标、键盘和屏幕阅读器都能申请/释放控制；此任务只接调用，不自行授予 lease。
- R11：font/zoom/DPI 导致尺寸变化时，observe 只更新本地显示；只有 Controlling + ControlVerified + verified resize capability 才生成 bounded `TerminalSize`。

## 子任务验收

- [ ] AC1（R1）：微软拼音多字候选预编辑期间 transport 收到 0 bytes；空格/回车选词各恰好一个 commit。
- [ ] AC2（R1, R3）：中文+英文+emoji+组合音标跨 frame/input 无 U+FFFD、重复或吞首字。
- [ ] AC3（R2）：composition 期间 Ctrl+K/Enter/Esc 不切 pane、不提前提交 prompt；结束后快捷键恢复。
- [ ] AC4（R3, R7）：Ctrl+C 到受控 terminal 为中断 bytes，Ctrl+Shift+C 复制本地选择，观察状态两者不产生错误写入。
- [ ] AC5（R4, R5）：搜索/通知跳转等待 renderer Ready 后聚焦；快速切换 100 次旧 focus/input 不串 pane/epoch。
- [ ] AC6（R8）：Claude Code、Codex、OpenCode 分别记录实际版本、shell、renderer、导航/确认/取消/输入/滚动结果。
- [ ] AC7（R6, R7, R8）：观察/控制滚动、选择、mouse reporting、alternate screen 按验证结果工作；未知模式明确降级。
- [ ] AC8（R5）：候选窗在 100/150/200% 和跨显示器靠近真实光标，无持续 resize 抖动。
- [ ] AC9（R9, R10, R11）：observe 下 font/zoom/DPI 连续变化 upstream resize=0；control 下 resize 去抖、范围有效、lease revoke 后立即停止，paste 不附加 Enter。

## 与产品 AC 的映射

- AC08（`planning/acceptance.json:61-66`）：本任务汇总 HD-005/HD-014 并最终拥有字节正确性验收。
- AC09（`planning/acceptance.json:69-74`）：本任务最终拥有中文 IME 真机验收。
- AC10（`planning/acceptance.json:77-82`）：本任务贡献三种 agent 的输入/导航矩阵；本地 MVP 综合最终归 HD-019。
- AC06/AC07：只贡献输入、resize、release UI 路径，控制 transport/互斥最终归 HD-016/HD-019。
- AC37/AC38：贡献 terminal 输入面的键盘、screen reader、DPI；最终归 HD-033。

## 非目标

- 不建立跨 agent 的 Shift+Enter 保证，不实现图形协议或原始日志滚动历史。
- 不通过 focus、WebView Ready 或 transport 存活推断 `ControlVerified`。
- 不在本任务处理剪贴板图像、文件上传或高危多行 paste 缓存。
