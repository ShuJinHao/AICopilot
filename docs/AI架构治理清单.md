# AI 历史治理清单与 Rule ID 索引

本文档保存 AICopilot 历史治理状态和 Rule ID 追溯信息，不是当前任务的默认执行计划。当前长期规则以工作区 `../docs/总规则.md`、`AGENTS.md`、`资料/AICopilot业务规则.md` 和命中的专题契约为准；只在当前改动命中具体 Rule ID、历史回归或未关闭风险时读取对应条目，禁止全文加载。本文中的旧候选、旧测试数量、旧全量验收和已被新契约取代的结论只作历史证据，不得覆盖当前 task mode、动态测试选择或统一业务数据查询契约。

专题契约入口：

- `docs/AICopilot安全部署契约.md`：部署安全、HTTP-only、secret、镜像、SSH、runner 和发布验收。
- `docs/Cloud只读数据分析契约.md`：Cloud 只读、Cloud AiRead、CloudReadOnly Direct DB、Text-to-SQL、DeviceLog 和 Simulation 边界。
- `docs/Agent工作流与异常契约.md`：Agent workflow、Plan/Chat、MCP/Tool/Human-in-the-loop、异常、前端错误和运行详情。

## 0. 执行边界和红线

- 本计划只允许修改 `AICopilot`。`IIoT.CloudPlatform` 和 `IIoT.EdgeClient` 相关问题只能记录为外部依赖或跨项目后续任务，不能在 AI 端任务中顺手修改。
- 当前内网生产部署必须保持 HTTP-only。不得把 HTTPS redirection、HSTS、nginx 443 listener、证书申请/续期或 OIDC HTTPS metadata 强制校验列为当前修复门槛。
- 如果未来要切 HTTPS，必须由用户另行批准传输层方案和证书来源；不能在 AI 安全整改中夹带。
- HTTP-only 不代表放弃安全。当前安全整改必须优先落内网可执行项：端口收敛、同源代理、CORS 白名单、强 secret、短期 token、非 root 容器、敏感信息脱敏、Cloud 只读边界、除 HSTS 外的安全响应头和部署 preflight。
- 需要长期追踪的架构/安全风险可以继续使用本清单的 Rule ID；普通业务修改不要求登记清单。
- 只有形成长期规则、修复历史回归、处理生产事故或改变部署机制时才更新项目滚动复盘；普通业务与测试同批调整不写任务流水。

## 1. 总体优先级

第一轮先堵 AI 端安全红线：

- HTTP-only 部署红线和安全头口径。
- `.env.example`、部署脚本、migration seed 和 preflight 的弱 secret、真实 IP、危险默认值治理。
- Docker 非 root 和 Harbor base image 口径。
- API key AES-CBC 改 AES-GCM。
- 后端 catch-all 脱敏异常契约。
- 前端吞诊断 catch 路径治理。

第二轮修 Cloud 只读和数据链路：

- AICopilot 对 Cloud 业务数据只读边界。
- 生产路径禁止 Simulation fallback。
- Text-to-SQL SQL/prompt/参数/连接串泄露门禁。
- Cloud AiRead production records 唯一路径。

第三轮收部署治理、runner/SSH 和测试门禁：

- root SSH 默认路径下线，专用部署用户和 sudo 白名单进入文档和脚本校验。
- self-hosted runner 短期权限收敛，长期 OIDC/Vault 或等价短期凭据方案进入外部依赖。
- 架构测试、后端测试、前端测试、部署 preflight 和 HTTP 线上探针补齐。

第四轮收文档和历史入口：

- 本清单逐项关单。
- 专题契约和项目规则保持一致。
- 过期计划和旧入口不再作为执行入口。
- 旧 `A助理*.md` 阶段材料、草稿错误码说明和静态权限矩阵已删除；当前入口只保留规则、专题契约、部署指南、前端集成契约和滚动复盘，`SecurityHardeningTests` 锁定这些旧入口不得恢复。

状态口径：

- 表格中的 `Done` 只表示 AICopilot 仓库内源码、脚本、文档和可本地运行的测试/静态门禁已完成。
- `Done` 不表示真实服务器已经发布、线上 HTTP 探针已经执行、容器镜像已经在生产启动、生产数据库已经迁移或 GitHub/服务器基础设施已经改造。
- 需要真实环境的验收必须在第 9 节发版前/发版后门禁中单独执行，并在复盘或发布记录中写明结果。
- 当前整体 AI 架构候选状态固定为 `AI-OVERALL-P0-P7-SOURCE-CANDIDATE / NOT-VERIFIED`：P0–P7 生产源码、前端和测试源码已按总计划接线，但本批按用户边界没有运行 build、test、restore、canonical、coverage、mutation、Playwright 或 demo；现有 inventory、case/coverage/mutation/Web baseline 和历史绿色 artifact 均不能证明当前候选，验证前不得写成 Done、通过退出门或可部署。

当前仓库内修复剩余未完全关单项：

- `AI-SEC-010`：仓库内已固化灾备 workflow 的 least-privilege、self-hosted runner label、非 root runner、hosted-runner 禁用门禁，并让 production/secrets 相关 workflow 执行 `check-runner-security-attestation.sh`；平台侧验收模板和记录 linter 已提供，且 linter 会拒绝模板占位、未勾选项、空签署人和 `pending` / `not implemented` / `N/A` 等弱证明词，并要求记录包含 production environment secret 限制和生产/secret workflow 无 GitHub hosted runner 的证据。GitHub self-hosted runner 机器权限收敛、OIDC/Vault 或等价短期凭据落地仍属于基础设施任务；如果暂未落地，只能在平台记录中写成已批准的基础设施例外，并按结构化字段包含 `Ticket or change id`、`Exception owner`、`Due date` 和 `Current mitigation`，不能伪造成已完成。
- `AI-SEC-012`：旧 `encv1:` 密文迁移代码路径已规划/测试，migration worker 在批量改写前会先预检同一集合中已有 `encv2:` 密文能用当前密钥解密，避免同一集合内混合旧密文和不可读 `encv2:` 时产生内存级部分改写；数据库写入使用 AiGateway 事务，并让临时 `RagDbContext` 通过 `UseTransactionAsync(transaction.GetDbTransaction())` 复用同一事务，避免 `LanguageModel.ApiKey` 与 `EmbeddingModel.ApiKey` 数据库级部分迁移；保存后会自检没有非 `encv2:` 的非空密钥残留，并再次验证 `encv2:` 可解密；发布脚本测试已锁定全量和按需发布中的 `aicopilot-migration -> check_model_secret_migration_preflight -> runtime` 顺序；但真实生产库迁移或管理员重录入仍需要发布窗口验收。

## 1.1 测试架构治理（AI-TEST）

本节仅保存历史测试治理状态，不是当前执行入口。当前只认项目规则与 `Select-AICopilotCiTests.ps1`/`Invoke-AICopilotCiSelectedTests.ps1` 的任务模式；表内固定 runner、case、coverage、mutation 或 baseline 数量均不得作为当前业务测试门禁。

