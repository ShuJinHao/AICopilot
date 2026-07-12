# AI 完整修复计划（HTTP-only 修正版）

本文档是 AICopilot 当前安全和架构修复的完整执行计划，也是 AI 端治理清单。长期规则仍以 `AGENTS.md`、`资料/AICopilot业务规则.md`、`AICopilot 项目部署与维护指南.md` 和 `deploy/enterprise-ai/README.md` 为准。

专题契约入口：

- `docs/AICopilot安全部署契约.md`：部署安全、HTTP-only、secret、镜像、SSH、runner 和发布验收。
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

- `AI-SEC-010`：仓库内已固化灾备 workflow 的 least-privilege、self-hosted runner label、非 root runner、hosted-runner 禁用门禁，并让 production/secrets 相关 workflow 执行 `check-runner-security-attestation.sh`；平台侧验收模板和记录 linter 已提供，且 linter 会拒绝模板占位、未勾选项、空签署人和 `pending` / `not implemented` / `N/A` 等弱证明词，并要求记录包含 production environment secret 限制和生产/secret workflow 无 GitHub hosted runner 的证据。GitHub self-hosted runner 机器权限收敛、OIDC/Vault 或等价短期凭据落地仍属于基础设施任务；如果暂未落地，只能在平台记录中写成已批准的基础设施例外，并按结构化字段包含 `Ticket or change id`、`Exception owner`、`Due date` 和 `Current mitigation`，不能伪造成已完成。
- `AI-SEC-012`：旧 `encv1:` 密文迁移代码路径已规划/测试，migration worker 在批量改写前会先预检同一集合中已有 `encv2:` 密文能用当前密钥解密，避免同一集合内混合旧密文和不可读 `encv2:` 时产生内存级部分改写；数据库写入使用 AiGateway 事务，并让临时 `RagDbContext` 通过 `UseTransactionAsync(transaction.GetDbTransaction())` 复用同一事务，避免 `LanguageModel.ApiKey` 与 `EmbeddingModel.ApiKey` 数据库级部分迁移；保存后会自检没有非 `encv2:` 的非空密钥残留，并再次验证 `encv2:` 可解密；发布脚本测试已锁定全量和按需发布中的 `aicopilot-migration -> check_model_secret_migration_preflight -> runtime` 顺序；但真实生产库迁移或管理员重录入仍需要发布窗口验收。

## 1.1 测试架构治理（AI-TEST）

测试架构总纲以 `../../docs/三项目测试架构治理总计划.md` 为专题执行入口。本节只记录 AICopilot 仓内状态，不把 Phase 0 资产冻结冒充物理分类、Roslyn Analyzer 或远端 CI 已完成。

