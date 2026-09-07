# Protocol design

最小修改范围：src/HerdDesk.Core/TerminalFrameParser.cs；tests/HerdDesk.Core.SmokeTests/Program.cs；tests/fixtures/invalid-cases.json（或单个专用 protocol-edge-cases.json 共同语料，优先复用现有结构）；tests/python/test_protocol.py。先读取 fixture 结构再决定是否需要新文件。

在 JSON 字符串/属性名 materialization 的可信边界收口已复现异常。可选最小 catch filter 扩展需强模型检查范围，避免把任意程序 InvalidOperationException 一并吞掉；优先限制到 GetString/Name 读取路径。沿用已有 malformed_terminal_record 和 failed latch，不新建错误码系统或重复预解析层。

Python strict_json_loads 对 escape 的行为与 C# 不同；先用同一合成输入比较观察结果，再确定哪项是协议必须一致。只对强模型确认的共同语义作最小调整；若需修改 scripts/herddesk_g0/protocol.py，须以 P4 的共同协议边界为限，不改未涉及的输入校验逻辑。

字段 value/name 和 terminal.closed.reason 三种核心回归、正常 Unicode pair 对照和同实例后续 valid 帧检查必须独立于修复实现。先枚举 :48 reason.GetString、:101 helper GetString、:119 property.Name，再确认全部字符串读取进入同一异常收口；不得只修 helper 和 name 而漏 reason。现有 Reject helper 仅接受任意 TerminalProtocolException；新增窄 helper/断言必须校验 code，不能误把第一次就因不相关原因拒绝当作通过。

所有权：本任务独占上述 C#/Python/fixture 文件；正式 AGENTS/spec 文案交给规则子任务单一负责人。
回滚：按本任务 diff 回退 parser 和相应测试；保留研究复现证据。无需改用户环境或调用外部系统。
