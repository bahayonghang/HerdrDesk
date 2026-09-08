# HD-002 执行计划

1. 审阅 README、license register、publication 和 HD-002，盘点已跟踪内容。
2. 建立来源台账和候选依赖/renderer/asset 准入模板。
3. 记录名称、品牌和 herdr/herdrm 关系，逐项记录许可/notice 义务。
4. 标记未知或冲突项，给出独立替代或移出构建的路径。
5. 将准入结论交给 HD-006；HD-035 依据实际产物复核。

## 验证

每个候选项都可定位版本、许可、notice、分发用途和复核任务；没有把“公开”写成
“允许复用”。AC02 只能由 HD-035 最终关闭。

## 追溯表

| 需求 | 子验收 | 设计机制 | 将来测试/证据 owner |
|---|---|---|---|
| R1 | AC02-C1 | 名称和上游关系条目 | HD-002，license register |
| R2 | AC02-C1/C2 | 单元级台账、source/hash/notice | HD-007/014/021，candidate review |
| R3 | AC02-C2 | 维护者审阅的准入记录与交接字段 | HD-034，package/asset manifest |
| R4 | AC02 最终 | lock/SBOM/release reverse audit | HD-035，final build review |

## 规划交付与实施完成的区别

规划交付完成是台账结构、准入表、交接字段和最终复核责任清楚。实施完成需要每一个
实际进入产品的依赖/资产拥有可追溯来源和许可结论；最终 AC02 仍需 HD-035 对
真实发行输入复核，不得被本任务的文档完成替代。
