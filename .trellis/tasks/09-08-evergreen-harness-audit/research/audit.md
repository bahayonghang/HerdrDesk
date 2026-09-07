# HerdDesk 常青审查

2026-09-08，HEAD `8ae4797d0aa10559648d1ade5b7e017ff46c7a53`。只读审查、离线测试和任务规划。未修改产品代码、全局环境、正式规则或远端。

结论：现有离线 G0 gate 全绿，但存在未覆盖的协议失败锁存缺陷、setup 副作用顺序问题和五工具说明不对齐。适合先做下列有限修复；不宜据当前状态扩展 WinUI/SSH 产品实施。

## 结构与已审阅边界

| 路径 | 当前职责/证据 |
|---|---|
| src/HerdDesk.Contracts | TerminalModels.cs 定义身份、epoch、帧和输入决策；BCL-only |
| src/HerdDesk.Core | TerminalFrameParser.cs 单 epoch fail-closed 解析；InputPolicy.cs 控制权/来源/大小约束 |
| scripts/herddesk_g0/protocol.py | 严格 JSON、帧/输入校验、NDJSON、合成 capture 报告 |
| scripts/probe_herdr.py | 只读 preflight 与需显式 disposable/input 授权的流式探针 |
| scripts/Invoke-HerdDesk*.ps1 | setup 与 preflight 包装；setup 有安装/用户环境副作用 |
| tests/python、tests/HerdDesk.Core.SmokeTests | 73 Python、22 C# smoke；probe selftest 另有23项 |
| .github/workflows/ci.yml、justfile | Windows/Linux 离线 G0；当前本机 Windows just ci 已运行 |
| planning、tasks、implementation、evidence | 36 HD 产品任务、48 未执行 AC、历史/当前证据 |
| docs/plan | 12 专题规划档案；规划端口不等于已编译 src 契约 |
| AGENTS.md、CLAUDE.md、各工具目录、.trellis | 共享入口、模块索引、本机生成适配器与占位 specs |

G0 未通过有真实门禁原因：运行中上游 CLI/schema/endpoint/ACL、隔离会话控制、Windows renderer/IME、许可/发行确认尚缺证据。它们不是现有测试失败，也不能由强模型推理替代实测。本计划不把尚未实现的 App/Infrastructure/renderer 当成代码缺陷。

## F1 — P0：JSON 字符串异常逃逸，失败后解析器仍继续

- 证据：src/HerdDesk.Core/TerminalFrameParser.cs:48 的 reason.GetString、:101 的 helper GetString 和 :119 的 property.Name 可因转义孤立 surrogate 抛 InvalidOperationException；:89 catch 不接收此异常；:86/:91 的 failed=true 均未执行。
- 复现：输入 `{"type":"\uD800"}` 或包含孤立 surrogate 属性名的记录，再向同一 parser 输入合法首帧。审查代理重复确认异常逃逸及随后 valid 帧被接受。
- 违反 src/HerdDesk.Core/CLAUDE.md:11 的失败锁存与 :25 的统一 malformed/脱敏契约。
- 为何现有测试未报：tests/HerdDesk.Core.SmokeTests/Program.cs:30-34 覆盖普通失败、重复键和非法 UTF-8，没有 JSON escaped surrogate 的延迟字符串读取异常。
- 最小修复：在所有当前 JSON 字符串 materialization 点统一转换异常并锁存；增加 type 字段值/属性名/terminal.closed.reason 三案例及后续合法帧拒绝、正常 Unicode 对照。reason 补充复现见 [plan-review.md](plan-review.md)。
- 所有者：protocol-failure-contract。P0 是本组件的优先修复门槛；没有推断生产利用或上游恶意行为。

## F2 — P1：setup 在校验之前安装和修改用户环境

- justfile:24 启动 scripts/Invoke-HerdDeskDotnetSetup.ps1；:44 自动 Install-PinnedSdk，:34 winget 接受协议并安装，:52/:57 写 User DOTNET_ROOT 和 DOTNET_MULTILEVEL_LOOKUP，:65 才读 global.json 校验。
- README.md:26 描述核对 SDK 和设置 DOTNET_ROOT，未描述缺 SDK 时自动安装及另一持久化变量。
- 因此检查失败可能发生在副作用之后；独立 ExpectedSdk 默认值与 global.json 会形成两个来源。此项为源码确认，未运行安装路径。
- 最小修复：默认只读检查，先读 global.json；显式安装/持久化才执行副作用，以无副作用 stub 测试路径。
- 所有者：sdk-setup-boundary。不要为了此次审查修环境或安装 SDK。

## F3 — P1：共享入口偏向 Claude，Trellis 规范尚未落地