| 编号 | 严重级 | 状态 | 当前结论 | 下一关闭条件 |
| --- | --- | --- | --- | --- |
| `AI-TEST-001` | P0 | Phase0-CI-Green | 唯一 policy/behavior/baseline/waiver、统一 failSkips runner、CODEOWNERS、Release 编译门禁和 25 分钟 `aicopilot-ci/build-test` 已建立；四个旧 xUnit 项目 freeze `All`，Backend 120 个测试源文件与 Vitest/deployment/support/runner 资产已内容冻结。`351334e` 已修复 Linux EXIT trap 恢复与 `tee` 失败传播，canonical run `29174501488` 在 15 分 46 秒通过，Simulation run `29174501490` 的两个 job 同时通过，三个 job annotations 均为 0。当前前端会话权威投影批次把 Vitest 扩展为 31 文件/184 条、Playwright 扩展为 46 runner case，并将治理负例扩展为 65 条；本地已通过但尚未形成新的远端绿提交。 | 当前 UI 批次独立提交/推送并复验 canonical 与 Simulation 后，把 `build-test` 配为 required status context。仓库目前只有 PR 作者本人一个管理员协作者，required Code Owner review 会造成永久自锁；须先加入独立 reviewer，再启用该保护。此条件满足前不得称防自改 hard gate 完成。 |
| `AI-TEST-002` | P0 | Planned | `BackendTests` 仍混装 Unit/Aggregate/Application，且全程序集禁止并行。 | 新建物理项目和 Testing.Core，按 RegressionId 无损迁移；恢复纯测试并行；旧桶对应声明归零后删除。 |
| `AI-TEST-003` | P0 | Planned | Agent workflow、approval、cancel、timeout、compensation 与 Simulation 仍混在 Backend/Phase/Batch；旧 `aicopilot-simulation-release-candidate.yml` 仍在 PR 重复运行 Architecture/Backend/Web，保留 Batch filter 和 Docker-aware skip。 | 盘点远端 required contexts；建立 WorkflowTests 和 deterministic Simulation profile，覆盖 success/empty/reject/transient/permanent/cancel/timeout/compensation/audit；删除 Phase/Batch filter 并将旧 workflow 改为 Manual/Release-only 或退役重复 job。 |
| `AI-TEST-004` | P0 | Planned | Contract、PostgreSQL persistence、Aspire HTTP integration 与少量 E2E 共享同一 Backend assembly/fixture。 | 拆为 Contract/Persistence/HttpIntegration/EndToEnd，并按 Runtime 分 runner；真实依赖缺失时 preflight 失败，禁止运行后 Skip。 |
| `AI-ARCH-001` | P0 | Planned | 现有 91 个 Architecture runner 中仍有大量源码字符串/Regex，只是冻结门禁。 | 建立 Roslyn/MSBuild Analyzer 与 AnalyzerTests；跨层、循环依赖、直接 DbContext/SQL、绕聚合根、插件越界和 Cloud-readonly 写路径以编译 error 阻断。 |
| `AI-EVAL-001` | P0 | Planned | 现有 6 个 JSON case 已冻结并进入 PR，但 approval/prompt-injection 两类仍是输入自证，只能称 legacy eval continuity。 | 接入真实生产 policy/formatter/workflow，建立版本化期望输出、审阅更新流程和 deterministic GoldenEval；真实 provider 另走受控 Release/Manual。 |
| `AI-TEST-DUP-001` | P1 | Planned | 当前只阻止测试声明跨项目精确重复，尚无生产/test fixture/case 的全量 clone ratchet。 | 分生产与测试建立 exact/near clone baseline；PR 阻止新增/扩大，Nightly 输出趋势；例外有 Owner、理由和到期日。 |
| `AI-TEST-UI-001` | P1 | Partial | Vitest 31 文件/184 条已进入 required 候选；Playwright 为 46 runner case、43 passed/3 个既有 viewport 条件 Skip。新增 12 个逻辑场景/24 个 desktop+mobile case，覆盖 clean/stale/有效缓存水合、A→A 成功/失败刷新、A→B 切换、B→C 串行化、SPA 回返、冷启动失败、新建失败和投影失败恢复；Unit 进一步固定 loaded-roster/canonical artifact ownership、任务-工作区-审批原子投影、function/agent approval authority-unknown、DELETE ACK 对账、同会话附件保留、catalog source error 隔离及 SSE 不自动重连。Playwright 仍只在旧 Simulation workflow 执行，不属于 canonical required reconcile。 | 把三个 viewport Skip 改为 desktop/mobile project-specific case，并将 smoke 迁入唯一 required reconcile；完成后 Required Skip=0，再关闭本项。 |

Phase 0 证据边界：本地 policy 已用 65 个负向变异和 4 个正向夹具完成独立复核，包括 dot-directory、Test SDK、MSBuild 大小写/嵌套 props/targets、solution/workflow/SDK 漂移、support host、Vitest/deployment 缩减、package/runner 替换、测试体空心化、跨 OS runner display-name 规范化以及 composer regression/前端结果对账防回退。AI-SEC-051 补强后的第一次完整 Backend 为 898/924、0 Skip，26 个失败统一来自本机缓存的 amd64 RabbitMQ 在 Apple Silicon 上发生 112 秒 AMQP 握手超时，migration/seed 已完成且业务测试体未进入；拉取原生 arm64 同 tag、确认无残留 DCP/容器后，同一 Backend 命令为 924/924、0 Skip、4 分 56 秒，required-result 对账通过。安全批次已在 `88a9687` 提交并由 GitHub run `29170924668` 证明 Architecture 91/91、Backend 924/924、两个 job 成功且 annotations 均为 0。Phase 0 首次 canonical run `29171520017` 在 6 分 08 秒稳定失败：Repository 与 6/91/924/1 数量均正确，但 macOS 生成的 AiEval/Backend display-name digest 在 Ubuntu 漂移，后续真实测试未执行，annotations 为 1 failure + 1 missing-artifact warning；该红测未被重跑掩盖。修复保留精确 digest，只把 xUnit 已截断且位于当前 workspace 根下的绝对路径参数标准化并改用 ordinal 排序；根相对 API、JSON Pointer、协议相对 URL、外部绝对值、相对业务数据和重复 case 仍完整参与 digest。macOS 与独立 Linux 10.0.301/GitHub 同路径重建均对账 6/91/924/1。旧 Windows Simulation job 仍按 OSType 跳过 Linux Docker/PostgreSQL suite，仅执行 12/12 的 skip-profile acceptance，且 npm audit 暴露 1 个 ECharts moderate vulnerability，这两项分别保留为 `AI-TEST-003` 与 `AI-SEC-053`。`351334e` 的 canonical/Simulation 已在 25 分钟预算内全绿；branch protection 和 Code Owner 独立 reviewer 前置仍未完成，同 PR 自改 policy/workflow 的平台风险尚未闭环。

