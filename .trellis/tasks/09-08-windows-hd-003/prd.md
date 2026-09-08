# HD-003 · API endpoint 与 pipe 映射验证

## 目标与事实

验证 Windows 的 explicit endpoint、default/named session、Unicode 路径及 pipe ACL，
不猜测 `%APPDATA%` 路径。原任务为 blocked，等待 HD-001 runtime 事实和 Windows
pipe/ACL 真机验证：`tasks/HD-003.md:1-35`。现有 endpoint fixture 只是候选产物，
不能证明连接到真实 daemon。

## 需求

- R1：输入为用户配置的 DeviceId、SessionKey、显式 endpoint；`PaneKey` 不能替代
  endpoint identity，现有类型定义见 `src/HerdDesk.Contracts/TerminalModels.cs:4-7`。
- R2：验证 default/named session、explicit path、Unicode path 和错误用户/权限的
  可观察结果；不自动跨用户发现 endpoint。
- R3：区分 API socket/pipe 与 terminal stdio；JSON RPC 不得发送到 terminal plane，
  这是后续 bridge 的硬边界。
- R4：失败只输出稳定、脱敏错误分类，不记录 token、terminal 正文和私有路径。

## 原 AC 映射

- AC03-C1（G0 贡献）：在隔离 Windows 环境获得 endpoint/pipe 映射与 ACL 事实。
- AC03-C2（P1 贡献，HD-008）：bridge 对已验证 endpoint 的请求/取消语义通过。
- AC03（最终）：四类 endpoint 均连接预期 API，错误用户/权限可诊断。

## 边界和撤销

不创建 RPC bridge、不写 session、不启动 SSH。映射不明确或 ACL 不符合预期时，
禁用自动发现、只接受显式配置，并将功能保持 blocked。
