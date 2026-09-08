# HD-001 执行计划

1. 复读 baseline、HD-001 和 status，列出静态字段、空字段与证据等级。
2. 建立 Windows/remote 分栏模板和 stable/preview 差异表。
3. 获得运行授权后，仅在隔离 Windows pane 采集只读版本、ping、schema、
   endpoint/ACL；保存脱敏输出和退出码。
4. 对照静态基线，发现不一致即停止兼容推断并生成下游阻塞项。
5. 在独立获批的 remote 专项采集同一字段，不要求 P3 bridge 已存在。
6. 审查记录不含凭据、终端正文或私有路径；未覆盖项保持 not_run。

## 验证

- JSON/链接结构可离线检查，但只证明格式。
- 每个 runtime 结论必须有环境、真实版本、hash、protocol/schema、输出索引和
  明确 evidence level。
- AC01 只在 Windows 与 remote 最终证据都齐备后由总体验收更新。

## 追溯表

| 需求 | 子验收 | 设计机制 | 将来测试/证据 owner |
|---|---|---|---|
| R1 | AC01-C1/C2/C3 | 分层 evidence level 和独立采集线 | HD-001，`evidence/runtime/` |
| R2 | AC01-C1 | 三类 hash 分栏和冲突矩阵 | HD-001，baseline review |
| R3 | AC01-C1 | stable/preview diff 与显式 block | HD-006，ADR review |
| R4 | AC01-C2 | disposable scope、脱敏附件 | HD-004，isolated Windows evidence |
| R5 | AC01-C3 | remote 独立采集，不依赖 P3 code | HD-026，future remote matrix |

## 规划交付与实施完成的区别

本任务的规划交付完成条件是采集 schema、矩阵、附件格式、冲突处置和责任归属被
审阅并写入任务工件。HD-001 的实施完成条件则是按获批环境收集 AC01-C1/C2/C3
所需运行证据。两者均不自动更新 G0 gate；总计划依据最终 evidence 决定阶段状态。
