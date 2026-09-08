# HD-010 implementation plan

以下步骤仅在计划获批、HD-009 完成后执行；当前不启动 actor 或连接。

## Ordered checklist

1. [ ] 固定 HD-008 request/subscription failure contract 和 HD-009 snapshot/event/capability/Store API；把未验证 getter 标成 full-resync。
2. [ ] 定义 DeviceSession public state、actor message 和 options；用一张 transition table覆盖 Offline/Connecting/Synchronizing/Ready/Stale/Incompatible。
3. [ ] 实现 bounded single-reader mailbox、epoch/operation guards 与 effect supervisor；先用无网络 fake 验证串行 mutation。
4. [ ] 实现 subscribe ack→snapshot→dirty generation→reconcile 流程，再添加 250 ms coalescing 和 5 s calibration。
5. [ ] 实现 entity getter/full-snapshot planner；定向 merge invariant 失败必须升级 full snapshot。
6. [ ] 实现 manual disconnect、request/subscription failure、schema incompatible 和 disposal；确保先 stale/readonly 后取消异步 effect。
7. [ ] 建 race-model tests，以 barrier 精确放置 event/snapshot/epoch 切换，不依赖 sleep；时间测试使用 injected TimeProvider。
8. [ ] 接入只读 state stream，验证 UI 合并不影响 actor event processing；明确 baseline 不发 notification。
9. [ ] 更新 HD-007 gate 与受影响 specs/indexes，并向 HD-011/012/018 交付状态/freshness contract。

## Key assertions

- subscribe 未 ack 时 snapshot 不发、Ready 不出现；snapshot 飞行中 dirty 不丢，最终 projection 等于 fake authority。
- completion/event/timer 各在 epoch rollover 前后注入，旧 epoch 的每种 message 对新 Store 均为 no-op。
- 10,000 event burst 后在途 timer/read 数有固定上限；quiet 后 dirty=0，dispose 后 task/connection/timer=0。
- unknown event/getter failure 触发 full snapshot；安装失败时旧 graph 保留但 freshness=Stale/Refreshing。
- 首次 snapshot 不触发 done 通知；request/sub EOF 分开诊断并立即清 capabilities。

## Proposed commands after projects exist

```powershell
dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj --configuration Release --no-build --filter DeviceSession
dotnet test tests/Contract/HerdDesk.ContractTests.csproj --configuration Release --no-build --filter Synchronization
just ci
```

生产 `events.subscribe` 交错是本任务负责的 L2：使用 HD-003/008已验证endpoint与HD-009 actual schema，在获批的可丢弃session内把create/close/status安排到snapshot窗口。若需mutation，实施前列出精确方法、目标、授权和清理；未获授权不运行，也不以fake race关闭原AC12。HD-019仅复跑跨UI候选。

## Evidence and rollback

L1 记录 transition/race case、seed、最终 graph hash、pending task/timer count；L2 记录实际 schema/hash、event 操作、snapshot 结果和脱敏状态，不收集 terminal 正文。回滚按 entity reconcile→periodic calibration→subscription 三层收缩，最低状态为用户触发 snapshot + Stale，不停 daemon、不写 server。
