# A助理 Agent 工作台发布候选总览

日期：2026-05-18

## 当前阶段

AICopilot 已从“继续新增后端 AI 功能”转入“前后端整合、Simulation 验收、发布候选硬化”阶段。

## 当前边界

- 只处理 AICopilot。
- 不修改 Cloud/Edge。
- 不接真实 Cloud。
- 不开放 shell。
- 不允许任意服务器路径写入。
- 不绕过 Tool Registry、审批、权限和审计。

## 发布候选材料

- `docs/A助理前端Agent工作台改造验收报告.md`
- `docs/A助理前后端Integration分支说明.md`
- `docs/A助理Agent工作台Simulation前后端联调验收报告.md`
- `docs/A助理权限矩阵.md`
- `docs/A助理部署配置说明.md`
- `docs/A助理错误码与前端提示说明.md`
- `docs/A助理已知问题清单.md`
- `docs/A助理Simulation模式说明.md`
- `docs/A助理真实CloudReadonly准备说明.md`
- `docs/A助理Agent工作台内部试用报告.md`

## 进入下一阶段条件

- 前端 PR 收口并通过 build/unit/smoke。
- 后端 PR #46 收口并通过 Simulation acceptance。
- integration 分支完成真实前后端联调。
- 发布候选硬化报告中的待完成项全部清零。
