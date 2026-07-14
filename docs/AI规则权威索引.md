# AICopilot 规则权威索引

本文档是 AICopilot 当前 Rule ID 注册表，但不是已生效的 required CI trust root。`TEST-GOV-RULE-EXTRACTION-001-AI` 已覆盖 111/111 个历史 section、18 个细粒度命题和工作区历史 45/45 个候选源行，`Needs-Decision=0`；提取结果已由独立且已推送提交 `190c458db19a81c9e766117cd0d785836c76d99f` 固定，并完成主 agent、非作者与三项目交叉终审，因此 `projectRuleExtractionClosure=true`。`TEST-GOV-RULE-EXTRACTION-001-AI-ENTRY` 仅把项目滚动复盘迁移为条件检索，须以本批独立提交后生效。base-owned required context、branch protection、独立平台 reviewer 和当前 required run 仍未证明，`trustRoot.effective=false`、`E0=false`，不能与 §6.5 的项目规则提取关单混写或互相替代。

- 安全部署：[`AICopilot安全部署契约.md`](AICopilot安全部署契约.md)
- Cloud 只读：[`Cloud只读数据分析契约.md`](Cloud只读数据分析契约.md)
- Agent 工作流与异常：[`Agent工作流与异常契约.md`](Agent工作流与异常契约.md)
- DDD 与持久化：[`DDD聚合根边界.md`](DDD聚合根边界.md)
- 前端：[`../src/vues/AICopilot.Web/AGENTS.md`](../src/vues/AICopilot.Web/AGENTS.md)
- 测试治理：[`../../docs/三项目测试架构治理总计划.md`](../../docs/三项目测试架构治理总计划.md)

规则提取台账 `docs/testing/rule-extraction-ledger.json` 仅是证据索引，不是规则来源。

## 1. 治理与变更收口

<a id="ai-rule-gov-001"></a>
### AI-RULE-GOV-001 正式规则与历史材料边界

当前约束只能来自工作区总规则、项目规则和专题契约，历史复盘不得成为有效规则的唯一来源。AICopilot 项目滚动复盘不是默认必读材料；修复历史回归、修改已冻结业务链路、当前实现与专题契约冲突、测试失败原因无法从源码和契约确定、同类问题曾经发生或用户明确要求追溯历史决策时，必须按模块名、Rule ID、错误码或关键类型精准检索，检索词可补充故障症状。读取复盘不能替代当前规则和专题契约，代码改动完成前仍必须新增本批复盘。工作区 `docs/历史核心记录.md` 的既有入口地位不因本项目迁移自动取消。`projectRuleExtractionClosure=true` 只证明 §6.5 项目审计关单；base-owned required context、branch protection、平台独立 reviewer 和当前 required run 仍决定 trust-root/E0，当前均未生效。自动化治理债务：`AI-RULE-AUDIT-LINT-001`。

<a id="ai-rule-gov-002"></a>
### AI-RULE-GOV-002 改动收口与长期规则结论

代码改动完成前必须更新项目滚动复盘，保留范围、原因、影响面、验证命令和结果，并以“已沉淀为 Rule ID”或“无新增长期规则；原因”结束。最终回复必须列出复盘、权威位置和验证命令。门禁：项目架构/文档门禁；人工验收：PR 文档检查。

<a id="ai-rule-gov-003"></a>
### AI-RULE-GOV-003 错误码契约同步

新增、删除或重命名后端错误码必须同批更新前端错误契约并运行 `ErrorCodeCatalogTests`。门禁：`ErrorCodeCatalogTests`。

<a id="ai-rule-gov-004"></a>
### AI-RULE-GOV-004 大范围改动验证口径

架构、管道、权限、工作流或契约改动不得只以 filtered tests 宣称完成；未执行全量 BackendTests、required CI 或真实依赖验收时必须明确披露。治理清单中的 `Done` 只表示仓库内源码、脚本、文档和本地门禁完成，不表示服务器、容器、生产数据库、GitHub environment、runner 或 OIDC/Vault 已验收。门禁：required CI reconcile；人工验收：证据包数量、Skip 和环境对账。

<a id="ai-rule-gov-005"></a>
### AI-RULE-GOV-005 专题契约先读与入口收敛

触碰部署、Cloud 只读、Text-to-SQL、Agent workflow、异常、DDD 或前端状态前必须先读对应专题契约；阶段计划、批次报告和旧 readiness 文档不得作为当前执行入口。门禁：链接/旧入口检索；人工验收：变更范围与契约对应关系。

<a id="ai-rule-gov-006"></a>
### AI-RULE-GOV-006 修改前证据阅读

修改 AICopilot 已验收能力前必须先读当前规则、相关专题契约、对应源码、对应测试和近期 Git/GitHub 历史；不得只依据旧复盘摘要或审计意见直接改代码。

## 2. 测试架构

<a id="ai-rule-test-001"></a>
### AI-RULE-TEST-001 唯一治理入口

AICopilot 测试治理只认总计划、`TestAICopilotTestGovernancePolicy.ps1` 和行为负例脚本；不得新增第二套 baseline、runner、runsettings 或 required workflow。门禁：测试治理 policy/behavior self-test。

<a id="ai-rule-test-002"></a>
### AI-RULE-TEST-002 冻结旧桶与受控迁移

