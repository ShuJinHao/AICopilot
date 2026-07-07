# AICopilot enterprise-ai deploy

本目录是 AICopilot 生产部署的可执行入口。新接手部署时，先读本文件，再按需查看仓库根目录的 `AICopilot 项目部署与维护指南.md`、`AGENTS.md` 和 `资料/AICopilot业务规则.md`。

## 部署口径

- 生产环境使用 Docker Compose 单机编排，服务器目录为 `/srv/enterprise-ai/deploy`。
- 标准发布走操作者本机：先 push GitHub 留痕，再本机构建镜像、推 Harbor，最后 SSH 触发服务器 `deploy-release.sh`。
- 多 agent 并行部署只按 [上传部署总览](../../../docs/上传部署总览.md) 的“多 agent 并行部署”执行；本目录只描述 AICopilot 自身发布步骤。
- `aicopilot-image` / `aicopilot-deploy` 只保留带确认词的灾备入口；日常生产发布不得等待这些 workflow。
- 单个镜像 build/push 默认 15 分钟超时，Harbor 登录/API 检查默认 2 分钟超时，SSH deploy 默认 30 分钟超时；超时必须停止并按脚本输出诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续 watch 或无限等待。
- 灾备 workflow 的 runner 必须使用专用非 root 用户，并带 `iiot-linux-prod` label；不得改回 GitHub hosted runner 执行镜像构建或部署。production/secrets 相关灾备 workflow 和 runner 机器侧都必须执行 `scripts/check-runner-security-attestation.sh`，验收非 root、工作目录、Docker Root Dir 和部署目录。
- 灾备 workflow 当前仍依赖 GitHub production environment secrets；它只允许最小仓库权限和非 root runner。runner 机器权限收敛、短期凭据或 Vault/OIDC 接入必须单独作为基础设施项验收，不能用 workflow 代码改动替代；runner 脚本只证明本机事实，不证明 GitHub environment secrets 或 Vault/OIDC 已完成。
- 应用镜像和基础镜像全部来自 Harbor，不从 Docker Hub/MCR 作为生产依赖源直接拉取。
- AICopilot 应用镜像不保留历史版本；Harbor 和服务器本机只保留当前生产正在运行的 `sha-*` 应用镜像。
- 真实 `.env` 只通过 GitHub secret `DEPLOY_ENV_FILE` 注入服务器，不提交到仓库。
- 当前内网部署红线是 HTTP-only。部署脚本、compose、nginx 模板和验收命令不得把 HTTPS redirection、HSTS、443 listener、证书申请/续期或 OIDC HTTPS metadata 强制校验作为当前门槛；安全加固改走内网隔离、端口收敛、同源代理、CORS 白名单、强 secret、短期 token、非 root 容器、只读边界和除 HSTS 外的安全响应头。
- AICopilot 对 Cloud 业务数据保持只读边界；不得通过 MCP、Tool、Agent workflow、后台任务或隐藏适配器写 Cloud。

## 目录文件

```text
deploy/enterprise-ai/
  .env.example            # 生产 .env 模板，只保留占位密钥
  build-and-push.sh       # 标准本机镜像构建和 Harbor push
  local-release.sh        # 标准本机发布入口，构建后 SSH 触发服务器 deploy-release.sh
  deploy-release.sh       # 服务器发布脚本，支持全量和 --services 按需发布
  docker-compose.yaml     # 生产 compose 模板
  mirror-base-images.sh   # 基础镜像同步到 Harbor
  runner-platform-attestation.template.md  # runner/GitHub/OIDC 平台侧验收记录模板
  cloud-readonly/         # Cloud PostgreSQL readonly role 授权和探针 SQL
  scripts/                # CloudReadOnly 授权、模型 provider smoke、runner、平台记录和发布后安全验收脚本
  post-release-cleanup.sh  # 发布成功后清理 build cache、旧应用镜像、旧 Harbor tag 并触发 GC
  cron/                   # 周级兜底清理 cron 模板
  README.md               # 本文件
  releases/               # 服务器发布状态，不提交仓库
```

## GitHub 配置

Secrets:

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

Variables:

```text
VITE_CLOUD_PLATFORM_URL=http://cloud.internal.example:81
```

`DEPLOY_ENV_FILE` 内容从 `.env.example` 复制后替换强密码和 token。不得把真实 `.env`、JWT secret、数据库密码、Qdrant key 或 Cloud service token 写入仓库。

Runner 机器侧验收：

```bash
cd /srv/enterprise-ai/deploy
./scripts/check-runner-security-attestation.sh \
  --work-root /data/github-runner/aicopilot \
  --docker-root /data/docker \
  --deploy-dir /srv/enterprise-ai/deploy
```

