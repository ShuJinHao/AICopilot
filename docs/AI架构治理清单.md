# AI 完整修复计划（HTTP-only 修正版）

本文档是 AICopilot 当前安全和架构修复的完整执行计划，也是 AI 端治理清单。长期规则以 `AGENTS.md`、`资料/AICopilot业务规则.md` 和对应专题契约为准；`AICopilot 项目部署与维护指南.md` 与 `deploy/enterprise-ai/README.md` 是部署执行入口，不另立长期规则权威。

专题契约入口：

- `docs/AICopilot安全部署契约.md`：部署安全、GitHub workflow/test trust root、receipt 冷启动、HTTP-only、secret、镜像、SSH、runner 和发布验收。
- `docs/Cloud只读数据分析契约.md`：Cloud 只读、Cloud AiRead、CloudReadOnly Direct DB、Text-to-SQL、DeviceLog 和 Simulation 边界。
- `docs/Agent工作流与异常契约.md`：Agent workflow、Plan/Chat、MCP/Tool/Human-in-the-loop、异常、前端错误和运行详情。

## 0. 执行边界和红线

- 本计划只允许修改 `AICopilot`。`IIoT.CloudPlatform` 和 `IIoT.EdgeClient` 相关问题只能记录为外部依赖或跨项目后续任务，不能在 AI 端任务中顺手修改。
- 当前内网生产部署必须保持 HTTP-only。不得把 HTTPS redirection、HSTS、nginx 443 listener、证书申请/续期或 OIDC HTTPS metadata 强制校验列为当前修复门槛。
- 如果未来要切 HTTPS，必须由用户另行批准传输层方案和证书来源；不能在 AI 安全整改中夹带。
- HTTP-only 不代表放弃安全。当前安全整改必须优先落内网可执行项：端口收敛、同源代理、CORS 白名单、强 secret、短期 token、非 root 容器、敏感信息脱敏、Cloud 只读边界、除 HSTS 外的安全响应头和部署 preflight。
- 所有修复项必须进入本清单，编号、严重级、状态、验证命令和复盘结论齐全；未进入清单的项视为未处理。
- 每批代码或文档改动完成前，必须更新 `docs/改动复盘与规则沉淀.md`，并判断是否需要沉淀到项目规则或部署文档。

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

当前仓库内修复剩余未完全关单项：

- `AI-TEST-GOV-001` / `AI-RULE-TEST-012`：AICopilot 测试治理必须先从可信锚点完成 `H0 -> A0 -> C0 -> non-merging probe -> E0` 冷启动。当前仅完成只读审计和 readiness 契约，`E0=false`；仓库 ruleset 为空、`main` 未保护、只有 `ShuJinHao` 一个 collaborator，且没有来源绑定的三个 required context 或独立 reviewer。GitHub 官方能力复核又确认：当前 user-owned repository 不能配置 GitHub Enterprise Cloud organization/enterprise source-bound ruleset workflow 或 team required reviewer，普通 organization/Team 也不能未经 feature availability 核实就视为满足；原生 PR merge/squash/rebase 不能满足 v1 精确保留 PR-head 落点，因此状态为 `Blocked`。旧 `198cc593` 的 13 个提交和 baseline provenance 不得直接复用。
- `AI-SEC-010`：仓库内已固化灾备 workflow 的 least-privilege、self-hosted runner label、非 root runner、hosted-runner 禁用门禁，并让 production/secrets 相关 workflow 执行 `check-runner-security-attestation.sh`；平台侧验收模板和记录 linter 已提供，且 linter 会拒绝模板占位、未勾选项、空签署人和 `pending` / `not implemented` / `N/A` 等弱证明词，并要求记录包含 production environment secret 限制和生产/secret workflow 无 GitHub hosted runner 的证据。GitHub self-hosted runner 机器权限收敛、OIDC/Vault 或等价短期凭据落地仍属于基础设施任务；如果暂未落地，只能在平台记录中写成已批准的基础设施例外，并按结构化字段包含 `Ticket or change id`、`Exception owner`、`Due date` 和 `Current mitigation`，不能伪造成已完成。
- `AI-SEC-012`：旧 `encv1:` 密文迁移代码路径已规划/测试，migration worker 在批量改写前会先预检同一集合中已有 `encv2:` 密文能用当前密钥解密，避免同一集合内混合旧密文和不可读 `encv2:` 时产生内存级部分改写；数据库写入使用 AiGateway 事务，并让临时 `RagDbContext` 通过 `UseTransactionAsync(transaction.GetDbTransaction())` 复用同一事务，避免 `LanguageModel.ApiKey` 与 `EmbeddingModel.ApiKey` 数据库级部分迁移；保存后会自检没有非 `encv2:` 的非空密钥残留，并再次验证 `encv2:` 可解密；发布脚本测试已锁定全量和按需发布中的 `aicopilot-migration -> check_model_secret_migration_preflight -> runtime` 顺序；但真实生产库迁移或管理员重录入仍需要发布窗口验收。

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
- 架构和回归测试：`src/tests/AICopilot.ArchitectureTests`、`src/tests/AICopilot.BackendTests`、`src/vues/AICopilot.Web/tests`。

