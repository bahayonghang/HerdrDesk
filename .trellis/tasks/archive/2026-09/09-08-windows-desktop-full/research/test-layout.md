# 统一测试布局与发现入口（后续实施合同）

当前只有 `tests/HerdDesk.Core.SmokeTests` 和 Python unittest，见 `HerdDesk.slnx:2`。
下表均为未来产品实施目标。路径是唯一工程入口；功能任务在其下添加用例，不能另起
同名Core/App/FileBridge测试工程，避免有文件但未被构建/发现的测试。

| 工程入口 | 首次建立 | 后续内容 | 环境 |
|---|---|---|---|
| `tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj` | HD-007 | actor、projection、dirty、input/control、operations、预算 | 跨平台L1，fake transport |
| `tests/Unit/HerdDesk.Infrastructure.Tests/HerdDesk.Infrastructure.Tests.csproj` | HD-007 | 配置、诊断、process spec、RPC/SSH/file job、更新策略 | 可跨平台部分L1；OS特定分支显式矩阵 |
| `tests/Contract/HerdDesk.ContractTests.csproj` | HD-007 | Bridge/RPC/terminal/filebridge C#codec与共享vectors | L1字节/协议测试；不连接真实目标 |
| `tests/Unit/HerdDesk.App.Tests/HerdDesk.App.Tests.csproj` | HD-011 | ViewModel、导航、设置、通知、文件意图 | 领域化VM用fake；WinUI引用部分仅Windows |
| `tests/Unit/HerdDesk.Terminal.Web.Tests/HerdDesk.Terminal.Web.Tests.csproj` | HD-014 | Host消息、flowcontrol、renderer状态边界 | Windows SDK所需编译；纯消息部分可headless |
| `tests/Integration.Windows/HerdDesk.Integration.Windows.csproj` | HD-011建壳测试入口；HD-013/014扩展 | Components、Shell、Terminal、Ownership、IME、Files、Security、Packaging | 编译门与真实桌面运行分开；L2/L3需明确实验目标 |
| `tests/Integration.Ssh/HerdDesk.Integration.Ssh.csproj` | HD-020 | SSH身份、部署、透明流、远端故障/文件 | 无目标只编译；实际L2/L3使用显式隔离fixture |
| `tests/HerdDesk.Core.SmokeTests/HerdDesk.Core.SmokeTests.csproj` | 已存在，保留 | 现有BCL兼容回归 | 仍使用dotnet run |
| `tests/python/` | 已存在，保留 | G0诊断与仓库检查 | stdlib unittest |
| `bridge/tests/` 与 `filebridge/tests/` | HD-008 / HD-027 | Rust边界与golden vectors | cargo test --locked |
| `web/terminal/tests/` | HD-014 | TypeScript消息/字节/IME事件契约 | 已锁定npm测试入口；不充当真实IME证明 |
| `tests/E2E/` 与 `docs/testing/` | HD-019起 | 场景卡、脚本、人工矩阵/采集说明 | 不把孤立.cs文件放此处；C#可执行用例放上面的Integration工程 |

## 每个新测试目标的必需动作

1. 用HD-007已验证的测试框架和精确依赖创建目标；不临时换框架或引入未经批准依赖。
2. 注册solution、restore/build/测试清单、对应just recipe和Actions矩阵；Rust与npm使用各自唯一入口。
3. 用测试发现命令/runner列表核对本任务用例实际进入目标，放入失败注入用例证明非零
   退出能到达最终job；不能只凭编译成功判断已执行。测试发现只是wired证明，功能判据仍看断言。
4. Windows/SSH desktop项目可以在离线门编译，但实际运行须显式隔离目标与授权；
   skip/not_run分别输出，缺桌面环境不能记“L3通过”。
5. `just ci`保持平台分支清楚：Linux不盲建WinUI，Windows构建产品。未创建目标不得提前写进CI调用。

## 文件归属示例

HD-028的C#codec测试位于 `tests/Contract/Files/`，由
`tests/Contract/HerdDesk.ContractTests.csproj` 默认源包含或显式Compile/资源规则纳入；
golden vector文件从 `filebridge/spec/test-vectors/` 单一来源加载。没有第二个
`HerdDesk.Infrastructure.FileBridge.Tests` 工程。HD-020/029的ViewModel测试都进入
同一个App.Tests；控件和WebView实际行为进入Integration.Windows中的对应子目录。

这是目录/发现合同，不要求重复测试同一低风险编辑。各任务仍按真实风险选择
L1/L2/L3/L4，不为满足文件数或coverage数字制造镜像测试。