该脚本只能证明 runner 机器本地事实。GitHub production environment secrets 权限、required reviewers、OIDC/Vault 或等价短期凭据必须由平台侧单独留痕。
平台侧留痕使用 `runner-platform-attestation.template.md` 复制一份填写，填好的记录不要提交真实 secret 或敏感截图；完成后用
`scripts/check-platform-attestation-record.sh --record <filled-attestation.md>` 做静态完整性校验。该 linter 会拒绝模板占位符、未勾选项、空签署人和 `pending` / `not implemented` / `N/A` 等弱证明词，并要求记录包含 GitHub production environment secret 限制、required reviewers、`contents: read`、`self-hosted + iiot-linux-prod`、生产/secret workflow 无 GitHub hosted runner 的证据；如果 OIDC/Vault 或等价短期凭据尚未落地，记录里只能写成已批准的基础设施例外，并按结构化字段给出 `Ticket or change id`、`Exception owner`、`Due date` 和 `Current mitigation`。该 linter 只检查记录是否完整，不能替代 GitHub、Vault、OIDC 或 runner 真实验收。

内部开发需要让 AICopilot 直连真实 Cloud 只读数据库时，连接串只放在 GitHub production environment secret
`DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING`，并通过 `aicopilot-enable-direct-cloud-readonly-db` 写入服务器 `.env`。
该模式只注册 AICopilot 自身的 DataAnalysis `CloudReadOnly` 数据源，不写 Cloud 业务表；数据库账号必须先确认为只读账号。
Cloud Postgres 不发布到宿主端口，AICopilot 部署脚本会创建外部 Docker 网络
`enterprise-ai-cloud-readonly`，并把 Cloud compose 的 `deploy/postgres` 容器连接为别名
`cloud-postgres`。只读连接串推荐使用：
`Host=cloud-postgres;Port=5432;Database=iiot-db;Username=<readonly_user>;Password=<readonly_password>`。
Cloud PostgreSQL readonly role 的授权权威载体是
`deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql` 和
`deploy/enterprise-ai/cloud-readonly/check-readonly-grants.sql`。它们只对
`devices`、`mfg_processes`、`device_logs`、`hourly_capacity`、`pass_station_records`
做显式表级 `GRANT SELECT`，并校验写权限、schema create 权限均不存在；不得改成
`GRANT SELECT ON ALL TABLES`、默认权限、未来表自动授权或列级/表级混用口径。
如果还没有只读账号，标准做法是在可访问服务器和 Cloud PostgreSQL 容器的机器上运行
`deploy/enterprise-ai/scripts/apply-cloud-readonly-grants.sh`；随后用
`deploy/enterprise-ai/scripts/check-cloud-readonly-grants.sh` 验证。历史
`scripts/Provision-AICopilotCloudReadOnlyDbRole.sh` 和
`aicopilot-provision-cloud-readonly-db-role` workflow 只保留为带确认词的手动兜底，
且必须读取同一组 `cloud-readonly/*.sql`，不得再维护内联 GRANT 清单。

启用 direct DB 后，服务器 `deploy-release.sh` 会在重启服务前自动执行
`scripts/check-cloud-readonly-grants.sh`；preflight 失败必须停止部署并先修 readonly
授权，不允许把权限缺口伪装成“数据源暂时不可用”继续发布。

服务器到私有模型 API 的连通性必须能绕开 AICopilot 应用独立验证：
`scripts/check-model-provider-openai.sh` 会直接调用 OpenAI-compatible
`/chat/completions`。模型 smoke endpoint、model 和 API key 必须放在服务器真实 `.env`
或显式命令参数中；如果当前私有模型网关允许 dummy key，也只能在真实 `.env` 中显式配置，
不得作为仓库默认值，并必须同时设置 `AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY=true`
或在手工 smoke 命令中传 `--allow-dummy-key`。设置
`AICOPILOT_MODEL_SMOKE_ENABLED=true` 后，`deploy-release.sh` 会在重启服务前执行该
preflight；失败时先修模型网络、端点或模型服务，不得把服务器到模型 API 不通解释成
前端或业务编排问题。

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

后端容器以非 root 用户运行，文件上传和 Agent artifact workspace 必须落在持久化可写卷。默认 compose 会把
`enterprise-ai-aicopilot-data` 挂到 `/var/lib/aicopilot`，并通过
`AICOPILOT_FILE_STORAGE_ROOT_PATH` / `AICOPILOT_ARTIFACT_WORKSPACE_ROOT_PATH`
配置应用读取路径；不要让生产容器依赖 `/app` 或 `LocalApplicationData` 默认路径写运行产物。
`base-dotnet-aspnet:10.0-noble` 由 `mirror-base-images.sh` 生成时必须内置
`libgssapi-krb5-2`、`tzdata`，并预创建 `/app`、`/var/lib/aicopilot/storage`
和 `/var/lib/aicopilot/artifact-workspaces` 的 `app:app` 权限；应用 Dockerfile
不得再用 `USER root` 或临时 `apt-get` 修运行环境。