canonical 第二次 run `29172644694` 证明交叉 OS discovery 已闭环：Architecture 91/91、AiEval 6/6、Backend 924/924、Vitest 132/132 和 build 都成功。它同时在 Ubuntu Bash 5.2 暴露了 `EXIT` trap 恢复成功分支的 bare `return` 继承原始 `58` 并转成 `86`，以及 deployment step 的 `| tee` 未开 `pipefail` 导致局部假绿；最终 reconcile 仍正确因 24/33 场景缺失把 job 拉红。修复已用 macOS 和 native Ubuntu 24.04 同一 behavior suite 验证 33/33，并将删除 `pipefail`/删除 stderr 证据纳入两条新负例；远端新提交绿测前状态仍是 `Phase0-CI-Red`，不得提前关单。

canonical 第三次 run `29174501488` 验证上述部署修复已闭环：`build-test` 15 分 46 秒内完成 governance、Release build、repository/discovery、Architecture 91/91、legacy Eval 6/6、Backend 924/924、deployment 33/33、Vitest 132/132、Web build 与 required-result reconcile；Simulation run `29174501490` 的 `simulation-rc` 和 `backend-full-tests` 也通过，三个 check-run annotations 均为 0。当前会话权威投影批次把 Vitest 扩展为 184、Playwright 扩展为 46 runner case；该未提交边界仍需新的远端证据，不能沿用第三次 run 冒充。

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
- AI-SEC-016 provider/workflow/worker 阶段证据：`AgentRuntimeFactory` provider fallback 和 endpoint pool fallback 不再 `LogWarning/LogDebug(ex, ...)`；`AgentTaskRunQueueWorker` iteration、queue item failure 和 heartbeat failure 不再记录原始 exception，worker-level failure message 固定为安全文案；`AgentWorkflowPipeline` branch fallback、`AgentSkillRouterAutoSelector`、`IntentRoutingExecutor`、`KnowledgeRetrievalExecutor`、`ToolsPackExecutor`、`DataAnalysisWidgetEmitter` 和 `FreeFormDbaAnalysisRunner` 的 fallback/error 日志只记录 `ErrorType` / `FailureCode` 与 `OriginalMessage=hidden_by_security_policy`；`SemanticAnalysisRunnerTests` 通过六类 Cloud 错误行为矩阵验证 provider detail 不进入结果，`ProductionLogs_ShouldNotAttachRawExceptionObjects` 继续覆盖其 logger 调用；Cloud AiRead `MissingRequiredParameter` 用户文案不再拼接 `ex.Message`。过期的 Runner `FailureCode` 源码字符串断言已随 Direct DB 分支删除，不以同义字符串门禁替代。
- AI-SEC-016 关单证据：`CloudAiReadHttpTransport`、`DocumentIndexingService`、`UploadDocument`、`McpServerBootstrap`、`McpRuntimeRegistrySynchronizer`、`McpServerManager`、`OutboxDispatcher`、`LanguageModelConnectivityTester`、`PostgreSqlSessionExecutionLock`、`TelemetryBehaviors`、`SqlAllowlistColumnInspector`、`SemanticQueryPlanner`、`ToolInputSchemaValidator`、`AgentDynamicPlanner`、`PlannerToolCatalog`、`AgentDynamicPlannerResponseParser`、`AstSqlGuardrail` 和 `AgentTaskRuntime` 不再把 raw exception 对象或 exception message 直接写入日志、接口响应、任务失败摘要或持久化失败原因；`SecurityHardeningTests.ProductionLogs_ShouldNotAttachRawExceptionObjects` 全量扫描 `src/hosts`、`src/infrastructure`、`src/services`、`src/core`、`src/shared` 禁止 `LogCritical/LogError/LogWarning/LogInformation/LogDebug/LogTrace` 以变量作为首参，从而覆盖 `ex`、`exception`、`e`、`cleanupException` 等异常变量名；`ErrorBoundaryMessages_ShouldNotReturnOrPersistRawExceptionMessages` 点名锁定已修错误边界。剩余 `ex.Message` 仅作为 `DataAnalysisToolResultFormatter`、`CloudReadOnlyTextToSqlRepairClassifier` 和 artifact finalized 识别的内部分类输入，输出仍是固定安全文案或 hash/code。
- AI-SEC-018 关单证据：`MessageRuntimeDetailsPanel` 使用无 `open` 的原生 `<details>`，smoke 验收断言“结构化展示”默认隐藏，点击 summary 后才可见；`runtimeDetails.ts` 对工具参数、工具结果、Widget 摘要、运行状态 summary/error 只输出安全摘要；`runtimeDetails.spec.ts` 覆盖 SQL、连接串、password、token、endpoint、sourceName、tableName、databaseName、内部路径和原始结果行不进入运行详情对象。
- AI-SEC-017 关单证据：`chatErrorStore` 统一解析 ProblemDetails `userFacingMessage` / validation errors / `detail` / `title`、已知 code 和安全兜底，SSE open/error 与普通 API 不再丢后端安全诊断；未知 Chat Error code 仍不直接展示 raw `detail`；AgentEvent、ApprovalRequest、AgentTask 和 Chat Error chunk 失败进入会话错误栏；agent catalog、task timeline、artifact preview/download/upload 和 ConfigView agent settings 刷新失败进入当前会话或页面错误栏；auth current-user 和 Cloud OIDC status 失败进入登录错误栏；RAG、Config CRUD、Access action 失败进入页面 `errorMessage`、`actionErrors` 或对应 dialog error；纯解析 fallback（metadata/widget/runtime details/chat run status）只降级展示或记录安全摘要，不是用户操作失败路径；chunk reload guard 的 storage catch 只保护 stale bundle 自动刷新；`chatErrorStore.spec.ts` 覆盖 ProblemDetails `detail/title` 解析，`frontendErrorHandling.spec.ts` 和 `rg -n "catch\\s*\\{" src/vues/AICopilot.Web/src` 固化无裸 catch。

