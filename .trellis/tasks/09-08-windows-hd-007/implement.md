# HD-007 implementation plan

本文件中的命令均为未来实施/验证步骤；本轮不运行 `task.py start`、restore、WinUI 或 live 产品。

## Ordered checklist

1. [ ] 复核 HD-005 renderer/package 结果和 HD-006 approved baseline；把 Unknown 留作能力禁用，不以临时 package 继续。
2. [ ] 在独立临时 checkout 查询模板与 NuGet package metadata，记录实际 ID/version/TFM/许可；确认 `global.json` 仍是唯一 SDK pin。
3. [ ] 增量建立 App、Infrastructure、Terminal.Web，以及统一 Core/Infrastructure unit 与 Contract 工程并更新 `HerdDesk.slnx`；保留现有 smoke 和 Linux 可建 BCL 子集，不抢先创建后续专业测试工程。
4. [ ] 写单一项目依赖图检查并添加正/反 fixture：每条允许边通过、每条禁止边都能让 structure gate 失败。
5. [ ] 写精确 package 版本、source mapping 和真实 restore 生成的 lock files；审查 transitive/许可，不手写 lock 内容。
6. [ ] 实现 AppDataPaths、DeviceProfile/SessionProfile/IDeviceProfileStore 与单一 configuration 原子写/备份/显式恢复；向 HD-011 交付每 DeviceId 多 session/endpoint 保存和冲突语义。
7. [ ] 实现受限 DiagnosticEvent、有界 JSONL sink 和 alias projector；用 canary secrets 验证默认日志无敏感值。
8. [ ] 建立唯一 App composition root 和 async disposal 顺序；未实现 adapter 以 unavailable capability 注入，禁止 production fake success。
9. [ ] 扩展 `just ci` 与 Actions：先保留 G0 跨平台 job，再加 Windows desktop；Cargo/npm gate 只在 HD-008/014 入口存在后增量接入。
10. [ ] 更新受影响的 module `CLAUDE.md` 与 backend/frontend spec，使其描述已落地工程；不得把 planning artifact 当当前事实。

## Focused tests and assertions

- 配置 tests：同设备两 session/endpoint 独立 round-trip；首次/覆盖写可读；并发 save 串行；serialize/flush/replace 注入失败后原文件字节不变；高版本/损坏文件不自动覆盖；恢复只使用原 `.bak`。
- 诊断 tests：channel 满有界；rotate 不越预算；device/session 只出现 alias；token/password/private-key/path/ANSI/input canary 在所有日志文件中零命中。
- 架构 tests：Contracts 无第三方引用；Core 只见 Contracts；fake port 可跑 Core；App 是唯一 composition root；生产项目不引用 tests。
- gate tests：format、analyzer、unit、contract 和 locked-restore 各制造一个隔离失败，确认 recipe/job 非零；不把这些故障提交到主变更。

## Proposed commands after their inputs exist

```powershell
dotnet restore HerdDesk.slnx --locked-mode
dotnet format HerdDesk.slnx --verify-no-changes --no-restore
dotnet build HerdDesk.slnx --configuration Release --no-restore
dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj --configuration Release --no-build
dotnet test tests/Unit/HerdDesk.Infrastructure.Tests/HerdDesk.Infrastructure.Tests.csproj --configuration Release --no-build
dotnet test tests/Contract/HerdDesk.ContractTests.csproj --configuration Release --no-build
python scripts/validate_repository.py
just ci
```

当前真实入口仍是 `just ci` 的 G0 集合；上列 test projects/locked restore 在创建前只是拟建命令。Rust/npm 命令由 HD-008/014 生成相应 manifest 后加入，不在此虚构。

## Evidence levels

- L1：temp-directory config tests、diagnostic privacy tests、architecture/contract tests 和跨平台 G0 job。
- L2：受支持 Windows 干净 checkout locked restore + product build；不需要启动 UI/live herdr。
- AC40 的 merge blocking 另需只读获取 GitHub ruleset/required checks；未经远端授权不配置，证据写 `UNVERIFIED`。

## Rollback and handoff

每完成一个项目/存储/gate 切片先保持独立可回退；restore 或边界失败即从 solution/gate 撤回该新增切片，原 G0 gate必须仍绿。禁止删除用户数据作为回滚。向 HD-008/009/011/013/014 交付已验证项目图、composition registrations、配置/诊断 ports 和 gate 接入规则。

AC39/40/47 在本任务交基础贡献；HD-036 在最终候选 SHA 汇总全部语言/模块和远端
required-check 证据，本任务不等待后续模块才完成。