| 编号 | 严重级 | 状态 | 当前结论 | 下一关闭条件 |
| --- | --- | --- | --- | --- |
| `AI-TEST-001` | P0 | Partial | 测试治理机制和 `AI-W0` 历史基线仍由 schema v3 inventory、Release evaluated 项目图、97 个 behavior/29 个 coverage guard、canonical `SourceRevisionId` 与 per-runner binding 证明；`b40ee21...` 的固定历史数量仍是 1026 case（required 1012 / Manual 14）和 Vitest 165。当前 P0 index candidate 保持 20 runner（17 required / 3 Manual）+ 5 support，候选源码与 HEAD/full discovery 基线独立对账得到 1122 case（required 1108 / Manual 14、0 Skip）；活动 Web 基线为 Vitest 168、Playwright 43、deployment behavior 33。旧项目图的 required 诊断重跑已先完成 13 runner / 972 case 全绿并把首红后移到 HttpIntegration；HTTP exact/full 最终达到 4/4、29/29，但随后 `HttpIntegrationTests -> AgentWorkflowTestKit` consumer edge 与 TestKit helper 又发生合法变化，因此该 inventory、TRX 和 binary 只作失败后移证据。当前图仍预计 17 required / 1108 required case，必须重生 clean inventory 后才可确认，故保持 Partial。 | 在本批 clean commit 上重生 `repositoryClean=true` inventory，确认真实 discovery 与候选 1122/1108/14 一致，再完成全部 required TRX/coverage、当前 168/43/33 精确对账、mutation、Web build 与 final reconcile；继续要求 `discovered=executed`、0 failed / 0 skipped，禁止用 dirty discovery、过滤或旧 artifact 关单。 |
| `AI-TEST-002` | P0 | Partial | 历史治理运行保持为证据，但旧 785 条 declaration transition 已退出活动 CI，不再绑定当前测试方法或阻断业务测试演进。当前 P0-only case baseline 为 1122（required 1108 / Manual 14），活动 Vitest 基线为 168；compatibility 候选为 active 5 / ordinary 16 / classified 60 / unclassified 0。当前图的 full 17、coverage 与 final reconcile 尚未从最后修改后的 clean HEAD 执行，因此保持 Partial，不改写旧 run 事实。 | 当前精确 HEAD 必须通过 event-bound quality base、完整 required+coverage、mutation、Web/deployment、同 run artifact fan-in 和 final reconcile；任何失败不得用 rerun、Skip、过滤、旧 1012/165 证据或跨 run artifact 覆盖。 |
| `AI-TEST-003` | P0 | Done | `WorkflowTests` 以真实 `RunBranchSafelyAsync` 与 `ResumeFinalAgentAsync -> RunFinalAgentAsync` 链覆盖 caller cancellation、cleanup 失败不覆盖主异常、compensation exactly-once 和完成 approval context 不清理。Simulation 已物理分为 Pure 12 case 与 Linux Docker/Aspire 1 case，Manual-only workflow 整 runner 执行，Docker preflight fail-fast，无 Skip、`--filter` 或 changed-files 选测；旧混合 runner 及源文件已物理删除。本地完整 acceptance 已精确通过 Pure 12/12、Docker 1/1，均 0 failed / 0 skipped。 | 持续用真实 workflow 和独立 Simulation 验收保持取消、补偿、审批与异常语义。 |
| `AI-TEST-004` | P0 | Done | Contract、Filesystem Contract、真实 PostgreSQL Persistence、Filesystem Persistence、真实 Aspire HTTP、Aspire E2E、Deployment 和 Tool/Plugin Conformance 已物理分层；AI-SEC-051 同时由 Analyzer/Architecture、Unit/Application、Contract、PostgreSQL 和真实 HTTP/auth/tracking 边界覆盖。Aggregate 只保留纯领域语义，application catalog/policy 已迁入 Application，Outbox EF mapping 已迁入 Persistence，本地文件存储已迁入 PersistenceFilesystem，并物理删除 `ApplicationFilesystemTests`。Runner 只引用 5 个中性 TestKit，不再 Link 旧混合 fixture 或源文件；Aspire/Persistence TestKit 不含 xUnit 或 FluentAssertions，生命周期和断言 helper 已下沉到实际 runner。 | 新责任按 concern/runtime 进入对应物理 runner；真实依赖缺失必须 preflight 失败，禁止运行后 Skip，禁止恢复 TestKit 测试框架/断言 package 例外。 |
| `AI-ARCH-001` | P0 | Done | 已建立 `netstandard2.0` 的 `AICopilot.Architecture.Analyzers` 和独立 required `AICopilot.Architecture.AnalyzerTests`，精确依赖 `Microsoft.CodeAnalysis.CSharp 5.6.0`；根 `Directory.Build.targets` 把 Analyzer 接入全部非测试生产项目。`AIARCH001`–`AIARCH007` 以 Error + enabled-by-default + `NotConfigurable` 阻断项目/分层依赖、聚合/repository、DB/SQL owner、enabled Admin 事务不变量、plugin/test-double/runtime 边界、Cloud-readonly call graph 副作用与权限/只读元数据；CompilationEnd 规则同时保留 `CompilationEnd` tag。AnalyzerTests 当前 30/30、真实临时 csproj AnalyzerFixtureTests 28/28，后者证明 pragma、`NoWarn`、`.editorconfig`、`.globalconfig`、`SuppressMessage` 和 generated code 均不能把诊断换绿。`AIARCH004` 在 CompilationEnd 解析 inline/stored/field/property method-group/lambda，分离 transaction synthetic 与真实 call edge，并以 externally reachable 或 source-no-incoming ordinary method 覆盖 protected HostedService、internal seeder 和 internal type public entry；valid/reversed/dual-use member delegate 与 hidden-root 均有精确正反例。`AIARCH002/006` 的 repository、command、Dapper、MCP write identity 只认正式完全限定 symbol，同名 fake 不触发；`AIARCH006` 仍按每个方法自身的真实 Cloud evidence 判 root 后遍历 reachable graph。动态 MCP 目标不再用 token/hostname/alias 启发式冒充可验证非 Cloud；聚合注册、runtime builder（含旧持久化数据）、Plan/能力发现和每次 executor 调用都必须通过 `CloudReadOnly + ReadOnlyQuery + readOnlyDeclared=true` 及既有 verb/hint/schema/risk 门禁；本地非 MCP tool 继续按原 capability/risk/approval 契约执行。 | 持续保持生产 build 0 Analyzer error，AnalyzerTests 与 required TRX 必须 `discovered=executed`、`failed=0`、`skipped=0`；任何规则/例外变更必须先改正式契约并同步真实正/反编译 fixture，禁止 `NoWarn`、warning 降级、config/pragma/attribute 抑制或恢复 Regex 影子门禁。 |
| `AI-EVAL-001` | P0 | Done | `GoldenEvalTests` 的 4 个确定性版本化 case 全部穿过真实 `AgentWorkflowPipeline.RunPlanDraftWorkflowAsync` 和生产 tool 安全发现门禁，分别证明 Cloud 写边界、`SideEffecting`、`Diagnostics` 被隐藏，只有 `CloudReadOnly + ReadOnlyQuery + readOnlyDeclared=true` 暴露。数据集版本为 `agent-workflow-safety-v2`，每条 case 都记录非空变更理由；当前运行 4/4、0 failed、0 skipped。 | 期望数据更新必须经语义审阅；不得退回直接调用 leaf policy/formatter、fixture 文本自证或不经生产编排路径的 case。 |
| `AI-TEST-DUP-001` | P1 | Done | 已对生产 exact/near/structural clone、TestKit helper 和 test assertion flow 建立 schema v4 ratchet；实例身份为 `path+line`，同一文件的重复出现也逐实例计数。动态 MCP 修正中把 Create/Update 重复默认参数物理收口为两个真实命令及其 handler 共用的 mutation contract；新增 `AiToolConfiguredMcpMetadata` 作为聚合、runtime builder 和 policy typed overload 共用的规范安全输入；Analyzer 保留 Roslyn 可追踪的 7 个直接 descriptor 字段，只以 CompilationEnd Rule ID 集合收口 tag 判定；测试拒绝断言 helper 各有两个真实调用方。这些类型和 helper 均承担实际语义，不是无调用方 alias/wrapper。P0 候选已对相对 `29f92786254fa84d5143d6c56bafc903bdf17d3a` 的 220 个 new/grown signature 逐项审计并分类，无未解释项；相对冻结 P0 HEAD，本批收口后无 new/grown signature，31 个 signature 被移除或收紧。当前 no-update 基线为 productionExact `446 / 953 / 7624 / 18069`、productionNear `608 / 1571 / 12568 / 29838`、productionStructural `1200 / 3852 / 30816 / 98378`、testSupportHelpers `107 / 306 / 1836 / 4263`、testAssertionFlows `245 / 885 / 2655 / 3746`，不保留回涨余量；冻结 P0 HEAD 的对应指标为 productionExact `452 / 965 / 7720 / 18343`、productionNear `615 / 1590 / 12720 / 30280`、productionStructural `1217 / 3933 / 31464 / 100110`、testSupportHelpers `107 / 306 / 1836 / 4263`、testAssertionFlows `245 / 885 / 2655 / 3746`。门禁同时锁定汇总与每个 signature；同文件增长负例已进入当前 97/97 infrastructure behavior。 | 后续只能收紧基线；更新 schema/baseline 前必须先有真实重复实现减少和指标下降，不得用汇总持平、signature swap/expand 或单纯重生基线换绿。 |
| `AI-TEST-UI-001` | P1 | Partial | 历史治理 HEAD `59b93f...` / `AI-W0` 对 28 文件、Vitest 165、Playwright 43、deployment 33 的绿色证据保持不变。当前 P0 candidate intentional 新增 `agentPlanV2Preview.spec.ts` 两项和 `chunkReducer.spec.ts` 一项，活动 CI/reconcile 基线同步为 29 files / Vitest 168；P1 final-review/frontend hunks不计入该三项增量。clean committed HEAD 的完整 Vitest、Web/deployment lane、Playwright 43、Web build 与 final fan-in 尚未执行，因此暂退 Partial。 | 在当前 clean HEAD 精确通过 Vitest 168、Playwright 43、deployment 33、Web build、0 failed / 0 skipped / 0 flaky，并由 final reconcile 消费同 run evidence 后恢复 Done；Viewport 仍按项目 tag 物理发现。 |

## 1.2 后续 AI 架构升级（AI-W）

| 编号 | 严重级 | 状态 | 当前结论 | 下一关闭条件 |
| --- | --- | --- | --- | --- |
| `AI-W0` | P0 | Done | B0 唯一实施基线为 clean commit `b40ee21b9bc248176e6d7e0c278e4c50101b1d59`、tree `6331fc362da7d30d05b81e733fc2a9c020f5d0a4`，同时包含最终测试治理候选 `59b93f...` 与当时 `main` `29f927...`。schema v3 inventory 为 25 项 / 20 runner / 17 required / 3 Manual / 5 support / 1026 case；本地 required .NET 1012、Vitest 165、Playwright 43、deployment 33、coverage 与 mutation 已按 clean HEAD 对账，PR #60 run `29562903935` attempt 1 五个 job 全绿。 | P0 只能在该锚点后代实施；若 ancestry、项目图、required workflow、Analyzer owner 或质量 baseline 改变，先重新通过 B0。 |
| `P0-010` | P0 | SourceCandidate / NOT-VERIFIED | 已形成唯一 `AgentPlanCanonicalizer`、版本化 `AgentIntentRegistry` 和唯一 `DeterministicAgentPlanCompiler`；Plan v2 / Evidence v1 / ExecutionSnapshot、完整选择模式、capability gap、`LinearV1`/显式受限 `DagV1`、多 `cloudReadonlyIntents` 与 Cloud/PLC/Prediction fail-closed 已进入生产源码。Skill、DynamicPlanner、旧 adapter/catalog 与影子 compiler 已物理退出候选树。 | 在用户单独批准验证后，从当前精确 clean 候选执行 build、Architecture/Unit/Application/Contract/Workflow/Persistence/HTTP/Golden 全矩阵与 canonical inventory；验证前不把源码存在写成退出门通过。 |
| `P0-020` | P0–P3 | SourceCandidate / NOT-VERIFIED | PlanDraft→确认→AgentTask、Task/Node 两级 claim/lease/fencing、预算预约、Node checkpoint、sealed Evidence、恢复、取消/重试、OutcomeUnknown 和 Artifact file-set stage/journal/manifest/checkpoint/reconciliation 已接入同一 durable 执行平面；Cloud 正式读取与 governed exploration 保持互斥且无失败 fallback。 | 单独批准后执行真实 PostgreSQL 多 Worker、kill/recovery、stale fence、quota、ACK unknown、真实文件系统+PostgreSQL file-set 故障矩阵，并证明恢复前后 digest/文件 hash 不漂移。 |
| `P0-030` | P4–P7 | SourceCandidate / NOT-VERIFIED | 同一 compiler 已支持有限 DAG、独立并行 state、required/optional 合流与局部重试；深度 1 `AgentReasoningNode` 只消费 EvidenceOnly/SafeSummary；设备状态健康评估为版本化 deterministic `DerivedFact`。对话 UI 已接入计划确认、真实运行/证据/错误/取消、结果/产物卡；Markdown/HTML/PDF/PPTX/XLSX/图表和显式 Chat 追问绑定同一 `EvidenceSetDigest`。 | 单独批准后执行当前真实 discovery、required/coverage/mutation/Web/deployment/final reconcile 与 §21 原型故障矩阵；验证 Chat 同范围复用、改范围重查和所有跨格式关键事实一致，再决定是否交付/合并。 |

