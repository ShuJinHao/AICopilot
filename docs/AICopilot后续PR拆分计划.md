# AICopilot 后续 PR 拆分计划

版本：2026-05-24

## 拆分原则

PR #48 只作为企业治理 readiness baseline 收口，不再继续叠加后续大阶段。后续每个 PR 只处理一个主题，单独评审、单独验证、单独回滚。

所有后续 PR 默认只修改 `AICopilot`。未经单独明确授权，不修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`，不执行真实 Pilot，不配置真实 endpoint/token。

## PR-A：Baseline Freeze

目标：冻结 PR #48 口径，确认 P18.2 只是 readiness baseline，不是 GA，不是真实 Pilot。

边界：

- 不改业务代码。
- 不改前端。
- 不改 Cloud/Edge。
- 不开放 `query_cloud_data_readonly`。
- 不配置 endpoint/token。

## PR-B：Pilot Authorization Workflow

目标：在 AICopilot 内实现系统内 Pilot 授权流程。该 PR 只允许后端/API、持久化、权限、审计、测试和文档。

边界：

- 不做前端页面。
- 不执行真实 Pilot。
- 不配置真实 endpoint/token。
- 不开放 Cloud 写。
- 不开放 Recipe/version。
- 不开放自由 SQL。
- 不让 readiness/planning 变成 execution permission。

## 后续候选 PR

- PR-C：Model/API Pool Productionization。
- PR-D：RAG Governance Completion。
- PR-E：Enterprise Data Source Platformization。
- PR-F：Security & Compliance Hardening。
- PR-G：Limited Pilot Execution，仅在明确授权后进入。
- PR-H：Internal GA Candidate。

后续 PR 只有在前置 PR 验收完成后才能进入。任何真实 Pilot 或 GA 语义都必须单独审批，不得从 readiness/planning 文档中推导。
