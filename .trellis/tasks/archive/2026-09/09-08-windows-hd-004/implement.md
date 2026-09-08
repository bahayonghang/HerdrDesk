# HD-004 执行计划

1. 从 HD-001 取得已证实 CLI/version/schema，不足则保持 blocked。
2. 使用现有 `preflight` 获取 observe/control 的实际 help；先执行只读 observe、
   full/delta、第二 observer 和 EOF/exit 分类，验证 parser 只消费完整行。
3. 在单独授权下，对可丢弃 pane 验证第二 controller 无 takeover、再验证明确
   takeover、旧 controller 失权、无 input ACK 的结果未知和 writer-only resize。
4. 最后执行 release，记录每个上游可观察信号、bridge exit、pane/daemon 存活。
5. 对无明确 grant 的路径保持 Unknown/Acquiring，写入 HD-006 的 ADR 问题。
6. 将 runtime frames 和信号矩阵交给 HD-005/013/016；不把它变成 UI 成功主张。

## 验证

AC05-C1 需实际 capture；probe/selftest 和 `TerminalFrameParser` smoke 仅验证
离线解析。任何输入实验需有可丢弃资源、明确授权和脱敏证据。

## 追溯表

| 需求 | 子验收 | 设计机制 | 将来测试/证据 owner |
|---|---|---|---|
| R1 | AC05-C1 | action-before/after capture 和 exit 分类 | HD-004 capture corpus |
| R2 | AC06/07/14/15/16-C1 | lease state mapping、负例矩阵 | HD-016/018 integration |
| R3 | AC06-C1 | disposable+allow-input gate、input ledger | HD-013 transport tests |
| R4 | AC08/09 非贡献 | CLI/protocol 与 UI evidence 分栏 | HD-005/014/015 renderer tests |

## 规划交付与实施完成的区别

规划完成是合法命令边界、步骤、状态映射、负例和 evidence fields 明确。实施完成
需要在获批 disposable pane 实测以上矩阵；probe `selftest`、`preflight` 或 observe
成功都不能替代 control/IME/UI 的通过证据。
