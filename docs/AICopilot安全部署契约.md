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
- 部署门禁测试：`src/tests/AICopilot.BackendTests/SecurityHardeningTests.cs`。

### 2.1 AICopilot 测试治理 trust root 冷启动（AI-RULE-TEST-012）

AICopilot 首次启用测试治理 trust root 时，必须遵守工作区根 `docs/三项目测试架构治理总计划.md` 第 16.2 节和第 20 节的通用 receipt 协议。本节只固定 AICopilot 项目参数，不另建第二套 receipt 状态机或 schema 权威。

- 当前冷启动审计锚点为 `M0=156fbd31713fe4b26f7e2e8b4009a6f61ccb30d2`。历史候选 `198cc59318f4a1748c719b9b8ecff1d969952ce8` 及其中 13 个提交只可作为最终语义和测试证据参考，不是可直接 cherry-pick、fast-forward 或复用 baseline provenance 的预批准链。
- `H0` 必须是 `M0` 的直接单父最小 root，且只允许新增或修改以下六个路径；任何路径或字节变化都必须重新审核整个 `H0`：
  - `.gitattributes`
  - `.github/CODEOWNERS`
  - `scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1`
  - `scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1`
  - `scripts/tests/baselines/migrations/TestAICopilotGovernanceMigrationValidator.v1.ps1`
  - `scripts/tests/baselines/migrations/aicopilot-governance-migration-receipt.schema.json`
- `H0` 不得夹带 baseline、waiver、Phase 0 policy、任何 workflow、生产/测试/部署差异、readiness 文档或实际 receipt。`CODEOWNERS` 中作者之外的第二个真实 GitHub principal/team 必须已存在、具备 GitHub 认可的必要仓库写权限，并按最终 last-match pattern 实际拥有全部 trust paths；只出现在注释、被后续 pattern 覆盖或指向无效 team 均不合格。本地 Agent 交叉审查、同一账号或作者自批均不构成独立 reviewer。
- `H0` 必须在 full clone 或已完整 fetch 的对象图上审核和实施。shallow clone 只能用于只读准备，不得用于证明完整祖先、生成最终 receipt 或实施真实冷启动链。
- `H0` 经独立 reviewer 审核并以精确 tree/parent 落到可信 base 后，首次 workflow migration 固定为 `H0 -> A0 -> C0`：`A0` 只能新增一份精确 pending receipt；`C0` 必须是 `A0` 的直接单父，只能把同一 blob 从 pending 移到 consumed，并安装 receipt 已逐路径、mode、blob 和 SHA-256 授权的唯一 preactivation workflow。授权与消费不得同提交，不得使用 synthetic merge、candidate wrapper、自批、同批 baseline/policy 或候选分支生成的证明工具。
- authoritative job 只能从当时 trusted base 提取并核对 wrapper、validator、self-test harness 和 schema 的 mode/blob/digest，再执行 base wrapper/validator；它不得加载 candidate validator。隔离的 self-test job 只用 base-owned harness/schema 验证 candidate validator，固定执行 `92` 条；该 job 不接触生产 secret，不与 authoritative/build job 共享 workspace、cache、artifact、后台进程或可写 Git 状态。任一测试失败、缺失或 Skip 均不得绿色。
- checkout 必须显式绑定原始 PR head 的 40 位 SHA，并设置 `persist-credentials: false`；required 证据必须绑定同一原始 PR head 和同一受信 workflow provenance。`H0/A0/C0` 的 v1 落点只能使用保留已审核 head SHA 的 fast-forward 或等价精确落点；merge commit、squash、rebase 和 merge queue 均不合格。平台无法在不 bypass 的情况下保留精确落点时必须停止并另立未来 landing 协议。当前尚无已授权、可执行的精确 landing 路径；本规则只定义 fail-closed 边界，不授权直推 `main`、管理员 bypass 或修改远端设置，实施前必须另获明确授权并审核 landing protocol。PR head、base、landing SHA、parent、tree、receipt、lease 或 required source 任一变化，原授权和绿灯立即失效，必须从新可信 base 重算。
- AICopilot 三个 required 逻辑角色固定为 `migration-validator-selftest`、`build-test`、`required-final`。仅有同名 status、候选仓内自定义 workflow、普通 branch protection 的 `any source` 或旧 `aicopilot-simulation-release-candidate.yml` 绿灯均不构成 base-owned 证明。
- `C0` 后必须创建不合入的 probe PR：先在 `E0=false` 时预检三个 context、原始 PR-head SHA、job 隔离和 workflow provenance；再启用候选保护配置，并在同一仍不合入的 probe 上真实验证无批准、stale approval、追加提交、protected-path 无 receipt、同名伪 check、candidate wrapper、synthetic merge、force 和 bypass 均不能放行。启用配置本身不等于 E0；全部主动阻断验证通过前始终写 `E0=false`。
- 耐久 source binding 只接受组织/企业 required workflow 对 source repository/ref/path 的精确绑定，或候选无法调用的专用 attestor GitHub App；绑定通用 GitHub Actions App 或普通同名 required status 不合格。只有上述来源绑定、独立 reviewer 和 branch protection/ruleset 均已实际启用，且同一 probe 的 active-block 证据完整时，才能写 `E0=true` 或宣称 trust root active。`AI-SEC-010` 的 production environment reviewer/runner 验收不能替代代码 receipt reviewer 和 base-owned required check。
- 如果本 readiness 文档或任何其它提交先进入 `main`，`M0` 立即失效；必须把新的 `main` 重新命名为可信锚点、重新审核 `H0` 的直接父关系，并重算全部 receipt/provenance。禁止为了保留旧 SHA 而绕过该重算。

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
- self-hosted runner 机器权限收敛、OIDC/Vault 或等价短期凭据属于外部基础设施任务；AICopilot 仓库只能提供 workflow 边界、runner 本机 attestation、平台验收模板和记录 linter，不能伪造成平台治理已完成。
- 平台验收记录必须同时覆盖 GitHub production environment secret 限制、required reviewers、`contents: read`、`self-hosted + iiot-linux-prod`、生产/secret workflow 无 GitHub hosted runner、runner 本机脚本结果，以及 OIDC/Vault 已落地或已批准基础设施例外；记录 linter 只校验这些证据字段完整，不替代真实 GitHub、runner、Vault 或 OIDC 验收。

### 7.1 可重复和并发安全发布

- 日常正式发布必须从 fresh 配置远端 tip 创建隔离 detached worktree；原工作树允许脏且后续并发修改不得改变本次发布，未推送内容不得发布。
- 每次日常运行必须使用私有 services、image 和 digest-bound request；不得用 `artifacts/deploy/aicopilot-built-services.txt` 这类跨任务固定文件控制发布。每次远端阶段只能建立一次 SSH。
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

## 8. 发布验收命令

PR 前：

```bash
rg -n "USER root|CipherMode.CBC|CHANGE_ME|dummy-key|Strict-Transport-Security|UseHttpsRedirection|listen 443|ssl_certificate" deploy src docs
bash -n deploy/enterprise-ai/*.sh deploy/enterprise-ai/scripts/*.sh
bash deploy/enterprise-ai/tests/deployment-behavior.sh
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~SecurityHardeningTests" --no-restore
```

发布前/发布的对外标准命令：

```powershell
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Doctor
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Services httpapi,dataworker,ragworker,web -DryRun
pwsh ./deploy/Deploy.ps1 -Target AICopilot -Services httpapi,dataworker,ragworker,web -Deploy
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
