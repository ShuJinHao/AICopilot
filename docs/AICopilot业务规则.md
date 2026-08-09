# AICopilot 业务规则

> **状态：第 0～7 批候选代码继续保留；复审问题尚未全部收口，当前候选代码不是生产基线；真实 Windows 验收、生产绑定迁移和部署仍未执行。**
>
> 本文件只细化 AICopilot 产品业务。跨端业务真值、当前现场和已经关闭的裁决统一见[业务总纲](../../docs/业务规则.md)；API、DTO、SQL、锁、持久化、部署和测试算法只见第 11 章专题契约。

## 1. 产品定位与核心职责

`AICopilot` 是分析助手和受控编排系统，不是 Cloud 制造主数据系统，也不是 Edge 现场运行或生产控制系统。

产品能力包括：

- 主对话：在同一会话内提供 Plan/Execute、澄清、知识检索、业务查询和受治理工具调用；
- RAG：基于文档和规则做问答、解释与总结；
- DataAnalysis：基于授权的只读数据源做查询、统计和分析；
- MCP：执行已配置、已授权且符合安全边界的工具；
- Human-in-the-loop：批准 AICopilot 自身允许但具有副作用或风险的动作。

产品不建设以下能力：

- 任意用户上传 Agent 定义后直接执行；
- 由模型、Prompt、插件或 MCP 扩大身份、权限、工具、数据源或证据边界；
- 用通用 SQL、MCP、Simulation 或模型推断冒充真实制造事实；
- 通过 AI 创建、修改、删除、补录、审批、派发或控制 Cloud/Edge 制造业务。

## 2. 制造数据层级与动态查询

本章继承业务总纲的`BR-DOM-*`、`BR-CLOUD-*`和`BR-AI-*`。

唯一查询层级为：

```text
工序 → 设备插件（ClientCode）→ PLC 总览 → 单 PLC 明细或业务数据
```

- AI 对制造数据严格只读，并使用与 Cloud 相同的业务层级。
- 一个设备插件就是一台具体现场设备；同一工序下不同设备插件可以拥有不同逻辑、PLC清单和Schema。
- AI只从Cloud动态读取经过当前用户权限过滤的工序、设备插件、PLC、插件实际安装版本、`TypeKey`、Schema和当前状态投影，不直连现场插件数据库。
- AI不维护AP、CP、正负极、P1、P2、固定PLC数量或固定字段映射作为生产真值。
- 用户只给出工序且该工序下存在多个设备插件时，必须询问或要求选择设备插件，不能默认第一条。
- 设备插件内存在多个PLC且范围不明确时，必须继续确认PLC范围，不能无依据猜测。
- 查询条件和结果必须保留真实工序、`ClientCode`、PLC身份、插件实际版本、`TypeKey`及Schema；普通回答优先展示Cloud设备名称，不向人员要求填写内部身份。
- Cloud返回PLC快照不可用或状态已过期时，AI必须明确返回“不可用”或“已过期”，不能冒充空PLC、在线或无数据。
- 新插件应通过 Cloud 元数据和通用查询能力自然出现，不修改 AI 核心意图映射或增加名称分支。
- 空结果保持空。Simulation、推断、默认值或其它插件数据不得用于补齐真实查询。

`ClientCode`是设备插件唯一跨端业务身份。`DeviceId`只属于Cloud内部关联，`ModuleId`只属于包内加载，`ProcessType`只表达工序分类；`TypeKey`只表达插件声明的业务记录类别，一个插件版本可以声明多项。AI不得把这些字段建立成另一套设备身份，也不得把 `ProcessType`、`TypeKey` 与 `ModuleId` 混同。精确DTO和`BusinessQuery`绑定由Cloud只读技术契约承载。

## 3. Cloud 严格只读边界

继承 `BR-AI-001`、`BR-AI-009`～`BR-AI-024`。

允许：

- 读取当前用户获准范围内的 Cloud 规则、业务对象和只读数据；
- 分析、解释、汇总、检索、趋势判断和异常说明；
- 生成建议、草稿和排查思路；
- 基于本轮真实查询事实生成回答、表格或 Widget。

禁止：

- 注册、修改、删除设备、人员、角色、权限或配方；
- 写入、补录、删除或修正产能、日志、生产记录和过站数据；
- 触发 Cloud 业务流程、派发任务、代办审批或直接写 Cloud 数据库；
- 通过 SQL、MCP、Tool、workflow、后台任务或隐藏适配器间接调用 Cloud 写能力；
- 把 Human-in-the-loop、Plan/Execute 或用户指令解释成越过永久只读边界的授权。

