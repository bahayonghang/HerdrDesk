# HD-034 签名、安装与更新回滚证据索引

L1 收口目录。指向已交付的 HD-007 配置/宿主、HD-011 设置 ViewModel、HD-021 helper 信任清单、HD-028 `TransferCoordinator` 产物。不是产品 AC41/AC42 通过，也不是 live 安装 / 签名 / 更新 / 回滚证明。

权威机器文件：

- `evidence/packaging/catalog.json`
- `evidence/packaging/support-matrix.json`
- `implementation/hd-034-l2.json`

`phase_gate=not_passed`。`g0_passed=false`。`ac41_passed` / `ac42_passed` 为 false。模板文件 `template=true`，空 hash / 退出码 / Publisher，不是一次成功运行。假 Publisher 不能让 AC41 通过。未签名本地构建不能当发行安装证据。复制旧 EXE 不能当回滚。`packaging/` 为 lab identity 未签名 layout 叠加，不是 WAP 工程，不是发行 Publisher。`App.xaml` 已存在。`tests/Integration.Windows` 是 `net10.0` 控制台 runner，catalog token 仍为 false。

## 执行卡

| 场景 | AC | owner | L1 产物 | live |
|---|---|---|---|---|
| clean install | AC41 | HD-007, HD-011 | 宿主 stub / `AppDataPaths` / `ShellViewModel` / `SettingsViewModel` | `not_run` |
| runtime missing | AC41 | HD-007 | `global.json` / setup pin；Evergreen WebView2 为计划运行时 | `not_run` |
| signed update | AC42 | HD-007 | 原子 JSON 配置存储；App Installer 为计划渠道 | `not_run` |
| bad publisher / tamper | AC42 | HD-021, HD-007 | `TrustedHelperManifestProvider`；拒绝错误 Publisher / 损坏包 | `not_run` |
| signed rollback | AC42 | HD-007 | 上一签名包 + 配置备份；复制 EXE 不是回滚 | `not_run` |
| config backup restore | AC42 | HD-007 | `AtomicConfigurationStore` 原子写与 `.bak` | `not_run` |
| file job defer | AC42 | HD-028 | `TransferCoordinator`；更新等待或用户取消自有作业；不杀 herdr | `not_run` |
| unsigned local build | AC41 | HD-007 | 未签名本地构建可记 `not_run`；不能当发行安装 | `not_run` |

Contract catalog 各条保持 `not_run`。不得发明 Publisher、证书主题、指纹、timestamp URL、分发 URL 或未构建包的 hash。

## 支持矩阵

承诺范围：Windows 11 x64 客户端。Linux x64 是远端 OS，不是 MSIX 客户端，不得宣称 Linux MSIX。禁止把 Windows 字段抄到 Linux。macOS / ARM64 无独立证据，标 `unsupported` 或 `experimental`，不得标 supported。

## live 行

| 行 | 状态 | 原因 |
|---|---|---|
| live clean install | `UNVERIFIED` / `not_run` | 无授权干净机安装 |
| live runtime missing | `UNVERIFIED` / `not_run` | 无授权缺 runtime VM |
| live signed update | `UNVERIFIED` / `not_run` | 无授权签名更新渠道 |
| live bad publisher / tamper | `UNVERIFIED` / `not_run` | Publisher 身份未确认 |
| live signed rollback | `UNVERIFIED` / `not_run` | 无授权签名回滚 |
| live config backup restore | `UNVERIFIED` / `not_run` | 无授权安装时配置恢复 |
| live file job defer | `UNVERIFIED` / `not_run` | 无授权更新期间作业延后 |
| live unsigned local build | `UNVERIFIED` / `not_run` | 无授权签名服务 |

残差 JSON：`implementation/hd-034-l2.json`。`l2_live_install` / `l2_live_sign` / `l2_live_update` / `l2_live_rollback` / `l3_clean_machine` 为 `UNVERIFIED`。产品 AC41/AC42 仍为 `not_run`。`tests/Integration.Windows` 是控制台 runner，不是 live 安装工程。`scripts/package_release.ps1 -Action Verify` 对 fixture 的退出码不是 AC41。

## 后续获权干净机矩阵（本目录未执行）

本 L1 目录不是 AC41/AC42。后续在发行方确认 Publisher、证书、timestamp、App Installer URL，并授予隔离 Windows 11 x64 标准用户机之后，才可按卡执行：

1. 干净机：安装 → 首次启动 → 普通启动 → 卸载。记录实际 package identity / hash / 返回值。不得填写假 Publisher。
2. 缺 runtime VM：Evergreen WebView2 为计划运行时。缺失必须提示，禁止静默管理员安装。
3. 更新：同一受控 App Installer 渠道。不并行自建更新服务。官方 App Installer 降级不是默认更新通道。
4. 故障：错误 Publisher、损坏包、断网、锁文件。当前安装与配置仍可恢复。
5. 回滚：上一已验证签名包 + 配置备份。复制旧 EXE 不是回滚。应用升级与 herdr 升级相互独立，不停止用户 daemon/agent。
6. 活动文件作业：等待结束，或由用户取消本应用自有 `TransferCoordinator` 作业后再更新。
7. 未签名本地构建只能记为 `not_run`，不能标发行安装通过。

签名服务、安装/卸载、外部渠道写入需要单独授权。本派遣未签名、未安装、未写入凭据或远端 URL。