每个批次必须先定位到以上目录和对应测试，再改代码或文档。找不到源码证据的项只能标为外部依赖、待审计或不纳入，不能写成 Done。

## 2.2 Phase 0：测试治理 trust root 冷启动（AI-TEST-GOV-001）

长期规则见 `docs/AICopilot安全部署契约.md` 的 `AI-RULE-TEST-012`；跨项目通用 receipt 协议仍以工作区根 `docs/三项目测试架构治理总计划.md` 第 16.2 节和第 20 节为唯一权威。本节只记录 AICopilot 当前执行输入、批次和外部依赖。

| 执行 ID | 严重级 | 状态 | 当前结论 | 完成条件 |
| --- | --- | --- | --- | --- |
| AI-TEST-GOV-001 | CRITICAL | Blocked (`E0=false`) | 冷启动设计与旧链净差分已审计；尚未实施 `H0/A0/C0`，未生成真实 receipt，未改 GitHub settings；当前个人仓库没有原生 source-bound required workflow，v1 exact-head landing 与 GitHub PR merge 语义不兼容 | 分别选择来源证明（已具备 GitHub Enterprise Cloud required-workflow 能力的 organization 或专用 attestor App）与落点协议（landing-v2 或未来版本化 exact landing），再补第二真实 write reviewer、full clone、Active protection/ruleset 和 non-merging probe 全部取证 |

### 2.2.0 GitHub 平台可行性审计

- 2026-07-14 只读 API 复核：`AICopilot`、`IIoT.CloudPlatform`、`IIoT.EdgeClient` 均为 `owner.type=User`、`visibility=public`、默认分支 `main`；AICopilot ruleset 为空、`main` 未保护、direct collaborator 仅 `ShuJinHao`。这不是 VPN、公司服务器或测试 Runner 连通性问题。
- GitHub 官方规则把 source repository/workflow 绑定型 required workflow 放在 GitHub Enterprise Cloud 的 organization/enterprise ruleset；迁入普通 organization 或 GitHub Team 不自动获得该能力，迁移前必须核实目标 plan 和 feature availability。user-owned repository 不存在 team required reviewer；个人仓库仍可增加第二名具备 write 权限的真实用户并要求 PR 审批，但不能据此得到 source-bound base-owned workflow。
- required status 可以绑定某个 GitHub App；该 App 必须已安装到仓库、具有 expected-source 选择所需的 `statuses:write`、近期提交过 check run，并与既有 required status check 关联。当前协议要求 App 创建绑定原始 PR-head SHA 的 check run，所以还必须具有 `checks:write`；若未来只发布 commit status，必须先版本化修改 check-run 证据口径。绑定通用 GitHub Actions App 仍只能证明 check 来自该 App，不能证明运行的是候选不可修改的特定 workflow。若保留个人仓库，必须另行设计候选无法调用的专用 attestor App，并审核其 token、事件、commit-SHA 和 check-name 防伪边界。
- GitHub 默认 merge 在 base 生成新的 merge commit；squash 生成新的 squash commit；GitHub rebase 始终创建新 SHA；linear history 强制 squash 或 rebase。故当前 v1 的 exact-head landing 在“只走受保护 PR、禁止 bypass/direct-main”条件下不可执行。任何 landing-v2 都必须作为新协议单独审查，不能静默放宽为 synthetic merge 或只比较 tree。
- 决策门分为两个正交选择：来源证明优先选择已具备 GitHub Enterprise Cloud required-workflow 能力的 organization，备选为候选不可调用的专用 attestor GitHub App；落点协议另选 landing-v2 或未来版本化 exact-landing。未来 exact-landing 若需要一次性 bootstrap bypass，必须在新协议中冻结范围、lease、actor、reviewer 和失效条件，并在 non-merging probe 前撤销，probe 重新证明 bypass 为空；当前 v1 没有 bypass 例外。两个选择都需要用户另行授权远端组织/应用/PR/settings 操作和第二真实 reviewer；未决前停止在 readiness 分支，`E0=false`。

### 2.2.1 冻结输入与旧链裁决

- 冷启动锚点：`M0=156fbd31713fe4b26f7e2e8b4009a6f61ccb30d2`，也是 2026-07-14 审计时的 `origin/main`。
- 历史候选 tip：`198cc59318f4a1748c719b9b8ecff1d969952ce8`。`M0..tip` 是 13 个直接单父、0 merge 的线性提交，但全部 author/committer 属于同一主体；最终净差分为 237 文件（A61/M169/D7，`+49,988/-5,169`），按互斥路径口径为 workflow 6、deploy 17、tests/quality 61、docs/rules 10、production source 140、governance roots 3。
- 第 9 个提交 `2d06efd6590a06dad45e0609fbb3bc2cb64e3814` 才在同一提交自引入 `.gitattributes`、仅含 `@ShuJinHao` 的 CODEOWNERS、canonical workflow、policy、behavior、baseline、waiver 和测试配置；授权与消费没有分离，不能倒推前 8 个生产/测试/部署提交已受信。
- 后续 `e69f5d0` 同批修改 policy、behavior 和 baseline，`351334e` 同批修改 workflow、policy 和生产 deploy，`f345f94` 同批修改 workflow、policy、前端和测试；旧 baseline 的 `Reviewed`/run 字段不能替代真实独立 reviewer 或 base-owned provenance。
- 在 `M0` 与旧 commit 1 之间插入 `H0` 会改变后续全部 parent/tree/commit identity；旧 13 个 SHA 不可能原样保留。39 个路径又跨多个语义组共享，重建时必须按最终 hunk/语义拆分，不能整提交或整文件 cherry-pick。
- 当前 readiness worktree 是 shallow clone；它只能生成和审核文档候选。真实 `H0`、祖先证明和 receipt 必须改用 fresh/full clone 或完整 fetch。
- 本 readiness 文档分支不得先于 `H0` 合入 `main`。如果它或任何其它提交先落入 `main`，必须把新 tip 重新定义为可信锚点并重算 `H0`、receipt 和 provenance，禁止继续引用 `M0` 假装直接父关系未变。

