# AICopilot enterprise-ai deploy

本目录是 AICopilot 镜像构建和旧事务维护实现目录。新接手日常部署先读工作区根 `deploy/README.md` 和 `deploy/Deploy.ps1`，再按需查看本文件、仓库根目录的 `AICopilot 项目部署与维护指南.md`、`AGENTS.md` 和 `资料/AICopilot业务规则.md`。

> 当前状态（2026-07-10）：新日常入口/Runner 已通过 fake 远端仓库、脏本地工作树、不可变 OCI、一次 SSH、migration 自动闭包、PostgreSQL 备份、失败回滚和不重建续传回归；旧原子事务回归仍保留。尚未完成真实 Harbor/SSH/生产容器 E2E，因此不能称生产部署已验收。

本目录按双层口径维护：

- 长期模板/规则：描述 Harbor/SSH/non-root/HTTP-only 的标准链路，不写真实 secret。
- 当前生产现场口径：当前标准部署根目录是 `/srv/enterprise-ai/deploy`，稳定日常 Runner 是 `runner/iiot-release-runner.sh`，Docker Root Dir 是 `/data/docker`；`releases/routine-*` 与备份目录必须保持 non-root 可访问。旧 support files 只在基础设施维护时同步。
- 当前与 Cloud 共用同一台生产宿主机，但部署根独立；共享宿主机事实、当前标准发布账号和 Cloud 根目录统一以工作区 [`docs/上传部署总览.md`](../../../docs/上传部署总览.md) 为准。AICopilot 当前未因同类权限问题失败，但必须和 Cloud 一样维持 release state / support files 的 non-root owner/mode 门禁。

## 部署口径

