# HD-015 设计

## 拟建文件责任

- `src/HerdDesk.Terminal.Web/Input/TerminalInputController.cs`：拟建 host 侧输入事件编排与 epoch 绑定。
- `src/HerdDesk.Terminal.Web/Input/ImeCompositionBridge.cs`：拟建 composition 生命周期和 cursor anchor。
- `src/HerdDesk.Terminal.Web/Input/KeySequenceTranslator.cs`：拟建已验证 key→bytes 转换。
- `src/HerdDesk.Terminal.Web/Input/SelectionAndMousePolicy.cs`：拟建本地选择、mouse mode、scroll 区分。
- `src/HerdDesk.App/Controls/TerminalFocusCoordinator.cs`：拟建搜索/通知/切 pane 后 focus 恢复。
- `src/HerdDesk.App/ViewModels/TerminalInputViewModel.cs`：拟建 read-only、composition、focus 与可解释拒绝状态。
- `web/terminal/src/input.ts`、`ime.ts`、`selection.ts`：拟建 Web 侧事件采集，不做权限判断。
- `tests/Integration.Windows/Input/`：拟建真机场景、版本矩阵与证据模板。

## 输入状态机

- `Unfocused → FocusRequested → Focused`；renderer 未 Ready 时停在 requested，可取消。
- `Focused → Composing → Committing → Focused`；commit 创建唯一 token，host 只接受一次。
- 任意输入态遇 epoch change/reset/offline → Suspended；清除 preedit、pending token 和 key state，不重放。
- `ReadOnly` 是与 focus 正交的状态；可选择/复制，但 user key/paste/mouse/scroll 写操作被 host 拒绝。

## 事件分类和命令

- Web 侧只发 `compositionStart/update/end`、`beforeInput/input`、`keyDown/up`、`pasteIntent`、`selectionChanged`、`mouseIntent`、`focusChanged`。
- host 归并浏览器差异后只产生 `UserKey`、`CommittedText`、`ExplicitPaste`、`EmulatorReply`。
- 每个事件由 binding 注入当前 `PaneKey/Epoch`，再交给 HD-016 lease + `InputPolicy`；JS 自报 identity 不被使用。
- `RequestFocus`、`RequestControl`、`ReleaseControl`、`CopySelection`、`PasteText`、`ScrollViewport` 是 UI 命令；后两项仍需来源和权限 gate。
- `RequestTerminalResize` 只接受 HD-014 计算的有限 cols/rows，在 HD-016 lease/capability 校验后去抖发送；observe/unknown/old epoch 只保留本地 layout。

## IME 规则

- preedit 字符串只存在 WebView/XAML 进程内，不进 Core、日志或缓存。
- compositionend 与 input 可能重复表达 commit，使用浏览器事件序列 + 单次 commit token 去重；不能靠文本内容去重。
- candidate anchor = terminal viewport origin + cursor cell rect，经 WebView rasterization scale 和 XAML transform 转屏幕坐标。
- Enter/Esc/space 在 composition active 时不进入 key translator；Ctrl+K 不打开搜索。
- renderer reset/切 pane 时取消 preedit 并给用户无敏感内容的提示，不把半成品发到旧/新 pane。
- font/zoom/DPI 改变时先更新 local cell transform/candidate anchor；control resize ack/后续 frame 再更新 server viewport，禁止 resize feedback loop。

## 键盘、鼠标与选择

- printable text 优先走 committed text；control/navigation keys 由验证 profile 输出 bytes。
- `AgentInputProfile` 按 agent name + exact version + shell + renderer version 标识；未知 profile 使用 terminal 基础键，不猜 Shift+Enter。
- Ctrl+C 无选择时发送中断；Ctrl+Shift+C 复制选择；有选择时 Ctrl+C 行为保持明确设置且默认仍保留 SIGINT。
- control 下滚轮是否发 `terminal.scroll` 由 mode probe/profile 决定；observe 下只显示说明，不发送。
- OSC 52 读拒绝、OSC 8 链接由 HD-014 host policy；本任务不绕开。

## UI 状态与可访问性

- Terminal status strip 显示“观察/申请控制/控制/输入暂停/连接过期”，文本与图标并用。
- composition/focus fault 提供 retry focus，不自动 takeover。
- selection toolbar 可由键盘打开，提供复制与清除选择；按钮带 AutomationName。
- screen reader 路径能读 access 状态并调用 Request/Release；terminal 内容朗读能力依赖 renderer，未真机测保持 UNVERIFIED。

## 错误与诊断

- 稳定错误类别：`renderer_not_ready`、`composition_active`、`wrong_pane`、`stale_epoch`、`control_not_verified`、`input_bytes_limit`、`unsupported_key_profile`。
- 被拒输入只记录 origin/length/reason/脱敏 key，不记录文本 bytes。
- 无法确认 commit 是否已发时显示结果未知，不自动重发。
- focus/IME 错误不关闭 terminal 或 pane；renderer fault 走 HD-014 reset。