## 7. 第五批：Cloud 只读边界和生产数据路径

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-019 | CRITICAL | Done | Cloud 只读 | AI 只能读 Cloud；禁止 MCP、Tool、Agent workflow、后台任务或隐藏 adapter 写 Cloud | `ArchitectureBoundaryTests` + `CloudReadonlyChatBoundaryTests` |
| AI-SEC-020 | CRITICAL | Done | Simulation | 生产路径 Cloud 查询失败、为空或未配置时不得 fallback 到 Simulation；Real provider 失败必须返回 Cloud AiRead 错误；生产基础配置和 compose 不携带 `MockOnly=true` | `CloudReadonlySimulationTests` + `SecurityHardeningTests.DeploymentConfig_ShouldNotCarryKnownWeakSecrets` |
| AI-SEC-021 | HIGH | Done | Cloud AiRead | Device、DeviceLog、Capacity、ProductionData、Process、ClientRelease 六类正式语义只能走 Cloud AiRead 正式只读 API | `CloudAiReadClientTests` + `SemanticAnalysisRunnerTests` |
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
- `ArchitectureBoundaryTests` 固化 AICopilot 不直接引用 Cloud 项目/命名空间、Cloud write tools 不纳入范围、CloudReadOnly direct DB 只读 guard、governed schema、只读账号 grant preflight，并用构造反射锁定正式语义执行器只依赖 Cloud AiRead、planner 和 logger。
- `CloudReadonlyChatBoundaryTests` 阻断 Cloud 业务修改、禁用设备、补录产能、删除日志和上传生产数据等写语义。
- `CloudReadonlySimulationTests` 固化 Simulation 只能在 Development 使用，Real 模式必须双开 `CloudReadonly:Real` 和 `CloudAiRead`，Cloud AiRead 不可用时不得降级返回 Simulation。
- `CloudAiReadClientTests` 固化 `/api/v1/ai/read/devices`、`processes`、`client-releases`、`device-client-states`、`device-logs`、`capacity/summary`、`capacity/hourly`、`production-records` 端点和参数契约；`deviceCode` 只有在未截断搜索结果中唯一精确匹配时才能解析成 `deviceId`，设备状态随后只向 `/device-client-states` 发送正式 `deviceId`，不得把 `deviceCode` 或整句自然语言降级为 keyword。
- `SemanticAnalysisRunnerTests` 以唯一六类目标数据源覆盖 Cloud 合法空集、规划失败、关闭和错误都不 fallback，并单独证明 Recipe 在 planner 前拒绝；Direct DB / Text-to-SQL 只保留在正式语义执行器之外的治理白名单补充分析。
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
| AI-SEC-044 | HIGH | Done | SemanticAnalysisRunner | Recipe 必须在 planner 前拒绝；六类正式语义在成功、空集、规划失败、关闭和 Cloud 错误时只走 Cloud AiRead 且不 fallback；物理删除 Runner 内不可达 Direct DB/SQL/fallback 分支和专用测试桩 | `SemanticAnalysisRunnerTests` 七目标边界与六类矩阵 + ArchitectureTests 构造反射 + 全量 BackendTests/solution build |

