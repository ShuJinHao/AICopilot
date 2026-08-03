# AICopilot Instructions

工作区 `../docs/总规则.md` 是唯一默认必读入口。本文件只负责项目路由和不可缺失的硬边界，不是第二份详细规则库。

## 按需路由

- 进入 AICopilot 实际修改后，只读取 `docs/AICopilot业务规则.md` 中与本批模块直接相关的章节、相关源码和受影响测试。
- Cloud AiRead、业务数据源插件或 Text-to-SQL：再读唯一技术正文 [Cloud 只读数据分析契约](docs/Cloud只读数据分析契约.md) 的相关章节。
- Agent workflow、Plan/Chat、MCP/Tool、审批、异常或前端错误：再读唯一技术正文 [Agent 工作流与异常契约](docs/Agent工作流与异常契约.md) 的相关章节。
- 聚合、repository、DbContext、迁移、审计、Outbox、事务、commit marker 或 RAG 文件持久化：再读唯一技术正文 [DDD 聚合根边界](docs/DDD聚合根边界.md) 的相关章节。
- Analyzer、测试物理归口或 `AIARCH`/`AI-SEC` Rule ID：再读业务规则工程章节、对应 Analyzer 账本和受影响测试。
- AI 架构阶段、剩余门禁或退出条件：再读 `docs/AI架构路线图.md` 的对应章节；路线图只描述当前候选状态，不是生产验收记录。
- 部署或生产配置：再读 `docs/AICopilot安全部署契约.md`、`deploy/enterprise-ai/README.md` 和工作区部署总览的对应章节。
- 只有修改 `src/vues/AICopilot.Web` 时才读取该目录的 `AGENTS.md`。
- 历史事实只从 Git、发布目录和部署记录追溯；不得新建滚动复盘、历史核心类文档、日期式治理快照或第二份部署指南。真实事故统一写入工作区 `docs/事故/生产事故.md` 或 `docs/事故/部署事故.md`。

## 项目硬边界

- AICopilot 是只读分析助手和受控编排系统，不是制造业务主数据源；默认只修改本项目，跨 Cloud/Edge 写入必须由用户当前轮明确授权。
- 当前唯一真实外部业务数据源是 Cloud；MES/ERP 以后只通过统一 provider/profile 插件扩展。AICopilot 不得通过 SQL、MCP、Tool、workflow、后台任务或隐藏适配器写 Cloud。
- 业务查询保持 typed provider 优先和服务端受控 fallback；模型只看到 `BusinessQuery`，不得直接获得独立 Text-to-SQL 工具，也不得借 fallback 绕过身份、权限、只读或数据源边界。结果矩阵、查询确认、Text-to-SQL、Simulation 和 fallback 决策细节只由上述 Cloud 唯一技术正文定义。
- SQL 安全只由执行咽喉的共享 AST guard、所选 source profile 和只读数据库账号共同负责；Prompt 不维护写操作动词黑名单。
- Plan / Execute 只是行为状态，不是安全隔离或授权边界；模式与授权正交，任何切换都不得扩大或缩小用户权限、可用工具、数据边界或批准策略。具体运行时语义按需读取上述唯一技术正文，本文件不重复定义。
- Cloud/MES/ERP 写入、生产控制和越权访问继续由身份、Tool Gate、`AiToolSafetyPolicy`、SQL AST guard、只读账号和 MCP 治理阻断，且不依赖 Plan / Execute。最终回答、图表和产物只能基于本轮或用户显式引用的封存证据，不得伪造事实。

## 任务与部署

- 沟通/审计只读且不运行测试；业务开发只运行 Architecture、Security 和 owner 选出的受影响 Business。使用 `dotnet test --filter` 时，必须用同一项目和 filter 的 `--list-tests` 证明至少命中 1 项；0-hit 即使命令退出 0 也不是有效诊断证据。全量、coverage、mutation、duplication、Quality、CrossProject 和三端对齐只在用户明确授权时运行；影响无法归属时停止。
- 普通部署只走工作区 `deploy/Deploy-Changed.ps1`：代码视为已完成，要求 clean、已提交的 `main`，可 push 现有 HEAD，但不得创建提交、编辑源码/测试/规则/文档或在失败后顺手修代码。
- 三端从零部署只走工作区 `deploy/Deploy-FromZero.ps1`；缺 Keychain 根密钥时远端零写入，AI 阶段只处理只读凭据/权限、migration、模型 seed 和健康，不创建设备、不注册 `ClientCode`、不轮换设备 bootstrap secret。
- 长期规则直接进入本文件、`docs/AICopilot业务规则.md` 或对应专题契约；事故只进入工作区事故文档，普通提交和版本变化只由 Git、发布目录与部署记录保存。
