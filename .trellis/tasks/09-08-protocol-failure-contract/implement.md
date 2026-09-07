# Implementation plan

1. 用户批准父计划后才 task.py start 本子任务。
2. 先补三个既有 C# 解析器必红的 surrogate 回归：type 字段值、属性名、terminal.closed.reason；每次错误断言异常类型与 code，再用同 parser 输入正常首帧并要求拒绝。
3. 增加合法 surrogate pair 对照与最小共同语料，Python 运行相同语义；强模型裁决差异，禁止用宽泛失败断言掩盖不一致。
4. 在 JSON 字符串 materialization 边界作最小修复，逐个覆盖 :48/:101/:119，复查 helper 的 type/encoding/bytes 调用；核心修复由强模型负责/复核，便宜模型只录入已确定案例与执行命令。
5. dotnet build HerdDesk.slnx --configuration Release；dotnet run --project tests/HerdDesk.Core.SmokeTests --configuration Release --no-build。
6. python -m unittest discover -s tests/python -p test_protocol.py -v；全部合成协议与安全回归按 just ci。
7. 强模型复核触发→异常转换→failed latch→下一帧拒绝整条链及实际错误脱敏，不接受仅 green test 的结论。
8. 写结果、检查计数/基线和适用五工具的执行注意事项，交规则/证据子任务回写；G0 仍未通过。

顺序：与 SDK 可并行，本任务内部按红→修复→绿依次执行。初始/最终命令结果保留在任务研究证据，不重复扩大到 live herdr、SSH、GUI。
