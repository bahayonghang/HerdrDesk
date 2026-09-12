# Implement plan

父任务只负责集成复核，不直接 `task.py start`。按以下顺序启动子任务：

1. `09-12-workbench-layout`
   - 读取 frontend spec 和 `research/current-gap.md`。
   - 仅修改 App Shell/ViewModel/测试边界；补四区可见几何、空态、详情断点和 tab/mosaic 宿主契约。
   - 先跑 App unit + XAML/surface contract；再跑 `dotnet format` 与 `just ci`。
   - 回滚点：恢复 `ShellPage.xaml(.cs)` 与详情策略，不触碰 transport/daemon。
2. `09-12-herdr-transport-mount`
   - 依赖 layout 的 pane/host 契约；读取 backend/frontend spec 和研究记录。
   - 实现显式授权后的连接编排与依赖注入；复用 `DeviceSession`，不在 App 解析 RPC。
   - 用 fake bridge/child 覆盖 connect→state→projection→terminal→dispose；补失败、旧 epoch、EOF、backpressure 和退出清理断言。
   - 真实 herdr/WinUI/pipe 不在 CI 中启动；回滚时保留 unavailable adapter。
3. 父级集成复核
   - 核对 `ShellPage`、`AppServices`、`ShellHost`、`SettingsViewModel`、`DeviceSession`、RPC/terminal factories 的跨层数据流。
   - 运行 `python ./.trellis/scripts/task.py validate <task>`、App/Infrastructure/Contract runners、`dotnet format --verify-no-changes --no-restore`、`just ci`。
   - 复核 `implementation/status.json`、G0、AC01–AC48 与所有 live/desktop/field 证据仍为未通过或 `UNVERIFIED`。

## Required approval gate

本规划完成不等于实现授权。只有用户明确批准最新规划摘要后，才可对某个子任务执行 `task.py start`；父任务不应先启动。