### 1.2.1 `P0-020` 测试守护矩阵

以下矩阵保留为 P0–P7 当前源码候选的验证责任清单。实现源码存在不等于这些证据已通过；不得用已退役 runner、字符串扫描、旧 inventory/TRX/binary 或非 fresh context 自证。

| 权威字段 / 链路 | 现有证据 owner | 当前缺口 | P0-020 补测与 required runner |
| --- | --- | --- | --- |
| Plan v2 canonical 属性顺序、数组语义顺序、`planDigest` 自引用排除、Unicode/UTF-8 字节数 | `AgentPlanContractV2Tests` | 需把 typed Node/Tool input 的 8,000 字节策略与 Tool Guard 共用，并证明中文与 `±1 byte` | `AICopilot.UnitTests`；Tool Guard 单元测试与 Plan 契约边界测试 |
| `AgentTask.PlanJson` 完整保存；`AgentStep.InputJson/OutputJson` 不 substring；失败时领域对象无部分变更 | `AgentTask` / `AgentStep` 既有 Aggregate 测试 | 无超旧 32,000/16,000 的正例、8,001 UTF-8 input 反例和原子失败断言 | `AICopilot.AggregateTests` |
| Plan v2 写入后 fresh `DbContext` 重读、反序列化、canonicalize、digest 复算；业务保存与校验同一事务结果 | 现有 repository commit engine 与 Persistence TestKit | 尚无 AgentTask Plan 真实 PostgreSQL fresh-context 成功/陈旧 digest/校验失败回滚证据 | `AICopilot.PersistenceTests`；使用真实 PostgreSQL，禁止 InMemory provider |
| 历史 completed v1 只读；非终态 v1、无效 JSON、截断嫌疑、缺/陈旧 digest 不得确认或执行 | 既有 Plan 确认和 runtime Application 测试 | 历史投影与新 runtime 门需要成对正反例 | `AICopilot.ApplicationTests` + `AICopilot.ContractTests` |
| 262,144-byte Plan 在 DTO/REST/SSE/`ChunkType.AgentTask` 中完整传输，且 digest 一致 | `FrontendContractSnapshotTests`、`chunkReducer.spec.ts` | 正好边界的后端 HTTP/SSE 与前端 preview 完整 round-trip 尚未闭环 | `AICopilot.ContractTests` + `AICopilot.HttpIntegrationTests` + Vitest |
| 超限 Plan 返回 `plan_payload_too_large`，ProblemDetails/SSE/前端错误栏不泛化 | `AppProblemCodes`、`chatErrorStore`、前端错误契约 | 缺少从生产 Plan 入口到用户可见错误的全链路反例 | `AICopilot.HttpIntegrationTests` + `AICopilot.ContractTests` + Vitest；同批对账错误码 catalog |
| `SkillDefinition` / `IAgentDynamicPlanner` 的生产 DI、持久化、API、解析和调用为零，不存在 alias/wrapper/shadow compiler | `PostFixClosureArchitectureTests` | 需由生产 DI 图、数据库 snapshot 和实际 coordinator 构造同时证明；步骤/节点只允许唯一 `AgentPlanCompiler` 生成 | `AICopilot.ArchitectureTests` + `AICopilot.ApplicationTests` + `AICopilot.PersistenceTests` |
| Cloud 写、PLC 与 `PredictionReadNode` 在 Intent/契约/runtime 三层拒绝；`DagV1` 只接受显式 profile、固定节点和受限合流 | `AgentPlanContractV2Tests`、Golden/Workflow 安全测试 | 需从当前候选重新证明 Cloud/Prediction 无 fallback、Linear 不被静默升级、DAG cycle/越界并发稳定拒绝 | `AICopilot.UnitTests` + `AICopilot.WorkflowTests` + `AICopilot.GoldenEvalTests` |
| `AI-PERSIST-01d / AI-SEC-047` file-set journal、manifest、marker、fencing、rollback/reconciliation 支持 ArtifactReference | `AgentArtifactFileSetCheckpointGate`、`ArtifactFileSetOutcomeAuthorityProbe`、file-set stores | 源码候选已接入，尚未运行真实文件系统 + PostgreSQL create/overwrite/archive/finalize/ACK-lost/kill/recovery 矩阵 | `AICopilot.PersistenceFilesystemTests` + `AICopilot.PersistenceTests` + `AICopilot.ApplicationTests` + `AICopilot.ArchitectureTests` |

矩阵的正式关单证据必须来自当前候选 clean HEAD 发现的 required runner；定向测试只用于实施期诊断，不替代 P0-030 完整对账。

当前发版/外部环境待验收项：

- `AI-SEC-002`：`deploy-release.sh` 发布时自动运行 `./scripts/check-release-security-attestation.sh`；真实内网 Web 入口也可用 `curl -I http://<intranet-host>:82` 复验，确认 HTTP 兼容安全头存在且无 `Strict-Transport-Security`，并确认 `/api/identity/cloud-oidc/status` 返回 200。
- `AI-SEC-008`：生产镜像构建后由 `deploy-release.sh` 自动运行 `./scripts/check-release-security-attestation.sh`，确认 `aicopilot-webui` 非 root 且 nginx 运行目录可写。
- `AI-SEC-010`：灾备 workflow 和 runner 机器侧运行 `./scripts/check-runner-security-attestation.sh` 验收非 root、工作目录、Docker Root Dir 和部署目录；平台 owner 复制填写 `runner-platform-attestation.template.md` 并用 `./scripts/check-platform-attestation-record.sh --record <filled-attestation.md>` 校验记录完整性；GitHub environment secret 权限和 OIDC/Vault/短期凭据方案仍需平台侧单独留痕。
- `AI-SEC-012`：发布窗口运行 migration；后端 runtime 按需发布必须包含 `migration`，由 migration worker 重新迁移并验证 `encv2:` 密文可用当前密钥解密；`deploy-release.sh` 在启动 runtime 服务前执行 `./scripts/check-model-secret-migration.sh` 模型密钥迁移 preflight，并在发布后自动运行 `./scripts/check-release-security-attestation.sh` 复验；必要时手工运行同一脚本证明 `aigateway.language_models` 与 `rag.embedding_models` 非空 API key 全部为 `encv2:`，且 `MigrationWorker__CheckSecretsOnly=true` 只读解密检查能用当前 `AICOPILOT_API_KEY_ENCRYPTION_KEY` 解开。
- 第 9 节所有发版前和线上发布后命令。

## 2. 执行前审计

执行任何代码修复前，先只读完成以下审计，输出到复盘：

```bash
rg -n "USER root|CipherMode.CBC|AesGcm|encv1:|SecretStringEncryptor|CHANGE_ME|dummy-key|(10\\.[0-9]{1,3}\\.|172\\.(1[6-9]|2[0-9]|3[01])\\.|192\\.168\\.)|Strict-Transport-Security|UseHttpsRedirection|listen 443|ssl_certificate|root@|DEPLOY_ENV_FILE" deploy src docs .github
rg -n "catch\\s*\\{|catch\\s*\\([^)]*\\)|UseCaseExceptionHandler|ProblemDetails|chatErrorStore|toFriendlyMessage|console\\.error|toast" src/hosts src/services src/vues/AICopilot.Web
rg -n "Simulation|CloudAiRead|CloudReadOnly|production-records|TextToSql|sourceName|connection string|prompt|SQL 原文" src AGENTS.md 资料 docs
git status --short
git log --oneline -n 20 -- AICopilot.slnx deploy src docs AGENTS.md 资料
```

审计结论必须明确：

- 哪些命中是必须修复的问题。
- 哪些命中是历史记录、测试样例或明确允许的 HTTP 内网口径。
- 是否存在用户未提交改动；如存在，只能追加本任务需要的改动，不得回退。

## 2.1 源码归属和执行面

后续执行必须按源码归属拆单，不允许只凭印象补丁：

- 部署和镜像：`deploy/enterprise-ai`、`.github/workflows`、`src/vues/AICopilot.Web/Dockerfile`、`src/vues/AICopilot.Web/nginx.conf.template`。
- HttpApi 和错误契约：`src/hosts/AICopilot.HttpApi`、`src/shared/AICopilot.SharedKernel/Result`、`docs/frontend-integration-contract-package-2026-05-17.md`。
- 模型密钥和运行时：`src/infrastructure/AICopilot.EntityFrameworkCore/Security`、`src/infrastructure/AICopilot.AiRuntime`、`src/infrastructure/AICopilot.Embedding`。
- Cloud 只读和 AiRead：`src/infrastructure/AICopilot.Infrastructure/CloudRead`、`src/services/AICopilot.AiGatewayService/BusinessSemantics`、`src/services/AICopilot.AiGatewayService/Workflows`。
- DataAnalysis / Text-to-SQL：`src/services/AICopilot.DataAnalysisService`、`src/infrastructure/AICopilot.Dapper`、`src/services/AICopilot.Services.CrossCutting/Sql`。
- Agent workflow / MCP / Tool / Approval：`src/services/AICopilot.AiGatewayService/AgentTasks`、`src/services/AICopilot.AiGatewayService/Tools`、`src/services/AICopilot.McpService`。
- 前端错误和运行详情：`src/vues/AICopilot.Web/src/services`、`src/vues/AICopilot.Web/src/stores`、`src/vues/AICopilot.Web/src/protocol`、`src/vues/AICopilot.Web/src/views`。
- 架构和回归测试：`src/tests` 下由 `Select-AICopilotCiTests.ps1` 按源码依赖与 owner 选中的当前 runner、`Invoke-AICopilotCiSelectedTests.ps1` 生成的本次动态发现证据、`src/testing` TestKit support 项目、Analyzer/AnalyzerTests，以及按当前前端改动选中的 `src/vues/AICopilot.Web/tests`。

每个批次必须先定位到以上目录和对应测试，再改代码或文档。找不到源码证据的项只能标为外部依赖、待审计或不纳入，不能写成 Done。

