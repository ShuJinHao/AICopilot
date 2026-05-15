# A助理现状差异分析

Updated: 2026-05-14

## 1. 执行边界

本次改造只覆盖 `AICopilot` 仓库，不修改 `IIoT.CloudPlatform` 和 `IIoT.EdgeClient`。AICopilot 可以读取、解释、汇总和分析已批准的云端只读数据，但不能通过 Agent、MCP、后台任务、直接 SQL 或隐藏适配器写入云端业务数据。

当前工作区存在大量未提交变更，主要集中在动态模型、路由模型、真实模型展示、前端交互和阶段记录上。后续实现必须基于这些变更继续向前做小步增量，禁止用整文件覆盖、格式化重排或回退来“清理”现有改动。

## 2. 已有能力

### AiGateway

- 已有 `LanguageModel`、`ConversationTemplate`、`ApprovalPolicy`、`RoutingModelConfiguration`、`Session`、`Message` 等核心聚合。
- 已有动态模型配置、连接测试、OpenAI-compatible/Anthropic Messages 协议、全局路由模型和真实回答/路由模型快照展示的进行中实现。
- 已有聊天流式入口 `/api/aigateway/chat`、审批决策入口 `/api/aigateway/approval/decision`、模型/模板/审批/会话配置接口。
- 已有 `ConversationTemplate` 安全校验，能拒绝“绕过审批”“直接写入 Cloud”“直接执行 SQL”等危险提示词片段。

### RAG

- 已有独立 `RagDbContext`、知识库、文档、切片、embedding 模型管理、上传入库、索引状态、重试和删除清理。
- 已有 `/api/rag` 管理接口和 Vue 知识库页面。
- 已有 PDF/TXT/Markdown 等解析基础、Qdrant 向量检索和 RAG 相关测试。

### DataAnalysis

- 已有独立 `DataAnalysisDbContext`、只读业务数据库配置、语义映射、SQL guardrail、Text-to-SQL 查询、结果 widget 输出。
- 已有云端配方主数据禁读边界和只读数据源安全测试。

### MCP 与审批

- 已有 `McpServerDbContext`、MCP 服务配置、允许工具清单、工具风险等级、只读声明和 Cloud 只读安全策略。
- 已有 `ApprovalPolicy`、审批卡片、待审批恢复、同会话 pending approval 阻塞等机制。
- 当前 MCP 是受控工具入口，不是任意 shell 或云端业务写入口。

### 前端

- 当前活跃路由包括 `/chat`、`/config`、`/knowledge`、`/access`。
- 已有聊天窗口、会话列表、审批卡片、工具/函数调用展示、模型配置、路由模型配置、RAG 管理和访问控制页面。
- 前端近期已有真实模型角标、主题和配置页面相关改动，后续工作台化应在此基础上渐进增强。

## 3. 主要缺口

- **提示词治理不完整**：现有 `ConversationTemplate` 可以管理系统提示词，但缺少内置模板编码、用途、版本、重置内置模板、统一“A助理”身份验收。
- **会话历史信息不足**：`Session` 仍以“新会话”为默认标题，缺少自动标题、摘要、消息数、任务状态、产物数量和可配置历史轮次。
- **上传能力分散**：RAG 文档上传已经存在，但缺少会话临时文件、Agent 输入文件、图片上传和上传用途绑定。
- **Agent 任务模型缺失**：目前是聊天工作流和工具调用能力，没有持久化的 Agent Task、Step、Plan、任务状态机和可恢复执行记录。
- **产物工作区缺失**：尚无任务级工作区、manifest、草稿/正式产物分离、版本记录和受控下载。
- **审批粒度需要统一**：已有审批策略可用，但计划确认、工具审批、产物确认、正式输出确认还没有统一落到 Agent 任务生命周期。
- **前端还不是完整工作台**：聊天页已有能力块，但还缺少任务执行面板、产物工作区页、上传卡片、RAG 来源卡片和工作区入口。

## 4. 可保留模块

- 保留 `ConversationTemplate` 作为提示词治理基础，不另建平行的 `ai_prompt_policies` 第一版表系。
- 保留现有 `LanguageModel`、`RoutingModelConfiguration` 和模型连接测试能力，不重新设计模型网关。
- 保留 `RagDbContext`、RAG 文档生命周期和知识库页面，在其上补会话/任务级上传和来源验收。
- 保留 `DataAnalysis` 只读查询、安全边界和 widget 输出，作为 Agent 可调用的只读分析能力。
- 保留 `McpServerInfo`、`AiToolSafetyPolicy` 和 `ApprovalPolicy`，作为受控工具和审批策略基础。
- 保留现有 Vue 路由和页面组织，先升级 `/chat` 为工作台，不重写登录、权限和知识库页面。

## 5. 需要新增或重建的模块

- `BuiltInConversationTemplates`：内置 A助理提示词基线、版本、用途和身份安全要求。
- `ChatRuntimeSettings`：全局上下文轮次、摘要阈值和 token 预算配置。
- `UploadedFile`：会话/任务级上传记录，补足 RAG 上传之外的临时文件和图片能力。
- `AgentTask` / `AgentStep`：受控 Agent 计划、执行步骤、状态机和失败记录。
- `ArtifactWorkspace` / `Artifact` / `ArtifactVersion`：受控工作区、草稿/正式产物、版本和 manifest。
- `ApprovalRequest`：计划确认、工具审批、产物确认、正式输出确认的统一任务内审批记录。
- 工作区文件服务：只允许写入应用管理目录，拒绝模型指定任意服务器路径。

## 6. 数据库处理原则

- 不删除非 AI 数据库和非 AICopilot 表。
- 现有 `aigateway`、`rag`、`mcp`、`dataanalysis`、`identity` 等上下文继续分离。
- 第一版不做大规模清库；只对明确属于 AICopilot AI 内部的新能力增加迁移。
- 旧会话和旧模板不作为长期兼容包袱，但在已有表结构可承载时优先扩展，不为兼容而增加双写或过渡表。

## 7. 后续执行顺序

1. 先完成 A助理内置提示词和身份治理，解决产品身份不稳定问题。
2. 再完成会话标题、历史上下文配置和历史列表元数据。
3. 接着新增上传、AgentTask、ArtifactWorkspace 和 ApprovalRequest 持久化基础。
4. 然后接入受控工具执行和基础产物生成。
5. 最后重构聊天页为工作台并补齐验收测试。