Cloud OIDC 首部署管理员收编由 `CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED` 控制，生产模板默认启用，并复用 `AICOPILOT_BOOTSTRAP_ADMIN_USERNAME` 作为唯一允许收编的本地 Admin 用户名；普通同名用户仍拒绝自动绑定。

## 基础镜像

首次部署前，在能访问 Docker Hub 的机器或已有本地缓存的机器上同步基础镜像：

```bash
cd AICopilot
docker login harbor.internal.example:80 --username <Harbor 用户>
REGISTRY=harbor.internal.example:80 HARBOR_PROJECT=enterprise-ai ./deploy/enterprise-ai/mirror-base-images.sh
```

需要存在于 Harbor 的基础镜像：

```text
harbor.internal.example:80/enterprise-ai/base-postgres:17.6
harbor.internal.example:80/enterprise-ai/base-rabbitmq:4.2-management
harbor.internal.example:80/enterprise-ai/base-qdrant:v1.15.5
harbor.internal.example:80/enterprise-ai/base-dotnet-aspnet:10.0-noble
harbor.internal.example:80/enterprise-ai/base-node:22-alpine
harbor.internal.example:80/enterprise-ai/base-nginx:1.27-alpine
```

## 标准发布

1. 推送代码到 GitHub，保证源码留痕。
2. 本机运行 `REGISTRY=<harbor-registry> CLOUD_PLATFORM_URL=http://<cloud-host>:<port> deploy/enterprise-ai/local-release.sh --services <services> --ssh-target <user@host>`；脚本会校验工作区干净、HEAD 已推送、Docker/buildx 和 Harbor 可用。
3. `build-and-push.sh` 按服务构建并推送 `sha-<git-sha>` 镜像到 Harbor，输出 `Deploy services input` 和 `artifacts/deploy/aicopilot-built-services.txt`。
4. `local-release.sh` 通过 SSH 在服务器执行 `DEPLOY_GIT_SHA=<sha> DEPLOY_TRIGGERED_BY=local ./deploy-release.sh sha-<sha> --services <services>`。
5. `services` 必须显式传入，例如 `httpapi,migration,web`；不要人工猜测，不要为了省事留空。全量发布必须显式传 `--all`。

应用镜像仓库只保留当前生产 `sha-*` tag。本机构建推送候选 tag 后，不立即删除当前生产 tag；必须等服务器部署健康检查通过后，由发布后清理删除旧 tag 并执行或确认 Harbor GC。`buildcache` 和基础镜像 tag 不计入应用版本保留。

部署成功并完成服务器验证后，必须清理 Docker/BuildKit build cache、服务器本机未被当前容器引用的旧 AICopilot 应用镜像，并输出清理前后磁盘摘要。Docker 管理镜像和 containerd 管理内容必须分开统计、分开清理；containerd 侧未确认 namespace、image ref、snapshot lease 和运行容器引用前不得强删。基础镜像、数据库卷、Qdrant/RabbitMQ/PostgreSQL 数据、备份、配置和 secrets 不清理。回滚不依赖旧镜像保留；需要回滚时重新构建或重新拉取目标 git sha 后部署。
Harbor tag retention 和 Harbor GC 需要服务器 `.env` 显式提供 `HARBOR_USERNAME/HARBOR_PASSWORD` 或 `OCI_REGISTRY_USERNAME/OCI_REGISTRY_PASSWORD`；未配置时 post-release cleanup 会跳过 Harbor API 清理但不阻断已健康的应用部署。需要把 Harbor API 清理变成硬门禁时，设置 `POST_RELEASE_HARBOR_RETENTION_REQUIRED=1` 或 `POST_RELEASE_HARBOR_GC_REQUIRED=1`。

`/data` 达到 80% 必须告警并输出占用摘要，达到 85% 必须先清理再继续普通部署，达到 90% 阻断非应急部署。发布后清理是主线，还必须配置周级兜底清理 cron，避免部署中断后 build cache、旧镜像和旧 Harbor blob 长期堆积。

服务器 `deploy-release.sh` 会登录 Harbor、重写所选应用镜像 tag、执行 `docker compose pull`，全量发布时先启动基础服务、运行 `aicopilot-migration`，并在启动 HttpApi/DataWorker/RagWorker/Web 之前检查模型和 Embedding API key 已全部迁移到 `encv2:`。按需发布会先从当前 release 读取未选服务镜像，避免 `.env` 被旧 tag 覆盖；如果目标机已有旧部署但还没有 `releases/current-release.env`，脚本会用服务器 `.env` 作为初始镜像基线并写入 release manifest，不需要把 `services` 留空。按需发布包含 `httpapi`、`dataworker` 或 `ragworker` 时必须同时包含 `migration`，由 migration worker 在 runtime 启动前重新迁移并验证 `encv2:` 密文可用当前密钥解密；web-only 发布可以不带 `migration`。服务启动后会探测 Web 首页、HTTP-only 安全头，并自动运行 `scripts/check-release-security-attestation.sh` 验收 Cloud OIDC 状态接口、Web 非 root 和 API key 密文迁移。部署完成后会写入 `releases/current-release.env`、`previous-release.env`、`staged-release.env`、`current-release.summary.md` 和 `history/`，其中 summary 会包含 release security attestation 输出。