## 3. 第一批：HTTP-only 部署安全红线

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-001 | CRITICAL | Done | 规则/部署文档 | 固化 HTTP-only 红线，明确 HTTPS/HSTS/443/certificate 不纳入当前修复 | `rg -n "HTTP-only|不得把 HTTPS|证书来源" AGENTS.md 资料 docs deploy` |
| AI-SEC-002 | CRITICAL | Done | Web nginx | 增加 HTTP 兼容安全头；不得加 HSTS | `curl -I http://<intranet-host>:82` 不含 `Strict-Transport-Security` |
| AI-SEC-003 | HIGH | Done | CORS/同源代理 | Web 到 API 走 nginx 同源 `/api/` 反代；HttpApi CORS 默认不开放跨源，确需直连时只允许精确 origin，不开放 `*` | `SecurityHardeningTests` 和 nginx/HttpApi 配置检索 |
| AI-SEC-004 | HIGH | Done | `.env.example` | 示例文件移除真实 IP、弱占位 secret 和 dummy key；HTTP URL 使用 `internal.example` 模板占位 | `rg -n "(10\\.[0-9]{1,3}\\.|172\\.(1[6-9]|2[0-9]|3[01])\\.|192\\.168\\.|CHANGE_ME|dummy-key)" deploy/enterprise-ai/.env.example` 无命中 |
| AI-SEC-005 | HIGH | Done | deploy preflight | 发布前校验 HTTP-only URL、Cloud OIDC issuer 仅限 loopback/私网 IPv4/保留内网 DNS 后缀、HTTP 兼容安全头、禁止 HSTS、必填 secret、弱占位、危险默认值和 `.env` 文件权限 | `bash -n deploy/enterprise-ai/*.sh deploy/enterprise-ai/scripts/*.sh` |

实施要点：

- `src/vues/AICopilot.Web/nginx.conf.template` 增加：
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: DENY` 或以 CSP `frame-ancestors 'none'` 表达
  - `Content-Security-Policy`，至少限制 `default-src 'self'`
  - `Referrer-Policy: no-referrer`
  - `Permissions-Policy` 禁用不需要的浏览器能力
- 不增加：
  - `Strict-Transport-Security`
  - `return 301 https://...`
  - `listen 443 ssl`
  - `ssl_certificate`
- `deploy-release.sh` 的 HTTP 探针继续使用 HTTP，但要检查安全头、OIDC 状态接口、Web 首页和 API 反代。
- Cloud OIDC HTTP issuer 必须显式开启内网 HTTP OIDC，只允许 loopback、私网 IPv4 或保留内网 DNS 后缀（`.internal.example`、`.internal`、`.lan`、`.local`）；公共 HTTP 域名即使开启内网 HTTP 开关也必须拒绝。

第一批完成定义：

- HTTP-only 红线在规则、业务规则、部署指南、部署 README 和本清单中一致。
- `docs/AICopilot安全部署契约.md` 与脚本、nginx 模板和 `SecurityHardeningTests` 口径一致。
- Web 入口 HTTP 可用，安全头可见，且无 HSTS。
- 部署脚本 dry-run/config 通过。
- 复盘写明“未引入 HTTPS/TLS/HSTS/证书流程”。

## 4. 第二批：部署 secret、镜像和 SSH/runner 治理

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-006 | CRITICAL | Done | `.env.example` / deploy | 弱 secret、空 token、默认 dummy key、真实内网 IP、HTTP-only OIDC 配置和公共 HTTP OIDC 域名进入 fail-fast 校验 | `deploy-release.sh` 加载真实 `.env` 后先执行 `validate_deploy_environment` |
| AI-SEC-028 | HIGH | Done | fresh DB seed | 新库默认模型只使用 `model.internal.example` 占位 URL、无默认 API key、默认禁用；私有 MiniMax seed 支持 `AICOPILOT_PRIVATE_MODEL_*` 生产 `.env` 注入，context window 固定 64k（`65536`），API key 入库前加密为 `encv2:`；已有同名模型只做 key 格式迁移，不强行覆盖现场 URL、参数或启用状态 | `FreshDatabaseSeedTests` + `SecurityHardeningTests.DeploymentConfig_ShouldNotCarryKnownWeakSecrets` |
| AI-SEC-007 | HIGH | Done | Dockerfile | 去掉 AICopilot Dockerfile 中 `USER root` 安装步骤，运行镜像保持非 root | `rg -n "USER root" deploy src/vues/AICopilot.Web` |
| AI-SEC-008 | HIGH | Done | Web Dockerfile | Web nginx 容器非 root 运行，运行目录和 pid/cache 目录可写 | 镜像构建和容器启动 smoke |
| AI-SEC-009 | HIGH | Done | SSH 发布 | `local-release.sh` 示例改专用部署用户；root SSH 只允许 `ALLOW_ROOT_SSH_DEPLOY=true` 显式应急，不作为默认路径 | `rg -n "ALLOW_ROOT_SSH_DEPLOY|root SSH" deploy docs` |
| AI-SEC-010 | HIGH | Partial | GitHub runner | 灾备 workflow 保留 `contents: read`、`self-hosted + iiot-linux-prod`、非 root runner 校验、`check-runner-security-attestation.sh`、hosted runner 禁用和真实 Cloud URL 默认值移除；提供平台侧验收模板和记录 linter，linter 拒绝模板占位、未勾选项、空签署人、弱证明词、缺少 production environment secret 限制证据、缺少生产/secret workflow 无 GitHub hosted runner 证据，以及缺少 ticket/change id、exception owner、due date、current mitigation 的批准例外；runner 机器权限收敛和 OIDC/Vault 仍是外部基础设施任务，暂未落地时只能记录为已批准的基础设施例外 | `SecurityHardeningTests.ProductionWorkflows_ShouldKeepLeastPrivilegeSelfHostedRunnerBoundary` + `SecurityHardeningTests.PlatformAttestationRecordCheck_ShouldRejectIncompleteSignOffsAndWeakEvidenceWords` + workflow 检索 + `check-platform-attestation-record.sh` |

实施要点：

- 后端依赖包不要在应用 Dockerfile 中临时 `apt-get`，应固化到 Harbor runtime base image。
- fresh DB seed 只能种不可直接访问生产资源的占位模型；默认模型无 API key 且禁用，管理员必须在 UI 或受控命令中录入真实 URL/key 并启用。
- migration seed 不得覆盖已有同名模型的现场 URL、参数和启用状态，只允许把旧 `encv1:` 或明文 key 迁移为 `encv2:`。
- 模型 smoke 的 `dummy-key` 只能作为真实环境显式例外使用；必须设置 `AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY=true` 或手工传 `--allow-dummy-key`，默认 preflight 必须拒绝。
- `DEPLOY_ENV_FILE` 仍可作为灾备 workflow secret，但标准路径不依赖 GitHub workflow 日常发布。
- root SSH 改造分两步：
  - 文档和示例先改为 `<deploy-user>@<host>`。
  - 后续服务器侧建立 sudo 白名单，只允许进入部署目录执行固定脚本。
- runner/Vault/OIDC 属于平台治理；AI 端只记录约束、灾备 workflow 短期校验和文档边界，不能自己伪造已完成。
- production/secrets 相关灾备 workflow 必须执行 `scripts/check-runner-security-attestation.sh`；该脚本只能证明 runner 机器本地事实，GitHub production environment secret 权限、OIDC/Vault 或等价短期凭据必须由平台侧单独填写 `runner-platform-attestation.template.md` 并留痕。`check-platform-attestation-record.sh` 会拒绝模板占位、未勾选项、空签署人和弱证明词，并要求记录包含 production environment secret 限制、`contents: read`、`self-hosted + iiot-linux-prod`、生产/secret workflow 无 GitHub hosted runner 的证据；OIDC/Vault 尚未落地时，只允许填写已批准的基础设施例外，并按 `Ticket or change id`、`Exception owner`、`Due date`、`Current mitigation` 四个字段写清例外证据。该脚本只校验事实记录完整性，不能替代真实平台验收。

第二批完成定义：

- 应用/前端容器默认非 root。
- 标准发布文档不再鼓励 root SSH。
- 弱 secret 和危险默认值有可执行 preflight。
- GitHub 灾备 workflow 不打印生产 secret，且 `contents: read`、内网 self-hosted label、非 root runner、禁用 hosted runner 和禁用宽权限由 `SecurityHardeningTests` 固化。

## 5. 第三批：API key AES-GCM 和密钥迁移

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-011 | CRITICAL | Done | `SecretStringEncryptor` | AES-CBC 改 AES-GCM，保存并校验 authentication tag，新密文 `encv2:` | `SecretStringEncryptorTests` |
| AI-SEC-012 | HIGH | Partial | 旧密文处理 | Migration worker 在 seed 前全量扫描 `LanguageModel.ApiKey` 与 `EmbeddingModel.ApiKey`，批量改写前先预检同一集合已有 `encv2:` 可用当前密钥解密，再将 `encv1:` 和历史明文重加密为 `encv2:`；数据库写入在 AiGateway 事务内创建临时 `RagDbContext` 并复用同一事务，`LanguageModel` 与 `EmbeddingModel` 保存后统一 commit，任一保存/自检失败则 rollback；保存后自检没有非 `encv2:` 的非空密钥残留并再次验证 `encv2:` 密文可解密；后端 runtime 按需发布必须包含 `migration`；`deploy-release.sh` 在 runtime 启动前调用 `check-model-secret-migration.sh` fail-fast 验迁移结果，脚本先做 SQL 前缀统计，再用 `MigrationWorker__CheckSecretsOnly=true` 验证当前主密钥可解密所有 `encv2:`；运行时 `Decrypt` 拒绝旧格式；仍需发布窗口跑真实库迁移验收 | `MigrationWorkerSecretMigratorTests` + `SecretStringEncryptorTests` + `SecurityHardeningTests` + `check-model-secret-migration.sh` + 发布窗口验收 |
| AI-SEC-013 | HIGH | Done | runtime provider | OpenAI/Anthropic/Embedding provider 只接受受保护密文；endpoint pool 覆盖密钥和环境变量也必须是 `encv2:`，不接受明文；测试同时覆盖配置项明文和环境变量明文失败 | `ModelSecretRuntimeBoundaryTests` |
| AI-SEC-014 | HIGH | Done | 审计/日志 | API key 写入、测试连接、DTO、审计摘要和连接测试错误不得输出明文或密文 | `ModelApiKeyProtectionTests` / `RagMcpAuditCommandTests` / `ModelSecretContractTests` |

