# HD-029 双栏文件界面

## 目标

建立本地/远端可互换的双栏文件工作区，提供结构化目录浏览、明确源/目标、传输队列、进度、取消和 Replace/KeepBoth/Cancel 冲突确认，任何焦点或设备切换都不能改变已确认 job 的目标。

## 当前事实与边界

- 文件主线是按调用启动的独立 `herddesk-filebridge`，协议由 HD-027 锁定，枚举/传输由 HD-028 提供（`docs/plan/docs/07_多设备SSH与文件.md:27-41`）。
- 本任务来源 `tasks/HD-029.md:5-13`，依赖 HD-028；UI 不解析 `ls`/`dir`、不拼远端 shell、不直接读 SSH 凭据。
- 双栏需覆盖面包屑、懒加载、空目录、权限、取消、速度/剩余量，离线缓存必须灰态（`docs/plan/docs/07_多设备SSH与文件.md:51-53`）。
- 以下 App/Files 路径均为拟建，当前仓库没有文件 UI。

## 需求

- R1：每栏位置是 `Local` 或完整 `DeviceId + RemotePath`，显示 device/session/provider/freshness；不得仅用路径字符串识别远端。
- R2：目录项使用后端结构化 name/type/size/mtime/symlink/permission；不从显示文本重建路径。
- R3：每栏覆盖 loading、ready、empty、error、offline/stale、permission denied、incompatible、cancelled 和 refreshing。
- R4：选择和拖放创建不可变 `TransferDraft`，明确 source、destination directory、设备、条目数、总量是否已知。
- R5：用户确认后创建 `TransferJob` target lease；后续 pane/栏/设备焦点变化不能重定向 job。
- R6：队列显示 queued/preparing/transferring/verifying/renaming/completed/cancelling/cancelled/failed/unknown，完成只在 hash+atomic rename 后出现。
- R7：进度同时显示 bytes/total、速度、ETA（可未知）、源→目标 breadcrumb；0B 不用虚假除零速度。
- R8：冲突无默认静默覆盖；Replace、KeepBoth、Cancel 显示确切路径和作用范围，批量“全部应用”只限当前 draft。
- R9：关闭窗口时存在 active job，v1 默认询问并取消/等待；不留下不可见无限后台作业。
- R10：键盘、screen reader、focus、selection、hover、高对比、窄窗口堆叠模式均可操作。

## 子任务验收

- [ ] AC1（R5, R6, R7）：本地↔远端、远端↔本地的 0B/1KiB/50MiB/大文件 job 显示正确设备和进度，完成后 hash 一致。
- [ ] AC2（R6, R8）：Replace/KeepBoth/Cancel 每条路径结果确定；并发同名时无静默覆盖，UI 不显示假完成。
- [ ] AC3（R1, R4, R5）：切换设备/pane/栏焦点 100 次，已确认 job 的 source/destination 不变。
- [ ] AC4（R2, R3）：空、权限拒绝、symlink、Unicode/空格/长名、网络断开分别准确显示；缓存列表标 stale。
- [ ] AC5（R6, R9）：取消只影响本 job，UI 反映清理中/已取消；不能提前从队列消失掩盖后端清理失败。
- [ ] AC6（R3）：重新加载目录可取消，迟到结果不覆盖新位置；breadcrumb 键盘可达。
- [ ] AC7（R8, R10）：窄窗口可在两栏间切换且持续显示源/目标摘要，冲突 dialog 不丢 focus。
- [ ] AC8（R2, R10）：文件名/错误/路径按不可信文本显示，不触发链接、脚本、导航或命令。

## 与产品 AC 的映射

- AC31（`planning/acceptance.json:245-250`）：本任务贡献传输进度、目标与完成 UI；最终文件综合归 HD-032。
- AC33（`planning/acceptance.json:261-266`）：本任务贡献三种冲突交互；竞争安全最终归 HD-032。
- AC30：消费并回归 HD-028 的结构化列举，最终后端正确性归 HD-028/HD-032。
- AC32：贡献取消/清理 UI，最终故障安全归 HD-032。
- AC37/AC38：贡献文件面的键盘、screen reader、响应式、DPI 与主题；最终归 HD-033。

## 非目标

- 不实现 filebridge wire protocol、hash/rename、路径规范化或 SSH 认证。
- 不提供递归删除、任意远端 exec、同步盘、断点续传或后台常驻文件服务。
- 不将“路径可见/预览成功”解释为 agent 已收到附件；投入 agent 归 HD-030。
