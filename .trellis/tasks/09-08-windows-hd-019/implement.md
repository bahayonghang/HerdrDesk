# HD-019 执行计划

1. 确认 HD-012/015/017/018 的实现和各自 unit/integration 证据完整。
2. 固定参考 Windows、WebView2、renderer、herdr、agent 版本及可丢弃资源。
3. 先 observe，再逐项 control/IME/resize/release，记录 single-delivery 和 lease。
4. 注入本地 renderer restart、EOF、daemon/pane exit，核对 epoch 和状态；SSH 网络故障留给 HD-026。
5. 关闭 GUI 100 次抽样验证仅释放自有 child；归档脱敏证据和支持矩阵。
6. 将失败项回传对应 HD-013–018，不把 UI/E2E 回归改为 parser-only 测试。

## 验证

此任务是实际 Windows 本地证据，`just ci` 仍只是补充。每个通过项需命令/版本、
环境、场景结果、证据索引和未覆盖限制；结果输入 HD-020 的远端前置门。

## 需求到验收追溯

| 需求 | 子验收 | 设计机制 | 测试与最终 owner |
|---|---|---|---|
| R1 | AC06-C1/AC10-C1 | scenario cards、disposable pane | HD-019 local MVP matrix |
| R2 | AC10-C1 | per-agent version/support row | HD-019 evidence owner |
| R3 | AC06/07/14/15-C1 | lease/input/ownership ledgers | HD-013/016/018 + HD-019 |
| R4 | AC13/15-C1 | distinct failure classification | HD-018 Recovery + HD-019 |

## 规划交付与实施完成的区别

本规划交付完成是场景卡、证据格式、owner、未来测试路径和失败回传规则完整。HD-019
实施完成要求上述 Windows 场景在批准的可丢弃资源运行并产生实际证据；随后才可将
本地 MVP 结果作为 HD-020 的依赖输入。任何 `not_run`、unit pass 或 synthetic capture
都不能充当本地 E2E pass。

AC13/14/15 在此仅交本地贡献，完整产品条款由 HD-026 汇总，不阻塞本地任务结束。
统一测试工程和发现入口见父 `research/test-layout.md`；本轮只规划这些执行卡。
