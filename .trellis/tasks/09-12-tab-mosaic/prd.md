# Tab 条与分栏 mosaic

Parent: `.trellis/tasks/09-12-herdr-workbench-gap`. Depends on `workbench-chrome` Zone3 host. No live herdr.

## Goal

选中工作区后，中央显示该工作区的 tab 条，并按 `LayoutProjection` 矩形铺可见终端槽（最多 4）。选择不授予控制。

## Background

`TabProjection` / `LayoutProjection` 已在 Contracts（`ProjectionModels.cs:20-50`）。`AppTestHost.SessionState` 传入空 Tabs/Layouts（`AppTestHost.cs:60-69`）。`PaneVisibilityCoordinator` 已实现 4 槽与 WaitingForCapacity。`ShellPage` 只有一个 `TerminalHost`。

## Requirements

- **R1**：`WorkbenchLayout` 从 catalog 选出当前工作区的 tabs、当前 tab 的 layout、每个槽的 `PaneKey` 与 visibility。
- **R2**：无 layout → 单槽。`zoomed` → 只 focused pane。第 5 个可见请求 → WaitingForCapacity 占位，不新建 WebView。
- **R3**：切 tab / Hide 离开的 pane；Show 新 pane。新 epoch。不重放输入。
- **R4**：槽位 `readOnly` 直到 `ControlVerified`。Ready/焦点不置控制权。
- **R5**：单测使用合成 snapshot，不启动 herdr/`--ui`。

## Acceptance Criteria

- [ ] **AC1**：两 tab、同一 tab 两个不重叠矩形 → 两条 tab、两槽；focused tab/pane 与投影一致。
- [ ] **AC2**：五个 pane 的 layout → 至多 4 个 Visible，其余 WaitingForCapacity 文案。
- [ ] **AC3**：选 tab 后 `ControlVerified` 仍为 false。
- [ ] **AC4**：产品 AC 保持 `not_run`。

## Out of scope

live observe、split 树扩展、pane.resize 发给上游、壁纸、超过 4 槽的虚拟化滚动。
