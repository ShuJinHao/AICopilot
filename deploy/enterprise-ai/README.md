# AICopilot enterprise-ai deploy

本目录是 AICopilot 生产部署的可执行入口。新接手部署时，先读本文件，再按需查看仓库根目录的 `AICopilot 项目部署与维护指南.md`、`AGENTS.md` 和 `资料/AICopilot业务规则.md`。

## 部署口径

- 生产环境使用 Docker Compose 单机编排，服务器目录为 `/srv/enterprise-ai/deploy`。
- 标准发布走 GitHub Actions：`aicopilot-image` 构建镜像，`aicopilot-deploy` 部署镜像。
- 多 agent 并行部署只按 [上传部署总览](../../../docs/上传部署总览.md) 的“多 agent 并行部署”执行；本目录只描述 AICopilot 自身发布步骤。
- 两个 workflow 都必须跑在内网 self-hosted runner `[self-hosted, iiot-linux-prod]`，runner 必须是非 root 专用用户。
- 应用镜像和基础镜像全部来自 Harbor，不从 Docker Hub 作为生产依赖源直接拉取。
- AICopilot 应用镜像不保留历史版本；Harbor 和服务器本机只保留当前生产正在运行的 `sha-*` 应用镜像。
- 真实 `.env` 只通过 GitHub secret `DEPLOY_ENV_FILE` 注入服务器，不提交到仓库。
- AICopilot 对 Cloud 业务数据保持只读边界；不得通过 MCP、Tool、Agent workflow、后台任务或隐藏适配器写 Cloud。

## 目录文件

```text
deploy/enterprise-ai/
  .env.example            # 生产 .env 模板，只保留占位密钥
  build-and-push.sh       # GitHub Actions 不可用时的应急手工构建
  deploy-release.sh       # 服务器发布脚本，支持全量和 --services 按需发布
  docker-compose.yaml     # 生产 compose 模板
  mirror-base-images.sh   # 基础镜像同步到 Harbor
  post-release-cleanup.sh  # 发布成功后清理 build cache、旧应用镜像、旧 Harbor tag 并触发 GC
  cron/                   # 周级兜底清理 cron 模板
  README.md               # 本文件
  releases/               # 服务器发布状态，workflow 同步时保留，不提交仓库
```

## GitHub 配置

Secrets:

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

Variables:

```text
VITE_CLOUD_PLATFORM_URL=http://10.98.90.154:81
```

`DEPLOY_ENV_FILE` 内容从 `.env.example` 复制后替换强密码和 token。不得把真实 `.env`、JWT secret、数据库密码、Qdrant key 或 Cloud service token 写入仓库。

内部开发需要让 AICopilot 直连真实 Cloud 只读数据库时，连接串只放在 GitHub production environment secret
`DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING`，并通过 `aicopilot-enable-direct-cloud-readonly-db` 写入服务器 `.env`。
该模式只注册 AICopilot 自身的 DataAnalysis `CloudReadOnly` 数据源，不写 Cloud 业务表；数据库账号必须先确认为只读账号。
Cloud Postgres 不发布到宿主端口，AICopilot 部署脚本会创建外部 Docker 网络
`enterprise-ai-cloud-readonly`，并把 Cloud compose 的 `deploy/postgres` 容器连接为别名
`cloud-postgres`。只读连接串推荐使用：
`Host=cloud-postgres;Port=5432;Database=iiot-db;Username=<readonly_user>;Password=<readonly_password>`。
如果还没有只读账号，可在本机运行
`scripts/Provision-AICopilotCloudReadOnlyDbRole.sh`。脚本会生成随机密码，写入 GitHub
production environment secrets，并触发 `aicopilot-provision-cloud-readonly-db-role`；该 workflow
必须带确认词，只在 Cloud PostgreSQL 中创建/轮换只读角色，只授予
`devices`、`device_logs`、`hourly_capacity`、`pass_station_records` 的 SELECT 权限。

后端容器以非 root 用户运行，文件上传和 Agent artifact workspace 必须落在持久化可写卷。默认 compose 会把
`enterprise-ai-aicopilot-data` 挂到 `/var/lib/aicopilot`，并通过
`AICOPILOT_FILE_STORAGE_ROOT_PATH` / `AICOPILOT_ARTIFACT_WORKSPACE_ROOT_PATH`
配置应用读取路径；不要让生产容器依赖 `/app` 或 `LocalApplicationData` 默认路径写运行产物。

Cloud OIDC 首部署管理员收编由 `CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED` 控制，生产模板默认启用，并复用 `AICOPILOT_BOOTSTRAP_ADMIN_USERNAME` 作为唯一允许收编的本地 Admin 用户名；普通同名用户仍拒绝自动绑定。

## 基础镜像

首次部署前，在能访问 Docker Hub 的机器或已有本地缓存的机器上同步基础镜像：

```bash
cd AICopilot
docker login 10.98.90.154:80 --username <Harbor 用户>
REGISTRY=10.98.90.154:80 HARBOR_PROJECT=enterprise-ai ./deploy/enterprise-ai/mirror-base-images.sh
```

