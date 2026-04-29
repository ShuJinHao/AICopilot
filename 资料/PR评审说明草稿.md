# AICopilot PR 评审说明草稿

## Title

AICopilot: harden streaming, approval, SQL safety, and delivery acceptance

## Summary

本 PR 收口 AICopilot 的生产前稳定性、安全性和可交付性。主要修复 SSE 异常兜底、审批幂等、消息持久化一致性、现场在岗声明、Redis 审批上下文、SQL AST Guardrail、Agent 生命周期和前端错误处理，并补齐 Docker/Aspire 验收脚本与交付文档。

## Why This PR Is Large

- 修复目标跨越 AI Gateway、审批、安全、SQL 分析、前端聊天体验、配置治理和测试验收。
- 多数变更来自同一条用户链路：聊天请求、模型/模板配置、工具审批、恢复执行、审计、会话历史、前端提示。
- 本 PR 不继续扩展新能力，重点是把已有能力变成可部署、可审计、可回滚。

## Key Changes

- Streaming reliability：聊天流和审批流增加统一异常兜底，配置缺失稳定返回 `chat_configuration_missing`。
- Approval safety：审批入口串行化并增加重复处理保护，现场在岗声明使用 UTC 安全时间字段。
- Persistence consistency：消息历史批量持久化，异常路径落助手侧错误摘要，减少孤儿 user 消息。
- Redis context store：生产和 AppHost 默认使用 Redis 保存 final-agent context，本地开发可继续使用 Memory。
- SQL safety：用 AST 只读校验替换关键字黑名单，并限制工具结果体积。
- Frontend behavior：Error chunk 显示明确提示，流结束复位 loading，审批卡片支持现场确认和重复审批反馈。
- Delivery closure：补齐 `Run-AcceptanceClosure.ps1`、Docker/Aspire runtime smoke、migration/Redis 验证和交付文档。

## Verification

- `powershell -ExecutionPolicy Bypass -File .\scripts\Run-AcceptanceClosure.ps1 -ReportPath .\资料\acceptance-closure-latest.md`
- 最新报告路径：`资料/acceptance-closure-latest.md`
- 已覆盖：HttpApi/AppHost/BackendTests/Frontend build、diff whitespace、focused unit tests、Docker、migration/Redis verification、missing template smoke、concurrent approval smoke、onsite attestation smoke。

## Rollback Notes

- 应用异常：回滚 HttpApi、MigrationWorkApp、RagWorker 和 Web 资产到上一稳定镜像。
- 数据库异常：先备份，再评估 EF migration down；不要直接 drop database、删除 PostgreSQL volume 或清表。
- Redis 异常：Redis 仅保存未完成审批上下文；清理 Redis 会让未完成审批失效，但不影响已落库会话历史。
- 前端异常：仅 UI 问题优先单独回滚 Web 资产。

## Non-goals

- 不接入 Anthropic、Azure OpenAI、OpenRouter 或本地 vLLM。
- 不做内部插件 MCP 化。
- 不引入 WebSocket 双向流。
- 不做 Workflow 配置化或事件总线重写。
- 不执行正式生产发布。

## Reviewer Checklist

- 错误码契约：`chat_configuration_missing`、`approval_already_processed`、现场在岗相关错误码是否稳定。
- 审批安全：同一 callId 并发提交是否只执行一次工具和一次审计。
- 配置安全：生产默认 Redis，开发 Memory，敏感值不进入生产模板。
- SQL 安全：AST Guardrail 是否覆盖 DML/DDL/绕过注释，且不误杀只读查询。
- 前端体验：Error chunk、loading 复位、审批卡片状态是否符合用户预期。

## 2026-04-29 PR 拆分更新

不要再按单个大 PR 提交。当前 dirty tree 应至少拆成两个 PR：

### PR1 Title

AICopilot: harden EF migration ownership and persistence boundaries

### PR1 Summary

This PR closes the blocking persistence review findings before merging the larger AI runtime work. It gives IdentityStoreDbContext a future migration owner, runs the IdentityStore baseline migration before seeding, guards the destructive Identity GUID migration from silently deleting existing `identity.AspNet*` rows, aligns MCP initial migration with guarded `ALTER TABLE ... SET SCHEMA`, and adds architecture/backend migration safety coverage.

### PR1 Required Notes

- `MigrateIdentityKeysToGuid` remains an active-development destructive migration for old `public.AspNet*` Identity data.
- Existing real rows under `identity.AspNet*` are now guarded and will fail migration with an explicit error instead of being silently dropped.
- `__EFMigrationsHistory` is intentionally not split in this PR; per-context history tables are a separate migration governance decision.
- BackendTests were attempted locally but Docker daemon was unavailable; Docker-dependent migration tests must be rerun before merge.

### PR1 Verification

- `dotnet build .\AICopilot.slnx --no-restore`
- `dotnet test .\src\Tests\AICopilot.ArchitectureTests\AICopilot.ArchitectureTests.csproj --no-build`
- `dotnet test .\src\Tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj --no-build` attempted, blocked by Docker daemon availability.

### PR2 Title

AICopilot: close AI runtime, frontend, safety, and cleanup work

### PR2 Summary

This PR should contain the remaining AI runtime, frontend, SQL guardrail, approval flow, old implementation removal, docs, and delivery closure work after PR1 isolates persistence and migration risk.
