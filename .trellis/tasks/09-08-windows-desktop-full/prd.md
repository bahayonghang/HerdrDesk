# HerdDesk Windows 完整桌面端实施总计划

## 目标与用户价值

交付完整的 Windows 原生 herdr 工作台：用户可在一个窗口管理本机及远端设备的会话、工作区、Agent和普通shell pane，安全地观察/控制真实终端，使用搜索通知、SSH、文件与附件，并获得可安装、可更新、可诊断的产品。herdr继续拥有运行进程，HerdDesk负责连接和桌面交互。

本轮用户明确要求“根据当前项目实施情况和plan，深入分析后创建完整Trellis实施任务及子任务，覆盖UI与后端”。当前授权是规划文件的创建与核验；不包含产品实现、依赖安装、live herdr/SSH/WinUI、产品AC状态变更、提交或发布。全部新任务保持planning，未启动实施。

## 已确认背景

- 当前是G0协议/策略骨架：`HerdDesk.slnx:2`；源码只包含Contracts、Core及smoke项目，未来App/Infrastructure/renderer/sidecar均未创建。
- `implementation/status.json:2` 记录G0未通过、Windows live not_run和无已验证AC；历史离线/hosted结果不能当作当前完整产品可用性。
- `evidence/compatibility-baseline.json:17` 的runtime binary、daemon、runtime schema证据仍缺；控制权与IME是下一阶段的关键技术风险。
- 活动产品需求来自`planning/backlog.json`和`tasks/HD-001.md`至`tasks/HD-036.md`；48项验收来自`planning/acceptance.json`。`docs/plan/`仅作为既有设计档案，不能覆盖活动状态。
- 完整现状/差距见 [research/current-state.md](research/current-state.md)，公开技术依据复核见 [research/technical-sources.md](research/technical-sources.md)。

## 范围口径

本轮按既有plan的**完整核心1.0（G0–P5）**建立36个实际实施子任务，完整保留48项原AC。计划里原属1.x的7个扩展已在 [extensions.md](extensions.md) 分别展开UI、后端、顺序、验收和启用条件，保持候选；这是当前规划假设，用户可将它们升级为必做，再创建对应子任务并修订整体范围。

首发目标沿用原plan：经验证的Windows11 x64客户端与Linux x64远端；macOS/ARM64等构建候选必须按独立矩阵晋级。任何“未支持”项必须在实际产品中清晰显示，不能由UI隐藏或用mock补齐后宣传支持。

## 需求

- **R1 G0可行性与准入**：基于真实本地/远端runtime、endpoint/ACL、终端控制信号、IME spike、资产许可与安全ADR建立可验证基线。失败不得用只读降级冒充完整桌面端。
- **R2 可维护工程基础**：独立Contracts/Core、可替换adapter、明确配置/诊断所有权、精确依赖和可重复构建；各平台CI失败与真实required-check合并门可追溯。
- **R3 状态正确性**：真实RPC快照/订阅、未知能力降级、dirty收敛与epoch隔离；herdr是运行状态唯一权威。
- **R4 桌面可用性**：WinUI设备/会话/工作区/pane导航、全局搜索、最近使用、未读通知与准确跳转、设置/诊断/关于、键盘/屏幕阅读器/DPI/主题完整。
- **R5 真终端交互**：观察/控制/释放/显式接管、真实字节/resize/scroll/选择/复制粘贴/IME、Claude Code/Codex/OpenCode文本TUI；不以终端截图或日志视图替代renderer。
- **R6 操作与恢复**：按真实schema创建/关闭/重命名workspace/terminal/agent；目标确认、控制权校验、未知结果查询、不重放输入、GUI退出不杀daemon/agent、跨设备不串写。
- **R7 远端完整链路**：OpenSSH config/key/agent/ProxyJump与host身份、可信helper部署、透明RPC/terminal流、多设备聚合、独立退避和认证阻断。
- **R8 文件与投入agent**：独立窄filebridge、列举/传输/hash/原子提交、双栏UI、取消/冲突/路径安全、文字/路径/图像意图区分、能力声明准确、上传不自动提交、剪贴板与缓存边界。
- **R9 性能与稳定性**：有界frame/queue/进程/连接、真实呈现延迟和聚合资源测量、100次回收/重连及8h soak，不能通过丢delta或吞输入达标。
- **R10 可发行与追溯**：签名MSIX、runtime依赖、更新/配置回滚、最终安全许可、真实支持说明，以及需求→任务→AC→测试证据→产物hash闭环。

原AC与上述需求逐条对应见 [acceptance-map.md](acceptance-map.md) 和机器可读副本。配置、诊断和空/错/过期状态是核心用户流程的一部分，已归到具体任务，不另建重复需求事实源。

## 父任务产品验收