需要存在于 Harbor 的基础镜像：

```text
10.98.90.154:80/enterprise-ai/base-postgres:17.6
10.98.90.154:80/enterprise-ai/base-rabbitmq:4.2-management
10.98.90.154:80/enterprise-ai/base-qdrant:v1.15.5
10.98.90.154:80/enterprise-ai/base-node:22-alpine
10.98.90.154:80/enterprise-ai/base-nginx:1.27-alpine
```

## 标准发布

1. 推送代码到 GitHub。
2. 等 push 触发的 `aicopilot-image` 完成。它会按路径只构建受影响镜像；禁止日常部署时手动 `workflow_dispatch` 触发 `aicopilot-image`，因为手动触发会构建全部镜像，只能用于明确的全量重建或灾备。
3. 查看 `aicopilot-image` 的 Step Summary 或 `aicopilot-built-services` artifact，确认 `Deploy services input`。
4. 手动触发 `aicopilot-deploy`。
5. 输入 `release_tag=sha-<git-sha>`。
6. `services` 必须照上一步的 `Deploy services input` 填，例如 `httpapi,migration,web`；不要人工猜测，不要为了省事留空。留空表示全量发布，只能用于明确的全量发布窗口。

应用镜像仓库只保留当前生产 `sha-*` tag。`aicopilot-image` 推送成功后必须清理同一应用仓库内其他旧 tag；`buildcache` 和基础镜像 tag 不计入应用版本保留。Harbor robot 或用户必须具备删除 tag 权限，并在删 tag 后执行或确认 Harbor Garbage Collection 回收磁盘。

部署成功并完成服务器验证后，必须清理 Docker/BuildKit build cache、服务器本机未被当前容器引用的旧 AICopilot 应用镜像，并输出清理前后磁盘摘要。Docker 管理镜像和 containerd 管理内容必须分开统计、分开清理；containerd 侧未确认 namespace、image ref、snapshot lease 和运行容器引用前不得强删。基础镜像、数据库卷、Qdrant/RabbitMQ/PostgreSQL 数据、备份、配置和 secrets 不清理。回滚不依赖旧镜像保留；需要回滚时重新构建或重新拉取目标 git sha 后部署。

`/data` 达到 80% 必须告警并输出占用摘要，达到 85% 必须先清理再继续普通部署，达到 90% 阻断非应急部署。发布后清理是主线，还必须配置周级兜底清理 cron，避免部署中断后 build cache、旧镜像和旧 Harbor blob 长期堆积。

发布脚本会同步本目录到服务器、写入 `DEPLOY_ENV_FILE`、登录 Harbor、重写所选应用镜像 tag、执行 `docker compose pull` 和 `docker compose up -d`，最后探测 Web 首页。按需发布会先从当前 release 读取未选服务镜像，避免 `.env` 被 secret 里的旧 tag 覆盖；如果目标机已有旧部署但还没有 `releases/current-release.env`，脚本会用服务器 `.env` 作为初始镜像基线并写入 release manifest，不需要把 `services` 留空。部署完成后会写入 `releases/current-release.env`、`previous-release.env`、`staged-release.env`、`current-release.summary.md` 和 `history/`，并把 summary 回贴到 GitHub Step Summary。

## 应急手工构建

仅当 GitHub Actions 不可用时使用：

```bash
cd AICopilot
REGISTRY=10.98.90.154:80 HARBOR_PROJECT=enterprise-ai TAG=sha-<git-sha> ./deploy/enterprise-ai/build-and-push.sh
```

应急构建默认也会执行 `harbor-retention.sh`。执行前需要导出 `HARBOR_USERNAME` / `HARBOR_PASSWORD`，或复用 `OCI_REGISTRY_USERNAME` / `OCI_REGISTRY_PASSWORD`。

## 应急手工部署

仅当 `aicopilot-deploy` 不可用时，在服务器执行：

```bash
cd /srv/enterprise-ai/deploy
docker login 10.98.90.154:80 --username <Harbor 用户>
./deploy-release.sh sha-<git-sha>
```

按需部署示例：

```bash
./deploy-release.sh sha-<git-sha> --services httpapi,web
```

手工按需部署仍应填写具体 `--services`；如果目标机没有 `current-release.env`，脚本会以 `.env` 为初始基线生成 release manifest。

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
test -f releases/current-release.env
test -f releases/current-release.summary.md
curl -I http://10.98.90.154:82
curl -I http://10.98.90.154:82/api/identity/cloud-oidc/status
```

## 禁止项

- 不提交 `.env`、token、API key、JWT secret、数据库密码或 Qdrant key。
- 不把 runner 改成 root。
- 不把 workflow 改回 `ubuntu-latest` 执行镜像构建或部署。
- 不使用 Docker Hub shorthand 作为生产 compose 镜像。
- 不通过 AICopilot 写 Cloud 业务数据。
- 不把 simulation、dry-run 或准备态写成真实生产试点完成。