查询首次执行前，应确认数据源、数据类型、工序、设备插件、PLC、`TypeKey`、时间范围和过滤条件。设备、PLC 或 `TypeKey` 为零个或多个候选时都必须询问或返回明确不可用，不能选择第一条。只允许在同一 `SessionId` 内复用未变化的已确认范围；来源、时间或过滤变化后重新确认。普通回答不得泄漏bootstrap secret、SQL、连接串、token或内部凭据；ClientCode仅在确有设备消歧或审计需要时展示，不要求用户维护。

- 交互式 Cloud 查询必须绑定当前有效 Cloud 用户的委托范围，并在每次查询时重新验证委托主体、有效期、撤销状态和授权范围；`AiGateway.Chat` 只表示可以使用查询工具，不代表拥有全部 Cloud 数据权限。
- 委托缺失、过期、撤销、无效、主体不匹配或范围不足时必须直接拒绝，禁止解释为 Global、租户全量或“无过滤”。
- 静态系统 Token 不得代替当前用户读取交互式数据。显式非交互系统任务如以后存在，必须使用独立系统身份、最小范围和结构化审计，不能复用用户查询链。

当前外部制造数据来源及未来 MES/ERP 扩展状态只按 `BR-AI-009` 阅读，不由项目文档擅自升级为已确认长期规则。

## 4. OIDC 身份业务

- Cloud OIDC 只解决 Cloud 身份、账号有效性和员工有效性。
- AICopilot 保留本地 AI 用户、AI 角色、AI 权限、SecurityStamp、本地禁用、审计和 emergency admin；Cloud role 不直接映射或覆盖 AI role。
- AICopilot 不读取 Cloud Cookie，不接收 Cloud 密码，不直连 Cloud 用户表；EdgeClient 不参与 Cloud 与 AICopilot 的 OIDC 对齐。
- 除工号 `101650` 的唯一例外外，Cloud 身份首次登录命中同名、启用且尚未绑定其它 Cloud 身份的本地 AI 账号时，必须要求用户使用该本地账号密码确认，不能自动覆盖或创建重名账号。
- 本地密码确认只建立外部身份与已有 AI 用户的一对一绑定，必须保留原有 AI 角色、权限、禁用状态和安全会话治理。
- 完全相同的既有绑定可以幂等复用；外部身份、本地用户或规范化用户名与其它绑定冲突时必须拒绝，不得覆盖或创建第二账号。
- 密码错误可在有效外部会话和登录限流内重试；取消、过期、账号禁用、身份失效或不可恢复冲突时清理外部会话。
- 绑定成功、拒绝、冲突、取消和失效都形成结构化脱敏审计；密码、token、cookie 和原始认证材料不得进入前端持久化、日志或审计。
- Cloud 工号 `101650` 是规范的 Cloud 绑定 AI `Admin`，也是非秘密固定业务值，不进入 Keychain、密码或 emergency 配置。AI 中没有该账号时，创建本地 AI 账号、赋予 `Admin` 并绑定当前有效且工号精确为 `101650` 的 Cloud 主体。
- 已有同名、启用、有效且未绑定冲突主体的账号时，确保其具有 `Admin` 后直接绑定；账号禁用、规范化身份冲突或已绑定其它 Cloud 主体时拒绝覆盖。
- 升级数据库中未绑定且仍带本地密码的 `101650` 可能属于历史 emergency admin seed，不能证明为“未冲突账号”；Identity 初始化和首次 Cloud 绑定必须以 `emergency_admin_canonical_cloud_admin_conflict` 失败，不得自动改名、合并、删除、提升或绑定。已有同一 Cloud 绑定的幂等登录不受此冲突判定影响。
- `101650` 是唯一免本地密码确认的自动收编例外，不是系统唯一 `Admin`；其它用户继续遵守普通 OIDC 本地密码确认、冲突拒绝和角色不映射规则。
- 本地 emergency admin 是独立恢复账号，使用独立配置和密码，不与 `101650` 共用用户名、配置或自动绑定链，也不得被 Cloud OIDC 自动收编。其用户名规范化后等于 `101650` 时，启动校验和部署预检都必须以稳定原因失败，不自动改名、合并或删除。

- Cloud 账号、员工、权限和设备范围变化由 Cloud 在每次 AiRead 请求实时拒绝；AICopilot 同时允许当前已认证用户主动注销 JWT 内的当前 grant，并在过期或撤销后清除密文 Token 和到期元数据。两条撤销链路互不代替。

