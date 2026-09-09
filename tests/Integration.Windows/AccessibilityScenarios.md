# HD-011 L3 accessibility scenarios

These steps are a checklist. They are not Narrator, keyboard, DPI, or theme evidence.

| Scenario | L2 in this project | L3 residual |
|---|---|---|
| Tree, search, and status have AutomationProperties.Name | XAML names parsed | Narrator UNVERIFIED |
| Ctrl+K during IME composition does not switch panes | ViewModel `IsComposing` | Real IME desktop UNVERIFIED |
| 100/150/200% DPI | Layout breakpoints in ViewModel | Live DPI UNVERIFIED |
| Light/dark/high contrast | Theme enum in Settings | Live theme UNVERIFIED |

Do not treat screenshots as AC37 or AC38 pass.