四个 Phase 0 旧测试桶冻结，不得新增测试声明、源文件或历史 Phase/Batch/Suite 主分类；迁移必须由有期限、可验证的 migration 记录指向目标测试类型。门禁：baseline/policy migration gate。

<a id="ai-rule-test-003"></a>
### AI-RULE-TEST-003 发现图与资产冻结

测试项目必须直接、无条件声明测试身份并进入 solution、统一 runner 和 Release discovery；项目图、workflow、traits、case、源文件和原始字节资产必须精确冻结，不能靠删测试、改脚本或替换 runner 换绿。门禁：policy validator、reference schema 与 self-test。

<a id="ai-rule-test-004"></a>
### AI-RULE-TEST-004 Required 零 Skip 与完整矩阵

Required lane 的 Skip 必须为 0；缺 Docker、Aspire 或 Browser 应在调度/preflight 失败。PR required job 必须运行 Architecture、Backend、legacy deterministic Eval、前端 Unit/build 和 deployment behavior，并保持 hard timeout，禁止 filter 或 `continue-on-error` 换绿。门禁：required workflow 与最终 reconcile。

<a id="ai-rule-test-005"></a>
### AI-RULE-TEST-005 Live 与 Simulation 分流

Cloud live 只允许显式 Manual/Release 的非生产真实契约执行，缺环境必须失败；普通 PR 不得执行真实 provider，也不得用 Stub/Simulation 代替跨仓 live 验收。门禁：workflow/policy 分类检查。

<a id="ai-rule-test-006"></a>
### AI-RULE-TEST-006 Eval 与 Analyzer 证据不得夸大

Legacy eval 只证明连续性，不能冒充生产 Golden；源码字符串/Regex 门禁只能称词法或动态门禁，不能冒充 Roslyn 编译语义。门禁：policy inventory；治理债务：AI-TEST-003 与后续 Analyzer 批次。

<a id="ai-rule-test-007"></a>
### AI-RULE-TEST-007 重复代码独立 ratchet

生产代码、测试 fixture 和测试 case 必须分开建立重复代码 baseline/ratchet；扫描不得自动合并语义不同的 Agent、RAG、DataAnalysis、MCP 或持久化实现。门禁：CodeQuality gate。

<a id="ai-rule-test-008"></a>
### AI-RULE-TEST-008 Required 证据失败传播

Required workflow 中经 `tee` 保存证据的 Shell 测试必须使用 `set -euo pipefail` 并合并 stderr；最终 reconcile 不能替代测试 step 自身正确传播失败。门禁：workflow self-test 和 native Linux behavior test。

<a id="ai-rule-test-009"></a>
### AI-RULE-TEST-009 不可变 PR 比较基线

PR scope 必须使用事件提供的 base commit SHA；手动入口只能接收严格校验并解析为 commit SHA 的仓库分支，workflow expression 只能经环境变量传入脚本，禁止把未校验 ref 拼入 shell。

<a id="ai-rule-test-010"></a>
### AI-RULE-TEST-010 Action runtime 兼容证据

官方 JavaScript action 必须使用受支持的 Node runtime；升级前同时核对 action metadata 和真实 runner 版本，不能仅为消除 warning 猜测兼容。

<a id="ai-rule-test-011"></a>
### AI-RULE-TEST-011 Shell Git mode 分类

被 workflow 或部署脚本直接执行的 tracked `.sh` 必须是 `100755`，仅由 `bash` 调用或 source 的脚本保持 `100644`；数据驱动门禁必须覆盖完整 tracked 集合且禁止交叉分类。

## 3. 会话、身份与授权

<a id="ai-rule-session-001"></a>
### AI-RULE-SESSION-001 resolved session 才能授权动作

sessionStorage raw id 只是待解析候选；Chat、Plan、Upload 等服务端动作只能使用 roster 内、已完成历史/审批/任务激活的 resolved session，并在 UI 与 store 两层复核。门禁：前端会话单测与 smoke。

<a id="ai-rule-session-002"></a>
### AI-RULE-SESSION-002 水合与跨会话重置

初始 `null -> A` 和 `A -> A` 水合不得清空草稿、附件、模式或高级选择；只有已解析会话 `A -> B` / `A -> null` 才重置。刷新失败必须恢复原可信投影。门禁：session authority/hydration tests。

<a id="ai-rule-session-003"></a>
### AI-RULE-SESSION-003 in-flight 临界区与 ACK-unknown

会话激活、流、任务、上传、下载、轮询或删除在途时，选择/新建/删除和路由离开必须由同一 store+router 临界区 fail-closed；DELETE ACK-unknown 必须先对账，不能猜测成功或恢复旧权限。门禁：session authority race tests。

<a id="ai-rule-session-004"></a>
### AI-RULE-SESSION-004 权威投影与归属

任务、工作区、产物和审批只能来自当前 roster/current task 的 canonical projection；任一步读取失败必须原子恢复上一代可信投影，审批未知时禁止 mutation。门禁：ownership/projection tests。

<a id="ai-rule-session-005"></a>
### AI-RULE-SESSION-005 非幂等 SSE 断线语义

Chat、Plan、Upload 或 Approval 等非幂等流在无 durable operation id 与结果回放协议时，silent timeout 或部分输出断线必须传播明确失败，禁止自动 reconnect 或重复提交。