本批没有重命名或迁移既有 HTTP route、配置键、physical mapping / semantic source status 运维诊断及其消费者，也没有删除 `SemanticSqlGenerator` 独立实现和测试；这些表面不属于正式语义执行器，后续若治理必须重新做生产与测试消费者审计，不能借本批 no-fallback 收口扩大删除范围。

### 7.5 2026-07-11 DataAnalysis 最终上下文字段边界

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-045 | HIGH | Done | DataAnalysis final context | metadata/preview 共用唯一字段标签映射；内部与 governed-schema 敏感 raw field 整项丢弃；标签指令、控制字符、超长和重名只在唯一入口收口；flat preview 只输出显式标量白名单，任意其他 object/collection 不展开也不调用自定义 `ToString()` | `DataAnalysisFinalContextFormatterTests` + `AiEvalBehaviorGuardrailTests` + `CloudReadOnlyTextToSqlFallbackRunnerTests` + Semantic/Widget/compact 定向回归 + ArchitectureTests/BackendTests/solution build |

本批固定口径：formatter 不再分别解析 metadata 名、preview key 和逐行重名；它先单次收集最多 3 个可识别 dictionary row，再根据 metadata、schema 顺序和实际 row key 产生一份 `OrdinalIgnoreCase` 映射。字段敏感判定复用 `CloudReadOnlyGovernedSchema.BlockedFieldFragments`，值仍只走既有 `SanitizeValue/SanitizeTextValue` 链，没有第二份 blacklist 或递归 nested sanitizer。Semantic/FreeForm Widget 不消费 formatter label map，本批不修改 Widget、route、Cloud API、数据库或部署链。

### 7.6 2026-07-11 Outbox 死语义删除与事务重试强制后续

