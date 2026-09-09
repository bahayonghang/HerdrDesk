# HD-035 设计：候选产物审查

## 拟建产物与审查对象

| 产物 | 内容 |
|---|---|
| `evidence/security-release/inventory.json` | 最终包的依赖、资产、来源、精确版本、license、NOTICE、hash；此为证据清单，不新增运行时权限配置 |
| `evidence/security-release/scans/` | 各生态扫描原始结果及工具/库日期 |
| `evidence/security-release/review.md` | 漏洞可达性、信任边界、修复引用、结论和未覆盖项 |
| `docs/licensing-register.md` | 更新现有登记，避免另建矛盾的许可证事实源 |
| `tests/Integration.Windows/Security/` | 真实 WebView/argv/diagnostic/cache 边界回归 |
| `docs/testing/security-release.md` | 可复现的人工/隔离环境攻击步骤 |

审核以安装后的候选包和启动的产品为对象；源码锁文件用于解释产物来源，不能替代最终 zip/MSIX/sidecar 内容核对。把已签名包、发布清单与 fixture 关联到同一 SHA。

## 扫描方式

复用 HD-007 选定的锁定工具，不在审查时临时加一套依赖框架。C# 使用当前 .NET 10 实际可用的 NuGet 漏洞/包清单命令，npm 使用 lockfile 审计，Rust 使用已批准工具版本的 advisory/license 检查。执行前检查工具可用性、网络与退出码含义；工具未安装/数据库下载失败时该生态是未覆盖，不是零漏洞。

直接/间接依赖都记录。人工复核漏洞是否在本产品路径可触发，保留证据；不单凭分数或一条忽略规则关闭风险。确认可利用严重项必须修复或移除相应发行功能，不能通过长期例外通过 stable。

## 特权边界矩阵

| 边界 | 攻击输入 | 应有结果 |
|---|---|---|
| WebView origin/导航 | iframe、跳转、popup、远端文档向 host 发 input/readFile/exec | 导航/消息被拒绝；无新增进程或文件访问 |
| 身份/epoch | 有效消息改成另一个设备同 pane ID、旧 window/epoch | Host 绑定拒绝，无跨设备输入 |
| 自动应答 | ANSI 查询、OSC52 read/write、链接伪装 | 无观察者写回、无自动剪贴板泄漏、链接只由用户手势并经方案许可打开 |
| SSH | 以选项起始的 host、空格/引号/换行路径、banner、key change | 在可信边界拒绝/错误诊断；stdout 不被清洗成伪合法 RPC |
| 文件 | symlink/路径遍历/并发替换/保留名 | 使用 HD-032 真实故障证据，无源文件/他 job 数据损坏 |
| 更新 | 错签名、错误架构、过期 manifest、活跃 helper 替换 | 安装/启动失败清晰，旧版本仍可恢复 |

攻击环境必须隔离且获批。不要在用户真实 agent/session 执行注入测试或读取真实凭据。

## 隐私验证机制

用专用 synthetic canary 标记密码、输入、ANSI、文件正文、用户名和路径。触发超时、异常、崩溃诊断、队列错误、传输取消，搜索各实际输出面；默认导出保留技术类别/脱敏 ID/版本而非正文。用户显式选择保留的排障字段须先预览，取消后不生成或发送外部数据。

## 验收与修复交接

AC1 由产物清单与包解包复核支撑；AC2 由各生态成功扫描和人工 reachability 支撑；AC3 由真实进程/文件观察支撑；AC4 由 canary 故障实验支撑；AC5 由最终报告绑定 SHA/hash 支撑。发现缺陷交拥有该模块的 HD task 修复，审核者复测受影响路径并检查产物重建后的漂移。报告必须列“已测”“失败”“未覆盖”，不能用整体一句通过覆盖例外。
