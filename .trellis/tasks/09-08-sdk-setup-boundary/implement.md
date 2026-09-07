# Implementation plan

禁止在本轮 task.py start；用户批准最新父计划后才逐项进入实施。

## Order and checks

1. 先建立 design.md 定义的最小可 dot-source 主流程和两个副作用函数边界，再以 fake host / 重定义函数构造 SDK missing、global mismatch、PATH host mismatch 的回归；在修复默认行为和校验顺序前，测试应为红。记录 fake sink 的次数/参数/调用顺序，默认与前置失败均为0次，显式 opt-in 的实际动作也只进入 fake sink。PATH stub 不被当成拦截静态 User setter 的证据。
2. 强模型批准边界后由执行模型修改脚本及薄文档；禁止运行带 opt-in 的真实安装。
3. 运行 python -m unittest discover -s tests/python -p test_setup.py -v；Windows 不得全部 skip。
4. 只读运行 pwsh -NoProfile -File scripts/Invoke-HerdDeskDotnetSetup.ps1；核对成功/失败解释与实际 dotnet --version。
5. just ci。完成后在 AGENTS.md 对应工具链段落及 docs/harness-workflows.md 回写适用五工具的副作用边界（文件由规则子任务统一编辑，传递内容）。

## Dependencies

无前置代码依赖，可与协议子任务并行。README 的实际编辑安排在规则子任务之后或交由单一文档负责人，避免覆盖。

## Acceptance trace

按本任务 prd.md 的 AC 逐项记录命令、退出码、关键输出及未验证项。子任务检查通过不代表父任务或 G0 产品门禁通过。
