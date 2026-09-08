# HD-022 实施计划

## 启动门

- [ ] `implementation/status.json` 的权威 `phase_gate` 已由 G0 验收流程改为 `passed`，待实施 HEAD 的 `just ci` 通过，HD-020/021 及相关 HD-008/010/013 完成，并收到用户对最新计划的明确实施批准后，才可 `task.py start`。
- [ ] 启动前核对实际 port、bridge CLI、terminal CLI 与 schema，不从规划草案复制未编译类型或虚构命令；有差异先回到规划。
- [ ] 所有 L2 SSH/断网/进程测试只使用授权隔离设备与可丢弃 pane；规划阶段不启动 live herdr/SSH。

## 有序步骤

1. [ ] 实现 `SshTransportOutcome.cs` 和 fakeable child abstraction，先锁 channel/stage/epoch/exit/redaction 契约，不把 stderr 文本放进公共 DTO。
2. [ ] 实现 `SshProcessChannel.cs` 的三流并发、bounded stderr、单 writer、取消和直接 child 回收；用 fake process 注入 partial read/write/exit。
3. [ ] 实现 `RemoteCompatibilityProbe.cs`，按 Device/profile/host-key/helper revision 缓存固定
   version/schema/helper 探针；每个 SessionKey 单独 RPC ping，证据分栏。
4. [ ] 实现 `RemoteRpcConnectionFactory.cs`，分别创建 request/event child，复用 HD-008 framer/pending/subscribe，验证 stdout 污染 latch 与 teardown。
5. [ ] 实现 `RemoteTerminalTransportFactory.cs`，复用 HD-013 transport/parser，observe/control 只能来自领域授权，断线清 input 且新 epoch 新 parser。
6. [ ] 实现 `RemoteSessionTransportSet.cs`：完整 SessionKey 拥有 epoch/pending/child registry，
   request+event 预留 pair 后启动；同设备多 session 隔离。只输出 disposition，不加 timer。
7. [ ] 补全 contract/fault fixtures 和所有 L1；再在隔离设备跑 L2 三通道、banner/stderr/断网/daemon/pane exit 场景。
8. [ ] 独立审查 AC1–AC8；AC24/26 只登记候选贡献，交 HD-026 汇总，不修改全局 acceptance 为 passed。

## 精确测试路径与用例

- `tests/Unit/HerdDesk.Infrastructure.Tests/SshTransports/SshProcessChannelTests.cs`：三流并发、stderr flood、partial IO、single writer、cancel≤3s、只结束直接 child。
- `tests/Unit/HerdDesk.Infrastructure.Tests/SshTransports/RemoteCompatibilityProbeTests.cs`：local/remote schema 分离、hash、ping、banner、unknown protocol、bounded output。
- `tests/Unit/HerdDesk.Infrastructure.Tests/SshTransports/RemoteSessionTransportSetTests.cs`：同设备
  两个 SessionKey、不同 endpoint/receipt、request/event pair、部分启动回滚、epoch/late event 隔离。
- `tests/Contract/SshTransports/RemoteRpcStreamContractTests.cs`：banner before/between JSON、stderr JSON、16 MiB、truncated EOF、unknown field。
- `tests/Contract/SshTransports/RemoteTerminalStreamContractTests.cs`：`-T`、首 full、delta、terminal.closed/EOF/exit 区分、old epoch/input drop。
- `tests/Integration.Ssh/Transports/RemoteTransportIntegrationTests.cs`：Windows→Linux 的三 child、
  同设备两 named sessions、各自 schema/ping/断网恢复与跨设备隔离。

## 命令与证据等级

- [ ] **现有命令**：`just ci`，启动门和收尾均执行；只证明离线 G0/既有回归。
- [ ] **拟建 L1 unit**：`dotnet test tests/Unit/HerdDesk.Infrastructure.Tests/HerdDesk.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~SshTransports`。
- [ ] **拟建 L1 contract**：`dotnet test tests/Contract/HerdDesk.ContractTests.csproj -c Release --filter FullyQualifiedName~Remote`。
- [ ] **拟建 L2**：`dotnet test tests/Integration.Ssh/HerdDesk.Integration.Ssh.csproj -c Release --filter FullyQualifiedName~RemoteTransportIntegration`，隔离 fixture 未配置时明确 skip/UNVERIFIED。
- [ ] **L3 候选**：从 App 观察设备 A/B 状态、断线提示、pane observe/control 恢复与无串写；记录 UI/环境证据但由 HD-026 判最终 AC24/26。

## 审查与回滚

- [ ] 审查三个 child 的 stdout/stderr/parser/lock 是否完全分离、所有路径都 `-T`、
  SessionKey/epoch/pending 是否 scoped、错误是否脱敏、无内置 retry。
- [ ] L1 失败回滚本任务 adapter；L2 失败禁用该 remote capability，保留 local path，不“容错”跳 banner 或并流 stderr。
- [ ] teardown/rollback 仅关闭本应用该 SessionKey 的直接 ssh child、丢弃其未发送输入并
  恢复 disconnected；不停止 daemon/agent/pane，不影响同设备其他 session 或其他 DeviceId。
