# AICopilot 安全部署契约

本文档是 AICopilot 部署安全的专题契约。总计划见 `docs/AI架构治理清单.md`，项目规则见 `AGENTS.md` 和 `资料/AICopilot业务规则.md`。

## 1. 部署红线

- 当前内网生产部署必须保持 HTTP-only。
- 当前修复、脚本、nginx 模板、compose、README、测试和发布验收不得强制引入 HTTPS redirection、HSTS、nginx 443 listener、证书申请/续期或 OIDC HTTPS metadata 校验。
- 未来如需切换 HTTPS，必须由用户另行批准传输层方案和证书来源，并重新定义本契约。
- HTTP-only 不等于放松安全；必须继续执行内网隔离、端口收敛、同源代理、CORS 白名单、强 secret、短期 token、非 root 容器、Cloud 只读边界、敏感信息脱敏、除 HSTS 外的安全响应头和发布前 preflight。

## 2. 源码归属

- Web nginx：`src/vues/AICopilot.Web/nginx.conf.template`。
- Web Dockerfile：`src/vues/AICopilot.Web/Dockerfile`。
- 后端 runtime base：`deploy/enterprise-ai/Dockerfile.backend-runtime`。
- compose 和部署模板：`deploy/enterprise-ai/docker-compose.yaml`、`deploy/enterprise-ai/.env.example`。
- 发布脚本：`deploy/enterprise-ai/deploy-release.sh`、`deploy/enterprise-ai/local-release.sh`、`deploy/enterprise-ai/build-and-push.sh`、`deploy/enterprise-ai/mirror-base-images.sh`。
- 发布验收脚本：`deploy/enterprise-ai/scripts/check-release-security-attestation.sh`、`deploy/enterprise-ai/scripts/check-model-secret-migration.sh`、`deploy/enterprise-ai/scripts/check-runner-security-attestation.sh`、`deploy/enterprise-ai/scripts/check-platform-attestation-record.sh`。
- 灾备 workflow：`.github/workflows/aicopilot-*.yml`。
- 部署门禁按职责拆分：静态安全边界在 `SecurityHardeningTests`，无发布副作用的环境 preflight 与 Shell Git mode 在 `DeploymentPreflightBehaviorTests`，Controller 组合根等结构约束在 `ArchitectureBoundaryTests`。

## 3. HTTP-only 安全头

Web 入口必须提供 HTTP 兼容安全头：

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Content-Security-Policy`，至少包含 `default-src 'self'` 和 `frame-ancestors 'none'`
- `Referrer-Policy`
- `Permissions-Policy`

Web 入口不得输出：

- `Strict-Transport-Security`
- `return 301 https://...`
- `listen 443 ssl`
- `ssl_certificate`

`check-release-security-attestation.sh` 和 `deploy-release.sh` 的 HTTP 探针必须同时验证安全头存在、`Strict-Transport-Security` 不存在、Cloud OIDC 状态接口可访问、Web 容器非 root、模型密钥迁移验收通过。

## 4. 同源代理和 CORS

- 标准生产访问路径是 Web nginx 同源 `/api/` 反代到 HttpApi。
- HttpApi CORS 默认不开放跨源。
- 确需浏览器直连后端时，只允许 `Cors:AllowedOrigins` 配置精确 origin。
- 禁止 `AllowAnyOrigin`、通配子域、带 path/query/fragment 的 origin 和运行时任意放行。

## 5. OIDC HTTP issuer

Cloud OIDC 使用 HTTP issuer 时必须满足全部条件：

- 显式启用 `ALLOW_INTRANET_HTTP_OIDC=true`。
- 显式关闭 `CLOUD_OIDC_REQUIRE_HTTPS_METADATA=false`。
- issuer host 只能是 loopback、私网 IPv4，或保留内网 DNS 后缀 `.internal.example`、`.internal`、`.lan`、`.local`。
- 公共 HTTP 域名即使开启内网 HTTP OIDC 也必须 fail-fast。

运行时边界由 `CloudOidcOptions` 执行，发布前边界由 `deploy-release.sh --validate-only` 和正式发布主路径执行。

## 6. secret 和默认值

