# AICopilot 部署与维护指南

本文档是 AICopilot 当前部署入口。长期业务边界见 `AGENTS.md` 和 `资料/AICopilot业务规则.md`；历史阶段计划和验收报告不再作为执行入口。

## 1. 部署口径

- 当前部署目录固定为 `deploy/enterprise-ai`。
- 生产环境使用 Docker Compose 单机编排，镜像从 Harbor 拉取。
- 真实 `.env` 只在服务器维护，不提交真实密钥。
- AICopilot 默认保持 Cloud 只读边界，不能注册、修改、删除或触发 Cloud 业务数据。
- Cloud OIDC 只用于身份对齐；AICopilot 保留本地 AI 用户、AI 角色、AI 权限、审计和 emergency admin。

## 2. 镜像和服务器目录

部署包至少包含：

```text
deploy/enterprise-ai/
  .env.example
  build-and-push.sh
  docker-compose.yaml
```

服务器建议目录：

```text
/srv/enterprise-ai/deploy
```

真实 `.env` 从 `deploy/enterprise-ai/.env.example` 复制后替换密钥和镜像 tag。

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
CLOUD_READONLY_REAL_ENABLED=false
CLOUD_READONLY_REAL_ALLOW_PRODUCTION_READ=false
CLOUD_AI_READ_ENABLED=false
CLOUD_OIDC_ENABLED=true
CLOUD_OIDC_ISSUER=http://10.98.90.154:81
ALLOW_INTRANET_HTTP_OIDC=true
CLOUD_OIDC_CLIENT_ID=aicopilot
CLOUD_OIDC_REQUIRE_HTTPS_METADATA=false
```

`CLOUD_AI_SERVICE_ACCOUNT_TOKEN` 只有在 Cloud 明确发放 AI 只读服务账号 token 后才填写；不能写入仓库。

## 4. 构建与发布

本地或 CI 构建前先确认 Docker 可用，且使用 Linux 容器。

```bash
cd AICopilot
./deploy/enterprise-ai/build-and-push.sh <tag>
```

若脚本需要 Harbor 参数，按脚本提示或服务器约定传入；同一批服务镜像使用同一个 `<tag>`。

服务器部署：

```bash
cd /srv/enterprise-ai/deploy
docker compose --env-file .env -f docker-compose.yaml pull
docker compose --env-file .env -f docker-compose.yaml up -d
docker compose --env-file .env -f docker-compose.yaml ps
```

首次部署会先运行 `aicopilot-migration`，成功后启动 HttpApi、DataWorker、RagWorker 和 WebUI。

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