<a id="ai-rule-session-006"></a>
### AI-RULE-SESSION-006 初始化错误按来源清除

session、catalog、history、approval 等初始化错误必须标识来源；只有同一来源的成功重试可以清除对应错误，composer 交互或其它来源成功不得抹掉仍未恢复的失败。

<a id="ai-rule-identity-001"></a>
### AI-RULE-IDENTITY-001 OIDC 身份边界

Cloud 只证明身份和账号/员工有效性；AI 角色、权限、SecurityStamp、本地禁用、审计和 emergency admin 留在 AICopilot。不得读取 Cloud Cookie、接收 Cloud 密码、直连 Cloud 用户表或直接映射 Cloud role。门禁：OIDC/Identity tests；人工验收：Cloud-AI 契约对齐。

<a id="ai-rule-identity-002"></a>
### AI-RULE-IDENTITY-002 enabled Admin 并发不变量

所有减少 enabled Admin 的路径必须在唯一 Identity transaction 中取得固定 PostgreSQL transaction advisory lock，并在 execution-strategy retry 中重新加锁和重读；人数存在与最小恢复权限是两个独立不变量。门禁：真实 PostgreSQL 竞争测试与架构 Analyzer 债务 AI-SEC-052。

<a id="ai-rule-identity-003"></a>
### AI-RULE-IDENTITY-003 status_version 与 SecurityStamp 撤销

Cloud 身份状态的确定性 `status_version` 变化、账号/员工失效或绑定身份不一致必须使 AICopilot 刷新本地 SecurityStamp 并拒绝旧 token；该轮询撤销链不等于事件驱动即时失效、SCIM 或 Cloud role 自动授权已完成。

<a id="ai-rule-identity-004"></a>
### AI-RULE-IDENTITY-004 Cloud 状态 service token

Cloud 身份状态接口的 service account token 只能来自部署 secret，禁止进入仓库、前端、日志或审计。

## 4. DDD、持久化与应用编排

<a id="ai-rule-persist-001"></a>
### AI-RULE-PERSIST-001 聚合根与专用 store

队列、投影、审计、worker 状态和执行过程记录不得成为聚合根或进入泛型 repository；新增聚合根必须更新白名单、分类和架构测试。门禁：`DddAggregateBoundaryTests`。

<a id="ai-rule-persist-002"></a>
### AI-RULE-PERSIST-002 Outbox 唯一所有权

无真实事件生产者的 DbContext 不得复制 Outbox mapping 或领域事件扫描；主 `AiCopilotDbContext` 是 migration owner，事件只能由唯一 repository commit participant 物化。门禁：DDD/ArchitectureTests。

<a id="ai-rule-persist-003"></a>
### AI-RULE-PERSIST-003 唯一提交引擎与官方重试

业务、Outbox、审计和 marker 必须由唯一 `PersistenceCommitEngine` 链在同一事务提交；execution strategy 使用官方事务重试入口，每次 attempt 对业务 Context 只保存一次，确认后才 Accept/清事件。RAG `DocumentId` 必须在进入可重放事务前由 PostgreSQL sequence 分配并作为 application-assigned 正整数使用；数据库生成的 child identity 必须有真实 PostgreSQL replay 测试证明重试后不重复。门禁：真实 PostgreSQL PersistenceCommit suite、RAG application-assigned metadata 与 sequence migration safety tests。

<a id="ai-rule-persist-004"></a>
### AI-RULE-PERSIST-004 commit-unknown 对账

COMMIT ACK 丢失必须用同事务 durable marker 和 fresh context 验证；marker 写入后 caller cancellation 不得中断确认。无法确认返回 `persistence_commit_outcome_unknown`，禁止自动重放。门禁：commit-ACK 丢失、verification transient/persistent 与 cancellation 测试。

<a id="ai-rule-persist-005"></a>
### AI-RULE-PERSIST-005 数据库绑定文件提交协议

RAG/UploadRecord 文件必须先写持久化 journal，再写文件并复用同一 commit id；请求与 DataWorker 使用同一 advisory lease。未知结果保留文件和日志，确认未提交后才删除。门禁：真实 PostgreSQL + 文件对账 suite。

<a id="ai-rule-persist-006"></a>
### AI-RULE-PERSIST-006 删除、durability 与 marker 清理

删除事件必须查询 journal、取得同一 lease 并在锁内退休记录后删文件；journal 不可读或 lease 活跃必须重试。marker 保留期长于对账延迟，有待处理日志时不得删除，损坏时 fail-closed。门禁：删除/cleanup behavior tests。

<a id="ai-rule-persist-007"></a>
### AI-RULE-PERSIST-007 Artifact 与知识库边界

ArtifactWorkspace 多文件原子性、历史 KB shadow 清理和同 KB/同文件并发去重是独立治理债务；知识库唯一写入口是 RAG Document API，禁止恢复 AiGateway shadow bridge。门禁/债务：AI-PERSIST-01d、AI-PERSIST-01e、AI-SEC-050。

<a id="ai-rule-persist-008"></a>
### AI-RULE-PERSIST-008 应用层 coordinator 边界

MediatR handler 不得新增三项及以上 persistence/store 依赖；跨聚合、跨 store、artifact lifecycle、审批、run queue、timeline 和 audit 编排必须进入明确命名 coordinator/query service。门禁：`AiGatewayHandlers_ShouldNotAddMultiPersistenceDependencyDebt` 及 coordinator ArchitectureTests。

