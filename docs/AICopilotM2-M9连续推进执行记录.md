# AICopilot M2-M9 连续推进执行记录

版本：2026-05-24

## 总体状态

- M1 baseline 已通过 PR #48 合并到 `main`，merge commit 为 `ec5e2bb0b989fee86df81fe6adbb02c3e940d0fa`。
- 本轮从 M1 合并基线进入 M2-M6 工程推进，并准备 M7-M9 的受控交付材料。
- 本轮只修改 `AICopilot`。`IIoT.CloudPlatform`、`IIoT.EdgeClient`、AICopilot 前端、真实 Pilot、真实 endpoint/token、Cloud 写、Recipe/version、自由 SQL 全部冻结。

## M2 Pilot Authorization Workflow

- 新增 `PilotAuthorizationSubmission` 聚合，作为 Pilot 授权材料唯一业务入口。
- `PilotAuthorizationReview`、`PilotCredentialWindow`、`PilotRollbackPlan`、`PilotEvidenceArchive` 作为聚合内状态数据，不拆独立业务入口。
- 状态固定为 `Draft`、`Submitted`、`MachineRejected`、`ReviewPending`、`ApprovedForCredentialWindowPlanning`、`ApprovedForLimitedPilotExecutionPlanning`、`Rejected`、`Expired`、`Revoked`。
- 未新增、未使用、未暗示任何直接执行授权状态。
- 新增后端 API：
  - `GET /api/aigateway/pilot-authorization/submissions`
  - `GET /api/aigateway/pilot-authorization/submissions/{id}`
  - `POST /api/aigateway/pilot-authorization/submissions`
  - `PUT /api/aigateway/pilot-authorization/submissions/{id}`
  - `POST /api/aigateway/pilot-authorization/submissions/{id}/submit`
  - `POST /api/aigateway/pilot-authorization/submissions/{id}/approve-credential-window-planning`
  - `POST /api/aigateway/pilot-authorization/submissions/{id}/approve-limited-pilot-execution-planning`
  - `POST /api/aigateway/pilot-authorization/submissions/{id}/reject`
  - `POST /api/aigateway/pilot-authorization/submissions/{id}/revoke`
- 新增权限：
  - `PilotAuthorization.Submit`
  - `PilotAuthorization.View`
  - `PilotAuthorization.Review`
  - `PilotAuthorization.ApprovePlanning`
  - `PilotAuthorization.Reject`
  - `PilotAuthorization.Audit`
- 默认 User 只获得 `Submit` 和 `View`，并在服务层限制只能修改/查看自己的提交；Admin 通过权限目录获得全部权限；审批人通过自定义角色授予 `Review`、`ApprovePlanning`、`Reject`、`Audit`。
- 机器校验只允许 `devices`、`capacity_summary`、`device_logs`、`pass_station_records`，并固定 `maxRows <= 50`、`timeRangeDays <= 7`、五类 owner 必填。
- 机器校验拒绝 token、API Key、connection string、raw payload、raw rows、full SQL、Recipe/version、Cloud write、free SQL 等越界语义。
- 审计只记录状态变化、安全摘要和白名单 metadata，不写敏感正文。
- 持久化使用 `AiGatewayDbContext` 新表 `aigateway.pilot_authorization_submissions` 和 EF migration。

## M3 Model/API Pool Productionization

- 当前基线已具备 `GetModelPoolsQuery`、`GetProviderReliabilityQuery`、`ModelProviderReliability`、运行时设置和模型连接性测试。
- 本轮将 M3 作为已并入后续总审包的工程能力，不配置真实 provider endpoint/token。
- M3 验收口径：模型池和可靠性状态只暴露配置、健康、安全摘要和统计，不输出 secret，不修改 appsettings 真实配置。

## M4 RAG Governance Completion

- 当前基线已具备知识分类、补充知识、文档治理元数据、访问范围和上传安全策略。
- 本轮将 M4 纳入总审包，保持 RAG 治理链路只处理来源、版本、分类、删除/禁用、召回审计和安全摘要。
- M4 验收口径：不输出 raw payload、raw business rows、完整 SQL 或任何凭据。

## M5 Enterprise Data Source Platformization

- 当前基线已具备企业数据源治理字段、DataSource 权限授权、Text-to-SQL 只读护栏和数据源审计。
- 本轮将 M5 纳入总审包，保持企业数据源能力为受控只读。
- M5 验收口径：不开放 Cloud 写、Recipe/version、自由 SQL，也不开放不受控的 `query_cloud_data_readonly` 执行面。

## M6 Security & Compliance Hardening

- 本轮新增 Pilot Authorization 审计白名单键，并保留现有文本编码、架构边界、企业数据治理范围脚本。
- 新增 M2-M9 静态门禁脚本，检查 API、状态、权限、审计安全键、M7 硬停和禁止语义。
- M6 验收口径：敏感正文不得进入 DTO、审计 metadata、报告、日志或测试样本。

## M7 前硬停

- 本轮只准备 M7 执行前授权包，不执行真实 Pilot。
- 未配置真实 endpoint/token。
- 未开启 Cloud 写或自由 SQL。
- 未声明 GA。

## M8/M9 总审准备

- M8 仅作为 Internal GA Candidate 准备口径，必须等待 M7 真实小范围验证完成后才能进入。
- M9 仅准备 GPT/5.5 Pro 总审包，不等同 GA 发布。
- GPT/5.5 Pro 审核应在 M2-M6 工程能力和 M7-M9 材料汇总后执行。
