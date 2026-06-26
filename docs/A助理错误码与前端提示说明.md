# A助理错误码与前端提示说明

日期：2026-05-18

> 历史记录：本文曾是前端错误码提示草稿。当前唯一契约入口是 `frontend-integration-contract-package-2026-05-17.md`，后端测试会检查该契约是否覆盖 `AuthProblemCodes`、`AppProblemCodes` 和 `CloudAiReadProblemCodes`。

前端实现位置：`src/vues/AICopilot.Web/src/stores/chatErrorStore.ts`。

新增或删除后端错误码时，必须同步更新 `frontend-integration-contract-package-2026-05-17.md`，不要在本文维护第二份错误码清单。
