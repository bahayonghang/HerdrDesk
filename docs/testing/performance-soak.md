# HD-033 性能、可访问性与 soak 证据索引

L1 收口目录加 L2 采集器。指向已交付的 HD-023 搜索 p95 单元、HD-025 队列/准入、HD-011 ViewModel 产物，以及环境/冷启动/搜索/短窗 idle 采集器。L2 Narrator overlay（`scripts/collect_narrator_overlay.py`，`evidence/quality/narrator-overlay-pointer.json`）只记录 Narrator.exe 是否存在与是否已启动；不是 AC37，也不是屏幕阅读器证据；overlay 不启动 Narrator。本机产品 UI `--ui` + Narrator.exe 启动记录见 `evidence/quality/narrator-product-ui-launch.json`；不是 `live_narrator` 成功，也不是 AC37。已交付 AutomationProperties 名称不是 Narrator 通过。L2 当前系统 DPI overlay（`scripts/collect_dpi_overlay.py`，`evidence/quality/dpi-overlay-pointer.json`）只记录 `GetDpiForSystem` 当前值；不是 AC38，也不是 100/150/200 矩阵。本机产品 UI `--ui` 8h soak START 见 `evidence/quality/live-soak-start.json`；不是 `eight_hour_soak_executed`，不是 `soak_hours=8`，不是 AC46。`--record-elapsed` 在墙钟未满 8h 或 START PID 不匹配时 fail-closed（`eight_hour_wall_clock_incomplete`），不写成功 `live-soak-elapsed.json`；该文件可不存在。可选 soak 进程 working-set overlay 见 `scripts/record_soak_working_set.py` 与 `evidence/quality/live-soak-working-set.json`；默认不 `--record`；采样已运行 START App PID，不另启 `--ui`；不是 1/4 pane、不是 100 次 open/close、不是 AC29/AC46。`live-working-set.not-run.json` 仍是 1/4 pane lab not_run。先前 START 中断见 `evidence/quality/live-soak-interrupted.json` 与 `evidence/quality/live-soak-interrupted-2.json` 与 `evidence/quality/live-soak-interrupted-3.json` 与 `evidence/quality/live-soak-interrupted-4.json` 与 `evidence/quality/live-soak-interrupted-5.json` 与 `evidence/quality/live-soak-interrupted-6.json`；中断不是 8h 完成。不是产品 AC 通过，也不是 live 8h soak 完成 / 输入到可见像素 / DPI / Narrator AC37 工作流 / working-set 证明。

权威机器文件：

- `evidence/quality/catalog.json`
- `evidence/quality/support-matrix.json`
- `implementation/hd-033-l2.json`

`phase_gate=not_passed`。`g0_passed=false`。`ac27_passed` / `ac28_passed` / `ac29_passed` 为 false。模板文件 `template=true`，空 hash / 退出码，不是一次成功运行。parser consumed 不是 GPU 或可见像素呈现。不得从 `Q_p` 推导进程工作集。MiB = 1,048,576 bytes。hosted CI 不是交互桌面。

## 执行卡

| 场景 | AC | owner | L1 产物 | live |
|---|---|---|---|---|
| cold start | AC28 | HD-011 | `ShellViewModel` / 组合根宿主 stub | `not_run` |
| input-to-visible-pixel | AC28 | HD-014, HD-015 | `RenderFlowController` ack 与 `TerminalInputController`；callback 不是呈现 | `not_run` |
| search p95 | AC28 | HD-011, HD-023 | in-memory `GlobalSearchPerformanceTests`；不是 live p95 | `not_run` |
| working set 1/4 pane | AC27, AC28 | HD-025 | `TerminalQueueBudget` / `DirtySetBudget`；不得从 `Q_p` 推导进程内存 | `not_run` |
| 100 hide/show | AC29 | HD-025, HD-011 | `PaneVisibilityCoordinator`；无 live 句柄 | `not_run` |
| Narrator | AC37 | HD-011 | ViewModel 名称；产品 UI `--ui` + Narrator.exe 已启动；AC37 工作流未完成 | `not_run` |
| DPI 100/150/200 | AC38 | HD-011, HD-014 | `TerminalDisplayCoordinator`；L3 DPI UNVERIFIED | `not_run` |
| 8h soak | AC46 | HD-025, HD-018 | 队列与恢复政策；产品 UI `--ui` soak START 曾中断六次；新 START 须新墙钟且 PID 不相交；8h 墙钟未满；100 次断连/切换未跑 | `not_run` |

Contract catalog 各条保持 `not_run`。不得发明 timings。不得把 parser callback 当作 input-to-pixel 通过。

## 支持矩阵

承诺范围：Windows 11 x64 客户端 + Linux x64 远端（远端输入延迟）。两行均为 `not_run`。禁止把 Windows 字段抄到 Linux。macOS / ARM64 无独立证据，标 `unsupported` 或 `experimental`，不得标 supported。

## live 行

| 行 | 状态 | 原因 |
|---|---|---|
| live cold start | `UNVERIFIED` / `not_run` | 无授权交互桌面冷启动 |
| live input-to-pixel | `UNVERIFIED` / `not_run` | 无授权可见像素探针；parser consumed 不是呈现 |
| live search p95 | `UNVERIFIED` / `not_run` | 内存搜索不是 live p95 |
| live working set | `UNVERIFIED` / `not_run` | 1/4 pane WebView lab 未跑；soak 进程样本不是该 lab；不得从 `Q_p` 推导 |
| live handle reclaim | `UNVERIFIED` / `not_run` | 无授权 100 次 hide/show |
| live Narrator | `UNVERIFIED` / `not_run` | 产品 UI + Narrator.exe 已启动；AC37 搜索/控制/释放/关闭确认未完成（无 live session） |
| live DPI/theme | `UNVERIFIED` / `not_run` | 当前系统 DPI overlay 已记录；100/150/200 矩阵未跑 |
| live 8h soak | `UNVERIFIED` / `not_run` | 先前 soak START 已中断（09:49、11:11、11:35、11:56、12:19、12:49）；新 START 须晚于这些中断；8 墙钟小时未满；100 次断连/切换与 live herdr/SSH 故障注入未授权 |

残差 JSON：`implementation/hd-033-l2.json`。`l3_ime` / `l3_narrator` / `l3_dpi` / `l4_soak` 为 `UNVERIFIED`。`github_required_check` 为 `UNVERIFIED`。产品 AC27/AC28/AC29/AC37/AC38/AC46 仍为 `not_run`。WinUI 四区 Shell 已在树中。`tests/Integration.Windows` 是 net10.0 控制台 runner，不启动窗口。L2 采集器不是 live 通过。L2 Narrator overlay 不是 AC37。产品 UI `--ui` + Narrator.exe 启动记录不是 AC37。L2 当前系统 DPI overlay 不是 AC38，也不是 100/150/200 矩阵。产品 UI `--ui` soak START 不是 AC46。中断的 START 不是 8h 完成。soak 进程 working-set overlay 不是 AC29，也不是 1/4 pane lab。
