# HD-004 设计

## 状态观测模型

记录每个动作前的 terminal access、命令 argv、stdout NDJSON、stderr 分类、进程
exit、pane 存活、连接 epoch 和输入是否发生。`ControlVerified` 只能由可信 adapter
依据已证实的上游信号设为 true；不得由首帧、进程存活、窗口焦点推断，现有约束见
`src/HerdDesk.Contracts/TerminalModels.cs:20-23`。

## 最小实验顺序

初始 observe → full frame/delta → EOF 分类 → 第二 observer → 第二 controller 无
takeover → control request 的实际 busy/rejection/grant signal → 显式 takeover
confirmation → 旧 controller 失权 → writer-only resize → release → pane 存活检查。
初始 observe 不发送 resize、input 或 release。每次 reconnect 新建 parser/epoch；每
一步失败即记录，不继续破坏性动作。

## 拟建证据

`tests/fixtures/real-terminal-v082/` 的脱敏 capture、协议差异表和 control-signal
matrix；fixture 标注真实/合成，不能把终端文本或凭据写入仓库。

## 已存在 probe 的合法命令边界

`python scripts/probe_herdr.py --help` 当前只公开 `selftest`、`preflight`、`observe`
和 `control`。`preflight` 采集上游 `terminal session observe --help` 与 `control --help`；
它不实施产品控制。`observe`/`control` 需 `--herdr`、`--output`、`--target`，控制还
要求 `--disposable-target`；发送一次 input 还要求 `--allow-input --input-file`。
这些来自 `scripts/probe_herdr.py:119-129,171-188,336-355`，不是本计划杜撰的 CLI。

未来产品若需要 lease/resize 适配器，接口暂命名为 `TerminalLeaseProbe`，操作为
`Observe`、`RequestControl`、`RequestTakeover`、`ResizeWhileVerified`、`Release`；
它必须把上游已观察 response 映射成 `Observing/Acquiring/Controlling/Unknown`，而不
定义虚构的 `Granted` wire message。实现前未知命令写为“拟建，需要 runtime help/
capture 证实”，不能作为可执行实验步骤。

## 关键负例与判断

| 情景 | 预期 | 证据 |
|---|---|---|
| 两 observer | 均可读，seq 各在本 epoch 内验证 | 两份 capture/epoch |
| 第二 controller 无 takeover | busy 或明确拒绝，不夺权 | stdout/stderr/exit 分类 |
| explicit takeover | 仅确认后尝试；旧 writer 停止 | 新旧 writer capture |
| 旧 controller write | 被拒绝/无发送，不重放 | input ledger |
| no input ACK | 不把 stdin write 当送达确认 | `result=unknown` ledger |
| resize | 只在 verified writer 状态尝试 | actual signal 或 unknown |
| release | bridge 结束，pane/daemon 继续 | process ownership evidence |