- `.env.example`、compose、workflow 默认值、脚本默认值、migration seed、fresh DB seed、滚动复盘和历史诊断记录不得携带真实内网 IP、弱 secret、`CHANGE_ME`、`dummy-key`、默认 `root@` 发布目标或可直接使用的模型 API key。
- JWT 配置必须由 `JwtSettings.EnsureValid()` 在 HttpApi 启动时统一校验：Issuer、Audience 非空，SecretKey 至少 64 字符，AccessTokenExpirationMinutes 大于 0；绕过部署脚本直接启动也必须 fail-fast，错误不得回显 secret。默认 access token 有效期保持 30 分钟。
- 日常发布前 Doctor 必须校验 `.env` 私有权限、Compose 可解析、Docker/non-root、稳定 Runner 和 `releases/routine-*` / backup 目录可写性；模板占位、弱 secret、HTTP-only URL、Cloud OIDC、direct Cloud readonly 和 support 目标深度校验保留在 CI/独立基础设施维护门禁，不能每次重复同步 support。
- `deploy-release.sh --validate-only` 是不发布的配置校验入口；该模式不得拉镜像、不得执行 Docker Compose、不得改写 release tag，但必须提前暴露 root-owned release state 这类标准 non-root 路径问题。
- 模型、Embedding、endpoint pool API key 必须是 `encv2:` AES-GCM 受保护格式；旧 `encv1:` 只能由 migration worker 迁移重加密，runtime provider 不得长期兼容旧格式或明文。
- 私有模型 seed 的真实 `AICOPILOT_PRIVATE_MODEL_BASE_URL`、`AICOPILOT_PRIVATE_MODEL_API_KEY` 和启用状态只能来自服务器真实 `.env` 或本机非 git 私密手册；仓库默认使用 `model.internal.example` 占位 URL、空 API key 和禁用状态。生产标准 context window 是 `65536`，API key 播种入库前必须加密为 `encv2:`。

## 7. 镜像、SSH 和 runner

- AICopilot 生产镜像必须使用 Harbor mirror 基础镜像，不能默认从 Docker Hub 或 MCR 拉生产基础镜像。
- 应用和 Web 运行容器必须非 root。
- 日常标准发布路径是工作区 `Deploy-Changed.ps1` 自动 push Git、读取生产 SHA，按改动和依赖闭包只选择受影响镜像，再由 `Deploy.ps1` 从 fresh 远端 tip 隔离构建、推 Harbor 并请求稳定服务器 Runner。影响无法归属禁止退化全量；`local-release.sh` / `deploy-release.sh` 只保留给基础设施维护和旧事务恢复。
- 稳定 Runner 必须使用专用 non-root 部署用户；root 只允许一次性修复 owner/mode，不得进入日常应用发布。
- 当前如果与 Cloud 共用同一台生产宿主机，必须在工作区总入口明确共享宿主机事实、共享标准发布人和两个独立部署根；不得把 Cloud 根的权限漂移和 AICopilot 根的权限状态混写成同一个“整机问题”。
- root 应急路径如果写入了 `releases/*`、`current-release.summary.md` 或 deploy support files，关闭任务前必须恢复 owner/mode，并重新验证标准 non-root `./deploy-release.sh --validate-only`。
- GitHub `aicopilot-image` / `aicopilot-deploy` 只保留灾备入口，不是日常生产发布入口。
- workflow 使用的官方 JavaScript action 必须采用受支持的 Node 24 runtime 主版本，self-hosted runner 必须高于 action 声明的最低 runner 版本；升级前以官方 action metadata 和真实成功 job 的 runner 版本共同确认，不得仅消除 warning 后猜测兼容。
- PR 比较必须使用事件提供的 base commit SHA；手动运行只能接收严格校验后的仓库分支名并先解析成 commit SHA。workflow expression 只能经环境变量传入脚本，scope 与 whitespace 检查统一基于不可变 `baseSha...HEAD`，禁止把未校验 ref 直接拼入 shell。
- 被 workflow 或部署脚本直接执行的 `.sh` 必须在 Git 索引中是 `100755`；只由 `bash` 调用或被 source 的库/行为测试保持 `100644`。数据驱动部署行为测试必须覆盖 `deploy/enterprise-ai` 下全部 tracked `.sh`，新增脚本未分类即失败。
- self-hosted runner 机器权限收敛、OIDC/Vault 或等价短期凭据属于外部基础设施任务；AICopilot 仓库只能提供 workflow 边界、runner 本机 attestation、平台验收模板和记录 linter，不能伪造成平台治理已完成。
- 平台验收记录必须同时覆盖 GitHub production environment secret 限制、required reviewers、`contents: read`、`self-hosted + iiot-linux-prod`、生产/secret workflow 无 GitHub hosted runner、runner 本机脚本结果，以及 OIDC/Vault 已落地或已批准基础设施例外；记录 linter 只校验这些证据字段完整，不替代真实 GitHub、runner、Vault 或 OIDC 验收。

