# 五套 harness 的能力与工作分配

核对日期：2026-09-08。这里的 OMP 指 can1357/oh-my-pi。以下是官方文档能力，不是本机五套 CLI 的运行认证。本轮通过原生 Codex 协作工具调用强模型审查代理，没有跨客户端启动收费模型作基准比较。

| Harness | 官方能力边界 | 本项目适用分工（审查建议） | 便宜执行的前提 |
|---|---|---|---|
| Claude Code | 原生 CLAUDE.md、skills、hooks、自定义 subagent；可限制工具并单独选模型 | 共用规则与模块说明的语义审查；PowerShell 修改的独立复核 | 用低成本模型做已批准的薄入口/链接/规范摘录；不能决定安全边界 |
| Codex | 默认 AGENTS.md/override；额外文件名需配置且不替代同目录已有 AGENTS；.agents/skills；子代理可选模型/推理 | 主规划、C#/Python 跨语言异常及 CI 根因审查，本轮主执行环境 | gpt-5.6-terra 做定界检索，gpt-5.6-luna 做明确重复编辑；核心协议修复由强模型审查 |
| Grok Build | AGENTS.md 与 CLAUDE.md 均可发现；忽略的规则文件跳过；内置 explore/plan 没有 shell；自定义 agent 可配置 | 独立架构反证、上游事实和需求审查；跑测试交主会话或合适的执行 agent | 不把无 shell 的 plan/explore 派去跑 just ci；按实际模型配置选择执行模型 |
| Kimi Code | 内置 coder/explore/plan，plan 无 shell；子代理只有显式任务上下文；支持 skills/hooks/custom agents | 从批准计划生成文档、fixtures 和明确的 Python 测试；plan 可独立查需求遗漏 | 明确传 task 路径和文件清单；不能假设 Claude agent 的 model 字段能在 Kimi 生效 |
| OMP | 多 provider/model role，plan/slow/smol 等；task 子代理、advisor；工具能力取决于配置 | 用强 plan/slow/advisor 复核任务分解，再以 smol 执行互不重叠的小项 | 记录解析后的实际模型与权限；角色名本身不能证明价格更低或只读 |

上述分工是工程建议，不是五套工具质量排名。不存在本项目实测的模型成本/耗时数据；不宣称 Kimi 或某一整个 harness 必然便宜。模型可用性、订阅与实际计费以执行时账号为准。

## 规则与执行边界

- Codex 不应依赖自动加载 CLAUDE.md。最小修复是把共享事实/边界置于 tracked AGENTS.md，CLAUDE.md 保留必要平台入口与模块导航，引用共享源。
- Grok 官方 sandbox 文档描述 Linux Landlock/macOS Seatbelt，默认关闭。不能由此声称本机 Windows shell 具有同等 OS 隔离。官方 plan/explore 的无 shell 边界也不代表自定义 trellis-check 无写权限。
- Kimi 文档明确 custom agent 未识别字段会忽略，举例包含 Claude 的 model 字段；因此跨工具复制 frontmatter 不构成模型锁定。
- 所有工具的计划模式、文本只读要求、permission gate、OS sandbox 分别记录；测试会生成 bin/obj，因此“只读审查”指不改产品源文件，不能把构建派给完全禁止写入的环境。
- 本轮未运行五套新会话，规则/hook/skill 实际发现与子代理模型解析统一为 UNVERIFIED。后续使用无敏感内容的固定题单作最小加载验收，不运行 herdr 或 GUI。

## 便宜模型可承担与不可承担

可承担：经强模型确定的入口链接调整、模块说明迁移、合成样例录入、文档中的状态/来源日期补齐、运行既定测试并返回退出码。
强模型保留：异常安全与失败锁存契约、控制权/epoch/SSH 权限判断、失败根因、需求与 AC 映射、检查充分性和最终验收。
执行失败、涉及跨层行为变更或超出文件所有权时，交回强模型；不让执行模型自行放宽测试或改批准范围。

## 一手来源

- [Claude Code subagents](https://code.claude.com/docs/en/sub-agents)：工具集、模型和权限配置。
- [Codex AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md)：文件发现与 fallback。
- [Codex subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents)：模型继承、选型和推理设置。
- [Codex skills](https://learn.chatgpt.com/docs/build-skills)：.agents/skills 发现。
- [Grok rules](https://docs.x.ai/build/features/project-rules)：AGENTS/CLAUDE 发现、gitignore 与 inspect。
- [Grok subagents](https://docs.x.ai/build/features/subagents)：explore/plan 无 shell。
- [Grok sandbox](https://docs.x.ai/build/features/sandbox)：默认关闭及平台限制。
- [Kimi agents](https://moonshotai.github.io/kimi-code/en/customization/agents)：coder/explore/plan、上下文、权限继承、忽略 model 字段。
- [Kimi skills](https://moonshotai.github.io/kimi-code/en/customization/skills.html)、[hooks](https://moonshotai.github.io/kimi-code/en/customization/hooks.html)：官方已支持，不应沿用“不支持”的旧判断。
- [OMP 官方仓库](https://github.com/can1357/oh-my-pi)：模型 roles、task、advisor。
- [OMP Agent Hub](https://github.com/can1357/oh-my-pi/blob/main/docs/agent-hub.md)：实际模型、状态和 usage 的观察入口。
