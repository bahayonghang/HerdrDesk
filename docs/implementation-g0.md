# 第一笔实施增量 · G0

## 交付边界

本次请求包括“公开建仓”和“按规划实施”。GitHub 账号读取成功，登录名为 bahayonghang；目标仓库读取返回404。当前连接没有 create-repository 操作，工具目录检索没有提供另一条已连接建仓能力；容器无 gh，也没有可用的本地 GitHub CLI 发布环境。**远端建仓、push、issue、PR、Actions 均未完成**。这里交付的是待发布初始源代码，发布脚本默认 dry-run。

本次已完成实际代码修改：

- 新增 Python `herddesk_g0.protocol`：字节级有界 NDJSON、严格 JSON/重复键/深度64、canonical Base64、u16/u32/u64 与 bool 分离、序列连贯、首帧 full、异常锁存；原探针接入共享校验器。
- 新增离线 `check_capture.py`，输出只含数量/hash/布尔证据状态，拒绝覆盖，EOF 不冒充 pane/daemon 死亡。
- 新增 C# Contracts、单连接帧解析器与 device/session/pane/epoch/控制证据检查；禁止默认接受 emulator reply。该部分源码未编译。
- 新增公开建仓脚本：核对账号、哈希与目标，暂存清单限定，现有仓库拒绝覆盖，检查远端 public 与 HEAD；没有读取或保存 token。
- 准备三项目 BCL-only solution、exact SDK、双平台 CI、原规划档案和活动任务状态。

## 已运行的检查

Linux / Python 3.13.5：70项新增 unittest 回归通过；probe selftest 23项检查通过；合成capture校验通过；JSON/XML、项目引用与36任务依赖结构检查通过。测试包含100轮随机分块回归，全部为合成数据。测试输出归档在 implementation/。

原规划内的23项探针检查不是本轮新增70项中的子集；分开报告，不合并成产品覆盖率。C#有22项smoke用例源码，全部未执行。Python协议校验与C# parser目前不是一份自动生成代码，后续需共享fixture做跨语言一致性测试。

## G0 状态

| 任务 | 已有准备 | 尚缺证据 | 状态 |
|---|---|---|---|
| HD-001 | 固定commit/blob/header与baseline manifest | 本地/远端binary hash、daemon ping、真实schema hash | in_progress |
| HD-002 | 名称、独立实现边界与许可登记 | 商标/package identity、项目许可决定、分发清点 | in_progress |
| HD-003 | 已读pipe映射源码，列出5类测试fixture | default/named/Unicode/ACL的真机结果及HD-001 runtime证据 | blocked |
| HD-004 | 探针增强、协议校验、合成回归 | 隔离Windows pane实测与HD-001 runtime证据 | blocked |
| HD-005/006 | 保留原规划 | renderer/IME、最终安全ADR | planned |
| HD-007–036 | 只做建仓必要目录/CI准备 | 所有对应前置与产品实现 | planned |

G0 未通过；48项AC都未标passed。没有宣称代码覆盖率、性能、CVE扫描、真实控制权或Windows GUI已经达标。

## 本机继续的执行顺序

先运行Python命令和 .NET build/smoke；使用明确隔离的 Windows herdr session记录 HD-001。然后从实际配置取得 API endpoint，验证 HD-003，不猜 APPDATA marker路径。用默认只读 probe 收集HD-004，再由维护者明确授权在 disposable pane进行control/resize/release。所有真实报告放在 gitignored `probe-results/`；脱敏审查后只提交必要证据。最后才进入HD-005 WinUI/中文IME spike。

C#控制策略是纯逻辑防线，不提供上游控制权确认。未来adapter在无法证明拥有输入权时必须保持 ControlVerified=false；不得把首帧、进程存活或窗口聚焦当作确认。
