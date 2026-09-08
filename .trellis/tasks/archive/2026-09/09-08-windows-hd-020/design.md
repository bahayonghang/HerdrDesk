# HD-020 技术设计

## 边界与依赖

`HerdDesk.App` 只收集字段和显示阶段结果；`HerdDesk.Infrastructure.Ssh` 独占 OpenSSH 发现、argv、子进程与 host-key。配置仍由 HD-007 的单一 `ConfigurationDocument` / `AtomicConfigurationStore` 持久化，本任务只扩展其中的 SSH variant，不建立 `SshDeviceProfileStore`。
Core 不引用 SSH、WinUI 或凭据。现有 `DeviceId` 继续作为设备稳定键：`src/HerdDesk.Contracts/TerminalModels.cs:4-7`。
HD-019 必须先给出可工作的本地 App 导航与生命周期；HD-007/009 已建立的项目与通用 device contract 是本任务的接入点。`DeviceProfile` 拥有设备级连接字段，内含多个 `SessionProfile`；每个 session 形成独立 SessionKey，禁止复制第二套身份或配置模型。

## 精确拟建/修改文件

- `src/HerdDesk.Contracts/Configuration/DeviceProfile.cs`：为 HD-007 的 BCL-only device contract 增加 SSH variant、认证模式与 connection profile revision；不复制 `SessionProfile`。
- `src/HerdDesk.Contracts/SshConnectionContracts.cs`：host-key 状态、测试阶段/result 和只读 SSH 解析/测试 port；不定义第二个 profile/store port。
- `src/HerdDesk.Infrastructure/Configuration/ConfigurationDocument.cs` 与 `AtomicConfigurationStore.cs`：在同一 document 中 round-trip SSH variant 与 `SessionProfile[]`，沿用原子替换、文件锁和 expected revision。
- `src/HerdDesk.Infrastructure/Ssh/OpenSshConfigResolver.cs`：固定 `ssh.exe` 路径、`-V`/`-G` 只读探测与有界输出解析。
- `src/HerdDesk.Infrastructure/Ssh/SshProcessSpecFactory.cs`：唯一的本地 argv 与远端 POSIX 参数引用边界；UI 不可绕过。
- `src/HerdDesk.Infrastructure/Ssh/HostKeyTrustStore.cs`：HerdDesk 自有 known-host 数据、候选比较、changed 阻断和 revision。
- `src/HerdDesk.Infrastructure/Ssh/SshConnectionTestService.cs`：可取消的固定阶段编排与无副作用认证探针。
- `src/HerdDesk.App/Devices/EditDevicePage.xaml` 与 `EditDevicePage.xaml.cs`：配置表单、阶段列表和可访问状态文本。
- `src/HerdDesk.App/Devices/EditDeviceViewModel.cs`：类型化校验、保存/测试命令和 busy/cancel 状态。
- `src/HerdDesk.App/Devices/HostKeyReviewDialog.xaml` 与 `HostKeyReviewDialog.xaml.cs`：候选指纹、可信侧信道提示、确认/取消/changed key 阻断。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/`、`tests/Unit/HerdDesk.App.Tests/Devices/`、`tests/Integration.Ssh/Auth/`：下述合同、ViewModel 与真机用例。

以上均为未来路径；当前树中不存在，不附伪造行号。若前置任务已建立同名职责文件，实施评审应合并到该唯一 owner，而不是保留重复类型。

## 主要契约

`DeviceProfile` 包含稳定 `DeviceId`、可改 label、Local/SSH 判别和 `SessionProfile[]`。SSH variant 只含 host alias、可选 user/port、identity-file 引用、ProxyJump alias、远端 herdr/helper 绝对路径、`SshAuthMode` 与 device-level `ProfileRevision`；endpoint/可选 named session 只存在于 `SessionProfile`。一个 DeviceProfile 的多个 SessionProfile 形成多个完整 SessionKey，并由后续任务创建独立 DeviceSession。
`SshAuthMode` 只允许 `OpenSshConfig`、`IdentityFile`、`Agent`；未知值保留 raw 并禁用连接，不自动映射为可用模式。
`SshResolvedConfiguration` 保留有效 hostname/user/port、identity-file 列表、identity-agent、ProxyJump 以及“显式字段/ssh config”来源，但默认 UI 和日志显示脱敏值。
`HostKeyAssessment` 为 `Trusted`、`UnknownCandidate`、`Changed`、`Unavailable`；候选含 host/port、key type、key blob hash、SHA256 fingerprint 和采集时间，始终带 `VerifiedOutOfBand=false`。trust record 以规范化 host/port/跳板层级为键，目标主机和每个 ProxyJump hop 分别核验。
`SshConnectionTestResult` 是阶段结果集合，每阶段只有稳定 code、duration 和 bounded metadata；不存在 raw stderr/argv 字段。

## 数据与信任流

1. ViewModel 将设备字段和 session rows 映射为同一个 `DeviceProfile`；service 先做长度、端口、alias 不以 `-` 开头、路径、认证模式及同设备 SessionKey 唯一性校验。label/session 排序等非连接字段不能伪造 SSH profile revision 变化。
2. resolver 通过 `ProcessStartInfo.ArgumentList` 调 `ssh -G`；不经过 `cmd /c`、PowerShell 或 `UseShellExecute=true`。解析器以第一个 ASCII 空白分隔 key/value，保留带空格和 Unicode 的 value，并限制总输出。
3. host-key 探测使用同一系统 OpenSSH 工具链和固定参数；扫描到的 key 是不可信网络观察，只能生成候选，不能写 trust store。
4. ReviewDialog 显示完整 SHA256 fingerprint、host/port/key type、侧信道核验说明；用户明确确认后，store 才把精确 key 写入应用私有文件并递增 `KnownHostRevision`。
5. `SshProcessSpecFactory` 对测试及后续通道统一传入 `StrictHostKeyChecking=yes` 和应用私有 `UserKnownHostsFile` 的绝对路径；不得回落到自动接受，也不得修改用户全局 known_hosts。目标主机和 ProxyJump hop 任一未受信都停止。
6. 已有 host/port 与新 key 不同进入 `Changed`。普通“测试连接”无替换入口；替换必须重新进入 review、再次侧信道确认并产生新 revision。
7. 通过身份门后执行固定 `ssh -T -o BatchMode=yes ... true` 探针。远端命令是编译期常量；profile、terminal output 或 UI 文本均不能成为命令。

## 状态、并发与取消

每个 ViewModel 同时最多一个测试；新测试先取消并等待旧测试直接子进程退出。service 的阶段机为 `Idle → Resolving → AwaitingHostVerification → Authenticating → Succeeded/Failed/Cancelled`。
host review 等待不持有 ssh 进程。取消先关闭重定向流，再等待最多 3 秒，最后只终止本应用启动的直接 `ssh.exe`；不得 kill tree、daemon 或远端 agent。
设备保存沿用 HD-007 的同目录临时文件、flush 后 atomic replace；trust store 与 configuration document 分开，任一失败保持旧版本可读。写入沿用既有串行门、文件锁和 expected revision，避免双窗口丢更新；不为尚未发布的 document 设计 migration。

## 错误与恢复

稳定类别至少为 `ssh_executable_unavailable`、`ssh_config_invalid`、`ssh_profile_invalid`、`host_key_unknown`、`host_key_changed`、`auth_unsupported`、`auth_failed`、`ssh_test_timeout`、`ssh_test_cancelled`、`persistence_failed`。
网络/SSH stderr 只进入有界内部分类；默认消息不拼原文。分类不确定时返回 `ssh_test_failed` 并提供脱敏诊断导出入口，不能误写为密码错误。
失败不改 trust/profile 的已提交版本。回退是禁用该设备远端连接并保留可编辑配置；本地设备与 herdr 继续运行。

## 验证设计

Unit/contract 使用 fake process runner、临时应用数据目录与恶意哨兵字符串；覆盖同一 DeviceProfile 的多个 SessionProfile，不得启动真实 ssh。L2 才在隔离 Windows 11 + 可丢弃 SSH 账号上验证 OpenSSH config、agent、ProxyJump 和 host-key 变化。
L3 人工检查页面焦点、键盘、屏幕阅读器名称、候选/changed 的非颜色状态与取消交互；该证据由 HD-026 最终矩阵引用。
