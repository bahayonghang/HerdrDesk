# P0 协议异常收口与跨语言回归

状态：planning；父任务 09-08-evergreen-harness-audit，待批准。P0 指当前协议契约必须优先修复，不代表已证明生产攻击或已有桌面产品。

## Goal

协议解析对 malformed JSON 字符串异常保持稳定脱敏，并确保当前 epoch 解析器失败后不再接受帧。

## Requirements

- P1：转义孤立 surrogate 出现在字段值、属性名或 terminal.closed.reason 时，C# 对外只暴露脱敏 TerminalProtocolException，禁止 InvalidOperationException 逃逸；覆盖所有当前字符串 materialization 点。
- P2：第一次解析失败之后，同实例后续合法帧也被 terminal_stream_not_active 拒绝。
- P3：保持现有正常 Unicode/UTF-8 原始终端字节、序列号、closed 和输入控制契约。
- P4：共享最小合成语料供 C#/Python 对照；清楚记录允许忽略字段、closed reason 等跨语言边界，不为一致性新增整套验证层。

## Evidence

TerminalFrameParser.cs:48 reason.GetString、:101 helper GetString、:119 property.Name 可触发 InvalidOperationException；:89 catch 没有覆盖它，:86/:91 的 failed=true 被绕过。审查代理复现字段值与属性名，主线程补充 reason 路径，三种输入失败后 valid 帧仍被接受。现有 smoke Program.cs:30-34 未覆盖此路径。

## Acceptance Criteria

- P-AC1 → P1/P2：type 字段值、属性名、terminal.closed.reason 三个孤立 surrogate 案例都产生 TerminalProtocolException，Message 为 malformed_terminal_record；同实例随后 valid 帧拒绝为 terminal_stream_not_active。
- P-AC2 → P1：错误不包含输入文本、路径或终端正文；断言精确异常类型与 code，不仅测试任意异常。
- P-AC3 → P3：有效 surrogate pair、原始 UTF-8 跨帧、seq/closed/输入策略旧回归继续通过。
- P-AC4 → P4：最小共同语料测试给出两语言 accept/reject 和状态结果；若存在非业务等价边界，由强模型明确裁决后才改 Python，不强求相同内部错误名。
- P-AC5 → P1–P4：C# build/smoke、Python 回归和 just ci 通过；三个核心异常/锁存测试对旧解析器为红；逐个审查 :48 reason.GetString、:101 helper GetString、:119 property.Name 均有收口，helper 的 type/encoding/bytes 调用也走同一边界。
- P-AC6：稳定异常/锁存规则和适用五工具的实现要求交给规则子任务正式回写；无 G0/产品 AC 提前放行。

## Dependencies and exclusions

无前置代码依赖。可与 SDK 修复并行；共同语料与 C# 文件由本子任务独占。原始终端帧字节不得作为 Unicode 文本验证。禁止新增依赖、重写 parser/transport、扩展真实连接或修改控制权模型。
