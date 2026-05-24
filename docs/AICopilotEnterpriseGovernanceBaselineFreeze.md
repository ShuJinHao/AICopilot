# AICopilot Enterprise Governance Baseline Freeze

版本：2026-05-24

## 结论

PR #48 / `integration/aicopilot-agent-workbench-simulation` 冻结为 AICopilot 企业治理 readiness baseline。当前 head 为 `fa0a85b1e756e0f02e9ab7ee8e6875f2ed408d82`，阶段覆盖到 P18.2 offline authorization submission format and machine-validation gate。

本基线不是 GA，不是真实 Pilot，不是生产上线许可，也不是 Cloud 真实读取或写入许可。

## 当前边界

- 当前只修改 `AICopilot`，Cloud/Edge 未改。
- 当前 `GateState=BlockedNoSubmittedAuthorizationMaterials`。
- 当前 `ExecutionPermission=not granted`。
- `query_cloud_data_readonly` 仍保持 disabled/hidden/non-executable。
- 不配置真实 endpoint/token。
- 不新增 Cloud endpoint。
- 不开放 Cloud 写。
- 不开放 Recipe/version。
- 不开放自由 SQL。
- 不执行真实 Pilot。
- 不声明 GA。

## Freeze 规则

PR #48 不再继续叠加 P19、P20、P21 或其他大阶段。后续工作必须拆成独立 PR，按单一主题、单独验证、单独评审、单独回滚推进。

如果后续需求涉及真实 endpoint/token、真实 Pilot、Cloud 写、Recipe/version、自由 SQL、Cloud/Edge 修改或 `query_cloud_data_readonly` 开放，必须新开任务并取得明确授权。

## 后续入口

M1 完成后，等待 PR #48 合并。PR #48 合并前不得开始 M2 系统内 Pilot Authorization Workflow 实现。PR #48 合并后，M2 必须从新分支和新 PR 开始。
