# Implement: workbench-chrome

Start this child only after the parent planning summary is approved.

1. Add chrome strings/tests first (`ShellViewModelTests` status assertions).
2. Wire `ShellHost` control ViewModel.
3. XAML: headers, empty banner, control bar, Settings connect.
4. Update `ShellSurface` / `AccessibilityNameCatalog`.
5. Run App unit tests, then `just ci`.

Do not edit `AppServices.CreateProduction` adapters. Do not launch `--ui` from CI.

Rollback: revert Shell XAML/ViewModel commits; coordinators stay L1.
