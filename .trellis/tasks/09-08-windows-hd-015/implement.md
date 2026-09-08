# HD-015 实施计划

## 开始前

- [ ] 用户批准实施且任务已 start。
- [ ] HD-014（`.trellis/tasks/09-08-windows-hd-014`）交付稳定 web message/input source/read-only/focus contract。
- [ ] HD-016（`.trellis/tasks/09-08-windows-hd-016`）提供真实 lease/ownership，不用 renderer 状态替代。
- [ ] 准备 disposable pane 和真实交互 Windows 11；禁止向用户工作 pane 自动发键。

## 顺序清单

- [ ] 冻结 input event fixture 和 commit token 规则，覆盖浏览器事件次序差异。
- [ ] 实现 host TerminalInputController 与完整 pane/epoch/lease gate。
- [ ] 实现 Web composition bridge、candidate anchor 和 focus lifecycle。
- [ ] 实现基础 control/navigation key translator 与 unknown profile 降级。
- [ ] 实现本地选择、复制、mouse intent、observe/control scroll 分流。
- [ ] 接入 HD-011/014 font/zoom/DPI layout change，验证 observe no-resize 与 control capability resize 去抖。
- [ ] 接入 HD-011 Ctrl+K 和 HD-012 notification focus coordinator。
- [ ] 增加 read-only/paused/rejected UI 与可访问性动作。
- [ ] 用 fake renderer/transport 跑 L1 exactly-once、旧 epoch 和 focus race。
- [ ] 在 Windows 集成环境跑 L2 WebView focus、resize、reset、lease revoke。
- [ ] L3 人工跑微软拼音、agent 矩阵、DPI、跨屏、鼠标和屏幕阅读器。
- [ ] 将完整原始步骤/版本/结果提交 HD-019 综合验收，不提前标 AC10 passed。

## 拟建测试路径

- `tests/Unit/HerdDesk.Terminal.Web.Tests/Input/CompositionDedupTests.cs`：compositionend/input 双事件恰好一次。
- `tests/Unit/HerdDesk.Terminal.Web.Tests/Input/KeySequenceTranslatorTests.cs`：Ctrl/AltGr/Tab/Esc/方向键 bytes。
- `tests/Unit/HerdDesk.Terminal.Web.Tests/Input/FocusRaceTests.cs`：搜索/通知/切 pane/reset/旧 epoch。
- `tests/Unit/HerdDesk.Terminal.Web.Tests/Input/SelectionMousePolicyTests.cs`：copy、mouse mode、observe scroll。
- `web/terminal/tests/ime-events.test.ts`：preedit 不发送、commit token、取消。
- `tests/Integration.Windows/Input/MicrosoftPinyinScenarios.md`：候选、提交、快捷键和 DPI。
- `tests/Integration.Windows/Input/TerminalResizePermissionTests.cs`：font/zoom/DPI、observe 0 resize、control/revoke。
- `tests/Integration.Windows/Input/AgentTextTuiMatrix.md`：Claude Code/Codex/OpenCode exact versions。

## 命令和证据

- [ ] `[现有] rtk proxy just ci`：现有 policy 回归；不证明 WebView/IME。
- [ ] `[拟建] npm --prefix web/terminal test -- ime-events`：JS L1。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.Terminal.Web.Tests/HerdDesk.Terminal.Web.Tests.csproj -c Release --filter Input`：C# L1。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter Input`：L2；交互项不得在无桌面 runner 假过。
- [ ] L3 逐例记录 bytes trace（脱敏/fixture）、实际 terminal 行为、IME candidate 截图和人工验收者。

## 回滚与结束门

- [ ] 未验证快捷键/profile 可按 agent/version 禁用，回退 terminal 基础键。
- [ ] IME 恰好一次失败时阻断控制输入，不降级为直接 keydown 文本拼接。
- [ ] 切换/断线清空 preedit/pending input；不重放、不自动 release 用户 pane。
- [ ] AC08/AC09 必须由完整 L1+L2+L3 证据汇总；AC10 留 HD-019 最终判定。
