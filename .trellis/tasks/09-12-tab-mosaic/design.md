# Design: tab mosaic

Parent design owns the algorithm (normalize layout cell rects → proportional Grid; zoomed = one pane; capacity via `PaneVisibilityCoordinator`).

New type in App (BCL): `WorkbenchLayout` + slot records (`PaneKey`, epoch, visibility, normalized rect). `ShellPage` hosts a tab `ListView`/`ItemsRepeater` and a mosaic panel of `TerminalHost` controls created for Visible slots only.

`AppTestHost.SessionState` gets an overload with tabs/layouts. Existing empty-list callers keep single-slot fallback.

`IPaneVisibilityHost` in unit tests: record Show/Hide; `BeginObserve` no-op; never Ready unless the test sets catalog renderer flags.

Do not map split DTOs until rects are insufficient.
