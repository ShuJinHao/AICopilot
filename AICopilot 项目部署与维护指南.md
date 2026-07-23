# AICopilot 部署与维护指南

> 读取边界：日常发布只读标准入口和本次命中的服务章节；历史状态、旧事务、灾备和未命中的维护章节不默认读取。

本文档是 AICopilot 当前项目级部署说明。日常增量入口是工作区根 `deploy/Deploy-Changed.ps1`，三端从零入口是 `deploy/Deploy-FromZero.ps1`；`Deploy.ps1` 只作为内部服务执行器、Doctor/DryRun 和显式恢复入口。

> 当前状态（2026-07-11）：AICopilot 全量应用已完成真实 Harbor、生产 Runner、PostgreSQL 备份、migration、rollout 与健康检查；自动增量入口的编译门禁、依赖影响测试和生产 SHA 只读 inspect 已通过，但尚未用新的单服务业务变更执行生产发布，因此不得把全量成功冒充增量生产 E2E。

工作区标准入口示例：

```powershell
pwsh ./deploy/Deploy-Changed.ps1 -Targets AICopilot # 日常唯一发布入口
pwsh ./deploy/Deploy-FromZero.ps1 -Targets Cloud,AICopilot,Edge -ConfirmFromZero # 三端从零唯一入口
pwsh ./deploy/Deploy.ps1 -Target AICopilot -InstallRunner # 仅首次或 Runner 升级
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Doctor
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Services httpapi,web -DryRun
# Deploy.ps1 -Deploy 只供统一入口内部调用或显式恢复，不是第二个日常入口
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
- 标准日常发布要求 clean、已提交的 `main`，只允许 push 现有 HEAD，禁止创建提交或修改 tracked 文件；复用同 SHA 绿色证据，只补受影响 Architecture/Security/DeploymentContract，再按依赖闭包发布受影响镜像。不得运行全量、coverage、mutation、duplication 或 CrossProject；失败只停止报告，不修代码。
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
- 日常链使用服务器预置且 mode `0600` 的真实 `.env`；support sync 不上传或覆盖它。本机生产密钥唯一 canonical 来源是 macOS Keychain，Markdown、旧 `~/.config` env 和 GitHub `DEPLOY_ENV_FILE` 不作为标准流程 fallback。
- 当前内网生产部署红线是 HTTP-only。AICopilot 当前修复和发布不得强制引入 HTTPS redirection、HSTS、nginx 443 listener、证书申请/续期或 OIDC HTTPS metadata 校验；如果未来要切 HTTPS，必须由用户单独批准传输层方案和证书来源。HTTP 部署下仍必须执行内网隔离、端口收敛、同源代理、CORS 白名单、强 secret、短期 token、非 root 容器、只读边界和除 HSTS 外的安全响应头。
- AICopilot 的“慢”不得被误写成 HTTP 上传限速问题。日常真实慢路径是选中镜像 build/Harbor push、migration 和 health；support sync、深度 attestation 和 cleanup 已拆出。
- Docker Hub 不作为生产依赖源，MCR 也不得作为生产构建的直接依赖源；PostgreSQL、RabbitMQ、Qdrant、.NET ASP.NET runtime、Node、Nginx 基础镜像必须先 mirror 到 Harbor。
- AICopilot 默认保持 Cloud 只读边界，不能注册、修改、删除或触发 Cloud 业务数据。
- Cloud OIDC 只用于身份对齐；AICopilot 保留本地 AI 用户、AI 角色、AI 权限、审计和 emergency admin。

### 1.1 从零部署中的 AICopilot 阶段

`Deploy-FromZero.ps1` 在任何远端写入前核对 canonical Keychain schema；缺少 Cloud readonly、AI 数据库、JWT/OIDC、Harbor、模型或管理员根密钥时只报告键名并停止。通过预检后，AICopilot 阶段固定执行：

1. 在 Cloud 已完成 migration/seed 且 release history 已恢复后，创建并验证专用 readonly role/授权和连接配置。
2. 创建所需外部网络与 AICopilot 基础设施，执行 AICopilot migration。
3. 用 Keychain 中的真实模型配置执行 seed，并校验受保护密钥格式。
4. 验证 Cloud OIDC、Cloud readonly 负权限、模型、常驻容器和 HTTP 健康。

每阶段写 checkpoint；失败恢复不得重新清空 Cloud/AI 数据。此入口只验收配置、迁移、播种、权限和健康，不运行普通业务测试、全量、coverage、mutation、duplication 或三端联合质量。

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

真实生产主机、Harbor、Cloud、数据库、模型和管理员密钥在本机只存 macOS Keychain，并由部署生成服务器受限 `.env`；本指南和 `.env.example` 只使用占位值。

真实 `.env` 由从零部署按无值 canonical secret schema 从 Keychain 生成并以 mode `0600` 保存到服务器 `/srv/enterprise-ai/deploy/.env`。标准流程不要求用户手工查询或拼接 Cloud 数据库密码；GitHub secret `DEPLOY_ENV_FILE` 只服务显式灾备。

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

持久化文件对账（默认值已在 compose 和 `.env.example` 对齐）：

```text
AICOPILOT_PERSISTENCE_MAINTENANCE_INTERVAL_SECONDS=300
AICOPILOT_PERSISTENCE_RECONCILIATION_DELAY_MINUTES=10
AICOPILOT_PERSISTENCE_MARKER_RETENTION_DAYS=30
AICOPILOT_PERSISTENCE_MAINTENANCE_BATCH_SIZE=100
```

HttpApi 与 DataWorker 必须共享 `/var/lib/aicopilot` 持久卷；compose 中 `FileStorage__RootPath` 与 `ArtifactWorkspace__RootPath` 固定在该卷下，不允许用 `.env` 覆盖到容器层或其他未共享目录。上传流程先写 `.persistence/file-reconciliation`，再写物理文件和数据库；请求侧持有以 commit id 派生的 PostgreSQL advisory lease，DataWorker 只有取得同一 lease 且记录超过安全延迟后才能对账，活跃上传必须跳过。有提交标记时保留文件，无标记时删除未提交文件；日志无法读取时必须停止标记过期清理。不得把 DataWorker 从共享卷移除，不得用 cron 或手工 `rm` 另起一套清理链。
RagWorker 必须挂载同一卷；文档删除事件会按 storage path 查 pending journal、争用同一 commit lease 并在锁内退休 journal 后删文件，journal 不可读或 lease 活跃时必须由消息重试。该共享卷是受信任后端内部状态，禁止挂给不受信任写入者；现有 symlink/reparse 检查不等同于抵御同 UID 恶意竞态的文件系统沙箱。
当前 durable local file/journal backend 只支持 Linux 与 macOS；标准生产路径是本节 Linux 容器。Windows 进程会明确拒绝构造该 backend，不能把 `MoveFileEx` 或未刷父目录的删除包装成耐久提交；如需 Windows 原生运行，必须先实现独立受治理 storage backend 并补齐崩溃恢复测试。

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

DataAnalysis direct Cloud readonly DB 的生产连接配置、模式开关和 readonly role
不再通过 GitHub production environment secret 或手动 workflow 写入。新环境或清空重建统一由
工作区 `deploy/Deploy-FromZero.ps1` 从 macOS Keychain canonical schema 生成受限服务器
`.env`、建立并验证只读授权；普通增量部署只消费既有配置，不修改它。该路径只注册
AICopilot DataAnalysis `CloudReadOnly` 数据源，必须使用已验证只读账号，不直连写 Cloud 业务数据。
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
`deploy/enterprise-ai/scripts/apply-cloud-readonly-grants.sh` 和
`deploy/enterprise-ai/scripts/check-cloud-readonly-grants.sh` 只作为统一从零入口的内部实现，
或在用户明确批准的独立基础设施维护中由服务器受限 `.env` 调用；它们不是第二套标准操作入口，
不得读取 GitHub secrets、生成本机 canonical 密钥或维护内联 GRANT 清单。

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

生产清空重部署的新库会由 migration worker 播种一个私有 OpenAI-compatible 模型。仓库模板只保留占位 URL 和禁用状态，真实值必须预存在 macOS Keychain，并由从零部署写入服务器受限 `.env`：

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
-> 工作区根 Deploy-Changed.ps1 确认 clean main、HEAD 已推送且 tracked 文件零变化
-> local-release.sh 为该 SHA 创建 detached worktree 和本次 run 私有目录
-> 从固定快照构建镜像并生成私有 services/image/support manifest
-> push <harbor-registry>/enterprise-ai/*:sha-<git-sha>
-> support staging 的 SHA256、reservation token、全局锁和 deploy-release digest 绑定
-> 统一入口通过 SSH 调用 /srv/enterprise-ai/deploy/deploy-release.sh
-> 服务器 pull/migration/model-secret-preflight/up/health/release-security-attestation/cleanup/history
```

