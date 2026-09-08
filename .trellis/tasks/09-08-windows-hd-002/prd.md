# HD-002 · 名称和授权清点

## 目标与事实

在引入 Windows App SDK、WebView renderer、Rust bridge、npm 资源、图标或安装
资产以前建立来源和分发许可事实。项目许可证尚未选定，公开可见不等于 MIT/Apache
授权：`README.md:80`。当前无 `PackageReference` 只说明 G0 依赖最小，
不证明未来依赖可分发：`scripts/validate_repository.py:17-22`。

## 需求

- R1：登记 HerdDesk、HerdrDesk、牧台和 herdr/herdrm 的使用关系。
- R2：为代码、图标、字体、NuGet/npm/Cargo、renderer assets、安装器和文档素材
  记录来源、版本/hash、license、notice、修改、分发位置和审批结论。
- R3：候选依赖先经过 approved / blocked / pending 准入；未知或冲突者不能进入
  可分发构建。
- R4：lock/SBOM/漏洞扫描/发行复核分别由 HD-007、034、035 补足。

## 原 AC 映射

- AC02-C1（贡献）：现有品牌、代码、文档、fixture 来源齐全。
- AC02-C2（贡献）：未来依赖/资产有可审阅的许可和 notice 准入模板。
- AC02（最终，HD-035）：最终构建确认无未授权 herdrm 代码、素材或依赖。

## 边界

不添加 package、图标、LICENSE 或发布内容；不替维护者作法律结论。候选资产无法
核实时标 `blocked` 并移出构建输入，不能把公开链接作为授权。
