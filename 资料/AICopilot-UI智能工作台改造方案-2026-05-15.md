# AICopilot UI 重构计划：云端同源的 AI Command Workbench

日期：2026-05-15

状态：Claude 审核方案，未开始代码实现

## 1. 结论

本轮不是 Element Plus 小修小补，而是 AICopilot 前端 UI 重构。

重构目标：

- 让 AI 端与云端 `iiot-web` 的 Niond 视觉体系同源。
- 保留 AI 端自己的前沿感：icon-only floating dock、深色 AI Canvas、Command Composer、Agent 状态面板。
- 使用与云端成功方案一致的 UI 栈，逐步替换 Element Plus 默认后台质感。
- 严格只改 `AICopilot/src/vues/AICopilot.Web` 的前端展示层，不碰 Cloud、Edge、AICopilot 后端。

核心修正：

上一版方案中过于保守的“第一阶段不新增 UI 框架、不引入 Tailwind/reka/lucide”应删除。既然本轮目标是 UI 重构和视觉天花板，AI 端应获准引入与云端同款 Niond UI 栈。

## 2. 当前事实

当前 AICopilot 前端位置：

```text
AICopilot/src/vues/AICopilot.Web
```

当前技术栈：

- Vue 3
- Vue Router 4
- Pinia
- Element Plus
- `@element-plus/icons-vue`
- ECharts
- markdown-it
- `@microsoft/fetch-event-source`
- `@vueuse/core`

当前主要页面：

- `/login`：账号密码登录与 Cloud OIDC 登录。
- `/chat`：AI 会话、Agent 工作台、审批、产物、审计、在岗声明。
- `/config`：模型、路由、模板、数据分析、MCP、审批、Agent 工作区配置。
- `/knowledge`：知识库、文档治理、嵌入模型、检索预览。
- `/access`：用户、角色、权限矩阵、审计日志。
- `/forbidden`：无权限页面。

当前主要问题：

- `AppShell` 仍是文字 sidebar，像普通后台，不像 AI 工作台。
- `/chat` 功能完整，但会话、消息、Agent 状态、产物和审批的层级不够清晰。
- `/login` 缺少与云端 Niond 同源的高级感。
- `/config`、`/knowledge`、`/access` 仍有明显 Element Plus 默认后台质感。
- AI 端视觉若继续限制在 Element Plus 上，难以达到云端 Niond 已经验证过的审美水平。

## 3. Approved Stack

AICopilot.Web 获用户批准使用与云端 `iiot-web` 同源的 UI 栈：

```json
{
  "@tailwindcss/vite": "^4.3.0",
  "tailwindcss": "^4.3.0",
  "reka-ui": "^2.9.7",
  "lucide-vue-next": "^1.0.0",
  "class-variance-authority": "^0.7.1",
  "clsx": "^2.1.1",
  "tailwind-merge": "^3.6.0",
  "vue-i18n": "^11.4.2"
}
```

说明：

- `@vueuse/core` 当前已经存在，继续保留。
- `element-plus` 和 `@element-plus/icons-vue` 作为迁移期旧控件暂时保留。
- Phase 3 完成后，再删除 `element-plus` 和 `@element-plus/icons-vue`。
- 同步更新 `iiot-frontend-polish` skill，记录 “AICopilot.Web 于 2026-05-15 获用户批准使用同款 Niond UI 栈”。

## 4. 设计方向

设计定位：

```text
云端 Niond 同源 + AI Command Workbench 差异化
```

同源部分：

- 浅灰画布。
- 白色功能卡。
- 大圆角。
- 柔和阴影。
- lime 点缀。
- 清晰数据层级。

AI 差异化部分：

- icon-only floating dock。
- 深色 AI Canvas。
- Command Composer。
- Agent 状态面板。
- 更强的任务、审批、工具调用和产物可视化。

建议颜色比例：

- 中性灰白：60%
- 深色 AI 工作区：25%
- lime / teal / 柔色状态：15%

禁止项：

- 不照抄参考图里的健康、训练、Upgrade、美刀、订阅、生活方式内容。
- 不做蓝紫赛博渐变主背景。
- 不做装饰光球、bokeh、无意义网格。
- 不出现暗示 Cloud 写入能力的 UI 文案。

文案规则：

- 默认中文。
- 英文只通过语言切换出现。
- `AICopilot / Agent / RAG / MCP / Cloud` 等技术名可以保留。

## 5. 分阶段实施

### Phase 1：新栈接入 + 视觉标杆

目标：

- 先做出 AI 端视觉标杆。
- 不深拆消息协议和业务组件。
- 让用户先确认 “AI 端方向对了”。

Phase 1a：基础设施

- 接入 Tailwind v4 和 `@tailwindcss/vite`。
- 接入 `reka-ui`、`lucide-vue-next`、CVA/clsx/tailwind-merge、`vue-i18n`。
- 建立 AI 端 Niond tokens：背景、白卡、深色 Canvas、lime、teal、border、shadow、radius、focus。
- 建立基础工具函数和 UI 组件目录，但不迁移业务页。
- 更新 skill Approved Stack Exceptions。

Phase 1b：Login + AppShell

- `LoginView` 改为浅灰画布、大白色 shell、左侧表单、右侧 AI 状态预览。
- 右侧预览展示 Cloud 只读连接、RAG、MCP、人机审批、Agent 工作区。
- `AppShell` 改为 icon-only floating dock。
- 导航项不显示文字，但必须有 tooltip 和 `aria-label`。
- 当前导航用深色圆形底或 lime 圆形底突出。
- 权限判断继续使用 `canUseChat / canViewConfig / canManageKnowledge / canManageAccess`。

Phase 1c：Chat 外骨架