旧链顺序仅作为审计索引：

```text
41edebdadb14e5f020b9aeaf28cfb8e6efc15799  preserve agent workflow branch failures
7764147590d0d1a85a9347d0a78b7ecc17d78f1d  harden release-candidate checks
ae445fe6b11a1d2618b0862440a89b6ecfc54e94  enforce cloud-only semantic runner
2e5eed04553dec6ff81c2f5444611935e66f3069  harden final data-analysis context
499829820a2edf33fc208d6af1867d3fa5c31c62  remove dead outbox persistence paths
7137e81cb50ed58a82e7592d297eac696413ff55  verify repository commit outcomes
f444faa98f67a5455b48a92b7fb40139fc7c6ed1  identity and file persistence reconciliation
88a9687a40e7c78d671bdc634e90941b91f3bde1  serialize enabled-admin mutations
2d06efd6590a06dad45e0609fbb3bc2cb64e3814  self-introduce legacy governance bundle
e69f5d032001632da3ad07e54e7369f5753339b8  normalize test discovery across OS
351334e3e16d84be243b39d24dff0086d9fff113  preserve deployment recovery evidence
f345f9424ad40a495efdefe8cf387851e589e175  session authority hydration fail-closed
198cc59318f4a1748c719b9b8ecff1d969952ce8  record session authority validation
```

### 2.2.2 冷启动阶段与 probe 矩阵

| 阶段 | 唯一允许内容 | 必须证据 | 禁止项 |
| --- | --- | --- | --- |
| `H0` | `M0` 的直接单父；仅 `.gitattributes`、含第二真实主体/team 的 CODEOWNERS、wrapper、validator、92 条 self-test、schema 六个路径 | parent/tree、逐路径 mode/blob/SHA-256、92/92、外部审核和最终 push 后非作者批准 | workflow、policy、baseline、waiver、receipt、readiness 文档、生产/测试/部署差异；`H0` 自证 |
| `A0` | `H0` 的直接单父；只新增一个 `pending/<MigrationId>.json`，mode `100644` | wrapper/validator/schema 从 `H0` blob 提取；receipt 精确冻结 C0 workflow 路径、字节、mode、blob、digest 和时限 | 同批改 workflow/policy/baseline；本地旧 receipt；作者自批 |
| `C0` | `A0` 的直接单父；pending 原字节/mode 移到 consumed，并新增 receipt 授权的唯一 preactivation workflow | diff/hash/count 与 receipt 完全一致；checkout 和 validator 参数均为原始 `pull_request.head.sha`；`migration-validator-selftest` 用 base harness 隔离验证 candidate validator 92/92，`build-test` 先执行 base authoritative gate 再跑 canonical build/test，`required-final` 只做 `needs + always()` fail-closed 汇总 | merge SHA、synthetic merge、candidate wrapper 进入 authoritative gate、额外路径、final policy/baseline 同批接入 |
| probe preflight | `C0` 后创建、永不合入，保持 `E0=false` | 三个 check-run 绑定同一原始 PR head，并预检已具备 GitHub Enterprise Cloud feature 的 organization/enterprise required workflow source repo/ref/path，或专用 attestor App、job 隔离和 reviewer 身份 | 用 probe 自身 workflow 或通用 GitHub Actions App 冒充来源绑定；合入 probe；复用 probe receipt |
| E0-candidate | probe 保持打开时启用候选保护配置，仍写 `E0=false` | 在 Active 配置下真实验证无批准、stale approval、追加提交、无 receipt 改 protected path、同名伪 check、candidate wrapper、synthetic merge、force 和 bypass 均不能放行；force/bypass 只能使用非破坏性 evaluation/mergeability 证据、与 main 同规则的牺牲 probe ref，或另获授权且预期失败的受控尝试 | 只看配置 JSON 或绿色 happy path 就宣称 active；用可能成功的 `main` 写入试探保护 |
| `E0` | 同一 probe 的 provenance 预检和 active-block 矩阵全部通过后才记录 | PR-only、strict up-to-date、linear history、禁 deletion/force/merge commit、至少 1 个独立批准、Code Owner、dismiss stale、most-recent-push 由他人批准、空 bypass；三个 context 均来源绑定 | 只锁 `required-final`、`any source`、普通同名 status、管理员 bypass、普通 merge queue |