并发锁、fresh-read、唯一约束、Identity 事务和提交结果属于技术实现；事务与持久化见[DDD 聚合根边界](./DDD聚合根边界.md)，错误码和前端异常见[Agent 工作流与异常契约](./Agent工作流与异常契约.md)。

## 5. RAG 业务

- RAG 只用于知识检索、规则解释和文档问答。
- 文档内容不能反向覆盖已确认业务规则或 Cloud 真实查询事实。
- RAG 结果与已确认规则或真实数据冲突时，以已确认业务来源和真实数据为准，并向用户说明冲突与证据边界。
- RAG 文件持久化、对账、journal 和恢复算法只见 DDD 专题契约。

## 6. DataAnalysis 业务

- DataAnalysis 只能连接经过授权的只读业务数据源。
- 当前真实 Cloud 查询只保留 typed AiRead provider；所有真实 Cloud Direct DB 与 Text-to-SQL 整体关闭，`Unsupported`、`Unavailable` 或任何其它 typed 结果都不得触发 SQL fallback。
- 模型只看到受治理的 `BusinessQuery`，不能直接取得 SQL 执行能力，也不能决定、触发或绕过当前关闭状态。
- Direct DB/Text-to-SQL 的现有代码、配置、授权脚本和治理定义可以保留为冻结资产；保留不代表已启用、可部署或具备生产查询权限。
- 只有当前 Cloud 用户委托范围能够贯穿到 SQL，并由 AST 强制注入不可移除的行条件，或数据库 RLS 能独立证明相同范围时，才允许另行复审是否开放；只读账号、表列白名单或 Prompt 限制本身不够。
- 未来复审仍必须拒绝 DML、DDL、管理语句、多语句和未授权表列，且不得削弱身份、数据源绑定或已确认范围。
- 查询结果只用于分析和展示，不产生制造业务写入；Simulation 结果必须明确标识，不能冒充真实 Cloud 结果。
- 最终回答只总结本轮真实查询事实；推断与建议必须明确标注，不能表达为已经执行的生产动作。

provider/profile、DTO、结果矩阵、确认协议、Text-to-SQL、AST guard、行数限制、Simulation 和 fallback 决策只见[Cloud 只读数据分析契约](./Cloud只读数据分析契约.md)。

## 7. MCP 业务

- MCP 是受治理工具入口，不是 Cloud、MES 或 ERP 的写入口。
- 工具必须声明稳定身份、风险和只读属性；无法证明治理属性时不得向模型暴露或执行。
- 涉及文件、外部系统、命令执行或其它副作用的允许工具必须进入批准流程。
- 不得配置直接或间接调用 Cloud 写接口、生产控制或越权数据的 MCP 工具。
- MCP discovery、身份、Schema、治理或运行状态失效时应 fail-closed，撤下不可证明安全的工具；这不等于删除合法的本地非 Cloud 工具。
- MCP 输出只能作为本轮真实工具结果使用；远端错误、缺失结构或 Schema 不匹配不能由文本或默认值伪装成成功。

SDK 版本、transport、canonical name、Schema、timeout、refresh fingerprint、输出校验和 Harness 工具注册只见[Agent 工作流与异常契约](./Agent工作流与异常契约.md)。

## 8. Human-in-the-loop 业务

- Human-in-the-loop 只批准 AICopilot 自身已经允许但具有风险的动作，不能覆盖制造数据严格只读边界。
- 需要批准的工具必须逐次展示规范工具身份和脱敏的安全参数摘要，不提供永久批准或“不再询问”。
- 批准只绑定当前用户、会话和本次已展示动作；身份、权限、工具或参数变化后必须重新判断。
- 人工批准不能把 Cloud/MES/ERP 写入、生产控制、越权访问或其它永久硬阻断变成允许动作。

批准绑定、续流、取消、超时和异常时序只见[Agent 工作流与异常契约](./Agent工作流与异常契约.md)。

## 9. 对话产品与 Plan / Execute