### 7.1 可重复和并发安全发布

- 日常正式发布必须从 fresh 配置远端 tip 创建隔离 detached worktree；原工作树允许脏且后续并发修改不得改变本次发布，未推送内容不得发布。
- 每次日常运行必须使用私有 services、image 和 digest-bound request；不得用 `artifacts/deploy/aicopilot-built-services.txt` 这类跨任务固定文件控制发布。每次远端阶段只能建立一次 SSH。
- 本地多服务镜像构建必须为每个 .NET 入口点使用服务私有 `--artifacts-path`，同一 SHA 只共享源码快照，不共享 `bin/obj`。构建顺序不得影响 ProjectReference 闭包、PackageReference 版本解析或编译输入。
- 日常应用发布不得同步 `docker-compose.yaml`、服务器脚本、`scripts/`、`cloud-readonly/` 或 Runner。support release 是独立基础设施维护任务，仍须用 staging/SHA256/旧事务锁，并禁止 `.env`、`releases/`、`.locks/`、备份进入同步包。
- support install reservation 与 `deploy-release.sh` 必须使用同一 token 和 support digest。远端 release manifest、summary、已安装 support manifest 和 lock metadata 的 digest 必须一致；任何不一致都必须在 `.env` 或容器变更前 fail-closed。
- AICopilot release lock 和共享 cleanup lock 必须记录 token、owner、PID、process-start、phase 和更新时间。active lock 立即返回 `75`；PID 已死亡、process-start 不匹配或 reservation 过期的 stale lock 允许安全回收。不得用静默轮询 15 分钟代替状态判断。
- `.env` 和 current/previous/staged release state 必须在容器变更前进入事务备份；健康检查或 attestation 前失败必须恢复原文件状态。已经健康并提交 current release 后 cleanup 失败属于“部署成功、清理失败”，不得回滚健康 release，也不得被包装成 `124`。
- timeout 必须终止命令的受监督本地进程树和 watchdog timer 树。只有计时器真实触发时返回 `124`；普通命令失败保留原始退出码。HUP/INT/TERM handler 必须显式退出并释放自己持有的锁，不得只删锁后继续执行；部署脚本不得主动创建新会话逃逸监督。
- 同 request digest 已运行且目标服务健康时必须幂等成功，不重复 migration 或容器重建。cleanup、Harbor GC 和深度 attestation 不在同步应用热路径。
- 部署行为测试入口必须根据脚本自身路径解析 AICopilot Git 根，不能依赖 AI 当前终端目录；从工作区根、AICopilot 仓库根或其它目录调用时必须测试同一固定提交/fake 外部依赖链，不能因工作区根不是 Git 仓库而误报 `128`。
- 日常入口内部生成 invocation、远端 SHA、服务闭包、OCI digest 和 request digest，操作者不再手工复制 plan path/SHA/digest。选择 HttpApi/DataWorker/RagWorker 时必须自动包含 migration；请求 digest 是误操作和传输完整性门禁，不是抵御已控制部署账号攻击者的密码学边界。
- 应用镜像必须使用 immutable `repository@sha256`；事务开始前还必须冻结 PostgreSQL、RabbitMQ、Qdrant 的真实 Config.Image、匹配仓库的 RepoDigest 和 runtime image id，并写入 transaction/release state。失败恢复只能使用冻结身份，不能重新解析可能移动的基础设施 tag。
- no-op/commit 必须同时满足 SHA、plan/profile、support/services/image digest、全局 `.env` canonical fingerprint、应用/基础设施实际镜像身份和常驻容器稳定性。canonical fingerprint 只落 SHA-256，不得打印或落地 secret；fingerprint 漂移时禁止部分服务发布。
- support installer、cancel、deploy 必须通过带 PID/process-start/owner/token 的原子 transition 串行化；live transition 不因 TTL 过期被回收，旧 token 不得删除新锁。恢复失败或 blocked evidence 写入失败时，残留 backup/永久 blocked lock 本身必须持续阻断并返回 `86`。
- SSH timeout/断联不得只根据本机 ssh 退出码判断失败或重试；必须按 invocation token 查询远端 terminal state。明确成功可收口，明确失败保留真实失败码，active/unknown 返回 `87` 并禁止取消、重试或并发接管。
- 回滚必须恢复并复验 support、compose、release state、PostgreSQL、RabbitMQ、Qdrant、HttpApi、DataWorker、RagWorker 和 Web；进程 Running、非 Restarting、非 OOM、RestartCount 稳定，且已有 Health 为 healthy 才能称恢复完成。数据库 migration 已执行后的失败保持 partial，禁止脚本猜测回滚数据库。
- `deploy-release.sh` 通过 `EXIT` trap 执行容器恢复；恢复链禁止 bare `return`，提前成功返回必须显式 `return 0`。恢复函数必须在可捕获的子 Shell 中运行，确保内部健康等待、Web 探针、安全头或 attestation 直接 `exit` 时，仍能在父 Shell 统一持久化 unsafe-partial `86`、blocked evidence 和 terminal invocation state。成功/内部失败两条语义都必须用 native Linux Bash 行为测试验收，macOS 旧 Bash 通过不能代替 Linux 证据。
- CI 把 deployment behavior 输出保存为 artifact 时，必须使用 `set -euo pipefail` 和 `2>&1 | tee ...`，确保左侧脚本失败立即使 step 失败，同时保留 stderr 证据。末尾的 33 场景/完成 marker reconcile 是独立对账，不得用来合理化前置 pipeline 假绿。

