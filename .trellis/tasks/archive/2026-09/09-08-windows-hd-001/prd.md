# HD-001 · 锁定上游事实与版本

## 目标

为完整 Windows 桌面端建立可审计的 herdr 兼容基线，不把源码阅读、历史 CI 或
合成 fixture 当作运行时兼容结论。本任务只规划证据采集和差异处置；不实现
bridge、UI、SSH 或控制输入。

## 已核实事实

- 当前 baseline 的证据等级为 `source_inspection_only`，记录 v0.8.2、commit、
  protocol 20 和 blob；runtime binary/schema hash 为空：
  `evidence/compatibility-baseline.json:3-28`。
- Windows local、remote Linux、named-pipe ACL、IME 均为 `not_run`：
  `evidence/compatibility-baseline.json:21-25`。
- G0 未通过；runtime CLI/daemon/schema、endpoint/ACL 和 renderer/IME 都是
  阻塞项：`implementation/status.json:2-36`。
- synthetic probe 不执行 herdr，也不测试 Windows GUI：
  `implementation/probe-selftest.json:2-7`。

## 需求

- R1：分开记录 source、synthetic、isolated Windows runtime、remote runtime 和
  UI manual evidence，包含环境、时间、命令、退出码、脱敏输出和限制。
- R2：区分 Git blob、分发 binary SHA-256 和 runtime schema SHA-256；禁止互推。
- R3：记录 stable/preview 差异和未知项；未知项必须使对应能力保持 blocked。
- R4：本机取证仅在获批的可丢弃 pane 内完成，未获授权时只审阅已有证据。
- R5：远端基线可在将来获批的外部测试环境直接采集，不等待 P3 产品代码，避免
  G0→P3→G0 的循环等待。

## 原 AC 映射与子验收

- AC01-C1（贡献）：静态 tag/blob/protocol 与 baseline 逐项核实，字段来源明确。
- AC01-C2（贡献）：Windows runtime 版本、daemon ping、schema/protocol、endpoint
  形态和 ACL 获得实际脱敏记录，或明确 `blocked` 的失败类别。
- AC01-C3（贡献）：remote 环境以独立条目记录等价事实或 `not_run`；不能借用
  Windows 结果。
- AC01（最终）：本机及远端均有 runtime 证据；client schema 不得充当 server 证明。

## 边界、风险和撤销

- 不修改产品状态、backlog、AC 或宣布 G0/AC01 passed。
- 版本/schema/endpoint 与静态证据冲突时，停止自动兼容推断，回到显式配置和
  只读诊断，并向 HD-003/004 传递差异。
- 任何需要非隔离 live 会话的操作都停止在计划阶段。
