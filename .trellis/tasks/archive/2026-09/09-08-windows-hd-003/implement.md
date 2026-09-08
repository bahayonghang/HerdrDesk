# HD-003 执行计划

1. 消费 HD-001 的 Windows version/schema/endpoint 事实，拒绝缺证据的路径假设。
2. 在批准的隔离 Windows 资源逐行执行 endpoint 测试矩阵，采集脱敏结果。
3. 更新 synthetic fixture 时标明其模拟边界，不将 fixture 通过写成 runtime pass。
4. 定义 resolver 输入/输出、错误码与“必须显式配置”的降级条件。
5. 将已证实映射传给 HD-008；将 ACL/版本风险传给 HD-006。

## 验证

AC03-C1 需要真实 Windows 证据；结构和 fixture 回归只是辅助。最终 AC03 等
HD-008 在实际 bridge 上确认四类 endpoint 后关闭。

## 追溯表

| 需求 | 子验收 | 设计机制 | 将来测试/证据 owner |
|---|---|---|---|
| R1 | AC03-C1 | DeviceId/SessionKey 输入与 resolver output | HD-003 isolated matrix |
| R2 | AC03-C1 | 七类 endpoint/ACL 情景表 | HD-003 Windows evidence |
| R3 | AC03-C2 | resolver 与 RpcTransport 分层 | HD-008 bridge contracts |
| R4 | AC03-C1/C2 | stable redacted diagnostic code | HD-008 integration tests |

## 规划交付与实施完成的区别

规划完成只表示矩阵、接口和失败策略可审阅。实施完成需要实际 Windows default、
named、explicit、Unicode、ACL/cross-user 结果；最终 AC03 还要求 HD-008 的生产
bridge 连接证据。`not_run` 不是可接受的 pass 替代。