- 主产品形态是 Codex-like 对话流，不是任务控制台、试点运营台或系统调试台。
- `AiGateway.Chat` 只授权进入对话并使用经过治理的工具目录；每个工具及其数据仍需独立权限和当前委托，不能把聊天权限解释为全部 Cloud 数据权限。
- 普通用户只看到问题、回答、Plan/Execute、批准卡、Widget 和安全错误；实现术语、原始 SQL、连接串、token、endpoint、工具原始参数和未脱敏错误不进入普通回答。
- 用户可见运行状态必须来自本轮真实请求、stream、工具和错误事实，并按会话隔离；禁止假进度、假查询次数、假返回行数和跨会话残留。
- Widget 只能重排本轮真实回答与查询结果，不能新增指标、补未查询数据或改写模型结论。
- 后端结构化错误和失败事件必须准确呈现，不能用泛化文案覆盖真实诊断。
- 批准卡提交后锁定，并以服务端待批准状态为权威；不显示永久批准选项。
- `Interrupted` 或 `ResetRequired` 不自动恢复、重放旧 turn 或复用旧批准。
- 模型推理标签、内部 metadata 和工具协议 chunk 不得作为普通用户正文显示。

Plan / Execute 的产品语义：

- `Plan` 用于交互式澄清、调查并形成 Todo；
- `Execute` 用于连续完成已经明确的 Todo；
- 两者只是行为模式，不是安全隔离或授权边界；切换模式不得改变身份、权限、工具、数据范围或批准策略。

会话持久化、stream/chunk、Harness 模式、Tool Gate、错误协议和前端实现分别见[Agent 工作流与异常契约](./Agent工作流与异常契约.md)及前端 `AGENTS.md`。

## 10. 当前现场与冻结边界

- 当前 AP/CP 插件、PLC、MG1/MG2、MES 身份和版本只引用 `BR-CURRENT-001`～`BR-CURRENT-009`，不在 AI 规则复制固定映射。
- `CP`、`AP` 是当前插件标识，不是工序枚举；“正极/负极 → cp/ap”不能作为 AI 永久推导规则。
- 当前 12 个 PLC、弹夹字段、MG1/MG2 和 MES 编码不能推广到其它插件。
- `P1-APUC`、`P2-CPUC`只属于当前MES参数，不是Cloud设备身份或设备—插件绑定证据。
- 配方和`R100`继续冻结，AI不得将其描述为当前已实现查询能力。
- AI不得通过固定名称、当前字段或兼容映射猜测设备身份；新增设备插件必须从Cloud动态元数据出现。

## 11. 技术、工程与部署路由

| 内容 | 唯一技术入口 |
|---|---|
| Cloud AiRead、`BusinessQuery`、DTO、查询确认、Text-to-SQL、SQL guard、Simulation、fallback | [Cloud 只读数据分析契约](./Cloud只读数据分析契约.md) |
| AgentSession、Plan/Execute、Harness、Tool/MCP、批准、异常和前端错误 | [Agent 工作流与异常契约](./Agent工作流与异常契约.md) |
| 聚合、repository、DbContext、迁移、事务、审计、Outbox、commit marker、RAG 文件 | [DDD 聚合根边界](./DDD聚合根边界.md) |
| HTTP/OIDC issuer、secret、模型 seed、镜像、migration 和 Runner | [AICopilot 安全部署契约](./AICopilot安全部署契约.md)及 `../deploy/enterprise-ai/README.md` |
| 当前架构状态与下一退出门 | [AI 架构路线图](./AI架构路线图.md) |
| 测试范围、授权、质量和工作区部署入口 | [工作区总规则](../../docs/总规则.md)及[上传部署总览](../../docs/上传部署总览.md) |

本文不复制精确 API、DTO、SQL、MCP SDK、锁算法、migration、Analyzer、TestKit、coverage 或部署步骤。

## 12. 原文迁移覆盖

| 原章节 | 第二轮归属 |
|---|---|
| 改动收口门禁 | 项目 `AGENTS.md` 与工作区总规则 |
| 核心职责、战略性不做 | 第 1 章 |
| Cloud 只读边界 | 第 2、3、6 章；精确 AiRead 转 Cloud 专题契约 |
| 固定 AP/CP 查询映射 | 第10章当前现场，不再作为通用规则 |
| OIDC 与 JIT | 第 4 章；锁和事务算法转 DDD、Agent 契约 |
| RAG | 第 5 章；文件实现转 DDD 契约 |
| DataAnalysis | 第 6 章；DTO、SQL 和 fallback 转 Cloud 契约 |
| MCP | 第 7 章；SDK、Schema、timeout 和 refresh 转 Agent 契约 |
| Human-in-the-loop | 第 8 章；精确批准协议转 Agent 契约 |
| 对话、Plan / Execute | 第 9 章；Harness 和前端实现转 Agent 契约 |
| HTTP、密钥、模型 seed 与部署 | 第 11 章安全部署与 README |
| 文档入口与工程边界 | 第 11 章专题路由和工作区总规则 |
