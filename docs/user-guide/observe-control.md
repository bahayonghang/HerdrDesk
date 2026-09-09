# Observe and control (L1)

Default is observe. HerdDesk owns the connection. herdr owns the agent and PTY.

`ControlLeaseCoordinator` and `TerminalControlViewModel` encode that policy in L1 tests. They are not live lease evidence. AC06/AC07/AC14/AC16 stay `not_run`.

## Policy that is already in code

- Observe does not send input.
- `ControlVerified` is not set from the first frame, process alive, or window focus.
- Takeover is an explicit grant. There is no global skip and no always-takeover flag on the ViewModel.
- After disconnect, queued input is not replayed. Old `ConnectionEpoch` input is rejected.
- Closing this application's view must not stop the user's herdr daemon or existing agent.

## What this page is not

This page does not name unbuilt WinUI buttons. Live lease, live takeover, and independent-user observation stay UNVERIFIED. Missing grant: `no_authorized_independent_user_walkthrough`.