## 单独本机构建

需要只构建并推送镜像、不触发部署时使用：

```bash
cd AICopilot
REGISTRY=harbor.internal.example:80 \
CLOUD_PLATFORM_URL=http://cloud.internal.example:81 \
  ./deploy/enterprise-ai/build-and-push.sh --services httpapi,migration,dataworker,ragworker,web
```

单镜像 build/push 默认 15 分钟超时，Harbor 检查默认 2 分钟超时；超时必须停止并诊断 `docker buildx ls`、`docker system df` 和 Harbor tag，不得等待灾备 GitHub workflow。

## 服务器应急部署

仅当本机 SSH 触发器不可用时，在服务器执行：

```bash
cd /srv/enterprise-ai/deploy
docker login harbor.internal.example:80 --username <Harbor 用户>
./deploy-release.sh sha-<git-sha>
```

按需部署示例：

```bash
./deploy-release.sh sha-<git-sha> --services migration,httpapi,web
```

手工按需部署仍应填写具体 `--services`；如果目标机没有 `current-release.env`，脚本会以 `.env` 为初始基线生成 release manifest。

## 发布前环境校验

在真实发布前可先只校验服务器 `.env`，不需要 release tag，不拉镜像、不执行 Docker Compose：

```bash
cd /srv/enterprise-ai/deploy
./deploy-release.sh --validate-only
```

该命令会 fail-fast 校验 `.env` 权限、模板占位、弱 secret、HTTP-only URL、Cloud OIDC 内网 HTTP issuer、必填 secret 和 direct Cloud readonly 配置。公共 HTTP OIDC 域名、HTTPS URL、HSTS/证书强制项、弱密码或空 token 都必须先修正再发布。

## API Key 密文迁移验收

发布包含密钥迁移代码的版本时，必须显式运行 migration 服务，不能只重启 HttpApi：

```bash
cd /srv/enterprise-ai/deploy
./deploy-release.sh sha-<git-sha> --services migration
```

迁移完成后，`deploy-release.sh` 会自动执行只读验收，确认语言模型和 Embedding 模型的非空 API key 都已经是 `encv2:`，并用当前 `AICOPILOT_API_KEY_ENCRYPTION_KEY` 验证这些 `encv2:` 密文可解密。需要手工复验时执行：

```bash
cd /srv/enterprise-ai/deploy
./scripts/check-release-security-attestation.sh
```

该脚本会同时验收 HTTP-only Web 安全头、Cloud OIDC 状态接口、`aicopilot-webui` 非 root 运行和 API key
密文迁移结果；如果只需要手工核对密钥迁移结果，执行独立只读脚本：

```bash
./scripts/check-model-secret-migration.sh
```

`legacy_count` 和 `unprotected_count` 必须全部为 `0`，`MigrationWorker__CheckSecretsOnly=true` 的只读解密检查也必须通过。如果失败，不要启动依赖模型或 Embedding 的运行服务；先重新运行 migration、恢复正确的 `AICOPILOT_API_KEY_ENCRYPTION_KEY`，或由管理员重新录入对应 API key。

发布后安全验收脚本支持 dry-run，便于发布前确认命令展开：

```bash
./scripts/check-release-security-attestation.sh --dry-run
```

## 验证

仓库侧验证：

```bash
dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj
dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter SecurityHardeningTests
```

服务器侧验证：

```bash
cd /srv/enterprise-ai/deploy
docker compose --env-file .env -f docker-compose.yaml config -q
docker compose --env-file .env -f docker-compose.yaml ps
./scripts/check-release-security-attestation.sh
test -f releases/current-release.env
test -f releases/current-release.summary.md
curl -I http://aicopilot.internal.example:82
curl -I http://aicopilot.internal.example:82/api/identity/cloud-oidc/status
./scripts/check-model-provider-openai.sh --env-file .env
```

## 禁止项

- 不提交 `.env`、token、API key、JWT secret、数据库密码或 Qdrant key。
- 不把 runner 改成 root。
- 不把 workflow 改回 `ubuntu-latest` 执行镜像构建或部署。
- 不使用 Docker Hub shorthand 作为生产 compose 镜像。
- 不通过 AICopilot 写 Cloud 业务数据。
- 不把 simulation、dry-run 或准备态写成真实生产试点完成。
