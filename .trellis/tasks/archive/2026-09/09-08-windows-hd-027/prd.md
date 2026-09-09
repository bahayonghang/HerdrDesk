# HD-027 · 文件 helper 协议与安全设计

## 目标与阶段门

锁定独立 `herddesk-filebridge` 的 v1 wire contract、威胁边界和兼容规则，供 HD-028
实现真实文件后端。任务保持 `planning`；只有 `implementation/status.json` 权威记录
`phase_gate=passed`、P3/HD-026 通过、待实施 HEAD 的 `just ci` 通过且用户批准后启动。

## 当前证据

- 当前树没有 `filebridge`、Infrastructure 或文件 UI（`.trellis/spec/backend/directory-structure.md:32`）。
- 旧 plan 选择同一 OpenSSH 身份下按调用启动的独立 filebridge；它与 RPC bridge 分离，
  不是 SFTP，也不常驻监听（`docs/plan/docs/07_多设备SSH与文件.md:29`）。
- 旧 plan 只列出 list/stat/read/write/rename 方向，并明确 wire API 尚未实现、P4 才锁定
  （`docs/plan/docs/07_多设备SSH与文件.md:33`）。
- 当前 `IRemoteFileService` 仅在未编译草案出现，不能当作可用 API
  （`docs/plan/contracts/HerdDesk.Contracts.cs:89`）。

## 需求

- R1：主线实现必须是单独 Rust 进程；不得把文件能力塞入 RPC bridge、WinUI host、
  terminal transport，且不得监听 TCP/pipe 或提供任意 exec/delete-recursive。
- R2：拟建调用固定为 `herddesk-filebridge serve --stdio --protocol 1.0`；本地或经
  HD-022 的 `ssh -T` 启动，路径只进 stdin frame，stdout 只承载协议、stderr 只诊断。
- R3：v1 使用有 magic/version/kind/flags/length/sequence 的定长二进制 header；JSON
  只承载控制/元数据，文件内容只用 data frame，所有跨语言整数/bytes 有规范编码，任何
  长度在分配内存前校验。
- R4：每个进程只处理一个 job、一个操作，不多路复用；请求只允许 list、stat、read、
  write、rename。EOF、gap、重复 frame、未知 kind/version 均 fail closed。
- R5：v1 远端只支持 Linux/macOS 的 Unix raw path components；显示字符串不得回传作路径，
  禁止空/`.`/`..`/嵌入 `/`/NUL，symlink 默认 lstat/no-follow。Windows 只走本机文件
  adapter，不建立 Windows WirePath 或 remote Windows 承诺。
- R6：list 分页且每页有界；control JSON、data chunk、cursor、路径和错误详情分别限额；
  v1.0 schema 严格拒绝未知字段/kind/version，未知远端错误 code 只按脱敏 opaque code 展示。
- R7：协议定义 job state、取消与 commit 竞态、完整性字段、冲突 token、terminal result
  和结果未知后的重观察；Complete 只能在后端完成长度/hash/commit 后发出，协议层本身
  不宣称文件已安全实现。
- R8：复用 HD-020/021/022 的 SSH identity、host-key、绝对 helper 路径及 hash；协议
  不接受密码/私钥，不自动安装，不从当前目录搜索 executable。
- R9：错误码稳定、可扩展、默认脱敏；远端文件名、路径、文件内容和 stderr 都是
  不可信数据，不能进入 argv、日志、UI 命令代理或 HTML。
- R10：协议锁定前必须通过产品 Rust helper 与 C# host codec 的 golden-vector、
  partial-read、边界/恶意 corpus 和
  安全评审；未通过则保留 P4 blocked，不允许 HD-028 猜协议。

## 来源 AC 映射与本任务验收

- AC1（R2–R7）：`protocol-v1.md` 给出逐字节 header、kind、方向、每个 operation 的字段、
  状态序列、限额、path/schema、commit 线性化和错误恢复；拟建命令显式标注为尚不存在。
- AC2（R3/R4/R6/R10）：Rust helper codec 与 C# host codec 对所有 golden vectors
  结论一致，分块读取、oversize、gap/replay、未知 kind 均有反例。
- AC3（R1/R8/R9）：威胁模型证明没有 generic exec、监听器、凭据参数、`ls`/`dir`
  文本解析或显示名 round-trip。
- AC4（贡献 AC30）：协议能无损表达空格、Unicode、换行/长名、symlink 和 permission
  error；AC30 的真实功能最终由 HD-028 完成。
- AC5（贡献 AC34）：协议拒绝 traversal/非法 Unix component，并携带父目录与目标的
  identity/conflict token；C# 本机 adapter 对 Windows 保留名/非法字符返回可询问状态，
  不自动改名。路径/TOCTOU 的真实抵抗最终由 HD-032 验证。
- AC6（R10）：ADR 记录接受/拒绝理由、兼容策略和未解决平台语义；只有 gate 全绿才锁 v1。

## 边界与回滚

本任务不实现 list/write 等文件系统动作、不创建可发布命令、不安装 helper、不运行真实
SSH/文件测试，也不通过 AC30/34。协议评审失败时删除未锁定发布声明，保留 terminal
路径粘贴能力并让 P4 文件面保持禁用；remote Windows 留到另一个获批协议版本。
