# ADR-0001 · 建仓准备不等于跨越 G0 门禁

状态：采用（仅仓库组织与诊断代码范围）。

本文件是仓库组织 ADR，不是规划 ADR-001。规划 ADR-001 至 ADR-007 的 G0 冻结见 [approved-baseline.md](approved-baseline.md)。

用户要求先创建 HerdDesk 公开仓库再开始实施，因此在 G0 内准备 README、目录、活动任务记录、CI 配置和发布脚本。提前准备 HD-007 的目录/CI 不代表 P1 开始或 HD-007 完成；所有业务、RPC 生产后端与 WinUI 产品功能保持后续阶段。

本轮新增 Python 代码限于 G0 探针/离线校验，不以 Python 替换原规划的 .NET Core/Rust bridge。C# Contracts/Core 为可编译目标的实现源码，但当前环境未编译，必须先通过远端/本机 .NET build 和 smoke runner，才可称 .NET 测试通过。

G0 runtime 证据不足时维持只读与未知状态。CI 的 Windows job 仅执行 BCL/诊断测试，不把它当作交互式 IME 或真实 herdr 兼容验收。

回滚：删除未通过的 G0 代码增量、保留原规划与诊断证据；不能通过杀死 daemon/agent 回滚。
