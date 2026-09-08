# HD-005 设计

## 决策状态

原 plan 已定：WinUI 原生壳、可替换 `ITerminalRenderer`、WebView2/xterm 交付基线、本地资源、无 CDN（`docs/plan/docs/04_技术选型与ADR.md` 第 9–19、25–27 行）。待 spike：native adapter 是否能在不自建 ConPTY 的前提下满足同一门禁，以及两种 adapter 的真实 IME/资源表现。

## 拟建产物

- `spikes/HerdDesk.RendererSpike/`：拟建 WinUI 小宿主，只负责选择 fixture、挂载候选和展示测量值。
- `spikes/HerdDesk.RendererSpike.Web/`：拟建 WebView2 host adapter。
- `spikes/HerdDesk.RendererSpike.Native/`：拟建 native raw-stream adapter；允许失败或最终删除。
- `spikes/web/terminal/`：拟建本地 xterm bundle 与固定消息 schema。
- `tests/Unit/HerdDesk.Terminal.Web.Tests/Spike/`：拟建字节、epoch、消费回执和消息校验测试。
- `tests/Integration.Windows/RendererSpike/`：拟建 Windows focus、IME、DPI 与 renderer 崩溃测试，归统一 Windows integration project。
- `docs/spikes/renderer-decision.md`：拟建最终决策报告；是本任务唯一必须保留的产品规划产物。

这些路径均未存在，不作为当前实现引用。

## 最小宿主组件树

```text
RendererSpikeWindow
├─ CandidatePicker (WebView2 / Native)
├─ FixturePicker + Play/Pause/Reset
├─ RendererViewport
│  ├─ WebTerminalSurface 或 NativeTerminalSurface
│  └─ Focus/IME anchor overlay
└─ EvidencePanel
   ├─ epoch/seq/queue bytes/consumption
   ├─ input-origin trace（只记录类别与长度）
   └─ PASS/FAIL/UNVERIFIED checklist
```

## 状态与事件

- `SpikeHostState = Initializing | Ready | Playing | Backpressured | Resetting | Failed | Disposed`。
- `RendererCandidateState = Unavailable | Loading | Ready | Focused | ReadOnly | Faulted`；Unavailable 必须显示缺失原因。
- `CompositionState = None | Composing | Committing`；只有 `Committing` 产生 `CommittedText`。
- 输入事件为 `UserKey`、`CommittedText`、`ExplicitPaste`、`EmulatorReply`；与已编译 `InputOrigin` 对齐（`src/HerdDesk.Contracts/TerminalModels.cs:9-18`）。
- frame 使用测试 epoch 包装已编译 `TerminalFrame`；当前编译类型没有 epoch，因此 spike host 保存 epoch，不能伪称已实现草案接口（`src/HerdDesk.Contracts/CLAUDE.md:30-44`）。

## 数据流

1. fixture reader 验证 NDJSON 行和大小，按原始块边界产生 frame。
2. host 校验当前 epoch 与严格递增 seq，将 bytes 送入有界队列。
3. Web 候选以 `PostWebMessageAsJson` 发送 metadata 与二进制编码载荷；native 候选以同一 adapter port 接受 bytes。
4. renderer 返回“解析消费”回执，host 释放在途配额；回执不记作屏幕呈现时刻（`docs/plan/docs/06_终端与输入法.md:9-24`）。
5. seq 缺口、epoch 错配、崩溃或消息 schema 错误进入 `Faulted`，清空局部基线并要求 full frame 重建。

## 输入、焦点与 IME

- XAML/WebView2 focus 转移由宿主显式请求；renderer 未 Ready 时不聚焦、不发送。
- composition 期间 Ctrl+K/Enter/Esc 交给 IME；commit 事件只编码一次 UTF-8。
- 候选窗 anchor 从 renderer 当前 cell 与 DPI transform 计算；跨屏移动后重新计算，不循环 resize。
- 观察模式设置 renderer read-only，并在 host 再次拒绝输入；不得只靠 JS UI disabled。
- 自动终端回复单独标为 `EmulatorReply`，默认不出宿主（`docs/plan/docs/06_终端与输入法.md:28-32`）。

## 安全与隐私

- WebView2 固定本地 origin，关闭导航、新窗口、下载、host object、dialog 和发布 devtools；消息按 type/version/epoch/长度 allowlist。
- ANSI、标题和 web message 均视为不可信；禁止 innerHTML、任意 URI、process/file proxy（`docs/plan/docs/08_安全与威胁模型.md:3-18,26-32`）。
- spike trace 只存 fixture ID、输入类别、字节长度、时延和错误码，不存真实输入/ANSI 正文。

## 决策方法

- 正确性与安全是硬门；任一硬门失败的候选不得晋级。
- IME、keyboard、selection、accessibility、DPI 只接受 L3 交互桌面证据。
- 性能比较记录同机版本与原始数据；差异不足以抵消正确性失败。
- native 未通过时，结论为继续 WebView2，并保留 `ITerminalRenderer` 替换口；这不是 spike 失败。
