# HD-033 性能、可访问性与 soak 证据索引

L1 收口目录。指向已交付的 HD-023 搜索 p95 单元、HD-025 队列/准入、HD-011 ViewModel 产物。不是产品 AC 通过，也不是 live 8h soak / 输入到可见像素 / DPI / Narrator / working-set 证明。

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
| Narrator | AC37 | HD-011 | ViewModel 名称；无 WinUI / Narrator | `not_run` |
| DPI 100/150/200 | AC38 | HD-011, HD-014 | `TerminalDisplayCoordinator`；L3 DPI UNVERIFIED | `not_run` |
| 8h soak | AC46 | HD-025, HD-018 | 队列与恢复政策；未执行 8h | `not_run` |

Contract catalog 各条保持 `not_run`。不得发明 timings。不得把 parser callback 当作 input-to-pixel 通过。

## 支持矩阵

承诺范围：Windows 11 x64 客户端 + Linux x64 远端（远端输入延迟）。两行均为 `not_run`。禁止把 Windows 字段抄到 Linux。macOS / ARM64 无独立证据，标 `unsupported` 或 `experimental`，不得标 supported。

## live 行

| 行 | 状态 | 原因 |
|---|---|---|
| live cold start | `UNVERIFIED` / `not_run` | 无授权交互桌面冷启动 |
| live input-to-pixel | `UNVERIFIED` / `not_run` | 无授权可见像素探针；parser consumed 不是呈现 |
| live search p95 | `UNVERIFIED` / `not_run` | 内存搜索不是 live p95 |
| live working set | `UNVERIFIED` / `not_run` | 无授权进程采样；不得从 `Q_p` 推导 |
| live handle reclaim | `UNVERIFIED` / `not_run` | 无授权 100 次 hide/show |
| live Narrator | `UNVERIFIED` / `not_run` | WinUI 未准入；无 Narrator 桌面 |
| live DPI/theme | `UNVERIFIED` / `not_run` | 无授权 DPI/主题矩阵 |
| live 8h soak | `UNVERIFIED` / `not_run` | 本派遣未执行 8h soak |

残差 JSON：`implementation/hd-033-l2.json`。`l3_ime` / `l3_narrator` / `l3_dpi` / `l4_soak` 为 `UNVERIFIED`。产品 AC27/AC28/AC29/AC37/AC38/AC46 仍为 `not_run`。`tests/Integration.Windows` 不存在。
