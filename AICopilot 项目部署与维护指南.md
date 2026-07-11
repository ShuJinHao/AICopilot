# AICopilot 部署与维护指南

本文档是 AICopilot 当前项目级部署说明。长期业务边界见 `AGENTS.md` 和 `资料/AICopilot业务规则.md`；历史阶段计划和验收报告不再作为执行入口。日常自动增量入口是工作区根 `deploy/Deploy-Changed.ps1`；`Deploy.ps1` 只作为显式服务执行器和恢复入口，`deploy/Invoke-WorkspaceDeploy.ps1` 只保留给部署基础设施维护和旧事务诊断。

> 当前状态（2026-07-10）：可信工作站日常链路已通过固定远端 SHA、脏本地工作树、fake Docker/SSH、一次 SSH、migration 备份、失败回滚和无重建续传回归。尚未执行真实 Harbor/SSH/生产容器 E2E，因此当前不得把本文目标链路解释为生产部署已验收。

工作区标准入口示例：

```powershell
pwsh ./deploy/Deploy.ps1 -Target AICopilot -InstallRunner # 仅首次或 Runner 升级
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Doctor
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Services httpapi,web -DryRun
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Services httpapi,web -Deploy
pwsh ./deploy/Deploy-Changed.ps1 -Targets AICopilot
```

本文档按双层口径维护：

- 长期模板/规则：描述 Harbor/SSH/non-root/HTTP-only/Cloud 只读边界，不写真实 secret。
- 当前生产现场口径：当前标准部署根目录是 `/srv/enterprise-ai/deploy`，稳定 Runner 是 `runner/iiot-release-runner.sh`，Runner work root 是 `/data/iiot-platform/runners/aicopilot`，Docker Root Dir 是 `/data/iiot-platform/runtime/docker`（与 Cloud 共用同一 Docker daemon）；`releases/routine-*`、备份和标准 non-root 发布路径必须保持一致。旧 support 目录只在基础设施维护时检查。
- 当前与 Cloud 共用同一台生产宿主机，但部署根独立；共享宿主机事实、当前标准发布账号和 Cloud 根目录统一以工作区 `../docs/上传部署总览.md` 为准。AICopilot 当前未因同类权限问题失败，但必须和 Cloud 共享相同的 non-root release-state / support-files 门禁原则。

## 1. 部署口径