实施要点：

- `encv2:` payload 建议格式：version + nonce + ciphertext + tag。
- `AICopilotSecurity__ApiKeyEncryptionKey` 仍作为主密钥输入，但需要长度/非弱值 preflight。
- 旧 `encv1:` 和历史明文的处理不能无限兼容：
  - 标准方案：发布窗口先运行 `aicopilot-migration`，由 `MigrationWorkerSecretMigrator` 扫描 `aigateway.language_models` 与 `rag.embedding_models`，重写为 `encv2:`。
  - 备选方案：如果迁移失败，管理员在 UI 或受控命令中重新录入 API key；服务不得静默使用 CBC 或明文。
- 不允许把旧 CBC 兼容藏在 `Decrypt` 里长期保留。

第三批完成定义：

- 代码中无 `CipherMode.CBC`。
- 新写入密钥均为 `encv2:`。
- 旧格式处理路径有明确退出条件：migration worker 负责一次性重加密；批量改写前先对同一集合已有 `encv2:` 做当前密钥可解密预检，避免不可读密文导致同一集合内迁移半途失败后留下内存级部分改写；数据库写入由 AiGateway 事务拥有，并让临时 `RagDbContext` 通过 `UseTransactionAsync(transaction.GetDbTransaction())` 复用同一事务，`LanguageModel` 与 `EmbeddingModel` 保存后统一 commit，任一异常 rollback，避免数据库级部分迁移；保存后仍会整体自检并拒绝伪 `encv2:` 或当前密钥不可解密的旧密文；后端 runtime 按需发布必须包含 `migration`；`deploy-release.sh` 在 runtime 启动前调用 `check-model-secret-migration.sh` fail-fast 验证迁移结果，脚本既做 SQL 前缀统计，也启动 `aicopilot-migration` 的 `MigrationWorker:CheckSecretsOnly` 只读模式验证当前主密钥可解密所有 `encv2:`；`SecurityHardeningTests` 锁定全量发布和按需发布两条分支的 migration 容器、secret preflight、runtime 启动顺序和共享事务结构，runtime provider 仍拒绝 `encv1:` 和明文。
- 后端和集成测试覆盖模型 API key、embedding key、连接测试和运行时读取。
- 真实生产库发布窗口运行 `./scripts/check-model-secret-migration.sh`，证明 `aigateway.language_models.api_key` 和 `rag.embedding_models.api_key` 中非空值均为 `encv2:`，且当前主密钥能解密这些 `encv2:` 密文。

## 6. 第四批：异常契约和前端错误展示

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-015 | HIGH | Done | `UseCaseExceptionHandler` | 增加未处理异常 catch-all，返回稳定 ProblemDetails 和 correlationId | `ChatErrorContractTests` |
| AI-SEC-016 | HIGH | Done | 后端日志/错误边界 | catch-all、Dapper SQL 执行链、DataAnalysis tool facade、模型 provider fallback、Agent workflow fallback、run queue worker、CloudAiRead transport、RAG indexing、MCP runtime、outbox、model connectivity tester、JSON/parser 用户错误和工具执行失败摘要均只返回/记录安全摘要、traceId、异常类型、SQL length/hash、failure code、reason code 或固定业务错误，不记录 raw exception message、SQL、prompt、token、endpoint、连接串或密码；少量 `ex.Message` 仅允许作为内部分类器输入，不能进入日志、接口响应或持久化错误文本 | `SecurityHardeningTests` + `TextToSqlReadOnlyTests` + workflow/queue/semantic/RAG/MCP/CloudRead 定向测试 + 生产源码日志静态门禁 |
| AI-SEC-017 | HIGH | Done | 前端 catch | 公共 API、stream、chunk、路由、OIDC、agent store、artifact、config、auth、RAG/access action 失败均有诊断日志、安全 fallback 或用户可见错误；裸 `catch {}` 已加前端单测门禁 | `chatErrorStore` / `chunkReducer` / `apiClient` / `agentStateStores` / `configViewUsability` / `authStore` / `frontendErrorHandling` 单测 |
| AI-SEC-018 | MEDIUM | Done | runtime details | 运行详情继续默认折叠，只展示安全摘要，不展示原始工具参数、状态错误原文和内部字段 | `runtimeDetails.spec.ts` + smoke |

实施要点：

- `ApiProblemDetailsFactory` 补齐 `code`、`detail`、`userFacingMessage`、`correlationId` 的统一结构。
- 未处理异常对用户显示稳定文案，例如“服务处理失败，请联系管理员并提供追踪编号”。
- 前端 catch 不要求所有地方都 `console.error`，但必须满足：
  - 用户操作失败有可见反馈。
  - 后端 `code/detail/userFacingMessage` 优先展示。
  - 解析失败有安全兜底，不丢会话状态。
  - 不把 SQL、token、连接串、endpoint 原文直接展示。

第四批完成定义：

- SSE、普通 API、AgentEvent、validation error 都走统一错误解析。
- `docs/Agent工作流与异常契约.md` 与后端错误、前端错误和运行详情测试口径一致。
- 没有“只 catch 然后无状态变化”的用户操作路径。
- 错误契约测试覆盖后端 ProblemDetails 和前端解析。
- AI-SEC-016 阶段证据：`UseCaseExceptionHandler` catch-all 不再把原始 exception 交给 logger；`DapperDatabaseConnector` guard reject 和 execution failure 只记录 `SqlLength`、`SqlSha256`、`ReasonCode`、`ErrorType` 与 `OriginalMessage=hidden_by_security_policy`；`DataAnalysisPlugin` 表结构/表名/SQL 执行失败不再 `logger.LogError(ex, ...)` 或返回 raw `ex.Message`；`DataAnalysisToolResultFormatter` 仅透出已知安全配置错误，SQL 拒绝原因映射成固定安全文案；`TextToSqlReadOnlyTests` 验证工具结果和日志不含 raw SQL、token、Host、Password；`SecurityHardeningTests` 固化禁止回退到 raw reason/ex.Message 日志。
- AI-SEC-016 provider/workflow/worker 阶段证据：`AgentRuntimeFactory` provider fallback 和 endpoint pool fallback 不再 `LogWarning/LogDebug(ex, ...)`；`AgentTaskRunQueueWorker` iteration、queue item failure 和 heartbeat failure 不再记录原始 exception，worker-level failure message 固定为安全文案；`AgentWorkflowPipeline` branch fallback、`IntentRoutingExecutor`、`KnowledgeRetrievalExecutor`、`ToolsPackExecutor`、`DataAnalysisWidgetEmitter` 和 `FreeFormDbaAnalysisRunner` 的 fallback/error 日志只记录 `ErrorType` / `FailureCode` 与 `OriginalMessage=hidden_by_security_policy`；`SemanticAnalysisRunnerTests` 通过六类 Cloud 错误行为矩阵验证 provider detail 不进入结果，`ProductionLogs_ShouldNotAttachRawExceptionObjects` 继续覆盖其 logger 调用；Cloud AiRead `MissingRequiredParameter` 用户文案不再拼接 `ex.Message`。过期的 Runner `FailureCode` 源码字符串断言已随 Direct DB 分支删除，不以同义字符串门禁替代。
- AI-SEC-016 关单证据：`CloudAiReadHttpTransport`、`DocumentIndexingService`、`UploadDocument`、`McpServerBootstrap`、`McpRuntimeRegistrySynchronizer`、`McpServerManager`、`OutboxDispatcher`、`LanguageModelConnectivityTester`、`PostgreSqlSessionExecutionLock`、`TelemetryBehaviors`、`SqlAllowlistColumnInspector`、`SemanticQueryPlanner`、`ToolInputSchemaValidator`、`AstSqlGuardrail` 和 `AgentTaskRuntime` 不再把 raw exception 对象或 exception message 直接写入日志、接口响应、任务失败摘要或持久化失败原因；`SecurityHardeningTests.ProductionLogs_ShouldNotAttachRawExceptionObjects` 全量扫描 `src/hosts`、`src/infrastructure`、`src/services`、`src/core`、`src/shared` 禁止 `LogCritical/LogError/LogWarning/LogInformation/LogDebug/LogTrace` 以变量作为首参，从而覆盖 `ex`、`exception`、`e`、`cleanupException` 等异常变量名；`ErrorBoundaryMessages_ShouldNotReturnOrPersistRawExceptionMessages` 点名锁定已修错误边界。剩余 `ex.Message` 仅作为 `DataAnalysisToolResultFormatter`、`CloudReadOnlyTextToSqlRepairClassifier` 和 artifact finalized 识别的内部分类输入，输出仍是固定安全文案或 hash/code。
- AI-SEC-018 关单证据：`MessageRuntimeDetailsPanel` 使用无 `open` 的原生 `<details>`，smoke 验收断言“结构化展示”默认隐藏，点击 summary 后才可见；`runtimeDetails.ts` 对工具参数、工具结果、Widget 摘要、运行状态 summary/error 只输出安全摘要；`runtimeDetails.spec.ts` 覆盖 SQL、连接串、password、token、endpoint、sourceName、tableName、databaseName、内部路径和原始结果行不进入运行详情对象。
- AI-SEC-017 关单证据：`chatErrorStore` 统一解析 ProblemDetails `userFacingMessage` / validation errors / `detail` / `title`、已知 code 和安全兜底，SSE open/error 与普通 API 不再丢后端安全诊断；未知 Chat Error code 仍不直接展示 raw `detail`；AgentEvent、ApprovalRequest、AgentTask 和 Chat Error chunk 失败进入会话错误栏；agent catalog、task timeline、artifact preview/download/upload 和 ConfigView agent settings 刷新失败进入当前会话或页面错误栏；auth current-user 和 Cloud OIDC status 失败进入登录错误栏；RAG、Config CRUD、Access action 失败进入页面 `errorMessage`、`actionErrors` 或对应 dialog error；纯解析 fallback（metadata/widget/runtime details/chat run status）只降级展示或记录安全摘要，不是用户操作失败路径；chunk reload guard 的 storage catch 只保护 stale bundle 自动刷新；`chatErrorStore.spec.ts` 覆盖 ProblemDetails `detail/title` 解析，`frontendErrorHandling.spec.ts` 和 `rg -n "catch\\s*\\{" src/vues/AICopilot.Web/src` 固化无裸 catch。

