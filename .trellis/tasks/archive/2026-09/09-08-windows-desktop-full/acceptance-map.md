# 原48项验收逐条追溯

源文字来自 planning/acceptance.json，本轮不改变任何状态。最终汇总责任只决定谁收集完整证据，多阶段实现与测试责任仍由贡献任务承担。所有原 AC 当前均 not_run。子任务可完成自己的局部交付，但只有原 AC 全部分句与承诺环境满足才可整体 passed。

| 原AC | 父需求 | 贡献任务 | 最终汇总 | 证据层 | 原始完整判据 |
|---|---|---|---|---|---|
| AC01 版本与schema | R1 | HD-001 | HD-001 | L2 | 本地/远端各记录 CLI version、daemon ping、schema protocol与hash；不把客户端schema当服务器证明。 |
| AC02 授权与名称 | R1 | HD-002, HD-035 | HD-035 | artifact | 登记上游/依赖许可、品牌/素材来源；没有复用未授权 herdrm 源码。 |
| AC03 端点发现 | R1 | HD-003, HD-008 | HD-008 | L2 | default/named session、显式path、Unicode路径各连到预期API endpoint；错误用户/权限可诊断。 |
| AC04 RPC契约 | R3 | HD-008, HD-009 | HD-009 | L1+L2 | snapshot/订阅及required字段依据实际schema，通过error/unknown字段fixture。 |
| AC05 terminal帧 | R5 | HD-004, HD-013 | HD-013 | L2 | observe获得可解码完整基线；seq/尺寸/Base64正确；EOF被报告而非冒充pane退出。 |
| AC06 control与release | R5 | HD-013, HD-019 | HD-019 | L2+L3 | 一次输入仅一次到达；resize实际生效；release结束桥但pane继续运行。 |
| AC07 控制者互斥 | R5 | HD-016, HD-019 | HD-019 | L2+L3 | 两个observer并存，第二controller不takeover时不夺权；确认takeover后旧控制者停止写入。 |
| AC08 字节正确性 | R5 | HD-005, HD-014, HD-015 | HD-015 | L1+L3 | 中文/emoji/组合字符跨块拆分后无替换字符、无重复；bytes模式可准确传输控制键。 |
| AC09 中文输入法 | R5 | HD-005, HD-015 | HD-015 | L3 | 预编辑不提前发送，提交恰好一次，候选窗定位和IME快捷键优先级通过真机测试。 |
| AC10 Agent文本TUI | R5 | HD-015, HD-019 | HD-019 | L3 | Claude Code、Codex、OpenCode各记录实际版本；导航、确认、取消、输入、滚动通过；图形另列。 |
| AC11 兼容性降级 | R3 | HD-009 | HD-009 | L1+L2 | 未知enum/字段不崩溃；缺少能力禁用相关按钮；未知protocol不自动执行写操作。 |
| AC12 同步竞态 | R3 | HD-010 | HD-010 | L1+L2 | 订阅与snapshot交错期间创建/关闭/改状态后，最终投影收敛，旧epoch消息不回写。 |
| AC13 断线恢复 | R6 | HD-018, HD-022, HD-024, HD-026 | HD-026 | L2+L3 | EOF、daemon重启、网络中断后状态显示过期；恢复先observe、重建基线。 |
| AC14 无输入重放 | R6 | HD-016, HD-018, HD-022, HD-024, HD-026 | HD-026 | L1+L3 | 断网前后排队的敏感输入不被自动重发；状态明确显示未发送或结果未知。 |
| AC15 进程所有权 | R6 | HD-013, HD-018, HD-019, HD-022, HD-026 | HD-026 | L2+L3 | 关闭/崩溃GUI后herdr daemon与已有agent继续；不调用server stop或杀进程树中的daemon。 |
| AC16 主动授权 | R6 | HD-016 | HD-016 | L1+L3 | 默认不takeover、不自动批准、不默默开启agent bypass；破坏操作有目标确认。 |
| AC17 通知一致性 | R4 | HD-012 | HD-012 | L1+L3 | blocked/done转换限流去重；初始snapshot和重连不补发历史done；失联不报运行成功。 |
| AC18 通知跳转 | R4 | HD-012 | HD-012 | L1+L3 | 点击通知定位原device/session/pane；已关闭目标显示过期，不跳到同ID的另一设备。 |
| AC19 全局搜索 | R4 | HD-011, HD-023, HD-026 | HD-026 | L1+L3 | 跨3台设备/100条投影搜索p95小于100ms；聚焦目标正确；不需要100个terminal桥。 |
| AC20 资源操作 | R6 | HD-017 | HD-017 | L1+L3 | 创建/关闭/重命名按实际schema验证；超时后查询再决定，不盲重试非幂等操作。 |
| AC21 跨设备隔离 | R6 | HD-023, HD-026 | HD-026 | L1+L3 | 两台设备均有w1:p1，快速切换/并发事件/旧消息下100次操作零串写。 |
| AC22 SSH主机身份 | R7 | HD-020, HD-024, HD-026 | HD-026 | L3 | 未知host显示人工核验流程；host-key变更阻断；日志与argv不含密码。 |
| AC23 SSH配置 | R7 | HD-020, HD-026 | HD-026 | L3 | 明确的alias/端口/identity/agent/ProxyJump支持组合通过；未支持认证模式显式标注。 |
| AC24 SSH透明流 | R7 | HD-022, HD-026 | HD-026 | L2+L3 | 使用-T而非-tt；远端stdout banner污染会明确失败；stderr不被当作NDJSON。 |
| AC25 helper部署 | R7 | HD-021 | HD-021 | L2+L3 | 安装需同意；平台架构正确；签名/可信hash检查；临时写入和原子替换；不使用sudo。 |
| AC26 多设备恢复 | R7 | HD-022, HD-024, HD-026 | HD-026 | L3 | 一设备失联不阻塞其他设备；指数退避有jitter；认证错误不无限后台重试。 |
| AC27 背压与限额 | R9 | HD-014, HD-025, HD-033 | HD-033 | L1+L2+L3+L4 | 超长行、慢renderer、高输出后内存有界；不丢delta维持假正常；取消可终止本进程。 |
| AC28 性能预算 | R9 | HD-033 | HD-033 | L2+L3 | 按docs/10统一硬件与负载实测冷启动、内存、CPU、输入到呈现p95并留原始数据。 |
| AC29 资源回收 | R9 | HD-033 | HD-033 | L2+L4 | 连接/关闭pane视图100次后桥进程和句柄回到基线容差；无持续增长。 |
| AC30 文件列举 | R8 | HD-027, HD-028 | HD-028 | L1+L2+L3 | 空格/Unicode/长文件名/symlink/权限拒绝准确；不通过ls文本猜文件元数据。 |
| AC31 传输完整性 | R8 | HD-028, HD-029, HD-032 | HD-032 | L3 | 0B、1KiB、50MiB和大文件成功传输，hash一致；进度和目标设备正确。 |
| AC32 取消与清理 | R8 | HD-028, HD-032 | HD-032 | L3 | 取消/断网仅清理本job临时文件；不删除源文件或其他job文件；无假完成。 |
| AC33 冲突处理 | R8 | HD-029, HD-032 | HD-032 | L1+L3 | Replace/KeepBoth/Cancel均有确定结果；竞争创建与同名文件不导致静默覆盖。 |
| AC34 路径攻击 | R8 | HD-027, HD-032 | HD-032 | L1+L3+L4 | 遍历、symlink、保留名、非法Windows字符、并发替换攻击被正确限制/询问。 |
| AC35 附件能力 | R8 | HD-030, HD-032 | HD-032 | L3 | 按agent+平台展示已验证能力；路径输入不声称等于附件接受；上传不自动提交。 |
| AC36 剪贴板 | R8 | HD-031 | HD-031 | L1+L3 | 文字/文件/图像意图区分；OSC52读取默认拒绝；高危多行粘贴有保护。 |
| AC37 可访问性 | R4 | HD-033 | HD-033 | L3 | 键盘与屏幕阅读器可完成搜索、控制、释放、关闭确认；颜色不是唯一状态编码。 |
| AC38 DPI和主题 | R4 | HD-033 | HD-033 | L3 | 100/150/200%及多显示器、深浅色、高对比通过；无持续resize抖动。 |
| AC39 构建可复现 | R2 | HD-007, HD-036 | HD-036 | clean-machine+CI | 精确依赖锁定、SDK记录、干净机还原成功；不写伪造package version。 |
| AC40 CI质量门禁 | R2 | HD-007, HD-036 | HD-036 | CI+GitHub-rules | format/analyzers/unit/contract失败会阻断合并；synthetic fixture与real fixture分开。 |
| AC41 安装包 | R10 | HD-034 | HD-034 | L3 | 干净受支持Windows可安装启动卸载；WebView2/runtime依赖、Publisher身份明确。 |
| AC42 更新回滚 | R10 | HD-034 | HD-034 | L3 | 更新签名验证和失败回滚通过；配置兼容；不强制停止/升级herdr。 |
| AC43 安全与许可 | R10 | HD-035 | HD-035 | artifact+L3 | 依赖扫描日期/工具/结果可追溯；无已确认可利用严重问题或未处理分发授权。 |
| AC44 特权边界 | R1 | HD-006, HD-035 | HD-035 | L1+L3 | 不可信renderer消息不能任意执行命令/读取文件/切目标；标准用户运行。 |
| AC45 支持文档 | R10 | HD-036 | HD-036 | L3 | 安装、认证、能力矩阵、故障诊断、日志隐私和不支持项与实际测试一致。 |
| AC46 稳定性 | R9 | HD-033 | HD-033 | L4 | 8小时代表负载及100次断连/切换通过；失败注入不破坏pane存活。 |
| AC47 依赖边界 | R2 | HD-007, HD-036 | HD-036 | L1+CI | Core/Contracts不依赖WinUI/WebView2/SSH实现；业务可用fake transport单测。 |
| AC48 可追溯发布 | R10 | HD-036 | HD-036 | artifact | 需求→任务→AC→测试证据→产物hash可追踪；未跑项不写Passed。 |

G0审阅使用AC02/44的准入证据；发行前HD035重新核验最终包。AC01的远端runtime基线由HD001在独立实验环境获取，P3的应用SSH矩阵另由HD026验证，不形成循环依赖。AC40必须包含真实required-check规则readback及失败阻断证据；仅YAML或本地exit code不够。Linux构建、Windows桌面、安装签名、真实SSH和hosted SHA是不同维度，不相互借用。
