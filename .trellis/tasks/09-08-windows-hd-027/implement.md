# HD-027 · 实施计划

## 启动条件

- [ ] `implementation/status.json` 已权威记录 `phase_gate=passed`，HD-026 与待实施 HEAD 的
  `just ci` 已通过，用户明确批准启动本子任务；本次创建计划不构成批准。
- [ ] 重新核验 HD-020/021/022 的 SSH、部署 manifest、绝对 helper path 和退出契约。
- [ ] 决定并记录 Rust toolchain/依赖精确版本；不得凭文档产品版本猜 package 版本。

## 顺序实施

1. 从 AC30/34 和专题 07/08 建立 requirement→wire field→attack case 对照表。
2. 写规范性 header、kind、双向 sequence、单 job state machine 与 terminal-result 规则。
3. 写 control JSON schema，逐 operation 定义 Unix raw path、十进制 u64/hash 编码、
   parent/target observation、冲突模式、分页和 error envelope。
4. 写 threat model，逐项覆盖 stdout 污染、路径注入、symlink、TOCTOU、oversize、凭据、
   process ownership 和未知版本；明确协议不能单独解决的 OS 原子语义。
5. 生成固定 binary vectors 和 manifest hash；包含合法 list/read/write/rename/cancel 序列。
6. 实现无 I/O Rust codec：增量 header/payload 解析、预分配限额、sequence/state latch、
   JSON validation；不添加 file operation 或 executable entrypoint。
7. 在 Infrastructure 实现 C# 增量 codec；它与 Rust 分别读取规范性 vectors，不能调用
   对方实现或复制对方生成的结果文件。
8. 添加 truncated header/payload、1 MiB±1、gap/replay/wrap、wrong direction、第二 request、
   data-before-accepted、unknown kind/version/flags、terminal-result/exit mismatch 用例。
9. 对 path components 加 Unix non-UTF8、Unicode/空格/换行、`.`/`..`、嵌入 `/`/NUL、
   oversize cursor/identity 的 round-trip/reject vectors；另在 C# adapter 测 Windows
   保留名/非法字符 mapping-required，不建立 Windows wire vector。
10. 加 cancel-before/after-commit、disconnect-before-receipt、重观察、Replace stale target、
    KeepBoth exclusive-create vectors，证明不会把未知结果自动重放成第二次写入。
11. 由 HD-028 owner 评审 write/rename/identity fields 是否足以实现安全 commit；不足只改
    proposed v1，不用未版本化扩展绕过。
12. 完成安全审查与 ADR；记录采用、拒绝方案、兼容策略、已知限制和撤销条件。
13. 运行离线 gates，保存结果；所有 L2/L3 文件系统、SSH、权限/TOCTOU 仍标 UNVERIFIED。

## 验证与未来命令

当前存在的 `just ci` 在变更后运行，只证明离线仓库/G0 gate。以下命令仅在拟建文件
存在后执行，当前仓库没有可调用的 `herddesk-filebridge`：

```powershell
cargo fmt --manifest-path filebridge/Cargo.toml --check
cargo clippy --manifest-path filebridge/Cargo.toml --locked --all-targets -- -D warnings
cargo test --manifest-path filebridge/Cargo.toml --locked
dotnet test tests/Contract/HerdDesk.ContractTests.csproj --configuration Release --filter FullyQualifiedName~Files.FileBridge
```

- L1：双 codec、golden vectors、partial reads、state/size/path corpus；可锁 wire 结构。
- L2：由 HD-028 用真实独立进程和文件系统证明 list/read/write/rename 语义。
- L3：由 HD-032 在真实 Windows+remote 目标证明权限、symlink/TOCTOU 和 UI 冲突流程。
- 无 L2/L3 时不得通过 AC30/34，也不得把拟建命令写成当前已实现能力。

## 回滚点

在 ADR accepted 前协议可整体重写；accepted 后协议变化必须另开获批版本并提供对应
vectors，本任务不增加兼容协商层。任何安全语义无法实现时撤回 v1 接受状态并阻塞 HD-028，不通过增加
generic operation、解析 `ls` 文本或把路径放进 shell command 规避。
