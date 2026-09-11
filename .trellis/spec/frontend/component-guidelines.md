# Component Guidelines

WinUI `Page` / `UserControl` conventions. Not React components or props.

---

## Overview

Visible G0 chrome is WinUI 3 XAML bound to L1 ViewModels. Code-behind wires names, keys, and size; policy stays in Core/App ViewModels.

Shipped pages: `Views/ShellPage.xaml`, `SettingsPage.xaml`, `DiagnosticsPage.xaml`, `AboutPage.xaml`. Shipped controls: `Controls/TerminalHost.xaml`, `SearchPalette.xaml`, `DeviceSessionRail.xaml`, `WorkspacePaneTree.xaml`. Many ViewModels still have no XAML.

---

## Patterns

- Shell is a `UserControl` (`ShellPage`) hosted by `MainWindow`.
- Interactive controls set `AutomationProperties.Name`. `AccessibilityNameCatalog` catalogs shipped names; that catalog is not AC37.
- `TerminalHost` hosts WebView2. Do not send JSON RPC on the terminal stdio plane.
- Search uses `Controls/SearchPalette.xaml` and Ctrl+K chrome. A keyboard-chrome overlay is not Narrator workflow completion.
- Four zones in `ShellPage`: device rail, workspace tree, terminal/settings/diagnostics/about host, details. File text in details currently says `文件区尚未启用`; do not invent a live file pane from that placeholder.

---

## Anti-patterns

- Do not grant control, send input, or set `ControlVerified` from focus, first frame, or process-alive.
- Do not add a hidden terminal bridge from Shell selection.
- Do not PackageReference `Microsoft.WindowsAppSDK` 2.4.0 umbrella.
- Do not copy Trellis React component templates into this tree.
- Do not treat `AutomationProperties.Name` or `AccessibilityNameCatalog` as a screen-reader pass.

---

## Examples

Four-zone Shell status and chrome (`src/HerdDesk.App/Views/ShellPage.xaml`):

```xml
<TextBlock x:Name="StatusText" AutomationProperties.Name="连接状态" />
<Button AutomationProperties.Name="设置" Click="OnOpenSettings" Content="设置" />
```

Device rail (`src/HerdDesk.App/Controls/DeviceSessionRail.xaml`):

```xml
<UserControl x:Class="HerdDesk.App.Controls.DeviceSessionRail"
             AutomationProperties.Name="设备与会话">
```

Terminal host (`src/HerdDesk.App/Controls/TerminalHost.xaml`):

```xml
<UserControl x:Class="HerdDesk.App.Controls.TerminalHost"
             AutomationProperties.Name="终端">
    <WebView2 x:Name="TerminalView" />
```
