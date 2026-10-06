# AICopilot

## 修改授权

- 严禁擅自修改用户未明确确认的内容，严格按已确认的项目、文件范围和操作执行。
- Git 提交、上传仅授权处理已确认的现有改动，不授权额外修复源码、测试、依赖、配置或其他项目。
- 遇到问题或 CI 失败，先说明原因、拟修改文件及影响并询问用户；未明确同意前不得修改，也不得为通过检查关闭门禁。


本文件是 AICopilot 的独立入口。默认只读取、修改本仓；不自动加载外层总规则、其他项目的初始化或其业务文档。

## 按需路由

- AICopilot 产品、只读、OIDC、RAG、DataAnalysis、MCP、Human-in-the-loop 和对话行为：读取 [AICopilot 业务规则](docs/AICopilot业务规则.md)相关章节。
- Cloud AiRead、`BusinessQuery`、DTO、查询确认、Text-to-SQL、SQL guard、Simulation 或 fallback：读取 [Cloud 只读数据分析契约](docs/Cloud只读数据分析契约.md)。
- `AgentSession`、Plan/Execute、Harness、Tool/MCP、批准、异常和前端错误：读取 [Agent 工作流与异常契约](docs/Agent工作流与异常契约.md)。
- 聚合、repository、DbContext、migration、事务、审计、Outbox、commit marker 或 RAG 文件持久化：读取 [DDD 聚合根边界](docs/DDD聚合根边界.md)。
- 部署、HTTP/OIDC issuer、secret、模型 seed、镜像或 Runner：读取 [AICopilot 安全部署契约](docs/AICopilot安全部署契约.md)及 [部署入口](deploy/enterprise-ai/README.md)；只有执行该目标的部署时才追溯控制面章节。
- 当前架构状态和下一退出门：读取 [AI 架构路线图](docs/AI架构路线图.md)。
- 只有修改 `src/vues/AICopilot.Web` 时才读取该目录的 `AGENTS.md`。

只读受影响章节；仅变更跨系统身份或接口时追溯契约引用的 `BR-*` 和提供方资料，引用不授权修改对方项目。

## 不可缺失边界

AICopilot 是严格只读的制造数据分析助手，不是 Cloud/Edge 制造主系统；SQL、MCP、Tool、workflow、后台任务、Plan/Execute 或人工批准均不得把 Cloud/MES/ERP 写入、生产控制或越权访问变成允许动作。

- AI只从Cloud读取经过权限过滤的工序、设备插件、PLC、Schema和当前状态投影，不直连现场插件数据库，也不按AP/CP、正负极、P1/P2或固定PLC数量推断查询范围。
- 交互式 Cloud 查询必须绑定当前有效 Cloud 用户委托；缺失、过期、撤销、无效或范围不足直接拒绝，`AiGateway.Chat` 和静态系统 Token 都不能代表全部 Cloud 数据权限。
- 当前真实 Cloud 只保留 typed AiRead；Direct DB/Text-to-SQL 整体关闭。只有委托范围贯穿 SQL 且 AST 强制行条件或数据库 RLS 能独立证明范围后，才允许另行复审。
- Cloud 工号 `101650` 是唯一免本地密码确认的自动收编例外，不是唯一 `Admin`；本地 emergency admin 使用独立配置和恢复链，不得与其共用账号或自动绑定。
- `ClientCode`是设备插件唯一跨端业务身份，但普通回答优先展示Cloud设备名称；`DeviceId`、`ModuleId`、`ProcessType`和`TypeKey`不得成为AI维护的第二套设备映射。
- Cloud返回快照不可用或已过期时，AI必须准确说明，不得把不可用冒充为空PLC、在线或无数据。

## 执行与维护

- 沟通/审计只读；纯文档整理做静态核对。开发运行不可弱化的 Architecture、Security 与受影响 Business；部署机制变化追加 DeploymentContract。普通 build 不运行测试，全量/Quality/CrossProject 须明确授权，影响不明时停止并列出文件。
- 普通部署按本仓入口和明确目标执行，不编辑源码、不测试、不构建；生产验收与当前候选分开记录。已投产接口、身份和数据行为改变须有明确迁移、版本化与回退方案。
- 保护现有改动和秘密；禁止硬编码凭据、未经批准的漏洞/预览依赖、隐蔽兼容路径和伪造验证。Git 仅操作本仓，使用 `codex/` 分支和 PR，不强推主分支。
- 业务、技术与部署规则各归现有唯一正文；精简保留稳定编号和未关闭问题，历史留 Git 与不可变证据，不新增阶段流水、重复手册或总结。
