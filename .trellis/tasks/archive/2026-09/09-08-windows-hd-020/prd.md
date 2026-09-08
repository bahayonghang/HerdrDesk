# HD-020 · SSH 设备配置与身份

## 目标与用户价值

让用户在 Windows 桌面端安全地新增、编辑和测试远端设备配置，并在建立任何产品通道前看清实际 OpenSSH 配置、认证能力与主机身份。
本任务交付 P3 的 SSH 配置/身份基础，不以静默接受主机密钥或隐藏认证提示换取“连接成功”。

## 已确认事实

- 原任务处于 P3，依赖 HD-019，源范围是 alias/参数、key/agent/host-key 流程：`tasks/HD-020.md:3-17`。
- 原 AC22 要求 unknown host 人工核验、host-key changed 阻断、日志和 argv 无密码；原 AC23 要求 alias/port/identity/agent/ProxyJump 组合与不支持项可见：`tasks/HD-020.md:19-22`。
- 规划基线只承诺 OpenSSH config + key + ssh-agent；密码、额外 MFA、浏览器或专门 Tailscale 交互必须独立验收：`docs/plan/docs/07_多设备SSH与文件.md:3-7`。
- herdr v0.9.0 `herdr machine` 是 **client 本地** catalog，官方文档写明 Windows 多机未支持、Windows 不能当 SSH 宿主。本任务继续用牧台自有 `DeviceId`/OpenSSH，不读写 herdr machine catalog，也不把 `herdr --remote` 或 endpoint generation 1 当作已批准的 1.0 默认，除非父任务开放决策改写。
- 当前 solution 只有 Contracts、Core 和 smoke 项目，没有 App 或 Infrastructure：`HerdDesk.slnx:1-8`；G0 的 Windows live、SSH 和产品验收仍为 `not_run`：`implementation/status.json:31-36`。

## 需求

- **R1 配置编辑**：扩展 HD-007 的单一 `DeviceProfile`：稳定 `DeviceId` 拥有可改 label、Local/SSH 判别；SSH variant 拥有 host alias、可选 user/port/identity-file 引用、可选 ProxyJump alias及远端 herdr/helper 绝对路径。其 `SessionProfile[]` 只拥有 endpoint 与可选 named session；同一设备可保存多个 session，并分别形成完整 `DeviceId+SessionKey`。改名不得改变 `DeviceId`。
- **R2 秘密边界**：配置只保存密钥文件路径或 agent/config 引用，不保存私钥内容、密码、口令、MFA token；UI、持久化、日志和诊断导出不得出现这些秘密。
- **R3 有效配置预览**：用固定本机 OpenSSH 路径执行只读 `ssh -G`，展示解析后的 hostname/user/port/identity/agent/ProxyJump 与来源；UI 只能提交类型化字段，不能拼接 argv 或远端命令。
- **R4 分阶段连接测试**：测试固定为 executable/version → `ssh -G` → host-key 状态 → 用户确认后 `ssh -T` 的无副作用认证探针；任何阶段可取消，且不安装 helper、不启动/升级 daemon、不创建 workspace/pane。
- **R5 主机身份**：unknown host 的扫描结果明确标为“未验证候选指纹”，只有用户经可信侧信道核对并明确确认后才写入 HerdDesk 自有 trust store；changed key 必须硬阻断，连接测试不得覆盖旧记录。
- **R6 认证基线**：支持经过组合测试的 OpenSSH config、显式 identity file、已解锁 ssh-agent、非默认 port 与 ProxyJump；加密 key 只有已由 agent 解锁时进入基线。
- **R7 明确不支持**：密码 argv/stdin、keyboard-interactive、不可见 passphrase prompt、MFA/浏览器确认、专门 Tailscale SSH 交互、PKCS#11/智能卡及安全密钥触摸在本任务中均显示“未支持”，不得后台等待或降级为交互提示。
- **R8 错误与隐私**：验证失败返回稳定分类与阶段，不回显原始 stderr、完整 argv、用户名、主机、私钥路径或候选 key；显式诊断导出仍需脱敏。

## 验收标准

- [ ] **AC1（R1–R2）**：新增→保存→重开→编辑设备后 `DeviceId` 不变，SSH variant 与两个 `SessionProfile` 均 round-trip，两个完整 SessionKey 不互相覆盖；构造含密码/私钥内容的提交被拒绝且文件中无秘密。
- [ ] **AC2（R3）**：alias、port、identity、agent、ProxyJump 的合成配置逐项解析正确；host alias 以 `-` 开头、非法端口、相对 executable/remote helper 路径被拒绝；测试证明 ViewModel 不接收原始 argv。
- [ ] **AC3（R4、贡献原 AC23）**：连接测试严格按阶段推进，取消会在 3 秒内终止本应用直接子进程；无远端安装、daemon start、terminal control 或输入副作用。
- [ ] **AC4（R5、贡献原 AC22）**：unknown 候选在确认前不能连接；取消不写 trust store；确认后精确 key 可用；同 host/port 的 changed key 硬阻断且旧记录不变。
- [ ] **AC5（R6–R7、贡献原 AC23）**：支持矩阵覆盖 config/key/agent/port/ProxyJump；每个未支持认证模式在编辑页和测试结果中显示确定状态，不出现隐藏 prompt 或无限等待。
- [ ] **AC6（R8、贡献原 AC22）**：错误、默认日志和进程快照不含密码、私钥内容、完整 argv 或未脱敏地址；敏感 fixture 的哨兵字符串全局断言不泄漏。
- [ ] **AC7（证据边界）**：L1 fixture 结果与 L2 隔离 Windows/OpenSSH 结果分开记录；没有 L2 时保持 `UNVERIFIED`，不修改 `planning/acceptance.json` 为 passed。

## 原 AC 所有权

- HD-020 对 **AC22** 交付 host-key UI/持久化与脱敏的阶段贡献；HD-024 交付失败停止重试的阶段贡献；**HD-026 统一拥有最终汇总验收**。
- HD-020 对 **AC23** 交付配置与单设备组合的阶段贡献；**HD-026 拥有 Windows+Linux 多设备支持矩阵的最终验收**。

## 范围外

- helper 构建/部署由 HD-021 负责；request/event/terminal 远端通道由 HD-022 负责；重试与 revision 解阻由 HD-024 负责；多设备最终支持声明由 HD-026 负责。
- 本任务不新增第二个 profile/config store，也不为尚未发布的配置格式增加 migration；它直接扩展 HD-007 的同一 document/store。它不新增密码/MFA provider，不修改用户全局 OpenSSH config/known_hosts，不自动接受 key，不拥有或停止 herdr daemon。

## 阻塞与回退

若固定 OpenSSH 路径、`ssh -G` 行为或主机身份流程无法在目标 Windows 版本验证，则设备保持“配置未验证/仅本地”，远端连接按钮禁用；不以宽松 host-key 或 shell 拼接绕过。
