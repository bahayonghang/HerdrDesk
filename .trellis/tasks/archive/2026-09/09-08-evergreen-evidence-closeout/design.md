# Design

PUBLICATION_MANIFEST.json 是历史建仓清单；先在 docs/publication.md 和 README 明确对应历史用途与审计基线，日常开发用 just ci。不自动刷新旧哈希或扩大 manifest 到 Trellis/私人目录。若脚本默认入口仍把历史漂移包装为当前发布候选，最小修改 scripts/publish_github.py 的帮助/诊断使预期范围明确，保持现有拒绝已有仓库的安全约束。

文件：README.md、docs/publication.md、scripts/publish_github.py、tests/python/test_publish.py、planning/CLAUDE.md、tasks/CLAUDE.md。implementation/status.json 仅在有对应 SHA 的新验证证据时更新，不覆盖既有记录的历史意义；planning/backlog.json 和 acceptance.json 保留真实 G0 状态。

父子任务只管理本轮改造执行；产品进度仍在 HD backlog 与 AC 文件。建立简短映射而非新 schema/自动同步器。正式稳定结论优先回写项目 AGENTS/spec/docs（由规则子任务单一负责人统一编辑），满足用户“项目说明、skill 库或团队知识库”的回写要求，不修改用户全局技能或私人知识库。

## Ownership and model

强模型负责契约、根因和最终审查；更便宜模型只执行明确文件与检查清单。适用 Claude Code / Codex / Grok Build / Kimi Code / OMP；具体边界见父 research/harness-matrix.md。
