# AICopilot AI 架构路线图

本文只记录源码状态、生产状态和后续退出门，不重复运行时实现正文。业务和安全规则以 [AICopilot 业务规则](./AICopilot业务规则.md)、MAF / Harness 唯一技术正文 [Agent 工作流与异常契约](./Agent工作流与异常契约.md)、[Cloud 只读数据分析契约](./Cloud只读数据分析契约.md) 和 [DDD 聚合根边界](./DDD聚合根边界.md) 为准；历史方案和阶段执行过程只通过 Git 追溯。

## 1. 当前源码状态

- Harness 主聊天、MAF 原生模式运行时、AgentSession、逐次批准、对话前端与 MCP 2.0 受治理通道的源码架构已收口；MAF / Harness 技术状态以 [Agent 工作流与异常契约](./Agent工作流与异常契约.md) 为唯一正文。
- Cloud OIDC/JIT 身份、Cloud 真实只读、BusinessQuery、KnowledgeQuery、模型调用治理与工具安全边界的源码已建立；长期规则由对应活动契约维护。
- 旧 Workflow、AgentTask、durable run、业务审批、Artifact、routing/runtime settings 和第二条活动编排链已物理退出；历史只通过 Git 追溯。
- 上述源码状态均不代表候选、产物或生产验收完成。

## 2. 能力状态

| 能力 | 源码状态 | 候选验证退出门 | 生产状态 |
|---|---|---|---|
| Harness 主聊天 | 源码已收口；技术正文见 [Agent 工作流与异常契约](./Agent工作流与异常契约.md) | exact-SHA MAF 合同、HTTP/SSE 与安全边界证据有效 | 未验收 |
| AgentSession 持久化 | 源码已收口；技术正文见 [Agent 工作流与异常契约](./Agent工作流与异常契约.md) | exact-SHA 持久化、并发和重启边界证据有效 | 未验收 |
| 逐次工具批准 | 源码已收口；技术正文见 [Agent 工作流与异常契约](./Agent工作流与异常契约.md) | exact-SHA 批准与 fail-closed 证据有效 | 未验收 |
| 对话前端 | 源码已收口；产品与技术规则均链接活动契约 | exact-SHA 前端受影响验证有效 | 未验收 |
| AI-01 OIDC/JIT 身份 | 源码已建立；规则见活动契约 | exact-SHA 身份、安全与审计证据有效 | 未验收 |
| AI-02 Cloud 真实只读 | 源码已建立；规则见活动契约 | exact-SHA provider、只读和发布顺序证据有效 | 未验收 |
| MCP 2.0 受治理通道 | 源码已收口；技术正文见 [Agent 工作流与异常契约](./Agent工作流与异常契约.md) | exact-SHA MCP conformance 与安全证据有效 | 未验收 |
| 工具与数据安全 | 源码已建立；规则见活动契约 | exact-SHA Architecture/Security 与 Tool Gate 证据有效 | 未验收 |

Harness 原生模式运行时、AgentSession、逐次批准、对话前端和 MCP 2.0 的源码架构已收口。“源码已收口”只表示当前源码状态，不等于 exact SHA 已通过候选验证、产物准备或生产验收。所有能力的生产状态继续保持“未验收”；旧分支、旧 CI run 和固定测试数不能证明当前候选完成。

## 3. 候选验证退出门

- 候选必须是已合入、已推送且 `HEAD == origin/main` 的 clean `main` exact SHA；PR head、旧分支或本地未提交字节不能作为候选。
- 生产 baseline 只能来自获批的只读生产状态工作流，并必须是当前仓库历史中的有效完整 SHA；状态缺失、不健康或无法验证时停止，不得使用旧标签、文档或猜测值替代。
- 工作区 `Validate-Candidate` 必须先判定同一候选 SHA 的默认 CI：运行中禁止重复本地验证，失败或无效证据不得被本地结果覆盖，成功且范围匹配时复用，缺失范围才由统一入口补齐。
- 只有绑定同一候选 SHA、真实生产 baseline、Architecture/Security/Business 分类和 Analyzer-enabled Release production graph 的签名绿色证据，才允许在后续独立批次进入 `Prepare-Release`。
- 未完成候选验证、产物准备和真实部署前，生产状态始终保持“未验收”。

## 4. 验证约束

- 根据当前 diff 选择不可弱化的 Analyzer/Architecture/Security 与受影响 Business；不使用旧固定 runner/case 数。
- 任何 `dotnet test --filter` 必须先用同项目、同配置、同 filter 的 `--list-tests` 证明至少命中一项；0-hit 不是有效证据。
- baseline 只在空库或设计时模型中验证；普通开发批次不应用现有数据库、不清库、不部署。
- 全量、coverage、mutation、duplication、Quality、CrossProject、真实 Cloud live 与生产操作只能在用户当前轮明确授权时运行。

## 5. 明确不做

- 不建设任意用户上传 Agent 定义后直接执行的平台。
- 不让模型扩大 Tool、MCP、知识库、数据源或证据权限。
- 不以通用 SQL、MCP 或 Direct DB 替代已覆盖的 Cloud typed GET。
- 不用 Simulation、LLM 推断或当前健康评分冒充生产事实或预测模型结果。
