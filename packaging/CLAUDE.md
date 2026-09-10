# packaging

[根索引](../CLAUDE.md)

HD-034 L2 unsigned local layout plus lab cert overlay. Not a WAP project. App stays `WindowsPackageType=None`. Lab cert overlay is not AC41. PFX is gitignored. Live install/sign/update/rollback stay UNVERIFIED. AC41/AC42/G0 stay not passed.

## 职责

- `Package.appxmanifest`：lab identity `HerdDesk.Lab` / `CN=HerdDesk Lab (not release)`。不是 Store 身份，不是发行 Publisher。
- `runtime.json`：单一方法 `dotnet_publish_layout`；RID `win-x64`；WinUI 2.3.6；WebView2 Evergreen 为运行时，不是 nupkg 宣称。
- `Assets/`：layout 所需 logo。不是商店图稿验收。
- 构建入口是 `scripts/package_release.ps1`。默认产物在 gitignored `artifacts/packaging/`。未签名本地构建不能当发行安装。lab 证书由 `scripts/new_lab_certificate.ps1` 写入 gitignored 路径；本目录不得存放 PFX。该 overlay 不是 AC41。

## 约束

- 不要加入 `HerdDesk.Package.wapproj`。
- 不要把私钥、`.pfx`、证书写入本目录或 git。
- 不要填写生产 Publisher、timestamp URL 或 App Installer 分发 URL。
- 不要把 WASDK 2.4.0 展示版本抄进 csproj 或本清单。
