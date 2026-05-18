# A助理二期整体拆分执行计划

版本：v1.0
用途：交给 Codex / 后端 Agent / 前端 Agent 分别执行
当前定位：把 A助理从“受控产物 Agent MVP”推进到“可内部试用的企业级 Agent 工作台”
重要前提：AICopilot 的 AI 模块允许整体重构；AI Gateway 数据库允许清库重建；Cloud 真实业务数据第一阶段仍保持只读边界。

---

## 0. 总体判断

当前 A助理已经具备以下 MVP 基础：

- A助理身份模板和提示词治理雏形
- 动态模型/API 配置雏形
- 历史轮次配置雏形
- 自动会话标题和历史摘要雏形
- 上传文件能力雏形
- Agent Task / Agent Step / Approval / Artifact Workspace 基础模型
- draft/final 产物目录和下载能力
- Markdown / HTML / 图表 / PDF / PPTX / XLSX 基础生成能力
- 前端 Agent 工作台雏形
- 审批队列和审计摘要雏形

但当前仍然不是最终版，主要短板是：

- PR 元信息与真实代码变更不一致
- 数据库迁移碎片较多，适合清库重建后整理
- Agent 规划仍偏固定步骤，不是真正动态规划
- Cloud 只读数据没有真正形成“云端数据 -> 分析 -> 图表 -> 报告”的闭环
- MCP / Tool Registry 尚未产品化
- 上传安全、图片理解、RAG 权限隔离不足
- finalize 与最终审批边界需要重新定义
- 前端交互仍是 MVP，需要分离重构成清晰工作台
- 需要更稳定的验收脚本和 CI

---

## 1. 产品目标

A助理二期目标不是继续做普通聊天功能，而是建设：

> 企业级受控产物 Agent 工作台。

它应该支持：

1. 用户提出任务目标。
2. A助理生成结构化计划。
3. 系统校验计划、工具、权限、风险。
4. 用户确认计划。
5. Agent 调用只读数据源、RAG、上传文件、MCP/工具链。
6. Agent 生成图表、报告、PDF、PPT、Excel 等草稿产物。
7. 产物进入受控工作区。
8. 用户查看、下载草稿、提出修改意见。
9. 需要审批的步骤必须进入审批队列。
10. 审批通过后才生成或移动到正式 final 目录。
11. 用户下载正式产物。
12. 全流程有审计、可追溯、可复盘。

---

## 2. 不变边界

以下边界必须保持：

- 不允许出现“朝小夕”“朝夕”等旧身份。
- A助理只能自称“A助理”。
- 不开放任意 shell。
- 不允许写任意服务器路径。
- 不允许 Agent 自由修改 Cloud 业务数据。
- Cloud 数据第一阶段只读。
- 生成的文件必须进入受控 Artifact Workspace。
- 草稿产物和正式产物必须分离。
- 高风险工具必须审批。
- 未经审批不得进入 final。
- 上传文件必须有安全限制。
- RAG 必须做权限隔离。
- 密钥、连接串、内部路径不得泄露到前端、日志、审计、模型上下文。

---

## 3. 执行拆分

本计划分成三份：

1. **总体计划**：定义目标、阶段、边界、交付顺序。
2. **后端计划**：交给 Codex / 后端 Agent 执行，负责接口、数据库、Agent Runtime、MCP/工具、安全、验收。
3. **前端计划**：交给前端 Agent 执行，负责页面结构、交互、状态管理、上传、工作区、审批、配置页。

前后端要分离执行，但接口契约必须先稳定。

建议执行顺序：

```text
阶段 0：冻结目标和 PR 元信息
阶段 1：后端契约稳定 + 数据库清理 + seed
阶段 2：后端 P0 安全修复
阶段 3：前端按稳定契约分离重构
阶段 4：动态 Agent Planner + Cloud 只读数据闭环
阶段 5：MCP / Tool Registry 产品化
阶段 6：最终验收、CI、内部试用
```

---

## 4. 阶段 0：PR 和范围收口

目标：让当前重构 PR 变成真实可审查对象。

后端/全栈 Agent 必须先处理：

