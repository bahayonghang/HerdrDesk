# Journal - lyh (Part 1)

> AI development session journal
> Started: 2026-09-08

---



## Session 1: HD-033 L2 theme overlay closeout

**Date**: 2026-09-11
**Task**: HD-033 L2 theme overlay closeout
**Branch**: `main`

### Summary

记录当前系统 theme overlay 与键盘 chrome 诚实化，SDK 改为 10.x 下限；用户授权 L2 overlay 收口归档。AC37/AC38/AC46 与 G0 仍未通过。

### Main Changes

- Shipped collect_theme_overlay.py and wired catalog/validators/tests/docs.
- Cleared garbled UIA names_sample; Ctrl+K is not AC37.
- SDK rollForward=latestMinor and opt-in just dev.

### Git Commits

| Hash | Message |
|------|---------|
| `2fbe3c8` | (see git log) |

### Testing

- [OK] 未运行 just ci（用户要求跳过验证）

### Status

[OK] **Completed**

### Next Steps

- Parent remains planning; live AC37/38/46 still need grants.


## Session 2: Fill WinUI frontend spec

**Date**: 2026-09-11
**Task**: Fill WinUI frontend spec
**Branch**: `main`

### Summary

HD-033 archived; frontend specs rewritten from App WinUI/XAML; bootstrap stays in_progress.

### Main Changes

- Filled .trellis/spec/frontend from src/HerdDesk.App; updated AGENTS/CLAUDE/harness-workflows; parent notes record 36/36 L2 overlay closeout.

### Git Commits

| Hash | Message |
|------|---------|
| `2bc49ce` | (see git log) |

### Status

[OK] **Completed**

### Next Steps

- Do not archive bootstrap until developer confirms. Do not start parent 09-08-windows-desktop-full. No new HD-037 without consent. No live AC37/38/46.


## Session 3: 工作台壳：中文状态与控制栏

**Date**: 2026-09-12
**Task**: 工作台壳：中文状态与控制栏
**Branch**: `main`

### Summary

归档 workbench-chrome。四区骨架改为中文状态、观察栏与 Settings 连接意图；未接 live herdr。下一刀 tab-mosaic。

### Main Changes

- 四区空壳不再用整页欢迎表和枚举 dump

### Git Commits

| Hash | Message |
|------|---------|
| `170ac4e` | (see git log) |
| `b2f88fc` | (see git log) |

### Testing

- [OK] App unit 147/147；just ci 未启动 --ui

### Status

[OK] **Completed**

### Next Steps

- 实施 09-12-tab-mosaic
