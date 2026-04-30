# Phase 4.3 人工在场与 AI 质量门禁

## 1. 目标

- 继续只改 `AICopilot`
- AI 在制造业中的定位固定为：观测、诊断、建议、知识问答
- 不执行任何控制动作，不把审批当作控制动作放行机制

## 2. 人工在场闭环

### 2.1 会话级在岗声明

- 接口：`PUT /api/aigateway/session/safety-attestation`
- 请求体：`{ sessionId, isOnsiteConfirmed, expiresInMinutes }`
- 最大有效期：`30` 分钟
- 会话上持久化以下字段：
  - `onsiteConfirmedAt`
  - `onsiteConfirmedBy`
  - `onsiteConfirmationExpiresAt`

### 2.2 结构化审批

- 接口：`POST /api/aigateway/approval/decision`
- 请求体：`{ sessionId, callId, decision, onsiteConfirmed }`
- `decision` 只允许：
  - `approved`
  - `rejected`

### 2.3 双层校验规则

对要求在岗的高风险建议能力，批准前必须同时满足：

- 会话存在有效在岗声明
- 审批时再次显式传 `onsiteConfirmed = true`

否则返回结构化问题码：

- `onsite_presence_required`
- `onsite_presence_expired`
- `approval_reconfirmation_required`

## 3. 运行时边界

### 3.1 直接拒绝的请求

以下请求在预分类阶段直接拒绝，不进入审批、MCP 或 SQL：

- 重启服务
- 写 PLC
- 下发配方
- 写参数
- 状态切换

统一返回：`control_action_blocked`

### 3.2 Text-to-SQL 治理

- 只允许真实只读库
- 固定命令超时
- 固定单次最大返回行数
- 只接受单结果集
- 超上限时自动截断并显式提示“结果已截断”

### 3.3 MCP 暴露边界

- 默认关闭
- 只允许 `AllowedToolNames` 内工具进入聊天暴露集合
- 控制型 MCP 不进入生产聊天主链
- 每个 allowlist 工具都要能汇总出：
  - 是否需要审批
  - 是否要求在岗声明

## 4. 回归入口

### 4.1 无 Docker 快速门禁

```powershell
cd "C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot"
dotnet test ".\src\Tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj" --filter "Suite=Phase43SafetyQuality&Runtime!=DockerRequired"
```

### 4.2 Docker 完整门禁

```powershell
cd "C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot"
dotnet test ".\src\Tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj" --filter "Suite=Phase43SafetyQuality"
```

### 4.3 前端构建

```powershell
cd "C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\Vues\AICopilot.Web"
npm run build
```

## 5. 当前门禁覆盖

- 人工在场声明设置、过期、重新声明
- 审批二次确认
- 控制请求预分类拒绝
- 制造业四类场景分类
- 五域结构化问答与五个 `Policy.*` 主题不退化
- MCP allowlist、审批要求、在岗要求摘要
- Text-to-SQL 只读限制、结果截断、异常口径

## 6. 说明

- `Runtime=DockerRequired` 只打在容器依赖的集成测试类上
- 无 Docker 机器先跑快速门禁
- Docker 环境恢复后，再跑完整门禁和 Phase 3.8 演示验收
