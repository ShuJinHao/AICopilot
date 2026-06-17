# AICopilot 部署与维护指南

本文档是 AICopilot 当前部署入口。长期业务边界见 `AGENTS.md` 和 `资料/AICopilot业务规则.md`；历史阶段计划和验收报告不再作为执行入口。

## 1. 部署口径

- 当前部署目录固定为 `deploy/enterprise-ai`。
- 生产环境使用 Docker Compose 单机编排，镜像从 Harbor 拉取。
- 标准发布走 GitHub Actions 内网 self-hosted runner，label 固定为 `iiot-linux-prod`。
- runner 必须使用专用非 root 用户运行，例如 `github-runner`；不要把 runner 装成 root 服务。
- 真实 `.env` 通过 GitHub secret `DEPLOY_ENV_FILE` 注入服务器，不提交真实密钥。
- Docker Hub 不作为生产依赖源；PostgreSQL、RabbitMQ、Qdrant、Node、Nginx 基础镜像必须先 mirror 到 Harbor。
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

真实 `.env` 从 `deploy/enterprise-ai/.env.example` 复制后替换密钥和镜像 tag，并保存到 GitHub secret `DEPLOY_ENV_FILE`。应急手工部署时，才直接放在 `/srv/enterprise-ai/deploy/.env`。

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
```

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

## 4. 构建与发布

标准流程：

```text
git push GitHub
-> aicopilot-image on [self-hosted, iiot-linux-prod]
-> build aicopilot-httpapi / migration / dataworker / ragworker / webui
-> push 10.98.90.154:80/enterprise-ai/*:sha-<git-sha>
-> manually trigger aicopilot-deploy with release_tag=sha-<git-sha>
-> sync deploy/enterprise-ai to /srv/enterprise-ai/deploy
-> write DEPLOY_ENV_FILE to .env
-> run deploy-release.sh
```

GitHub secrets：

```text
OCI_REGISTRY=10.98.90.154:80
OCI_NAMESPACE=enterprise-ai
OCI_REGISTRY_USERNAME=<Harbor robot 或用户>
OCI_REGISTRY_PASSWORD=<Harbor 密码或 token>
DEPLOY_TARGET_DIR=/srv/enterprise-ai/deploy
DEPLOY_ENV_FILE=<完整生产 .env 内容>
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
10.98.90.154:80/enterprise-ai/base-node:22-alpine
10.98.90.154:80/enterprise-ai/base-nginx:1.27-alpine
```

应急手工构建只在 GitHub Actions 不可用时使用：

```bash
cd AICopilot
REGISTRY=10.98.90.154:80 HARBOR_PROJECT=enterprise-ai TAG=sha-<git-sha> ./deploy/enterprise-ai/build-and-push.sh
```

应急手工部署只在 `aicopilot-deploy` 不可用时使用：

```bash
cd /srv/enterprise-ai/deploy
docker login 10.98.90.154:80 --username <Harbor 用户>
./deploy-release.sh sha-<git-sha>
```

`deploy-release.sh` 会按 release tag 重写五个应用镜像、拒绝 Docker Hub shorthand、执行 `docker compose pull`、启动 compose，并探测 Web 首页。

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
