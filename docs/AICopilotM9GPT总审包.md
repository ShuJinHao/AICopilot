# AICopilot M9 GPT 总审包

版本：2026-05-24

## 审核目标

本文件用于 M9 时交给 GPT/5.5 Pro 做总审。审核对象不是 M1 单点，而是 M1-M6 工程能力、M7 硬停授权包、M8 内部 GA 候选前置条件和 M9 发布评审条件。

## 当前可审内容

- M1：PR #48 baseline freeze 已合并，P0-P18.2 仅为 readiness baseline。
- M2：Pilot Authorization Workflow 已新增后端/API、持久化、权限、审计、机器校验和测试。
- M3：Model/API Pool Productionization 纳入总审，当前不配置真实 endpoint/token。
- M4：RAG Governance Completion 纳入总审，当前不输出 raw payload/raw rows/full SQL。
- M5：Enterprise Data Source Platformization 纳入总审，当前只做受控只读。
- M6：Security & Compliance Hardening 纳入总审，当前补齐 Pilot Authorization 审计白名单和 M2-M9 静态门禁。
- M7：真实 Pilot 前硬停，等待单独授权。

## GPT 审核提示词

请审核 AICopilot M1-M9 连续推进包，重点检查：

1. M2 Pilot Authorization Workflow 是否只授予 planning，不授予 execution。
2. 状态机中是否不存在任何直接执行授权状态或等价执行授权语义。
3. 机器校验是否拒绝 token、API Key、connection string、raw payload、raw rows、full SQL、Recipe/version、Cloud write、free SQL。
4. 审计、DTO、报告、日志是否只保留安全摘要，不保存敏感正文。
5. M3-M6 是否仍然只在 AICopilot 内完成，不修改 Cloud/Edge，不配置真实 endpoint/token。
6. M7 是否在真实 Pilot 前硬停，是否需要额外授权材料。
7. 是否存在把 readiness/planning 偷换成 real execution 或 GA 的表述。

## 本轮不可审为通过的内容

- 不把 M7 真实 Pilot 视为已执行。
- 不把 M8 内部 GA 候选视为已达成。
- 不把 M9 审核包视为 GA 发布批准。
- 不把任何 readiness/planning 结论解释为 `ExecutionPermission granted`。
