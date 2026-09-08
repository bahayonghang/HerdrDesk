# HD-007 · 仓库骨架与 CI

状态：`planning`。本任务只形成实施计划，不启动 P1。

## Goal

在保留现有 G0 BCL/诊断基线的前提下，建立可还原的 Windows 桌面 solution、依赖边界、配置与诊断基础、DI 组合根和完整质量门，为后续 UI、RPC、终端及发布任务提供唯一工程入口。当前 solution 只有 Contracts、Core 和 SmokeTests，产品模块仍未建仓：`.trellis/tasks/09-08-windows-desktop-full/research/current-state.md:7-17`。

## Confirmed facts

- `global.json:1-7` 已把 .NET SDK 固定为 `10.0.400` 且禁止 roll-forward；`Directory.Build.props:1-11` 已启用 nullable、analyzers、warnings-as-errors 和 deterministic build。
- 现有 `just ci` 只运行 G0 Python、合成 fixture、结构校验、BCL build/smoke：`justfile:54-74`；Actions 也明确不证明桌面、IME 或 live herdr：`.github/workflows/ci.yml:30-46`。
- Windows App SDK 网页展示的 stable 产品版本不是可直接写入的 NuGet 包版本，精确 package 必须由模板、包元数据和真实 restore 决定：`.trellis/tasks/09-08-windows-desktop-full/research/technical-sources.md:3-8`。
- 原任务要求 AC39、AC40、AC47：`tasks/HD-007.md:19-23`；三项在 `planning/acceptance.json:309-320`、`planning/acceptance.json:373-378` 仍为 `not_run`。

## Requirements

- R1：仅在 HD-006 的安全/ADR 基线获批且 HD-005 给出 renderer/package 选择后实施；不能用本任务创建工程来跨越 G0 上游证据门。
- R2：保留并增量扩展现有 `HerdDesk.Contracts`、`HerdDesk.Core` 和 smoke runner；建立 App、Infrastructure、Terminal.Web 及统一 unit/contract 工程。Windows integration 由 HD-011 建立并复用。Native renderer 仍是条件候选，不预建空实现。
- R3：严格保持 Contracts→BCL、Core→Contracts、Infrastructure→Contracts/Core ports、Terminal.Web→Contracts、App→Core/adapters 的依赖方向；生产项目不得反向引用 App 或测试项目。
- R4：逐个核验实际 package ID、版本、许可、目标框架和 restore 结果后才写精确版本；NuGet 使用 lock file 和 CI locked restore。HD-008/014 后续新增 Cargo/npm 依赖时各自生成并提交真实锁文件，本任务不预填版本或下载依赖。
- R5：提供单一版本化 JSON 配置存储、DeviceProfile/SessionProfile 类型化 port、原子替换、上一份可恢复备份和注入式数据目录；P1 每个稳定 DeviceId 保存 label、已核验本机 herdr 路径及多个 named session/类型化 endpoint，配置不得保存密码、私钥、terminal 正文或硬编码用户目录。
- R6：提供结构化诊断基础；事件只接受已分类字段，默认不接受任意 message/payload/exception dump，并对 device/session 使用脱敏别名。日志写入失败不得把产品改成可写状态。
- R7：唯一 DI 组合根位于 App 启动层，注册配置、诊断、Core service 与 adapter factory；View/ViewModel 不自行 `new` transport，Infrastructure 不定位 UI 控件。HD-011 在 P1 通过本任务 port 实现本地 Device/named session/explicit endpoint 的编辑、校验、原子保存与首次连接；HD-020 只增量扩展 SSH profile。
- R8：未来唯一开发门保持 `just ci`，但按平台分支：所有平台继续跑 G0/BCL/contract；Windows 另跑完整 desktop restore/build/test。Linux 不无条件编译 WinUI，也不能把 G0 job 绿色写成全应用绿色。
- R9：本任务不运行 live herdr、SSH、WinUI 交互、takeover 或输入；这些 L2/L3 检查必须在后续明确授权的隔离环境执行。

## Acceptance criteria

- AC1（R2/R3；原 AC47 贡献）：solution 中每个生产/测试项目均可由一张允许依赖图解释；自动结构检查拒绝 Core→WinUI/WebView2/SSH、Contracts→第三方包及任何生产层反向引用。
- AC2（R3/R7；原 AC47 贡献）：Core 的关键服务通过 fake ports 单测，不要求 WinUI、WebView2、herdr、SSH 或真实用户目录。原 AC47 只在后续所有模块集成后复核并最终关闭。
- AC3（R4；原 AC39 贡献）：在受支持 Windows 干净 checkout 上按 lock file restore/build 成功，记录 SDK、package 源、direct/transitive 版本和命令；任一 package 未核实则保持 blocked，不写猜测版本。
- AC4（R5）：本地 Device profile 可按稳定 DeviceId 创建/修改/删除，同设备两个 named session/类型化 endpoint 独立 round-trip；首次写入、覆盖、并发保存、序列化/replace 失败、主文件损坏和显式备份恢复均有测试，失败后原配置可读且残留 temp 不被晋升。
- AC5（R6）：诊断事件含 component/operation/outcome/error category/epoch/计数和脱敏 ID；含 token、密码、私钥、绝对路径、ANSI/输入正文的测试样本不会进入默认日志。
- AC6（R7）：App 从单一组合根构造；测试可替换配置、时钟、诊断和 transport factory，且 production 启动不注册会伪装成功的 fake adapter。
- AC7（R8；原 AC40 贡献）：format/analyzers/unit/contract/locked-restore 任一失败会让本地 `just ci` 及对应 Actions job 非零；synthetic、live fixture 与人工证据目录分开。
- AC8（R8；原 AC40 贡献边界）：Actions YAML/job 成功只证明 job；GitHub required-check/ruleset 的读取及失败阻断证据才证明“阻断合并”。缺证据时记录 `UNVERIFIED`，需要变更远端规则时取得对应授权。

原 AC39/40/47 的最终汇总归 HD-036：本任务完成其已引入工程的基础门即可提交贡献，
不等待后续 Rust/npm/全部业务模块，从而不形成阶段完成循环。完整候选仍须在最终 SHA
复核所有依赖、质量门和 fake-port 业务测试，不能凭本任务早期通过关闭产品 AC。

## Out of scope

- HD-011 的 Shell/Settings/About UI（但消费本任务的本地设备配置 port）、HD-008 的 bridge/RPC、HD-013 的 terminal process、HD-014 的 xterm assets、HD-020 的 SSH identity 扩展、HD-034 的 MSIX/更新。
- 本任务不选择项目总许可证、不配置远端 branch protection、不安装 SDK/runtime、不写用户全局环境，也不把配置备份恢复扩展成任意文件恢复器。

## Blocking gate

开始实现前必须拿到本计划的单独批准、HD-005/006 已接受结论及可核验 package 输入；无阻塞产品问题。package 精确值属于实施取证结果，无法核实时停止相应项目，不用占位版本继续。