receipt 字段、空值、mode/blob/hash、时限和状态转换不得由本清单另行定义；`H0` schema 必须逐项实现工作区总计划与 `AI-RULE-TEST-012`，并在 `H0` 外部审核包中输出完整字段矩阵。当前实现审查至少要确认 cold-start 缺失值有唯一表示、每个路径同时绑定 Git blob 与 SHA-256。`receipt.approvedBy` 必须唯一指向 `A0` AuthorizationOnly PR 在其最终原始 head 上仍有效的非作者人类 reviewer；`C0` Consume PR 还必须在自身最终原始 head 上取得单独的非作者有效 review，不能用 A0 字段或旧 approval 代替。最终以已审核 schema/validator 字节和两阶段 GitHub review 事实为准。

三个 required 角色的执行隔离、base/candidate 工具边界和 fail-closed 汇总只认 `AI-RULE-TEST-012`；本清单表格仅记录当前 AICopilot context 名与实施阶段，不构成第二套 workflow 规范。

`H0/A0/C0` 的 v1 最终落点必须保留已审核 PR-head SHA；merge commit、squash、rebase 和普通 merge queue 均会生成或验证不同 revision，不得沿用旧绿灯。GitHub 平台若无法在 PR-only、空 bypass 下完成 fast-forward 或等价精确落点，必须停止并另立可在最终 landing SHA 重跑同一 base-owned checks 的未来协议，不能用管理员 push 或改写 SHA 收口。

耐久 `E0` 需要已具备 GitHub Enterprise Cloud required-workflow feature 的 organization/enterprise ruleset 精确绑定 source repository/ref/path，或候选无法调用且 required check 明确绑定的专用 attestor GitHub App。普通 required status 即使绑定通用 GitHub Actions App，也只绑定 App、不绑定具体 workflow；这种配置只能保留为受控外部证据，不能写成 durable trust root active。

2026-07-14 GitHub 只读审计事实：

- repository rulesets 为 `[]`，`main` 返回 `Branch not protected`；direct collaborator 只有 `ShuJinHao`，teams 为空，不存在第二 reviewer。
- Actions `allowed_actions=all`、`sha_pinning_required=false`；默认 `GITHUB_TOKEN` 为 `contents:read` 且不能批准 PR，这一项为正向证据，但不足以建立 provenance。
- `production` environment 无 protection rules；它与 `AI-SEC-010` 的部署平台验收也不能替代代码 receipt reviewer。
- Draft PR `#59` 的 head 是 `198cc593`，仅有候选仓内产生的 `backend-full-tests`、`build-test`、`simulation-rc` 成功状态，无 review、无 `migration-validator-selftest`/`required-final`；这些绿灯不是 E0。

任一条件出现即停止，不实施或继续消费：第二主体缺失；PR/settings/landing 权限未另行授权；C0 最终字节未冻结；提交不是直接单父；A0 或 C0 缺少自身最终 head 上的非作者有效 review；receipt 过期、漂移或存在第二份 active pending；任一 check 不在原始 head；存在 force/bypass；同名伪 check 可放行；平台无法保留精确 landing SHA；base、head、landing、lease、workflow source 或 review identity 变化。

### 2.2.3 E0 后的最终语义重建批次

以下批次只表达旧 13 提交的最终净语义，不保留旧 SHA，也不允许在 `E0=false` 时实施：

| 批次 | 历史语义来源 | 最终范围/路径证据 | 依赖与顺序 |
| --- | --- | --- | --- |
| B1 | 旧 1、3、4 | Agent workflow/DataAnalysis fail-closed；23 个 unique paths（src 10/test 9/doc 4） | E0 后可最早准备 |
| B2 | 旧 2、11 | release-candidate/deploy recovery；38 个 unique paths（workflow 6/deploy 15/test 11/doc 5/src 1） | 与 B1 业务独立，但共享 CI/policy/docs，集成串行 |
| B3 | 旧 5 | dead outbox cleanup；28 个 unique paths（src 17/test 6/doc 5） | 先于 B4，或与 B4 按最终态合并，禁止重放短命 stager |
| B4 | 旧 6 | repository commit outcome/persistence engine；63 个 unique paths（src 42/test 14/doc 7） | 依赖 B3 |
| B5 | 旧 7 | identity + file persistence reconciliation；82 个 unique paths（src 54/test 18/deploy 4/doc 6） | 依赖 B4 的 commit engine/marker/allocator |
| B6 | 旧 8 | enabled-admin invariant serialization；26 个 unique paths（src 14/test 9/doc 3） | 依赖 B5 identity transaction/advisory lock |
| G0 | 旧 9、10，仅作参考 | 13 个 unique paths（governance root 3/workflow 1/test 6/doc 3） | 不得原样重放；E0 后重新生成 policy/baseline/waiver/workflow，并分别走 receipt |
| B8 | 旧 12、13 | frontend session-authority hydration；37 个 unique paths（src 20/test 12/workflow 1/doc 4） | B5/B6 的身份/API 事实稳定后实施 |

