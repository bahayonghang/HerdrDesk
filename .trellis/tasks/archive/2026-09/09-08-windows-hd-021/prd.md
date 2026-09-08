# HD-021 · 跨平台 helper 构建与部署

## 目标与用户价值

为本地与远端 RPC bridge 提供可追溯、架构匹配且可安全回滚的 helper 产物；远端安装必须由用户针对具体设备、版本和 hash 明确同意。
部署失败不得破坏上一可用 helper，也不得把 sudo、远端工具链或在线下载变成隐藏前提。

## 已确认事实

- 原任务处于 P3，依赖 HD-008 与 HD-020，源范围是多架构产物、同意、校验和原子更新：`tasks/HD-021.md:3-17`。
- 原 AC25 的完整分句是安装需同意、平台架构正确、签名/可信 hash、临时写入+原子替换且不使用 sudo：`tasks/HD-021.md:19-21`。
- 规划要求 Windows x64、Linux x64/ARM64、macOS x64/ARM64 独立目标；远端不得假设 Rust/.NET/Python 已安装：`docs/plan/docs/05_协议与接口契约.md:50-54`。
- helper 必须写入同用户私有目录，验证后替换；应用更新与 helper 兼容检查不能替换活跃传输中的二进制：`docs/plan/docs/07_多设备SSH与文件.md:17-19`、`docs/plan/docs/11_工程化发布运维.md:11-17`。
- 当前 solution 只有 Contracts、Core、SmokeTests，`bridge/`、Infrastructure 与 App 都尚未实现；现有检查不能证明远端部署：`.trellis/tasks/09-08-windows-desktop-full/research/current-state.md:9-10`、`.trellis/tasks/09-08-windows-desktop-full/research/current-state.md:28-31`。

## 需求

- **R1 构建矩阵**：从 HD-008 的同一 bridge 源与锁文件生成 Windows x64、Linux x64/ARM64、macOS x64/ARM64 的独立产物；每个目标单独记录工具链、源 commit、构建结果与 unsupported 状态。
- **R2 可信清单**：版本化 manifest 为每个产物记录 target triple、OS/arch、文件名、byte length、SHA-256、bridge/protocol 版本和源 commit；运行时只接受构建时嵌入并绑定应用版本的只读 manifest，任意外部路径 provider 不进入 release。HD-034 再证明正式安装包签名与 Publisher 身份，不反向阻塞本任务对可信 hash 链的实现和验收。
- **R3 平台选择**：部署前用固定只读探针获取远端 OS/arch，映射到封闭目标表；unknown/ambiguous/remote Windows 立即阻断，不能猜测、运行首个可执行文件或静默降级。
- **R4 明示同意**：UI 显示 DeviceId 对应的脱敏目标、OS/arch、版本、hash、私有安装目录、将执行的固定操作及回滚；本次内存态确认只覆盖当前 device+version+hash，执行前任一值变化都返回重新确认。取消不留持久同意或“始终允许”状态。
- **R5 私有部署**：仅以登录用户权限写入该用户 HerdDesk 私有 helper 目录，目录/文件最小权限；不写 `/usr`、`/opt` 等系统目录，不调用 sudo，不修改 shell profile 或全局 PATH。
- **R6 bootstrap 与原子性**：不依赖远端预装 bridge/filebridge；用 HD-020 的固定 `ssh -T` 进程把 payload 写入登录用户私有版本目录中的独占 staging 目录。上传前后分别核对 manifest length/hash，以同目录 hard-link no-clobber 发布不可变版本；不支持该原子原语的目标保持 unavailable。
- **R7 激活与并发**：版本化 helper 不就地覆盖；`--version`/protocol 自检通过后按
  `DeviceId+SessionKey+已解析远端身份` 原子更新本地 current receipt，不能用每设备单值
  覆盖不同 endpoint。每个 ConnectionEpoch 捕获 receipt/绝对路径并持有租约；失败、取消
  或晚到旧 epoch 不改变新连接，多实例竞争只能复用相同 hash 的 final。
- **R8 回滚与清理**：保留上一可用版本与 receipt；回滚只切回旧 current，不停止 daemon/agent。清理只能删除本应用私有目录内、无租约的已知版本，默认不自动清理。
- **R9 隐私与错误**：日志只含脱敏 DeviceId、target、version、hash 前缀、阶段和稳定 code；不含主机/user/私有路径、完整远端命令、payload 或原始 stderr。

## 验收标准

- [ ] **AC1（R1–R2；原 AC25）**：每个声明支持的 target 都有独立 manifest 条目、可复现构建记录和 hash；缺 runner/工具链的目标明确 `not_run`，不能混用另一架构产物。
- [ ] **AC2（R2–R3；原 AC25）**：篡改 manifest、payload byte、length、hash、target 或 protocol 任一项均在上传前失败；unknown OS/arch 不建立部署会话。
- [ ] **AC3（R4；原 AC25）**：无确认、取消、version/hash 在确认后变化三种情况均零远端写入；确认视图可键盘/屏幕阅读器读取完整安全信息。
- [ ] **AC4（R5–R6；原 AC25）**：隔离 Linux/macOS 账号中只产生私有目录、独占 staging 与版本文件；无预装 helper 仍可部署，权限正确、远端 length/hash 一致，hard-link no-clobber 成功后 final 才可见，命令记录中无 sudo/system path。
- [ ] **AC5（R7–R8；原 AC25）**：注入上传中断、hash mismatch、自检失败、current 写失败及并发部署，旧 receipt/版本仍可启动；活跃旧进程不被替换或终止。
- [ ] **AC6（R9；原 AC25）**：含秘密哨兵的 host/path/stderr fixture 不出现在默认日志、exception、UI telemetry 或进程参数快照。
- [ ] **AC7（证据）**：L1 manifest/状态机、L2 隔离 SSH 部署、L3 用户同意/回滚分别归档；任一必需平台未跑则该平台不列支持。

## 原 AC 所有权

HD-021 **完整拥有 AC25 的实现与最终验收**。HD-034 可复用 helper 产物做应用打包/更新验证，但不重复或改写 AC25 结论；HD-026 只引用已通过的 helper 能力建立多设备矩阵。

## 范围外

- HD-008 拥有 bridge RPC wire 与源码行为；HD-020 拥有 SSH 身份；HD-022 拥有运行时通道；HD-027 起才设计 `herddesk-filebridge`，本任务不得把文件 API 塞入 RPC bridge。
- 不做在线自更新、远端 daemon/herdr 升级、remote Windows 支持、系统级安装、自动 sudo、无证据的 macOS/ARM64 支持声明。

## 阻塞与回退

若可信 manifest 来源、目标 runner、远端 hash 工具或 hard-link no-clobber 在目标平台未验证，则该 target 保持 unavailable；产品回退到上一 receipt 或禁用远端连接，不执行未校验 helper。
