# Synthetic fixtures

这些数据由本资料生成，不是上游抓包，不含真实会话。字段依据 herdr v0.8.2 的 `src/client/mod.rs:850–1220`。

terminal-valid.ndjson 故意把一个中文 UTF-8 字符拆在两帧，防止逐帧解码设计。input-valid 中包含 Ctrl+C 的编码示例，仅用于验证器，不会由 selftest 发送到任何终端。invalid-cases 中 terminal.granted 是故意构造的非法消息，不是实际 API。

验证器的初始 full、连续 seq、输入非空和大小限制是 HerdDesk 客户端门禁；如真实桥不满足，应记录差异，不擅自宣称上游违反通用标准。

endpoint-cases.json 是 HD-003 resolver 模拟矩阵，不是 named-pipe 真机证据，不能把 AC03 标为通过。

lease-cases.json 是 HD-004 lease 映射模拟，不是 observe/control 真机证据，不能把 AC05 标为通过。real-terminal-v082/ 是空占位索引。

renderer-cases.json 是 HD-005 renderer L1 宿主标本，不是 WinUI、WebView2、IME 或 native 真机证据，不能把 AC08/AC09 标为通过。
