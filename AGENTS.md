# AICopilot Instructions

工作区 `../docs/总规则.md` 是唯一默认必读入口。本文件只负责项目路由和不可缺失的硬边界，不是第二份详细规则库。

## 按需路由

- 进入 AICopilot 实际修改后，只读取 `资料/AICopilot业务规则.md` 中与本批模块直接相关的章节、相关源码和受影响测试。
- Cloud AiRead、业务数据源插件或 Text-to-SQL：再读 `docs/Cloud只读数据分析契约.md` 的相关章节。
- Agent workflow、Plan/Chat、MCP/Tool、审批、异常或前端错误：再读 `docs/Agent工作流与异常契约.md` 的相关章节。
- 聚合、repository、DbContext、事务、文件持久化：再读 `docs/DDD聚合根边界.md` 的相关章节。
- Analyzer、测试物理归口或 `AIARCH`/`AI-SEC` Rule ID：再读业务规则工程章节和 `docs/AI架构治理清单.md` 的命中条目，不全文加载清单。
- 部署或生产配置：再读 `docs/AICopilot安全部署契约.md`、项目部署指南和工作区部署总览的对应章节。
- 只有修改 `src/vues/AICopilot.Web` 时才读取该目录的 `AGENTS.md`。
- 复盘、历史记录、旧计划和治理证据只在回归、冻结链路冲突、失败原因不明、同类故障追溯或用户明确要求时按关键词读取命中邻域。

## 项目硬边界

- AICopilot 是只读分析助手和受控编排系统，不是制造业务主数据源；默认只修改本项目，跨 Cloud/Edge 写入必须由用户当前轮明确授权。
- 当前唯一真实外部业务数据源是 Cloud；MES/ERP 以后只通过统一 provider/profile 插件扩展。AICopilot 不得通过 SQL、MCP、Tool、workflow、后台任务或隐藏适配器写 Cloud。
- 每个分析任务先确认来源、数据类型、对象、时间和过滤条件；业务插件优先，只有同源 `Unsupported`/`Unavailable` 可尝试 Text-to-SQL，空集、询问、未授权和凭据失败不得绕过，Simulation 必须显式选择。
- SQL 安全只由执行咽喉的共享 AST guard、所选 source profile 和只读数据库账号共同负责；Prompt 不维护写操作动词黑名单。
- Plan 模式在用户确认前只生成草案，不执行查询、工具或 Worker；最终回答、图表和产物只能基于本轮或用户显式引用的封存证据，不得伪造事实。

## 任务与部署

- 沟通/审计只读且不运行测试；业务开发只运行 Architecture、Security 和 owner 选出的受影响 Business。全量、coverage、mutation、duplication、Quality、CrossProject 和三端对齐只在用户明确授权时运行；影响无法归属时停止。
- 普通部署只走工作区 `deploy/Deploy-Changed.ps1`：代码视为已完成，要求 clean、已提交的 `main`，可 push 现有 HEAD，但不得创建提交、编辑源码/测试/规则/文档或在失败后顺手修代码。
- 三端从零部署只走工作区 `deploy/Deploy-FromZero.ps1`；缺 Keychain 根密钥时远端零写入，AI 阶段只处理只读凭据/权限、migration、模型 seed 和健康，不创建设备、不注册 `ClientCode`、不轮换设备 bootstrap secret。
- 只有形成长期规则、修复历史回归、处理生产事故或改变部署机制时，才更新项目复盘。
