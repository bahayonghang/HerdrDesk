# HD-034 设计：安装、更新与配置回滚

## 文件与责任（全部拟建/后续修改）

| 路径 | 责任 |
|---|---|
| `packaging/HerdDesk.Package.wapproj` 或所选 WinUI 模板的单项目 MSIX 配置 | HD-007 模板验证后只保留一种打包入口，不同时维护两种模板 |
| `packaging/Package.appxmanifest` | 身份、OS 范围、应用激活、所需能力；不授予超出产品实际需要的权限 |
| `packaging/HerdDesk.appinstaller` | 同一 Publisher 的受控更新策略，不填写未经确认的生产 URL |
| `scripts/package_release.ps1` | 参数化本地包/签名/verify 步骤，独立退出码，不在脚本中存签名 secret |
| `src/HerdDesk.Infrastructure/Updates/` | 更新状态、配置备份协调、当前作业检查；不实现通用下载执行器 |
| `src/HerdDesk.App/Views/SettingsPage.xaml` | HD-011 基础上增加当前版本/可用更新/延后/恢复帮助，显示受影响目标 |
| `tests/Integration.Windows/Packaging/` | 安装、升级、失败、回退及应用自有数据边界 |
| `evidence/packaging/` | 版本、测试机器、步骤、签名、hash 与人工验收记录 |

## 主渠道和状态

基线建议使用 App Installer，而不是并行构建另一套后台自更新服务。状态为 Idle → Checking → Available → AwaitingSafeExit → Installing；失败显示 Failed，并保留当前版本/支持诊断。系统安装状态从 Windows 部署结果读取，不由“下载完成”推断安装成功。

普通更新在启动时检查并显示提示，允许用户延后。后台检查/自动安装不是本任务默认需求。官方 App Installer 降级需要独立配置，见父 `research/technical-sources.md`；普通更新通道不默认接受任意降级。回滚使用经过签名验证、用户确认的指定包和操作说明，在隔离机验证它确实能取代当前安装。

## 运行时与身份

HD-007 的工具链锁定决定 framework-dependent / self-contained 的唯一实际配置。必须记录最终包需要的 .NET、Windows App Runtime 与 WebView2。Evergreen WebView2 作为主线；缺失时给受控引导，不静默安装或提升到管理员。

包内 JS/字体/sidecar 随包并有 hash。MSIX Publisher 必须匹配实际签名证书；生产证书与测试证书分开。未获得发行身份时可验证未签名本地构建，不能标发行安装验收通过。

## 配置与跨版本边界

复用 HD-007 的应用数据目录/版本化 JSON 原子写。只支持已发行的相邻版本更新与回退矩阵，不为不存在的历史用户数据编写迁移。需要格式改变时先保留旧文件，转换到新文件后校验并原子切换；不能转换则保持旧配置并报错。回滚恢复旧程序及旧配置副本；不让旧程序猜测新格式。

备份包含设备稳定 ID、偏好和 credential reference，不包含密钥材料。卸载清理仅为本应用目录中用户选择的数据；系统 known_hosts、herdr 数据和远端目录不在清理范围。

## 更新时的进程所有权

活跃传输存在时提示等待，或由用户明确取消该 job 后继续。停止新输入，释放本应用 terminal/RPC/SSH/renderer 连接，确认自己的进程退出后交给系统部署。daemon/agent 不在 cleanup 集合。helper 活跃时不原地替换；保留上一已验证版本，当前连接退出后按 HD-021 的原子切换机制升级。

## AC 机制和失败实验

| 子 AC | 机制 | 独立验证 |
|---|---|---|
| AC1 | 实际 manifest/runtime 探测/安装引导 | 全新用户及缺 runtime VM，实际启动进程与窗口 |
| AC2 | 系统部署事务 + Publisher/hash 验证 | 截断下载、坏签名、锁文件、离线后旧版仍可启动 |
| AC3 | 指定旧签名包 + 配置备份恢复 | 前后稳定 ID 对比，外部 herdr 客户端验证原 pane 存活 |
| AC4 | 作业门/自有资源清单 | 传输中更新被延后；卸载后 herdr/远端/系统凭据保持 |
| AC5 | 产物 digest 与 commit 绑定 | 对最终分发的字节重新计算 hash，不能引用打包前源码 hash |
