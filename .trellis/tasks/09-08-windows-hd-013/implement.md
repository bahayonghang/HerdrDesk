# HD-013 implementation plan

以下步骤仅在计划获批、HD-008/010 已完成且 HD-004 evidence boundary 明确后执行；当前不启动 CLI。

## Ordered checklist

1. [ ] 冻结 HD-004 argv/help、wire fixture、control signal matrix 与 executable hash；未知 ownership signal 明确保持 Unknown。
2. [ ] 增量定义 terminal factory/open request/events/write receipt ports，并迁移现有 parser 类型而不破坏 Contracts→BCL、Core→Contracts。
3. [ ] 实现固定 `ProcessStartInfo` 与 direct-child ownership record；为每个 transport 创建独立 epoch parser 和一次性 lifecycle latch。
4. [ ] 实现 chunk-safe stdout NDJSON pump、item/byte budget 与 owned frame lifetime；先覆盖 full/delta/closed/EOF/protocol/backpressure。
5. [ ] 实现 nonblocking stderr drainer、redacted classifier 与 flood aggregation；验证 raw stderr/input 不进入默认诊断。
6. [ ] 实现 typed serializer、bounded byte queue 和 single writer；为 input/resize/scroll/release 建 queued/writing/flushed disposition。
7. [ ] 实现 closing/release/dispose 顺序及 direct-child-only timeout fallback；所有竞争出口汇入一个 terminal end event。
8. [ ] 增加 fake child executable/harness，精确控制 stdout chunk、stderr flood、stdin capture、exit 与 cancellation barrier。
9. [ ] 接入 HD-014/016 前只暴露 typed ports；交付 PaneKey/epoch/attempt verification 与 no-ACK contract。
10. [ ] 更新未来 `just ci` 的 Infrastructure unit/contract targets；只有获批 disposable pane 才执行 L2。

## Key assertions

- 任意 chunk boundary、CR/LF、16 MiB 边界和 EOF 点都产生确定 framing 结果；每个 epoch 的 parser 状态互不共享。
- 100 concurrent calls 的 fake stdin 逐行可解析、无交错、每个 command id 最多一次；断连后 queue=0 且没有 retry task。
- stderr 产生大于 pipe capacity 的数据时 child 不死锁，诊断内存有界；terminal bytes 与 input payload 不出现在 log capture。
- release/write/EOF/process-exit 同时发生时仅一个终态；release 后 enqueue 被拒，fake daemon pid 的 kill count 恒为 0。
- observe frame/process/focus fixture 的 `ControlVerified` 始终 false；未知 classifier 不自动提升状态。

## Proposed commands after projects exist

```powershell
dotnet test tests/Unit/HerdDesk.Infrastructure.Tests/HerdDesk.Infrastructure.Tests.csproj --configuration Release --no-build --filter TerminalCliTransport
dotnet test tests/Contract/HerdDesk.ContractTests.csproj --configuration Release --no-build --filter TerminalWire
just ci
```

L2 命令必须使用已授权 disposable target 和记录 hash 的本机 herdr binary。本任务直接收集 observe baseline/seq/尺寸/Base64/EOF以汇总AC05；真实resize/一次输入/release结果交HD-019汇总AC06，pane/daemon/agent存活交HD-026汇总AC15。没有对应证据时只报告L1 PASS，相关原AC保持 `not_run`。

## Evidence and rollback

L1 保存 fixture provenance、seed/chunk plan、stdin command ids/dispositions、peak byte budget、direct-child pid/kill ledger；不保存终端正文。L2 保存脱敏 frame/exit/signal 摘要和 target disposal record。回滚顺序为关闭 control writes→关闭 resize/scroll→仅 observe；不得用停止 daemon 或关闭 pane 回滚。
