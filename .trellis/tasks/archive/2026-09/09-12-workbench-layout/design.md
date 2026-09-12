# Design: Shell four-zone visual contract

`ShellPage.xaml(.cs)` owns outer geometry and route visibility. `WorkbenchLayout` owns tab/layout/pane projection only. Keep `DeviceSessionRail` and `WorkspacePaneTree` as separate controls; the latter receives workspace items only. Keep details collapsible through one breakpoint policy and preserve the existing narrow navigation overlay.

Use visible borders/backgrounds/minimum widths at the outer Grid, a docked EmptyBanner, and the existing tab/mosaic controls. Do not fabricate a live catalog to make the screenshot look populated. Layout math continues to consume `LayoutProjection` rects and `PaneVisibilityCoordinator`; no new Core contract is needed.

Rollback is a ShellPage/ViewModel/test-only revert. It must not change AppServices, RPC factories, endpoint resolution, or daemon ownership.
