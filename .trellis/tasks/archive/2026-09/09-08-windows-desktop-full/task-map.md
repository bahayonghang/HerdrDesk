# 任务顺序与原计划映射

所有子目录均在本父任务 task.json.children 中登记，状态 planning。父子关系只表达归属，执行前仍逐项检查以下依赖。任务正文中的补充集成依赖同样有效。下表“前置HD”保留源backlog，不隐式改写原计划；生效的补充条件见下方。

| HD | 阶段 | Trellis 子任务 | 前置 HD | 原验收贡献 | 原估算人日 |
|---|---|---|---|---|---|
| HD-001 | G0 | [锁定上游事实与版本](../09-08-windows-hd-001/prd.md) | 无 | AC01 | 0.5–1 |
| HD-002 | G0 | [名称和授权清点](../09-08-windows-hd-002/prd.md) | 无 | AC02 | 0.5–1 |
| HD-003 | G0 | [API endpoint 与 pipe 映射验证](../09-08-windows-hd-003/prd.md) | HD-001 | AC03 | 1–2 |
| HD-004 | G0 | [终端桥协议探针](../09-08-windows-hd-004/prd.md) | HD-001 | AC05 | 2–3 |
| HD-005 | G0 | [WinUI renderer与中文输入spike](../09-08-windows-hd-005/prd.md) | HD-004 | AC08, AC09 | 2–4 |
| HD-006 | G0 | [安全边界与ADR定稿](../09-08-windows-hd-006/prd.md) | HD-002, HD-003, HD-005 | AC44 | 1–2 |
| HD-007 | P1 | [仓库骨架与CI](../09-08-windows-hd-007/prd.md) | HD-006 | AC39, AC40, AC47 | 1–2 |
| HD-008 | P1 | [RPC stdio bridge和请求层](../09-08-windows-hd-008/prd.md) | HD-003, HD-007 | AC03, AC04 | 2–3 |
| HD-009 | P1 | [类型契约与状态投影](../09-08-windows-hd-009/prd.md) | HD-008 | AC04, AC11 | 2–3 |
| HD-010 | P1 | [订阅与收敛策略](../09-08-windows-hd-010/prd.md) | HD-009 | AC12 | 2–3 |
| HD-011 | P1 | [本地导航与搜索](../09-08-windows-hd-011/prd.md) | HD-009 | AC19 | 1–2 |
| HD-012 | P1 | [通知与未读](../09-08-windows-hd-012/prd.md) | HD-010, HD-011 | AC17, AC18 | 1–2 |
| HD-013 | P2 | [TerminalCliTransport与生命周期](../09-08-windows-hd-013/prd.md) | HD-008, HD-010 | AC05, AC06, AC15 | 2–3 |
| HD-014 | P2 | [可交付WebView2终端adapter](../09-08-windows-hd-014/prd.md) | HD-005, HD-007, HD-013 | AC08, AC27 | 3–5 |
| HD-015 | P2 | [输入法、键盘与选择](../09-08-windows-hd-015/prd.md) | HD-014 | AC09, AC10 | 2–4 |
| HD-016 | P2 | [控制权状态机](../09-08-windows-hd-016/prd.md) | HD-013, HD-014 | AC07, AC14, AC16 | 1–2 |
| HD-017 | P2 | [workspace与agent基本操作](../09-08-windows-hd-017/prd.md) | HD-009, HD-016 | AC20 | 1–2 |
| HD-018 | P2 | [断连和崩溃恢复](../09-08-windows-hd-018/prd.md) | HD-010, HD-013, HD-016 | AC13, AC14, AC15 | 2–3 |
| HD-019 | P2 | [本地MVP综合验收](../09-08-windows-hd-019/prd.md) | HD-012, HD-015, HD-017, HD-018 | AC06, AC07, AC10, AC15 | 2–3 |
| HD-020 | P3 | [SSH设备配置与身份](../09-08-windows-hd-020/prd.md) | HD-019 | AC22, AC23 | 2–3 |
| HD-021 | P3 | [跨平台helper构建与部署](../09-08-windows-hd-021/prd.md) | HD-008, HD-020 | AC25 | 2–4 |
| HD-022 | P3 | [远端RPC与terminal通道](../09-08-windows-hd-022/prd.md) | HD-020, HD-021 | AC24, AC26 | 2–3 |
| HD-023 | P3 | [多设备聚合与搜索](../09-08-windows-hd-023/prd.md) | HD-011, HD-022 | AC19, AC21 | 1–2 |
| HD-024 | P3 | [认证异常与重连退避](../09-08-windows-hd-024/prd.md) | HD-022 | AC22, AC26 | 1–2 |
| HD-025 | P3 | [连接与带宽预算](../09-08-windows-hd-025/prd.md) | HD-023, HD-024 | AC27 | 1–2 |
| HD-026 | P3 | [多设备MVP验收](../09-08-windows-hd-026/prd.md) | HD-023, HD-024, HD-025 | AC21, AC23, AC24, AC26 | 1–2 |
| HD-027 | P4 | [文件helper协议与安全设计](../09-08-windows-hd-027/prd.md) | HD-026 | AC30, AC34 | 2–3 |
| HD-028 | P4 | [文件枚举与传输后端](../09-08-windows-hd-028/prd.md) | HD-027 | AC30, AC31, AC32 | 2–4 |
| HD-029 | P4 | [双栏文件界面](../09-08-windows-hd-029/prd.md) | HD-028 | AC31, AC33 | 2–4 |
| HD-030 | P4 | [文件与图片投入agent](../09-08-windows-hd-030/prd.md) | HD-017, HD-029 | AC35 | 2–3 |
| HD-031 | P4 | [剪贴板与缓存策略](../09-08-windows-hd-031/prd.md) | HD-030 | AC36 | 1–2 |
| HD-032 | P4 | [文件故障与安全测试](../09-08-windows-hd-032/prd.md) | HD-028, HD-029, HD-031 | AC32, AC33, AC34, AC35 | 2–3 |
| HD-033 | P5 | [性能、可访问性与soak](../09-08-windows-hd-033/prd.md) | HD-026, HD-032 | AC27, AC28, AC29, AC37, AC38, AC46 | 2–4 |
| HD-034 | P5 | [签名、安装与更新回滚](../09-08-windows-hd-034/prd.md) | HD-007, HD-021 | AC41, AC42 | 2–3 |
| HD-035 | P5 | [最终安全与许可审查](../09-08-windows-hd-035/prd.md) | HD-006, HD-031, HD-034 | AC02, AC43, AC44 | 2–3 |
| HD-036 | P5 | [发布文档与追踪归档](../09-08-windows-hd-036/prd.md) | HD-033, HD-034, HD-035 | AC45, AC48 | 1–2 |