- 当前部署目录固定为 `deploy/enterprise-ai`。
- `deploy/enterprise-ai/README.md` 是部署目录内的自解释实现入口；新 AI 接手标准路径时先读工作区 `deploy/README.md` 和 `deploy/Deploy.ps1`，再按需下钻到该文件。
- 多 AI 可以并行准备候选，但每次运行必须使用固定 SHA 和私有 manifest；远端 support install、release、容器变更和 cleanup 由同一 token/digest 与全局锁串行化，active lock 必须立即失败。
- 生产环境使用 Docker Compose 单机编排，镜像从 Harbor 拉取。
- 标准日常发布走工作区 `deploy/Deploy-Changed.ps1`：要求 clean/main，自动把已提交 HEAD push 到 `origin/main`，读取生产 SHA，按 Git 改动和项目依赖闭包只选择受影响镜像，再调用 `Deploy.ps1 -Services ...` 构建不可变镜像并请求稳定 non-root Runner。影响无法安全归属禁止退化全量。`RemoteTransport=Auto` 优先 SSH，SSH TCP 不可达时自动使用 `aicopilot-routine-request.yml` self-hosted Runner。
- 后端应用服务自动包含 migration；Runner 先备份 PostgreSQL，再迁移并用 `--no-deps` 更新选中应用，失败恢复旧应用镜像。构建后远端失败使用 `-ResumeInvocation` 续传，不重新构建。
- Compose、Runner、scripts/cloud-readonly、cleanup/GC 和深度 attestation 属于独立基础设施维护，不随日常应用发布同步。
- GitHub `aicopilot-image` / `aicopilot-deploy` 只保留带确认词的灾备入口；日常生产发布不得等待这些 workflow。
- 单个镜像 build/push 默认 15 分钟超时，Harbor 登录/API 检查默认 2 分钟超时，SSH deploy 默认 30 分钟超时；超时必须停止并按脚本输出诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续 watch 或无限等待。
- 灾备 runner 必须使用专用非 root 用户运行，例如 `github-runner`，并带 `iiot-linux-prod` label；不要把 runner 装成 root 服务。production/secrets 相关灾备 workflow 和 runner 机器侧都必须执行 `deploy/enterprise-ai/scripts/check-runner-security-attestation.sh`，验收非 root、工作目录、Docker Root Dir 和部署目录。
- 灾备 workflow 只能使用最小 GitHub 权限和生产环境 secrets；这不是 OIDC/Vault 已完成的证明。runner 机器权限收敛、短期凭据或 Vault/OIDC 接入必须作为独立基础设施任务验收；runner 脚本只证明本机事实，不证明 GitHub environment secrets 或 Vault/OIDC 已完成。平台侧验收使用 `deploy/enterprise-ai/runner-platform-attestation.template.md` 复制填写，再用 `deploy/enterprise-ai/scripts/check-platform-attestation-record.sh --record <filled-attestation.md>` 校验记录完整性；该记录校验不替代真实 GitHub/Vault/OIDC/runner 检查。
- 当前服务器 runner 工作目录固定为 `/data/iiot-platform/runners/aicopilot`，Docker Root Dir 固定为 `/data/iiot-platform/runtime/docker`，不要把构建缓存放回系统盘。
- 当前标准 non-root 发布还要求 `releases/current-release*`、`staged-release*`、`previous-release*`、`current-release.summary.md` 和 deploy support files 对标准部署用户可读可写；root 应急路径一旦写入这些状态，关闭任务前必须恢复 owner/mode 并重新验证 `--validate-only`。
- AICopilot 应用镜像不保留历史版本；Harbor 和服务器本机只保留当前生产正在运行的 `sha-*` 应用镜像。
- 当前内网环境 Git smart HTTP 可能超时，旧 workflow 使用 GitHub archive/codeload 兜底拉取源码；这些 workflow 仅用于灾备，不作为日常发布入口。
- 日常本地构建 + SSH 标准链使用服务器预置且 mode `0600` 的真实 `.env`；support sync 不上传或覆盖 `.env`。GitHub secret `DEPLOY_ENV_FILE` 只用于灾备 workflow，不提交真实密钥。
- 当前内网生产部署红线是 HTTP-only。AICopilot 当前修复和发布不得强制引入 HTTPS redirection、HSTS、nginx 443 listener、证书申请/续期或 OIDC HTTPS metadata 校验；如果未来要切 HTTPS，必须由用户单独批准传输层方案和证书来源。HTTP 部署下仍必须执行内网隔离、端口收敛、同源代理、CORS 白名单、强 secret、短期 token、非 root 容器、只读边界和除 HSTS 外的安全响应头。
- AICopilot 的“慢”不得被误写成 HTTP 上传限速问题。日常真实慢路径是选中镜像 build/Harbor push、migration 和 health；support sync、深度 attestation 和 cleanup 已拆出。
- Docker Hub 不作为生产依赖源，MCR 也不得作为生产构建的直接依赖源；PostgreSQL、RabbitMQ、Qdrant、.NET ASP.NET runtime、Node、Nginx 基础镜像必须先 mirror 到 Harbor。
- AICopilot 默认保持 Cloud 只读边界，不能注册、修改、删除或触发 Cloud 业务数据。
- Cloud OIDC 只用于身份对齐；AICopilot 保留本地 AI 用户、AI 角色、AI 权限、审计和 emergency admin。

## 2. 镜像和服务器目录

部署包至少包含：

```text
deploy/enterprise-ai/
  .env.example
  build-and-push.sh
  local-release.sh
  deploy-release.sh
  docker-compose.yaml
  mirror-base-images.sh
  runner-platform-attestation.template.md
  cloud-readonly/
  scripts/apply-cloud-readonly-grants.sh
  scripts/check-cloud-readonly-grants.sh
  scripts/check-release-state-access.sh
  scripts/check-platform-attestation-record.sh
```

服务器建议目录：

```text
/srv/enterprise-ai/deploy
```

真实生产主机、Harbor 和 Cloud 地址只写入服务器 `.env`、GitHub secret 或发布命令环境变量；本指南和 `.env.example` 只使用 `*.internal.example` 占位，避免把现场 IP 固化到仓库入口。

真实 `.env` 从 `deploy/enterprise-ai/.env.example` 复制后替换密钥和镜像 tag，并直接保存到服务器 `/srv/enterprise-ai/deploy/.env`。GitHub secret `DEPLOY_ENV_FILE` 只服务灾备 workflow，不作为日常标准发布前置条件。

