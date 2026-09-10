# HD-035 安全与许可审查证据索引

L1 收口目录加 L2 工作树 admitted-input auditor（`scripts/audit_release_inputs.py`）。指向已交付的 HD-002 许可台账、HD-006 ADR 基线、HD-014 renderer allowlist、HD-020/024 SSH fail-closed、HD-031 clipboard/OSC52/cache、HD-032 文件故障目录、HD-034 包装目录。不是产品 AC02/AC43/AC44 通过，也不是 live 依赖扫描、renderer 进程观察、canary 导出或已签名包反向审计证明。

权威机器文件：

- `evidence/security-release/catalog.json`
- `evidence/security-release/support-matrix.json`
- `evidence/security-release/inventory.json`
- `implementation/hd-035-l2.json`

`phase_gate=not_passed`。`g0_passed=false`。`ac02_passed` / `ac43_passed` / `ac44_passed` 为 false。模板文件 `template=true`，空 hash / 扫描日期 / 工具版本，不是一次成功运行。缺扫描工具或数据库下载失败是未覆盖，不是零漏洞。扫描失败不得被后续成功覆盖。公开可见不是许可授予。`tests/Integration.Windows` 未建仓。

本页不填写未验证的扫描工具版本或 advisory 数据库日期。

## 执行卡

| 场景 | AC / R | owner | L1 产物 | live |
|---|---|---|---|---|
| license inventory | AC02 | HD-002 | `docs/licensing/register.json` | `not_run` |
| herdrm not copied | AC02 | HD-002, HD-006 | ADR-001 / register herdrm `blocked` | `not_run` |
| nuget scan | AC43 | HD-007 | `src/HerdDesk.App/packages.lock.json` 存在；live advisory 未跑 | `not_run` |
| cargo scan | AC43 | HD-008, HD-027 | `bridge/` 与 `filebridge/` lock 存在；live advisory 未跑 | `not_run` |
| npm scan | AC43 | HD-014 | `web/terminal/package-lock.json` 存在；live audit 未跑 | `not_run` |
| renderer boundary | AC44 | HD-014, HD-020, HD-024 | L1 allowlist / SSH fail-closed；不是 live 进程观察 | `not_run` |
| diagnostic canary | R4 | HD-031 | L1 脱敏；不是 live canary 导出 | `not_run` |
| signed package reverse audit | R5 | HD-032, HD-034 | HD-034 无已签名 MSIX | `not_run` |

Contract catalog 各条保持 `not_run`。不得发明扫描结果、工具版本、advisory 日期或“零漏洞”。不得发明 Publisher、证书或已签名包 hash。

## 支持矩阵

承诺范围：Windows 11 x64 客户端，状态 `promised_not_run`。Linux x64 是远端 OS，不能替代 Windows renderer/进程观察。禁止把 Windows 字段抄到 Linux。macOS / ARM64 无独立证据，标 `unsupported` 或 `experimental`，不得标 supported。

## live 行

| 行 | 状态 | 原因 |
|---|---|---|
| live license inventory | `UNVERIFIED` / `not_run` | 无授权维护者许可决定 |
| live herdrm not copied | `UNVERIFIED` / `not_run` | 无授权 herdrm 发行输入反向审计 |
| live nuget scan | `UNVERIFIED` / `not_run` | 无授权 NuGet advisory 扫描 |
| live cargo scan | `UNVERIFIED` / `not_run` | 无授权 cargo advisory 扫描 |
| live npm scan | `UNVERIFIED` / `not_run` | 无授权 npm audit |
| live renderer boundary | `UNVERIFIED` / `not_run` | 无授权 WebView 进程观察 |
| live diagnostic canary | `UNVERIFIED` / `not_run` | 无授权 canary 诊断导出 |
| live signed package reverse audit | `UNVERIFIED` / `not_run` | 无授权已签名包解包 |

残差 JSON：`implementation/hd-035-l2.json`。L2/L3 扫描、live renderer、canary、已签名包反向审计为 `UNVERIFIED`。产品 AC02/AC43/AC44 仍为 `not_run`。`tests/Integration.Windows` 不存在。

## 后续获权审计步骤（本目录未执行）

本 L1 目录不是 AC02/AC43/AC44。后续在维护者选定项目许可、隔离扫描环境、隔离 Windows 11 x64 标准用户机、以及已签名候选包 hash 冻结之后，才可按卡执行：

1. 解包候选包，列出 NuGet、npm、Cargo、native runtime、字体和图标；与锁文件比对。HD-034 当前无已签名 MSIX。
2. 维护者决定项目许可与未决资产处置。公开可见不得当作许可授予。不得复制 herdrm 源码、图标、字体、截图或布局。
3. 运行各生态扫描。记录真实工具版本、数据库日期、命令、exit code 与原始结果。工具未安装或数据库下载失败记为未覆盖，不是零漏洞。任一步失败不得被后续成功覆盖。不把用户配置、日志或私钥上传扫描服务。
4. 人工复核已确认可利用 Critical/High。不得用例外隐藏已确认可利用严重问题。
5. 在授权隔离目标观察 renderer 进程与访问，不能只看 allowlist 字符串。
6. 用合成 canary 凭据与正文触发失败/导出；默认可见日志与包中无 canary。
7. 审核最终包与最终 SHA；修复后复测并重新绑定产物 hash。

扫描服务、解包已签名包、live WebView、SSH 与文件目标需要单独授权。本派遣未选定项目许可、未运行 live 扫描、未观察 renderer 进程、未导出 canary、未解包已签名包。