- `/chat` 改为：
  - 左侧会话/任务 rail。
  - 中央深色 AI Canvas。
  - 右侧 Agent 状态面板。
  - 底部 Command Composer。
- 保留 Chat 内部业务组件和 Element Plus 控件。
- 不改 SSE、chunk reducer、approval protocol、widget payload、Agent task API。

### Phase 2：Chat 内部组件视觉重做

目标：

- 让 Chat 的消息、审批、工具调用、widget、产物、审计都统一为 AI 工作台风格。
- 逻辑零改，只换展示层。

范围：

- `MessageItem`
- `ApprovalCard`
- `FunctionCallItem`
- `ArgumentViewer`
- `SessionList`
- `WidgetRenderer`
- `ChartWidget`
- `DataTableWidget`
- `StatsWidget`

要求：

- 用 lucide 替换 Element Plus icons。
- 简单控件改为 reka-ui 或自定义 `Ai*` 组件。
- markdown 渲染、SSE 流、审批协议、widget payload 不变。
- 工具调用结果、失败状态、审批状态、产物状态必须视觉清楚。

### Phase 3：管理页迁移 + 删除 Element Plus

目标：

- 让 Config、Knowledge、Access 与 Chat 工作台视觉统一。
- 完成 Element Plus 迁移，减少 bundle 和默认后台质感。

范围：

- `/config`
- `/knowledge`
- `/access`
- `/forbidden`

新增基础展示组件：

- `AiDataPage`
- `AiTableCard`
- `AiToolbar`
- `AiActionGroup`
- `AiDrawer`
- `AiModal`
- `AiButton`
- `AiInput`
- `AiSelect`
- `AiTag`

保留业务：

- Config 保留模型、可靠性、路由、模板、数据分析、MCP、审批、Agent tabs。
- Knowledge 保留知识库、检索预览、文档治理、嵌入模型配置。
- Access 保留用户、角色、权限矩阵、审计日志。

收尾：

- 删除 `element-plus`。
- 删除 `@element-plus/icons-vue`。
- 确认 `rg "element-plus|@element-plus/icons-vue|<el-" src package.json package-lock.json` 无业务残留。
- 确认 build 输出无 Element Plus 相关 chunk。

## 6. 公共接口与业务边界

不得修改：

- 后端 API。
- DTO。
- SSE 协议。
- approval protocol。
- widget payload。
- router guard。
- Pinia store 方法签名。
- AICopilot 后端。
- IIoT.CloudPlatform。
- IIoT.EdgeClient。

不得新增：

- Cloud 写入能力。
- 隐式 Cloud 写入文案。
- 绕过审批的人机协同入口。
- 后端 NuGet 依赖。

允许新增：

- 前端展示组件。
- 前端静态文案 i18n。
- 前端视觉 token。
- 前端布局组件。

新增组件只承载展示层，不直接发 API，不承接权限判断，不写审批业务逻辑。

## 7. 验收计划

每个 Phase 必跑：

```powershell
cd AICopilot/src/vues/AICopilot.Web
npm run build
npm run test:unit
```

环境允许时跑：

```powershell
npm run test:smoke
```

Phase 1 视觉验收：

- `/login`
- `/chat`
- `1920x1080`
- `1366x768`
- `1024x768`

检查：

- icon dock 无文字一级导航。
- tooltip 和 `aria-label` 完整。
- active 态明显。
- 深色 AI Canvas 有高级感但不赛博。
- Command Composer 不像普通 textarea。
- 右侧 Agent 面板不拥挤。
- 1024 宽度不重叠。

Phase 2 功能回归：

- 发送消息。
- SSE 流式渲染。
- markdown 渲染。
- 审批通过/拒绝。
- 工具调用展示。
- widget 渲染。
- Agent 任务继续。
- 产物下载。

Phase 3 全站验收：

- `/config`
- `/knowledge`
- `/access`
- `/forbidden`
- 配置 CRUD。
- 知识库上传/治理。
- 权限管理。
- 审计日志。
- 主题切换。
- 语言切换。

文案扫描：

- 默认中文。
- 无 `Upgrade`。
- 无美刀、健康、训练、订阅参考图残留。
- 无暗示 Cloud 写入能力文案。

## 8. 风险与控制

风险 1：AI 端业务比云端复杂。

控制：

- Element Plus 不能一次性删除。
- Phase 1 只做外骨架。
- Chat 内部组件放到 Phase 2。
- 管理页放到 Phase 3。

风险 2：ChatWindow 功能密集，容易误伤协议。

控制：

- 不改 SSE。
- 不改 approval protocol。
- 不改 widget payload。
- 不改 store 方法签名。
- 每次只替换展示层。

风险 3：icon-only dock 可用性不足。

控制：

- 每个图标必须有 tooltip。
- 每个图标必须有 `aria-label`。
- 点击区域不小于 44px。
- active 态必须明显。

风险 4：AI 深色区域跑偏成赛博风。

控制：

- 深色只用于 AI Canvas 和关键状态面板。
- 页面主背景仍保持云端同源浅灰。
- lime 只做少量点缀。

## 9. Claude 审核重点

请重点审核：

1. 新 UI 栈是否已明确获得用户批准，而不是擅自加依赖。
2. 分 3 阶段迁移是否足以降低 Chat/SSE/approval/widget 风险。
3. Phase 1 是否应该进一步限定为 Login + AppShell + Chat 外骨架。
4. 是否有任何地方暗示 AICopilot 可以写入 Cloud 业务数据。
5. 新增 `vue-i18n` 是否合理，是否需要首期只做静态文案。
6. 是否需要先把 skill 的 Approved Stack Exceptions 合并后再实施代码。