## 7. 第五批：Cloud 只读边界和生产数据路径

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-019 | CRITICAL | Done | Cloud 只读 | AI 只能读 Cloud；禁止 MCP、Tool、Agent workflow、后台任务或隐藏 adapter 写 Cloud | `ArchitectureBoundaryTests` + `CloudReadonlyChatBoundaryTests` |
| AI-SEC-020 | CRITICAL | Done | Simulation | 生产路径 Cloud 查询失败、为空或未配置时不得 fallback 到 Simulation；Real provider 失败必须返回 Cloud AiRead 错误；生产基础配置和 compose 不携带 `MockOnly=true` | `CloudReadonlySimulationTests` + `SecurityHardeningTests.DeploymentConfig_ShouldNotCarryKnownWeakSecrets` |
| AI-SEC-021 | HIGH | Done | Cloud AiRead | Device、DeviceLog、Capacity、ProductionData、Process、ClientRelease 六类正式语义只能走 Cloud AiRead 正式只读 API | `CloudAiReadClientContractTests` + `SemanticAnalysisRunnerTests` |
| AI-SEC-022 | HIGH | Done | production records | 生产记录唯一高频路径是 `/api/v1/ai/read/production-records` | `CloudAiReadClientContractTests` endpoint policy |
| AI-SEC-023 | HIGH | Done | DeviceLog 追问 | 追问其他日志级别、设备、工序、时间必须重新查询，不基于上一轮回答推断 | `DeviceLogFollowUpIntentRewriterTests` |

实施要点：

- `CloudAiReadEndpointPolicy` 只允许 Cloud AiRead 当前八个正式 typed GET；不得保留任意 method/path 公共传输、可配置 POST allowlist、legacy adapter 或双轨接口。GET allowlist 必须逐项覆盖：`devices`、`processes`、`client-releases`、`device-client-states`、`capacity/summary`、`capacity/hourly`、`device-logs`、`production-records`。
- `deviceCode` 只能用于设备解析，不能当 `deviceId` 发送给业务读取端点。
- `scenarioId`、`from`、`to`、`pilotWindowId`、`boundary` 等 AI 内部参数不得透传 Cloud。
- Cloud 只读 DB direct mode 必须继续使用只读账号、显式白名单表、preflight grant check。
- Production `appsettings.json` 和 `deploy/enterprise-ai/docker-compose.yaml` 不得携带 `MockOnly=true`；mock/simulation 只能留在 Development 或专门测试路径。

第五批关单证据：

- `docs/Cloud只读数据分析契约.md` 与 Cloud AiRead、CloudReadOnly Direct DB、Text-to-SQL、DeviceLog 和 Simulation 测试口径一致。
- `ArchitectureBoundaryTests` 固化 AICopilot 不直接引用 Cloud 项目/命名空间、Cloud write tools 不纳入范围、CloudReadOnly direct DB 只读 guard、governed schema、只读账号 grant preflight，并用构造反射锁定正式语义执行器只依赖 Cloud AiRead、planner 和 logger。
- `CloudReadonlyChatBoundaryTests` 阻断 Cloud 业务修改、禁用设备、补录产能、删除日志和上传生产数据等写语义。
- `CloudReadonlySimulationTests` 固化 Simulation 只能在 Development 使用，Real 模式必须双开 `CloudReadonly:Real` 和 `CloudAiRead`，Cloud AiRead 不可用时不得降级返回 Simulation。
- `CloudAiReadClientContractTests` 固化 `/api/v1/ai/read/devices`、`processes`、`client-releases`、`device-client-states`、`device-logs`、`capacity/summary`、`capacity/hourly`、`production-records` 端点和参数契约；`deviceCode` 只有在未截断搜索结果中唯一精确匹配时才能解析成 `deviceId`，设备状态随后只向 `/device-client-states` 发送正式 `deviceId`，不得把 `deviceCode` 或整句自然语言降级为 keyword。
- 统一业务查询管线覆盖六类能力的插件结果分类；`Empty`、`NeedClarification`、`Unauthorized` 终止，只有 `Unsupported` 或同源 `Unavailable` 可由确认计划选择同源 Text-to-SQL。Recipe 仍在 planner 前拒绝，Simulation 和跨源 fallback 始终禁止。
- `DeviceLogFollowUpIntentRewriterTests` 固化追问日志级别、设备、工序、时间窗口时重新生成 `Analysis.DeviceLog.*` 查询。

### 7.1 2026-07-10 AI-only 契约、安全、Agent 与前端修复

以下编号补充记录此前 `AI-SEC-021/022` 关单后发现的消费方假阳性和能力缺口；历史 `Done` 结论不重写，新项按当前源码事实独立关单：

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-029 | HIGH | Superseded | Device 语义/CloudRead | 设备主数据、最后上报运行状态和最新日志级别继续分离；旧“任何情况都不回退 Text-to-SQL”已由统一查询契约取代。插件成功/空结果终止，只有 `Unsupported` 或同源 `Unavailable` 可按确认计划进入同源 Text-to-SQL，始终禁止 Simulation 和跨源回退 | 受影响 Contract/Unit/Application/Workflow |
| AI-SEC-030 | HIGH | Done | production records | `typeName/typeKey` 不得冒充 `processName/stationName`；正式字段缺失保持空 | `CloudAiReadClientContractTests` 负向 fixture + required runner 全量对账 |
| AI-SEC-031 | HIGH | Done | Cloud AiRead transport | 删除可配置 POST、任意 method/path 公共传输和双轨接口；只保留八个正式 GET，且不破坏 Cloud identity status GET 校验 | `CloudAiReadClientContractTests` + `ToolRegistryApplicationTests` + ArchitectureTests + required runner 全量对账 |
| AI-SEC-032 | HIGH | Done | JWT runtime | HttpApi 启动统一校验 issuer、audience、至少 64 字符 secret 和正数 token lifetime | Unit/Architecture/HttpIntegration 安全测试 + required runner 全量对账 |
| AI-SEC-033 | HIGH | Deferred | bootstrap admin 默认值 | 默认关闭 auto-bind、默认员工号为空；本项涉及工作区部署总览/profile，同步授权前不得在 AI-only 窗口半修 | 外部部署治理授权后执行 compose/config/security tests |
| AI-SEC-034 | MEDIUM | Done | Process Agent | 在统一语义和 Agent workflow 内增加 `/processes` 只读能力 | semantic/planner/Workflow/EndToEnd/GoldenEval + required runner 全量对账 |
| AI-SEC-035 | MEDIUM | Done | ClientRelease Agent | 在统一语义和 Agent workflow 内增加 `/client-releases` 只读能力，不生成 Cloud 未返回的发布事实 | semantic/planner/Workflow/EndToEnd/GoldenEval + required runner 全量对账 |
| AI-SEC-036 | MEDIUM | Done | DeviceStatus Agent | 复用 AI-SEC-029 唯一状态实现，覆盖空态、心跳缺失、权限、截断和 Plan 确认门禁 | Workflow/EndToEnd/GoldenEval + required runner 全量对账 |
| AI-SEC-037 | MEDIUM | Done | REST/OpenAPI contract | 使用现有依赖增强 OpenAPI、前端快照和错误码目录测试；新增生成器依赖需另行批准 | `Suite=FrontendIntegrationContract` + `ErrorCodeCatalogTests` |
| AI-SEC-038 | MEDIUM | Done | Web 产品化 | 保留会话隔离、真实运行状态、错误和 Widget 契约，完成真实视口与全状态验收 | type-check/unit/build/smoke + 1920×1080、1366×768、1024×768 浏览器验收 |
| AI-SEC-039 | LOW | Done | dead code | 证据确认无绑定、反射、序列化或测试引用后删除 `CloudReadonlyPilotReadinessOptions` | 全仓 `rg` + solution build + ArchitectureTests + required runner 全量对账 |

本批固定口径：`Analysis.Device.Status` 只能称为“最后上报运行状态”，同时给出心跳/更新时间；Cloud 没有正式 freshness 规则时不得推断在线、离线、当前或 stale。零条表示暂无状态上报，不表示离线。Cloud 提供方契约缺失、模糊或 fixture 不一致时，AI 消费方不得猜字段、扫描分页或自动选择第一条。

本批关单证据：Cloud AiRead 公共传输已收敛为八个 typed GET；设备主数据、最后上报状态与最新日志级别已分离；生产记录缺失字段不再代填；JWT 统一运行时校验完成；Process、ClientRelease、DeviceStatus 已接入统一语义、Agent 确认门和 Cloud-only 执行；OpenAPI/ProblemDetails 快照、前端真实状态门禁和无用 readiness DTO 删除均有测试覆盖。AI-SEC-033 继续 Deferred，因为默认 bootstrap admin 还需要工作区部署总览/profile 的跨目录同步授权。真实 Cloud 提供方 HTTP E2E、部署和生产验证不属于本次 AI-only 关单证据，不得据此宣称 Cloud 已发布或生产已验收。

### 7.2 2026-07-11 Agent 分支事实与路由日志补漏

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-040 | HIGH | Done | Agent workflow fan-in | `BranchResult` 必须区分 `Skipped/Empty/Succeeded/Failed`；必需性由当前 intents 与 executor 相关性判定产生；必需分支失败必须阻止最终合成，合法空结果可以继续，非成功载荷不得进入上下文 | `AgentWorkflowBranchSemanticsTests` + `ClaudeFollowupClosureTests` + ArchitectureTests + solution build |
| AI-SEC-041 | HIGH | Done | intent routing 日志 | 不记录模型路由原始响应、reasoning、prompt 或查询原文；只记录 response length、SHA-256、response type 和 parse state | `AgentWorkflowBranchSemanticsTests.RoutingResponseLogMetadata_ShouldExposeOnlyLengthHashTypeAndParseState` + AI-SEC-016 日志门禁 |

本批固定口径：四个分支继续通过 `Task.WhenAll` 和 `AgentWorkflowSink` 并行 fan-out/fan-in；没有相关 intent 才能标记 `Skipped`，相关能力执行后真实零结果才能标记 `Empty`，异常只能标记 `Failed`。`Required + Failed` 在 `ContextAggregatorExecutor` 和 final agent 之前返回稳定 Chat Error；可选失败、跳过和空结果不向最终模型注入伪上下文。调用方取消继续向上传播。PlanDraft 的能力发现边界、Cloud-only 八个 typed GET、成功/空集/失败均不得 fallback、Tool/MCP/HITL 审批边界均未改变。

