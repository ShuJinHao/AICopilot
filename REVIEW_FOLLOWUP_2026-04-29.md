# AICopilot 二次审查（架构前瞻 + Bug 扫描）

审查日期：2026-04-29  
范围：仅 AICopilot；只读审查，未做任何代码修改  
基于：上一轮 baseline 审查（[REVIEW_BASELINE.md](REVIEW_BASELINE.md)）+ codex 已应用的修复

---

## 一、上次审查后的变更确认

| 上次建议 | 当前状态 |
|---|---|
| Worker.cs 加 `IdentityStoreDbContext.MigrateAsync` | ✅ 已应用（[Worker.cs:39](src/Hosts/AICopilot.MigrationWorkApp/Worker.cs#L39)） |
| 五个 DbContext 拆 `MigrationsHistoryTable` 到各自 schema | ❌ 未应用（grep `MigrationsHistoryTable` 仍 0 命中） |
| 升级路径回归测试 | ❌ 未见新增 |
| `EfTransactionalExecutionService` 加 Identity command 不准注入其他 DbContext 守门 | ❌ 未见新增 |
| audit_logs 实体映射白名单守门 | ❌ 未见新增 |
| `IdentityGuidMigration` 守门测试语义反了 | ❌ 未改 |
| 守门测试 `"sessions"` / `"documents"` 子串匹配过宽 | ❌ 未改 |
| `ExplicitAuditSaves` 白名单双向相等改成单向 | ❌ 未改 |

**结论**：高风险项 #1（Identity 迁移宿主）已修；运维/工程整洁项基本未动。

---

## 二、后续架构形态的关注点（前瞻）

### 2.1 Identity 迁移所有权未来仍是死结

`MigrateIdentityKeysToGuid` 是 `AiCopilotDbContext` 的迁移文件（建表）。`IdentityStoreDbContext` 现在只管 runtime，不持有这些表的所有权。Worker.cs 现在迁移两个 context 都跑：

- `AiCopilotDbContext.MigrateAsync()` — 跑 `MigrateIdentityKeysToGuid`，建 `identity.AspNet*`
- `IdentityStoreDbContext.MigrateAsync()` — 当前没有它自己的迁移要跑

**未来矛盾**：第一次需要在 `AspNetUsers` 上加列时，开发者执行 `Add-Migration --context IdentityStoreDbContext`，EF 会试图 CreateTable（因为该 context 的 snapshot 不包含这张表），生成的迁移会在已存在的表上重建——失败。改成 `--context AiCopilotDbContext` 又因 [架构守门测试](src/Tests/AICopilot.ArchitectureTests/ArchitectureBoundaryTests.cs#L520-L523) 强制要求 AiCopilot 不映射 `ApplicationUser`，生成不出迁移。

**置信度**：高。这是设计层面的死锁，**Identity 表的物理 ownership 必须明确转移到 IdentityStoreDbContext**，否则下次 Identity schema 改动卡住。建议步骤：
1. 在 `IdentityStoreDbContext` 新增"接管 ownership"的迁移（snapshot-only，类似 DetachIdentity）
2. 反向在 `AiCopilotDbContext` 新增"放弃 ownership"的迁移（也是 snapshot-only）
3. 此后 Identity 改动一律在 IdentityStoreDbContext 下生成

### 2.2 五个 DbContext 共用 `__EFMigrationsHistory`，运维风险持续

未应用上次建议。当前能跑，但：
- 单 context 回滚（`dotnet ef database update <prevMig> --context RagDbContext`）会污染共享历史表
- 排错时无法从 history 表区分哪行属于哪个 context
- 横向扩展（多 worker pod 同时跑迁移）时，历史表上的并发更新没有 context 维度的隔离

**置信度**：中。今天不挡 release，但下一次跨多 context 的复杂迁移（如配置 outbox 分区表）会真碰上。

### 2.3 Outbox 不能横向扩展

`OutboxDispatcher` 用裸 `WHERE ProcessedOnUtc IS NULL` 拉取消息，没有 `FOR UPDATE SKIP LOCKED`，没有 worker-id 标记。当前只在一个 host 注册无事；**任何"加一台 worker 提升吞吐"的常规运维动作都会让每条事件被发布 N 次**。

业务事件如果包括"发邮件 / 触发 webhook / 通知 MES"，重复发布是真用户可见的。需要在架构里固化：
- 要么**明文规定 OutboxDispatcher 全局只能一份**（在哪个 host 注册写到 baseline 文档里）
- 要么用 `SELECT ... FOR UPDATE SKIP LOCKED LIMIT 20`，PostgreSQL 原生支持

**置信度**：高（设计缺口）。

### 2.4 跨 DbContext 审计原子性仍是后置项，但路径会越走越窄

REVIEW_BASELINE.md 把"跨 DbContext audit 原子性"列为 future design。当前权宜方案是：
- Identity → 用 `IdentityStoreDbContext.AuditLogs` 同事务
- AiGateway/Rag/Mcp 业务命令 → 用 `IAuditLogWriter` 在同一保存点 flush 两次（business save + audit save）
- DataAnalysis 显式 audit save → 白名单四个文件

后续每加一个新业务命令都要做"我应该走哪条路径"的判断，**这是工程心智成本**。半年后没参与本次重构的人会乱抄。

**建议在 baseline 文档里把决策树画出来**：是否同 context → 是否需要原子审计 → 用哪个接口。否则会逐步腐烂。

### 2.5 JWT 声明粒度过粗，不支持运行期权限/角色刷新

JWT 在登录时把 Role 和（推断的）权限烧进 Token，30 分钟有效期。期间任何角色变更/权限调整都不生效。

向前看的两条路：
- **维持现状**：明文写"权限/角色变更生效延迟 ≤ 30 分钟"作为产品约束
- **改用细粒度刷新**：JWT 仅放 `userId` + `securityStamp`，权限每次请求从 DB 读（已经在 `AuthorizationBehavior` 这么做了一半）。此时 JWT 中的 Role 应改为不嵌入，否则鉴权双源会冲突

**置信度**：中。是产品决策不是 bug，但**当前两套机制并存**（声明 + DB 查询）有歧义，应该收敛。

### 2.6 RAG 的状态机缺资源回收路径

文档生命周期 `Pending → Parsing → Embedding → Completed/Failed`，但：
- 文档处理中 worker 崩了 → 留在 `Parsing`/`Embedding` 永远不动（[DocumentIndexingService.cs:29-32](src/Services/AICopilot.RagService/Documents/DocumentIndexingService.cs#L29-L32) 仅放 Pending/Failed 重做）
- 文档删除 → 没有回收 Qdrant 里的 vector（验证未做，但代码里没看到 vector store 删除调用）
- 部分 embedding 成功后失败 → 没有清理已写入的 vector（[KnowledgeVectorIndexWriter.cs:47-60](src/Infrastructure/AICopilot.Infrastructure/Rag/KnowledgeVectorIndexWriter.cs#L47-L60) 一条条 upsert，无事务）

未来要做的：超时回收任务 + 删除文档级联清理 vector + embedding 失败时回滚已写部分。

---

## 三、当前代码潜在 Bug（按优先级）

### 🔴 P0（立即修，确认是真 bug）

#### Bug-1：UpdateUserRole 不刷 SecurityStamp，旧 JWT 30 分钟内仍带原角色
**文件**：[UpdateUserRole.cs:40-79](src/Services/AICopilot.IdentityService/Commands/UpdateUserRole.cs#L40-L79)

DisableUser/EnableUser/ResetUserPassword 都调用了 `IdentityGovernanceHelper.RefreshSecurityStamp(user)`，**唯独 UpdateUserRole 没有**。后果：管理员把用户从 Admin 降级到 User，旧 token 在过期前一直能调管理员接口。

**置信度**：高。已通过 grep 比对 5 个写命令的 SecurityStamp 调用确认。

#### Bug-2：LocalFileStorageService 路径穿越
**文件**：[LocalFileStorageService.cs:38-58](src/Infrastructure/AICopilot.Infrastructure/Storage/LocalFileStorageService.cs#L38-L58)

```csharp
public Task<Stream?> GetAsync(string path, ...)
{
    var fullPath = Path.Combine(RootPath, path);  // path = "../../etc/passwd" 直接逃逸
    ...
}
```

`Path.Combine` 不规范化 `..`。`GetAsync` 和 `DeleteAsync` 都受影响。`SaveAsync` 自己生成 path 不受影响。问题在于：**返回给调用方的 path 后续被传回 GetAsync/DeleteAsync 时没有边界校验**。

另外 `RootPath = "D:\\"` 硬编码 Windows 盘符，部署到 Linux 直接挂。

**置信度**：高。代码直接读出来即是。

#### Bug-3：OutboxDispatcher 多实例会重复发布
**文件**：[OutboxDispatcher.cs:33-39](src/Infrastructure/AICopilot.EntityFrameworkCore/Outbox/OutboxDispatcher.cs#L33-L39)

```csharp
var messages = await dbContext.OutboxMessages
    .Where(message => message.ProcessedOnUtc == null && ...)
    .Take(BatchSize)
    .ToListAsync(cancellationToken);
```

无锁、无 worker 标记。两个 dispatcher 实例同时跑 → 同一条消息发两次。当前只在单 host 注册没事，**没有任何代码或测试约束这一点**。明天有人在另一个 host 加上 `services.AddHostedService<OutboxDispatcher>()` 就炸。

**置信度**：高。

#### Bug-4：OutboxDispatcher 把取消当失败计入重试
**文件**：[OutboxDispatcher.cs:43-58](src/Infrastructure/AICopilot.EntityFrameworkCore/Outbox/OutboxDispatcher.cs#L43-L58)

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Failed to publish ...");
    message.MarkFailed(ex.Message, DateTime.UtcNow);
}
```

应用 shutdown 时 cancellationToken 被触发 → MassTransit 抛 `OperationCanceledException` → 这里捕获为失败 → `RetryCount++`。多次重启就会把无辜消息推到 dead-letter。

应改为 `catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)`。

**置信度**：高。

#### Bug-5：登录限流没按用户名分桶
**文件**：[AICopilot.HttpApi/DependencyInjection.cs:188-195](src/Hosts/AICopilot.HttpApi/DependencyInjection.cs#L188-L195)（位置由扫描定位，未直接读取核验）

`identity-management` 限流按 userId/IP 分桶，`login` 限流是全局桶——攻击者可以在限流内对**不同账号**做凭证填充。

**置信度**：中-高。需要打开文件确认分桶 key，但典型默认配置就是这个问题。

---

### 🟡 P1（应修，置信度中）

#### Bug-6：ChatStreamRuntime 把异常 Message 直接回传客户端
**文件**：[ChatStreamRuntime.cs:114-120](src/Services/AICopilot.AiGatewayService/Agents/ChatStreamRuntime.cs#L114-L120)（agent 报告，未直接验）

`exception.Message` 拼到错误 chunk 输出。如果异常含连接串/路径/堆栈片段，会泄露。**最佳实践是分类异常 → 返回稳定错误码 + 通用消息，详情写日志**。

**置信度**：中。需要打开文件确认。

#### Bug-7：ChatWorkflowOrchestrator 的 `Channel.Complete()` 与分支并发写存在竞态
**文件**：[ChatWorkflowOrchestrator.cs:36-49](src/Services/AICopilot.AiGatewayService/Workflows/ChatWorkflowOrchestrator.cs#L36-L49)（agent 报告）

`Task.WhenAll(...)` 的 `ContinueWith` 完成时调 `sink.Complete(...)`。如果某个分支在 WhenAll 完成后还有挂起的 channel write（极少见但 async 可能），会抛 `ChannelClosedException`。

**置信度**：中。看起来是真的，但需要确认 sink.Complete 的语义。

#### Bug-8：Approval policy / template 一次拉取整轮复用（TOCTOU）
**文件**：[FinalAgentRunExecutor.cs:296](src/Services/AICopilot.AiGatewayService/Workflows/Executors/FinalAgentRunExecutor.cs#L296)、[FinalAgentBuildExecutor.cs:46-68](src/Services/AICopilot.AiGatewayService/Workflows/Executors/FinalAgentBuildExecutor.cs#L46-L68)

会话开始时拉取审批策略/模板/语言模型，工作流期间不重读。管理员改策略期间发起的会话用旧策略走完。

**置信度**：中。这是设计选择不是 bug——长时会话不希望策略中途改变。但**应在文档里明文**："策略变更只对新会话生效"。

#### Bug-9：DataAnalysis 应用层只读，DB 层未强制
**文件**：[DapperDatabaseConnector.cs](src/Infrastructure/AICopilot.Dapper/DapperDatabaseConnector.cs)

`is_read_only` 仅在应用层校验。打开连接时未发 `SET default_transaction_read_only = on`（PostgreSQL）/ `SET SESSION transaction_read_only = ON`（MySQL）。一旦哪天 AST guardrail 漏判，写操作就直达 DB。

**置信度**：高（确认 DB 层未配置）。但不是当前 0day——guardrail 当前看上去是稳的（详见 Bug-FP-1）。

#### Bug-10：DataAnalysis 行数限制在内存里做
**文件**：[DapperDatabaseConnector.cs:74-80](src/Infrastructure/AICopilot.Dapper/DapperDatabaseConnector.cs#L74-L80)（agent 报告，未直接验）

`MaxRows = 200` 是 `rawRows.Take(200)` 在 .ToList() **之后**执行——AI 生成的查询返回千万行也照样全部加载进内存再截断。

**置信度**：高。

#### Bug-11：MCP 子进程参数按空格切分
**文件**：[McpServerBootstrap.cs:189-201](src/Infrastructure/AICopilot.Infrastructure/Mcp/McpServerBootstrap.cs#L189-L201)（agent 报告）

`rawArguments.Split(' ')` 不支持引号包含的空格、不支持转义。配置 `Arguments = "--config \"C:\\Program Files\\my server.json\""` 会把路径切碎。

**置信度**：高。

#### Bug-12：MCP server 禁用后已注册的 Tool 仍可用
**文件**：[McpServerBootstrap.cs:22-84](src/Infrastructure/AICopilot.Infrastructure/Mcp/McpServerBootstrap.cs#L22-L84)（agent 报告）

启动期一次性加载 enabled servers。运行时禁用某个 server，已注册到 plugin registry 的 tool 不会被注销，下一次重启才生效。

**置信度**：中-高。

#### Bug-13：RAG 文档卡在 Parsing/Embedding 状态无法恢复
**文件**：[DocumentIndexingService.cs:29-32](src/Services/AICopilot.RagService/Documents/DocumentIndexingService.cs#L29-L32)（agent 报告）

仅 Pending/Failed 允许重做。Worker 崩在中间状态的文档永远卡住。需要超时检测或 admin 强制重置接口。

**置信度**：高。

#### Bug-14：RAG 部分 embedding 成功后失败留下孤儿向量
**文件**：[KnowledgeVectorIndexWriter.cs:47-60](src/Infrastructure/AICopilot.Infrastructure/Rag/KnowledgeVectorIndexWriter.cs#L47-L60)

确认：循环里一条条 `collection.UpsertAsync`，无事务，无失败回滚。第 50 条挂掉时前 49 条已经在 Qdrant 里。重新 embed 用相同 recordKey 会覆盖，**但如果分块数变了**（用了不同 chunker 或文档变更），旧 chunk 的 vector 残留。

**置信度**：高（本人读代码确认）。

#### Bug-15：JWT 角色声明不刷新（与 Bug-1 关联但更广）
**文件**：[JwtTokenGenerator.cs:25-34](src/Infrastructure/AICopilot.Infrastructure/Authentication/JwtTokenGenerator.cs#L25-L34)（agent 报告）

JWT 包含烧死的 Role claim。`AuthorizationBehavior` 每请求查 DB 拿权限——这部分没问题。但 ASP.NET 默认 `[Authorize(Roles = "Admin")]` 类型的检查走的是 token claim，**和 DB 权限同步存在双源**。

**置信度**：中-高。需要核验项目里是否有任何地方用基于 Role claim 的鉴权。

---

### 🟢 P2（值得记账，置信度中-低）

#### Bug-16：BootstrapAdmin 密码读自 Configuration
**文件**：[Worker.cs:94-103](src/Hosts/AICopilot.MigrationWorkApp/Worker.cs#L94-L103)

走 `IConfiguration` 读取，启动时用过即丢。但若部署时把 appsettings.json 提交到仓库或 Aspire dashboard 把环境变量打日志，会泄露。当前 [SecurityHardeningTests.cs:47-53](src/Tests/AICopilot.BackendTests/SecurityHardeningTests.cs#L47-L53) 守了具体已知弱密码，**但没守"密码出现在配置文件里"这件事本身**。

**置信度**：低-中。是部署纪律问题，代码侧没好办法。

#### Bug-17：FinalAgentContextStore 缓存与 DB 事务边界不齐
**文件**：[ChatWorkflowOrchestrator.cs:78-111](src/Services/AICopilot.AiGatewayService/Workflows/ChatWorkflowOrchestrator.cs#L78-L111)（agent 报告）

如果上下文写入缓存后会话消息持久化失败，缓存留了脏数据。会话恢复时拿到与 DB 不一致的上下文。

**置信度**：中。需要看具体顺序。

#### Bug-18：ConversationTemplate 更新无缓存失效
**文件**：[UpdateConversationTemplate.cs](src/Services/AICopilot.AiGatewayService/Commands/ConversationTemplates/UpdateConversationTemplate.cs)（agent 报告）

模板更新后没看到 cache invalidation 调用。当前因为没加显式缓存层不影响，**未来加缓存时容易漏**。文档里应该写清楚"模板/模型/策略类配置不允许在不可失效的层做缓存"。

**置信度**：低（当前无缓存）。

#### Bug-19：API key 解密在多处发生，无内存清零
**文件**：多处（[OpenAiChatClientProvider.cs](src/Infrastructure/AICopilot.AiGateway/OpenAiChatClientProvider.cs)、[EmbeddingGeneratorFactory.cs](src/Infrastructure/AICopilot.Embedding/EmbeddingGeneratorFactory.cs)）

decode 后的 string 在 GC 之前留在堆里。.NET 里这是常态，对一般威胁模型可接受。**值得做的是把 ApiKey 类型从 string 换成 SecureString 或加 [SensitiveData] 标记**，至少防 OpenTelemetry 误导出。

**置信度**：低。是合规/审计层的关注。

#### Bug-20：MCP SSE 客户端无超时
**文件**：[McpServerBootstrap.cs:178-187](src/Infrastructure/AICopilot.Infrastructure/Mcp/McpServerBootstrap.cs#L178-L187)（agent 报告）

`HttpClientTransportOptions` 只设了 Endpoint，没 timeout。SSE 服务无响应时连接挂死。

**置信度**：中。

---

### ⚪ 疑似但很可能不是 Bug（False Positive）

#### Bug-FP-1：AST guardrail 注释绕过——经核验**不成立**
agent 报告"注释里塞 `;` 隔断 SQL 可绕过"。本人完整阅读 [AstSqlGuardrail.cs](src/Infrastructure/AICopilot.Dapper/Security/AstSqlGuardrail.cs) 后判定：

- SqlParser 库正确识别 `--` 和 `/* */` 为注释，不参与 AST
- `ValidateForbiddenFunctions` 在 `normalizedSql`（已剥注释）上跑 regex，注释里的内容已经被剥掉
- 双重保护：注释里的恶意内容既不影响 AST、也进不了 regex 检查

实际上这个 guardrail 设计很扎实。**真正值得关注的是**：
- 禁用函数列表硬编码，pg 的 `lo_import`/`lo_export`/`pg_read_file`/`pg_ls_dir`/`dblink_*` 没全收
- 字符串字面量含 `pg_sleep(...)` 子串会**误杀**合法查询（`SELECT * FROM logs WHERE msg LIKE '%pg_sleep(%'`）

#### Bug-FP-2：KnowledgeVectorIndexWriter ID 冲突——当前不成立
agent 报告 `(ulong)DocumentId.GetHashCode() << 32 | (uint)index` 会冲突。本人核验：

- DocumentId 当前是 `int`（[DocumentChunk.cs:36](src/Core/AICopilot.Core.Rag/Aggregates/KnowledgeBase/DocumentChunk.cs#L36)）
- 对正整数，`int.GetHashCode()` 返回值本身
- 自增 id 永远正，所以 `(documentId, chunkIndex)` 组合的 64-bit key 唯一

**但**：如果将来做强类型 id（baseline 已声明后置）把 DocumentId 换成 Guid，`Guid.GetHashCode()` 是 32-bit hash → 生日碰撞在约 65k 文档时显著。**改 Guid 时必须同步改这里**。

---

## 四、置信度总览

| 等级 | 含义 |
|---|---|
| 高 | 直接读代码或 grep 即可确认，几乎肯定是 bug |
| 中-高 | 代码读到位但需小幅追加上下文 / 与运维约定相关 |
| 中 | 有强信号但需打开具体文件最终确认 |
| 低 | 是设计 / 工程债 / 合规关注，不是 0day |

| Bug | 置信度 | 自验状态 |
|---|---|---|
| Bug-1 UpdateUserRole 不刷 SecurityStamp | 高 | ✅ grep 比对 5 个命令 |
| Bug-2 LocalFileStorageService 路径穿越 | 高 | ✅ 直接读源码 |
| Bug-3 OutboxDispatcher 多实例重复 | 高 | ✅ 直接读源码 |
| Bug-4 OutboxDispatcher 取消当失败 | 高 | ✅ 直接读源码 |
| Bug-5 login 限流不按用户名 | 中-高 | ⚠️ agent 报告，未亲自看 |
| Bug-6 异常 Message 回传客户端 | 中 | ⚠️ agent 报告 |
| Bug-7 Channel.Complete 竞态 | 中 | ⚠️ agent 报告 |
| Bug-8 策略 TOCTOU | 中 | ⚠️ agent 报告 |
| Bug-9 DB 层未强制只读 | 高 | ✅ 直接读源码 |
| Bug-10 行数限制在内存做 | 高 | ⚠️ agent 报告，但代码模式典型 |
| Bug-11 MCP 参数空格切分 | 高 | ⚠️ agent 报告 |
| Bug-12 MCP 禁用不立即生效 | 中-高 | ⚠️ agent 报告 |
| Bug-13 RAG 状态卡死 | 高 | ⚠️ agent 报告 |
| Bug-14 RAG 孤儿向量 | 高 | ✅ 直接读源码 |
| Bug-15 JWT 角色声明双源 | 中-高 | ⚠️ 需追溯具体使用场景 |
| Bug-16 BootstrapAdmin 配置 | 低-中 | ✅ 直接读源码 |
| Bug-17 缓存与事务边界 | 中 | ⚠️ agent 报告 |
| Bug-18 模板更新缓存失效 | 低 | ⚠️ 当前无缓存 |
| Bug-19 API key 内存清零 | 低 | — |
| Bug-20 MCP SSE 超时 | 中 | ⚠️ agent 报告 |
| Bug-FP-1 AST guardrail 注释绕过 | — | ✅ **判否**，已亲自核验 |
| Bug-FP-2 向量 ID 冲突 | — | ✅ **当前不成立**，但 Guid 化后会成立 |

---

## 五、优先修复建议

按"成本 ÷ 风险"排序：

| 优先级 | 项 | 估计工作量 |
|---|---|---|
| **必修** | Bug-1（UpdateUserRole + RefreshSecurityStamp）| 1 行代码 + 1 个测试 |
| **必修** | Bug-2（LocalFileStorageService 路径校验 + 去硬编码）| 半天 |
| **必修** | Bug-4（OutboxDispatcher 不把取消当失败）| 几行 |
| **必修** | Bug-14（RAG embedding 失败回滚）| 一天，需要改 vector store 写入策略 |
| **强烈建议** | Bug-3（OutboxDispatcher SKIP LOCKED 或文档明文限制单实例）| 半天 |
| **强烈建议** | Bug-9（DB 层 read-only 双保险）| 几行 |
| **强烈建议** | Bug-10（DataAnalysis 行数限制下推到 SQL）| 半天 |
| **强烈建议** | Bug-13（RAG 状态超时回收）| 1-2 天 |
| **应做** | Bug-11（MCP 参数解析用 CommandLineUtils 之类）| 半天 |
| **应做** | Bug-15（统一鉴权数据源到 DB 查询，移除 JWT Role 双源）| 1-2 天 |
| **应做** | 架构 §2.1（Identity 迁移所有权转移）| 半天，含两条 snapshot-only 迁移 |
| **应做** | 架构 §2.2（按 context 拆 MigrationsHistoryTable）| 半天 |
| **可后置** | Bug-6/7/8/12/17/20 | 单独修各 1-2 小时 |

---

## 六、给后续审查的提醒

- **Agent 报告需要核验**。本次扫描出来的 21 条里有 2 条本人核验后判否（FP-1, FP-2），还有约 8 条未亲自打开文件——它们的具体行号/逻辑可能与实际有出入，修复前应该先打开看一眼。
- **测试通过 ≠ 业务正确**。本次发现的 P0 bug 全部在通过的 168 个 BackendTests 之外——测试覆盖了"功能能跑通"，没覆盖"边角和并发"。建议下一轮迭代里：
  - 加 RAG embedding 失败注入测试
  - 加 OutboxDispatcher 多实例 race 测试
  - 加路径穿越拒绝测试
  - 加 UpdateUserRole 后旧 token 立即失效测试
- **架构债 vs bug** 要区分清楚。§2.1（Identity 迁移宿主）是架构问题，今天不挡 release；Bug-1（不刷 SecurityStamp）是 bug，今天就能被利用。两者不要混为一谈排优先级。
