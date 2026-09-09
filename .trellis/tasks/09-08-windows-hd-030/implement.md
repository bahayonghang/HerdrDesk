# HD-030 实施计划

## 开始前

- [ ] 用户批准实施且任务已 start。
- [ ] HD-017 提供当前 agent kind/version/工作目标，HD-029 提供 transfer job/target lease。
- [ ] HD-015/HD-016 提供 committed text/explicit paste 和 control gate。
- [ ] 只在 disposable pane/测试文件运行真实上传/输入；不自动对用户 agent 发路径。

## 顺序清单

- [ ] 定义 capability key/evidence、draft、target lease 和 delivery state。
- [ ] 先建立 Unknown/Unsupported 降级，不填未经证实的 support bool。
- [ ] 实现 Flyout 三意图、目标 breadcrumb、source picker/drop 和全状态。
- [ ] 接入 HD-029 upload job，完成只接受 hash+rename success。
- [ ] 实现 PathReady→InsertPath 的 target/epoch/control 重新验证和无 Enter bytes。
- [ ] 实现 target change/reconnect/lease revoke 的 Stale 路径。
- [ ] 实现取消、cache lease release、result unknown 和诊断脱敏。
- [ ] 用 fake ports 跑 L1 capability/state/target race/zero-enter 测试。
- [ ] 跑 L2 Windows picker/drop/upload/input 集成。
- [ ] L3 按 agent exact version 验证 path/direct clipboard/image，未知保持 UNVERIFIED。
- [ ] 证据交 HD-032 做 AC35 故障/攻击综合验收。

## 拟建测试路径

- `tests/Unit/HerdDesk.Core.Tests/Attachments/CapabilitySelectionTests.cs`：exact version、Unknown、Unsupported。
- `tests/Unit/HerdDesk.Core.Tests/Attachments/DeliveryStateMachineTests.cs`：upload/path/insert/unconfirmed。
- `tests/Unit/HerdDesk.Core.Tests/Attachments/TargetLeaseRaceTests.cs`：100 次 switch/reconnect/revoke。
- `tests/Unit/HerdDesk.Core.Tests/Attachments/NoAutoSubmitTests.cs`：输入 bytes 无额外 Enter。
- `tests/Integration.Windows/Components/Attachments/FlyoutStatesTests.cs`：loading/offline/error/stale/accessibility。
- `tests/Integration.Windows/Attachments/AgentCapabilityMatrix.md`：真实 agent+platform+version。

## 命令和证据

- [ ] `[现有] rtk proxy just ci`：当前输入 policy 回归；不证明附件能力。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj -c Release --filter Attachment`：L1。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter Attachment`：L2 component + disposable target。
- [ ] L3 记录 agent/OS/renderer/CLI 版本、delivery method、actual bytes/job result 和人工观察；path inserted 不写 accepted。

## 回滚与结束门

- [ ] direct attachment 不可靠时按 capability key 禁用，保留 copy path/manual。
- [ ] target lease 或 no-auto-submit 失败时隐藏整个投入入口，不能降级为当前 focus 发送。
- [ ] 取消只清本 job/cache object，不停止 agent、daemon 或其他 transfer。
- [ ] AC35 的完整判定留 HD-032；本任务只提交可追踪贡献证据。