本批关单证据：新增行为测试覆盖四态区分、异常不再冒充 empty、合法 empty、required/optional gate、取消传播、只聚合 succeeded 以及真实 `ILogger` formatter 不含路由原文；既有 topology/Cloud-only/semantic 测试和 solution build 用于证明四分支并行、Cloud 只读与无 fallback 边界没有回退。未执行生产发布或线上日志验收，因此 `Done` 只代表仓库内实现与本地门禁完成。

### 7.3 2026-07-11 Simulation 默认值与 CI 执行元数据

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-042 | HIGH | Done | Simulation 默认值 | 两层 HttpApi JSON 配置叠加后仍默认 Disabled；Simulation 只能由测试 fixture 或显式运行参数开启，Production 拒绝规则不放宽 | `CloudReadonlySimulationTests` 真实加载 `appsettings.json + appsettings.Development.json`，既有 Production 负向测试保留 |
| AI-SEC-043 | HIGH | Done | CI / 部署元数据 | PR 与手动运行先把受信输入解析为不可变 base SHA；Node 24 action 与 runner 版本匹配；全部部署 Shell 按直执行或 bash/source-only 唯一分类并锁定 Git mode | Simulation workflow + `DeploymentPreflightBehaviorTests` + ArchitectureTests + 成功 runner job 版本证据 |

本批固定口径：脚本不再重复解析 JSON 充当配置行为测试；部署 preflight 不再混入大型安全静态测试；Controller 注入约束用运行时类型反射验证。新增 tracked Shell 未进入唯一分类、直执行脚本不是 `100755`、手动 base ref 未严格校验或比较对象不是 commit SHA，都必须在业务测试或发布前失败。

### 7.4 2026-07-11 正式语义执行器数据源边界收口

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-044 | HIGH | Superseded | 统一业务查询入口 | Recipe 继续在 planner 前拒绝；六类正式能力改由 provider registry、确认上下文和结构化结果统一编排。旧“关闭或不可用也绝不回退”已被同源条件 fallback 契约取代；不得恢复第二套 Runner/Guard/Prompt | 受影响 `SemanticAnalysisRunnerTests`、查询管线与 Architecture/Security |

本批没有重命名或迁移既有 HTTP route、配置键、physical mapping / semantic source status 运维诊断及其消费者，也没有删除 `SemanticSqlGenerator` 独立实现和测试；这些表面不属于正式语义执行器，后续若治理必须重新做生产与测试消费者审计，不能借本批 no-fallback 收口扩大删除范围。

### 7.5 2026-07-11 DataAnalysis 最终上下文字段边界

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-045 | HIGH | Done | DataAnalysis final context | metadata/preview 共用唯一字段标签映射；内部与 governed-schema 敏感 raw field 整项丢弃；标签指令、控制字符、超长和重名只在唯一入口收口；flat preview 只输出显式标量白名单，任意其他 object/collection 不展开也不调用自定义 `ToString()` | Application/GoldenEval 对应 formatter/guardrail/fallback + Semantic/Widget/compact 定向回归 + ArchitectureTests + required runner 对账 + solution build |

本批固定口径：formatter 不再分别解析 metadata 名、preview key 和逐行重名；它先单次收集最多 3 个可识别 dictionary row，再根据 metadata、schema 顺序和实际 row key 产生一份 `OrdinalIgnoreCase` 映射。字段敏感判定复用 `CloudReadOnlyGovernedSchema.BlockedFieldFragments`，值仍只走既有 `SanitizeValue/SanitizeTextValue` 链，没有第二份 blacklist 或递归 nested sanitizer。Semantic/FreeForm Widget 不消费 formatter label map，本批不修改 Widget、route、Cloud API、数据库或部署链。

### 7.6 2026-07-11 Outbox 死语义删除与事务重试强制后续

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-046 | HIGH | Done | DbContext / Outbox / retry | `AI-PERSIST-01a` 已删除无生产者扫描与重复 Outbox ownership；01b 已把普通 repository、独立 audit writer、AiGateway `Session` events 和 RAG delayed factories 收口到唯一 `PersistenceCommitEngine` / `RepositoryPersistenceCommitter`；01c 删除 `EfTransactionalExecutionService`，让 Identity 复用同一 engine，非成功 `Result` 回滚全部中间保存并在回滚后独立提交拒绝审计。RAG `UploadDocument` 与 active AiGateway `SessionTemp` / `AgentInput` 上传先写 durable journal、再写物理文件并复用 commit id；请求、RAG 删除 consumer 与 DataWorker 使用同 commit PostgreSQL advisory lease，删除事件必须先安全退休 journal 再删文件。DataWorker 在固定共享卷按 marker 对账、局部隔离失败项并按 retention 分批清 marker。旧原始 `IFileStorageService.SaveAsync`、重复文件测试替身和重复 Identity 源码门禁已物理删除 | 真实 PostgreSQL pre-commit retry、Identity rejected-result/UserManager rollback、commit-ACK lost、fresh verification、UploadRecord/RAG 文件未知结果、unknown 后删除事件、活跃 lease、durable absence、损坏日志/缺失文件 fail-closed、marker retention/index、migration ownership/safety + 全量与 deployment/baseline/scope gates |
| AI-SEC-047 | HIGH | SourceCandidate / NOT-VERIFIED | ArtifactWorkspace 文件集/数据库 | `ArtifactFileSetOperation`、file-set store、checkpoint gate、outcome authority probe、migration/EF mapping 和 runtime finalization 已覆盖 workspace 初始化、draft 文本/二进制多文件、版本归档、当前文件替换及 `final/` 文件集；stage manifest、task/node fencing、数据库 checkpoint/marker、rollback/reconciliation 与 orphan cleanup 均有源码 owner。尚未运行真实文件系统 + PostgreSQL 故障矩阵，不能标 Done | 覆盖当前文件、归档两文件、final 多文件复制、workspace 初始化、DB 失败/ACK lost/进程中断/后台对账的真实文件系统 + PostgreSQL 行为测试 |
| AI-SEC-048 | HIGH | Partial | KnowledgeBase shadow upload 删除 | 生产消费者审计确认 KB 唯一真实入口是 `/api/rag/document`；AiGateway shadow record 不可按 KB 查询且无可读文件。本批已停止新写：API/前端只允许 SessionTemp/AgentInput，删除 RAG bridge/DI，领域和查询 spec 拒绝 KB scope。`AI-PERSIST-01e` 不再建设 saga，而是在单独维护窗口盘点/导出历史 shadow 行，检查 RAG hash、旧任务引用和异常列值，随后删除 shadow 行、索引、`knowledge_base_id` / `rag_document_id`，并增加 active scope 与目标二选一数据库约束；status 列是否删除由历史值盘点决定 | 止写端点/领域/查询负向测试 + 生产树 bridge 零引用 + 前端 type/unit/build；后续真实 PostgreSQL upgrade 测试证明只删 shadow、保留 Session/Agent 行、列/索引/check/not-null 正确，维护窗口 drain 旧 HttpApi 后再执行 migration |
| AI-SEC-049 | MEDIUM | Partial | 上传安全策略技术探测去重 | AiGateway 与 RAG 必须保留各自业务 allowlist、限额和拒绝语义，但当前仍重复 seekable stream 归一化、header 读取、MZ/文本 NUL 探测和 content-type 技术判断。后续治理只抽取一个窄的字节探测/stream ownership helper，明确临时 MemoryStream 的释放责任；禁止合并两套业务政策、复制第三份规则表或用万能 upload framework 隐藏差异 | 等价矩阵覆盖 seekable/non-seek、短流、MZ、NUL、空 content-type、调用方/被调用方 dispose ownership；两套 policy 行为不变，重复技术实现归零，Architecture/Backend 全量通过 |
| AI-SEC-050 | HIGH | Partial | RAG 同文件并发幂等 | `UploadDocument` 当前 hash 预查只覆盖顺序请求，数据库没有 `(knowledge_base_id, file_hash)` 唯一约束，两个并发请求可各自读到不存在并提交两条 Document/两份文件。治理前先盘点既有重复 hash、chunk/审计/事件引用并确定 canonical document，再增加数据库唯一约束；唯一冲突必须安全返回既有 Document 或稳定冲突，并让失败请求通过既有 file journal 回滚自己的文件。禁止把应用层 `Any`/集合查重称为并发幂等 | 真实 PostgreSQL barrier 并发上传同 KB/同 hash，最终一条 Document/一份有效文件；不同 KB 可各自保存；唯一冲突、ACK unknown、既有重复数据 migration、Outbox/chunk/audit 保留均有行为测试 |
| AI-SEC-051 | HIGH | Done | 至少一个可用管理员 | `DisableUser`、`UpdateUserRole` 和 migration seed 已在唯一 Identity transaction 内先取得固定全局 `pg_advisory_xact_lock`，再读取用户、角色和 enabled Admin；多角色 Admin 按真实 membership 判断，拒绝业务事务先回滚，再独立提交 Rejected audit；disabled bootstrap 不会被 seed 偷偷启用，最终零 enabled Admin 会明确失败。execution-strategy transient retry 已用竞争事务证明第二 attempt 会重新加锁并重读新状态。当前生产树没有 DeleteUser API，未来新增减员入口必须同时进入 `AI-ARCH-001` Analyzer 和真实并发测试，不能制造空壳 API | 分层样板已由 Unit policy、Contract ProblemDetails、真实 PostgreSQL lock/retry/rejected audit、真实 Aspire HTTP auth/middleware/trace 和 Architecture owner 分别持有；旧 Backend 同义断言已删除，各 required runner 必须 0 failed/0 skipped |
| AI-SEC-052 | HIGH | Partial | Admin 恢复权限基线 | `UpdateRole` 当前可把 Admin 角色的关键身份恢复/权限治理能力全部移除；enabled Admin 数量虽然仍大于零，但可能形成“仍叫 Admin、却无法恢复治理能力”的伪可用管理员。该问题与 AI-SEC-051 的数量不变量分开治理 | 先定义不可移除的 Admin 最小恢复权限集合；更新 Admin 时缺少任一基线权限必须稳定拒绝并写审计；普通角色仍允许差量权限；API、真实数据库行为和 Analyzer/Contract 正反例均通过 |
| AI-SEC-053 | MEDIUM | Partial | ECharts 依赖 XSS 公告 | PR run `29170924668` 的前端 audit 报告 `echarts 6.0.0` 命中 `GHSA-fgmj-fm8m-jvvx`；当前门禁只拒绝 high/critical，因此 job 成功不代表该中危漏洞不存在。GitHub advisory 指向 `<6.1.0` 受影响、`6.1.0` 首次修复 | 独立前端依赖批次升级到 `>=6.1.0`，运行 type-check、当前 168 条 Vitest、build、图表组件/数据编码回归与 `npm audit`；确认 lockfile 只包含已修版本后关单，不在测试治理批次顺手升级 |
| AI-SEC-054 | MEDIUM | Partial | Web mutation ACK-unknown / 幂等回放 | 本批已为普通请求设置显式 timeout、禁止非幂等 SSE 自动 reconnect，并对 DELETE session 用权威列表对账；但 create session、upload、Chat/Plan/approval SSE 在服务端已提交而响应/首个权威 chunk 丢失时，仍缺 client operation id、数据库唯一 receipt 和结果回放。超时只能标记 outcome unknown，不能推断“未创建/未执行”或自动重放 | 为每类 mutation 定义稳定 `clientOperationId`，服务端以用户+操作类型+key 唯一保存 receipt/result，重试返回同一结果；覆盖 commit 后断链、chunk 前断链、重复点击、进程重启、跨实例和 receipt retention 的 Contract/Workflow/Persistence 测试，再允许安全重试 |

