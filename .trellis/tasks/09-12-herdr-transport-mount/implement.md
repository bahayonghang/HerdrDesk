# Implement plan

1. Define the smallest App-level connection orchestration port and adapter selection contract; keep `UnavailableAdapter` as the fail-closed branch.
2. Wire explicit `DeviceProfile` endpoint/path to `DeviceSession`, decoder/store, state pump and `ProjectionCatalog` without parsing RPC in App.
3. Wire selected-pane observe to `TerminalCliProcessFactory`, current identity/epoch and TerminalHost frame flow; keep control lease unchanged.
4. Register only bridge/terminal/SSH children in `AppExitCoordinator`; add fake lifecycle tests for disposal and daemon non-ownership.
5. Update `tests/Contract/Program.cs` old permanent-unavailable expectations and run Infrastructure/App/Contract runners, `dotnet format`, and `just ci`.

Risky files: `src/HerdDesk.App/Composition/AppServices.cs`, `src/HerdDesk.App/Composition/ShellHost.cs`, `src/HerdDesk.App/MainWindow.xaml.cs`, `src/HerdDesk.App/ViewModels/SettingsViewModel.cs`, new App orchestrator, `src/HerdDesk.Infrastructure/Rpc/*`, `src/HerdDesk.Infrastructure/Terminal/*`, `tests/Contract/Program.cs`.
