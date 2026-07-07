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
- 发布前必须先执行 `.env` 权限、模板占位、弱 secret、HTTP-only URL、Cloud OIDC issuer、必填 secret 和 direct Cloud readonly 配置校验。
- `deploy-release.sh --validate-only` 是不发布的配置校验入口；该模式不得拉镜像、不得执行 Docker Compose、不得改写 release tag。
- 模型、Embedding、endpoint pool API key 必须是 `encv2:` AES-GCM 受保护格式；旧 `encv1:` 只能由 migration worker 迁移重加密，runtime provider 不得长期兼容旧格式或明文。
- 私有模型 seed 的真实 `AICOPILOT_PRIVATE_MODEL_BASE_URL`、`AICOPILOT_PRIVATE_MODEL_API_KEY` 和启用状态只能来自服务器真实 `.env` 或本机非 git 私密手册；仓库默认使用 `model.internal.example` 占位 URL、空 API key 和禁用状态。生产标准 context window 是 `65536`，API key 播种入库前必须加密为 `encv2:`。

## 7. 镜像、SSH 和 runner

- AICopilot 生产镜像必须使用 Harbor mirror 基础镜像，不能默认从 Docker Hub 或 MCR 拉生产基础镜像。
- 应用和 Web 运行容器必须非 root。
- 标准发布路径是本机构建镜像、推 Harbor、SSH 触发服务器 `deploy-release.sh`。
- `local-release.sh` 默认使用专用部署用户；root SSH 只允许 `ALLOW_ROOT_SSH_DEPLOY=true` 的记录化应急路径。
- GitHub `aicopilot-image` / `aicopilot-deploy` 只保留灾备入口，不是日常生产发布入口。
- self-hosted runner 机器权限收敛、OIDC/Vault 或等价短期凭据属于外部基础设施任务；AICopilot 仓库只能提供 workflow 边界、runner 本机 attestation、平台验收模板和记录 linter，不能伪造成平台治理已完成。
- 平台验收记录必须同时覆盖 GitHub production environment secret 限制、required reviewers、`contents: read`、`self-hosted + iiot-linux-prod`、生产/secret workflow 无 GitHub hosted runner、runner 本机脚本结果，以及 OIDC/Vault 已落地或已批准基础设施例外；记录 linter 只校验这些证据字段完整，不替代真实 GitHub、runner、Vault 或 OIDC 验收。

## 8. 发布验收命令

PR 前：

```bash
rg -n "USER root|CipherMode.CBC|CHANGE_ME|dummy-key|Strict-Transport-Security|UseHttpsRedirection|listen 443|ssl_certificate" deploy src docs
bash -n deploy/enterprise-ai/*.sh deploy/enterprise-ai/scripts/*.sh
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~SecurityHardeningTests" --no-restore
```

发布前：

```bash
ENV_FILE=<server-.env> ./deploy/enterprise-ai/deploy-release.sh --validate-only
REGISTRY=harbor.internal.example:80 CLOUD_PLATFORM_URL=http://cloud.internal.example:81 ./deploy/enterprise-ai/local-release.sh --services httpapi,migration,dataworker,ragworker,web --ssh-target deploy@aicopilot.internal.example --dry-run
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
