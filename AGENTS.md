# AICopilot Instructions

工作区 `../docs/总规则.md` 是唯一默认必读入口。本文件只负责 AICopilot 按需路由和不可缺失的只读硬边界，不承载第二份业务、工程、测试或部署规则。

## 按需路由

- 跨系统业务对象、`工序 → 设备插件 → PLC → 数据`和唯一身份关系：读取 `../docs/业务规则.md` 对应 `BR-*`。
- AICopilot 产品、只读、OIDC、RAG、DataAnalysis、MCP、Human-in-the-loop 和对话行为：读取 [AICopilot 业务规则](docs/AICopilot业务规则.md)相关章节。
- Cloud AiRead、`BusinessQuery`、DTO、查询确认、Text-to-SQL、SQL guard、Simulation 或 fallback：读取 [Cloud 只读数据分析契约](docs/Cloud只读数据分析契约.md)。
- `AgentSession`、Plan/Execute、Harness、Tool/MCP、批准、异常和前端错误：读取 [Agent 工作流与异常契约](docs/Agent工作流与异常契约.md)。
- 聚合、repository、DbContext、migration、事务、审计、Outbox、commit marker 或 RAG 文件持久化：读取 [DDD 聚合根边界](docs/DDD聚合根边界.md)。
- 部署、HTTP/OIDC issuer、secret、模型 seed、镜像或 Runner：读取 [AICopilot 安全部署契约](docs/AICopilot安全部署契约.md)、`deploy/enterprise-ai/README.md` 和工作区部署总览对应章节。
- 当前架构状态和下一退出门：读取 [AI 架构路线图](docs/AI架构路线图.md)。
- 只有修改 `src/vues/AICopilot.Web` 时才读取该目录的 `AGENTS.md`。

## 不可缺失边界

AICopilot 是严格只读的制造数据分析助手，不是 Cloud/Edge 制造主系统；SQL、MCP、Tool、workflow、后台任务、Plan/Execute 或人工批准均不得把 Cloud/MES/ERP 写入、生产控制或越权访问变成允许动作。

- AI只从Cloud读取经过权限过滤的工序、设备插件、PLC、Schema和当前状态投影，不直连现场插件数据库，也不按AP/CP、正负极、P1/P2或固定PLC数量推断查询范围。
- 交互式 Cloud 查询必须绑定当前有效 Cloud 用户委托；缺失、过期、撤销、无效或范围不足直接拒绝，`AiGateway.Chat` 和静态系统 Token 都不能代表全部 Cloud 数据权限。
- 当前真实 Cloud 只保留 typed AiRead；Direct DB/Text-to-SQL 整体关闭。只有委托范围贯穿 SQL 且 AST 强制行条件或数据库 RLS 能独立证明范围后，才允许另行复审。
- Cloud 工号 `101650` 是唯一免本地密码确认的自动收编例外，不是唯一 `Admin`；本地 emergency admin 使用独立配置和恢复链，不得与其共用账号或自动绑定。
- `ClientCode`是设备插件唯一跨端业务身份，但普通回答优先展示Cloud设备名称；`DeviceId`、`ModuleId`、`ProcessType`和`TypeKey`不得成为AI维护的第二套设备映射。
- Cloud返回快照不可用或已过期时，AI必须准确说明，不得把不可用冒充为空PLC、在线或无数据。