- 更新 PR 标题。
- 更新 PR body。
- 明确当前 PR 已经不是文档 PR，而是 A助理 Agent MVP 重构。
- 列出新增后端模块。
- 列出新增前端模块。
- 列出数据库变化。
- 列出测试结果。
- 列出未完成事项。
- 保持 Draft，直到 P0 完成。
- 不要合并 Cloud/Edge 的无关 dirty worktree。
- 明确 Cloud 第一阶段只读。

验收：

- PR 描述和实际变更一致。
- reviewer 能从 PR body 直接知道风险、范围、测试、剩余事项。
- 不再出现“documentation-only / no build required”这类错误描述。

---

## 5. 阶段 1：AI Gateway 数据库整理

由于用户明确允许 AI 模块重构、AI 数据库可清库，因此不要背负旧迁移包袱。

目标：

- 清理碎片迁移。
- 保留最终领域模型。
- 重新生成干净的 AI Gateway schema。
- 建立 fresh database seed。
- 新数据库一启动即可拥有 A助理最小可用配置。

必须 seed：

- 默认 runtime settings。
- A助理内置模板。
- 默认权限。
- 默认工作区配置。
- 默认审批策略。
- 默认工具注册记录。
- 如需要，seed 一个 disabled 的示例模型配置，避免空页面崩溃。

禁止：

- seed 旧身份。
- seed 明文敏感密钥。
- seed 指向真实生产 Cloud 写接口的工具。

验收：

- 清库后可启动。
- 清库后配置页可打开。
- 清库后提示词为 A助理。
- 清库后可以创建会话。
- 清库后可以上传文件。
- 清库后可以生成 Agent 计划。
- 清库后可以创建工作区。
- 清库后测试全部通过。

---

## 6. 阶段 2：后端 P0 安全和契约修复

目标：让后端成为前端可依赖的稳定契约。

必须先修：

1. 旧身份文本扫描。
2. API Key 加密或至少脱敏。
3. 上传文件白名单。
4. RAG 权限隔离。
5. workspace final 审批拆分。
6. Agent 状态枚举统一。
7. 审批类型枚举统一。
8. session 标题刷新接口或事件。
9. Agent task / workspace / artifact DTO 稳定。
10. 错误码稳定。
11. 工具白名单稳定。
12. 审计字段稳定。

交付物：

- OpenAPI 或接口契约文档。
- DTO 字段说明。
- 状态机说明。
- 错误码说明。
- 前端 mock 数据。

---

## 7. 阶段 3：前端分离重构

目标：前端不再在旧聊天页上堆功能，而是拆成清晰 Agent 工作台。

建议前端分为：

```text
ChatShell
SessionSidebar
ChatPanel
AgentWorkbenchPanel
ApprovalQueuePanel
ArtifactWorkspacePage
UploadPanel
KnowledgeSourceSelector
ModelSelector
RuntimeConfigView
TemplateConfigView
ToolRegistryConfigView
ApprovalPolicyConfigView
```

前端 Agent 重点做：

- 页面结构。
- 状态管理。
- 文件上传体验。
- 任务计划展示。
- 步骤状态展示。
- 审批操作。
- 工作区文件树。
- 草稿/正式产物展示。
- 下载操作。
- 修改意见入口。
- 错误提示。
- 响应式布局。
- 配置页重构。

前端不要承担：

- 工具权限判断。
- 最终安全决策。
- RAG 权限判断。
- Cloud 数据权限判断。
- final 审批绕过。
- 密钥明文处理。

---

## 8. 阶段 4：动态 Agent Planner

目标：从固定步骤 Agent 升级为结构化规划 Agent。

执行流程：

```text
用户目标
-> planner prompt
-> 模型输出 plan JSON
-> 后端 schema 校验
-> 工具白名单校验
-> 风险评估
-> 审批策略补齐
-> 展示安全计划
-> 用户确认
-> 执行
```

Plan JSON 至少包含：

- title
- goal
- taskType
- requiredInputs
- dataSources
- steps
- toolCode
- toolArguments
- expectedArtifacts
- riskLevel
- approvalRequired
- fallback
- userQuestions

系统必须拒绝：

- 未注册工具。
- 任意 shell。
- 任意路径写入。
- Cloud 写操作。
- 未授权知识库。
- 超出上下文和 token 预算的计划。
- 没有审批却要求 final 的计划。

---

