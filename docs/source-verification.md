# 本轮重新读取的上游依据

基线 herdr v0.8.2 固定 commit：`9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c`。2026-09-07 通过 GitHub 连接读取，blob ID 与原规划一致。

| 文件 | 范围 | 本轮使用的事实 |
|---|---|---|
| [src/client/mod.rs](https://github.com/herdrdev/herdr/blob/9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c/src/client/mod.rs#L900-L1220) | 900–1220 | observe/control 桥；bytes Base64 字段；input/resize/scroll/release；EOF 不必有 closed |
| [src/server/render_stream.rs](https://github.com/herdrdev/herdr/blob/9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c/src/server/render_stream.rs#L1-L170) | 1–170 | 每连接 seq，发出后提交递增；ANSI 差分依赖前态；帧内图形插入 |
| [src/ipc.rs](https://github.com/herdrdev/herdr/blob/9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c/src/ipc.rs#L1-L210) | 1–210 | Windows 使用 GenericNamespaced；marker 不是普通字节流；private listener 的 DACL 不能推广成所有 API 管道 |
| [schema](https://github.com/herdrdev/herdr/blob/9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c/docs/next/api/herdr-api.schema.json#L1-L12) | 1–12 | protocol 20 / schema_version 1；仍非运行中 daemon 证明 |

来源只用于协议约束和文件定位；本包没有复制这些上游代码文件。限制 16MiB line / 8MiB frame / 64KiB input、JSON 深度64、异常锁存是 HerdDesk 客户端策略，不宣称是上游协议所有版本的上限。

构建工具核验：[.NET 10 下载页](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) 在读取时列出 SDK 10.0.400；global.json 精确固定此版本。当前环境实际没有 dotnet，下载失败，不把官网有版本等同于本地已安装。

建仓命令依据：[GitHub CLI repo create](https://cli.github.com/manual/gh_repo_create)。`--public --source --push` 是用户本机发布脚本的执行路线；本会话没有执行过成功建仓。