<a id="ai-rule-persist-009"></a>
### AI-RULE-PERSIST-009 文件路径约束与脱敏

数据库绑定文件路径必须拒绝父目录、绝对路径、大小写旁路、控制字符及既有 symlink/reparse traversal；客户端路径先归一为安全 basename，日志和拒绝审计不得保留原始客户端路径。

<a id="ai-rule-persist-010"></a>
### AI-RULE-PERSIST-010 journal、文件与 marker 顺序

一次文件提交必须使用同一 commit id，按“durable journal → WriteThrough 文件 → repository marker”顺序执行；确认只删 journal，明确回滚先删文件后删 journal，结果未知保留二者等待对账。

<a id="ai-rule-persist-011"></a>
### AI-RULE-PERSIST-011 marker 与维护清理

维护 worker 只能处理超过安全延迟且取得 lease 的 journal；pending journal 对应 marker 不得清理，损坏 journal 或 marker 已提交但文件缺失必须保留证据并 fail-closed，禁止第二套 cron/手工清理器。

<a id="ai-rule-persist-012"></a>
### AI-RULE-PERSIST-012 目录 durability 与平台边界

文件或 journal 删除后必须完成父目录 durability barrier；标准生产限定 Linux 共享持久卷，macOS 只用于本地验证，Windows 明确拒绝，不得用空 flush 或容器层路径冒充耐久。

## 5. Agent、MediatR 与异常

<a id="ai-rule-agent-001"></a>
### AI-RULE-AGENT-001 统一工作流与 PlanDraft

Chat 与 Plan 复用统一意图、上下文和能力发现；Plan 确认前只能生成 PlanDraft，不得真实查询、调用工具或入队。能力未匹配不能阻断草案生成。门禁：workflow/Plan tests。

<a id="ai-rule-agent-002"></a>
### AI-RULE-AGENT-002 fan-out/fan-in 拓扑

Tools、Knowledge、DataAnalysis、BusinessPolicy 由 `AgentWorkflowTopology` 显式声明并保持 `Task.WhenAll` + `AgentWorkflowSink`；禁止拍平成串行或另起孤立链。门禁：workflow topology tests。

<a id="ai-rule-agent-003"></a>
### AI-RULE-AGENT-003 分支事实与合流门禁

分支完成、失败、跳过和原因必须成为结构化事实；必需分支失败时 Final Agent 不得伪造完整结果，路由/日志只记录安全摘要。门禁：branch status/aggregation tests。

<a id="ai-rule-agent-004"></a>
### AI-RULE-AGENT-004 Cloud 写入永久禁止

AICopilot 不得通过 SQL、MCP、Tool、Agent workflow、后台任务、隐藏适配器或 Human-in-the-loop 写 Cloud 业务数据。门禁：Cloud readonly architecture/policy tests。

<a id="ai-rule-agent-005"></a>
### AI-RULE-AGENT-005 MediatR 统一入口与顺序

横切行为只通过 `AddAICopilotMediatRPipeline` 注册；顺序固定为 Telemetry -> Validation -> Authorization。新增 validation 必须有真实 validator；telemetry 不得记录敏感载荷。门禁：ArchitectureTests 与 pipeline behavior tests。

<a id="ai-rule-agent-006"></a>
### AI-RULE-AGENT-006 stream 授权与透传

公开 `IStreamRequest` 必须声明权限并由 `AuthorizationStreamBehavior` 在 handler 前校验；stream 只能逐项透传，不得预读、缓存或套事务/审计边界。门禁：stream authorization tests。

<a id="ai-rule-agent-007"></a>
### AI-RULE-AGENT-007 事务/审计拥有者唯一

事务与审计拥有者必须显式唯一；MediatR behavior 不得开启事务、调用 SaveChanges 或保存审计。Identity 必须复用统一提交引擎，禁止恢复第二套手写 transaction/retry。门禁：transaction boundary ArchitectureTests。

<a id="ai-rule-agent-008"></a>
### AI-RULE-AGENT-008 安全异常契约

未知异常返回稳定错误码和 traceId，不泄露内部消息；提交结果未知使用专用 503 契约。生产 logger 不得把异常对象作为首参直接展开。门禁：SecurityHardening/ErrorCode/exception logging tests。

<a id="ai-rule-agent-009"></a>
### AI-RULE-AGENT-009 Controller 授权显式性

每个 HttpApi Controller 或 action 必须显式声明 `[Authorize]` 或 `[AllowAnonymous]`；匿名只能用于经契约批准的登录、OIDC 和初始化状态表面。

<a id="ai-rule-agent-010"></a>
### AI-RULE-AGENT-010 Stream 与 worker 身份边界

公开 `IStreamRequest` 必须声明权限要求，stream 管道不得承载事务或审计边界；普通 worker 请求无权限声明时不得解析登录用户态服务。

## 6. Cloud 只读与 DataAnalysis

<a id="ai-rule-cloud-001"></a>
### AI-RULE-CLOUD-001 Cloud AiRead 高频正式路径

设备日志、产能、生产数据以及已批准 Process/ClientRelease/DeviceStatus 语义优先且唯一使用 Cloud AiRead 正式只读 API；不得保留旧 route 或双轨实现。门禁：CloudAiRead client/endpoint policy/contract tests。

