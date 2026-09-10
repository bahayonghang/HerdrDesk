# HerdDesk.Integration.Windows

[根索引](../../CLAUDE.md) · [tests](../CLAUDE.md)

HD-011 L2 控制台 runner。`net10.0` only。无 `PackageReference`。无 `Microsoft.NET.Test.Sdk`。Ubuntu 与 `HerdDeskBclOnly=true` 均可编译。不启动 WinUI 窗口。不泵 message loop。

解析 App XAML 名称与 AutomationProperties，并复用 BCL `ShellViewModel` / `AppActivationCoordinator`。可调用 HD-033 L2 采集器；默认 runner 不启动窗口、不跑 8h soak。`--hd033-collect` 只写 gitignored `probe-results/`。L3 IME / Narrator / DPI 为 UNVERIFIED。产品 AC19 未通过。`integration_windows_project=true` 不得写入 evidence catalog。
