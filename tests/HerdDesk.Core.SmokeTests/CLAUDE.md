# HerdDesk.Core.SmokeTests

[根索引](../../CLAUDE.md) · [tests](../CLAUDE.md) · SmokeTests

生成日期：2026-09-08。BCL-only G0 控制台检查程序。

## 职责

用 `dotnet run` 执行解析、输入策略与 endpoint resolver 断言。不启动 WinUI，不连接 herdr。计数以本次运行为准。

## 入口

```powershell
dotnet run --project tests/HerdDesk.Core.SmokeTests --configuration Release --no-build
```

`Program.cs` 顶层语句。失败打印 `FAIL <name>` 到 stderr，退出码 1；全部通过退出码 0。结尾固定声明：`no live Windows/daemon validation`。

项目：`OutputType=Exe`，引用 `../../src/HerdDesk.Core/HerdDesk.Core.csproj`。无测试框架、无 NuGet。

## 用例

解析：full 帧、有序 delta、拒绝初始 delta、缺口、失败锁存、重放、重复键、非法 UTF-8、跨帧 UTF-8 字节 `0xe4` 原样保留、closed、关闭后再帧、`ulong.MaxValue` seq。

策略：已验证控制允许中文 `CommittedText`；未验证 / Observing / 旧 epoch / 其他 pane / `EmulatorReply` / 空输入 / 超限 / 未知 origin / 默认 identity 均拒绝。

Endpoint：加载 `tests/fixtures/endpoint-cases.json` 七行模拟矩阵；Default 不猜 `%APPDATA%` 或常规 pipe 名；Named 不回退 default；拒绝 UNC；`PaneKey` / 标题 / agent 类型不是 endpoint 身份；其他 DeviceId 的映射忽略；fixture 不得当作 runtime pass。合成数据，不是 Windows 真机连接。

## 依赖

Contracts + Core。构造帧使用 `System.Text.Json` 序列化匿名对象，再交给 parser。

## 关键文件

- `Program.cs` — 全部用例。
- `HerdDesk.Core.SmokeTests.csproj`

## 约束

- 禁止改成 xUnit/`dotnet test` 而不更新 README、CI 与本索引。当前 CI 步骤是 `dotnet run --no-build`。
- 合成数据。中文输入只验证策略放行，不验证 IME。
