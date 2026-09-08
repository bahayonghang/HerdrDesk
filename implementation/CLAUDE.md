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

这些文件多数来自 2026-09-07 Linux 实施环境。不要用其中的 `csharp_compiled=false` 覆盖已经通过的 CI。