## 9. 阶段 5：Cloud 只读数据闭环

目标：真正做到用户要求的“从云端拿数据，生成图表和报告”。

最小闭环：

1. 用户说“分析最近一周产线数据”。
2. Planner 判断需要 Cloud readonly data。
3. 系统展示将查询的数据范围。
4. 用户确认。
5. 工具调用 Cloud 只读接口。
6. 返回结构化数据摘要。
7. 生成 chart payload。
8. 生成 Markdown/HTML 报告。
9. 生成 PDF/PPT/Excel 草稿。
10. 写入 workspace draft。
11. 用户确认。
12. 审批后进入 final。
13. 下载正式产物。
14. 审计记录完整。

Cloud 只读工具必须限制：

- 只能调用白名单接口。
- 只能读。
- 参数必须 schema 化。
- 时间范围有限制。
- 返回行数有限制。
- 记录审计。
- 失败要解释。
- 不得把 SQL、连接串、内部表名暴露给模型和前端。

---

## 10. 阶段 6：MCP / Tool Registry 产品化

当前工具执行不能长期依赖硬编码 switch。后续要做 Tool Registry。

工具注册字段建议：

- toolCode
- displayName
- description
- inputSchema
- outputSchema
- riskLevel
- requiredPermission
- approvalPolicy
- isEnabled
- providerType
- mcpServerId
- timeoutSeconds
- maxRetries
- auditLevel
- allowedScopes
- createdAt
- updatedAt

工具执行必须：

- 校验 schema。
- 校验权限。
- 校验风险。
- 校验审批。
- 记录输入摘要。
- 记录输出摘要。
- 记录失败原因。
- 记录耗时。
- 支持重试。
- 支持取消。
- 禁止把敏感数据送入模型。

---

## 11. 阶段 7：产物工作区增强

目标：workspace 不只是下载文件，而是任务交付中心。

增强：

- 独立 workspace 页面。
- 文件树。
- draft/final 分区。
- artifact 版本。
- 修改意见。
- 重新生成。
- 版本历史。
- PDF 浏览器预览。
- 图表 PNG/SVG/JSON 预览。
- PPTX 封面/页数/下载。
- XLSX 前几行预览。
- Markdown/HTML 直接预览。
- final 防重复确认。
- final 后防篡改。
- 过期清理。
- 归档策略。

---

## 12. 阶段 8：验收规则

必须覆盖以下正向闭环：

```text
上传 CSV
-> 生成 Agent 计划
-> 用户确认计划
-> 执行解析
-> 生成图表
-> 生成 Markdown/HTML
-> 生成 PDF/PPTX/XLSX 草稿
-> 工作区可查看
-> 提交最终确认
-> 审批通过
-> 进入 final
-> 下载正式产物
-> 审计可查
```

必须覆盖以下负向闭环：

- 驳回计划后不得执行。
- 驳回工具后不得继续该步骤。
- 驳回 final 后不得生成正式产物。
- 上传危险文件必须失败。
- 无权限 RAG 不得返回结果。
- 无权限 workspace 不得查看。
- 无权限 artifact 不得下载。
- 模型输出未知工具必须失败。
- 模型要求 shell 必须失败。
- 模型要求 Cloud 写入必须失败。
- 未审批不得 final。
- 旧身份扫描必须通过。

---

## 13. 验收命令建议

后端：

```bash
dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj
dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj
dotnet list src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive
```

前端：

```bash
npm install
npm run test:unit
npm run build
npm run test:smoke
```

整体验收：

```bash
powershell -ExecutionPolicy Bypass -File scripts/Run-AcceptanceClosure.ps1 -ReportPath 资料/acceptance-closure-latest.md
```

---

## 14. 最终完成标准

达到以下标准后，A助理才可进入内部试用：

- PR 元信息真实。
- fresh database 可用。
- A助理模板干净。
- 动态模型可配置。
- 历史轮次可配置。
- 会话标题和历史显示正常。
- 上传文件安全。
- RAG 权限隔离。
- Agent plan 可审查。
- Agent tool 可控。
- Cloud 只读工具有真实数据闭环。
- workspace draft/final 清晰。
- final 必须审批。
- 前端工作台清晰可用。
- 审计闭环完整。
- CI 或验收脚本稳定通过。