`build-and-push.sh` 必须显式接收 `--services httpapi,migration,dataworker,ragworker,web` 的子集或 `--all`，但只由统一入口内部调度；无参数直接失败。选择 `httpapi`、`dataworker` 或 `ragworker` 时会自动加入 `migration`。正式发布的 `Deploy services input`、image manifest 和 support manifest 只写本次 run 私有目录，`local-release.sh` 只读取同一次运行的清单；不得再用共享 `artifacts/deploy/aicopilot-built-services.txt` 控制发布。

后端服务使用同一份源码快照但不共享 SDK 中间产物：统一入口把源码 detached worktree 放在工作区 `artifacts/deploy/routine/<invocation>/snapshot-worktree`，`build-and-push.sh` 再为每个 service 传入同一 invocation 输出目录下独立的 `--artifacts-path <output-dir>/service-build/<service>`。源码和 SDK artifacts 都不得落入 macOS `TMPDIR=/private/var/folders/...`，避免 `/private/var` 与 `/var` 路径别名让 MSBuild 把同一项目识别为不同输入。禁止去掉该隔离、改回共享 `bin/obj` 或用“先构建 HttpApi”之类顺序假设止损；连续构建 DataWorker 后再构建 HttpApi 也必须使用各自依赖图。

### 4.1 不可变候选、幂等与恢复