## 原始估算复算

| 阶段 | 任务数 | 最小人日 | 最大人日 |
|---|---:|---:|---:|
| G0 | 6 | 7 | 13 |
| P1 | 6 | 9 | 15 |
| P2 | 7 | 13 | 22 |
| P3 | 7 | 10 | 18 |
| P4 | 6 | 11 | 19 |
| P5 | 4 | 7 | 12 |
| 合计 | 36 | 57 | 99 |

25% 储备后为 71.25–123.75 有效人日（按一位小数四舍五入为 71.3–123.8）。原 plan 的 71.2 是小数舍入显示差异，精确计算以这里为准。这是原始任务估算，未扣除已有 G0 准备，也未对本轮细化重新估时；不能当当前剩余工期承诺。工具链/上游控制信号/IME/证书/真机环境会影响排期。单工程师每周4个有效人日时量级约17.8–30.9周，未计外部等待，双人不能简单减半。

1.x 扩展不计入57–99人日；详见 extensions.md。已完成代码准备可在HD001–007逐项复用，不能凭此把某个完整HD任务跳过。

## 生效的补充依赖

- HD-011 实施前增加 HD-010：P1本地设备保存/连接和过期恢复依赖其Session协调层，不能等待P3的HD-020。UI布局草案可提前研究，完整HD-011实施以该前置为准。
- HD-034可在原HD-007/021完成后准备包；其最终验收另需HD-019生命周期、HD-032传输取消和HD-033候选证据。HD-035依赖HD-034交付安装/回滚证据，HD-034不反向等待HD-035。
- HD-035最终检查另消费HD-032/033同一候选构建。上述条件分别写入task-map.json的implementation_prerequisites与final_evidence_prerequisites；不是Trellis运行时依赖功能。
- AC12由HD-010基于HD-008真实bridge和明确授权的隔离事件实验自行汇总；HD-019只复跑UI整合，不构成HD-010首次完成的前置。
- AC13/14/15由HD-018/019提交P2本地贡献，HD-026补齐真实SSH断网、恢复和SSH子进程所有权后最终汇总；P2不反向等待P3。原任务AC贡献照录于上表，新增最终责任以acceptance-map为准。
- HD-026还最终汇总AC19三设备搜索及AC22主机身份；这些来自HD-011/020/023/024的产品范围，不改变源任务列表。

所有C#测试目标按[统一测试布局](research/test-layout.md)创建并进入solution/just/CI；只写测试文件而没有发现/执行入口不算完成。
- AC39/40/47由HD-007提交基础工程贡献，HD-036对最终候选的全部语言/模块/质量门与真实required-check证据汇总；HD-007不反向等待后续功能完成。
