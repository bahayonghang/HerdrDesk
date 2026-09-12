# Implement: tab-mosaic

Start after `workbench-chrome` is in_progress or completed, and after parent plan approval.

1. Extend `AppTestHost` with layout/tab fixtures; add `WorkbenchLayout` tests first.
2. Integrate coordinator Show/Hide with slot list.
3. XAML mosaic + tab strip in Zone3.
4. `just ci`. No `--ui` in CI.

Risky: multiple WebView2 in `--ui` operator preview (optional, not required to close the child). Hide unused hosts.