<a id="ai-rule-cloud-002"></a>
### AI-RULE-CLOUD-002 Direct DB 低频补充与 Simulation 隔离

CloudReadOnly Direct DB/Text-to-SQL 只用于治理白名单内低频探索或尚未覆盖的只读链路，不能压过正式 Cloud AiRead。Simulation 默认关闭，只能显式离线演示/测试，不得在真实链路为空、失败或未配置时补位。门禁：semantic routing/Simulation tests。

<a id="ai-rule-cloud-003"></a>
### AI-RULE-CLOUD-003 正式语义 no-fallback

正式六类语义在空集、规划失败、关闭或 Cloud 错误时不得 fallback 到 Direct DB、SQL 或 Simulation；Recipe 必须在 planner 前拒绝。门禁：semantic runner/guardrail tests。

<a id="ai-rule-cloud-004"></a>
### AI-RULE-CLOUD-004 端点表面全量对齐

`CloudAiReadEndpointPolicy` 与 client 必须逐项覆盖 Cloud 已批准的 `GET /api/v1/ai/read/*` 表面；接通高频端点不等于全量契约对齐。门禁：endpoint policy/contract coverage tests。

<a id="ai-rule-cloud-005"></a>
### AI-RULE-CLOUD-005 governed Text-to-SQL

Text-to-SQL 只接收治理白名单 schema 和安全修复上下文，执行前通过双层表/列只读 guard；失败 SQL 只允许当前调用内存回传，禁止持久化。repair 默认最多 3 次、硬上限 5 次；timeout、权限、凭据、非只读、系统表、敏感字段、多语句或写 SQL 等不可修复边界错误不得重试。门禁：TextToSql generator/guard/`CloudReadOnlyTextToSqlFallbackRunnerTests`。

<a id="ai-rule-cloud-006"></a>
### AI-RULE-CLOUD-006 readonly grant 唯一权威

Cloud PostgreSQL readonly 授权只认 `deploy/enterprise-ai/cloud-readonly/*.sql`；只允许显式表级 `GRANT SELECT`，禁止 all tables、默认权限、未来表自动授权或写/schema 权限。创建或轮换账号只能通过带显式确认的受控自动化更新专用 readonly role，禁止授予写权限、schema create、superuser、createdb、createrole 或 replication。新增表列 join 必须同步 schema、SQL、探针、preflight 和测试。门禁：`ArchitectureBoundaryTests.CloudReadOnlyReadonlyGrantSources_ShouldStayAlignedWithGovernedSchema` 与只读 guard/seeder tests；真实 role 权限仍须目标数据库验收。

<a id="ai-rule-cloud-007"></a>
### AI-RULE-CLOUD-007 DeviceLog 查询与追问证据

DeviceLog 使用真实级别枚举，多级别分析必须显式查询覆盖；改变级别、设备、工序或时间的追问必须重新查询，最终回答只基于本轮 `query_execution`。门禁：planner/follow-up/final-context tests。

<a id="ai-rule-cloud-008"></a>
### AI-RULE-CLOUD-008 最终上下文和值边界

DataAnalysis 最终 context 的 key 和值均属不可信消费边界；metadata/preview 共用唯一映射，flat preview 只输出显式标量白名单，SQL、连接串、source/table、prompt injection 和内部字段不得外泄。门禁：formatter/guardrail tests。

<a id="ai-rule-cloud-009"></a>
### AI-RULE-CLOUD-009 结构化展示只来自执行事实

DeviceLog display blocks、Widget、指标和证据只能从本轮只读查询行、`query_execution` 与 `semantic_summary` 派生，禁止模型、Markdown 解析或前端假数据编造。门禁：display builder/widget normalizer tests。

<a id="ai-rule-cloud-010"></a>
### AI-RULE-CLOUD-010 live 验收证据边界

显式 live-test 缺真实环境必须失败；token 只经子进程环境传递，单仓 Stub 不得宣称跨仓契约已验收。门禁：Cloud live workflow/policy；人工验收：真实非生产 provider 证据。

<a id="ai-rule-cloud-011"></a>
### AI-RULE-CLOUD-011 设备主数据与运行状态来源分离

Direct DB 设备主数据查询不得连接或投影 `device_logs.level` 作为设备状态，也不得暴露由最新日志派生的 `status`、`lineName` 或 `updatedAt`。设备运行状态只允许由 `Analysis.Device.Status` 读取 `/api/v1/ai/read/device-client-states` 的 Cloud 权威 `softwareStatus`、运行心跳原值和 freshness；日志级别只属于 `Analysis.DeviceLog.*`。任何映射或展示都不得把最新日志级别包装为设备在线、运行或健康状态。门禁：`CloudAiReadClientTests.Client_ShouldRouteDeviceCodeStatusDirectlyToDeviceClientStates`、`Client_ShouldSendDeviceClientStateQueryWithOfficialParametersOnly` 与 `IntentRoutingFallbackClassifierTests.TryClassify_ShouldNotInventLineFilterForDeviceMasterData`。

<a id="ai-rule-cloud-012"></a>
### AI-RULE-CLOUD-012 Cloud-only 上下文与旧 Direct DB 路线

