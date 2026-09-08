# HD-025 · 实施计划

## 启动条件

- [ ] `implementation/status.json` 已权威记录 `phase_gate=passed`，HD-023/024 与待实施
  HEAD 的 `just ci` 已通过，且用户明确批准启动本子任务。
- [ ] 届时重新读取根/模块 `AGENTS.md`、`CLAUDE.md` 和实际 backend/frontend specs。
- [ ] 核对 HD-014/022 已交付的 renderer 回执和 SSH transport 接口；差异先回修设计。
- [ ] 测试网络和所有远端 session 均为可丢弃资源；不得对真实训练/生产 pane 注入故障。

## 顺序实施

1. 在 Contracts 增加完整 DeviceId/SessionKey/epoch/channel owner、预算、占用快照和租约
   拒绝结果；不加入进程或 WinUI 类型。
2. 在 Core 写纯 `ConnectionAdmissionPolicy`，覆盖 session RPC pair 的二槽原子准入、
   五类连接、每设备/全局限制、公平等待与非可见 session 暂停顺序。
3. 写 lease 生命周期测试，证明并发 acquire/release、启动失败、双 dispose 和重连宽限
   不会泄漏或重复归还。
4. 实现 `TerminalQueueBudget` 的当前 epoch/seq/bytes 记账；先预检再入队，确认后释放。
5. 实现 dirty-set 合并与 full-resync 标记；验证它不被 terminal pipeline 调用。
6. 将 HD-022 SSH process factory 接到 admission lease；request/event 启动前先取得 pair，
   任一失败 teardown 两个 child；每个退出路径都在 finally/dispose 精确归还 owner。
7. 将 HD-014 renderer consumption 接到 `Q_p`；超限关闭当前 epoch 并发出单一领域错误，
   不截断、不跳 seq、不继续显示 online。
8. 在 PaneVisibilityCoordinator 接入 Visible/Hidden/WaitingForCapacity 状态；隐藏时释放
   terminal/renderer，再显示时只创建 observe/new epoch。
9. 给 UI 增加 session 容量等待/暂停、预览暂停、过载重连的可访问文本和重试动作；
   不显示伪进度，也不新增用户可调连接预算。
10. 建立性能脚本，参数化 RTT、loss、bandwidth、pane/device/session 数和输出速率；脚本只操作
    显式传入的隔离目标，并保存命令版本与脱敏环境。
11. 跑高输出/慢 renderer/取消/隐藏显示矩阵，采集 `Q_p`、连接租约、吞吐、进程树
    working set/private bytes/handles；失败样本也保留。
12. 根据矩阵选定并记录固定 `B_ssh`，更新 evidence README，逐条关联 AC1–AC6；未跑平台写
    `UNVERIFIED`，没有足够证据就阻塞 P3，不猜值、不改 AC27 passed。

## 精确测试用例

- `ConnectionAdmissionPolicyTests`：同 Device 两个 SessionKey 分别准入；RPC pair 全有或
  全无；3 个单 session+4 terminal 的 10 槽与附加 file/maintenance 的 13 槽样本；使用
  小型测试预算触发公平等待/暂停；并发 1000 次不超 `B_ssh`。
- `TerminalQueueBudgetTests`：边界 `24 MiB` 接受、`+1` 拒绝；确认单调释放；旧 epoch、
  seq 回退、重复 ack 不释放；单帧/行上限仍由 parser owner 处理。
- `DirtySetBudgetTests`：重复实体只占一项、溢出转 full resync、snapshot 后复位。
- `SshConnectionBudgetTests`：同设备多 session/不同 endpoint、一个 pair 第二 child 失败、
  认证失败、EOF、取消和退避均释放正确槽；旧 epoch 晚 dispose 不释放新 epoch，
  一设备阻塞不影响其他设备。
- `PaneVisibilityBudgetTests`：隐藏 100 次无 terminal/renderer 残留；恢复均为新 epoch observe；
  第五 pane 有明确等待/切换，缓存投影不冒充实时 terminal。
- 性能矩阵：RTT 0/100/300 ms、loss 0/1/3%、限带宽与突发高输出；实际可实现条件
  以测试环境记录为准，缺少真实整形不得用 sleep/fake 结果替代。

## 命令与证据等级

当前已存在且在变更后运行：`just ci`。它只证明当前离线 G0/L1 门，不证明 SSH/WinUI。

以下是拟建路径存在后才可运行的未来命令，当前不得伪称可用：

```powershell
dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj --configuration Release
dotnet test tests/Integration.Ssh/HerdDesk.Integration.Ssh.csproj --configuration Release
dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj --configuration Release
pwsh -NoLogo -File scripts/Run-SshPerformanceMatrix.ps1 -Config <approved-test-config>
```

- L1：公式、字节记账、租约并发和状态转换；可在 CI 使用 fake clock/process。
- L2：真实 Windows 11、真实 app-owned ssh/bridge 进程、可丢弃 herdr；证明槽回收和取消。
- L3：交互桌面加真实 Linux 目标与网络整形；证明 UI 状态和高 RTT/loss 行为。
- HD-033 才汇总全应用内存、soak 与 AC27 最终结论。

## 回滚与停止条件

每接入一层保持可独立回滚的提交；失败时按 Core→Infrastructure→UI 逆序撤销本任务变更。
任何丢 delta、旧输入重放、错误进程终止或内存无界现象立即停止晋级，降至更低可见
pane 上限/只读 observe。回滚不得停止 daemon 或用户 agent，证据缺失保持 blocked。
