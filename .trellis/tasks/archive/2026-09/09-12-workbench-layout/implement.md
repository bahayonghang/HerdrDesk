# Implement plan

1. Update ShellPage outer Grid geometry, separators/backgrounds, empty banner placement, details policy and breakpoint handling.
2. Keep `WorkbenchLayout`/mosaic projection bounded to four visible hosts; add or adjust synthetic App fixtures and surface assertions.
3. Verify no pane rows leak into Zone2, no selection grants control, and no production fake-success path is added.
4. Run App unit runner, Integration.Windows runner, `dotnet format --verify-no-changes --no-restore`, then `just ci`.

Risky files: `src/HerdDesk.App/Views/ShellPage.xaml(.cs)`, `src/HerdDesk.App/ViewModels/ShellViewModel.cs`, `src/HerdDesk.App/ViewModels/WorkbenchLayout.cs`, `tests/Unit/HerdDesk.App.Tests/*`, `tests/Contract/AppXamlSurface.cs`.
