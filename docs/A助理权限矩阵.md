# A助理权限矩阵

日期：2026-05-18

| 权限 | Admin | User | AgentApprover | Operator | Viewer |
| --- | --- | --- | --- | --- | --- |
| PlanAgentTask | 是 | 是 | 是 | 否 | 否 |
| ApproveAgentTaskPlan | 是 | 否 | 是 | 否 | 否 |
| ApproveAgentToolCall | 是 | 否 | 是 | 否 | 否 |
| ApproveFinalOutput | 是 | 否 | 是 | 否 | 否 |
| RunAgentTask | 是 | 是 | 是 | 否 | 否 |
| CancelAgentTask | 是 | 仅本人任务 | 是 | 是 | 否 |
| SubmitFinalReview | 是 | 是 | 是 | 否 | 否 |
| FinalizeWorkspace | 是 | 否 | 是 | 否 | 否 |
| EditArtifact | 是 | 仅 WorkspaceReady 本人草稿 | 是 | 否 | 否 |
| DownloadArtifact | 是 | 可见任务产物 | 可见任务产物 | 可见任务产物 | 可见任务产物 |
| ToolRegistry.Read | 是 | 否 | 是 | 是 | 只读 |
| ToolRegistry.Manage | 是 | 否 | 否 | 否 | 否 |
| RunQueue.Read | 是 | 否 | 是 | 是 | 只读 |
| RunQueue.Manage | 是 | 否 | 否 | 是 | 否 |
| WorkerStatus.Read | 是 | 否 | 是 | 是 | 只读 |
| Mcp.GetListServers | 是 | 否 | 是 | 是 | 只读 |

说明：最终权限以 AICopilot 后端权限常量和角色授权为准，前端只展示后端返回的 `canXXX`、审批状态和队列状态，不自行推断授权。
