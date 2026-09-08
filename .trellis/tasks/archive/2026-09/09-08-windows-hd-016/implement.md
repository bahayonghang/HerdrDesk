# HD-016 implementation plan

以下步骤仅在计划获批且 HD-013/014 ports 已实现后执行；当前不请求 control 或 takeover。

## Ordered checklist

1. [ ] 固定 HD-004 ownership/busy/release evidence 与 HD-013 receipt；证据不足时把 control capability定义为 unavailable/Unknown。
2. [ ] 增量定义 lease state、intent/outcome、public challenge view 与 binding-host port，确认 public API 无 raw argv、nonce 或 resource command。
3. [ ] 用 pure transition table覆盖五个 TerminalAccess 状态、busy outcome、attempt/generation/epoch guards；先写 exhaustive table test。
4. [ ] 实现 bounded actor、current observe和单 candidate ownership；candidate frame复用 HD-014 byte budget，不混流。
5. [ ] 实现 no-takeover acquisition四重 gate与 promotion；所有 failure先 readonly，再回现有 observe或 Unknown。
6. [ ] 实现 busy-bound one-shot challenge、target confirmation UI 与 takeover-only typed authorization；验证 stale/reuse/selection-change拒绝。
7. [ ] 实现 input/resize/scroll current-context gate、receipt mapping 与 memory-only ledger；保留 EmulatorReply默认拒绝。
8. [ ] 实现 release、lost-ownership、EOF、dispose和 HD-018 RecoverObserve入口；每条路径递增 generation并清旧队列。
9. [ ] 将 TerminalControlViewModel/ControlBar 接到 HD-011 shell和 HD-014 readonly/bind/ack，不在 XAML code-behind复制策略。
10. [ ] 用 fake Store/transport/renderer跑交错测试；只有获批 disposable pane 才做双 observer/controller L2。

## Key assertions

- 普通 RequestControl 构造的 fake transport `TakeoverAuthorized=false`；仅有效 challenge确认产生一次 true。
- first frame/process alive/focus/renderer ready 的任意排列都不能单独设置 verified；late proof 不改变新 attempt。
- acquire 期间 renderer只收到 observe epoch；promotion 从 candidate full baseline重绑，随后才 readonly=false。
- release/EOF与 1,000 inputs交错时旧 generation无 replay，payload capture每项最多一次，NotSent/Unknown可解释。
- challenge在 pane/session/device/epoch/revision/busy id任一变化后不可用，日志中没有 handle/nonce。
- public coordinator surface不包含 create/rename/close/exec/approval bypass；resource操作测试不引用 terminal lease。

## Proposed commands after projects exist

```powershell
dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj --configuration Release --no-build --filter TerminalLease
dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj --configuration Release --no-build --filter ControlLease
just ci
```

Windows integration target可用 fake child离线运行。AC16由本任务汇总L1默认参数/无bypass断言与获批本地L3目标确认UI证据。真实AC07双client场景交HD-019，跨断网AC14交HD-026；未执行时标 `UNVERIFIED`，不能用fake替代。

## Evidence and rollback

L1 记录 transition cases、attempt/generation、fake ownership provenance、renderer readonly顺序、command disposition和零 replay断言；不记录输入。L2 记录 binary hash、目标 disposable证据、两 client状态与脱敏结果。回滚为禁用 takeover→禁用 control→所有 pane强制 observe；不停止 daemon、不关闭 pane。