## 8. 发布验收命令

PR 前：

```bash
rg -n "USER root|CipherMode.CBC|CHANGE_ME|dummy-key|Strict-Transport-Security|UseHttpsRedirection|listen 443|ssl_certificate" deploy src docs
bash -n deploy/enterprise-ai/*.sh deploy/enterprise-ai/scripts/*.sh
bash deploy/enterprise-ai/tests/deployment-behavior.sh
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~SecurityHardeningTests|Suite=DeploymentBehavior" --no-restore
```

发布前/发布的对外标准命令：

```powershell
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Doctor
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Services httpapi,dataworker,ragworker,web -DryRun
pwsh ./deploy/Deploy-Changed.ps1 -Targets AICopilot
```

仓库内安全检查仍可单独运行，但不触发生产发布：

```bash
./deploy/enterprise-ai/scripts/check-release-security-attestation.sh --dry-run
./deploy/enterprise-ai/scripts/check-model-secret-migration.sh --dry-run
./deploy/enterprise-ai/scripts/check-runner-security-attestation.sh --dry-run
./deploy/enterprise-ai/scripts/check-platform-attestation-record.sh --record <filled-runner-platform-attestation.md>
```

线上发布后：

```bash
curl -I http://<intranet-host>:82
curl -I http://<intranet-host>:82/api/identity/cloud-oidc/status
./deploy/enterprise-ai/scripts/check-release-security-attestation.sh
./deploy/enterprise-ai/scripts/check-model-secret-migration.sh
```

## 9. 未完成和外部依赖

- 真实服务器 `.env`、真实 Cloud OIDC、真实 Harbor、真实容器和线上 HTTP header 必须在发布窗口验收；本地测试不能替代。
- GitHub self-hosted runner 权限收敛、GitHub environment secret 权限、OIDC/Vault 或等价短期凭据必须由平台侧验收并留痕。
- CloudPlatform 是否 HTTP-only 以及 Cloud nginx / OIDC Provider 的安全头口径属于 Cloud 项目，不由 AICopilot 单独改动。