已由 Cloud AiRead 覆盖的正式语义只能把 Cloud typed response 放入最终上下文；旧 Direct DB 优先、Cloud 失败后回退或双读路线均已废止，不得恢复。

## 7. 前端产品事实

<a id="ai-rule-frontend-001"></a>
### AI-RULE-FRONTEND-001 错误可见且安全

认证、目录、任务、产物和配置失败必须进入统一错误状态并给用户可见反馈；ProblemDetails 优先展示安全 code/detail/errors，未知 code 不得泄露 raw detail。门禁：frontend unit/smoke 与 ErrorCodeCatalogTests。

<a id="ai-rule-frontend-002"></a>
### AI-RULE-FRONTEND-002 会话隔离运行状态

超过 1 秒的工具、DataAnalysis 或 Cloud 查询必须显示按会话/消息隔离的运行状态；状态只来自本轮事实，禁止伪造进度、次数、行数或成功。门禁：chat run status tests。

<a id="ai-rule-frontend-003"></a>
### AI-RULE-FRONTEND-003 运行详情安全折叠

运行详情默认折叠，只能从本轮 chunks、metadata 和隔离状态生成安全摘要；不得展开 SQL、连接串、token、endpoint、内部字段、原始结果行或未脱敏错误。门禁：runtime details tests/smoke。

<a id="ai-rule-frontend-004"></a>
### AI-RULE-FRONTEND-004 DeviceLog 回答呈现

固定 DeviceLog 段落可结构化重排，但不得新增指标、补未查询数据或改写结论；可能原因标注 AI 推断，建议只能是人工排查，不得写成已执行控制。门禁：answer sections/AiEval tests。

<a id="ai-rule-frontend-005"></a>
### AI-RULE-FRONTEND-005 产品真实性

前端不得硬编码在线、就绪、命中率、数据源数量或业务结果；内部操作默认不预加载/不暴露，权威状态来自后端聚合/投影。门禁：frontend product truthfulness tests/smoke。

<a id="ai-rule-frontend-006"></a>
### AI-RULE-FRONTEND-006 Trial/Pilot 不得恢复

已物理删除的 Trial、Pilot 和 Production Readiness 运营线不得重新接回普通产品导航、Skill、后台接口或执行入口。

## 8. 安全与模型

<a id="ai-rule-security-001"></a>
### AI-RULE-SECURITY-001 HTTP-only 与内网 OIDC

当前生产保持 HTTP-only；切换 HTTPS 必须由用户单独批准。HTTP OIDC 仅允许显式启用的 loopback、私网 IPv4 或批准内网 DNS 后缀，公共 HTTP issuer 必须拒绝。门禁：runtime/preflight/security tests。

<a id="ai-rule-security-002"></a>
### AI-RULE-SECURITY-002 同源代理与 CORS

Web 标准路径走 nginx 同源 `/api/`；CORS 默认关闭，例外只允许精确 origin，禁止 wildcard、通配子域和带 path/query/fragment 的 origin。门禁：CORS/security tests。

<a id="ai-rule-security-003"></a>
### AI-RULE-SECURITY-003 日志与错误脱敏

SQL、prompt、token、key、endpoint、连接串、原始 provider message、内部字段和原始结果不得进入日志、审计、运行详情或用户错误。门禁：SecurityHardening/ArchitectureTests。

<a id="ai-rule-security-004"></a>
### AI-RULE-SECURITY-004 仓库与示例不得含现场秘密

模板、示例、workflow/script 默认值、seed、复盘和诊断不得含真实内网地址、弱 secret、`CHANGE_ME`、默认 `root@` 或未批准 dummy key；真实值只来自受控配置。门禁：secret/IP negative scan 与 security tests。

<a id="ai-rule-security-005"></a>
### AI-RULE-SECURITY-005 encv2 密钥边界

模型、Embedding 和 endpoint pool API key 必须存 `encv2:` AES-GCM；`encv1:` 仅允许迁移重加密，runtime 不兼容旧格式。迁移必须在改写任何实体前批量预检既有 `encv2:` 可由当前主密钥解密；`AiGatewayDbContext` 与临时 `RagDbContext` 必须复用同一 PostgreSQL transaction，任一 save、自检或 commit 前异常整体 rollback；保存后再次确认非空值全部为可解密 `encv2:`。endpoint pool scheduler 只校验配置项/环境变量值已受保护，不解密、不记录，最终 provider 统一解密。门禁：`SecretStringEncryptorTests`、`MigrationWorkerSecretMigratorTests`、`ModelSecretRuntimeBoundaryTests`、发布 preflight；共享事务当前只有源码结构门禁，真实关系数据库双 Context 故障回滚登记为 `AI-RULE-SECURITY-005-DBTX-GAP`，不得冒充已自动证明。

<a id="ai-rule-security-006"></a>
### AI-RULE-SECURITY-006 私有模型 seed 与 smoke

fresh DB 私有模型默认禁用、空 key、占位 URL 和 64k context；真实值只来自生产环境。模型故障用服务器侧独立 smoke 与应用错误分离；dummy key 仅在显式批准兼容开关下允许。门禁：seed/smoke/preflight tests。

<a id="ai-rule-security-007"></a>
### AI-RULE-SECURITY-007 依赖安全

已知高危依赖默认阻断；具体 CVE 版本修复属于事件证据，不固定成业务规则。门禁：NuGet/npm vulnerability scan。

