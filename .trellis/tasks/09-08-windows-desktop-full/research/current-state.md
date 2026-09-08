# 当前实施与规划差距

核查日期：2026-09-08。仓库起点：main，HEAD `1cce6e4`；开始时工作树干净。本次为源码、文档与任务结构核查，未运行产品或现场测试。

## 1. 事实来源与复用边界

| 证据 | 已有内容 | 对实施的意义 |
|---|---|---|
| `HerdDesk.slnx:2` | Contracts、Core、SmokeTests 三个项目 | WinUI App、Infrastructure、terminal renderer、Rust sidecar 尚未进入 solution |
| `src/HerdDesk.Contracts/TerminalModels.cs:4` | 身份、epoch、TerminalAccess、RendererInput、InputContext | 增量补齐 ports；保持现有身份定义，不整文件覆盖规划草案 |
| `src/HerdDesk.Core/TerminalFrameParser.cs:13` | 单 epoch、完整 record 的 fail-closed parser | transport 仍须实现 framing、生命周期、队列、重连；parser 不是整条终端链路 |
| `src/HerdDesk.Core/InputPolicy.cs:5` | 只有同 pane/epoch 且 Controlling + ControlVerified 可输入 | policy 不负责获取控制权，不可由首帧/进程存活填充证明 |
| `tests/HerdDesk.Core.SmokeTests/Program.cs:6` | BCL smoke runner | 保留现有回归；未来单元测试不得吞并或伪称真机证据 |
| `scripts/herddesk_g0/protocol.py:1` | Python 诊断契约 | 复用 fixture 与诊断，产品后端继续按 C#/Rust 主线实现 |
| `implementation/status.json:2` | G0 未通过；Windows live not_run；verified AC 为空 | 当前产品完成度不能由离线绿灯估算为功能完成百分比 |
| `evidence/compatibility-baseline.json:17` | runtime binary、daemon、schema hash 为空 | 上游源码 blob 与运行二进制 hash 分开记录 |
| `docs/adr/0001-g0-bootstrap.md:5` | G0 提前建仓不等于 HD-007 完成 | 后续完整 solution、应用依赖和质量门仍属 HD-007 |

`planning/backlog.json` 的活动状态：HD-001/002 in_progress，HD-003/004 blocked，HD-005–036 planned；`planning/acceptance.json` 共 48 项，全为 not_run。新 Trellis 子任务统一 planning，表示本次实施计划尚待批准，不能反向抹去旧 HD 任务的已有准备。

`implementation/status.json` 的 73 个 Python / 22 个 smoke 及 hosted commit 是历史记录；源码 smoke 已新增用例。本文不把旧计数当当前 HEAD 执行结果。本轮只需任务/文档结构验证，不为规划修改重复运行全部产品测试。

## 2. 差距按交付阶段分布

| 阶段 | 已有可复用准备 | 缺失交付 |
|---|---|---|
| G0 | 上游固定 tag/协议记录、探针、synthetic fixtures、BCL parser/policy | 本地与远端 runtime 基线、实际 endpoint/ACL、控制证明、renderer/IME spike、授权/工具链/ADR 定案 |
| P1 | 目录与 G0 CI | 产品 solution、窄 RPC relay、请求/订阅 transport、actor Store、收敛、WinUI 壳、导航搜索、通知 |
| P2 | 输入拒绝策略 | terminal 长连接/生命周期、renderer、真实 IME、控制/释放/接管、资源命令、故障恢复及本地验收 |
| P3 | SSH 主线设计 | 配置与身份 UI、部署 helper、远端透明流、认证阻断、设备隔离、带宽预算、多设备验收 |
| P4 | file service 草案 | 独立 filebridge wire contract、安全文件操作、传输、双栏、冲突、附件、剪贴板和故障验收 |
| P5 | 发布历史与许可初表 | 全应用性能/可访问性/soak、签名 MSIX、更新回滚、最终安全许可和可追溯发布 |

## 3. 对旧 plan 的必要解释

1. **多阶段 AC 不提前结束。** G0 的 AC03/05 是环境/探针证据，产品 adapter 分别在 HD-008/013 实现；AC08/09 spike 不等于产品 IME；AC19 本地导航只是贡献；AC27 背压在多个层实现，最后在 HD-033 汇总实测。
2. **G0 与远端证据不形成死锁。** AC01 完整内容要求本地/远端各记录；HD-001 的远端基线取证可使用另行授权的外部实验环境，无须等待 P3 App。P3 再验证应用自身的 SSH 路径。两者都无证据时不得整体标 passed。
3. **规则更新有明确归属。** HD-006 在 G0 真机证据和架构决定被接受后提出项目 phase/guidelines 的最小更新；本轮不修改 AGENTS 或 spec。G0 中 HD-005 spike 需要明确批准其小型宿主范围，不能被“当前没有 App”误解释为永远禁止验证 UI。
4. **兼容性不凭空承诺。** 缺上游方法/ownership 信号/VT 模式时，记录能力缺口并阻止相关功能晋级；不得用本地模拟显示“控制成功”或用文本截图代替终端。
5. **标准桌面功能落到现有任务。** 配置持久化/诊断后端 HD-007，壳/设置/关于/空状态 HD-011；身份表单 HD-020；通知激活 HD-012；文件作业状态 HD-028/029；发布设置 HD-034。不因原任务标题短而遗漏这些功能，也不另造状态系统。
6. **项目名称以当前仓库为准。** GitHub 是 HerdrDesk，应用/namespace 是 HerdDesk；旧 plan 建议 slug 不触发重命名。

## 4. 未验证项目

Windows 本机 herdr、Linux/macOS 远端、named pipe ACL、control/release/takeover、Agent TUI、WinUI/WebView2/IME、文件 helper、性能、安装/更新、签名与 hosted 当前 SHA 全部不由本次规划证明。规划通过只表示任务可被审阅，不表示 G0 已通过。