对外标准环境校验必须从工作区根运行 `pwsh ./deploy/Deploy.ps1 -Target AICopilot -Doctor`。获批旧事务排障时才允许在服务器部署目录内部运行 `./deploy-release.sh --validate-only`；该内部命令不是第二套 AI 标准入口。

## 3. 关键环境变量

入口和镜像：

```text
COMPOSE_PROJECT_NAME=enterprise-ai
AICOPILOT_PUBLIC_URL=http://aicopilot.internal.example:82
CLOUD_PLATFORM_URL=http://cloud.internal.example:81
AICOPILOT_WEB_PORT=82
AICOPILOT_HTTPAPI_IMAGE=harbor.internal.example:80/enterprise-ai/aicopilot-httpapi:sha-replace-with-release-tag
AICOPILOT_MIGRATION_IMAGE=harbor.internal.example:80/enterprise-ai/aicopilot-migration:sha-replace-with-release-tag
AICOPILOT_DATAWORKER_IMAGE=harbor.internal.example:80/enterprise-ai/aicopilot-dataworker:sha-replace-with-release-tag
AICOPILOT_RAGWORKER_IMAGE=harbor.internal.example:80/enterprise-ai/aicopilot-ragworker:sha-replace-with-release-tag
AICOPILOT_WEBUI_IMAGE=harbor.internal.example:80/enterprise-ai/aicopilot-webui:sha-replace-with-release-tag
POSTGRES_IMAGE=harbor.internal.example:80/enterprise-ai/base-postgres:17.6
RABBITMQ_IMAGE=harbor.internal.example:80/enterprise-ai/base-rabbitmq:4.2-management
QDRANT_IMAGE=harbor.internal.example:80/enterprise-ai/base-qdrant:v1.15.5
```

必须在服务器替换的密钥：

```text
POSTGRES_PASSWORD
RABBITMQ_PASSWORD
QDRANT_KEY
AICOPILOT_BOOTSTRAP_ADMIN_PASSWORD
AICOPILOT_API_KEY_ENCRYPTION_KEY
AICOPILOT_JWT_SECRET_KEY
CLOUD_AI_SERVICE_ACCOUNT_TOKEN
```

Cloud 只读和 OIDC 默认：

```text
CLOUD_READONLY_MODE=Disabled
CLOUD_AI_READ_ENABLED=false
CLOUD_OIDC_ENABLED=true
CLOUD_OIDC_ISSUER=http://cloud.internal.example:81
ALLOW_INTRANET_HTTP_OIDC=true
CLOUD_OIDC_CLIENT_ID=aicopilot
CLOUD_OIDC_REQUIRE_HTTPS_METADATA=false
CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED=false
```

生产模板和 compose fallback 默认使用 `CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED=false`。只有在已核对目标本地 emergency Admin 与 Cloud `employee_no` 的短时首部署窗口中才能显式改为 `true`；它只允许该本地 Admin 在无既有 Cloud 绑定时被收编，绑定完成后必须立即恢复 `false`。普通同名用户和已绑定后 sub 漂移仍拒绝，Cloud role 不映射为 AI role。

开发期收口要求：生产 Cloud 只读读取不再通过普通 Real CloudReadonly 双轨入口推进；真实 Cloud 读取必须走当前批准的 Cloud AiRead / P12 / P13 受控入口。`CLOUD_AI_SERVICE_ACCOUNT_TOKEN` 只有在 Cloud 明确发放 AI 只读服务账号 token 后才填写；不能写入仓库。

Cloud AiRead 受控读取启用时，必须同时满足：

```text
CLOUD_AI_READ_ENABLED=true
CLOUD_AI_READ_BASE_URL=<Cloud Gateway URL>
CLOUD_AI_SERVICE_ACCOUNT_TOKEN=<Cloud 签发的 AI 只读服务账号 JWT>
```

Cloud AiRead 契约：

