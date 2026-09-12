# 授权后的 observe 接线

Parent: `.trellis/tasks/09-12-herdr-workbench-gap`. **Do not `task.py start` until the user grants live herdr observe.** Default `--ui` stays disconnected.

## Goal

在明确授权后，生产组合根可以对本机已保存 Device/Session 做 observe 级连接，把 snapshot 填进 catalog，mosaic 才能显示真终端。默认路径仍然 Unavailable，不得伪成功。

## Background

`CreateProduction` 使用 `UnavailableAdapter`（`AppServices.cs:74-76`）。`RpcStdioConnectionFactory` 已存在（`src/HerdDesk.Infrastructure/Rpc/RpcStdioConnectionFactory.cs`）。`ShellHost` 固定 `DaemonAvailable = false`。HD-008 L2 named-pipe ACL 仍 UNVERIFIED。两条平面：JSON RPC vs `herdr terminal session` stdio。

## Requirements

- **R1**：默认 `--ui` / `CreateProduction` 行为不变（Unavailable，`!DaemonOnline`）。
- **R2**：opt-in 仅在用户授予后启用；仍禁止发送用户输入、takeover、replay。
- **R3**：`PendingConnects` 在授权路径被消费：成功则 `DaemonAvailable` 来自真实 ping/snapshot；失败则 `DaemonUnavailable` + 可重试。
- **R4**：退出只杀 owned bridge/CLI。不停止 herdr daemon/agent/pane。
- **R5**：CI 不启动 herdr 或 `--ui`。工厂选择用单测覆盖。

## Acceptance Criteria

- [ ] **AC1**：无 opt-in 时 Unavailable 计数与现网一致，无假 Ready。
- [ ] **AC2**：opt-in 测试替身（非 `IsFakeSuccess` 生产路径）证明 Connect 会尝试打开 RPC 平面并更新 lifecycle。
- [ ] **AC3**：产品 AC / G0 保持未通过。Live 成功不是本任务 CI 门禁。

## Out of scope

SSH、helper 部署、控制权授予、文件桥、输入回放、把 preview 写成 `compatible_by_default`。
