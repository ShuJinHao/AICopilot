# A助理企业级数据源与知识治理 P1 范围冻结

## 范围

P1 只修改 `AICopilot`。本阶段不修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`，不接真实 Cloud 生产数据，不启用 Real CloudReadonly。

P1 在 P0 的模拟业务库、只读 SQL、RAG 治理和模型池基础上补齐试用闭环：

- 自然语言 Text-to-SQL 草稿和执行链路。
- Agent 通过受控工具调用业务库只读查询链路。
- Prompt Policy 版本化管理。
- 会话级上下文预算报告。
- RAG 分类、文档版本、软删除和补充说明管理入口。
- 模型/API endpoint pool 调度、熔断、fallback 和统计。
- fake/mock endpoint 压测与 P1 验收台账。

## 明确不做

- 不接真实 Cloud 生产数据。
- 不启用 Real CloudReadonly。
- 不开放 shell 工具。
- 不写任意服务器路径。
- 不允许模型执行 DDL/DML 或多语句 SQL。
- 不输出明文 API Key、连接串、token、密码。
- 不把 `aicopilot_sim_business` 伪装成真实业务系统。
- 不绕过数据源权限、RAG 权限或审批。
- 不做长期个人记忆。
- 不做 MCP 工具库深度治理。
- 不做旧接口兼容层、双写、迁移桥或 Cloud/Edge 适配。

## 测试口径

P1 不依赖用户提供真实 API Key。核心验收默认使用：

- `SimulationBusiness` 数据源。
- 确定性 Text-to-SQL 生成器。
- fake/mock 模型 endpoint。
- 后端聚焦测试和前端 build。

真实 API 后续只作为小流量 smoke，不作为 P1 安全逻辑的前置条件。

## 验收入口

运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseDataGovernanceP1Acceptance.ps1
```

脚本输出：

```text
docs/enterprise-data-governance-p1-latest.md
```

报告必须记录 scope guard、后端构建、P0/P1 聚焦测试、前端 build、模型池 mock 统计和剩余风险。
