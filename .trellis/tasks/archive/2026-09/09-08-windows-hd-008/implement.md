# HD-008 implementation plan

以下均为任务获批后的步骤；当前不执行 Cargo restore、bridge 或 endpoint 连接。

## Ordered checklist

1. [ ] 读取 HD-003 的 runtime endpoint matrix 和 HD-007 的 dependency/process contracts；逐项标出可实现、Unknown 和 blocked。
2. [ ] 建立最小 Rust workspace/binary；核验 `interprocess` exact version/API/许可，真实生成 `Cargo.lock`，不复制上游业务代码。
3. [ ] 实现 cfg-specific endpoint mapping、纯字节 relay、Unix write-half shutdown、Windows whole-process cancellation 和稳定 stderr codes。
4. [ ] 建立 Rust fake local-socket peers，先验证双向随机 bytes/fragmentation，再验证 EOF、slow reader、partial write、error 和 Unicode path。
5. [ ] 定义 request/subscription 分离 ports 和 raw envelope ownership；不把规划草案整文件覆盖进 Contracts。
6. [ ] 实现 OwnedChildProcess、bounded NDJSON、request writer/reader/pending map；确认 cancellation 三条竞态都 remove exactly once。
7. [ ] 实现独立 subscription process、ack gate 和 bounded cloned event stream；overflow/EOF 通知 HD-010 resync。
8. [ ] 添加 C# fake bridge executable/streams 的 contract tests，覆盖 stderr flood、banner pollution、child hang、dispose 和 late old-epoch response。
9. [ ] 仅在用户另行授权后，在 disposable Windows endpoint 跑 EP01-EP05；不发送 mutation 或 terminal input。
10. [ ] 把 Cargo fmt/clippy/test、C# RPC contract tests 接入 HD-007 的 `just ci`，并更新实际 module specs/indexes。

## Key assertions

- 任意 binary payload 经 relay 的 SHA-256/length 相同，stdout 零额外字节；每方向常驻 buffer≤64 KiB，慢端不触发线性内存增长。
- Unix upload EOF 后仍收到 peer tail；Windows case 显式标记 no portable half-close，cancel 后 bridge direct child≤3s 退出。
- 10,000 个乱序 responses 各匹配一次；cancel/timeout/disconnect 后 pending=0，unknown/duplicate id 不完成其他 caller。
- subscribe ack 前不宣告 Ready；event queue overflow/invalid JSON/EOF fail-closed；每个 yielded JSON 独立于 reader document lifetime。
- 进程退出路径只针对记录的 bridge PID，测试 sentinel daemon/process 不受影响。

## Proposed commands after files exist

```powershell
cargo fmt --manifest-path bridge/Cargo.toml --all -- --check
cargo clippy --manifest-path bridge/Cargo.toml --workspace --all-targets --locked -- -D warnings
cargo test --manifest-path bridge/Cargo.toml --workspace --locked
dotnet test tests/Unit/HerdDesk.Infrastructure.Tests/HerdDesk.Infrastructure.Tests.csproj --configuration Release --no-build --filter Rpc
dotnet test tests/Contract/HerdDesk.ContractTests.csproj --configuration Release --no-build --filter Rpc
just ci
```

L2 Windows endpoint 命令必须由 HD-003/007 实际生成的 test harness 提供，未创建前不在计划中虚构 CLI。现有 `probe_herdr.py` 不包含通用 RPC call/subscribe，不能代替该 harness。

## Evidence and rollback

- L1：Rust loopback/fake listener、C# fake child、pending/subscribe/EOF/backpressure contract tests。
- L2：经批准的 Windows 11 + pinned herdr API endpoint，记录 mapping、ACL、process cleanup；没有授权即 `UNVERIFIED`。
- 回滚先从 DI/gate 移除 RPC adapter，再终止其 direct bridge；Store 标记 stale/非实时。保留 locks 与失败证据供审查，不停止 daemon、不删除 endpoint marker。