各行 unique path 不能相加当作 237，因为 39 个共享路径跨多个批次。共享治理文档、`AGENTS.md`、`ArchitectureBoundaryTests` 和 CI/policy 文件必须按 hunk/语义拆分；每个最终 SHA 都要重新运行责任测试、全量 required、baseline provenance 和 digest 对账。只有 G0 与后续 workflow/policy/baseline/waiver receipt 全部激活后，才能进入 `AI-SEC-051`、`AI-ARCH-001` 和测试物理迁移。

已推送但尚未进入 `main` 的规则提取分支已有 `AI-RULE-TEST-001` 至 `011`；Scheme C 后续重建该索引时必须注册 `AI-RULE-TEST-012` 并指向本安全部署契约，不能在 readiness 分支另建第二份索引或遗漏编号迁移。

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
- production/secrets 相关灾备 workflow 必须执行 `scripts/check-runner-security-attestation.sh`；该脚本只能证明 runner 机器本地事实，GitHub production environment secret 权限、required reviewers、OIDC/Vault 或等价短期凭据必须由平台侧单独填写 `runner-platform-attestation.template.md` 并留痕。`check-platform-attestation-record.sh` 会拒绝模板占位、未勾选项、空签署人和弱证明词，并要求记录包含 production environment secret 限制、`contents: read`、`self-hosted + iiot-linux-prod`、生产/secret workflow 无 GitHub hosted runner 的证据；OIDC/Vault 尚未落地时，只允许填写已批准的基础设施例外，并按 `Ticket or change id`、`Exception owner`、`Due date`、`Current mitigation` 四个字段写清例外证据。该脚本只校验记录完整性，不能替代真实平台验收。

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
| AI-SEC-017 | HIGH | Done | 前端 catch | 公共 API、stream、chunk、路由、OIDC、agent store、artifact、config、auth、RAG/access action 失败均有诊断日志、安全 fallback 或用户可见错误；裸 `catch {}` 已加前端单测门禁 | `chatErrorStore` / `chunkReducer` / `apiClient` / `chatStoreSkills` / `agentStateStores` / `configViewUsability` / `authStore` / `frontendErrorHandling` 单测 |
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
- AI-SEC-016 provider/workflow/worker 阶段证据：`AgentRuntimeFactory` provider fallback 和 endpoint pool fallback 不再 `LogWarning/LogDebug(ex, ...)`；`AgentTaskRunQueueWorker` iteration、queue item failure 和 heartbeat failure 不再记录原始 exception，worker-level failure message 固定为安全文案；`AgentWorkflowPipeline` branch fallback、`AgentSkillRouterAutoSelector`、`IntentRoutingExecutor`、`KnowledgeRetrievalExecutor`、`ToolsPackExecutor`、`DataAnalysisWidgetEmitter`、`FreeFormDbaAnalysisRunner` 和 `SemanticAnalysisRunner` fallback/error 日志只记录 `ErrorType` / `FailureCode` 与 `OriginalMessage=hidden_by_security_policy`；Cloud AiRead `MissingRequiredParameter` 用户文案不再拼接 `ex.Message`；`SecurityHardeningTests.ProviderWorkflowAndWorkerLogs_ShouldNotAttachRawExceptions` 固化这些源码不回退到 raw exception logger overload。
- AI-SEC-016 关单证据：`CloudAiReadHttpTransport`、`DocumentIndexingService`、`UploadDocument`、`McpServerBootstrap`、`McpRuntimeRegistrySynchronizer`、`McpServerManager`、`OutboxDispatcher`、`LanguageModelConnectivityTester`、`PostgreSqlSessionExecutionLock`、`TelemetryBehaviors`、`SqlAllowlistColumnInspector`、`SemanticQueryPlanner`、`ToolInputSchemaValidator`、`AgentDynamicPlanner`、`PlannerToolCatalog`、`AgentDynamicPlannerResponseParser`、`AstSqlGuardrail` 和 `AgentTaskRuntime` 不再把 raw exception 对象或 exception message 直接写入日志、接口响应、任务失败摘要或持久化失败原因；`SecurityHardeningTests.ProductionLogs_ShouldNotAttachRawExceptionObjects` 全量扫描 `src/hosts`、`src/infrastructure`、`src/services`、`src/core`、`src/shared` 禁止 `LogCritical/LogError/LogWarning/LogInformation/LogDebug/LogTrace` 以变量作为首参，从而覆盖 `ex`、`exception`、`e`、`cleanupException` 等异常变量名；`ErrorBoundaryMessages_ShouldNotReturnOrPersistRawExceptionMessages` 点名锁定已修错误边界。剩余 `ex.Message` 仅作为 `DataAnalysisToolResultFormatter`、`CloudReadOnlyTextToSqlRepairClassifier` 和 artifact finalized 识别的内部分类输入，输出仍是固定安全文案或 hash/code。
- AI-SEC-018 关单证据：`MessageRuntimeDetailsPanel` 使用无 `open` 的原生 `<details>`，smoke 验收断言“结构化展示”默认隐藏，点击 summary 后才可见；`runtimeDetails.ts` 对工具参数、工具结果、Widget 摘要、运行状态 summary/error 只输出安全摘要；`runtimeDetails.spec.ts` 覆盖 SQL、连接串、password、token、endpoint、sourceName、tableName、databaseName、内部路径和原始结果行不进入运行详情对象。
- AI-SEC-017 关单证据：`chatErrorStore` 统一解析 ProblemDetails `userFacingMessage` / validation errors / `detail` / `title`、已知 code 和安全兜底，SSE open/error 与普通 API 不再丢后端安全诊断；未知 Chat Error code 仍不直接展示 raw `detail`；AgentEvent、ApprovalRequest、AgentTask 和 Chat Error chunk 失败进入会话错误栏；agent catalog、task timeline、artifact preview/download/upload 和 ConfigView agent settings 刷新失败进入当前会话或页面错误栏；auth current-user 和 Cloud OIDC status 失败进入登录错误栏；RAG、Config CRUD、Access action 失败进入页面 `errorMessage`、`actionErrors` 或对应 dialog error；纯解析 fallback（metadata/widget/runtime details/chat run status）只降级展示或记录安全摘要，不是用户操作失败路径；chunk reload guard 的 storage catch 只保护 stale bundle 自动刷新；`chatErrorStore.spec.ts` 覆盖 ProblemDetails `detail/title` 解析，`frontendErrorHandling.spec.ts` 和 `rg -n "catch\\s*\\{" src/vues/AICopilot.Web/src` 固化无裸 catch。

