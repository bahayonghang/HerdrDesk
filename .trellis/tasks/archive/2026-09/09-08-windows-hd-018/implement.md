# HD-018 implementation plan

以下步骤仅在计划获批且 HD-010/013/016 完成后执行；当前不注入 live 故障。

## Ordered checklist

1. [ ] 冻结三个 owner 的 epoch/state/receipt接口和 failure taxonomy；画出 RPC owner与terminal owner的唯一写入边界。
2. [ ] 实现 pure RecoveryFailure/Decision/Backoff；用 injected TimeProvider/random验证1–30秒、jitter、attempt cap和不重试类别。
3. [ ] 扩展 DeviceSession：异常先Stale/clear capabilities/rollover，再单timer重连；复用原 subscribe→snapshot→dirty converge，不建第二Store。
4. [ ] 扩展 ControlLeaseCoordinator：projection stale或terminal failure先readonly/generation++/clear queue；只支持RecoverObserve。
5. [ ] 实现 ProjectionReady target revalidation和new observe/full baseline/renderer ack顺序；pane消失与同名替代写负例。
6. [ ] 接入 BaselineEstablished通知边界和 RecoveryViewState；验证历史done不补发、写按钮在fresh前统一disabled。
7. [ ] 实现 AppStopping latch、late process start cleanup和direct-child ledger；禁止daemon/service verb进入adapter。
8. [ ] 增加 deterministic barrier tests，覆盖每类旧completion、六个input断点、重复EOF、manual Retry与stop races。
9. [ ] 在统一 Windows integration project中用 fake bridge/CLI process验证pipe EOF、process exit和GUI-host harness，不连接真实server。
10. [ ] 汇总 P2 本地/通用恢复贡献；仅在明确授权后补 disposable本机daemon/GUI fault，把结果与未测网络/SSH边界交给 HD-026 最终验收。

## Key assertions

- 每个failure trace中 Stale/readonly序号小于timer/process start；任何时刻每session timer/reconnect均≤1。
- 100次epoch rollover后旧RPC/terminal/render/control completion对当前state hash为no-op，pending/timer/task最终为0。
- backoff fake clock精确覆盖1/2/4/8/16/30s与jitter bounds；incompatible/auth/user stop/app stop start-count=0。
- terminal恢复argv固定observe且takeover=false；新full前write-count=0，旧seq/receipt不释放新buffer或打开control。
- 断点注入后旧input bytes在新writer/config/log/state中零命中；outcome只为NotSent或ResultUnknown。
- stop/crash harness的kill ledger只含owned bridge/CLI pid；fake daemon/agent/pane始终可由独立client访问。

## Proposed commands after projects exist

```powershell
dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj --configuration Release --no-build --filter Recovery
dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj --configuration Release --no-build --filter Recovery
just ci
```

真实 daemon restart、network cut、一次可识别敏感输入和GUI强杀必须使用获批的 disposable session；命令、target和恢复动作由当次实验记录给出。HD-018没有live授权时以离线 Windows integration 完成其 P2范围；网络/SSH及产品最终 AC13/14/15证据由 HD-026汇总。

## Evidence and rollback

L1记录failure/epoch/event序列、fake-time delay、input disposition、queue/task峰值和state hash。Windows fake integration记录direct child pid与exit cause。L2/L3只保存脱敏状态、binary/schema hash及外部pane存活检查。回滚先撤自动retry保留manual reconnect，再降级为仅显示Stale；永不启动/停止daemon或重放输入。
