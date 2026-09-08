# HD-002 设计

## 台账和准入

每个可分发单元一条记录：名称、类别、来源、精确版本/hash、license/notice、修改
方式、分发位置、审批结论、证据日期、责任人和最终复核任务。源码、预构建 binary
和运行时下载物单独登记。

实施任务先提出候选；HD-002 给出 approved / blocked / pending；HD-007、014、021、
034 只能使用 approved 项并添加 lock/hash；HD-035 从最终构建、SBOM、notice 反向
审计。台账存在不代表 AC02 passed。

上游 schema、截图、图标和 herdrm 源码都有独立权利风险。独立构造的 fixture 只能
说明测试资产来源，不能替代上游分发许可。

## 产物和资产准入表

| 决策对象 | 形成产物 | 允许进入下一任务的条件 | 最终 owner |
|---|---|---|---|
| 应用名称/品牌 | `docs/licensing-register.md` 名称条目 | 来源与发行主体明确 | HD-035 |
| NuGet/npm/Cargo 包 | package candidate entry + notice requirement | 精确版本、license、再分发条件已审 | HD-007/034 |
| xterm/WebView/字体/图标 | asset manifest candidate | 素材来源、修改权、bundle 条件已审 | HD-014/034 |
| bridge/filebridge binary | binary provenance record | source、build、hash、目标平台明确 | HD-021/034 |
| 测试 fixture/截图 | fixture provenance record | 独立构造或授权明确，已脱敏 | HD-019/026 |

台账不应混合“实现可行”“安全允许”“可发行”三种结论。一个候选可在技术 spike
中 `pending`，但不得因此进入 package lock、WebView bundle、MSIX 或发行清单。

## 跨任务复核

后续任务在现有 `docs/licensing-register.md` 中补充候选名称、版本/hash、来源、
实际分发方式、修改情况、许可依据及 notice 安排，由维护者审阅并记录结论与原因。
这是文档和人工判断，不新增代码类型、审批服务或许可证规则引擎。HD-035 以最终
产物反查，候选台账不能替代最终包的许可证据。
