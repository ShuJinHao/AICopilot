# AICopilot enterprise-ai deploy

本目录是 AICopilot 生产部署的可执行入口。新接手部署时，先读本文件，再按需查看仓库根目录的 `AICopilot 项目部署与维护指南.md`、`AGENTS.md` 和 `资料/AICopilot业务规则.md`。

## 部署口径

- 生产环境使用 Docker Compose 单机编排，服务器目录为 `/srv/enterprise-ai/deploy`。
- 标准发布走操作者本机：先 push GitHub 留痕，再本机构建镜像、推 Harbor，最后 SSH 触发服务器 `deploy-release.sh`。
- 多 agent 并行部署只按 [上传部署总览](../../../docs/上传部署总览.md) 的“多 agent 并行部署”执行；本目录只描述 AICopilot 自身发布步骤。
- `aicopilot-image` / `aicopilot-deploy` 只保留带确认词的灾备入口；日常生产发布不得等待这些 workflow。
- 单个镜像 build/push 默认 15 分钟超时，Harbor 登录/API 检查默认 2 分钟超时，SSH deploy 默认 30 分钟超时；超时必须停止并按脚本输出诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续 watch 或无限等待。
- 灾备 workflow 的 runner 必须使用专用非 root 用户，并带 `iiot-linux-prod` label；不得改回 GitHub hosted runner 执行镜像构建或部署。
- 应用镜像和基础镜像全部来自 Harbor，不从 Docker Hub/MCR 作为生产依赖源直接拉取。
- AICopilot 应用镜像不保留历史版本；Harbor 和服务器本机只保留当前生产正在运行的 `sha-*` 应用镜像。
- 真实 `.env` 只通过 GitHub secret `DEPLOY_ENV_FILE` 注入服务器，不提交到仓库。
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
  cloud-readonly/         # Cloud PostgreSQL readonly role 授权和探针 SQL
  scripts/                # CloudReadOnly 授权 apply/check 脚本
  post-release-cleanup.sh  # 发布成功后清理 build cache、旧应用镜像、旧 Harbor tag 并触发 GC
  cron/                   # 周级兜底清理 cron 模板
  README.md               # 本文件
  releases/               # 服务器发布状态，不提交仓库
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
10.98.90.154:80/enterprise-ai/base-dotnet-aspnet:10.0-noble
10.98.90.154:80/enterprise-ai/base-node:22-alpine
10.98.90.154:80/enterprise-ai/base-nginx:1.27-alpine
```

## 标准发布

1. 推送代码到 GitHub，保证源码留痕。
2. 本机运行 `deploy/enterprise-ai/local-release.sh --services <services> --ssh-target <user@host>`；脚本会校验工作区干净、HEAD 已推送、Docker/buildx 和 Harbor 可用。
3. `build-and-push.sh` 按服务构建并推送 `sha-<git-sha>` 镜像到 Harbor，输出 `Deploy services input` 和 `artifacts/deploy/aicopilot-built-services.txt`。
4. `local-release.sh` 通过 SSH 在服务器执行 `DEPLOY_GIT_SHA=<sha> DEPLOY_TRIGGERED_BY=local ./deploy-release.sh sha-<sha> --services <services>`。
5. `services` 必须显式传入，例如 `httpapi,migration,web`；不要人工猜测，不要为了省事留空。全量发布必须显式传 `--all`。

应用镜像仓库只保留当前生产 `sha-*` tag。本机构建推送候选 tag 后，不立即删除当前生产 tag；必须等服务器部署健康检查通过后，由发布后清理删除旧 tag 并执行或确认 Harbor GC。`buildcache` 和基础镜像 tag 不计入应用版本保留。

部署成功并完成服务器验证后，必须清理 Docker/BuildKit build cache、服务器本机未被当前容器引用的旧 AICopilot 应用镜像，并输出清理前后磁盘摘要。Docker 管理镜像和 containerd 管理内容必须分开统计、分开清理；containerd 侧未确认 namespace、image ref、snapshot lease 和运行容器引用前不得强删。基础镜像、数据库卷、Qdrant/RabbitMQ/PostgreSQL 数据、备份、配置和 secrets 不清理。回滚不依赖旧镜像保留；需要回滚时重新构建或重新拉取目标 git sha 后部署。
Harbor tag retention 和 Harbor GC 需要服务器 `.env` 显式提供 `HARBOR_USERNAME/HARBOR_PASSWORD` 或 `OCI_REGISTRY_USERNAME/OCI_REGISTRY_PASSWORD`；未配置时 post-release cleanup 会跳过 Harbor API 清理但不阻断已健康的应用部署。需要把 Harbor API 清理变成硬门禁时，设置 `POST_RELEASE_HARBOR_RETENTION_REQUIRED=1` 或 `POST_RELEASE_HARBOR_GC_REQUIRED=1`。

`/data` 达到 80% 必须告警并输出占用摘要，达到 85% 必须先清理再继续普通部署，达到 90% 阻断非应急部署。发布后清理是主线，还必须配置周级兜底清理 cron，避免部署中断后 build cache、旧镜像和旧 Harbor blob 长期堆积。

服务器 `deploy-release.sh` 会登录 Harbor、重写所选应用镜像 tag、执行 `docker compose pull` 和 `docker compose up -d`，最后探测 Web 首页。按需发布会先从当前 release 读取未选服务镜像，避免 `.env` 被旧 tag 覆盖；如果目标机已有旧部署但还没有 `releases/current-release.env`，脚本会用服务器 `.env` 作为初始镜像基线并写入 release manifest，不需要把 `services` 留空。部署完成后会写入 `releases/current-release.env`、`previous-release.env`、`staged-release.env`、`current-release.summary.md` 和 `history/`。

## 单独本机构建

需要只构建并推送镜像、不触发部署时使用：

```bash
cd AICopilot
./deploy/enterprise-ai/build-and-push.sh --services httpapi,migration,dataworker,ragworker,web
```

单镜像 build/push 默认 15 分钟超时，Harbor 检查默认 2 分钟超时；超时必须停止并诊断 `docker buildx ls`、`docker system df` 和 Harbor tag，不得等待灾备 GitHub workflow。

## 服务器应急部署

仅当本机 SSH 触发器不可用时，在服务器执行：

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