- [ ] **AC1（R1）**：G0取证及准入决定完成，Windows控制证明与IME路线成立，source/synthetic/runtime/desktop证据分开；原AC01/02/03/44所需前置证据可查。
- [ ] **AC2（R2）**：干净环境按锁定版本构建，Contracts/Core依赖方向正确，fake transport可测；失败检查确实阻断约定合并路径，平台skip不记成功（原AC39/40/47）。
- [ ] **AC3（R3）**：snapshot与订阅竞态、未知字段/enum/能力、旧epoch、断开与恢复均有确定结果并收敛（原AC04/11/12）。
- [ ] **AC4（R4）**：用户能用键盘/屏幕阅读器完成导航搜索、通知定位、控制/释放/关闭确认；三设备100条投影搜索p95<100ms，主题/DPI矩阵通过，设置/诊断可用（原AC17/18/19/37/38及UI蓝图）。
- [ ] **AC5（R5）**：真实终端完成观察/控制/释放/resize，两个observer与controller互斥符合实测契约；UTF8、中文IME和三类Agent文本TUI在记录版本上通过（原AC05–10）。
- [ ] **AC6（R6）**：创建/关闭/重命名符合schema且超时不重复执行；断线/崩溃后输入不重放、恢复先observe、pane继续存活，100次同名目标竞态零串写（原AC13–16/20/21）。
- [ ] **AC7（R7）**：实际SSH身份/config/端口/key/agent/ProxyJump组合通过，host-key变化阻断，helper经同意和可信hash安装，透明流与设备故障隔离通过（原AC22–26）。
- [ ] **AC8（R8）**：文件大小/名称/权限/symlink/冲突/中断/路径攻击矩阵通过，完整性与取消清理正确；附件/路径/文本语义真实、OSC52默认拒读、上传不自动提交（原AC30–36）。
- [ ] **AC9（R9）**：高输出/慢renderer无静默丢帧，预算和呈现延迟有原始测量，100次资源回收/断连及8h负载通过（原AC27–29/46）。
- [ ] **AC10（R10）**：干净机安装/更新/回退及安全许可通过，文档与支持矩阵对应实际验证；原48项完整追溯到最终包hash，未测项不写passed（原AC41–45/48）。外部发布只在明确授权后实施。

以上为未来产品验收，不能因本轮规划文件完成而打勾。多阶段AC由acceptance-map中的final_owner汇总，贡献任务局部通过不等于整项完成。

## 设计决定与明确排除

采用原plan主线：WinUI + .NET10 + C# Core + WebView2/xterm + Rust RPC bridge + OpenSSH；精确Windows App SDK/NuGet/npm/Cargo版本要经HD-005/007实际还原后锁定，不能按网页展示版本编造依赖。JSON原子配置足够，不加业务数据库/服务端。默认observe、从不自动接管、输入不重放、GUI关闭不终止herdr拥有的进程。

不在核心1.0新增daemon替代品、云账号同步、插件市场、计费平台、Windows被控宿主、standalone shell、全量密码/MFA provider、原生renderer正式替换或任意图形协议保证。现有plan的1.x扩展保留单独路径；缺能力不降低完整1.0的已承诺验收。

## 待验证与执行前决定的归属

| 项目 | 拥有任务 | 对执行的影响 |
|---|---|---|
| runtime/schema/pipe ACL及控制信号 | HD-001/003/004 | 阻断依赖这些证据的功能实施与晋级，不阻断规划 |
| renderer/IME及精确依赖 | HD-005/007 | 先spike和锁定；不得绕过验收换架构 |
| 项目许可/Publisher与安全ADR | HD-002/006/034 | 本地准备可先做，分发/签名需实际维护者决定 |
| 真实SSH测试目标与helper部署授权 | HD-020/021/026 | 不以模拟或其他机器成功代替目标组合 |
| filebridge wire合同与原子性 | HD-027/028/032 | 先安全评审再产品实现，不虚构现有协议 |
| 真机/IME/soak/安装/渠道 | HD-019/026/033–036 | 按环境取证；无授权/资源时记未完成，不提前标成功 |

技术未知已分配给明确的研究/验证交付，详细方案是可审阅的实施路线，后续单任务启动仍需达到其前置条件。36任务原估算57–99有效人日，25%储备后精确71.25–123.75；属于原始排期量级，不是承诺剩余工期。

## 规划工件与交付检查

- [task-map.md](task-map.md)：36项任务、依赖、原AC贡献、原估算与关键路径；JSON副本用于一次性一致性检查。
- [design.md](design.md)、[ui-blueprint.md](ui-blueprint.md)：全局契约、模块/文件责任、状态/数据流和完整UI行为。
- [implement.md](implement.md)：分波执行、子任务协议、平台质量门与最终集成。
- 36个子任务各有PRD/design/implement及implement/check JSONL；上下文只指向存在的spec/research。
- 本轮通过任务结构、原需求覆盖/依赖和独立规划审查后交付；审查结果与残余条件在父研究记录，不改变G0或任何原AC状态。

本轮结果见 [research/validation.md](research/validation.md)；独立合并报告位于
[整树规划审查](../../reviews/09-08-windows-desktop-full.md)。Phase 1规划交付已核验；
本轮未启动产品实施，原48项产品AC保持not_run，已有G0代码与源任务进度保留。
