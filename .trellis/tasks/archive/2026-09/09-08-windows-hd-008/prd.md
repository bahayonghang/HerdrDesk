# HD-008 · RPC stdio bridge 和请求层

状态：`planning`。本任务不连接真实 endpoint，不启动 bridge。

## Goal

交付一个只做 platform socket↔stdio 字节转发的 Rust `herddesk-bridge`，以及 C# 中严格分离 request 与 subscription 的 RPC transport；在取消、EOF、慢消费者和晚到响应下保持内存有界、连接可回收且不误用 terminal plane。

## Confirmed facts

- 上游 v0.9.0（commit `b99002a`）Windows listener 仍用 `Path` 字符串经 `GenericNamespaced` 映射，Unix 用 `GenericFilePath`；marker 文件不是可读 RPC 流，且 private listener DACL 不能外推到每个 API endpoint：herdr `src/ipc.rs:35-77`；历史对照 `docs/source-verification.md:3-12`。
- JSON RPC schema `protocol=22` / `schema_version=1`。lifecycle `events.subscribe` 不回放保留事件，须先订阅再 snapshot：`.trellis/tasks/09-08-windows-desktop-full/research/herdr-0.9.0.md`。
- endpoint generation 1 与二进制 `PROTOCOL_VERSION` 独立；本任务默认仍转发 API socket 字节，不实现 `endpoint.hello.v1`，除非父任务开放决策改为默认路径。
- 自有 bridge 不是 herdr 现有命令，只转发字节、不解析业务、不创建 shell、不监听 TCP：`docs/plan/docs/03_架构与数据流.md:52-60`、`docs/plan/docs/05_协议与接口契约.md:50-56`。
- 常态下同一 session 使用独立 request 与 event subscription 连接；pending response 只可应用于当前 epoch：`docs/plan/docs/03_架构与数据流.md:54-54`、`docs/plan/docs/03_架构与数据流.md:78-82`。
- AC03/AC04 原文在 `planning/acceptance.json:21-34`，当前均为 `not_run`；HD-003 只完成 endpoint 事实输入，生产 bridge 仍归本任务。

## Requirements

- R1：实施依赖 HD-003 提供已验证 endpoint kind/path/ACL 结果及 HD-007 的项目、依赖、诊断和 owned-process 基础；未知映射时只接受显式配置，不猜 `%APPDATA%`。
- R2：`herddesk-bridge rpc --socket-path <path>` 的 stdin/stdout 必须逐字节透明；stdout 只能出现 peer bytes，所有稳定诊断走 stderr，`--version` 是独立命令，无 welcome/handshake/banner。
- R3：Windows 必须复现 pinned 上游的 `Path→to_string_lossy→GenericNamespaced` 名称映射且不打开 marker；Unix 使用 filesystem Unix-socket name。binary client socket 或远程 SMB pipe 输入必须拒绝。
- R4：两向 relay 各只有固定大小 buffer，让 pipe/socket 写阻塞形成自然背压；禁止 `ReadToEnd`、无界 channel 或把协议数据转成 String。
- R5：Unix 在 stdin EOF 后对 socket write half 执行真实 `Shutdown::Write` 并继续把反向尾数据写到 stdout；Windows named pipe 没有可移植 half-close，不得把 interprocess 的 no-op shutdown 宣称成功，连接结束由 peer EOF 或父进程取消整个 owned bridge。
- R6：C# 用两个独立 bridge 子进程/连接实现 `IRpcRequestConnection` 与 `IRpcSubscriptionConnection`；subscription 不能借 request pending map，多次 snapshot 不能冒充事件流。
- R7：request id 在连接内唯一；写入先登记 pending，response 精确匹配一次。取消/超时删除 pending；晚到或未知 id 只生成脱敏诊断，绝不写入新 epoch 或完成其他请求。
- R8：subscription 必须先收到同一 request id 的成功确认，随后才发布 cloned/owned event payload；error、协议污染、line overflow、EOF 均结束该 epoch，不返回悬挂 `JsonElement`。
- R9：所有子进程使用核验过的绝对路径和 `ArgumentList`，禁止 shell；取消最多等待 3 秒后仅终止 bridge direct child，不 stop daemon、不 kill process tree。
- R10：本任务只提供 raw RPC envelope/transport；schema DTO 与 Store 投影由 HD-009，订阅收敛由 HD-010，业务 mutation allowlist 由 HD-017。

## Acceptance criteria

- AC1（R2-R5）：跨平台 relay contract tests 对任意二进制分块保持双向字节完全一致；慢读端时内存不随输入增长，stderr 不污染 stdout。
- AC2（R3；原 AC03 贡献）：default/named/explicit/Unicode 和拒绝案例全部消费 HD-003 的 endpoint matrix；Windows 真机连接到预期 API pipe 且 ACL/错误用户返回稳定类别。最终 AC03 需合并 HD-003 runtime 与本任务 L2 证据。
- AC3（R5）：Unix stdin EOF→write-half close→反向尾数据→peer EOF 顺序通过；Windows 测试明确 whole-connection cancellation≤3s，不产生虚假的 half-close pass。
- AC4（R6/R7）：并发请求 response 可乱序但各完成一次；cancel/timeout 后 pending 数归零，晚到 response 不复活 task，断线后旧 epoch response 不应用。
- AC5（R6/R8；原 AC04 贡献）：request 与 subscribe 使用两个 OS process/连接；订阅确认前事件按协议处理，确认后 error/unknown fields 可交给 HD-009 decoder，EOF 结束枚举。
- AC6（R4/R8）：16 MiB 行上限、严格 UTF-8/JSON envelope、重复 id/error+result/未知 response id 等故障均 fail-closed，错误不含 raw JSON、endpoint、terminal 或凭据。
- AC7（R9）：正常 dispose、parent cancel、peer EOF、stderr flood 和 child hang 后没有 owned bridge 残留；测试证明未调用 server stop 或整树 kill。

## Out of scope

- 不实现上游业务 DTO、Store、资源命令、terminal session、SSH transport、helper 部署、UI 或 daemon lifecycle。
- 不声称 Windows named pipe 支持 Unix 式 half-close，不新增 TCP listener，不运行 live herdr/SSH；L2 需要后续用户明确授权的隔离 endpoint。

## Blocking gate

HD-003 的 endpoint type/mapping 仍 Unknown 或 HD-007 未锁定 Rust/C# 工具链时不得实施生产连接。若 upstream schema 没有可验证 subscribe 确认形态，则本任务停在 request-only blocked，不伪造实时状态。