- 设备列表：`GET /api/v1/ai/read/devices`，参数为 `maxRows` 和可选 `keyword`。
- 产能摘要：`GET /api/v1/ai/read/capacity/summary`，参数为 `deviceId`、`startDate`、`endDate`、`maxRows`。
- 小时产能：`GET /api/v1/ai/read/capacity/hourly`，参数为 `deviceId`、`date` 或 `preset`、可选 `plcName`、`maxRows`。
- 设备日志：`GET /api/v1/ai/read/device-logs`，参数为 `deviceId`、`startTime`/`endTime` 或 `preset`、可选 `level` 或 `minLevel`、可选 `keyword`、`maxRows`。
- 生产记录：`GET /api/v1/ai/read/production-records`，参数为 `typeKey`/`processId`/`deviceId` 至少一个、`startTime`/`endTime` 或 `preset`、可选 `barcode`、`result`、`fieldMode`、`maxRows`；新工序字段通过返回的 `fieldSchema`/`fields` 通用加载。
- `deviceCode` 只能用于设备查询/解析，无法唯一命中时不得继续读取业务数据。
- P12/P13 的 `scenarioId`、`from`、`to`、`boundary`、`intentId`、`goalHash`、`analysisType`、`pilotWindowId` 等只允许留在 AICopilot 内部审计，不得作为 Cloud query 参数。
- AICopilot 不读取未批准的配方主数据、配方详情或配方版本。
- Simulation 只能用于联调和演示，不能作为生产验收结果。

内部开发低频探索验证也允许走 DataAnalysis direct Cloud readonly DB：只读连接串放在 GitHub production environment
secret `DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING`，本地可用
`scripts/Set-AICopilotCloudReadOnlyDbSecret.sh` 写入并触发
`aicopilot-enable-direct-cloud-readonly-db`。该路径只注册 AICopilot DataAnalysis
`CloudReadOnly` 数据源，必须使用已验证只读账号，不直连写 Cloud 业务数据。
Cloud Postgres 不发布宿主 5432；AICopilot 部署会创建外部 Docker 网络
`enterprise-ai-cloud-readonly`，把 Cloud compose 的 `deploy/postgres` 容器接入并设置别名
`cloud-postgres`。只读连接串推荐写：
`Host=cloud-postgres;Port=5432;Database=iiot-db;Username=<readonly_user>;Password=<readonly_password>`。
Cloud PostgreSQL readonly role 的授权权威载体是
`deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql` 和
`deploy/enterprise-ai/cloud-readonly/check-readonly-grants.sql`。它们只对
`devices`、`mfg_processes`、`device_logs`、`hourly_capacity`、`pass_station_records`
做显式表级 `GRANT SELECT`，并校验写权限、schema create 权限均不存在；不得改成
`GRANT SELECT ON ALL TABLES`、默认权限、未来表自动授权或列级/表级混用口径。
如果还没有只读账号，标准做法是在可访问服务器和 Cloud PostgreSQL 容器的机器上运行
`deploy/enterprise-ai/scripts/apply-cloud-readonly-grants.sh`，随后用
`deploy/enterprise-ai/scripts/check-cloud-readonly-grants.sh` 验证。历史
`scripts/Provision-AICopilotCloudReadOnlyDbRole.sh` 和
`aicopilot-provision-cloud-readonly-db-role` workflow 只保留为带确认词的手动兜底，
且必须读取同一组 `cloud-readonly/*.sql`，不得再维护内联 GRANT 清单。

启用 direct DB 后，服务器 `deploy-release.sh` 会在重启服务前自动执行
`scripts/check-cloud-readonly-grants.sh`；preflight 失败必须停止部署并先修 readonly
授权，不允许把权限缺口伪装成“数据源暂时不可用”继续发布。

服务器到私有模型 API 的连通性必须用 `deploy/enterprise-ai/scripts/check-model-provider-openai.sh`
独立验证；该脚本直接 POST OpenAI-compatible `/chat/completions`，不经过
AICopilot 应用层。模型 smoke endpoint、model 和 API key 必须由服务器真实 `.env`
或命令参数显式提供；如果当前私有模型网关允许 dummy key，也只能写在真实 `.env`，
不能作为仓库默认值，并必须同时设置 `AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY=true`
或在手工 smoke 命令中传 `--allow-dummy-key`。生产 `.env` 设置
`AICOPILOT_MODEL_SMOKE_ENABLED=true` 后，`deploy-release.sh` 会把模型 smoke 作为
发布前 preflight；失败时先修服务器到模型端点的网络、端口或模型服务。

生产清空重部署的新库会由 migration worker 播种一个私有 OpenAI-compatible 模型。仓库模板只保留占位 URL 和禁用状态，真实值必须写在服务器 `.env` 或本机非 git 私密手册中：

