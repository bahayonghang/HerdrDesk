# Child task map

Parent: `.trellis/tasks/09-12-herdr-workbench-mount-refactor`

| 顺序 | 目录 | 开始条件 | live herdr |
|---|---|---|---|
| 1 | `09-12-workbench-layout` | 规划摘要获批准后 | 否 |
| 2 | `09-12-herdr-transport-mount` | layout 子任务完成并复核宿主契约 | 仅未来显式 observe 授权；当前否 |

父任务保持 `planning`，不直接 `task.py start`。每个子任务独立验证、回滚和归档；子任务之间的顺序是实施约束，不是 live 连接授权。
