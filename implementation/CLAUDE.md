# implementation

[根索引](../CLAUDE.md) · 实施快照

生成日期：2026-09-08。本目录存放已运行检查的 JSON 结果。当前门禁以 `status.json` 为准。`docs/implementation-g0.md` 是叙事历史。

## `status.json`

| 字段 | 值 |
|---|---|
| `phase` / `phase_gate` | G0 / `not_passed` |
| `github_repository` | `bahayonghang/HerdrDesk`（维护者创建并完成 push） |
| Python 回归 | passed，73 |
| probe selftest | passed，23，合成 |
| C# build / smoke | `passed_ci`，22 |
| Actions | passed，verified commit `629bb01bd6bdb76e8576fd29668aa84a4894e59b`，run `34138627135`，windows-latest + ubuntu-latest |
| `windows_live_tests` | `isolated_capture_recorded`（非 AC 通过） |
| `verified_acceptance_ids` | `[]` |

`scope`：G0 unit/contract/build only。每个后续 SHA 需要自己的 Actions 结果。

阻塞：runtime protocol 22 与源码 20 不兼容；named-pipe ACL / remote / endpoint 真机；`control_verified` 仍 false；IME/WinUI 未授权。

## 其他快照

| 文件 | 含义 |
|---|---|
| `probe-selftest.json` | `checks=23`，`herdr_executed=false`，平台 linux |
| `synthetic-capture.json` | 3 帧、41 decoded bytes、capture SHA-256 与 fixture 一致、`saw_terminal_closed=true`、退出/IME/输入未验证 |
| `structure-check.json` | 结构通过；当时 `csharp_compiled=false`（本地）。CI 之后的编译状态见 `status.json` |
| `original-plan-validation.json` | 规划包校验：36 任务、48 AC、272 本地链接、76 清单 hash；未执行 herdr/Windows |
| `hd-007-packages.json` | HD-007 NuGet 探针与 L2 App windows lock。WinUI 2.3.6 准入后 `windows_desktop_restore=admitted`。umbrella WASDK 2.4.0 与 Test.Sdk 仍未准入。`github_required_check=UNVERIFIED`。不是 AC39/40/47 通过。 |
| `hd-008-packages.json` | HD-008 Rust 探针。`interprocess` 2.4.4（0BSD OR Apache-2.0），toolchain 1.98.0。不是 AC03/AC04 通过。 |
| `hd-008-l2.json` | L2 Windows named-pipe ACL / live EP01–EP05 = `UNVERIFIED`。 |
| `hd-010-l2.json` | L2 live `events.subscribe` interleave = `UNVERIFIED`。L1 fake race 不能关闭产品 AC12。`ac12_passed=false`，`phase_gate=not_passed`。 |
| `hd-011-l2.json` | L2 Windows visual/activation 与 L3 IME/screen-reader/DPI = `UNVERIFIED`。四区 Shell XAML 已建仓。`ac19_passed=false`，`phase_gate=not_passed`。 |
| `hd-012-l2.json` | L2 Windows toast activation = `UNVERIFIED`。L1 reducer/ViewModel 不能关闭产品 AC17/AC18。`ac17_passed=false`，`ac18_passed=false`，`phase_gate=not_passed`。 |
| `hd-013-l2.json` | L2 live `herdr terminal session` = `UNVERIFIED`。L1 fake-child transport 不能关闭产品 AC05/AC06。`ac05_passed=false`，`ac06_passed=false`，`phase_gate=not_passed`。 |
| `hd-014-l2.json` | L2 WebView process 与 L3 DPI/theme/focus = `UNVERIFIED`。`@xterm/xterm` 6.0.0 与 WinUI WebView2 控制已准入。L1/L2 不能关闭产品 AC08/AC27。`github_required_check=UNVERIFIED`。 |
| `hd-015-l3.json` | L3 真机 IME 桌面 = `UNVERIFIED`。L2 WebView IME = `UNVERIFIED`。`webview2_admitted` / `npm_xterm_admitted` 与 HD-014 一致。`winui_admitted` 与 HD-011 一致（false）。`github_required_check=UNVERIFIED`。不能关闭产品 AC09/AC10。 |
| `hd-016-l2.json` | L2 live lease = `UNVERIFIED`。L1 coordinator 不能关闭产品 AC07/AC14/AC16。`phase_gate=not_passed`。 |
| `hd-017-l2.json` | L2 live mutation = `UNVERIFIED`。L1 coordinator 不能关闭产品 AC20。`phase_gate=not_passed`。 |
| `hd-018-l2.json` | L2 live disconnect = `UNVERIFIED`。L1 RecoveryPolicy 不能关闭产品 AC13/AC14/AC15。`phase_gate=not_passed`。 |
| `hd-019-l2.json` | L2 live local MVP = `UNVERIFIED`。缺 disposable pane / WebView2。L1 catalog/composition 不能关闭产品 AC06/AC07/AC10/AC15。`phase_gate=not_passed`。 |
| `hd-019-l3.json` | L3 IME desktop 与 agent TUI versions = `UNVERIFIED`。缺 IME desktop / agent TUI versions。`phase_gate=not_passed`。 |
| `hd-020-l2.json` | L2 隔离 Windows/OpenSSH = `UNVERIFIED`。L1 DeviceProfile 编辑/预览/测试不能关闭产品 AC22/AC23。`phase_gate=not_passed`。 |
| `hd-021-l2.json` | L2 live helper deploy = `UNVERIFIED`。L1 manifest/consent/publish 状态机不能关闭产品 AC25。`phase_gate=not_passed`。 |
| `hd-022-l2.json` | L2 live SSH = `UNVERIFIED`。L1 fake `ssh -T` transport set 不能关闭产品 AC24/AC26。`phase_gate=not_passed`。 |
| `hd-023-l2.json` | L2 live 3-device search p95 = `UNVERIFIED`。L1 in-memory 聚合不能关闭产品 AC19/AC21。`phase_gate=not_passed`。 |
| `hd-024-l2.json` | L2 live auth = `UNVERIFIED`。L1 DeviceSession backoff/block 不能关闭产品 AC22/AC26。`phase_gate=not_passed`。 |
| `hd-025-l2.json` | L2 live SSH/perf = `UNVERIFIED`。`b_ssh_measured=false`。L1 准入/队列政策不能关闭产品 AC27。`phase_gate=not_passed`。 |
| `hd-026-l2.json` | L2 live SSH = `UNVERIFIED`。L1 P3 收口目录不能关闭产品 AC13/14/15/19/21/22/23/24/26。`phase_gate=not_passed`。 |
| `hd-027-l2.json` | L2 FS/SSH/TOCTOU = `UNVERIFIED`。ADR accepted wire only。L1 不能关闭产品 AC30/AC34。`phase_gate=not_passed`。 |
| `hd-027-packages.json` | HD-027 Rust 探针。无额外 crate。toolchain 1.98.0。不是 AC30/AC34 通过。 |
| `hd-028-l2.json` | L2 live FS/SSH/TOCTOU = `UNVERIFIED`。无 Integration.Ssh/Windows。AC30/AC31/AC32/G0 false。 |
| `hd-028-packages.json` | HD-028 crate 探针。零额外 Cargo crate。toolchain 1.98.0。 |
| `hd-029-l2.json` | L2 live UI/SSH = `UNVERIFIED`。无 Integration.Windows。WinUI 未准入。AC31/AC33/G0 false。 |
| `hd-030-l2.json` | L2 live agent/IME/SSH = `UNVERIFIED`。无 Integration.Windows。WinUI 未准入。auto-submit false。AC35/AC31/AC32/AC36/G0 false。 |
| `hd-031-l2.json` | L2 live clipboard/IME = `UNVERIFIED`。无 Integration.Windows。WinUI 未准入。无 watcher。OSC 52 默认 deny。AC36/G0 false。 |
| `hd-032-l2.json` | L2 live FS/SSH/TOCTOU/attack = `UNVERIFIED`。L1 P4 收口目录不能关闭产品 AC31/32/33/34/35。`phase_gate=not_passed`。 |
| `hd-033-l2.json` | L2/L3/L4 = `UNVERIFIED`。`github_required_check=UNVERIFIED`。L2 采集器不是 live 通过。L2 Narrator overlay 不是 AC37 或屏幕阅读器证据。L2 当前系统 DPI overlay 不是 AC38，也不是 100/150/200 矩阵。产品 UI soak START 不是 AC46。中断的 START 不是 8h 完成。L1 P5 收口目录不能关闭产品 AC27/28/29/37/38/46。parser consumed 不是呈现。`phase_gate=not_passed`。 |
| `hd-034-l2.json` | L2/L3 live 安装/签名/更新/回滚 = `UNVERIFIED`。L2 未签名 lab layout 叠加与 lab 证书 overlay 不能关闭产品 AC41/AC42。PFX gitignored。假 Publisher 不能通过 AC41。`App.xaml` 已存在。`packaging_project` 仍为 false。`phase_gate=not_passed`。 |
| `hd-035-l2.json` | L2 工作树 auditor overlay；L2/L3 live 扫描/renderer/canary/已签名包反向审计 = `UNVERIFIED`。不能关闭产品 AC02/AC43/AC44。缺扫描不是零漏洞。`phase_gate=not_passed`。 |
| `hd-036-l2.json` | L2 hosted-workflow pointer overlay；L2/L3/L4 live 走查/矩阵/SHA 绑定/干净还原/required-check/图重跑/发布/签名 hash = `UNVERIFIED`。Hosted workflow 不是 required-check。L1 P5 收口目录不能关闭产品 AC39/AC40/AC45/AC47/AC48。未发布。不是完整 1.0。`phase_gate=not_passed`。 |

这些文件多数来自 2026-09-07 Linux 实施环境。不要用其中的 `csharp_compiled=false` 覆盖已经通过的 CI。
