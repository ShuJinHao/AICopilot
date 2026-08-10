# AICopilot AI 架构路线图

本文档只登记当前能力状态和下一退出门，不承载实现正文、验证算法、部署操作或历史过程。MAF / Harness 细节见 [Agent 工作流与异常契约](./Agent工作流与异常契约.md)，Cloud 查询与数据安全见 [Cloud 只读数据分析契约](./Cloud只读数据分析契约.md)，聚合与持久化见 [DDD 聚合根边界](./DDD聚合根边界.md)，候选与生产退出规则见 [AICopilot 安全部署契约](./AICopilot安全部署契约.md)。战略性“不做”边界只见 [AICopilot 业务规则](./AICopilot业务规则.md)。

当前状态：身份、当前用户委托 AiRead、`101650` 自动收编、grant 注销/清理、system identity-status token 和真实 Cloud SQL fail-close 候选源码已合并到 `main`；生产 E2E、生产迁移和部署未执行，当前不是生产基线。`SingleInstance` 已完成且唯一技术正文只在[Agent 工作流与异常契约第 1.2 节](./Agent工作流与异常契约.md#12-agentsession-持久化)，不再列为缺口。

| 能力 | 源码状态 | 候选状态 | 生产状态 | 下一退出门 |
|---|---|---|---|---|
| Harness / MAF 主聊天 | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
| AgentSession 与逐次批准 | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
| Cloud OIDC/JIT 普通身份与 `101650` 例外 | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
| Cloud typed AiRead 用户委托 | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
| Cloud delegation grant revoke / cleanup | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
| system identity-status token | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
| Cloud Direct DB / Text-to-SQL 生产关闭 | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
| KnowledgeQuery / RAG | 已建立 | 待验证 | 未验收 | 取得该能力候选证据 |
| MCP 2.0 受治理通道 | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
| 模型调用治理 | 已建立 | 待验证 | 未验收 | 取得该能力候选证据 |
| 对话前端 | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
| DDD 与持久化 | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
| 旧架构退役 | 已收口 | 待验证 | 未验收 | 取得该能力候选证据 |