```dotenv
AICOPILOT_PRIVATE_MODEL_ENABLED=true
AICOPILOT_PRIVATE_MODEL_PROVIDER=MiniMax Private
AICOPILOT_PRIVATE_MODEL_NAME=MiniMax-M3-AWQ-INT4
AICOPILOT_PRIVATE_MODEL_BASE_URL=<private-model-base-url>/v1
AICOPILOT_PRIVATE_MODEL_API_KEY=<private-model-api-key>
AICOPILOT_PRIVATE_MODEL_CONTEXT_TOKENS=65536
AICOPILOT_PRIVATE_MODEL_MAX_OUTPUT_TOKENS=4096
AICOPILOT_PRIVATE_MODEL_TEMPERATURE=0.2
```

`AICOPILOT_PRIVATE_MODEL_API_KEY` 即使模型网关允许任意值，也必须显式写入真实 `.env` 并由 seed 加密入库，不能提交到仓库默认值。已存在同 provider/model 的模型记录时，migration worker 只修复密钥保护格式，不强行覆盖现场 URL、启用状态或参数。

AiGateway 会话并发锁：

- 生产组合根必须使用 PostgreSQL advisory `ISessionExecutionLock`，依赖 `ConnectionStrings:ai-copilot`。
- `InMemorySessionExecutionLock` 只允许作为服务层测试/本地 fallback，不得成为生产或多实例部署的实际锁实现。
- 多实例部署前必须先验证每个 `AICOPILOT_HTTPAPI_IMAGE` 实例都走 PostgreSQL advisory lock；如果后续改为其他分布式锁，必须同步更新部署文档和并发验收用例。
- 同一 session 的并发 Chat、Plan、Approval 请求必须被串行化或返回明确锁错误，不允许并发执行同一会话工作流。

## 4. 构建与发布

目标标准流程（不代表当前生产验收）：

```text
git push GitHub
-> 工作区根 Invoke-WorkspaceDeploy.ps1 确认 HEAD 已推送且工作区干净
-> local-release.sh 为该 SHA 创建 detached worktree 和本次 run 私有目录
-> 从固定快照构建镜像并生成私有 services/image/support manifest
-> push <harbor-registry>/enterprise-ai/*:sha-<git-sha>
-> support staging 的 SHA256、reservation token、全局锁和 deploy-release digest 绑定
-> 统一入口通过 SSH 调用 /srv/enterprise-ai/deploy/deploy-release.sh
-> 服务器 pull/migration/model-secret-preflight/up/health/release-security-attestation/cleanup/history
```

`build-and-push.sh` 必须显式接收 `--services httpapi,migration,dataworker,ragworker,web` 的子集或 `--all`，但只由统一入口内部调度；无参数直接失败。选择 `httpapi`、`dataworker` 或 `ragworker` 时会自动加入 `migration`。正式发布的 `Deploy services input`、image manifest 和 support manifest 只写本次 run 私有目录，`local-release.sh` 只读取同一次运行的清单；不得再用共享 `artifacts/deploy/aicopilot-built-services.txt` 控制发布。

### 4.1 不可变候选、幂等与恢复

- 正式发布必须先由工作区入口 `CheckCandidate` 生成只读 plan，再用同一个完整 SHA、plan digest、profile digest 和显式服务闭包执行 `Deploy`；项目脚本不得直接作为第二入口。
- 应用镜像使用 immutable OCI ref。事务开始前同时冻结 PostgreSQL、RabbitMQ、Qdrant 的真实 RepoDigest/runtime image id；回滚按冻结身份恢复，不重新解析可变 tag。
- 同 SHA 的 no-op 还必须满足 support/services/image digest、服务器配置 fingerprint、运行镜像身份和全部常驻容器稳定；配置 fingerprint 漂移只能全量发布。
- support/compose/infra/runtime/state 任一恢复或证据落盘不确定时返回 `86` 并保留 blocked/backup；SSH 断联后按 invocation token 对账，active/unknown 返回 `87`，不得自动取消或盲目重试。
- DataWorker/RagWorker 当前没有独立业务健康端点；发布只能证明容器进程、OOM、重启稳定性及已有 Docker Health，不能把它表述为完整业务健康。

全量发布会先运行 `aicopilot-migration`，并在启动 HttpApi/DataWorker/RagWorker/Web 前执行模型和 Embedding API key 迁移 preflight。以下服务器命令只用于获批的维护诊断/break-glass，不是 AI 日常标准入口；按需处理 `httpapi`、`dataworker` 或 `ragworker` 时，`--services` 必须同时包含 `migration`，web-only 可以不带：