| 编号 | 严重级 | 状态 | 范围 | 修复要求 | 验收 |
| --- | --- | --- | --- | --- | --- |
| AI-SEC-046 | HIGH | Done | DbContext / Outbox / retry | `AI-PERSIST-01a` 已删除无生产者扫描与重复 Outbox ownership；01b 已把普通 repository、独立 audit writer、AiGateway `Session` events 和 RAG delayed factories 收口到唯一 `PersistenceCommitEngine` / `RepositoryPersistenceCommitter`；01c 删除 `EfTransactionalExecutionService`，让 Identity 复用同一 engine，非成功 `Result` 回滚全部中间保存并在回滚后独立提交拒绝审计。RAG `UploadDocument` 与 active AiGateway `SessionTemp` / `AgentInput` 上传先写 durable journal、再写物理文件并复用 commit id；请求、RAG 删除 consumer 与 DataWorker 使用同 commit PostgreSQL advisory lease，删除事件必须先安全退休 journal 再删文件。DataWorker 在固定共享卷按 marker 对账、局部隔离失败项并按 retention 分批清 marker。旧原始 `IFileStorageService.SaveAsync`、重复文件测试替身和重复 Identity 源码门禁已物理删除 | 真实 PostgreSQL pre-commit retry、Identity rejected-result/UserManager rollback、commit-ACK lost、fresh verification、UploadRecord/RAG 文件未知结果、unknown 后删除事件、活跃 lease、durable absence、损坏日志/缺失文件 fail-closed、marker retention/index、migration ownership/safety + 全量与 deployment/baseline/scope gates |
| AI-SEC-047 | HIGH | Partial | ArtifactWorkspace 文件集/数据库 | `CreateWorkspaceAsync`、draft 文本/二进制写入、版本归档、当前文件覆盖和 `final/` 复制现在仍可于 repository 提交前改动文件系统。`AI-PERSIST-01d` 必须建立多文件 change-set journal、覆盖前备份/原子替换、同 commit id marker、commit-unknown 对账和孤儿目录清理；不得硬套单文件上传 stage 或宣称 01c 已覆盖 | 覆盖当前文件、归档两文件、final 多文件复制、workspace 初始化、DB 失败/ACK lost/进程中断/后台对账的真实文件系统 + PostgreSQL 行为测试 |
| AI-SEC-048 | HIGH | Partial | KnowledgeBase shadow upload 删除 | 生产消费者审计确认 KB 唯一真实入口是 `/api/rag/document`；AiGateway shadow record 不可按 KB 查询且无可读文件。本批已停止新写：API/前端只允许 SessionTemp/AgentInput，删除 RAG bridge/DI，领域和查询 spec 拒绝 KB scope。`AI-PERSIST-01e` 不再建设 saga，而是在单独维护窗口盘点/导出历史 shadow 行，检查 RAG hash、旧任务引用和异常列值，随后删除 shadow 行、索引、`knowledge_base_id` / `rag_document_id`，并增加 active scope 与目标二选一数据库约束；status 列是否删除由历史值盘点决定 | 止写端点/领域/查询负向测试 + 生产树 bridge 零引用 + 前端 type/unit/build；后续真实 PostgreSQL upgrade 测试证明只删 shadow、保留 Session/Agent 行、列/索引/check/not-null 正确，维护窗口 drain 旧 HttpApi 后再执行 migration |
| AI-SEC-049 | MEDIUM | Partial | 上传安全策略技术探测去重 | AiGateway 与 RAG 必须保留各自业务 allowlist、限额和拒绝语义，但当前仍重复 seekable stream 归一化、header 读取、MZ/文本 NUL 探测和 content-type 技术判断。后续治理只抽取一个窄的字节探测/stream ownership helper，明确临时 MemoryStream 的释放责任；禁止合并两套业务政策、复制第三份规则表或用万能 upload framework 隐藏差异 | 等价矩阵覆盖 seekable/non-seek、短流、MZ、NUL、空 content-type、调用方/被调用方 dispose ownership；两套 policy 行为不变，重复技术实现归零，Architecture/Backend 全量通过 |
| AI-SEC-050 | HIGH | Partial | RAG 同文件并发幂等 | `UploadDocument` 当前 hash 预查只覆盖顺序请求，数据库没有 `(knowledge_base_id, file_hash)` 唯一约束，两个并发请求可各自读到不存在并提交两条 Document/两份文件。治理前先盘点既有重复 hash、chunk/审计/事件引用并确定 canonical document，再增加数据库唯一约束；唯一冲突必须安全返回既有 Document 或稳定冲突，并让失败请求通过既有 file journal 回滚自己的文件。禁止把应用层 `Any`/集合查重称为并发幂等 | 真实 PostgreSQL barrier 并发上传同 KB/同 hash，最终一条 Document/一份有效文件；不同 KB 可各自保存；唯一冲突、ACK unknown、既有重复数据 migration、Outbox/chunk/audit 保留均有行为测试 |
| AI-SEC-051 | HIGH | Done | 至少一个可用管理员 | `DisableUser`、`UpdateUserRole` 和 migration seed 已在唯一 Identity transaction 内先取得固定全局 `pg_advisory_xact_lock`，再读取用户、角色和 enabled Admin；多角色 Admin 按真实 membership 判断，拒绝业务事务先回滚，再独立提交 Rejected audit；disabled bootstrap 不会被 seed 偷偷启用，最终零 enabled Admin 会明确失败。execution-strategy transient retry 已用竞争事务证明第二 attempt 会重新加锁并重读新状态。当前生产树没有 DeleteUser API，架构门禁冻结现有三条减员表面；未来新增删除或其它减员入口必须同时进入 `AI-ARCH-001` Analyzer 和真实并发测试，不能制造不存在的兼容 API | 本地真实 PostgreSQL/HTTP/全量矩阵通过；独立提交 `88a9687` 已推送 Draft PR #59，GitHub run `29170924668` 的 Architecture 91/91、Backend 924/924、前端与 acceptance 检查通过，两个 job annotations 均为 0。未 merge、未部署 |
| AI-SEC-052 | HIGH | Partial | Admin 恢复权限基线 | `UpdateRole` 当前可把 Admin 角色的关键身份恢复/权限治理能力全部移除；enabled Admin 数量虽然仍大于零，但可能形成“仍叫 Admin、却无法恢复治理能力”的伪可用管理员。该问题与 AI-SEC-051 的数量不变量分开治理 | 先定义不可移除的 Admin 最小恢复权限集合；更新 Admin 时缺少任一基线权限必须稳定拒绝并写审计；普通角色仍允许差量权限；API、真实数据库行为和 Analyzer/Contract 正反例均通过 |
| AI-SEC-053 | MEDIUM | Partial | ECharts 依赖 XSS 公告 | PR run `29170924668` 的前端 audit 报告 `echarts 6.0.0` 命中 `GHSA-fgmj-fm8m-jvvx`；当前门禁只拒绝 high/critical，因此 job 成功不代表该中危漏洞不存在。GitHub advisory 指向 `<6.1.0` 受影响、`6.1.0` 首次修复 | 独立前端依赖批次升级到 `>=6.1.0`，运行 type-check、184 条 Vitest、build、图表组件/数据编码回归与 `npm audit`；确认 lockfile 只包含已修版本后关单，不在测试治理批次顺手升级 |
| AI-SEC-054 | MEDIUM | Partial | Web mutation ACK-unknown / 幂等回放 | 本批已为普通请求设置显式 timeout、禁止非幂等 SSE 自动 reconnect，并对 DELETE session 用权威列表对账；但 create session、upload、Chat/Plan/approval SSE 在服务端已提交而响应/首个权威 chunk 丢失时，仍缺 client operation id、数据库唯一 receipt 和结果回放。超时只能标记 outcome unknown，不能推断“未创建/未执行”或自动重放 | 为每类 mutation 定义稳定 `clientOperationId`，服务端以用户+操作类型+key 唯一保存 receipt/result，重试返回同一结果；覆盖 commit 后断链、chunk 前断链、重复点击、进程重启、跨实例和 receipt retention 的 Contract/Workflow/Persistence 测试，再允许安全重试 |