## 7. 第五批：Cloud 只读边界和生产数据路径

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-019 | CRITICAL | Done | Cloud 只读 | AI 只能读 Cloud；禁止 MCP、Tool、Agent workflow、后台任务或隐藏 adapter 写 Cloud | `ArchitectureBoundaryTests` + `CloudReadonlyChatBoundaryTests` |
| AI-SEC-020 | CRITICAL | Done | Simulation | 生产路径 Cloud 查询失败、为空或未配置时不得 fallback 到 Simulation；Real provider 失败必须返回 Cloud AiRead 错误；生产基础配置和 compose 不携带 `MockOnly=true` | `CloudReadonlySimulationTests` + `SecurityHardeningTests.DeploymentConfig_ShouldNotCarryKnownWeakSecrets` |
| AI-SEC-021 | HIGH | Done | Cloud AiRead | 高频设备日志、小时/汇总产能、生产数据优先走 Cloud AiRead 正式只读 API | `CloudAiReadClientTests` + `SemanticAnalysisRunnerTests` |
| AI-SEC-022 | HIGH | Done | production records | 生产记录唯一高频路径是 `/api/v1/ai/read/production-records` | `CloudAiReadClientTests` endpoint policy |
| AI-SEC-023 | HIGH | Done | DeviceLog 追问 | 追问其他日志级别、设备、工序、时间必须重新查询，不基于上一轮回答推断 | `DeviceLogFollowUpIntentRewriterTests` |

实施要点：

- `CloudAiReadEndpointPolicy` 只允许 Cloud AiRead 当前八个正式 typed GET；不得保留任意 method/path 公共传输、可配置 POST allowlist、legacy adapter 或双轨接口。GET allowlist 必须逐项覆盖：`devices`、`processes`、`client-releases`、`device-client-states`、`capacity/summary`、`capacity/hourly`、`device-logs`、`production-records`。
- `deviceCode` 只能用于设备解析，不能当 `deviceId` 发送给业务读取端点。
- `scenarioId`、`from`、`to`、`pilotWindowId`、`boundary` 等 AI 内部参数不得透传 Cloud。
- Cloud 只读 DB direct mode 必须继续使用只读账号、显式白名单表、preflight grant check。
- Production `appsettings.json` 和 `deploy/enterprise-ai/docker-compose.yaml` 不得携带 `MockOnly=true`；mock/simulation 只能留在 Development 或专门测试路径。

第五批关单证据：

- `docs/Cloud只读数据分析契约.md` 与 Cloud AiRead、CloudReadOnly Direct DB、Text-to-SQL、DeviceLog 和 Simulation 测试口径一致。
- `ArchitectureBoundaryTests` 固化 AICopilot 不直接引用 Cloud 项目/命名空间、Cloud write tools 不纳入范围、CloudReadOnly direct DB 只读 guard、governed schema、只读账号 grant preflight 和高频 Cloud AiRead 优先级。
- `CloudReadonlyChatBoundaryTests` 阻断 Cloud 业务修改、禁用设备、补录产能、删除日志和上传生产数据等写语义。
- `CloudReadonlySimulationTests` 固化 Simulation 只能在 Development 使用，Real 模式必须双开 `CloudReadonly:Real` 和 `CloudAiRead`，Cloud AiRead 不可用时不得降级返回 Simulation。
- `CloudAiReadClientTests` 固化 `/api/v1/ai/read/devices`、`processes`、`client-releases`、`device-client-states`、`device-logs`、`capacity/summary`、`capacity/hourly`、`production-records` 端点和参数契约；`deviceCode` 只有在未截断搜索结果中唯一精确匹配时才能解析成 `deviceId`，设备状态随后只向 `/device-client-states` 发送正式 `deviceId`，不得把 `deviceCode` 或整句自然语言降级为 keyword。
- `SemanticAnalysisRunnerTests` 固化高频 DeviceLog/Capacity/ProductionData 在 CloudAiRead 启用时优先走 Cloud AiRead；Direct DB / Text-to-SQL 只保留治理白名单内补充分析。
- `DeviceLogFollowUpIntentRewriterTests` 固化追问日志级别、设备、工序、时间窗口时重新生成 `Analysis.DeviceLog.*` 查询。

