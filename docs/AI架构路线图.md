# AICopilot AI 架构路线图

本文只记录当前仍有效的目标架构、阶段状态和退出门。业务与安全规则以[AICopilot业务规则](./AICopilot业务规则.md)及四份专题契约为准；历史设计、评审、测试数量和执行过程只通过 Git 追溯。

## 1. 当前状态

- AICopilot 的目标是企业数据与证据编排平台，不是通用自治多 Agent 平台。
- 仓库历史中已经形成覆盖 P0–P7 的源码候选，包含 Plan v2、唯一 PlanCompiler、durable NodeRun/Evidence、有限 DAG、受控 Agent 节点、IntentRegistry、产物和前端工作台接线。
- “源码候选”不等于当前 exact HEAD 已通过全部受影响验证，更不等于已经合并、发布或生产验收。旧分支、旧 CI run、固定 runner/case 数和旧覆盖率不能作为当前候选证据。
- 当前生产边界保持不变：Cloud 永久只读；AICopilot 不直连 PLC；Plan 未确认前不执行；Simulation 必须显式开启且不得作为 Cloud 失败 fallback；模型不能授予权限。
- 当前工作区另有 OIDC 同名本地账号确认绑定改动；该改动不改变本路线图的阶段状态，验证和发布必须按自己的影响闭包执行。

## 2. 已确定的架构决策

### 2.1 入口与编排

- `AgentWorkflowPipeline` 是统一用户输入主干。
- Chat 使用请求级、低延迟、流式编排；Plan 只生成草案，确认后由 durable task 编排执行长任务。
- 两种编排共享 Node Contract、Node Executor、Tool/Policy/Guard、Evidence、版本快照和预算，不合并成巨型 Runtime。

### 2.2 Plan 与 Skill

- Plan v2 是唯一新执行协议；已完成 Plan v1 只保留不可变历史，非终态旧计划必须取消或重建。
- 不建设 v1/v2 双执行器、compat adapter、legacy alias 或第二套 compiler。
- 顺序固定为：先补 Plan/Node/Evidence/恢复地基，再验证能力与拒绝语义等价，最后物理退役 Skill 和 DynamicPlanner。
- `ToolRegistration` 只描述单个 Tool，不用通用能力包把 Skill 改名重建。
- `TaskType` 只用于展示和统计，不能授予工具、数据或 Evidence 权限。

### 2.3 能力选择和计划完整性

- 用户选择只表达最大允许范围，最终能力必须取用户范围、服务端授权、资源状态和 Guard 的交集。
- `PluginSelectionMode` 与 `CapabilitySelectionMode` 必须显式；空数组不得解释为“全部允许”。
- P0–P3 只允许 `LinearV1`；P4 才通过新 schema、digest 和重新确认启用 `DagV1`。
- Plan、Node input/output 和 Evidence metadata 必须 canonicalize、按 UTF-8 字节显式限长并完整持久化；禁止 substring 或静默截断结构化 JSON。

### 2.4 Evidence 与耐久运行时

- Evidence 是统一结果契约，不是新聚合根。Durable Task 保存 Evidence；快速 Chat 只保存安全摘要或 digest。
- 主 Agent 只消费经过授权、脱敏、类型和 digest 校验的 EvidenceSet，不消费子 Agent 原始对话或任意 raw output。
- 任务级 QueueItem/RunAttempt 与节点级 NodeRun 是两套原子 claim 协议，各自拥有 lease 和 fencing；checkpoint 同时验证两级 token。
- `NodeRun + Evidence checkpoint` 是恢复权威，Timeline 只是 Projection，Audit 是独立审计面。
- 外部副作用结果不确定时进入 `OutcomeUnknown` 和 fenced reconciliation，禁止自动重放。
- 多实例模型 RPM/TPM/并发配额以 PostgreSQL 原子预约为权威，进程内 scheduler 只做本地健康与候选选择。
- 大 Evidence 必须经过 ArtifactWorkspace 文件集 journal、manifest、fencing、digest 和恢复对账；关单前只能接受有界 inline Evidence。

### 2.5 数据和安全

- Cloud 正式语义读取与治理型自由探索是两条互斥只读路径，不自动互相 fallback。
- 已覆盖语义走 Cloud typed GET；未覆盖且获准的自由探索才走受治理 Direct DB/Text-to-SQL。
- Unknown、空集、未授权、凭据失败和正式 Cloud 错误不能自动进入另一来源、Simulation、MCP 或隐藏旁路。
- AICopilot 永不写 Cloud 业务数据、永不直连 PLC；Human-in-the-loop 只授权 AICopilot 自身动作。
- Evidence、Prompt、Tool、Plugin、MCP、模型和执行快照都必须绑定租户、用户、版本和数据范围，敏感信息不能进入日志、前端或普通持久化结果。

