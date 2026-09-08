# HD-006 · 安全边界与 ADR 定稿

## 目标与事实

在 HD-002、003、005 产生可审阅事实后冻结 G0 的最小安全决策，明确后续模块和
spec 的适用边界。当前 Core 只能依赖 Contracts，禁止 WinUI/WebView2/SSH/凭据：
`.trellis/spec/backend/quality-guidelines.md:11-18`。两条通信面必须隔离，目标
模块责任在 `docs/plan/docs/03_架构与数据流.md:64-76`，但尚未实现。

## 需求

- R1：写 ADR 说明 JSON RPC/API socket 与 terminal stdio 的边界、协议所有者和
  禁止混发规则。
- R2：定义 host、renderer、bridge、filebridge、daemon 和 agent 的 process ownership；
  GUI 关闭仅可停止自己的 direct child，不能停止 daemon/agent。
- R3：默认 observe；control、takeover、input、upload 必须有显式授权，断开后不重放。
- R4：将 HD-001/003/004/005 的已证实、未知、阻塞与降级记录写成可执行门。
- R5：未来本任务获批实施且 G0 真实证据被接受后，负责同一任务内的项目阶段规则、
  spec 和状态同步；本轮只写规划，不改变这些文件，也不提前宣称 G0/AC44 passed。

## 原 AC 映射

- AC44-C1（贡献）：renderer 消息的 capability/origin/schema 边界及拒绝行为定稿。
- AC44-C2（贡献）：bridge/filebridge 无任意命令、任意文件、跨目标能力的决策。
- AC44（最终，HD-035）：按最终实现与标准用户运行证据完成安全复核。

## 边界和撤销

不写产品代码或用户全局规则。未来本任务获批实施可同步项目规则；若 HD-003/004/005 任一关键事实未知，则 ADR 保留 Unknown、
功能只读，不以规划文本放行可写开发。
