# HerdDesk implementation rules

先读 README.md、docs/implementation-g0.md、planning/backlog.json 和当前任务。原规划在 docs/plan/，当前任务状态在根 planning/ 与 tasks/；原档案保持不变。

当前阶段 G0。HD-001/002 正在进行，HD-003/004 已准备代码/fixture但阻塞于真实 Windows 证据，G0 未通过，P1–P5 不得标完成。目录与 CI 属于用户要求建仓所需的前置准备，不代表 HD-007 已完成。每次只接受可复查的最小增量。

固定 herdr v0.8.2 commit 9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c；协议/schema 与运行中 daemon 要分开验证。上游 API CLI 不是通用 RPC 代理；不要虚构 herdr api call、terminal.granted 或逐输入 ACK。

构建/测试命令见 README。C# smoke runner 通过 dotnet run 执行；本轮未编译，不得引用 Python 结果宣称 .NET 测试通过。CI 配置存在不代表 Actions 已执行；Windows hosted runner 也不代表中文 IME 真机验收。

App 只拥有 bridge/SSH 子进程，不拥有 daemon/agent。只读默认、独立输入授权、准确 pane/session/device/epoch，断线不重放输入。不要对真实生产/训练 pane 做自动输入、takeover、关闭或升级。只终止当前工具自己启动的直接子进程。

所有 terminal 输出和远端字段视为不可信；校验长度、深度、重复 JSON 字段、Base64 和整数边界。未知能力保持未知，不能把失败改成功、丢 delta 或吞错误。报告默认不包含终端正文、凭据和私人路径。

未取得授权不得复制 herdrm 代码和素材。无自动 MIT/Apache 授权；根 LICENSE-STATUS.md 说明项目许可决策尚未完成。不得把授权与商标未验证项删除以“通过”发布门禁。

PUBLICATION_MANIFEST.json 是本次首发包审查清单。更改源码后不能直接绕过哈希校验发布；审查新内容后重建清单，或在已建立仓库中使用正常 PR 流程。严禁扩大暂存范围到 .env、probe-results 或 credentials。