`AI-PERSIST-01b/01c` 固定口径：业务代码不写 retry loop；每个 attempt 对业务 Context 只调用一次 `SaveChangesAsync(false)`，Outbox/audit/marker 使用同一 PostgreSQL transaction，commit 确认后才 accept/clear。Identity Result 写命令也复用该 engine，失败 Result 不能提交 UserManager/RoleManager 中间保存。COMMIT ACK 丢失由 fresh context 验证 marker；无法确认时返回 `persistence_commit_outcome_unknown` 且不自动重放。RAG `UploadDocument` 与 active SessionTemp/AgentInput `UploadRecord` 单文件上传必须先写 journal、再写文件并复用 commit id，DataWorker 取得同一 advisory lease 后按 marker 对账；损坏 journal、已提交但缺失文件都保留证据并停止危险清理。`AuditTransactionCoordinator`、`RagIntegrationEventStager`、`EfTransactionalExecutionService`、原始上传写 API、KB RAG bridge、AiGateway/RAG Outbox mapping 和重复 PostgreSQL/文件测试资产均已物理删除。`AI-SEC-046` 只对事务 engine、Identity 和上述 active 单 Context 上传边界关单；`ArtifactWorkspace` 为 `AI-SEC-047 Partial`，历史 KB shadow 清库为 `AI-SEC-048 Partial`，本状态不代表已合并或生产部署。

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