- CLAUDE.md:3,75,117 保存详细项目索引/模块/安全规则；AGENTS.md:6-17 只有 Trellis 导航，没有共用事实入口。
- Codex .codex/config.toml:9-10 的 fallback 只有 AGENTS.md；官方默认发现不能自动补全同目录 CLAUDE.md。
- .trellis/spec/backend/index.md:9 与 frontend/index.md:9 仍要求 Fill in，规范条目为 To fill。已有 00-bootstrap-guidelines 处于 in_progress，不能将模板当作项目契约。
- Grok 官方支持读取 CLAUDE.md，因此不笼统说所有非 Claude harness 必然读不到；明确缺口是缺少统一、显式、可移植的入口。
- 最小修复：tracked AGENTS.md 的托管块外承载共享规则/导航，CLAUDE 保留薄入口与模块索引；按真实 C#/Python 填规范，前端未实现明确 deferred。
- 所有者：harness-context-alignment，与旧 bootstrap 任务关联交付。

## F4 — P1：本机适配器、平台陈述与实际加载证据混淆

- .gitignore:25-31 忽略 .agents/.codex/.grok/.kimi-code/.omp/.claude；本机存在不代表 fresh clone 分发。只读 reviewer 对模板哈希确认没有局部改动漂移。
- .kimi-code/skills/trellis-implement/SKILL.md:49-53 声称 Kimi 无 project custom agents，check/research 同类文案；当前官方已支持 custom agents/hooks。
- .trellis/workflow.md:104 泛称全平台 UserPromptSubmit hook；:223/:226 对 Kimi skill 包装与“没有同名 skill”自相矛盾；:483 泛称自动注入。
- .codex/config.toml:2-18 自身说明 trust/hooks feature/一次性启用条件，不能宣称本机 hook 已工作。本轮原生 Codex 协作成功只证明当前会话工具，不等于五 CLI 的全部注入路径。
- Grok 官方只明确忽略规则文件的行为；没有据此认定 ignored agents/skills 的 runtime 一定失活。
- 最小修复：tracked 五工具矩阵与手工 pull 回退、准确条件措辞和最小新会话验收。生成适配器上游变更列 handoff，不擅自改相邻 Trellis/全局 skill 仓库。
- 所有者：harness-context-alignment。

## F5 — P2：历史 publication 清单与常青默认核验错位

- python scripts/publish_github.py 默认离线路径在 scripts/publish_github.py:133 附近调用 validate_manifest；:62-64 对每个清单条目按当前字节核验，遇 .gitattributes 即停止。
- 全量审查：.gitattributes/.gitignore/AGENTS.md/README.md 共4个条目漂移，无 listed missing。清单有154条，是历史 reviewed source import，非完整当前 tracked 集。
- README.md:56 的默认可离线核验措辞容易被当作当前 gate；tests/python/test_publish.py 使用临时合成 manifest，CI 不调用真实 publication dry-run，故 just ci 全绿并不矛盾。
- 根因是时间/集合范围未明确，不应“刷新四个 hash”掩盖历史清单性质。计划保留冻结历史，日常 gate 指向 just ci，并精确描述核验失败用途。
- 所有者：evergreen-evidence-closeout。

## F6 — P2：证据和任务权威需要常青维护约定

- README.md:13-17、CLAUDE.md:15-18,155-159 写死计数/覆盖说明；implementation/status.json 引用明确历史 SHA，已说明后续 SHA 独立验证，这一边界正确，应保留。
- 当前 HEAD 无 hosted run，已验证的是本机 Windows 离线；历史失败为默认编码，629bb01 已修复。不要再次计划修已修复编码问题。
- Trellis 与 HD 两套任务并存，需要职责映射，不能双向自动复制产品状态。
- 不推荐新增大而全 validator。文档常青靠明确权威引用、对应检查结果及强模型语义审查；现有结构验证只证结构。
- 所有者：evergreen-evidence-closeout，与规则子任务统一回写。

## 工具/模型分配

完整官方能力、来源与差异见 [harness-matrix.md](harness-matrix.md)。
F1：Codex 强模型规划/修复审查，Claude 强审查复核；便宜模型仅做确定的 fixture/test 录入。
F2：Codex 在 Windows 下验证 shell 实际行为，Claude/Grok 强模型审权限与副作用链；执行模型做已批准的脚本局部调整和 stub 回归。
F3/F4：Claude/Codex 强模型整理权威入口，Grok/Kimi plan 独立审查，OMP 强 plan/advisor 检查模型路由；便宜模型做薄入口/链接/文档同步，不能自选权限。
F5/F6：Codex/Claude 强模型裁定证据范围；Kimi coder、OMP smol 或 Codex 便宜 worker 做明确文档/测试修改。价格/账号未实测，不把整个 harness 标为便宜。

## 交付和限制

本轮仅父子任务/研究证据新增。当前代码问题仍未修复；正式规则/skill/团队知识回写等待用户批准。优先回写本仓 tracked 项目说明和 Trellis specs，注明适用工具；不上推、不修改用户全局技能和记忆。五工具 fresh-session、真实运行和产品验收缺口明确保留。
