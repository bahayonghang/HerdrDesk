# 规划交付核验记录

日期：2026-09-08。基线：main / `1cce6e4`。本记录只证明规划文件与静态结构，
不改变产品 G0、原任务或原 AC 的运行状态。

## 实际检查结果

| 检查 | 结果与边界 |
|---|---|
| Trellis 任务结构 | 父任务与36个直接子任务全部 `planning`；37/37 `task.py validate` exit 0 |
| 整树机械预检 | `plan_precheck.py --include-descendants`：37 members，blocking 0 |
| 来源任务一致性 | 原36项ID、依赖、AC贡献、阶段和估算保留；新增实施/最终证据条件单列 |
| 原验收保真 | 48条原AC标题、完整判据和not_run状态与 `planning/acceptance.json` 一致 |
| 依赖与责任 | 实施及最终证据依赖合并后无环；每条AC有贡献方及最终汇总方 |
| 上下文 | 74份manifest引用存在且无重复；每份恰有一个统一测试布局入口，无 `_example` |
| 排期算术 | 原估算57–99有效人日；加25%储备为71.25–123.75，不是承诺剩余工期 |
| 原仓库结构 | `python scripts/validate_repository.py` PASS：70个JSON、3个工程、36个源HD任务；`csharp_compiled=false`、`windows_verified=false` |
| 变更范围 | 检查时已跟踪文件的 `git diff --name-only` 为空；新增内容限本轮规划及合并审查报告 |
| 独立语义审查 | 整树核对需求、逐分句AC、机制、身份/恢复归属、算术及完成依赖；无未解决的可证实规划缺陷 |

合并审查报告：`.trellis/reviews/09-08-windows-desktop-full.md`。报告与本轮规划为
未提交的新增文件；没有更改gitignore，也未启动、完成或归档产品实施任务。

## 本轮审查后收敛的关键合同

1. 配置由HD-007建立唯一DeviceProfile/SessionProfile[]/AtomicConfigurationStore；
   HD-011完成本地编辑，HD-020扩展SSH，不复制配置store。同Device多SessionKey独立。
2. 每个SessionKey的DeviceSession是恢复timer/attempt唯一owner；远端transport set
   属于该session，profile级认证/host-key阻断不创建第二套恢复调度。
3. HD-018/019只承担P2本地贡献；AC13/14/15由HD-026补齐真实SSH矩阵后汇总。
   AC39/40/47由HD-007交基础贡献，HD-036在最终候选SHA收口，避免前后阶段互等。
4. 本地设置、通知激活、字体缩放、真实IME/终端呈现、CPU测量及文件意图都有子任务
   机制与测试归属；终端显示变化不自动授权上游resize，输入不重放。
5. 测试工程首次建立、solution/just/CI接入和用例发现采用统一布局；源目录或
   编译成功不能代替真实桌面、SSH、故障、资源回收或soak证据。

## 尚未由本轮证明的条件

G0 runtime/schema/endpoint ACL及控制信号、WinUI/WebView2/IME、真实Agent TUI、
SSH身份与透明流、helper部署、文件原子性/故障攻击、性能/8h soak、干净机安装更新、
签名及当前SHA hosted required-check行为仍须由对应实施任务取得实际证据。
本轮没有执行 `just ci`、产品UI、live herdr、SSH、输入/接管、安装或发布。

范围沿用现有plan的完整核心1.0；7个1.x扩展在 `extensions.md` 中作为候选展开，
未被隐式计入36个必做子任务，也未被标为已实施。规划质量通过不等于实施获批，
更不等于G0或48项产品验收通过。