<a id="ai-rule-security-008"></a>
### AI-RULE-SECURITY-008 私有模型真实值来源

仓库 seed 和示例只能使用禁用状态、空 key 与 `model.internal.example` 占位地址；真实模型 base URL、API key、启用状态和运行参数只能来自服务器真实 `.env` 或非 Git 私密手册。

<a id="ai-rule-security-009"></a>
### AI-RULE-SECURITY-009 模型 smoke 输入真实性

模型 smoke 必须验证实际 OpenAI-compatible base URL、鉴权 header、响应字段和安全错误；dummy key 只能由显式批准的兼容开关启用，不能成为默认或生产证据。

## 9. 部署与运行

<a id="ai-rule-deploy-001"></a>
### AI-RULE-DEPLOY-001 日常唯一入口与增量闭包

日常应用发布唯一对外入口是工作区 `deploy/Deploy-Changed.ps1`；只发布 Git 影响与依赖闭包，无法归属必须停止，禁止退化全量。项目 `Deploy.ps1`/脚本仅作内部执行或显式恢复。门禁：部署 policy tests 与工作区 profile。

<a id="ai-rule-deploy-002"></a>
### AI-RULE-DEPLOY-002 不可变候选与真实运行身份

release/no-op 必须绑定完整 Git SHA、服务闭包、plan/profile/support/image digest、OCI RepoDigest、配置 fingerprint 和真实容器身份；本地未推送内容不得发布。门禁：deployment behavior suite。

<a id="ai-rule-deploy-003"></a>
### AI-RULE-DEPLOY-003 support 安装与 reservation

support sync 使用 staging、SHA256、reservation token 和全局锁，不得同步 `.env`、release state、锁或备份；install 与 release 必须绑定同一 digest/事务，失败恢复原状态。reservation/transition 的 PID 与 process-start 仍证明 owner 存活时，即使 TTL 到期也不得回收；cancel 只能释放调用方自己的 token，旧 token 不得删除新任务锁。门禁：隔离 `deployment-behavior.sh` reservation/adopt/cancel/race 场景；不构成生产证据。

<a id="ai-rule-deploy-004"></a>
### AI-RULE-DEPLOY-004 恢复事务与未知状态

support、infra、runtime、probe 与 release state 作为同一恢复事务验收；SSH ACK 丢失必须按 invocation token 查询并对账，不能新建请求或盲重试。恢复/证据不确定返回 86，invocation active/unknown 返回 87，禁止猜测成功、取消或重试。门禁：隔离 deployment recovery/ACK-loss behavior tests；不构成目标服务器证据。

<a id="ai-rule-deploy-005"></a>
### AI-RULE-DEPLOY-005 non-root Runner 与 root 应急

稳定 Runner 必须 non-root；root 仅允许一次性修复 owner/mode，关闭前须恢复并重验 non-root。所有触达 `environment: production` 或 `secrets.` 的灾备 workflow 必须保持最小权限、`self-hosted + iiot-linux-prod`、非 root/禁 hosted runner，并执行 runner 机器侧 attestation。日常发布不得覆盖服务器执行代码或基础设施。门禁：workflow 静态边界、runner/platform attestation 与 validate-only；仓库门禁不能证明真实 runner/GitHub 设置。

<a id="ai-rule-deploy-006"></a>
### AI-RULE-DEPLOY-006 Harbor 基础镜像

生产构建的 .NET、Node、Nginx、PostgreSQL、RabbitMQ 和 Qdrant 基础镜像必须来自 Harbor mirror，workflow/Dockerfile/script 不得默认直拉 Docker Hub 或 MCR。门禁：SecurityHardeningTests。

<a id="ai-rule-deploy-007"></a>
### AI-RULE-DEPLOY-007 部署测试目录无关

部署行为测试必须从脚本自身位置解析项目根，不得依赖当前终端目录或把非 Git 工作区根误报为部署失败。门禁：deployment behavior test launcher。

<a id="ai-rule-deploy-008"></a>
### AI-RULE-DEPLOY-008 平台验收与受控例外

production secret restriction、required reviewer、least privilege、self-hosted runner、OIDC/Vault 等必须由真实平台证据验收；仓库模板或 attestation 不能伪造成完成。记录 linter 必须拒绝空签署、未勾选项和 `pending` / `unverified` / `unknown` / `N/A` 等弱证明词；未完成只能留带 ticket/change id、owner、due date、current mitigation 的结构化受控例外。门禁：真实执行 platform record linter 只证明字段完整；人工验收：平台截图/设置证据。

<a id="ai-rule-deploy-009"></a>
### AI-RULE-DEPLOY-009 migration 与模型 preflight

后端 runtime 发布必须包含 migration 闭包，启动前完成密钥迁移与可解密验收；模型 provider 可启用服务器侧 smoke preflight。门禁：deploy policy/security tests。

<a id="ai-rule-deploy-010"></a>
### AI-RULE-DEPLOY-010 可写持久路径

容器必须显式挂载可写 `FileStorage:RootPath` 与 `ArtifactWorkspace:RootPath` 到共享持久卷，不得依赖容器层、LocalApplicationData 或 `/app`。门禁：compose/security tests。

<a id="ai-rule-deploy-011"></a>
### AI-RULE-DEPLOY-011 现场拓扑先更新权威资料

