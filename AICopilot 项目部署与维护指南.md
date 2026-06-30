# AICopilot 部署与维护指南

本文档是 AICopilot 当前部署入口。长期业务边界见 `AGENTS.md` 和 `资料/AICopilot业务规则.md`；历史阶段计划和验收报告不再作为执行入口。

## 1. 部署口径

- 当前部署目录固定为 `deploy/enterprise-ai`。
- `deploy/enterprise-ai/README.md` 是部署目录内的自解释入口；新 AI 接手时先读该文件即可执行标准发布和应急发布。
- 多 agent 并行部署只按 `../docs/上传部署总览.md` 的“多 agent 并行部署”执行；AICopilot agent 只负责 AI 镜像、AI deploy 和 Web/OIDC 验证。
- 生产环境使用 Docker Compose 单机编排，镜像从 Harbor 拉取。
- 标准发布走操作者本机：先 push GitHub 留痕，再本机构建镜像、推 Harbor，最后通过 SSH 触发服务器 `deploy-release.sh`。
- GitHub `aicopilot-image` / `aicopilot-deploy` 只保留带确认词的灾备入口；日常生产发布不得等待这些 workflow。
- 单个镜像 build/push 默认 15 分钟超时，Harbor 登录/API 检查默认 2 分钟超时，SSH deploy 默认 30 分钟超时；超时必须停止并按脚本输出诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续 watch 或无限等待。
- 灾备 runner 必须使用专用非 root 用户运行，例如 `github-runner`；不要把 runner 装成 root 服务。
- 当前服务器 runner 工作目录固定为 `/data/github-runner/aicopilot`，Docker Root Dir 固定为 `/data/docker`，不要把构建缓存放回系统盘。
- AICopilot 应用镜像不保留历史版本；Harbor 和服务器本机只保留当前生产正在运行的 `sha-*` 应用镜像。
- 当前内网环境 Git smart HTTP 可能超时，旧 workflow 使用 GitHub archive/codeload 兜底拉取源码；这些 workflow 仅用于灾备，不作为日常发布入口。
- 真实 `.env` 通过 GitHub secret `DEPLOY_ENV_FILE` 注入服务器，不提交真实密钥。
- Docker Hub 不作为生产依赖源，MCR 也不得作为生产构建的直接依赖源；PostgreSQL、RabbitMQ、Qdrant、.NET ASP.NET runtime、Node、Nginx 基础镜像必须先 mirror 到 Harbor。
- AICopilot 默认保持 Cloud 只读边界，不能注册、修改、删除或触发 Cloud 业务数据。
- Cloud OIDC 只用于身份对齐；AICopilot 保留本地 AI 用户、AI 角色、AI 权限、审计和 emergency admin。

## 2. 镜像和服务器目录

部署包至少包含：

```text
deploy/enterprise-ai/
  .env.example
  build-and-push.sh
  deploy-release.sh
  docker-compose.yaml
  mirror-base-images.sh
```

服务器建议目录：

```text
/srv/enterprise-ai/deploy
```

2026-06-18 现场校准：`10.98.90.154` 使用 `/srv/enterprise-ai/deploy` 作为 compose 工作目录，应用入口为 `http://10.98.90.154:82`，镜像项目为 Harbor `enterprise-ai`。

真实 `.env` 从 `deploy/enterprise-ai/.env.example` 复制后替换密钥和镜像 tag，并直接保存到服务器 `/srv/enterprise-ai/deploy/.env`。GitHub secret `DEPLOY_ENV_FILE` 只服务灾备 workflow，不作为日常标准发布前置条件。

## 3. 关键环境变量

入口和镜像：

```text
COMPOSE_PROJECT_NAME=enterprise-ai
AICOPILOT_PUBLIC_URL=http://10.98.90.154:82
CLOUD_PLATFORM_URL=http://10.98.90.154:81
AICOPILOT_WEB_PORT=82
AICOPILOT_HTTPAPI_IMAGE=10.98.90.154:80/enterprise-ai/aicopilot-httpapi:<tag>
AICOPILOT_MIGRATION_IMAGE=10.98.90.154:80/enterprise-ai/aicopilot-migration:<tag>
AICOPILOT_DATAWORKER_IMAGE=10.98.90.154:80/enterprise-ai/aicopilot-dataworker:<tag>
AICOPILOT_RAGWORKER_IMAGE=10.98.90.154:80/enterprise-ai/aicopilot-ragworker:<tag>
AICOPILOT_WEBUI_IMAGE=10.98.90.154:80/enterprise-ai/aicopilot-webui:<tag>
POSTGRES_IMAGE=10.98.90.154:80/enterprise-ai/base-postgres:17.6
RABBITMQ_IMAGE=10.98.90.154:80/enterprise-ai/base-rabbitmq:4.2-management
QDRANT_IMAGE=10.98.90.154:80/enterprise-ai/base-qdrant:v1.15.5
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
CLOUD_OIDC_ISSUER=http://10.98.90.154:81
ALLOW_INTRANET_HTTP_OIDC=true
CLOUD_OIDC_CLIENT_ID=aicopilot
CLOUD_OIDC_REQUIRE_HTTPS_METADATA=false
CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED=true
```

`CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED=true` 只允许首部署本地 Admin 用户在 Cloud `employee_no` 精确等于 `AICOPILOT_BOOTSTRAP_ADMIN_USERNAME` 且该用户无既有 Cloud 绑定时被收编；普通同名用户和已绑定后 sub 漂移仍拒绝。

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
- 设备日志：`GET /api/v1/ai/read/device-logs`，参数为 `deviceId`、`startTime`、`endTime`、`maxRows`。
- 过站记录：`GET /api/v1/ai/read/pass-stations/{typeKey}`，必须显式传入 `{typeKey}`，参数为 `deviceId`、`startTime`、`endTime`、`maxRows`。
- `deviceCode` 只能用于设备查询/解析，无法唯一命中时不得继续读取业务数据。
- P12/P13 的 `scenarioId`、`from`、`to`、`boundary`、`intentId`、`goalHash`、`analysisType`、`pilotWindowId` 等只允许留在 AICopilot 内部审计，不得作为 Cloud query 参数。
- AICopilot 不读取未批准的配方主数据、配方详情或配方版本。
- Simulation 只能用于联调和演示，不能作为生产验收结果。

内部开发验证也允许走 DataAnalysis direct Cloud readonly DB：只读连接串放在 GitHub production environment
secret `DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING`，本地可用
`scripts/Set-AICopilotCloudReadOnlyDbSecret.sh` 写入并触发
`aicopilot-enable-direct-cloud-readonly-db`。该路径只注册 AICopilot DataAnalysis
`CloudReadOnly` 数据源，必须使用已验证只读账号，不直连写 Cloud 业务数据。
Cloud Postgres 不发布宿主 5432；AICopilot 部署会创建外部 Docker 网络
`enterprise-ai-cloud-readonly`，把 Cloud compose 的 `deploy/postgres` 容器接入并设置别名
`cloud-postgres`。只读连接串推荐写：
`Host=cloud-postgres;Port=5432;Database=iiot-db;Username=<readonly_user>;Password=<readonly_password>`。
如果还没有只读账号，使用本机脚本
`scripts/Provision-AICopilotCloudReadOnlyDbRole.sh`。它生成随机密码并写入 GitHub production
environment secrets，然后触发 `aicopilot-provision-cloud-readonly-db-role`；该 workflow
要求确认词，只在 Cloud PostgreSQL 中创建/轮换只读角色，只授权四张白名单表 SELECT，可随后启用
AICopilot direct DB。

AiGateway 会话并发锁：

- 生产组合根必须使用 PostgreSQL advisory `ISessionExecutionLock`，依赖 `ConnectionStrings:ai-copilot`。
- `InMemorySessionExecutionLock` 只允许作为服务层测试/本地 fallback，不得成为生产或多实例部署的实际锁实现。
- 多实例部署前必须先验证每个 `AICOPILOT_HTTPAPI_IMAGE` 实例都走 PostgreSQL advisory lock；如果后续改为其他分布式锁，必须同步更新部署文档和并发验收用例。
- 同一 session 的并发 Chat、Plan、Approval 请求必须被串行化或返回明确锁错误，不允许并发执行同一会话工作流。

## 4. 构建与发布

标准流程：

```text
git push GitHub
-> 本机确认 HEAD 已推送且工作区干净
-> 本机 deploy/enterprise-ai/build-and-push.sh --services <services> 或 --all
-> push 10.98.90.154:80/enterprise-ai/*:sha-<git-sha>
-> 本机 deploy/enterprise-ai/local-release.sh 通过 SSH 调用 /srv/enterprise-ai/deploy/deploy-release.sh
-> 服务器 pull/up/health/cleanup/history
```

`build-and-push.sh` 必须显式传 `--services httpapi,migration,dataworker,ragworker,web` 的子集，或显式 `--all`；无参数直接失败。脚本会输出 `Deploy services input` 和 `artifacts/deploy/aicopilot-built-services.txt`，`local-release.sh` 使用该服务清单触发服务器部署。

