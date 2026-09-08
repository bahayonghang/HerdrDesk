# HD-006 执行计划

1. 汇总 HD-001 至 005 的证据级别、差异、未解问题与许可准入。
2. 写通信平面、ownership、控制、renderer IPC、日志和文件能力 ADR。
3. 为每条 ADR 指定未来代码拥有者、required test/evidence 和只读撤销方式。
4. 后续本任务获批实施且真实 G0 证据被接受后，按 design 同步项目 AGENTS、
   backend/frontend index、nested CLAUDE 和实际状态；此步由 HD-006 负责，不另留未建任务。
5. 复核 ADR 不将 static/synthetic/probe 成功提升为 live/UI 证明。

## 验证

审查必须能从每个决定回链到事实或明确 Unknown；AC44 仅为贡献，最终由 HD-035
在最终实现、依赖和标准用户环境下验证。

## 追溯表

| 需求 | 子验收 | 设计机制 | 将来测试/证据 owner |
|---|---|---|---|
| R1 | AC44-C1 | 原 ADR-003 plane ownership | HD-008/013 contract tests |
| R2 | AC15-C1 | ADR-005 child ownership model | HD-013/019 process evidence |
| R3 | AC16-C1/AC14-C1 | 原 ADR-006 explicit lease and no replay | HD-016/018 E2E |
| R4 | G0 readiness | evidence/unknown/rollback ledger | total-plan gate review |
| R5 | transition contribution | 证据通过后同任务内项目规则同步 | HD-006阶段转换diff及证据，HD-007/011消费 |

## 规划交付与实施完成的区别

规划完成是 ADR 清单、门标准、未知项和更新责任可审阅。HD-006 实施完成需要被批准
的 ADR 文本、输入证据及批准范围内的项目阶段规则同步；G0/P1 状态转换需要对应 runtime 证据，
`blocked/not_run` 从不构成过门条件。
