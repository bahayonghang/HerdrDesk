# 官方技术来源复核

查询日期：2026-09-08。只读查询；未下载包、还原依赖、安装 runtime 或运行 GUI。本记录是规划依据，精确 package 选择及兼容性归 HD-005/007。

| 来源 | 本轮确认 | 实施影响 |
|---|---|---|
| [Windows App SDK downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads) | 页面 stable 列表显示产品版本 2.4.0，日期 2026-08-13；SDK 从 Microsoft.WindowsAppSDK NuGet 获取 | 产品展示版本不能直接作为 NuGet 包版本。HD-005/007 实际检查包元数据、模板与 restore 后锁定组合 |
| [WebView2 security](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security) | 官方要求验证 origin/消息，避免通用代理，使用结构化 JSON 消息，并建议普通用户完整性级别运行 | HD-014 验证固定资源 origin、宿主绑定和消息；JS 所报 pane/epoch 不构成输入授权 |
| [xterm flow control](https://xtermjs.org/docs/guides/flowcontrol/) | write 异步排队；callback 表示消费，快速生产端需要背压 | HD-014/025 采用有界字节记账与消费回执；callback 不是 GPU 呈现、上游输入 ACK 或业务执行成功 |
| [App Installer update settings](https://learn.microsoft.com/en-us/windows/msix/app-installer/update-settings) | 更新可在启动时检查并提示；ForceUpdateFromAnyVersion 允许降级 | HD-034 规划同一 Publisher 的受控更新/回滚路径；普通通道不默认打开任意降级，必须测试指定回滚包和配置恢复 |

本轮没有重新确认所有上游 herdr 方法，也没有把网页信息升级成目标机器可用性证据。旧 plan 固定 v0.8.2/protocol 20 仍作为待运行核验的基线；升级另经兼容性矩阵。
