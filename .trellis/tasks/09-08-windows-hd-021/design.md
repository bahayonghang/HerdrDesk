# HD-021 技术设计

## 边界与依赖

HD-008 产出 `herddesk-bridge` 源与协议；本任务从该锁定源生成发布产物，并实现选择、同意、部署、激活和回滚。
HD-020 提供已验证 SSH profile、host-key 与唯一进程构造边界。本任务不重新解析 alias、不接收密码，也不打开产品 request/event/terminal 通道。
部署控制面位于 Infrastructure，App 只渲染当前不可变 `DeploymentPlan` 并返回本次确认；renderer 和 Core 无远端文件写能力。

## 精确拟建/修改文件

- `bridge/release/targets.json`：封闭 target triple、OS/arch probe 值、artifact 名与支持状态。
- `bridge/release/manifest.json`：构建生成的版本、protocol、commit、length 与 SHA-256 清单。
- `bridge/release/README.md`：产物来源、native runner、签名包信任、验证和不支持目标。
- `scripts/Build-HerdDeskBridgeRelease.ps1`：按 target 构建/收集、拒绝脏源、生成并 verify manifest；不下载或部署。
- `.github/workflows/bridge-release.yml`：Linux/Windows/macOS native matrix、锁定工具链、产物 hash 与清单汇合。
- `src/HerdDesk.Contracts/HelperDeploymentContracts.cs`：`HelperTarget`、`DeploymentPlan`、本次确认值、`DeploymentReceipt` 和 service port。
- `src/HerdDesk.Infrastructure/Ssh/TrustedHelperManifestProvider.cs`：只加载构建时嵌入并绑定应用版本的 manifest/payload；release 中无任意路径 provider。
- `src/HerdDesk.Infrastructure/Ssh/RemotePlatformProbe.cs`：经 `ssh -T` 执行固定 read-only OS/arch/home 探针并严格限制输出。
- `src/HerdDesk.Infrastructure/Ssh/RemoteHelperDeploymentService.cs`：上传、远端 hash、权限、同目录 rename、自检与回滚。
- `src/HerdDesk.Infrastructure/Ssh/HelperDeploymentReceiptStore.cs`：按 DeviceId/SessionKey/已解析远端身份保存 current receipt、atomic replace、锁与版本租约。
- `src/HerdDesk.App/Devices/HelperInstallDialog.xaml`、`HelperInstallDialog.xaml.cs`、`HelperInstallViewModel.cs`：计划展示、同意、进度、取消和回滚。
- `tests/Contract/BridgeRelease/`、`tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/HelperDeployment/`、`tests/Integration.Ssh/HelperDeployment/`：下述验证。

以上是未来路径，不附行号。前置任务若已建立等价 port/runner，实施时扩展唯一 owner，不复制第二套 process 或 identity 层。

## 清单与目标契约

manifest 顶层含 `manifest_version`、`bridge_version`、`protocol_version`、`source_commit` 与 artifact map；artifact key 是 target triple，值含相对文件名、length、lowercase SHA-256。
允许目标为 `x86_64-pc-windows-msvc`、`x86_64-unknown-linux-gnu`、`aarch64-unknown-linux-gnu`、`x86_64-apple-darwin`、`aarch64-apple-darwin`；支持状态必须由该目标实际构建/测试产生。
release provider 从编译期资源索引读取 manifest/payload，并核对索引中固定的 manifest digest、应用版本与源 commit；不能由配置、环境变量或调用参数改指任意路径。HD-034 打包后再验证 package identity、Publisher 与安装目录不可写；测试 provider 只编译进测试项目。这两层分别证明可信 hash 内容链与正式分发签名，避免 HD-021 反向依赖 HD-034。
远端 probe 不调用 helper，只经 HD-020 唯一 process factory 执行编译期固定的
`ssh -T ... sh -c <probe>`；它只接受 Linux `x86_64/aarch64`、Darwin
`x86_64/arm64` 和绝对 home。stdout 行数/长度严格有界，banner、控制字符、相对 home、
unknown 或 stderr 污染均失败。

## 部署事务