```bash
cd /srv/enterprise-ai/deploy
./deploy-release.sh sha-<git-sha> --services migration
```

迁移后 `deploy-release.sh` 会先在 runtime 启动前确认 `aigateway.language_models.api_key` 和 `rag.embedding_models.api_key` 的非空值全部是 `encv2:`、没有 `encv1:` 或明文，并通过 `MigrationWorker__CheckSecretsOnly=true` 只读模式验证当前 `AICOPILOT_API_KEY_ENCRYPTION_KEY` 能解开这些密文；发布后安全验收会再次确认 Cloud OIDC 状态接口可达和密钥迁移结果。需要手工复验时执行：

```bash
cd /srv/enterprise-ai/deploy
./scripts/check-release-security-attestation.sh
```

该脚本会同时验收 HTTP-only Web 安全头、Cloud OIDC 状态接口、`aicopilot-webui` 非 root 运行和 API key
密文迁移结果。需要单独手工核对密钥迁移结果时，再执行独立只读脚本：

```bash
./scripts/check-model-secret-migration.sh
```

两个计数列必须全为 `0`。否则不要启动依赖模型或 Embedding 的服务，先重新运行 migration 或由管理员重新录入对应密钥。

应用镜像仓库只保留当前生产 `sha-*` tag。本机构建推送候选 tag 后，不立即删除当前生产 tag；必须等服务器部署健康检查通过后，由发布后清理删除旧 tag 并执行或确认 Harbor GC。`buildcache` 和基础镜像 tag 不计入应用版本保留。

AICopilot 发布成功且服务器验证通过后，必须清理 Docker/BuildKit build cache、服务器本机未被当前容器引用的旧 AICopilot 应用镜像，并执行或确认 Harbor GC。服务器本机 Docker 管理镜像和 containerd 管理内容必须分开统计、分开清理；containerd 侧未确认 namespace、image ref、snapshot lease 和运行容器引用前不得强删。发布摘要必须输出清理前后 `df`、`docker system df`、containerd snapshots/content 占用和 Harbor registry 占用。基础镜像、数据库卷、Qdrant/RabbitMQ/PostgreSQL 数据、备份、配置和 secrets 不属于清理对象。回滚不依赖旧镜像保留；需要回滚时重新构建或重新拉取目标 git sha 后部署。
Harbor tag retention 和 Harbor GC 需要服务器 `.env` 显式提供 `HARBOR_USERNAME/HARBOR_PASSWORD` 或 `OCI_REGISTRY_USERNAME/OCI_REGISTRY_PASSWORD`；未配置时 post-release cleanup 会跳过 Harbor API 清理但不阻断已健康的应用部署。需要把 Harbor API 清理变成硬门禁时，设置 `POST_RELEASE_HARBOR_RETENTION_REQUIRED=1` 或 `POST_RELEASE_HARBOR_GC_REQUIRED=1`。

`/data` 达到 80% 必须告警并输出占用摘要，达到 85% 必须先清理再继续普通部署，达到 90% 阻断非应急部署。发布后清理是主线，还必须配置周级兜底清理 cron，避免部署中断后 build cache、旧镜像和旧 Harbor blob 长期堆积。

`local-release.sh` 必须显式传 `--services` 或 `--all`。传入 `httpapi`、`migration`、`dataworker`、`ragworker`、`web` 或逗号组合时，只重写对应镜像 tag、只拉取并重启指定应用服务。基础服务 `postgres`、`eventbus`、`qdrant` 会保持可用；选择 `httpapi`、`dataworker` 或 `ragworker` 时，标准本机构建会自动把 `migration` 加入服务清单并运行迁移容器。

GitHub secrets：

```text
OCI_REGISTRY=harbor.internal.example:80
OCI_NAMESPACE=enterprise-ai
OCI_REGISTRY_USERNAME=<Harbor robot 或用户>
OCI_REGISTRY_PASSWORD=<Harbor 密码或 token>
DEPLOY_TARGET_DIR=/srv/enterprise-ai/deploy
DEPLOY_ENV_FILE=<完整生产 .env 内容>
DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING=<可选，Cloud PostgreSQL 只读账号连接串>
DATA_ANALYSIS_CLOUD_READONLY_USERNAME=<可选，仅用于自动创建/轮换只读 DB 角色>
DATA_ANALYSIS_CLOUD_READONLY_PASSWORD=<可选，仅用于自动创建/轮换只读 DB 角色>
```