### 2.6 DAG、Agent 和预测

- 第一版 DAG 并行度限制为 2–4，使用固定且可测试的 skeleton。
- 第一版 Agent 派生深度为 1；子 Agent 不能继续创建子 Agent，父 Workflow 决定是否派发。
- 设备健康评估在 P7 只能是可回溯 `DerivedFact`。
- 时序采集、异常模型、故障预测和剩余寿命另立跨项目 DP 路线；没有真实数据、标签和跨项目授权时不得用 Simulation 或 LLM 生成结果冒充预测。

## 3. 阶段路线

| 阶段 | 目标交付 | 当前状态 | 退出条件 |
|---|---|---|---|
| B0 | 唯一实施基线、项目图和 Analyzer owner | 历史基线曾关闭；旧证据不自动覆盖当前 HEAD | 当前候选 clean、exact SHA，项目图/Analyzer/受影响测试重新对账 |
| P0 | IntentCandidate、Plan v2、LinearV1、Node/Evidence/ExecutionSnapshot 契约 | 已有源码候选 | schema/digest 稳定，超限显式拒绝，影子 planner 不进入生产 DI |
| P1 | 两级 claim/fencing、状态机、恢复、OutcomeUnknown、配额和 Artifact 对账 | 已有源码候选 | 多 Worker claim/kill/recovery、状态迁移、配额结算、文件集故障矩阵通过 |
| P2 | 共享 Node Executor、Evidence Normalizer、双只读执行器和最小生产 PlanCompiler | 已有源码候选 | Chat/Durable 语义一致，无 compiler 空窗、上下文污染或跨路径 fallback |
| P3 | Plan v2 单轨和 Skill/DynamicPlanner 物理退役 | 已有源码候选 | 保留能力等价、退役能力显式、旧拒绝不变、活动系统零旧消费者 |
| P4 | 有限 DagV1、依赖与合流 | 已有源码候选 | 新 digest/确认、并发、失败传播和恢复稳定 |
| P5 | 受控 Agent Node | 已有源码候选 | 独立上下文、typed result、深度/预算/权限均 fail-closed |
| P6 | 完整 IntentRegistry 和同一 PlanCompiler 泛化 | 已有源码候选 | 单一 registry/compiler，多意图稳定，unknown fail-closed |
| P7 | 工作台、Eval、监控、产物和当前数据健康评估 | 已有源码候选 | UI 与后端事实一致，可观测、可追溯，健康结果保持 DerivedFact |
| DP1–DP3 | 时序合同、异常模型、故障预测 | 未纳入当前候选 | 单独跨项目授权、真实数据/标签、时间切分防泄漏和模型回滚门 |

阶段顺序不可交换：

```text
B0 → P0 → P1 → P2 → P3 → P4 → P5 → P6 → P7
```

P1 不能晚于 Skill 退役；P2 的唯一最小 compiler 不能晚于 P3；P5 不能早于稳定 DAG、预算和 checkpoint；预测不能夹入 P0–P7。

## 4. 下一轮实施与验证

进入下一轮代码工作前：

1. 以当前实际目标分支和 exact HEAD 重新检查 P0–P7 源码候选是否已经完整进入活动树，禁止从旧 worktree 整体搬运。
2. 依据当前 diff 选择不可弱化的 Analyzer/Architecture/Security 与受影响 Business；不使用旧固定 runner/case 数。
3. 定向验证至少覆盖：
   - `CloudAiReadClientContractTests`：typed GET、参数、Cloud-only 和 no-fallback。
   - `AgentSafetyApplicationTests`：最终上下文、SQL/内部字段和敏感信息脱敏。
   - Plan v2 canonical/digest/超限、确认前禁止执行、capability gap。
   - task/node fencing、恢复、OutcomeUnknown、Evidence/Artifact 完整性。
   - 有限 DAG、Agent 深度、预算、取消和失败传播。
4. 活动文档中的任何定向 `dotnet test --filter` 必须先用同项目、同 filter 的 `--list-tests` 证明至少命中一项；0-hit 即使命令退出 0 也不是有效证据。
5. 只有当前 exact SHA 的受影响验证、生产编译和不可变产物完成后，才能进入工作区 `Validate-Candidate` / `Prepare-Release`；未执行真实部署时必须标记生产未验收。

## 5. 明确不做

- 不恢复 Skill、DynamicPlanner、TaskType 授权或第二套 PlanCompiler。
- 不建设任意用户上传 Agent YAML 后直接执行的平台。
- 不让模型自行扩大 Tool、Plugin、MCP、数据源或 Evidence 权限。
- 不以通用 SQL、MCP 或 Direct DB 替代已覆盖的 Cloud typed GET。
- 不用当前健康评分冒充故障预测。
- 不用旧计划、旧 CI run、固定测试数量或历史覆盖率证明当前候选完成。
