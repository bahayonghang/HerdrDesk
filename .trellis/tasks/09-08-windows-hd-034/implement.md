# HD-034 实施步骤

## 开始条件

- [ ] 已批准 HD-034 的具体实现方案；HD-007/021 交付工具链、配置和 helper 清单。
- [ ] 发行方确认 Publisher、签名渠道、分发渠道和允许测试的 Windows 机器。凭据未知不阻断离线打包准备，但阻断依赖其结果的签名/安装验收。
- [ ] 读取父 `research/current-state.md`、`research/technical-sources.md` 与本任务三件套；未来路径先创建后运行命令。
- [ ] 最终验证前取得 HD-019/032/033 对同一候选 SHA 的证据。

## 有序交付

1. [ ] 从 HD-007 已实际还原的模板确定单一 MSIX 构建方式，记录最终依赖与 supported OS/RID，不抄展示包版本。
2. [ ] 编写 manifest 和 appinstaller 草案；加入启动激活、通知导航及资源打包，核对最小 capabilities。
3. [ ] 在 `scripts/package_release.ps1` 实现 Build、Verify、Sign 的明确入口；默认无签名服务调用，失败即时返回非零。
4. [ ] 增加 runtime 缺失提示及设置页更新状态，检查 UI 取消/失败/延后路径能回到正常壳。
5. [ ] 与 HD-007 配置存储衔接相邻版本备份/恢复；加入旧格式拒绝与故障中断测试。
6. [ ] 与 HD-021/028 所有权和传输状态衔接安全退出；不接触 daemon。
7. [ ] 加入 `tests/Integration.Windows/Packaging/InstallUpdateRollbackTests.cs` 与 `docs/testing/packaging-manual.md`，标明自动化可证范围和人工步骤。
8. [ ] 在获批隔离机安装、更新、故障、指定版本回滚、卸载；记录每次实际 package identity/hash/返回值。
9. [ ] 将材料交 HD-035 审查和 HD-036 发布追溯；单独取得外部发布授权后才允许向渠道写入。

## 验证命令与证据

现有命令：`just ci` 只代表当前 G0 离线门，产品实施时由 HD-007 扩展并区分平台覆盖。

未来入口由本任务创建：`pwsh -NoLogo -File scripts/package_release.ps1 -Action Build`、`-Action Verify`；参数契约随脚本一起受测，不把未创建入口当已执行证据。签名入口和安装/卸载命令只在绑定发行身份、测试目标并获得授权后运行。

L1：manifest/包内容验证、配置升级失败和恢复、更新状态、活动作业延后。
L3：干净 Windows、runtime 缺失、离线、普通用户、实际签名和包 identity、通知激活、更新时 pane 存活。
结果写到 `evidence/packaging/`，含测试人、环境、commit、包 hash、预期/实际、失败截图/日志、缺失平台。

## 回滚与结束

未通过安装/回滚则保留当前签名版本，停用该候选渠道，不停止 herdr。本任务实现导致的配置损坏先从测试备份恢复。不要执行递归清理未知用户目录。

只有子 AC1–AC5 实际证据齐全才能提交验收结论；本轮规划保持 planning。后续本地提交/归档、远端发布分别按用户授权执行。