应用镜像仓库只保留当前生产 `sha-*` tag。本机构建推送候选 tag 后，不立即删除当前生产 tag；必须等服务器部署健康检查通过后，由发布后清理删除旧 tag 并执行或确认 Harbor GC。`buildcache` 和基础镜像 tag 不计入应用版本保留。

AICopilot 发布成功且服务器验证通过后，必须清理 Docker/BuildKit build cache、服务器本机未被当前容器引用的旧 AICopilot 应用镜像，并执行或确认 Harbor GC。服务器本机 Docker 管理镜像和 containerd 管理内容必须分开统计、分开清理；containerd 侧未确认 namespace、image ref、snapshot lease 和运行容器引用前不得强删。发布摘要必须输出清理前后 `df`、`docker system df`、containerd snapshots/content 占用和 Harbor registry 占用。基础镜像、数据库卷、Qdrant/RabbitMQ/PostgreSQL 数据、备份、配置和 secrets 不属于清理对象。回滚不依赖旧镜像保留；需要回滚时重新构建或重新拉取目标 git sha 后部署。

`/data` 达到 80% 必须告警并输出占用摘要，达到 85% 必须先清理再继续普通部署，达到 90% 阻断非应急部署。发布后清理是主线，还必须配置周级兜底清理 cron，避免部署中断后 build cache、旧镜像和旧 Harbor blob 长期堆积。

`local-release.sh` 必须显式传 `--services` 或 `--all`。传入 `httpapi`、`migration`、`dataworker`、`ragworker`、`web` 或逗号组合时，只重写对应镜像 tag、只拉取并重启指定应用服务。基础服务 `postgres`、`eventbus`、`qdrant` 会保持可用；只有选择 `migration` 时才运行迁移容器。

GitHub secrets：

```text
OCI_REGISTRY=10.98.90.154:80
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
VITE_CLOUD_PLATFORM_URL=http://10.98.90.154:81
```

首次使用 runner 前，在能访问 Docker Hub 的机器，或已有本地基础镜像缓存的机器上，把基础镜像同步到 Harbor：

```bash
cd AICopilot
docker login 10.98.90.154:80 --username <Harbor 用户>
REGISTRY=10.98.90.154:80 HARBOR_PROJECT=enterprise-ai ./deploy/enterprise-ai/mirror-base-images.sh
```

需要同步的基础镜像：

```text
10.98.90.154:80/enterprise-ai/base-postgres:17.6
10.98.90.154:80/enterprise-ai/base-rabbitmq:4.2-management
10.98.90.154:80/enterprise-ai/base-qdrant:v1.15.5
10.98.90.154:80/enterprise-ai/base-dotnet-aspnet:10.0-noble
10.98.90.154:80/enterprise-ai/base-node:22-alpine
10.98.90.154:80/enterprise-ai/base-nginx:1.27-alpine
```

本机标准发布：

```bash
cd AICopilot
git push
DEPLOY_SSH_TARGET=root@10.98.90.154 \
  ./deploy/enterprise-ai/local-release.sh --services httpapi,dataworker
```

单独构建镜像时使用：

```bash
./deploy/enterprise-ai/build-and-push.sh --services httpapi,migration,dataworker,ragworker,web
```

单镜像 build/push 默认 15 分钟超时，Harbor 检查默认 2 分钟超时，SSH deploy 默认 30 分钟超时；超时必须停止并诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续等待灾备 GitHub workflow。

服务器手工部署只在本机 SSH 触发器不可用时使用：

```bash
cd /srv/enterprise-ai/deploy
docker login 10.98.90.154:80 --username <Harbor 用户>
./deploy-release.sh sha-<git-sha>
# 或按需发布：
./deploy-release.sh sha-<git-sha> --services httpapi,web
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
curl -I http://10.98.90.154:82
curl -I http://10.98.90.154:82/api/identity/cloud-oidc/status
```

Cloud OIDC 验证：

- Cloud 侧 “打开助手” 指向 `http://10.98.90.154:82/api/identity/cloud-oidc/challenge`。
- AICopilot 完成 Cloud OIDC 后仍使用本地 AI 权限，不直接映射 Cloud role。
- 未启用 Cloud 只读 token 时，Cloud 业务读取保持关闭。

## 6. 禁止项

- 不提交 `.env`、token、API key、JWT secret、数据库密码、Qdrant key。
- 不通过 MCP、Tool、Agent workflow、后台任务或直接 SQL 调用 Cloud 写接口。
- 不把 Cloud role 直接映射成 AICopilot role。
- 不在文档里把 simulation、dry-run 或准备态描述成真实生产试点完成或 GA 通过。
- 不保留旧普通 Real 双轨、旧工具 schema 或旧 query 参数作为生产兼容入口。
