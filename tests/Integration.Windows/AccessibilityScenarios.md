# HD-011 / HD-033 accessibility scenarios

These steps are a checklist. They are not Narrator, keyboard, DPI, or theme evidence.
`l3_narrator` stays UNVERIFIED. Screenshots are not AC37 or AC38 pass.

Shipped AutomationProperties.Name values come from `ShellSurface.AutomationNames` and
`AccessibilityNameCatalog`. Keyboard command strings that have no XAML control yet
remain ViewModel names.

## Keyboard (L2 console / XAML names)

1. Start the four-zone Shell. Confirm AutomationProperties.Name on 设备与会话, 工作区树, 终端, 详情, 连接状态.
2. Tab to 打开导航, 设置, 诊断, 关于, 展开详情, 添加设备. Names match `ShellStrings`.
3. Press Ctrl+K. Search palette AutomationProperties.Name is 搜索. During IME composition, Ctrl+K does not open the palette (`IsComposing`).
4. Type a query. Result list keeps name 搜索. Activate a pane. Selection does not set `ControlVerified`.
5. Request control / release control / confirm close use `ShellStrings.RequestControl`, `ReleaseControl`, `ConfirmClose`. No always-takeover.
6. Visit loading / empty / error / offline / expired / permission / disabled / focus states. Each has a text name in `ShellStrings`; color is not the only encoding.

## Narrator (L3 residual)

1. Confirm `Narrator.exe` is present. Do not treat presence as a pass.
2. Launch Narrator against the product UI. Read the names in the keyboard list above.
3. Complete search, request control, release, and close confirm with Narrator plus keyboard.
4. Record expected vs actual speech, operator, date, and commit. Do not mark `l3_narrator` passed from this file.

| Scenario | L2 in this project | L3 residual |
|---|---|---|
| Tree, search, and status have AutomationProperties.Name | XAML names parsed | Narrator UNVERIFIED |
| Settings / diagnostics / about / add-device names | XAML names parsed | Narrator UNVERIFIED |
| Ctrl+K during IME composition does not switch panes | ViewModel `IsComposing` | Real IME desktop UNVERIFIED |
| Request / release / close confirm names | `ShellStrings` catalog | Narrator UNVERIFIED |
| loading/empty/error/offline/expired/permission/disabled/focus | `ShellStrings` catalog | Narrator UNVERIFIED |
| 100/150/200% DPI | Layout breakpoints in ViewModel | Live DPI UNVERIFIED |
| Light/dark/high contrast | Theme enum in Settings | Live theme UNVERIFIED |

Do not treat screenshots as AC37 or AC38 pass.