`AI-PERSIST-01b/01c` 固定口径：业务代码不写 retry loop；每个 attempt 对业务 Context 只调用一次 `SaveChangesAsync(false)`，Outbox/audit/marker 使用同一 PostgreSQL transaction，commit 确认后才 accept/clear。Identity Result 写命令也复用该 engine，失败 Result 不能提交 UserManager/RoleManager 中间保存。COMMIT ACK 丢失由 fresh context 验证 marker；无法确认时返回 `persistence_commit_outcome_unknown` 且不自动重放。RAG `UploadDocument` 与 active SessionTemp/AgentInput `UploadRecord` 单文件上传必须先写 journal、再写文件并复用 commit id，DataWorker 取得同一 advisory lease 后按 marker 对账；损坏 journal、已提交但缺失文件都保留证据并停止危险清理。`AuditTransactionCoordinator`、`RagIntegrationEventStager`、`EfTransactionalExecutionService`、原始上传写 API、KB RAG bridge、AiGateway/RAG Outbox mapping 和重复 PostgreSQL/文件测试资产均已物理删除。`AI-SEC-046` 只对事务 engine、Identity 和上述 active 单 Context 上传边界关单；`ArtifactWorkspace` 为 `AI-SEC-047 SourceCandidate / NOT-VERIFIED`，历史 KB shadow 清库为 `AI-SEC-048 Partial`，本状态不代表已验证、合并或生产部署。

### 7.7 2026-07-19 Cloud↔AI provider 跨契约收紧

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-055 | HIGH | Partial | Cloud AiRead provider/consumer cross-contract | Cloud 与 AI 共用唯一 `1..100` row-limit 口径；sealed semantic plan 越界必须在 provider/HTTP 前固定拒绝；八 GET item exact schema、required-present nullable、全部 raw item 先验后 Take、envelope `nextCursor/rowCount`、Production safe-key/六 type/fieldSchema 关联/稀疏 required 语义都必须 fail-closed，禁止宽松 DTO、未知字段或截断掩盖 drift | AI dirty-candidate 已有独立静态 PASS，InProcess 45/45、Application 1/1、Simulation 12/12，均 0 failed / 0 skipped / 0 warning；仍需当前 clean HEAD 的完整 required/coverage/reconcile、Cloud provider 真实 controller/DTO/validator tests、双方原字节 digest 和 Manual-only 非生产 live 后才能 Done |

本项没有改变 `AI-SEC-019/021/022/031` 的历史关单事实，也不把 consumer targeted tests 冒充 Cloud provider 已发布或生产可用。Cloud 与 AI 任一生产字节变化都会使旧 digest/live 证据失效；完成顺序固定为 provider tests → consumer tests → clean-HEAD 原字节 digest → 非生产联合验收，且不得执行生产部署。

## 8. 第六批：Text-to-SQL 和提示词泄露门禁

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-024 | HIGH | Done | LLM Text-to-SQL | prompt 只暴露 governed schema，不暴露连接串、role、样例数据、非白名单字段 | `ArchitectureBoundaryTests` + `PromptGovernanceTests` |
| AI-SEC-025 | HIGH | Done | repair retry | 修复重试默认最多 3 次、硬上限 5 次；不可修复错误不重试 | `CloudReadOnlyTextToSqlFallbackRunnerTests` |
| AI-SEC-026 | HIGH | Done | 审计/日志/state | SQL 原文、用户 prompt、参数值、连接串、敏感字段不得落库或展示；只保存 hash 和安全分类 | `ArchitectureBoundaryTests` + `CloudReadOnlyTextToSqlFallbackRunnerTests` |
| AI-SEC-027 | HIGH | Done | Final answer | 最终回答不得暴露 SQL、表名、视图名、sourceName、endpoint、内部字段 | `AgentSafetyApplicationTests` + `PromptGovernanceTests` |

实施要点：

- `CloudReadOnlyTextToSqlFallbackRunner` 只能在当前调用内把上一轮失败 SQL 临时回传给 LLM。
- `DataAnalysisFinalContextFormatter` 和 `FinalAgentBuildExecutor` 保持安全上下文，只给最终模型执行事实和脱敏摘要。
- 前端运行详情继续只展示查询次数、返回行数、业务过滤条件和截断状态。

第六批关单证据：

- `CloudReadOnlyLlmTextToSqlGenerator` 的输入只包含 `governedSchema`、`joinHints`、repair hash/length 和输出契约，不带连接串、role、样例数据或任意 schema。
- `CloudReadOnlyTextToSqlFallbackRunner` 只在当前调用内传递 `PreviousSqlForRepair`；成功/失败结果和审计只包含 `QueryHash`、`questionHash`、`sqlHash`、行数、截断状态和 repair 分类。
- `CloudReadOnlyTextToSqlOptions` 默认 repair 3 次、硬上限 5 次，timeout/write SQL 等不可修复错误不重试。
- `ArchitectureBoundaryTests` 禁止 `PreviousSqlForRepair` 进入审计、日志、state、结果或持久化模型，并校验 repair attempt 只保留 SQL hash/length。
- `AgentSafetyApplicationTests` 构造 SQL、连接串、sourceName、tableName、prompt injection、DeviceLog 内部字段，最终上下文和展示块必须脱敏或移除。
- `PromptGovernanceTests` 固化 chat answer 和 cloud readonly text-to-sql prompt 的只读、governed schema、不得暴露 SQL/数据库名/物理表名约束。

## 9. 当前测试与发布路由

- 默认 push/PR 与业务开发使用 `Select-AICopilotCiTests.ps1 -Mode Default`，只运行 Architecture、Security 和受影响 Business，并按当次 discovery/TRX 对账。
- 普通部署只允许工作区 `deploy/Deploy-Changed.ps1` 调用 `Deployment` 模式，复用同 SHA、同 changed-files scope 的绿色证据，只补缺失的受影响 Architecture、Security、DeploymentContract。
- 全仓 required、coverage、mutation、duplication、完整 Web/Playwright、Quality、Full 和 CrossProject 均为用户显式模式，不得由 push、普通部署或 nightly 自动追加。
- 普通部署视代码已经完成，只接受 clean、已提交的 `main`；可以 push 现有 HEAD，但不得创建提交、编辑文件或失败后修代码。配置、迁移、健康、只读权限和回滚登记按部署计划验证。
- 三端从零部署只走工作区 `deploy/Deploy-FromZero.ps1`，由 canonical Keychain schema 提供根密钥；它不运行业务测试，也不创建设备、不注册 `ClientCode`、不轮换设备 bootstrap secret。
- 发布后的 HTTP/OIDC、模型 provider、Cloud readonly grant 和 release summary 检查按本次受影响服务执行；未执行不得冒充通过。

## 10. 外部依赖和跨项目项

以下项不由 AI 端单独完成，必须单独开 Cloud/Edge 或基础设施任务：

- CloudPlatform 是否也保持 HTTP-only，以及 Cloud nginx / OIDC Provider 的安全头和 HTTP 内网口径。
- EdgeClient 默认密码、Launcher 样本账号、MES token、MD5/HMAC、部署脚本真实 IP。
- GitHub self-hosted runner 到 OIDC/Vault 或等价短期凭据的基础设施落地。
- 服务器专用部署用户、sudo 白名单和 root SSH 禁用的系统级操作。
- Harbor、Cloud、AICopilot、模型服务之间的网络 ACL 和内网边界配置。

这些外部依赖在 AI 计划中只能记录，不得伪造成 AICopilot 已完成。

## 11. 不纳入当前 AI 修复的项

- 不做 HTTPS/TLS/HSTS/nginx 443/certificate 流程。
- 不修改 CloudPlatform 或 EdgeClient。
- 不通过 AI 端绕过 Cloud 权限、设备权限、MES/Cloud 分流或 Edge 启动红线。
- 不把 Simulation、dry-run、mock、测试数据或空态伪装成真实生产数据。
- 不保留旧安全债务的长期兼容路径；确需短期过渡时，必须写明退出条件并由用户确认。

## 12. 关单格式

每个编号关单时必须在复盘写明：

- 编号和标题。
- 改动文件。
- 是否只在 AICopilot 内。
- 是否影响 Cloud 只读边界、OIDC、MCP、Tool、Agent workflow、DataAnalysis 或前端错误契约。
- 验证命令和结果。
- 是否形成长期规则；形成则写入规则或专题契约，未形成则写明“无新增长期规则”及原因。

最终回复必须列出：

- 复盘文档。
- 规则沉淀位置。
- 验证命令。
- 未完成项或外部依赖。
