# HD-003 设计

## 测试矩阵

矩阵行是 default session、named session、explicit path、Unicode path、wrong user
和 denied ACL；列为输入配置、解析到的 endpoint 类型、连接结果、权限结果、错误
类别、证据等级。所有探针在可丢弃环境只读运行，未授权时矩阵只列预期和空证据。

## 接口边界

未来 `EndpointResolver` 只把稳定 DeviceId/SessionKey 配置映射到候选 API endpoint，
`RpcTransport` 才连接它。终端 `herdr terminal session` 的 stdin/stdout 由 HD-013
拥有；resolver 不创建 terminal 或从 UI 标题推断 ID。

## 撤销点

发现 ACL 跨用户可访问、路径含糊或版本行为漂移时，不回退到猜测算法；清空自动
发现候选，要求显式配置，记录阻断原因供 HD-006 决策。

## 逐项测试预期

| 情景 | 输入 | 预期 | 失败结果 |
|---|---|---|---|
| explicit endpoint | 用户给定绝对 endpoint | 仅连接该 endpoint 并记录身份 | 不存在/不匹配即拒绝 |
| default session | 无 named session 的稳定配置 | 解析到实际验证的 default 映射 | 映射未知则要求显式配置 |
| named session | DeviceId + SessionKey name | 仅连接该 session 的 endpoint | 不回退 default |
| Unicode endpoint | 合法 Unicode path/name | 字节/编码不替换，映射一致 | 编码错误明确报告 |
| ACL denied | 无权限用户 | 连接失败且分类为 permission denied | 不重试为其他用户 |
| cross-user | 不同 Windows identity | 不可发现/不可连接 | 记录隔离证据 |
| 远程SMB pipe/网络UNC | 显式网络路径候选（不含本机named-pipe命名空间） | 核心1.0拒绝远程SMB端点，不因理论可连而启用 | 不转换为本地路径、不扩大认证面 |

## resolver 概念接口与样例

`ResolveEndpoint(DeviceId device, SessionKey session, EndpointPreference preference)` 的
输出概念为 `ResolvedEndpoint { kind, canonical_location, evidence_id, access_scope }` 或
`EndpointResolutionFailure { code, diagnostic_id, requires_explicit_configuration }`。
样例：`preference=Explicit("\\\\.\\pipe\\herdr-api")` 只能返回同一 canonical
location；`preference=Default` 在无已验证映射时返回 failure，绝不能猜 `%APPDATA%`。
实现阶段应以受控配置对象承载该接口，不能把 UI label/pane title 用作 key。
