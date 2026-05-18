# A助理错误码与前端提示说明

日期：2026-05-18

前端必须对以下错误码显示用户可理解的提示：

| 错误码 | 前端提示方向 |
| --- | --- |
| `missing_permission` | 当前账号缺少权限 |
| `cloud_readonly_tool_disabled` | Cloud 只读工具未启用 |
| `tool_requires_approval` | 工具需要人工审批 |
| `agent_task_run_queued` | 任务已入队，禁止重复运行 |
| `agent_task_run_in_progress` | 任务正在执行 |
| `agent_worker_unavailable` | DataWorker 不可用 |
| `agent_worker_workspace_mismatch` | Worker 与 HttpApi 工作区不一致 |
| `artifact_finalized` | 产物已 final，不可编辑 |
| `workspace_manifest_invalid` | 工作区清单无效 |
| `planner_model_unavailable` | 规划模型不可用 |
| `tool_disabled` | 工具已禁用 |
| `tool_blocked` | 工具被安全策略阻断 |
| `tool_permission_denied` | 缺少工具调用权限 |
| `agent_plan_invalid` | Agent 计划未通过后端校验 |

实现位置：`src/vues/AICopilot.Web/src/stores/chatErrorStore.ts`。
