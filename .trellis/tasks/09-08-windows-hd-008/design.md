# HD-008 technical design

## Rust relay

拟建 `bridge/Cargo.toml` workspace、`bridge/herddesk-bridge/Cargo.toml`、`src/main.rs`、`endpoint.rs`、`relay.rs`、`error.rs` 和 `tests/relay_contract.rs`。Cargo direct dependency 只在实施时按 HD-007 流程核验并精确记录；`Cargo.lock` 由真实 build 生成后提交。bridge 不引用 C# DTO，也不包含 JSON parser。

`endpoint.rs` 接收单个 OS path。Windows 分支与 pinned upstream 一致：`PathBuf`→lossy string→`to_ns_name::<GenericNamespaced>()`；Unix 分支 `to_fs_name::<GenericFilePath>()`。`--socket-path` 为空、NUL、非本地 endpoint、错误 kind 时在 connect 前拒绝；错误只输出 `bridge_endpoint_invalid`、`bridge_connect_denied`、`bridge_connect_failed` 等类别。

`relay.rs` 使用一条 connected stream 的独立 recv/send handle 和两个固定 64 KiB pump，不缓存整行或解释编码。coordinator 收集 upload/download completion：download EOF 先 flush stdout 后退出；upload EOF 在 Unix shutdown write half 后继续等 download，在 Windows 只标记 upload closed 并等 peer EOF/parent termination。任一 I/O error 关闭本连接、让另一方向退出并返回非零。每次 write 必须处理 partial write；背压由同步 write/有限 buffer 向上游传播。

Windows `interprocess` local socket 的 portable shutdown 是 no-op，因此测试和日志不能写 `half_close_succeeded`。C# dispose 关闭 child stdin、等待 3 秒；仍存活则按保存的 direct PID/handle 终止 bridge 本身。Unix half-close 是平台专用行为。两端 socket EOF、stdin EOF、parent cancellation 是不同 relay outcome。

## C# ports and process adapter

拟在 `src/HerdDesk.Contracts/Rpc/RpcPorts.cs` 定义 `IRpcConnectionFactory`、`IRpcRequestConnection`、`IRpcSubscriptionConnection`、`RpcRequestId`、`RpcFailure`；在 `src/HerdDesk.Infrastructure/Rpc/` 实现 `RpcStdioConnectionFactory.cs`、`RpcRequestConnection.cs`、`RpcSubscriptionConnection.cs`、`RpcEnvelopeParser.cs`；共用 `Process/OwnedChildProcess.cs` 与 `Process/BoundedNdjsonReader.cs`。

factory 每个 `SessionKey/ConnectionEpoch` 启动两条独立连接，绝对 binary path 与 socket path 通过 `ProcessStartInfo.ArgumentList` 传递，`UseShellExecute=false`，stdin/out/err 全重定向。request id 使用 epoch + 单调 `ulong` 计数，不作为业务 ID；counter overflow 终止连接而非复用。

request connection 有一个 stdout reader、一个 bounded outbound channel 和一个 writer；`ConcurrentDictionary<RpcRequestId, PendingRequest>` 只由登记/reader/cancel 路径访问。登记发生在 enqueue 前；enqueue/write 失败原子移除并完成失败。response 必须二选一含 result/error，ID 精确匹配后先 remove 再 complete。调用方释放返回的 `JsonDocument`。

subscription connection 发送一个 subscribe envelope，先等待匹配 ack；ack 成功后 reader 将每个 event 的 `JsonElement.Clone()` 放入有界 async stream。subscription payload 不进入 request pending map。队列满触发连接失败和 HD-010 全量重同步，不能无界堆积或静默丢事件。

`BoundedNdjsonReader` 只切分 LF、拒绝超过 16 MiB/EOF 截断；`RpcEnvelopeParser` 严格 UTF-8、object、duplicate key、id/result/error 形态。它不解析 snapshot/event 业务字段。stdout 的任意 banner/stderr 合流会成为 `rpc_protocol_pollution`。

## Cancellation and error flow

caller cancellation 在 pending 未写时返回 `NotSent`；已 flush 到 bridge 后取消只返回 `CancelledAfterWrite`，不重发且不能推断 server 未执行。connection EOF 一次性完成所有 pending 为 `rpc_connection_lost`，dispose reader/writer/stderr tasks，再处置 direct child。旧 epoch 的 reader 即使晚完成也不能发布到 factory 的新连接。

stderr 从进程启动即由独立有界 reader drain；默认只映射 exit/error category 和长度，不复制正文。bridge exit code、stdout EOF、protocol error、caller cancel 分开。HD-010 只接收 typed failure/category，不接触 Process 或 raw stderr。

## Rollback

若 relay 或订阅不可靠，禁用 subscription/live projection，保留 G0 `api snapshot` 只读诊断并明确“非实时”；删除/停用的仅是自有 bridge direct child。不得改成把 JSON RPC 发到 terminal client socket，也不得以管理员运行绕过 ACL。