GitHub variables：

```text
VITE_CLOUD_PLATFORM_URL=http://cloud.internal.example:81
```

首次使用 runner 前，在能访问 Docker Hub 的机器，或已有本地基础镜像缓存的机器上，把基础镜像同步到 Harbor：

```bash
cd AICopilot
docker login harbor.internal.example:80 --username <Harbor 用户>
REGISTRY=harbor.internal.example:80 HARBOR_PROJECT=enterprise-ai ./deploy/enterprise-ai/mirror-base-images.sh
```

需要同步的基础镜像：

```text
harbor.internal.example:80/enterprise-ai/base-postgres:17.6
harbor.internal.example:80/enterprise-ai/base-rabbitmq:4.2-management
harbor.internal.example:80/enterprise-ai/base-qdrant:v1.15.5
harbor.internal.example:80/enterprise-ai/base-dotnet-aspnet:10.0-noble
harbor.internal.example:80/enterprise-ai/base-node:22-alpine
harbor.internal.example:80/enterprise-ai/base-nginx:1.27-alpine
```

`base-dotnet-aspnet:10.0-noble` 是 AICopilot 后端 hardened runtime base，不是普通
MCR 直转镜像。`mirror-base-images.sh` 生成该镜像时必须内置 `libgssapi-krb5-2`、
`tzdata`，并预创建 `/app`、`/var/lib/aicopilot/storage`、
`/var/lib/aicopilot/artifact-workspaces` 的 `app:app` 权限；后端应用 Dockerfile
不得再通过 `USER root` 或临时 `apt-get` 修运行环境。

工作区标准发布：

```powershell
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Services httpapi,dataworker -Deploy
```

单独构建镜像时使用：

```bash
REGISTRY=harbor.internal.example:80 \
CLOUD_PLATFORM_URL=http://cloud.internal.example:81 \
  ./deploy/enterprise-ai/build-and-push.sh --services httpapi,migration,dataworker,ragworker,web
```

单镜像 build/push 默认 15 分钟超时，Harbor 检查默认 2 分钟超时，SSH deploy 默认 30 分钟超时；超时必须停止并诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续等待灾备 GitHub workflow。

服务器手工部署只在本机 SSH 触发器不可用时使用：

```bash
cd /srv/enterprise-ai/deploy
docker login harbor.internal.example:80 --username <Harbor 用户>
./deploy-release.sh sha-<git-sha>
# 或按需发布：
./deploy-release.sh sha-<git-sha> --services migration,httpapi,web
```

`deploy-release.sh` 会按 release tag 重写所选应用镜像、拒绝 Docker Hub shorthand、执行 `docker compose pull`、启动 compose，并探测 Web 首页。未传 `--services` 时按五个应用镜像全量发布。

## 5. 验证

本地仓库验证：

```powershell
pwsh ./scripts/Test-AICopilotBaselineFreezeScope.ps1
pwsh ./scripts/Test-ArchitectureBoundaries.ps1
pwsh ./scripts/Test-TextEncoding.ps1
dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj
dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj
```

服务器验证：

```bash
docker compose --env-file .env -f docker-compose.yaml config -q
docker compose --env-file .env -f docker-compose.yaml ps
./scripts/check-release-security-attestation.sh
curl -I http://aicopilot.internal.example:82
curl -I http://aicopilot.internal.example:82/api/identity/cloud-oidc/status
./scripts/check-model-provider-openai.sh --env-file .env
```

Cloud OIDC 验证：

- Cloud 侧 “打开助手” 指向 `http://aicopilot.internal.example:82/api/identity/cloud-oidc/challenge`，真实生产地址只配置在 Cloud 和服务器环境中。
- AICopilot 完成 Cloud OIDC 后仍使用本地 AI 权限，不直接映射 Cloud role。
- 未启用 Cloud 只读 token 时，Cloud 业务读取保持关闭。

## 6. 禁止项

- 不提交 `.env`、token、API key、JWT secret、数据库密码、Qdrant key。
- 不通过 MCP、Tool、Agent workflow、后台任务或直接 SQL 调用 Cloud 写接口。
- 不把 Cloud role 直接映射成 AICopilot role。
- 不在文档里把 simulation、dry-run 或准备态描述成真实生产试点完成或 GA 通过。
- 不保留旧普通 Real 双轨、旧工具 schema 或旧 query 参数作为生产兼容入口。
