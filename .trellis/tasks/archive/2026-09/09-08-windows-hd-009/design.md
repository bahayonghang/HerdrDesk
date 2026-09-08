# HD-009 technical design

## Types and ownership

拟在 `src/HerdDesk.Contracts/State/` 增量增加 `ConnectionPhase.cs`、`CapabilityProfile.cs`、`ProjectionModels.cs`、`RpcStatePorts.cs`；保留 `TerminalModels.cs` 中已有身份/terminal 类型。`CapabilityProfile` 保存 client/server version（server 可 Unknown）、protocol/schema/hash 和 `VerifiedOperations`；不使用一组容易把 Unknown 写成 false 的 bool。

拟在 `src/HerdDesk.Infrastructure/Rpc/SchemaV1/` 增加 `SnapshotDto.cs`、`EventDto.cs`、`SchemaValue.cs`、`RpcStateDecoder.cs`。文件名 `SchemaV1` 只有在实际 schema_version=1 与 hash 被执行证据确认后使用；若版本不同则按证据命名，不把计划名当事实。

`SchemaValue<TKnown>` 含原始 wire string 和 nullable known value；比较/显示可用 raw，任何有副作用分支只看 known + VerifiedOperations。DTO 的 extension map 使用 ordinal field name 和 `JsonElement.Clone()`，mapper 不读取它来推断能力。所有 `JsonDocument` 在 decoder 返回前释放，输出不引用其 backing buffer。

## Projection graph

拟在 `src/HerdDesk.Core/Store/` 增加 `DeviceProjectionStore.cs`、`ProjectionGraph.cs`、`ProjectionMapper.cs`。graph 根键为 DeviceId，session 键为完整 SessionKey，workspace 节点保存 SessionKey+upstream workspace id，pane 键为 PaneKey；agent 的 key/parent 只按实际 schema 定义，不用标题或 agent kind。

mapper 两阶段运行：先验证并索引所有节点、required fields、父子引用和 duplicate identities；全部成功后构造 immutable graph。任何错误返回 `ProjectionDecodeFailure(code)`，不留下半个 snapshot。projection 保留 display fields、known/raw state、capabilities 与必要 extension metadata；不保留 terminal bytes、凭据或完整 raw response。

Store 只有 actor 可调用的 mutation API：`InstallSnapshot(epoch, graph)`、`InstallEntityRead(epoch, changeSet)`、`MarkStale(epoch, reason)`、`ClearForNewEpoch(epoch)`；对外 `Read()` 返回 immutable `DeviceProjectionSnapshot`。本地 `Revision` 每次成功 commit 递增，仅供 UI 避免重复刷新；HD-010 不可把它当服务器顺序。

snapshot 安装先检查 epoch 等于 Store 当前 epoch；新 epoch 必须先 `ClearForNewEpoch`，使旧图标记 stale/被替换。定向 read 只有 schema 提供已验证 getter 且 changeSet 仍能满足 parent invariants 才能原子合并，否则返回 `full_snapshot_required` 给 HD-010。

## Error and capability model

decoder 使用稳定类别：`rpc_required_field_missing`、`rpc_field_type_invalid`、`rpc_duplicate_identity`、`rpc_parent_missing`、`rpc_schema_incompatible`、`stale_epoch`；异常/log 不包含 raw JSON、title、cwd 或 endpoint。upstream error envelope 由 HD-008 先分类，本 mapper 不将 error 当空 snapshot。

`ConnectionPhase.Incompatible` 与 `CapabilityProfile.VerifiedOperations` 是后续 UI/commands 的唯一兼容门。未知 enum 不导致全图崩溃，但该实体的相关 mutation capability 不出现。unknown field 保留用于诊断/升级测试，不允许 App 直接强制转换。

## Compatibility and rollback

不做 old-data migration：Store 只在内存，decoder 与 fixture 按 schema profile 成对版本化。上游升级时先添加新 profile/fixtures，再切 compatibility manifest。回滚删除当前投影并从权威 snapshot 重建，不反向写 herdr、不落数据库。
