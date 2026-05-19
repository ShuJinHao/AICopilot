# A助理 Agent 交付候选审查清单

生成日期：2026-05-15

## 交付目标

本清单用于把 Phase 0-11 的 AICopilot Agent/Artifact 改造整理成可审查的交付候选包。当前阶段不新增后端能力、不新增数据库表、不新增依赖，也不推送远端或创建 PR。

交付范围只包含 `AICopilot`。`IIoT.CloudPlatform` 当前检查为干净工作树；`IIoT.EdgeClient` 存在既有 Homogenization 相关 dirty worktree，本阶段只记录边界，不修改、不恢复、不覆盖。

## 变更归属分组

| 分组 | 审查重点 |
| --- | --- |
| 动态模型与路由 | 语言模型协议、用途、连接测试、路由模型配置、实际模型元数据回传、token 预算和结构化路由解析。 |
| Prompt/Session | A助理内置模板治理、模板重置、身份约束、会话标题/摘要/消息计数、会话重命名和历史配置。 |
| RAG/Upload | 会话/Agent/知识库上传记录、知识库入库桥接、RAG 来源字段、片段分数和低置信度提示。 |
| Agent/Artifact/Workspace | AgentTask、AgentStep、受控工作区、固定目录、manifest、产物版本、下载和 finalize。 |
| 审批审计 | 计划确认、风险工具审批、产物确认、finalize 确认、审批驳回、任务审计摘要和下载/finalize 审计。 |
| 前端工作台 | `/chat` 三栏工作台、移动端 Agent 抽屉、刷新恢复、审批队列、产物卡片、图表预览、审计摘要、中文文案。 |
| 测试脚本 | 后端全链路验收、架构边界、AI 安全评测、前端单测、前端 smoke、编码检查、最终验收脚本。 |
| 交付文档 | 现状差异分析、最终验收报告、产品化工作台验收报告、发布候选硬化报告、阶段状态和本清单。 |

## 核心能力检查

- A助理身份固定为“A助理”，旧名称和越权承诺由 Prompt 治理与 AI 评测保护。
- Agent 计划由服务端生成，用户批准前不执行工具。
- 低风险工具只能通过白名单运行；高风险步骤必须进入审批请求。
- 所有产物文件只能写入应用管理的工作区目录，目录固定为 `source/`、`data/`、`charts/`、`draft/`、`final/`、`logs/`、`audit/`。
- 未 finalize 前正式 `final/` 输出为空；finalize 后才允许下载正式产物。
- 下载、finalize 和任务审计摘要都做所有权校验。
- Cloud 业务数据只读；不存在 Agent、MCP、后台任务或隐藏适配器写 Cloud 的交付入口。

## 接口边界

保持现有路由语义，不新增后端大接口：

- `agent/task/*`
- `agent/approval/*`
- `workspace/{code}`
- `artifact/{id}/download`
- `workspace/{code}/finalize`
- `agent/task/{id}/audit-summary`
- `runtime-settings`
- `upload`
- `template/reset-builtins`
- `session/rename`

本阶段没有新增数据库迁移、NuGet/npm 依赖、后台自主 Agent、在线 Office/PDF 编辑器、任意 shell、任意服务器路径写入或 Cloud 写接口。

## 审查前检查项

- 确认 AICopilot 变更仍按上述分组可解释，没有混入 Cloud/Edge 文件。
- 确认 `git ls-files --others --exclude-standard` 未暴露 `dist`、`node_modules`、`bin`、`obj`、`playwright-report`、`test-results`、`artifacts` 等未忽略生成物。
- 确认 `git diff --check` 无行尾空白或冲突标记。
- 确认中文文案和交付文档通过 `scripts/Test-TextEncoding.ps1`。
- 确认最终验收脚本刷新 `资料/acceptance-closure-latest.md` 后所有步骤为 `PASSED`。

## 人工验收路径

1. 登录 `/chat`，确认桌面三栏工作台和移动端 Agent 抽屉可用。
2. 上传 CSV/JSON/XLSX，生成 Agent 计划，确认批准前不会执行工具。
3. 批准计划并运行，确认低风险步骤连续执行，高风险步骤进入审批队列。
4. 驳回任一审批，确认任务停止或进入可重试状态，后续步骤不会继续执行。
5. 批准产物与 finalize，确认草稿可预览/下载，正式产物只在 finalize 后进入 `final/`。
6. 查看任务审计摘要，确认计划、审批、工具、产物、下载、finalize 均有只读审计记录。
7. 登录 `/config`，确认 Agent/Workspace 区只读展示工作区根目录、固定目录、允许产物类型、历史参数、审批策略和最近验收时间。

## 剩余边界

- 当前交付候选不包含完整企业权限分层、长期后台自主 Agent、多 Agent 协作、复杂模板系统或在线编辑器。
- PDF/PPTX/XLSX 是基础导出草稿，不是高保真模板引擎。
- Cloud/Edge 对齐仍需单独授权和单独计划，本交付包不包含 Cloud/Edge 改动。
- 远端推送、提交拆分、PR 创建和审查反馈处理需要后续单独授权。
