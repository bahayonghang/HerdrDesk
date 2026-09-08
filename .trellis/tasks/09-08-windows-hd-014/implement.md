# HD-014 实施计划

## 开始前

- [ ] 用户批准实施且任务已 start。
- [ ] HD-005 已形成可审计 decision；若仍 blocked，不跳过 renderer/IME 硬门。
- [ ] HD-007 已创建真实 App/Web 项目并锁定恢复验证过的包版本。
- [ ] HD-013（`.trellis/tasks/09-08-windows-hd-013`）提供 terminal event/lifecycle port 和受控 fixture。
- [ ] 与 HD-015/HD-016 冻结 renderer input、read-only 和 focus 接口，避免三处重复 gate。
- [ ] 首次创建 Terminal.Web/JS/测试目标时加入 `HerdDesk.slnx`、`just ci` 与 `.github/workflows/ci.yml`；资产构建未入 gate 不算可交付。

## 顺序清单

- [ ] 增量添加 renderer port；不整文件覆盖规划草案。
- [ ] 建立 protocol.ts 与 C# validator 的 shared fixture，先固定 version/kind/limits。
- [ ] 配置 WebView2 固定本地 origin 和发布安全选项。
- [ ] 构建离线 xterm bundle、Uint8Array frame path、local scrollback=0。
- [ ] 实现 RenderFlowController、parsed ack、限额、取消和 old-epoch token invalidation。
- [ ] 实现 TerminalHost 全状态 overlay、RetryObserve、read-only 与 focus handoff。
- [ ] 接入 HD-011 terminal display settings，覆盖 preview/save/restore、observe no-resize 和 cell/IME anchor 更新。
- [ ] 接入 HD-013 frame；seq/epoch/full 错误统一走 reset→observe。
- [ ] 加入输入/resize/link 的窄消息，但把 IME/key mapping 留给 HD-015。
- [ ] 运行 L1 contract/JS/component 和恶意消息测试。
- [ ] 运行 L2 Windows WebView process failure、隐藏 pane、取消与资源回收。
- [ ] 运行 L3 DPI、主题、焦点、选择和辅助功能；高输出/时延最终归 HD-033。

## 拟建测试路径

- `tests/Unit/HerdDesk.Terminal.Web.Tests/WebMessageContractTests.cs`：双向 fixture 与未知消息。
- `tests/Unit/HerdDesk.Terminal.Web.Tests/RenderFlowControllerTests.cs`：慢 ack、取消、old epoch、硬限额。
- `web/terminal/tests/utf8-chunks.test.ts`：跨块文本与 bytes 控制键。
- `web/terminal/tests/security-boundary.test.ts`：HTML/ANSI/URI/message 注入。
- `tests/Integration.Windows/Components/TerminalHostStatesTests.cs`：loading/offline/backpressure/fault/focus。
- `tests/Integration.Windows/Terminal/TerminalDisplaySettingsTests.cs`：font/size/zoom、observe no-resize、DPI/anchor。
- `tests/Integration.Windows/Terminal/WebViewLifecycleTests.cs`：process crash、重建、4 pane/hidden pane。

## 命令和证据

- [ ] `[现有] rtk proxy just ci`：现有 G0 gate，不证明 WebView2。
- [ ] `[拟建] npm --prefix web/terminal run typecheck && npm --prefix web/terminal test && npm --prefix web/terminal run build`：JS L1。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.Terminal.Web.Tests/HerdDesk.Terminal.Web.Tests.csproj -c Release`：C# L1。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter TerminalWeb`：L2。
- [ ] `[拟建] dotnet build HerdDesk.slnx -c Release`：solution/asset packaging。
- [ ] L3 记录真实 WebView2 runtime、GPU、DPI、主题、screen reader、焦点与选择步骤；callback 不用于 input-to-render 指标。

## 回滚与结束门

- [ ] 单视图 fault 只销毁其 WebView/bridge binding，重新 observe；不关闭 pane。
- [ ] 安全边界或字节正确性失败时禁用 renderer，阻断 P2，不降级为任意脚本调用。
- [ ] 资源回收失败可将可见 pane 上限降为 1 作为临时诊断，必须记录而不能宣称 AC27 passed。
- [ ] 向 HD-015 交接 input source/focus/message contract，向 HD-033 交接原始 queue/latency 指标端口。
