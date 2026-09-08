# HD-007 technical design

## Solution and dependency graph

保留 `HerdDesk.slnx`、`Directory.Build.props`、`global.json`、`NuGet.Config`、`justfile` 和现有三个工程；拟增 `src/HerdDesk.App/`、`src/HerdDesk.Infrastructure/`、`src/HerdDesk.Terminal.Web/`、`tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj`、`tests/Unit/HerdDesk.Infrastructure.Tests/HerdDesk.Infrastructure.Tests.csproj`、`tests/Contract/HerdDesk.ContractTests.csproj`。统一布局依据父任务 `research/test-layout.md`；Windows integration 由 HD-011 建立 `tests/Integration.Windows/HerdDesk.Integration.Windows.csproj`，其余任务复用。HD-007 创建可编译骨架和无业务的 App 启动容器；`App.xaml(.cs)`、`MainWindow.xaml(.cs)`、Shell 与 settings 内容由 HD-011 接手。

允许引用固定为：Contracts 无 ProjectReference/PackageReference；Core 只引用 Contracts；Infrastructure 只引用 Contracts/Core；Terminal.Web 只引用 Contracts 与经 HD-005 核验的 Windows renderer packages；App 引用 Core、Infrastructure、Terminal.Web；测试只能从测试工程指向被测生产工程。该图由 `scripts/validate_repository.py` 单点校验，build/locked restore 验证实际项目可解析，不另建第二份规则表。

`Directory.Packages.props` 只在首个经核验的 package 落地时建立，保存精确 direct version；每个相关项目提交 `packages.lock.json`。`NuGet.Config` 只加入实施时核验的 HTTPS source 和最小 package-source mapping。CI 传 `--locked-mode`；发现 lock 漂移先审查 diff、许可与 transitive 变化，禁止自动更新。Windows App SDK 展示版本不写入任何版本字段。

## Configuration boundary

拟建 `src/HerdDesk.Contracts/Configuration/DeviceProfile.cs`、`SessionProfile.cs`、`IDeviceProfileStore.cs`，以及 `src/HerdDesk.Infrastructure/Configuration/AppDataPaths.cs`、`ConfigurationDocument.cs`、`AtomicConfigurationStore.cs` 和对应 tests。App 通过 Windows application-data API 解析 settings/cache/log 根目录，再以 `AppDataPaths` 构造值注入；store 本身只使用 BCL 文件 API，可在测试中使用临时目录。

磁盘 document 以 `schema_version` 开头，`devices` 中每个 DeviceProfile 保存稳定 DeviceId、label、连接种类及已核验 absolute herdr path，并包含 `SessionProfile[]`。每个 session 保存 LocalDefault/NamedSession/ExplicitEndpoint 类型及相应 named session/endpoint，结合所属 DeviceId 得到完整 SessionKey；同设备两会话不能被合并成一个连接。endpoint 不是任意命令或 SMB pipe，具体映射仍经 HD-003/008 adapter验证。HD-011 直接消费该 port；HD-020 在同一文档/存储中增加 SSH 连接字段，不另建 profile store，也不成为 P1 保存前置。尚未发布的格式直接扩展，不建立旧格式 migration；实际发行后的格式演进才按 HD-034 更新/回滚合同处理。高版本返回 `configuration_version_unsupported`；损坏主文件保留，只有用户明确恢复才读取 `.bak`。

port 以 expected revision 拒绝同实例过期草稿；稳定 DeviceId 由 host 创建一次，rename/edit 不改变。HD-011 在建立配置 writer 前取得跨进程单实例所有权，`SemaphoreSlim` 只负责实例内串行化，不证明跨进程互斥。同目录创建唯一 owned temp，UTF-8 严格序列化并 `Flush(true)`；首次保存同卷 move，覆盖 replace 并生成单份 `.bak`。失败清理 owned temp，绝不先删除目标；旧 revision 返回 `configuration_write_conflict`，不做 last-writer-wins。删除 profile 只删本地引用及自有连接，不停止 daemon/session。

## Diagnostics boundary

拟建 `src/HerdDesk.Contracts/Diagnostics/DiagnosticEvent.cs` 与 `IDiagnosticSink.cs`，以及 `src/HerdDesk.Infrastructure/Diagnostics/JsonlDiagnosticSink.cs`。event 是受限字段 record：UTC time、component、operation、outcome、error code、epoch、duration、queue bytes、可选脱敏 device/session alias；不含自由文本或 `Exception`。

sink 使用有界 channel 和单 writer，文件按固定字节预算轮换；队列满时累加 dropped count，不阻塞 UI 或协议 reader。ID alias 由每安装随机 salt + SHA-256 生成，salt 只在应用数据中保存且不写日志。调用者在边界把异常映射为稳定类别；默认日志不落 stderr/raw JSON/terminal bytes。显式诊断导出由后续任务另做。

## Composition root

拟建 `src/HerdDesk.App/Composition/AppServices.cs`。它负责解析 AppDataPaths，构造 diagnostic sink/config store，注册 Core services 和后续 `IRpcConnectionFactory`、`ITerminalTransportFactory`、`ITerminalRenderer` factory。当前不存在的 adapters 不注册 fake success；功能通过 unavailable capability 保持禁用。所有 singleton 实现 `IAsyncDisposable`，App 停止时按 renderer→session actors→owned transports→diagnostics 的次序释放。

## Future gate

`just ci` 保留现有 Python/selftest/capture/structure/BCL smoke，并新增实际存在后才可调用的 recipes：locked restore、`dotnet format --verify-no-changes --no-restore`、Release build、unit/contract tests。HD-008/014 创建 Cargo/npm 入口后再把 fmt/clippy/test、`npm ci`/typecheck/lint/test 纳入；不得提前写会调用不存在路径的 recipe。

Actions 拆成跨平台 BCL/contract、Rust matrix 与 Windows desktop job；job shell 必须传播每个命令的非零退出码。live Windows/herdr/IME 不进无授权 CI。`just ci` 的平台分支与 Actions job 名单写入同一维护说明，但 branch ruleset 是远端状态，另行读取验证。

## Error and rollback

startup 区分 restore/build、configuration、diagnostic、adapter-unavailable；日志错误只降级诊断并展示安全摘要，不能开放控制。若新产品骨架/依赖无法还原，回退 solution 中新增项目和相应 gate，保留原 G0 Contracts/Core/tests。回滚不删除用户配置、日志、herdr 数据或任何 daemon/agent。