### 7.1 2026-07-10 AI-only 契约、安全、Agent 与前端修复

以下编号补充记录此前 `AI-SEC-021/022` 关单后发现的消费方假阳性和能力缺口；历史 `Done` 结论不重写，新项按当前源码事实独立关单：

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-029 | HIGH | Done | Device 语义/CloudRead/Direct DB | 设备主数据、最后上报运行状态和最新日志级别分离；`Analysis.Device.Status` 只消费 `/device-client-states`，不得回退 Direct DB/Text-to-SQL/Simulation | `CloudAiReadClientTests` + semantic/mapping/runtime/acceptance + 全量 BackendTests |
| AI-SEC-030 | HIGH | Done | production records | `typeName/typeKey` 不得冒充 `processName/stationName`；正式字段缺失保持空 | `CloudAiReadClientTests` 负向 fixture + 全量 BackendTests |
| AI-SEC-031 | HIGH | Done | Cloud AiRead transport | 删除可配置 POST、任意 method/path 公共传输和双轨接口；只保留八个正式 GET，且不破坏 Cloud identity status GET 校验 | `CloudAiReadClientTests` + `ToolRegistryGovernanceTests` + ArchitectureTests + 全量 BackendTests |
| AI-SEC-032 | HIGH | Done | JWT runtime | HttpApi 启动统一校验 issuer、audience、至少 64 字符 secret 和正数 token lifetime | JWT options/startup tests + 安全定向测试 + 全量 BackendTests |
| AI-SEC-033 | HIGH | Deferred | bootstrap admin 默认值 | 默认关闭 auto-bind、默认员工号为空；本项涉及工作区部署总览/profile，同步授权前不得在 AI-only 窗口半修 | 外部部署治理授权后执行 compose/config/security tests |
| AI-SEC-034 | MEDIUM | Done | Process Agent | 在统一语义和 Agent workflow 内增加 `/processes` 只读能力 | semantic/planner/Agent/acceptance + 全量 BackendTests/AiEvalTests |
| AI-SEC-035 | MEDIUM | Done | ClientRelease Agent | 在统一语义和 Agent workflow 内增加 `/client-releases` 只读能力，不生成 Cloud 未返回的发布事实 | semantic/planner/Agent/acceptance + 全量 BackendTests/AiEvalTests |
| AI-SEC-036 | MEDIUM | Done | DeviceStatus Agent | 复用 AI-SEC-029 唯一状态实现，覆盖空态、心跳缺失、权限、截断和 Plan 确认门禁 | Agent workflow/acceptance + 全量 BackendTests/AiEvalTests |
| AI-SEC-037 | MEDIUM | Done | REST/OpenAPI contract | 使用现有依赖增强 OpenAPI、前端快照和错误码目录测试；新增生成器依赖需另行批准 | `Suite=FrontendIntegrationContract` + `ErrorCodeCatalogTests` |
| AI-SEC-038 | MEDIUM | Done | Web 产品化 | 保留会话隔离、真实运行状态、错误和 Widget 契约，完成真实视口与全状态验收 | type-check/unit/build/smoke + 1920×1080、1366×768、1024×768 浏览器验收 |
| AI-SEC-039 | LOW | Done | dead code | 证据确认无绑定、反射、序列化或测试引用后删除 `CloudReadonlyPilotReadinessOptions` | 全仓 `rg` + solution build + ArchitectureTests + 全量 BackendTests |

本批固定口径：`Analysis.Device.Status` 只能称为“最后上报运行状态”，同时给出心跳/更新时间；Cloud 没有正式 freshness 规则时不得推断在线、离线、当前或 stale。零条表示暂无状态上报，不表示离线。Cloud 提供方契约缺失、模糊或 fixture 不一致时，AI 消费方不得猜字段、扫描分页或自动选择第一条。

本批关单证据：Cloud AiRead 公共传输已收敛为八个 typed GET；设备主数据、最后上报状态与最新日志级别已分离；生产记录缺失字段不再代填；JWT 统一运行时校验完成；Process、ClientRelease、DeviceStatus 已接入统一语义、Agent 确认门和 Cloud-only 执行；OpenAPI/ProblemDetails 快照、前端真实状态门禁和无用 readiness DTO 删除均有测试覆盖。AI-SEC-033 继续 Deferred，因为默认 bootstrap admin 还需要工作区部署总览/profile 的跨目录同步授权。真实 Cloud 提供方 HTTP E2E、部署和生产验证不属于本次 AI-only 关单证据，不得据此宣称 Cloud 已发布或生产已验收。

