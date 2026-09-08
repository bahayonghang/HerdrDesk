# HD-021 实施计划

## 启动门

- [ ] 保持 `planning`；只有权威 `implementation/status.json` 已记录 `phase_gate=passed`、待实施 HEAD 的 G0 `just ci` 通过、HD-008/020 完成且用户明确批准最新计划后，才可 `task.py start`。
- [ ] 启动前核对 HD-008 的 Cargo workspace/binary 名、HD-020 的 process/profile port 和 HD-007 的 App/测试项目；职责不一致先改计划，不另建兼容层。
- [ ] 本轮规划不构建/下载依赖、不部署 helper、不连 live SSH；实施期 live 写入仍只限用户授权的普通隔离账号。

## 有序步骤

1. [ ] 定义 `targets.json` 与 `HelperDeploymentContracts.cs`，固定 target、manifest、plan、当前 device/version/hash 确认值、receipt/lease 语义；不能猜 target 或持久化 blanket consent。
2. [ ] 完成 release 脚本和 native CI matrix；从锁定源生成每目标 artifact 与 manifest，验证干净源、commit、length/hash，上传构建产物前跑 Rust gate。
3. [ ] 实现 `TrustedHelperManifestProvider.cs`，release 只允许编译期固定资源索引，不接受运行时任意路径；补索引 digest/应用版本绑定、篡改、路径遍历、重复 target、大小/hash/protocol 错误测试。
4. [ ] 实现 `RemotePlatformProbe.cs` 与 allowlist；只读探针 stdout 必须精确，stderr 分离，unknown/banner/timeout 失败。
5. [ ] 实现按 DeviceId/SessionKey/resolved-target 分区的 receipt store、文件锁、atomic
   replace 与 epoch/artifact lease；模拟 crash 与旧 epoch 晚释放，证明上一 current 可读且
   不覆盖另一 endpoint。
6. [ ] 实现 deployment service 的本次确认比较，以及不依赖 helper 的固定 `ssh -T` bootstrap：独占 staging、远端 length/hash、chmod、hard-link no-clobber publish、selftest 和本地 current 切换；取消只清本 job。
7. [ ] 完成 HelperInstallDialog/ViewModel 的 target/version/hash/目录/操作展示、确认、取消、失败和 rollback；无 blanket “always install” 开关。
8. [ ] 按 target 跑 L1/L2/L3，生成 AC25 唯一证据矩阵；未跑 target 明确 not_run，不由其他平台代替。

## 精确测试路径与用例

- `tests/Contract/BridgeRelease/BridgeReleaseManifestTests.cs`：schema、重复 target、path traversal、length/hash/protocol/commit、五 target 完整性。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/HelperDeployment/TrustedHelperManifestProviderTests.cs`：编译期索引 digest 与应用版本绑定、任意路径拒绝、篡改 payload/manifest；package identity 留给 HD-034 的打包测试。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/HelperDeployment/RemotePlatformProbeTests.cs`：allowlist、banner、多行、控制字符、unknown、timeout。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/HelperDeployment/HelperDeploymentReceiptStoreTests.cs`：同设备多 session/endpoint 隔离、锁、atomic failure、plan 变化重确认、旧 epoch lease、切回旧 receipt。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/HelperDeployment/RemoteHelperDeploymentServiceTests.cs`：零同意写、三元组变化重确认、固定 argv/stdin、private dir、exclusive staging、length/hash、hard-link no-clobber/selftest、并发和精确取消清理。
- `tests/Integration.Ssh/HelperDeployment/RemoteHelperDeploymentTests.cs`：普通账号、无 sudo、权限、远端 hash、活跃旧版本、失败回滚、Linux/macOS/arch 独立结果。

## 命令与证据等级

- [ ] **现有命令**：`just ci`，在启动前和合并前运行；它不构建拟建 bridge matrix，也不证明部署。
- [ ] **拟建 L1/Rust**：`cargo fmt --manifest-path bridge/Cargo.toml --all -- --check`、`cargo clippy --manifest-path bridge/Cargo.toml --locked --all-targets -- -D warnings`、`cargo test --manifest-path bridge/Cargo.toml --locked`。
- [ ] **拟建 L1/release**：`pwsh -NoLogo -File scripts/Build-HerdDeskBridgeRelease.ps1 -VerifyOnly`；检查 manifest 与本机可构建 target，不伪造其他 runner 成功。
- [ ] **拟建 L1/.NET**：`dotnet test tests/Contract/HerdDesk.ContractTests.csproj -c Release --filter FullyQualifiedName~BridgeRelease` 及 Infrastructure helper deployment filter。
- [ ] **拟建 L2**：`dotnet test tests/Integration.Ssh/HerdDesk.Integration.Ssh.csproj -c Release --filter FullyQualifiedName~RemoteHelperDeployment`，必须显式隔离 fixture；缺环境为 skipped/UNVERIFIED。
- [ ] **L3 人工**：逐 target 记录 consent、取消、更新、回滚、版本仍运行和辅助技术读屏，附脱敏截图与 manifest/artifact hash。

## 审查与回滚

- [ ] 独立审查 target 选择、manifest 信任、bootstrap 无 helper/无 sudo、staging 独占与 no-follow、hard-link no-clobber、active lease、精确清理和日志隐私；逐分句追踪 AC1–AC7→原 AC25。
- [ ] 任一 hash/selftest/current 失败均切回旧 receipt；不覆盖同 version 不同 hash，不删除正在运行或未知来源文件。
- [ ] 回滚只影响本应用对应 session/target 的 helper current、自有 staging 和 ssh 直接子进程；不修改系统目录、全局 PATH、daemon、agent 或用户其他文件。