- 正式发布必须先由工作区入口 `CheckCandidate` 生成只读 plan，再用同一个完整 SHA、plan digest、profile digest 和显式服务闭包执行 `Deploy`；项目脚本不得直接作为第二入口。
- 应用镜像使用 immutable OCI ref。事务开始前同时冻结 PostgreSQL、RabbitMQ、Qdrant 的真实 RepoDigest/runtime image id；回滚按冻结身份恢复，不重新解析可变 tag。
- 同 SHA 的 no-op 还必须满足 support/services/image digest、服务器配置 fingerprint、运行镜像身份和全部常驻容器稳定；配置 fingerprint 漂移时普通部署停止，转独立配置维护或从零部署，不自动扩大为全量。
- 当前生产 Harbor 是内网 HTTP。镜像推送成功后先尝试标准 OCI inspection；若 `buildx imagetools inspect` 因 HTTPS 假设失败，构建器使用 `docker manifest inspect --insecure --verbose` 并只提取唯一 `linux/amd64` descriptor digest。仍必须以 `image@sha256:...` 请求服务器，禁止把 HTTP fallback 变成 tag 部署。
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

GitHub 只读生产状态 inspect 只需要：

```text
DEPLOY_TARGET_DIR=/srv/enterprise-ai/deploy
```

标准发布的 Harbor、模型、管理员、Cloud readonly 与数据库凭据全部来自 macOS Keychain；不从 GitHub `DEPLOY_ENV_FILE` 或 Cloud readonly secrets fallback。Emergency workflow 的独立配置只在用户明确授权灾备时按该 workflow 检查，不是普通部署准备项。

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
pwsh ./deploy/Deploy-Changed.ps1 -Targets AICopilot
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
pwsh -NoProfile -File scripts/tests/Select-AICopilotCiTests.ps1 `
  -Mode Default `
  -BaseRef <ancestor-sha> `
  -OutputPath artifacts/ci-test-selection.json
pwsh -NoProfile -File scripts/tests/Invoke-AICopilotCiSelectedTests.ps1 `
  -SelectionPath artifacts/ci-test-selection.json
```

上述入口按当前改动选择架构、安全和受影响业务测试，并以本次 TRX 动态发现数完成
`discovered = executed = passed` 对账。`Quality`、`Full` 和 `CrossProject` 只能在用户明确要求时显式选择；
普通部署不得用它们替代发布验证。

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