- 生产环境使用 Docker Compose 单机编排，服务器目录为 `/srv/enterprise-ai/deploy`。
- 工作区日常标准发布走 `pwsh ./deploy/Deploy.ps1 -Target AICopilot ...`；本目录 `build-and-push.sh` 只负责镜像，`local-release.sh` / `deploy-release.sh` 只保留给基础设施维护和旧链恢复。
- 日常入口取 `origin/main` tip 建 detached worktree；本地业务工作树可以继续迭代且不会被修改，未推送改动不会发布。每次日常远端阶段只有一次 SSH。
- AICopilot 选择 HttpApi/DataWorker/RagWorker 时自动包含 migration。Runner 在 migration 前生成 PostgreSQL dump/checksum，随后只对选中应用服务执行 `--no-deps` 更新；PostgreSQL、RabbitMQ、Qdrant 不随应用发布重建。
- Runner/Compose/scripts/cloud-readonly 支持文件升级、深度安全巡检、cleanup 和 Harbor GC 均是独立维护任务，不得塞回日常应用热路径。
- 多 agent 可以同时准备本地候选，但远端 support install、release state、容器变更和 cleanup 必须由托管锁串行化；第二个发布遇到 active lock 时立即返回 `75`，不得静默等待或绕锁重发。
- `aicopilot-image` / `aicopilot-deploy` 只保留带确认词的灾备入口；日常生产发布不得等待这些 workflow。
- 单个镜像 build/push 默认 15 分钟超时，Harbor 登录/API 检查默认 2 分钟超时，SSH deploy 默认 30 分钟超时；超时必须停止并按脚本输出诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续 watch 或无限等待。
- 灾备 workflow 的 runner 必须使用专用非 root 用户，并带 `iiot-linux-prod` label；不得改回 GitHub hosted runner 执行镜像构建或部署。production/secrets 相关灾备 workflow 和 runner 机器侧都必须执行 `scripts/check-runner-security-attestation.sh`，验收非 root、工作目录、Docker Root Dir 和部署目录。
- 灾备 workflow 当前仍依赖 GitHub production environment secrets；它只允许最小仓库权限和非 root runner。runner 机器权限收敛、短期凭据或 Vault/OIDC 接入必须单独作为基础设施项验收，不能用 workflow 代码改动替代；runner 脚本只证明本机事实，不证明 GitHub environment secrets 或 Vault/OIDC 已完成。
- 应用镜像和基础镜像全部来自 Harbor，不从 Docker Hub/MCR 作为生产依赖源直接拉取。
- AICopilot 应用镜像不保留历史版本；Harbor 和服务器本机只保留当前生产正在运行的 `sha-*` 应用镜像。
- 日常本地构建 + SSH 标准链使用服务器预置且 mode `0600` 的真实 `.env`，support sync 明确排除它；GitHub secret `DEPLOY_ENV_FILE` 只服务灾备 workflow。真实 `.env` 不提交到仓库。
- 当前内网部署红线是 HTTP-only。部署脚本、compose、nginx 模板和验收命令不得把 HTTPS redirection、HSTS、443 listener、证书申请/续期或 OIDC HTTPS metadata 强制校验作为当前门槛；安全加固改走内网隔离、端口收敛、同源代理、CORS 白名单、强 secret、短期 token、非 root 容器、只读边界和除 HSTS 外的安全响应头。
- AICopilot 对 Cloud 业务数据保持只读边界；不得通过 MCP、Tool、Agent workflow、后台任务或隐藏适配器写 Cloud。
- 当前标准 non-root 发布还要求 `releases/current-release*`、`staged-release*`、`previous-release*`、`current-release.summary.md` 和 deploy support files 对标准部署用户可读可写；root 应急路径一旦写入这些状态，关闭任务前必须恢复 owner/mode。
- AICopilot 日常真实慢路径只剩选中镜像 build/Harbor push、migration 和健康检查，不存在等价的“HTTP 上传限速 1000M”概念。support sync、深度 attestation 和 cleanup 已拆出。
- 正式日常发布只构建 fresh remote tip 的固定 Git SHA，并使用独立 detached worktree；不得读取或要求清理正在被其他 agent 修改的原工作树。
- 每次发布使用独立 run id 和私有 services/image/support manifest；`dry-run`、另一个 agent 或另一次发布不能覆盖当前发布的控制文件。
- `docker-compose.yaml`、服务器脚本、`cloud-readonly/` 和 `scripts/` 作为同一份 digest-bound support release staging 安装；`.env`、`releases/`、`.locks/` 和备份不进入同步包。
- support install 取得的 reservation token 必须由同一次 `deploy-release.sh` 接管，并把 support digest 写入 release manifest/summary；digest 不一致必须在 `.env` 或容器变更前返回 `65`。
- `.env`、current/previous/staged manifest 和 current summary 在发布事务内备份；健康/attestation 前失败必须恢复原状态。部署已经健康但 cleanup 失败时保留已提交 release，并原样返回 cleanup 退出码，不能伪装成超时。
- 只有 watchdog 实际触发时返回 `124`；普通 git、dotnet、Docker、SSH、preflight、support、attestation 或 cleanup 失败必须保留真实退出码。TERM/INT 必须清理本地命令树和自己持有的远端锁。
- support/state/runtime 恢复不完整或阻断证据无法正常落盘时统一返回 `86`，保留 transaction/support backup 和至少一份 durable marker；发现该状态后 support 安装和正式发布都必须 fail-closed。
- 即使 unsafe marker 和 `blocked-release.env` 都写入失败，无法被正常 live reservation 证明所有权的任何 `.support-backups/<token>` 也是独立阻断证据；对应锁尽力转成不自动 stale 的 `blocked` 状态，人工核对前不得删除 backup/lock 或重试。
- SSH 超时或断联不能直接判定部署失败。服务器按 token 持久化 invocation 终态并受 deadline 约束；本地只能在查询到明确终态后结束。轮询后仍 active/unknown 时返回 `87`，禁止自动取消或重试。

## 目录文件

