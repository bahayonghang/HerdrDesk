# HD-014 设计

## 拟建文件责任

- `src/HerdDesk.Contracts/TerminalRendererPorts.cs`：拟建、增量落地 `ITerminalRenderer` 与 render consumption 类型。
- `src/HerdDesk.Terminal.Web/WebTerminalRenderer.cs`：拟建 adapter 生命周期和 epoch gate。
- `src/HerdDesk.Terminal.Web/RenderFlowController.cs`：拟建端到端 byte/frame 配额、ack 与取消。
- `src/HerdDesk.Terminal.Web/WebMessageValidator.cs`：拟建双向消息 schema allowlist。
- `src/HerdDesk.Terminal.Web/WebViewSecurityPolicy.cs`：拟建 origin/navigation/permission 配置。
- `src/HerdDesk.App/Controls/TerminalHost.xaml`：拟建 renderer surface、状态 overlay 与 focus handoff。
- `web/terminal/src/terminal.ts`、`protocol.ts`、`styles.css`：拟建 xterm host、消息协议与主题。
- `tests/Unit/HerdDesk.Terminal.Web.Tests/`、`tests/Integration.Windows/Terminal/`：拟建 unit/Windows 测试，Windows 用统一 integration project。

## Adapter 接口

- 增量落地草案 `BindAsync(PaneKey, ConnectionEpoch)`、`ApplyAsync(TerminalFrame)`、`ReadInputsAsync()`、`SetReadOnlyAsync()`、`FocusAsync()`（`docs/plan/contracts/HerdDesk.Contracts.cs:68-76`）。
- adapter 另消费 App 侧 `TerminalDisplayPreferences`，只负责 local font/theme/zoom；它不直接调用 transport resize。
- `ApplyAsync` 返回当前 epoch 和 last parsed seq；完成只表示 parser consumed，不是 GPU 呈现或远端执行。
- `SetReadOnlyAsync` 是展示/前端 gate；可信 host 仍按 `InputPolicy` 校验完整目标和 lease（`src/HerdDesk.Core/InputPolicy.cs:13-31`）。
- disposal 只关闭 WebView/队列，不关闭 pane、daemon 或 agent。

## 组件树

```text
TerminalHost
├─ TargetBreadcrumb + AccessBadge（来自 App/Core）
├─ WebTerminalSurface
│  └─ WebView2 fixed-local-origin → xterm
├─ StateOverlay
│  ├─ Loading / Rebuilding / Backpressured
│  ├─ Offline / Stale / Incompatible
│  └─ Fault + RetryObserve
└─ LiveStatusRegion（节流后的文字状态）
```

## 状态机

- `Uninitialized → Loading → Bound → Ready`；只有 Bound 后收到有效 full frame 才 Ready。
- `Ready ↔ Backpressured`；Backpressured 仍保留正确队列并显示状态，超过硬限额进入 Faulted。
- `Ready/Backpressured → Resetting`：seq gap、epoch change、web process failure、protocol error。
- `Resetting → Loading/Bound`：销毁旧 WebView 和 ack，等待新的 observe/full baseline。
- 任意状态 → Disposed；迟到 callback 只记录并丢弃，不能释放新 epoch 配额。

## 消息协议

- Host→Web：`initialize(version, theme, readOnly)`、`frame(epoch, seq, full, bytes)`、`focus(token)`、`dispose`。
- Web→Host：`ready(version)`、`parsed(epoch, seq, bytesConsumed)`、`input(origin, bytes)`、`resize(cols, rows, cellPx)`、`linkRequest(uri, userGesture)`、`fault(code)`。
- host 校验固定 source、schema version、kind、字段类型、epoch、单条/累计大小和速率；未知字段按协议策略拒绝或忽略，不能落到动态执行。
- pane/session/device 不由 web 自报；binding 在 host 侧闭包持有，所有 web input 重新包装为当前绑定目标。

## 背压和字节所有权

1. HD-013 transport reader 将受限 frame 交给 Core/adapter queue。
2. `RenderFlowController` 预约 frame bytes + 编码/消息副本预算后才发送。
3. xterm `write(Uint8Array, callback)` 完成后回 parsed；内部仍可能排队，因此 host 不无限调用 write。
4. parsed 回传逐层释放该 frame 的配额；取消/epoch reset 一次性使旧 token 失效。
5. 任何层无法继续时让上游读取形成背压；不得 drop delta，相关计划依据 `docs/plan/docs/06_终端与输入法.md:9-26`。

## 输入、选择与辅助功能

- HD-014 只建立输入来源、只读 gate 和事件通道；IME/key mapping 的最终语义归 HD-015。
- local scrollback=0；观察模式滚动不发 server scroll，选择文本只复制可见字符、不含 ANSI。
- terminal 不透明背景，外壳可用 Mica；高对比采用语义资源，不重写 transport bytes。
- font/zoom 变更保留 frame 指定 cols/rows，在可用 viewport 内重算 cell/glyph 和 IME anchor；只有 HD-015 根据 access/capability 生成的 resize intent 才可离开 renderer。
- state overlay 可聚焦且有 RetryObserve；Ready 后由 App 显式恢复 terminal focus。

## 安全与错误

- 拒绝 NavigationStarting/NewWindow/download/permission；只允许用户手势的 http/https 链接交给 host 再确认。
- 关闭 host objects、任意 dialogs 和发布 devtools；JS/字体随包，依赖 hash 进入发布清单。
- renderer 错误输出稳定 code、epoch、seq、queue bytes；诊断不含 ANSI/input。
- 不可信输出无法直接打开文件、切目标、创建进程或调用 RPC（`docs/plan/docs/08_安全与威胁模型.md:26-32`）。
