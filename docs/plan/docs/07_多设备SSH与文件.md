# 07 · 多设备、SSH 与文件面

## 1. 设备模型及身份

一个 Device 可以有多个命名 Session。配置只保存 label、SSH alias/解析后的目标、herdr 与 helper 绝对路径、显式 session/endpoint、认证方式引用和功能偏好；私钥/密码不保存到普通 JSON。不要把本地路径、Windows session、WSL distro 和远端 Linux session 合并成一个命名空间。

v0.2 认证基线是 OpenSSH config + key + ssh-agent；Tailscale 可先作为用户已配置的 SSH 连接路径，不能把普通 SSH 成功就标记为通过专门的 Tailscale 交互认证。需要浏览器确认、密码或额外 MFA 的 provider 进入独立验收，后台不得长期挂着不可见提示。

## 2. SSH 命令构造（设计示例）

```text
ssh.exe -T -o BatchMode=yes -o StrictHostKeyChecking=yes <已验证alias> <经过POSIX引用的远端命令>
```

这里 `<...>` 是占位说明，不可原样执行。本地使用 `ProcessStartInfo.ArgumentList`，不经 `cmd /c`、PowerShell 拼接或 shell=true。远端 OpenSSH exec 通常仍经 shell 解释一段命令字符串：需要同时完成本地 argv 和远端 POSIX 参数引用；ArgumentList 并不会自动解决远端引号问题。主机目标不得以 `-` 开头；禁止将 pane 输出拼接进远端命令。

terminal/control 使用远端同版本 herdr 的绝对路径与明确 session；RPC 运行自有 `herddesk-bridge rpc --socket-path ...`。不用 `ssh -tt`，否则可能引入 tty 换行/回显污染 NDJSON。用户 shell 启动脚本若向 stdout 输出 banner，连接应以“协议流被污染”失败并给诊断，不静默跳过未知行。

远端不自动安装、升级或启动 daemon。helper 部署也需一次明确确认，先上传到同用户私有目录临时文件，校验签名/可信发布清单和 hash 后原子替换；不往系统目录写入，不需要 sudo。

## 3. 连接恢复与性能

每设备独立指数退避 1–30s 加 jitter，网络恢复/用户点击可提前触发；认证失败与主机密钥变化不无限重试。重连首先恢复 RPC/状态，terminal 默认 observe，旧输入队列立即丢弃并告知“未发送内容未重放”。对 Create/Close/Rename 等操作超时，先查询确认结果，不能假设 request_id 带来幂等保证。

一设备可能同时使用请求 RPC、事件 RPC、终端和文件连接；记录进程数与 SSH 连接预算。Windows 上不能默认依赖 Unix ControlMaster socket 复用；先使用 agent 避免重复凭据提示，再实测连接开销，必要时做受控 channel multiplexing 而不是盲加连接。#3701 是 preview 机器侧边栏路径报告，只作为复现用例，不代表本设计 ssh-exec 路径必然受影响。[E15](../evidence/版本与证据索引.md#e15) [E18](../evidence/版本与证据索引.md#e18)

## 4. 文件传输路线：保持认证一致，不解析 ls 输出

**1.0 主线选择通过同一 OpenSSH 身份调用独立 `herddesk-filebridge`；不是宣称这就是 SFTP。** 这是本项目新增的可选、按调用启动的文件 helper，不常驻监听。目的在于避免同时实现一套 SSH.NET/私钥/ProxyJump/agent 认证逻辑，导致终端能连接而文件栏不能连接。它与 RPC bridge 分离，不能给 RPC bridge 偷加任意文件执行能力。

文件服务 port 保留标准 SFTP provider 扩展点。团队若已有经过验证的 SFTP 库，且确实支持产品承诺的 OpenSSH身份/跳板功能，可以在同一验收下替换 file helper；未验证前不写“sftp.exe/SSH.NET 能无差别复用所有 OpenSSH 配置”。这种路线选择是工程权衡，不是 SFTP 不适合。

`herddesk-filebridge` 拟提供 list/stat/read/write/rename 的狭窄命令集合，不提供 exec/delete-recursive 任意命令。结构化请求放 stdin，不在远端 shell 命令中拼文件路径；目录响应为受大小限制的 JSON，文件内容用独立有长度界限的二进制流或版本化 chunk 帧。**协议在 P4 设计、测试和安全评审后锁定，本包没有伪造已实现的 wire API。**

### 文件安全与正确性

目录列出 name/type/size/mtime/可选权限，明确显示 symlink；按远端 OS 的文件名规则保存原值，Windows 下载目标做非法字符/保留名冲突处理，无法无损映射时询问而非自动改坏。不要用人类可读 `ls`/`dir` 输出解析带空格、换行或 Unicode 的名称。

上传到同目录随机临时名，使用独占创建、防 symlink 跟随、限额和 hash；完成后同目录原子 rename。Replace 需确认；Keep Both 也需避免 TOCTOU 名称碰撞。取消删除本次 job 创建的临时文件，不按宽泛通配符清理他人文件。断线续传不是 v1 必需，未做协议级偏移/hash校验前只能重新传输。

远端所有路径均受登录用户实际权限约束；本地隐藏“..”不构成安全沙箱。上传目录推荐每用户私有；TTL/容量淘汰只作用于本应用缓存目录、且不能删除正在传输或仍有租约的对象。

## 5. 将文件/图片投入 agent

区分三种意图：粘贴文字；粘贴文件路径；发送图像附件。文本在 controller 下遵守 bracketed paste 模式，不默认附加回车；大段文本或多行中疑似命令时提示确认。图片落成本地/远端私有临时文件后输入路径，只说明“路径已输入”，不把它自动宣称为该 agent 已接收图像。

本地 Ctrl+V 是否由 agent 直接读取系统剪贴板取决于具体 agent/终端路径。herdr Windows 官方列出本地图片桥边界，不能照搬 macOS 的 Ctrl+V 假设。为 agent 提供能力表：支持 direct clipboard / file path / image attachment / unknown；unknown 要求用户确认，不自动绕过限制。[E12](../evidence/版本与证据索引.md#e12) [E15](../evidence/版本与证据索引.md#e15)

文件上传的目标必须显示“设备 → session → pane”；切换设备后尚未确认的拖放不能被重新解释成另一个目标。图片预览与上传是两种能力，支持预览不等于 agent 支持附件。

## 6. UI 验收场景

双栏自由选择本地或远端设备，有面包屑、懒加载、空目录、权限错误、取消、速度/剩余量提示。远端关闭时保留灰态目录，不以缓存列表误导用户为在线。传输完成之后只聚焦当前原目标，不能因为新 pane 获得焦点就粘到新 pane。后台传输是否随窗口退出继续必须明确；v1 默认确认后取消，不留不可见无限作业。