```text
deploy/enterprise-ai/
  .env.example            # 生产 .env 模板，只保留占位密钥
  build-and-push.sh       # 统一入口内部调度的本机镜像构建实现
  local-release.sh        # 旧事务/基础设施维护的固定提交发布实现
  deploy-release.sh       # 服务器发布脚本，支持全量和 --services 按需发布
  docker-compose.yaml     # 生产 compose 模板
  mirror-base-images.sh   # 基础镜像同步到 Harbor
  runner-platform-attestation.template.md  # runner/GitHub/OIDC 平台侧验收记录模板
  cloud-readonly/         # Cloud PostgreSQL readonly role 授权和探针 SQL
  scripts/                # CloudReadOnly 授权、模型 provider smoke、runner、平台记录和发布后安全验收脚本
  scripts/release-common.sh  # active/stale lock、SHA256 和 timeout 进程树公共原语
  scripts/install-support-release.sh  # staging + manifest 校验 + 原子 support 安装
  scripts/cancel-support-reservation.sh  # 按 token 原子取消仍为 reserved 的 support 安装
  scripts/query-release-invocation.sh  # SSH 断联后的只读 invocation 终态查询
  scripts/check-release-state-access.sh  # release state owner/mode 非 root 门禁
  tests/deployment-behavior.sh  # fake Docker/SSH/remote 行为回归
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
标准 non-root 发布还要求 `releases/current-release*`、`staged-release*`、`previous-release*`、`current-release.summary.md` 和 deploy support files 对标准部署用户可读可写；root 应急路径一旦写入这些状态，关闭任务前必须恢复 owner/mode。

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

Cloud OIDC 首部署管理员收编由 `CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED` 控制，生产模板和 compose fallback 均默认关闭。只有在已核对目标本地 emergency Admin 与 Cloud `employee_no` 的短时首部署窗口中才能显式设为 `true`，绑定完成后必须立即恢复 `false`。它只允许复用 `AICOPILOT_BOOTSTRAP_ADMIN_USERNAME` 指定的本地 Admin；普通同名用户仍拒绝自动绑定，Cloud role 也不会映射为 AI role。

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

## 目标标准发布（当前仅隔离行为回归）

正式 `local-release.sh`（非 `--dry-run`）只接受工作区统一入口调度，必须同时携带
`IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1`、`IIOT_WORKSPACE_DEPLOY_INVOCATION_ID`、
`IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA`、`IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST`、
`IIOT_WORKSPACE_DEPLOY_PLAN_FILE` 和 `IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST`。项目脚本会重算 plan 文件字节 digest、
核对 plan 内 `profileDigest`，并校验 expected SHA 等于固定源码 SHA 和 fresh `origin/main` tip。
invocation、plan、profile、services/image/support manifest digest 会绑定到同一次远端 reservation。
缺少或漂移时必须在 `.env`、release state 或容器变更前拒绝。
`--dry-run` 和 `tests/deployment-behavior.sh` 均会显式输出
`NON_PRODUCTION_MECHANISM_TEST productionEligible=false`，不得当作生产验收证据。

固定 SHA 镜像 tag 必须经 OCI inspect 解析为 `repository@sha256:<digest>`。run-private image manifest
同时保存 tag、OCI digest 和 immutable ref，远端只消费 immutable ref。`--check-current` 是纯只读：
不创建 network、不执行 Docker network connect、不创建 release lock，并必须同时匹配 plan/profile、
services/image/support digest、服务器 `.env` canonical fingerprint 和选中运行容器的实际 image id。
fingerprint 覆盖除五个应用镜像键和部署瞬时键外的全部有效 `.env` 行，包括 secret 轮换；
状态只保存 SHA256 fingerprint，不保存、打印或生成包含 secret 原文的中间文件。
fingerprint 发生变化时禁止部分服务发布，必须选择全部五个应用服务，使基础设施、migration 和全部 runtime 在同一候选下重新验证。
migration 作为 one-shot 单独记录完成事实。

容器已部分更新后失败时，未执行 migration 的可逆场景会恢复 previous manifest 并重做健康验证。
已执行 migration 或恢复失败时，会写入 `releases/blocked-release.env`、保留 transaction backup 并阻断自动重试。
support 安装会先校验 staging、新旧 manifest 安全路径和 shell 语法，旧 support backup 保留到整个 deploy 提交。
安装或发布失败时先恢复旧 support/compose，再恢复 release state，并重新 pull/up/验证 postgres、eventbus、qdrant 及四个常驻 runtime；任何未选中 runtime 验证失败也不得宣称恢复成功。事务开始前必须先从真实容器读取 `Config.Image` 和 `.Image` image id，再对该 image id 执行 image inspect，从多个 `RepoDigests` 中按 `Config.Image` repository 精确选择 immutable ref，不得从 container 对象读 `RepoDigests` 或盲取第一项。三个基础服务的 immutable ref 和 runtime image id 会写入 transaction/release state；回滚使用该冻结身份，不重新解析可变 tag。
reservation adopt/cancel 通过带 PID、process-start、owner 和 TTL 的原子 transition 目录争用；只要 PID+process-start 仍证明 transition owner 存活，即使 transition 或父 reservation TTL 过期也不得回收，TTL 只用于回收死亡/孤儿 transition。support 安装期间锁为 `active/support-installing`，全部文件验证完成后才转成 `reserved/support-ready`。cancel 只能恢复并释放自己 token 且仍处于 reserved 的任务，旧 token 不能删除新任务的锁。

选中的常驻容器在 no-op 和 commit 前必须两次验证 Running=true、Restarting=false、OOMKilled=false、
RestartCount 在稳定窗口内不增长；已声明 Docker Health 的服务还必须为 healthy。
Web/HttpApi 继续使用 HTTP 探针和 security attestation。DataWorker/RagWorker 目前没有独立业务 health endpoint，
剩余边界是只能证明进程运行、无 OOM/restarting 且重启计数稳定，不能冒充业务处理已健康。

上述 marker/digest 是受控工作流内的 fail-closed 一致性边界，不是对同一用户 shell 中恶意进程的密码学信任边界。
生产仍依赖 runner 权限隔离、Harbor/SSH 凭据管理和服务器文件权限。

1. 推送代码到 GitHub，保证目标改动已经进入 `origin/main`。
2. 在工作区根运行 `pwsh ./deploy/Deploy.ps1 -Target AICopilot -Services <services> -Deploy`；入口 fetch 远端 tip 并创建隔离 worktree，不检查或修改本地业务工作树。
3. `build-and-push.sh` 从隔离 worktree 按服务构建并推送不可变 OCI 镜像；HttpApi/DataWorker/RagWorker 自动加入 migration。
4. 入口通过一次 SSH 发送私有 digest-bound request；稳定 Runner 串行执行备份、migration、`--no-deps` rollout、健康检查、失败回滚和 history。
5. `services` 必须显式传入，例如 `httpapi,web`；全量发布显式传 `-All`。不要直接调用项目脚本。
6. 构建后远端失败时使用 `Deploy.ps1 -Deploy -ResumeInvocation <id>` 重发同一请求，不重新 build。Runner/Compose/support/cleanup/GC/深度 attestation 只在独立维护窗口执行。

应用镜像仓库只保留当前生产 `sha-*` tag。本机构建推送候选 tag 后，不立即删除当前生产 tag；稳定 Runner 健康提交后由独立定时维护清理旧 tag 并执行或确认 Harbor GC。清理失败不得改写健康应用发布结果。`buildcache` 和基础镜像 tag 不计入应用版本保留。

部署成功并完成服务器验证后，必须清理 Docker/BuildKit build cache、服务器本机未被当前容器引用的旧 AICopilot 应用镜像，并输出清理前后磁盘摘要。Docker 管理镜像和 containerd 管理内容必须分开统计、分开清理；containerd 侧未确认 namespace、image ref、snapshot lease 和运行容器引用前不得强删。基础镜像、数据库卷、Qdrant/RabbitMQ/PostgreSQL 数据、备份、配置和 secrets 不清理。回滚不依赖旧镜像保留；需要回滚时重新构建或重新拉取目标 git sha 后部署。
Harbor tag retention 和 Harbor GC 需要服务器 `.env` 显式提供 `HARBOR_USERNAME/HARBOR_PASSWORD` 或 `OCI_REGISTRY_USERNAME/OCI_REGISTRY_PASSWORD`；未配置时 post-release cleanup 会跳过 Harbor API 清理但不阻断已健康的应用部署。需要把 Harbor API 清理变成硬门禁时，设置 `POST_RELEASE_HARBOR_RETENTION_REQUIRED=1` 或 `POST_RELEASE_HARBOR_GC_REQUIRED=1`。

`/data` 达到 80% 必须告警并输出占用摘要，达到 85% 必须先清理再继续普通部署，达到 90% 阻断非应急部署。发布后清理是主线，还必须配置周级兜底清理 cron，避免部署中断后 build cache、旧镜像和旧 Harbor blob 长期堆积。

服务器必须预先具备 Harbor pull 凭据；本机构建脚本按需执行 `docker login`，服务器 `deploy-release.sh` 本身只重写所选应用镜像 tag 并执行 `docker compose pull`，不伪装成会自动登录 Harbor。全量发布时先启动基础服务、运行 `aicopilot-migration`，并在启动 HttpApi/DataWorker/RagWorker/Web 之前检查模型和 Embedding API key 已全部迁移到 `encv2:`。按需发布会先从当前 release 读取未选服务镜像，避免 `.env` 被旧 tag 覆盖；如果目标机已有旧部署但还没有 `releases/current-release.env`，脚本会用服务器 `.env` 作为初始镜像基线并写入 release manifest，不需要把 `services` 留空。按需发布包含 `httpapi`、`dataworker` 或 `ragworker` 时必须同时包含 `migration`，由 migration worker 在 runtime 启动前重新迁移并验证 `encv2:` 密文可用当前密钥解密；web-only 发布可以不带 `migration`。服务启动后会探测 Web 首页、HTTP-only 安全头，并自动运行 `scripts/check-release-security-attestation.sh` 验收 Cloud OIDC 状态接口、Web 非 root 和 API key 密文迁移。部署完成后会写入 `releases/current-release.env`、`previous-release.env`、`staged-release.env`、`current-release.summary.md` 和 `history/`，其中 summary 会包含 release security attestation 输出。

## 单独本机构建

需要只构建并推送镜像、不触发部署时使用：

```bash
cd AICopilot
REGISTRY=harbor.internal.example:80 \
CLOUD_PLATFORM_URL=http://cloud.internal.example:81 \
  ./deploy/enterprise-ai/build-and-push.sh --services httpapi,migration,dataworker,ragworker,web
