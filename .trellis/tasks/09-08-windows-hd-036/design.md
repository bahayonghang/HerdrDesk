# HD-036 设计：文档、证据与发布交接

## 拟建/修改产物

| 路径 | 权威关系 |
|---|---|
| `docs/user-guide/` | 安装、设备/会话、终端输入、通知、文件、设置、诊断和更新操作说明 |
| `docs/release/support-matrix.md` | 从最终兼容/测试证据投影人类可读支持组合 |
| `docs/release/notes.md` | 候选版本变化、已验证范围、限制与回滚入口 |
| `evidence/releases/` | 最终 SHA、包/sidecar hash、AC证据索引与审核结果 |
| `planning/acceptance.json` | 仍是原 48 项 AC 的状态权威，只写入对应证据支持的结论 |
| `planning/backlog.json` 与 `tasks/HD-*.md` | 同步产品任务状态与完成证据，不改 docs/plan 历史副本 |
| `README.md` | 更新实际安装入口、支持范围及文档链接；独立客户端声明 |
| `.trellis/tasks/` | 按本任务与父任务的验收证据收口；未完成/条件任务不冒充完成 |

本轮 task-map/acceptance-map 只是实施规划索引，最终发布仍以原 planning 文件及绑定产物的证据为准。避免同一个“已通过”状态在三份新 schema 中维护。

## 文档信息架构

1. 安装与前提：Windows支持组合、runtime、现有 herdr 前提、普通用户安装、包来源。
2. 首次连接：显式 endpoint/named session；显示 daemon 不可达、未知协议、权限不足的诊断路径。
3. 日常操作：设备→会话→工作区→pane，Ctrl+K，观察/控制/释放/接管差异，关闭视图与关闭 pane 差异。
4. 输入与 TUI：IME/复制粘贴/控制键/鼠标/滚动，按 agent 版本列已测能力，Shift+Enter 与图形协议不做统一保证。
5. 多设备：OpenSSH config/key/agent/ProxyJump，host 指纹核验，认证停止重试，helper 安装同意。
6. 文件：双栏、目标确认、进度/取消/冲突，路径粘贴与附件接受的区别，上传后不自动提交。
7. 运维：日志预览/隐私、离线/过期/未知结果、更新与回滚，不以重启 daemon 作为通用修复步骤。
8. 键盘/屏幕阅读器/DPI/主题与 known limitations。

UI 截图和测试数据在后续获批的产品 UI 验收中生成；本轮不能用虚构截图冒充现有 App。

## 追溯记录最小字段

每条证据记录 AC、HD/Trellis 路径、测试文件/步骤、环境/版本、预期/实际、pass/fail/not_run、人工验收者、日期、commit、证据路径；最终发布记录产物名称与 SHA256。已有 schema 可容纳就复用，不为规划增加运行时数据库。

一个 AC 的 Windows/Linux/agent 子矩阵可能分开。只有承诺组合全部通过，才允许该 AC 整体通过；未覆盖项留空/未运行并说明。历史 snapshot 与重建后的当前产物不能混用。

## AC39/40/47 的最终候选检查

- AC39：在干净受支持环境从冻结 SHA 进行全部实际工程的锁定依赖还原与构建；记录
  SDK、NuGet/npm/Cargo 精确锁、源与命令结果。无新增代码时复用同 SHA 已有有效运行，
  缺该候选证据才执行；HD-007 早期骨架结果不能代替最终包依赖。
- AC40：完整 `just ci` 和当前 SHA 的必需 Actions jobs 覆盖 format/analyzers/unit/contract
  及后续语言门；读取实际 required-check/ruleset，核对失败状态确实阻断约定合并路径。
  已有合格规则无需写入；只有需变更规则时取得远端写授权。缺规则/失败阻断证据为
  `UNVERIFIED`，不能把本地非零退出或旧 SHA hosted 成功当作替代。
- AC47：针对最终全部生产模块运行已有唯一依赖图检查；Core/Contracts 保持边界，
  fake transport 的业务用例出现在规范测试工程的发现与运行结果中。新增 adapter
  未接入测试或只以空 fake 构建通过，都不能满足此项。

复用 HD-007 及后续任务建立的脚本和测试入口，不新增第二套门。最终汇总只在本任务
发生，前置功能任务可交付自己的局部贡献，避免完成条件倒挂。

## 发布步骤边界

先准备候选包、所有文案与本地核验，审阅后才决定外部渠道写入。发布前冻结 SHA，重新计算产物 hash 并确认签名；若任何产品修复改变产物，重做受影响的验证与签名审核。旧 hosted Actions 不可作为新 SHA 的门。

AC1 由独立用户流程验证；AC2 由支持矩阵行的证据绑定；AC3 由逐项 AC 追溯检查；AC4 由实际产物 hash/签名；AC5 由父集成结论与明确发布结果支撑。不能以 Markdown 链接存在代替测试事实。
