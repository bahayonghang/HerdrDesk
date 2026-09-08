# HD-009 implementation plan

以下是计划获批并具备真实 schema 后的步骤；本轮不生成 DTO 或修改 Contracts/Core。

## Ordered checklist

1. [ ] 对齐 HD-001/008 的 client/server/schema/hash 证据，列出 snapshot、subscribe、event 的 required/optional/enum 字段及 operation allowlist。
2. [ ] 从现有 `TerminalModels.cs` 增量添加 ConnectionPhase、CapabilityProfile、projection 与 decoder port；先跑 duplicate symbol/identity 搜索。
3. [ ] 实现 strict schema DTO/decoder、owned extension fields 和 raw+known enum；不让 disposed JsonDocument 逃逸。
4. [ ] 实现两阶段 ProjectionMapper，先验证全图和 parent identity，再一次构造 immutable graph。
5. [ ] 实现单 actor writer 的 Store API、epoch guard、atomic snapshot/entity install、stale 与 local revision。
6. [ ] 用 synthetic schema fixtures 覆盖 required/error/unknown/duplicate/collision；若有真实 capture，只提交脱敏最小 fixture 并标 `real`。
7. [ ] 为 Core 使用 fake decoded data；断言不引用 Infrastructure/UI，未知 capability 在后端已禁用。
8. [ ] 将 typed snapshot/event/capability contracts 交 HD-010；将 read-only projection 交 HD-011/012；将 capability gate 交 HD-016/017。
9. [ ] 更新模块索引/spec 和 HD-007 gate，保留 G0 smoke 对现有类型的兼容测试或有意迁移说明。

## Key assertions

- disposing raw response immediately after decode 后，所有 DTO/projection 字段仍可读；unknown extension/enum round-trip 保留原值。
- 缺每个 required field、wrong kind、duplicate JSON/entity、dangling parent 均拒绝整图，Store revision/graph 不变。
- `DeviceA/session/pane-1` 与 `DeviceB/session/pane-1` 同时存在；错误 SessionKey 的 entity read 无法覆盖另一设备。
- epoch N 的所有晚到结果在 epoch N+1 被拒绝；新 snapshot 原子替换，不出现混合查询结果。
- 未知 protocol/schema/operation 时 VerifiedOperations 为空，Core 不产出可写命令；异常与诊断中无 raw payload。

## Proposed commands after test projects exist

```powershell
dotnet test tests/Contract/HerdDesk.ContractTests.csproj --configuration Release --no-build --filter RpcSchema
dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj --configuration Release --no-build --filter Projection
python scripts/validate_repository.py
just ci
```

L1 使用 synthetic/minimal redacted fixtures；真实 server schema/capture 没有对应 hash 时必须写 `UNVERIFIED`。本任务没有 UI/live 环境验收。

## Rollback and evidence

decoder、mapper、Store 分三段提交/验证；任一段失败先移除 DI consumer，清空内存投影并回到 HD-008 raw diagnostics。不得缓存半解码状态、创建兼容 fallback 或把 unknown 转成允许。记录 fixture provenance、schema hash、测试命令/退出码和 AC1-AC7 结果。
