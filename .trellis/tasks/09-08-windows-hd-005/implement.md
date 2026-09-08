# HD-005 实施计划

## 启动条件与授权

- [ ] 父任务实施获用户批准，本任务由 `task.py start` 进入 `in_progress`；当前规划不能启动 spike。
- [ ] HD-004（`.trellis/tasks/09-08-windows-hd-004`）提供可用、脱敏、明确 synthetic/real 的 terminal frame 证据。
- [ ] 记录实际 .NET SDK、Windows App SDK、WebView2、xterm 与 native 候选版本；依赖新增另经批准并锁定。
- [ ] 只使用 disposable fixture/pane；不连接用户正在运行的训练或 agent。

## 顺序清单

- [ ] 建立候选无关的 spike host、adapter port、epoch/seq gate 与有界队列。
- [ ] 建立跨块 UTF-8、full/delta、seq gap、超长行、恶意 ANSI 和慢消费 fixture。
- [ ] 实现 WebView2/xterm 候选的本地资源、固定 origin、message allowlist 和解析消费回执。
- [ ] 实现最小 native 候选；若必须启动 ConPTY 或绕开输入来源策略，立即记录 FAIL 并停止扩张。
- [ ] 加入 read-only、focus、composition、commit、paste、resize、selection 与 renderer reset 路径。
- [ ] 先跑 L1 fixture/contract；任何字节、epoch 或无界队列失败先修复再进入真机。
- [ ] 在隔离 Windows 11 桌面跑 L2 renderer 生命周期和资源回收。
- [ ] 人工跑 L3 微软拼音、100/150/200% DPI、跨显示器、高对比和屏幕阅读器场景。
- [ ] 汇总原始数据、环境、失败与 UNVERIFIED 项，完成 renderer decision；不修改 AC 状态。

## 拟建测试与用例

- `tests/Unit/HerdDesk.Terminal.Web.Tests/Spike/Utf8ChunkingTests.cs`：汉字/emoji/组合音标跨块、无 U+FFFD/重复。
- `tests/Unit/HerdDesk.Terminal.Web.Tests/Spike/EpochSequenceTests.cs`：旧 epoch 回执、gap、回退、full reset。
- `tests/Unit/HerdDesk.Terminal.Web.Tests/Spike/WebMessageBoundaryTests.cs`：未知 type、超长载荷、错 epoch、伪造 target。
- `tests/Unit/HerdDesk.Terminal.Web.Tests/Spike/BackpressureTests.cs`：slow consumer、队列上限、取消与 reset。
- `tests/Integration.Windows/RendererSpike/ImeScenarios.md`：预编辑、空格/回车选词、切换中英文、候选窗。
- `tests/Integration.Windows/RendererSpike/FocusAndDpiScenarios.md`：点击、跳转、跨屏、主题和无障碍。

## 验证命令与证据层级

- [ ] `[现有] rtk proxy just ci`：保护当前 G0；只能证明现有离线门，不能证明 spike。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.Terminal.Web.Tests/HerdDesk.Terminal.Web.Tests.csproj -c Release --filter Spike`：L1 字节与消息契约。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter RendererSpike`：L2；需交互的用例应明确 skipped 而非假通过。
- [ ] `[拟建] npm --prefix spikes/web/terminal test` 与 `npm --prefix spikes/web/terminal run build`：Web renderer 单测与离线 bundle。
- [ ] L3 人工记录 OS/IME/DPI/GPU/WebView2/renderer 版本、步骤、实际结果和脱敏截图。

## 回滚与结束门

- [ ] 删除/禁用失败候选不会改变 Core、用户 pane 或 herdr daemon。
- [ ] renderer fault 只销毁 spike 视图和自有子进程，不 stop server、不杀 agent。
- [ ] 决策缺少 L3 证据时保持 blocked/UNVERIFIED；不把 source review 写成 AC09 passed。
- [ ] 独立审查确认 HD-014/HD-015 可直接采用结论后再归档本任务。
