# herdr v0.9.0 规划基线（2026-09-08）

规划决策（Q1–Q4 = A/A/A/A）：核心 1.0 **规划**上游钉 GitHub 稳定 tag **v0.9.0**。
不运行 `herdr channel set`，不停止用户 preview daemon。
IME / 真实 SSH L3 保持 UNVERIFIED。
1.0 shell 路径保持 **JSON RPC API socket** 与 **`herdr terminal session` stdio**；
`ENDPOINT_PROTOCOL_GENERATION = 1` 记为并行合同，HD-008/020 再定是否采用。

本文件是规划依据。它不把 G0、AC01–AC48 标为 passed，也不替代 `evidence/` 运行时记录。

## 来源

| 项 | 值 | 来源 |
|---|---|---|
| 稳定 tag | `v0.9.0`，`prerelease=false`，发布 2026-09-07T19:21:31Z | `gh release view v0.9.0 --repo herdrdev/herdr` |
| 二进制 protocol | `PROTOCOL_VERSION = 22` | `herdrdev/herdr@v0.9.0:src/protocol/wire.rs` |
| endpoint 代 | `ENDPOINT_PROTOCOL_GENERATION = 1` | `herdrdev/herdr@v0.9.0:src/protocol/endpoint.rs` |
| 网站 latest.json | 仍为 `0.8.2` / protocol `20` | https://herdr.dev/latest.json （滞后于 GitHub） |
| 本机 PATH | `0.9.0-preview.2026-09-08-62431dbd033b`，channel=`preview`，schema protocol 22 | 本机 `herdr --version` 与 `probe_herdr.py preflight` |
| 历史对照 | tag `v0.8.2` commit `9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c`，`api_protocol=20` | `evidence/compatibility-baseline.json` 源码行 |

preview `2026-09-08-62431dbd033b` 比稳定 tag 新约一天。规划钉 **稳定 tag**，preview 只作漂移行。

## v0.9.0 对牧台的合同变化

来自 GitHub release body / `CHANGELOG.md` `[0.9.0] - 2026-09-07`：

1. 新 lifecycle 订阅从 live 事件开始，不回放保留历史。API 客户端应 **先 subscribe 再 snapshot**（#1270）。
2. 多客户端可独立看不同 workspace/tab；同 tab 最后交互者控制尺寸（#3526）。
3. 终端 UI 在每个 client 上跑（#3487）。
4. client 更新可保留兼容 server 与 running agent；早于 endpoint generation 1 的 server 需一次性升级（#3509）。
5. pane graphics 默认开启；`terminal.kitty_graphics = false` 可关。
6. `herdr machine` 管理 Local 与已保存 SSH 机器（#3670）。

`endpoint.rs`：generation 1 是 Local/SSH/Cloud **client-owned shell** 的稳定 JSON 合同，与 same-install 二进制 `PROTOCOL_VERSION` 独立。新 JSON 字段须 optional/default；未知 enum 须 `Unknown`。

## 对已归档 G0 子任务的规划含义

| 任务 | 含义 |
|---|---|
| HD-001 | 源码矩阵增加 v0.9.0/protocol 22 行。本机 preview 已记录，不得标为与 v0.8.2/20 兼容。稳定 tag 二进制本机未安装，runtime hash 待 named session 或用户切 stable 后再采。 |
| HD-003 | 显式 endpoint 规则不变。ACL 仍缺 L2。 |
| HD-004 | subscribe-before-snapshot 进入 HD-008/013 设计。stdio 帧桥仍是 1.0 终端路径。 |
| HD-005 | IME L3 仍 UNVERIFIED。graphics 默认开启须在 renderer 安全边界写清忽略/关闭策略。 |
| HD-006 | ADR-003 保持两平面。endpoint generation 1 不替换 1.0 默认路径。R5 阶段规则同步仍等 G0 目标证据。 |
| HD-007+ | 仍不得用建仓跨越 G0 证据门。P1 在 IME/ACL/远端/稳定 tag 运行时证据未接受前不 start。 |

## 明确未做

- 未 `herdr channel set`
- 未停止用户 daemon/agent
- 未把 `planning/acceptance.json` 或 `phase_gate` 标 passed
- 未把 preview 二进制当作稳定 tag 证明
- 未把 herdr.dev/latest.json 的 0.8.2 当作当前 GitHub latest
