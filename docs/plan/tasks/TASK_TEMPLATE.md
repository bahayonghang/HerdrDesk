# HD-xxx · 任务名称

状态：planned / in_progress / blocked / accepted。阶段：G0/P1/…。

## 目标与证据

明确需求、相关ADR、上游tag/文件/行号和风险；新接口要标注为HerdDesk自有。

## 范围与依赖

拟改文件、不得修改的模块、前置任务与gate；按已有项目规范创建任务。

## 实现步骤

先失败用例与contract fixture，再实现，最后回归。区分synthetic和real capture。

## 验收与完成证据

AC编号、命令、环境/版本、预期、实际、截图或脱敏日志、commit、剩余未验证项。

## 风险、回滚和指标

回归风险、功能级回滚；不得停止用户herdr代替回滚。工作量为有效人日；Before未知则写未测。