```

单镜像 build/push 默认 15 分钟超时，Harbor 检查默认 2 分钟超时；超时必须停止并诊断 `docker buildx ls`、`docker system df` 和 Harbor tag，不得等待灾备 GitHub workflow。

行为回归脚本根据自身路径定位 AICopilot Git 仓库，不依赖调用者当前目录。从工作区根执行：

```bash
bash AICopilot/deploy/enterprise-ai/tests/deployment-behavior.sh
```

从 AICopilot 仓库根执行时也可使用 `bash deploy/enterprise-ai/tests/deployment-behavior.sh`。

该回归使用隔离 clone、fake Docker、fake SSH 和 fake remote，验证成功发布、普通失败真实退出码、真超时 `124`、无 watchdog orphan、并发/transition 锁、旧 token 隔离、support digest/path/symlink 拒绝、ACK 丢失终态对账、active/unknown 禁止取消、全局配置漂移门禁、unsafe-partial `86`、全 runtime 恢复验证、signal cleanup 和同 SHA 幂等；不连接 Harbor 或生产服务器。

## 服务器应急部署

服务器 `deploy-release.sh` 不再接受无候选绑定的手工发布。本机 SSH 触发器不可用时，
也必须由获批的统一入口产生完整 candidate/reservation 参数后再触发；下面旧的无绑定命令已禁用：

```bash
# 禁止：缺少 workspace marker / invocation / expected SHA / plan digest / reservation
./deploy-release.sh sha-<git-sha> --services migration,httpapi,web
```

## 服务器内部环境诊断（非对外入口）

对外标准环境校验使用工作区 `Deploy.ps1 -Target AICopilot -Doctor`。只有获批排障时，才在服务器内部运行旧 `.env` 校验；该命令不能作为 AI 绕过统一入口的发布前替代链：

```bash
cd /srv/enterprise-ai/deploy
./deploy-release.sh --validate-only
```

该命令会 fail-fast 校验 `.env` 权限、模板占位、弱 secret、HTTP-only URL、Cloud OIDC 内网 HTTP issuer、必填 secret、direct Cloud readonly 配置，以及 `releases/*` owner/mode 对标准 non-root 路径是否仍可读可写。公共 HTTP OIDC 域名、HTTPS URL、HSTS/证书强制项、弱密码、空 token 或 root-owned release state 都必须先修正再发布。

## API Key 密文迁移验收

发布包含密钥迁移代码的版本时，必须显式运行 migration 服务，不能只重启 HttpApi：

```bash
cd <workspace-root>
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Services migration -Deploy
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