## 8. 第六批：Text-to-SQL 和提示词泄露门禁

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-024 | HIGH | Done | LLM Text-to-SQL | prompt 只暴露 governed schema，不暴露连接串、role、样例数据、非白名单字段 | `ArchitectureBoundaryTests` + `PromptGovernanceTests` |
| AI-SEC-025 | HIGH | Done | repair retry | 修复重试默认最多 3 次、硬上限 5 次；不可修复错误不重试 | `CloudReadOnlyTextToSqlFallbackRunnerTests` |
| AI-SEC-026 | HIGH | Done | 审计/日志/state | SQL 原文、用户 prompt、参数值、连接串、敏感字段不得落库或展示；只保存 hash 和安全分类 | `ArchitectureBoundaryTests` + `CloudReadOnlyTextToSqlFallbackRunnerTests` |
| AI-SEC-027 | HIGH | Done | Final answer | 最终回答不得暴露 SQL、表名、视图名、sourceName、endpoint、内部字段 | `AiEvalBehaviorGuardrailTests` + `PromptGovernanceTests` |

实施要点：

- `CloudReadOnlyTextToSqlFallbackRunner` 只能在当前调用内把上一轮失败 SQL 临时回传给 LLM。
- `DataAnalysisFinalContextFormatter` 和 `FinalAgentBuildExecutor` 保持安全上下文，只给最终模型执行事实和脱敏摘要。
- 前端运行详情继续只展示查询次数、返回行数、业务过滤条件和截断状态。

第六批关单证据：

- `CloudReadOnlyLlmTextToSqlGenerator` 的输入只包含 `governedSchema`、`joinHints`、repair hash/length 和输出契约，不带连接串、role、样例数据或任意 schema。
- `CloudReadOnlyTextToSqlFallbackRunner` 只在当前调用内传递 `PreviousSqlForRepair`；成功/失败结果和审计只包含 `QueryHash`、`questionHash`、`sqlHash`、行数、截断状态和 repair 分类。
- `CloudReadOnlyTextToSqlOptions` 默认 repair 3 次、硬上限 5 次，timeout/write SQL 等不可修复错误不重试。
- `ArchitectureBoundaryTests` 禁止 `PreviousSqlForRepair` 进入审计、日志、state、结果或持久化模型，并校验 repair attempt 只保留 SQL hash/length。
- `AiEvalBehaviorGuardrailTests` 构造 SQL、连接串、sourceName、tableName、prompt injection、DeviceLog 内部字段，最终上下文和展示块必须脱敏或移除。
- `PromptGovernanceTests` 固化 chat answer 和 cloud readonly text-to-sql prompt 的只读、governed schema、不得暴露 SQL/数据库名/物理表名约束。

## 9. 第七批：测试、部署 preflight 和发布验收

PR 前必须过：

```bash
rg -n "USER root|CipherMode.CBC|CHANGE_ME|dummy-key|(10\\.[0-9]{1,3}\\.|172\\.(1[6-9]|2[0-9]|3[01])\\.|192\\.168\\.)|Strict-Transport-Security|UseHttpsRedirection|listen 443|ssl_certificate" deploy src docs
bash -n deploy/enterprise-ai/*.sh deploy/enterprise-ai/scripts/*.sh
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "SecurityHardeningTests|SecretStringEncryptorTests|ChatErrorContractTests|CloudAiReadClientTests|CloudReadonlySimulationTests|AiEvalBehaviorGuardrailTests" --no-restore
```

合 main 前必须过：

```bash
dotnet build AICopilot.slnx --no-restore
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
cd src/vues/AICopilot.Web
npm run type-check
npm run test:unit
npm run build
```

发版前必须过：

```powershell
pwsh ../deploy/Deploy.ps1 -Target AICopilot -Doctor
pwsh ../deploy/Deploy.ps1 -Target AICopilot -Services <实际服务列表> -DryRun
```

下面是 AICopilot 仓内只读配置/安全专项诊断，不替代工作区统一入口；从 AICopilot 仓库根执行：

```bash
docker compose --env-file deploy/enterprise-ai/.env.example -f deploy/enterprise-ai/docker-compose.yaml config -q
curl -I http://<intranet-host>:82
curl -I http://<intranet-host>:82/api/identity/cloud-oidc/status
./deploy/enterprise-ai/scripts/check-release-security-attestation.sh --dry-run
./deploy/enterprise-ai/scripts/check-runner-security-attestation.sh --dry-run
./deploy/enterprise-ai/scripts/check-platform-attestation-record.sh --record <filled-runner-platform-attestation.md>
./deploy/enterprise-ai/scripts/check-model-secret-migration.sh --dry-run
./deploy/enterprise-ai/scripts/check-model-provider-openai.sh --base-url http://model.internal.example:40034/v1 --model smoke-model --api-key explicit-test-key --dry-run
```

线上发布后必须确认：

- HTTP 首页 200。
- OIDC 状态接口 200。
- Web 响应头包含 HTTP 兼容安全头，不包含 HSTS。
- `deploy-release.sh` 自动运行 `./scripts/check-release-security-attestation.sh` 并在 release summary 写入结果，覆盖 Web 安全头、Cloud OIDC 状态接口、Web 非 root 和 API key 迁移；必要时手工复跑通过。
- 模型 provider smoke 通过或明确禁用并记录原因。
- CloudReadOnly direct DB 启用时 readonly grant preflight 通过。
- 当前 release summary 写入部署服务、git sha、验证结果和清理结果。

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