1. 解析 target 后读取可信 artifact，流式计算 length/hash；不匹配则在任何 SSH 写入前停止。
2. 建立 `DeploymentPlan(device, target, version, hash, privateDir, operations)`；UI 返回当前
   device/version/hash 三个确认值。service 在首次远端写前重新比较；不一致就回到
   AwaitingConsent。确认属于该不可变 plan 实例，不持久化、不另建审批服务；部署按已解析
   远端身份用现有 async gate 串行。
3. bootstrap 不调用尚未安装的 helper。HD-020 process factory 以 `ssh -T` 执行一段编译期
   固定 POSIX `sh` 脚本，payload 是唯一 stdin；version/target/hash/length/staging id 均由
   allowlist、lowercase hex 或十进制校验后作为位置参数传入，任何 UI/path 文本不得入命令。
4. 脚本 `umask 077`，逐段拒绝 symlink 并创建用户私有 base/version/target 目录；随后以
   `mkdir .stage-<128-bit-id>` 原子独占 staging，`cd -P` 后只写相对常量名 `payload`。
   staging 已由本 job 创建且 mode 0700，因此 payload 既不覆盖旧文件也不跟随预置 symlink。
5. EOF 后以 `wc -c` 核对长度；Linux 用已探测的 `sha256sum`、macOS 用
   `shasum -a 256` 重算 hash。工具或精确输出不符即只删除 `payload` 并 `rmdir` 本 staging；
   不用 glob/recursive cleanup。hash 匹配后 chmod 0700。
6. 在 staging cwd 中用 `ln payload ../herddesk-bridge` 作同文件系统原子 no-clobber publish；
   final 已存在时不覆盖，只在独立核对其 length/hash 完全相同时视为幂等成功，否则报
   `helper_version_collision`。目标文件系统不支持 hard link 则该 target unavailable；不以
   `mv -f`、先检查再 rename 或宽松 fallback 替代。
7. 发布后运行该绝对版本路径 `--version` 核对版本/protocol。本地 receipt store 再以
   same-directory temp+flush+atomic replace 更新 `(DeviceId, SessionKey, resolved-target)` 的
   current；连接 epoch 启动时捕获 receipt/绝对路径并以完整 owner key 持有 lease，因此
   旧 epoch 的切换/释放不能替换、结束或误释放新 epoch 二进制。

## 状态、取消与恢复

状态为 `Planning → AwaitingConsent → Uploading → Verifying → Activating → Succeeded`，任一阶段可进入 `Cancelled/Failed/RolledBack`。
AwaitingConsent 不保持 SSH 进程；取消上传时关闭 stdin、终止本应用直接 ssh 子进程，并
只删除本 staging id 下的 `payload` 后 `rmdir` 空 staging。final/旧版本/current 不动。
上传、remote hash、自检或 receipt 写失败均保持旧 current。若 final 已成功但 receipt 未切换，留下未激活 immutable 版本并报告，可由下次相同计划安全复用。
双 App 实例使用 receipt 文件锁；远端独占 staging、hard-link no-clobber、不可变 final 和
hash collision 检查使竞争 fail-closed。脚本只能以精确 staging id 删除自身
`payload`/空目录；旧版本清理需要零 lease 与明确维护动作。

## 错误、隐私与信任

稳定 code 至少含 `helper_manifest_untrusted`、`helper_manifest_invalid`、`helper_platform_unsupported`、`helper_consent_required`、`helper_consent_stale`、`helper_upload_failed`、`helper_hash_mismatch`、`helper_version_collision`、`helper_selftest_failed`、`helper_activation_failed`、`helper_cancelled`。
远端字符串、path 与 stderr 都是不可信数据；默认事件只记录阶段、target、version、hash 前缀和错误 code。UI 不显示完整远端命令，service 不允许调用者提供任意脚本。
Linux/macOS payload 的内容信任来自应用内固定 manifest+hash；HD-034 负责证明该应用本身经签名包分发，不声称 Authenticode 跨平台签署 helper。独立下载/第二签名体系超出本任务。

## 验证设计

L1 用伪造 manifest、payload、clock、runner 和临时 receipt 目录证明选择/同意/事务/并发。L2 用两个普通隔离账号验证 Linux x64 与可用的第二目标；macOS/ARM64 无环境则保持 not_run。
L3 人工验证同意对话框、取消/重试/回滚和可访问文本；AC25 的每个平台结论必须绑定 source commit、manifest hash、目标环境与证据路径。
