# HerdDesk.Core.Tests

BCL console runner (`dotnet run`，不是 `dotnet test`). Fake ports only. No WinUI/herdr/SSH. HD-009 mapper/Store tests use decoded Contracts inputs and must not load Infrastructure. HD-010 DeviceSession/race tests drive the shipped actor from real start state with fake HD-008 ports, barriers, and an injected `TimeProvider`. L2 live subscribe interleave remains UNVERIFIED.
