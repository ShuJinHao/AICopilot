# AICopilot 阶段记录：Artifact Workspace P9

- 日期：2026-05-20
- 范围：仅 AICopilot
- 阶段：P9 Artifact Workspace 产物交付中心闭环

## 已收口能力

- Artifact 持久化来源元数据：source mode、boundary、simulation/sandbox 标识、source label、query/result hash、row count、truncated 和 finalizedAt。
- Workspace DTO 增加 draft/final 分区和 P9 产物字段。
- Agent 生成 Markdown/HTML/PDF/PPTX/XLSX/chart 时继承业务查询或 sandbox 查询来源标识。
- 新增 artifact 预览、修订意见、草稿重新生成和 artifact 级 final 审批提交入口。
- 前端 Agent 工作台增加产物工作区、来源标识、hash/行数/截断状态和预览面板。
- P9 scope guard、迁移和聚焦后端测试进入验收入口。

## 保持关闭的边界

- Real CloudReadonly 默认关闭。
- `query_cloud_data_readonly` 默认 disabled / hidden / non-executable。
- Cloud/Edge 未纳入本阶段修改。
- 不接真实生产数据，不开放生产 Agent 查询。