部署根、Runner work root、Docker Root Dir、support 目标、用户或同机双部署根事实变化时，必须先更新工作区 profile、部署总览和项目部署文档，再改脚本或发布。门禁：profile/deploy policy tests；人工验收：现场事实对账。

<a id="ai-rule-deploy-012"></a>
### AI-RULE-DEPLOY-012 EXIT trap 恢复失败传播

`EXIT` trap 恢复链禁止 bare `return`，提前成功必须显式 `return 0`；恢复函数在可捕获子 Shell 中执行，内部 `exit` 也统一收口到 blocked evidence 和 terminal state。门禁：native Linux recovery behavior tests。

<a id="ai-rule-deploy-013"></a>
### AI-RULE-DEPLOY-013 候选工作区与并行业务 worktree

调用 `Deploy-Changed.ps1 -Targets AICopilot` 的候选工作区必须是 clean `main` 且目标改动已提交；其它 agent 的 worktree 可以脏，但不得作为候选、被读取或修改，未提交/未推送内容绝不发布。

<a id="ai-rule-deploy-014"></a>
### AI-RULE-DEPLOY-014 Profile 权威与摘要证据

部署目录、账号、Runner、Docker Root、support 范围和默认参数的机器可读权威必须先更新 workspace profile；每次运行必须产出绑定 invocation 的目标摘要和 history，不能依靠历史对话猜现场事实。

<a id="ai-rule-deploy-015"></a>
### AI-RULE-DEPLOY-015 进程树、超时与退出码

部署 supervisor 必须并发排空 stdout/stderr、实时 heartbeat，并在 timeout/INT/TERM 时清理自己创建的完整进程树；只有 watchdog 真实触发返回 124，普通失败保留原退出码，逃逸新会话属于违规。

<a id="ai-rule-deploy-016"></a>
### AI-RULE-DEPLOY-016 OCI/request digest 与迁移备份

远端请求必须绑定固定 Git SHA、服务闭包、OCI RepoDigest 和 request digest；任何 runtime 更新前必须完成 PostgreSQL 备份/checksum，后端闭包必须先 migration，再 `--no-deps` rollout，失败按冻结镜像身份回滚并写 history。

<a id="ai-rule-deploy-017"></a>
### AI-RULE-DEPLOY-017 旧入口与 support 维护隔离

旧 `Invoke-WorkspaceDeploy.ps1`、无候选 `Deploy.ps1 -Deploy`、`local-release.sh` 和手工 SSH/root 命令不得恢复为日常入口；Runner/Compose/support install/cleanup/GC/深度巡检只能在独立维护或绑定旧事务恢复中执行。

<a id="ai-rule-deploy-018"></a>
### AI-RULE-DEPLOY-018 全局漂移与 no-op

no-op/commit 必须同时验证 plan/profile/support/services/image digest、全局配置 fingerprint、真实应用/基础设施镜像身份和常驻容器稳定；全局 fingerprint 漂移时禁止部分发布。

<a id="ai-rule-deploy-019"></a>
### AI-RULE-DEPLOY-019 runtime 环境与 preflight

发布前必须 fail-fast 验证 non-root owner/mode、真实 `.env`、必填 secret、HTTP-only URL、Cloud OIDC 状态端点、模型密文可解密、共享持久路径和 migration 闭包；preflight 不得修改生产状态。服务器发布主链必须在写入 current release manifest 前自动执行发布后 security attestation，覆盖 HTTP-only 安全头、Cloud OIDC 状态、Web 非 root 和模型密钥迁移；失败不得把候选记录为 current。门禁：validate-only 行为、SecurityHardening 源码结构和隔离 deployment behavior；真实 header/container/PostgreSQL 仍须发布窗口验收。

<a id="ai-rule-deploy-020"></a>
### AI-RULE-DEPLOY-020 真实环境证据不可替代

fake、dry-run、validate-only、隔离 behavior 或静态 attestation 只能证明非生产机制；Harbor push、SSH/Runner、backup、migration、rollout、health、cleanup 和 history 未在目标环境执行时，不得宣称生产发布或真实 E2E 完成。

<a id="ai-rule-deploy-021"></a>
### AI-RULE-DEPLOY-021 灾备与 fail-fast

灾备 image/deploy workflow 只能带确认词显式触发且不得成为日常等待链；本地 build、Harbor、SSH、锁和 health 超时必须 fail-fast 并进入对应诊断，禁止无限 watch 或未经对账整轮重发。

## 10. 代码质量

<a id="ai-rule-quality-001"></a>
### AI-RULE-QUALITY-001 LINQ、下推与热路径

简单转换优先 LINQ，`IQueryable` 下推过滤/投影/排序/分页；热路径、状态机、解析器、流式枚举和 Span 紧循环可用 for/foreach。重点阻断重复枚举、先物化再过滤、N+1、O(n²) 和错误数据结构。门禁：CA1851 warning、ArchitectureTests 与代码审查。

## 11. Incident-Only 与 Superseded 记录

<a id="incident-only-and-superseded-records"></a>

`Incident-Only` 只保留具体故障、测试命令、环境依赖或实现过程，不产生当前规范；`Superseded` 必须在台账中指向本索引的替代 Rule ID。任何此类记录都不能反向覆盖 Active 规则。
