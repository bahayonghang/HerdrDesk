# HD-020 实施计划

## 启动门

- [ ] 保持任务为 `planning`；只有 `implementation/status.json` 的 G0 `phase_gate` 经所需 runtime/Windows 证据正式变为 `passed`、待实施 HEAD 的 `just ci` 通过、HD-019 完成且用户明确批准最新父/子计划后，才可运行 `task.py start`。
- [ ] 启动前重读届时有效的 backend/frontend spec 与 HD-007/009/019 产物，确认项目名和 device port owner；若计划发生实质变化，先回到规划复核。
- [ ] 所有 live SSH 仅在用户批准的隔离目标执行；本任务不运行生产/训练主机、live herdr、terminal 输入或 helper 安装。

## 有序步骤

1. [ ] 扩展 HD-007 `DeviceProfile` 的 SSH variant，并定义 auth mode、host-key/test result；保持 `SessionProfile` 只拥有 endpoint/named session、Contracts 为 BCL-only，并加未知模式 fail-closed 用例。
2. [ ] 扩展同一 `ConfigurationDocument` / `AtomicConfigurationStore` 直接保存 SSH variant 与多个 SessionProfile；沿用 expected revision、原子保存、文件锁和旧文件保留，不新增 store 或未发布格式 migration。
3. [ ] 实现 `OpenSshConfigResolver.cs` 与 fakeable process runner；固定 executable，限制 stdout/stderr/超时，解析 `ssh -G` 而不把秘密放进结果。
4. [ ] 实现唯一 `SshProcessSpecFactory.cs`；覆盖 alias/port/identity/agent/ProxyJump、`-T`/BatchMode/strict host key，并证明不经 shell、不接收 UI argv。
5. [ ] 实现 `HostKeyTrustStore.cs`：候选指纹、侧信道确认、changed 比较、私有 ACL、atomic replace 与 `KnownHostRevision`；禁止测试路径自动接受/覆盖。
6. [ ] 实现 `SshConnectionTestService.cs` 的固定阶段、取消和直接子进程清理；分类 unknown/changed/unsupported/auth/network，默认输出脱敏。
7. [ ] 完成 EditDevicePage/ViewModel 与 HostKeyReviewDialog；显示支持/未支持认证、每阶段结果、取消与硬阻断，补 AutomationProperties 与键盘流。
8. [ ] 串行完成 L1、L2、L3 证据；把 AC22/23 标为本任务“贡献”，不抢占 HD-026 的最终结论。

## 精确测试路径与用例

- `tests/Unit/HerdDesk.Infrastructure.Tests/Configuration/AtomicConfigurationStoreTests.cs`：SSH variant 与同 DeviceId 两个 SessionProfile round-trip、稳定 DeviceId、秘密字段拒绝、并发写、atomic failure recovery。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/OpenSshConfigResolverTests.cs`：Unicode/空格、多个 identity、agent、ProxyJump、超限/超时/恶意 stderr。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/SshProcessSpecFactoryTests.cs`：ArgumentList token、无 shell、无 `-tt`、无密码、alias `-` 注入拒绝、固定 `true` 命令、所有 hop 共用应用私有 trust file 且严格校验。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/HostKeyTrustStoreTests.cs`：unknown 不写、确认后写、changed 不覆盖、revision、文件锁/替换失败。
- `tests/Unit/HerdDesk.App.Tests/Devices/EditDeviceViewModelTests.cs`：阶段状态、取消、unsupported 文案、UI 无 raw argv 接口。
- `tests/Integration.Ssh/Auth/SshDeviceConfigurationTests.cs`：隔离 OpenSSH 的 key/agent/port/ProxyJump、unknown 侧信道确认、changed 硬失败、无隐藏 prompt。

## 命令与证据等级

- [ ] **现有命令 / 启动前与收尾**：`just ci`；只证明当前离线 G0/回归面，不证明 SSH 或 WinUI。
- [ ] **拟建命令 / L1**：`dotnet test tests/Unit/HerdDesk.Infrastructure.Tests/HerdDesk.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~Ssh`。
- [ ] **拟建命令 / L1**：`dotnet test tests/Unit/HerdDesk.App.Tests/HerdDesk.App.Tests.csproj -c Release --filter FullyQualifiedName~Devices`。
- [ ] **拟建命令 / L2**：`dotnet test tests/Integration.Ssh/HerdDesk.Integration.Ssh.csproj -c Release --filter FullyQualifiedName~SshDeviceConfiguration`，仅在显式隔离 fixture 启用；缺环境必须 skip/UNVERIFIED。
- [ ] **L3 人工**：记录 Windows/OpenSSH 版本、config 片段脱敏 hash、agent/ProxyJump 拓扑、host-key 侧信道、页面可访问性、预期/实际与证据路径。

## 审查与回滚

- [ ] 安全审查 argv、trust store ACL/原子性、错误脱敏、UI 边界和 changed-key 无旁路；独立检查 AC1–AC7 与原 AC22/23 的每个分句。
- [ ] 若 L1 失败，回滚本任务文件；若 L2/L3 失败，保留配置编辑但禁用远端 Connect/Test，标明不支持组合。
- [ ] 回滚只能终止本任务拥有的直接 ssh 子进程和恢复旧配置/trust 文件；不得改用户 known_hosts、停止 daemon/agent 或删除远端数据。
