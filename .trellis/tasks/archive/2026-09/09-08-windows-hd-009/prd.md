# HD-009 · 类型契约与状态投影

状态：`planning`。本任务不请求 snapshot、不开事件订阅、不实现 UI。

## Goal

把 HD-008 交付的 raw RPC document 按实际 schema 解码为 HerdDesk 自有、可前向降级的类型，并建立以完整 device/session/workspace/pane 身份和 `ConnectionEpoch` 隔离的可丢弃状态投影，供 actor 与 UI 读取而不泄漏 transport JSON。

## Confirmed facts

- 当前已编译身份是 `DeviceId`、`SessionKey`、`PaneKey`、`ConnectionEpoch`，输入上下文含 `ControlVerified=false` 默认值：`src/HerdDesk.Contracts/TerminalModels.cs:4-23`；不能用 pane id、标题或 agent type 当全局 key。
- Contracts 只拥有 HerdDesk 类型/ports，Core 拥有 Store/policy，RPC 具体 DTO 属 Infrastructure 边界：`docs/plan/docs/03_架构与数据流.md:64-76`。
- 上游 operation、required fields、event filters 必须来自实际 schema；规划钉 v0.9.0 schema `protocol=22` / `schema_version=1`。未知 field 可保留/忽略，未知 enum 必须保留 raw value 并降级：`docs/plan/docs/05_协议与接口契约.md:38-48`。客户端内嵌 schema 不能冒充运行 daemon。
- AC04 和 AC11 在 `planning/acceptance.json:29-34`、`planning/acceptance.json:85-90` 仍为 `not_run`；本任务同时依赖 HD-008 的 raw envelope 和实际 schema/hash。

## Requirements

- R1：增量扩展现有身份类型，禁止从规划草案整文件覆盖源码；所有 workspace/pane/agent 投影都携带可回溯的完整 Session/Pane identity。
- R2：decoder 输入必须绑定已记录的 CLI/server/schema protocol、schema version 和 SHA-256；客户端 schema 不能冒充运行 daemon 版本，无法配对时状态为 Incompatible/Unknown。
- R3：为 snapshot、subscription event 和 capability 建立实际-schema DTO；required 缺失、wrong type、duplicate key、非法数值 fail-closed，optional 缺失保留缺失语义，不用默认值制造状态。
- R4：未知 object fields 以 owned cloned `JsonElement` 扩展集保留在 transport DTO；未知 enum 用 `raw + known?` value object 保留。UI/Core 不得各自重新解析同一 raw 字段。
- R5：DTO→domain projection 的单一 mapper 只输出 HerdDesk 概念；Store 是 herdr 权威状态的可丢弃副本，不持久化回服务端、不把本地排序/名称覆盖 upstream。
- R6：Store 只接受带当前 `ConnectionEpoch` 的完整 snapshot 或定向权威读取；旧 epoch、错误 SessionKey、重复实体和 dangling parent 一律拒绝，不做部分安装。
- R7：能力采用已验证 operation 集，不把 missing/unknown 写成已测 false；未知 protocol/schema 自动移除所有 mutation/control capability，但允许显示安全的版本/不兼容状态。
- R8：暴露给 UI 的 `DeviceProjectionSnapshot` 不含可变字典或活的 `JsonDocument`；一次安装原子替换整图，并提供本地 revision 仅供观察，不能当 upstream revision/event cursor。
- R9：本任务不做订阅竞态、通知、搜索、resource commands 或控制权；HD-010/011/012/016/017 只能消费 typed snapshot/capabilities，不能绕回 raw JSON。

## Acceptance criteria

- AC1（R2/R3；原 AC04 贡献）：基于已记录 schema 的 snapshot/event required-field fixtures 全部通过；missing/wrong/duplicate/error fixtures 返回稳定、脱敏代码。AC04 最终还需 HD-008 transport 与 HD-010 subscription/收敛证据。
- AC2（R4；原 AC04/AC11 贡献）：加入未知 field 和未知 enum 后 decode 不崩溃；field value 是 owned clone，原 `JsonDocument` dispose 后仍可审查；raw enum 可显示但不启用能力。
- AC3（R1/R6）：相同 pane/workspace/agent id 出现在不同 DeviceId 或 SessionKey 时不碰撞；重复 key、缺 parent、跨 session parent 在安装前拒绝整份 snapshot。
- AC4（R6/R8）：旧 epoch snapshot/entity read 返回 `stale_epoch` 且当前图字节等价不变；新 epoch 首次成功安装一次性替换旧图，不混合两个 epoch。
- AC5（R5/R8）：所有 public projection 是 immutable/owned；Store 可从同一 fixture 重建出等价图，清空 Store 不影响配置或上游状态。
- AC6（R7；原 AC11 贡献）：未知 protocol、缺 operation、schema hash 不匹配均使 mutation/control capability 缺失；最终按钮禁用还需 HD-011，命令拒绝还需 HD-016/017，故本任务不单独关闭 AC11。
- AC7（R9；原 AC47 贡献）：Core mapper/Store 只使用 Contracts，并以 fake decoded input 单测；不加载 Infrastructure、WinUI、WebView2、SSH 或真实 herdr。

## Out of scope

- 不生成未经核验的方法名/字段，不把 dynamic future schema 强加给 pinned stable；不实现 event loop、刷新定时器、UI view model、SQLite/cache 持久化或 resource mutation。
- 不保存 terminal frame/输入正文，不运行 live herdr；真实 schema/fixture 取证归 HD-001/008，且需脱敏后才能进入仓库。

## Blocking gate

HD-008 未交付 owned raw envelope，或 runtime/client schema hash 无法解释 required fields 时保持 blocked。schema 中仍不明确的字段在设计中标 Unknown，不由实现者猜默认值。
