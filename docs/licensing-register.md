# HD-002 · License and naming register

| Item | Origin / role | Initial handling | Status |
|---|---|---|---|
| HerdDesk name | Requested by repository owner | Independent project label; no official affiliation | Name selected; trademark/package checks not run |
| New Python/C# code | Written for this initial increment | Included as source; no automatic permissive license selection | Project-wide license pending maintainer decision |
| Prior plan and diagnostic fixtures | User-supplied implementation bundle | Archived intact in docs/plan; active probe improved separately | Included; no third-party application sources bundled |
| herdr v0.8.2 | Protocol reference | Fixed source locations and blob IDs recorded; no binary/source vendoring | Distribution/NOTICE review deferred until packaging |
| herdrm | Product reference only | Do not copy code/assets without clear permission | No copied files |
| Python / .NET | Host toolchains | No runtime/SDK binaries redistributed | Runtime licenses remain upstream |
| GitHub Actions | CI tools | checkout/setup-python/setup-dotnet commit-pinned | No vendored action code; hosted execution not run |
| WinUI / WebView2 / xterm / Rust dependencies | Future product dependencies | Not introduced in this BCL-only G0 increment | License/version/lock review required before introduction |

HD-002/AC02 is not complete: project license, trademark/package identity and release dependency inventory remain unresolved. This register is an engineering inventory, not legal clearance.
