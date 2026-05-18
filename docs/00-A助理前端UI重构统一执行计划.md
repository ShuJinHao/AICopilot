# A助理前端 UI 重构统一执行计划

版本：v1.0

日期：2026-05-15

状态：前端统一计划，未开始代码实现

适用范围：仅 `AICopilot/src/vues/AICopilot.Web`

不包含：AICopilot 后端、IIoT.CloudPlatform、IIoT.EdgeClient、数据库、MCP 后端、Cloud 写入能力

---

## 0. 为什么需要这份统一计划

当前已有三份相关计划：

1. `docs/A助理二期整体拆分执行计划.md`
2. `docs/A助理前端分离重构修复计划.md`
3. `资料/AICopilot-UI智能工作台改造方案-2026-05-15.md`

三份计划单独看都成立，但合在一起执行时存在四个冲突：

- 右侧面板数量不一致：有的说 1 个 Agent panel，有的拆成 6 个面板。
- Chat 中央区命名和视觉不一致：有的叫 ChatPanel，有的叫深色 AI Canvas。
- Phase 节奏不一致：二期整体是阶段 0-6，UI 方案是 Phase 1a/1b/1c，前端分离计划没有拆小阶段。
- UI 栈承接不一致：UI v2 明确使用 Tailwind/reka/lucide，前端分离计划未说明组件用什么栈实现。

本文件作为前端执行时的统一口径。后续前端 Agent / UI Agent / Gemini / Codex 实施时，以本文件为入口，再按引用去读其它计划。

---

## 1. 阅读顺序

前端执行前按以下顺序阅读：

1. `AICopilot/AGENTS.md`
2. `docs/A助理二期整体拆分执行计划.md`
3. `docs/A助理前端分离重构修复计划.md`
4. `资料/AICopilot-UI智能工作台改造方案-2026-05-15.md`
5. 本文件：`docs/00-A助理前端UI重构统一执行计划.md`

冲突处理规则：

- 项目边界、Cloud 只读、安全边界：以 `AICopilot/AGENTS.md` 和二期整体计划为准。
- 前端功能拆分、状态管理、DTO 使用：以前端分离重构计划为准。
- 视觉风格、UI 栈、Niond 同源：以 UI 智能工作台改造方案 v2 为准。
- 如果三份文档对同一 UI 结构描述不同，以本文件的“统一命名和执行节奏”为准。

---

## 2. 总目标

前端目标不是继续在聊天页上堆功能，而是重构为：

> 云端 Niond 同源的 A助理 AI Command Workbench

具体目标：

- 与云端 UI 视觉同源：浅灰画布、白色功能卡、大圆角、柔和阴影、lime 点缀。
- AI 端保留差异化：icon-only floating dock、深色 AI Canvas、Command Composer、AgentWorkbenchPanel。
- 前端只负责展示、交互、状态编排和用户反馈，不承担后端安全判断。
- 不新增 Cloud 写入能力，不暗示 AI 可以写 Cloud 业务数据。
- 每个子阶段独立验收，避免一次性重写 50+ 组件导致失控。

---

## 2.1 视觉锚点：参考图吸收方式

本次 AI 端重构要吸收用户提供的 Be.run 风格参考图，但不能照抄业务内容。

要吸收的设计语言：

- 外层使用暖灰 / 米灰 / 石墨灰画布，而不是冷冰冰的纯白页面或蓝紫渐变背景。
- 主界面使用大圆角白色 shell，内部是分区明确的工作台，而不是传统后台满屏表格。
- 左侧一级导航采用 icon-only floating dock，不显示文字导航；必须提供 tooltip 和 `aria-label`。
- 导航按钮使用 44px 以上圆形 / 胶囊形点击区，active 态可以用深色圆形底、lime 小图标或柔和高亮。
- 中央内容采用大卡片、强留白、清晰标题和状态块，卡片圆角保持 22-28px。
- AI 主工作区允许使用深色石墨卡片形成对比，类似参考图右上深色卡，但内容必须是 Agent / 审批 / 任务 / 产物状态。
- 可以使用少量柔和光感数据可视化，例如状态气泡、进度条、任务热区，但必须服务信息表达，不能做装饰光球。
- 底部输入区使用 Command Composer，视觉上像一个可执行命令的工作台入口，而不是普通聊天输入框。
- 颜色比例保持克制：中性暖灰白为主体，深色 AI Canvas 做重点区域，lime / teal 只做状态和行动点缀。

禁止照抄的内容：

- 不出现健康、训练、步数、日历训练、饮食、Upgrade、美刀、订阅等参考图业务内容。
- 不把 AI 端做成健身 app、营销页或生活方式 dashboard。
- 不为了“科技感”堆蓝紫渐变、发光球、赛博网格或无意义动态背景。
- 不回到 Element Plus 默认后台质感；本轮是前端 UI 重构，不是旧页面套壳。

这张参考图的作用是定义“高级感、icon dock、卡片节奏、深浅对比和空间感”，不是定义业务模块。

---

## 3. UI 技术栈统一口径

AICopilot.Web 已获用户批准使用与云端同源的 UI 栈：

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

- `@vueuse/core` 当前已存在，继续保留。
- `element-plus` 和 `@element-plus/icons-vue` 是迁移期依赖，不是目标 UI 栈。
- Phase 1 和 Phase 2 可暂时保留 Element Plus，避免误伤 Chat/SSE/审批/widget。
- Phase 3 结束后删除 Element Plus，并确认无 `<el-*>` 业务残留。

所有新增前端基础组件使用 Tailwind + reka-ui + lucide + CVA/clsx/tailwind-merge 实现。

---

## 4. 统一信息架构

最终前端主工作台采用以下结构：

```text
AiAppShell
├── AiIconDock
│   ├── AI 工作台
│   ├── 知识库
│   ├── 运行配置
│   ├── 权限治理
│   ├── 返回云平台
│   ├── 主题切换
│   └── 用户 / 退出
└── AiWorkbenchLayout
    ├── SessionTaskRail
    │   ├── 会话列表
    │   └── Agent 任务历史
    ├── AiCanvas
    │   ├── MessageTimeline
    │   ├── WidgetRenderer
    │   └── CommandComposer
    └── AgentWorkbenchPanel
        ├── 计划
        ├── 步骤
        ├── 审批
        ├── 产物
        ├── 审计
        └── 边界
```

命名统一：

- 中央主区统一叫 `AiCanvas`。
- 右侧统一叫 `AgentWorkbenchPanel`。
- 左侧会话/任务区统一叫 `SessionTaskRail`。
- 底部输入区统一叫 `CommandComposer`。
- 全局图标导航统一叫 `AiIconDock`。

旧文档里的 `ChatPanel` 等价于本文件里的 `AiCanvas`。
旧文档里的多个右侧面板统一收纳进 `AgentWorkbenchPanel` 的内部 tab。

---

## 5. 右侧面板冲突解决

不创建 6 个并列右侧面板。1366×768 屏幕无法承载。

统一方案：

```text
AgentWorkbenchPanel，宽度 320-400px
内部 tab：
  计划
  步骤
  审批
  产物
  审计
  边界
```

子视图映射：

| 前端分离计划组件 | 统一后位置 |
|---|---|
| `AgentPlanPanel` | `AgentWorkbenchPanel` 的“计划”tab |
| `AgentStepsPanel` | `AgentWorkbenchPanel` 的“步骤”tab |
| `ApprovalQueuePanel` | `AgentWorkbenchPanel` 的“审批”tab |
| `ArtifactSummaryPanel` | `AgentWorkbenchPanel` 的“产物”tab |
| `AuditSummaryPanel` | `AgentWorkbenchPanel` 的“审计”tab |
| `SafetyBoundaryPanel` | `AgentWorkbenchPanel` 的“边界”tab，另在顶部显示简短状态条 |

交互规则：

- 默认 tab 由当前状态决定：
  - 有待审批：默认打开“审批”。
  - 有运行中步骤：默认打开“步骤”。
  - 有新产物：默认打开“产物”。
  - 其它情况：默认打开“计划”或上次用户停留 tab。
- 移动到 1024px 以下时，`AgentWorkbenchPanel` 可改为抽屉，不挤压 `AiCanvas`。

---

## 6. 阶段节奏统一

二期整体计划中的“阶段 3：前端分离重构”拆为以下子阶段。

### Phase 3a：前端基础设施和状态拆分

目标：

- 接入新 UI 栈。
- 建立前端状态拆分基础。
- 不改核心交互流程。

任务：

- 接入 Tailwind v4、reka-ui、lucide、CVA/clsx/tailwind-merge、vue-i18n。
- 建立 AI Niond token。
- 建立 `Ai*` 基础 UI 目录。
- 拆分或整理 store，但不改变外部 API 行为。
- 明确 `uiLayoutStore` 只管理 UI 状态：rail 折叠、右侧 tab、抽屉、密度、主题，不存业务数据。

验收：

- `npm run build` 通过。
- `npm run test:unit` 通过。
- 业务页面视觉可暂时不变。
- 没有后端 API 改动。

### Phase 3b：AppShell + Login + Chat 外骨架

目标：

- 建立第一版视觉标杆。

任务：

- `LoginView` 改为云端同源 AI 登录页。
- `AppShell` 改为 `AiIconDock`。
- `/chat` 改为三栏骨架：
  - `SessionTaskRail`
  - `AiCanvas`
  - `AgentWorkbenchPanel`
- 建立 `CommandComposer` 外观。

验收：

- `/login`、`/chat` 在 `1920×1080`、`1366×768`、`1024×768` 不重叠。
- icon-only dock 无文字一级导航，但有 tooltip 和 `aria-label`。
- 深色 `AiCanvas` 视觉成立。
- Chat 内部原有消息、审批、widget 可继续工作。

### Phase 3c：AgentWorkbenchPanel 内部 tab

目标：

- 把右侧 6 个功能收敛成 1 个面板 + 6 个 tab。

任务：

- 实现“计划 / 步骤 / 审批 / 产物 / 审计 / 边界”tab。
- 使用现有 store 数据，不自造审批和任务状态。
- 将待审批、运行中、失败、产物生成等状态映射到默认 tab。

验收：

- 待审批时可直接定位到审批 tab。
- 步骤运行时可看到当前步骤。
- 产物生成后可看到 draft/final 状态。
- 安全边界文案清楚表达 Cloud 只读，不暗示写入。

### Phase 3d：Chat 内部组件视觉重做

目标：

- 深化 AI 工作台质感，但不改协议。

任务：

- 重做 `MessageItem`、`ApprovalCard`、`FunctionCallItem`、`ArgumentViewer`。
- 重做 `WidgetRenderer`、`ChartWidget`、`DataTableWidget`、`StatsWidget`。
- 重做 `SessionList`。
- 用 lucide 替换 Element Plus icons。
- 简单控件迁移到 reka-ui 或自定义 `Ai*` 组件。

验收：

- SSE 流式渲染正常。
- markdown 正常。
- approval protocol 不变。
- widget payload 不变。
- 工具调用、审批、产物、错误状态视觉清楚。

### Phase 3e：管理页迁移和 Element Plus 删除

目标：

- 迁移 `/config`、`/knowledge`、`/access`、`/forbidden`。
- 完成 Element Plus 替换。

任务：

- 建立并使用：
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
- Config 保留模型、可靠性、路由、模板、数据分析、MCP、审批、Agent tabs。
- Knowledge 保留知识库、检索预览、文档治理、嵌入模型配置。
- Access 保留用户、角色、权限矩阵、审计日志。
- 删除 `element-plus` 和 `@element-plus/icons-vue`。

验收：

- `rg "element-plus|@element-plus/icons-vue|<el-" src package.json package-lock.json` 无业务残留。
- build 输出无 Element Plus chunk。
- 配置 CRUD、知识库上传/治理、权限管理、审计日志可用。

---

## 7. 与后端改造的并行规则

本文件只定义前端工作。

前端可并行做：

- UI 栈接入。
- token。
- AppShell。
- Login。
- Chat 外骨架。
- 只依赖现有 DTO 的视觉重排。

前端必须等待后端稳定后再做：

- 新 Agent task 字段展示。
- 新 approval 类型展示。
- 新 artifact/workspace 字段展示。
- 新错误码精细化展示。
- 动态 Agent Planner 的结构化 plan UI。

如果后端契约尚未稳定：

- 前端只能基于当前已有 DTO 做兼容展示。
- 不允许在前端先发明字段。
- 不允许将 mock 字段写入生产路径。
- 不允许把“未来会有的后端状态”显示成已支持能力。

---

## 8. 前端状态管理统一口径

前端分离计划中提到 12 个 store，方向是对的，但必须避免出现新的杂物 store。

原则：

- 业务状态归业务 store。
- UI 展示状态归 `uiLayoutStore`。
- API 调用集中在 service/store，不进展示组件。
- 组件只接收 props、emit 事件或调用已有 store action。

`uiLayoutStore` 允许保存：

- icon dock 折叠状态。
- session rail 折叠状态。
- AgentWorkbenchPanel 当前 tab。
- 移动端抽屉开关。
- 面板宽度。
- 当前视觉密度。

`uiLayoutStore` 禁止保存：

- 当前 session 业务数据。
- Agent task 数据。
- approval 数据。
- artifact 数据。
- Cloud 数据。
- 用户权限判断。

---

## 9. 视觉验收标准

必须达到：

- AI 端与云端 Niond 同源，不像另一个系统。
- AI 端比云端更像智能工作台，不是普通后台。
- icon dock 无文字一级导航，但 tooltip 完整。
- `AiCanvas` 是明确的深色工作区，但不赛博、不蓝紫渐变泛滥。
- `CommandComposer` 是指挥台式输入，不是普通 textarea。
- `AgentWorkbenchPanel` 不拥挤，tab 清楚。
- 默认中文，无无关英文营销文案。
- 不出现 `Upgrade`、美刀、健康、训练、订阅等参考图残留。
- 不出现暗示 Cloud 写入能力的 UI 文案。
- 1024px 宽度不重叠。

视口：

- `1920×1080`
- `1366×768`
- `1024×768`

---

## 10. 功能验收标准

每个子阶段至少执行：

```powershell
cd AICopilot/src/vues/AICopilot.Web
npm run build
npm run test:unit
```

环境允许时执行：

```powershell
npm run test:smoke
```

功能回归：

- 登录。
- Cloud OIDC complete。
- 退出。
- 主题切换。
- 语言切换。
- 会话切换。
- 发送消息。
- SSE 流式渲染。
- markdown 渲染。
- 文件上传。
- 审批通过/拒绝。
- Agent 任务继续。
- 工具调用展示。
- widget 渲染。
- 产物下载。
- 配置 CRUD。
- 知识库上传/治理。
- 权限管理。
- 审计日志。

---

## 11. 禁止事项

本轮前端计划禁止：

- 修改 AICopilot 后端。
- 修改 Cloud。
- 修改 Edge。
- 增加 Cloud 写入能力。
- 在前端伪造审批通过。
- 前端绕过 final 流程。
- 前端保存 API Key 明文。
- 前端保存敏感数据到 localStorage。
- 前端自判 RAG / Cloud 权限。
- 把 draft 显示成正式产物。
- 用 mock 字段冒充真实后端能力。
- 一次性删除 Element Plus。
- 一次性重写 Chat 全部内部组件。

---

## 12. 交付给 Claude / Gemini 的执行摘要

如果要交给 Claude 或 Gemini 执行，先给这一段：

```text
本轮只做 AICopilot.Web 前端 UI 重构，不碰 AICopilot 后端、Cloud、Edge。

请先读：
1. AICopilot/AGENTS.md
2. docs/A助理二期整体拆分执行计划.md
3. docs/A助理前端分离重构修复计划.md
4. 资料/AICopilot-UI智能工作台改造方案-2026-05-15.md
5. docs/00-A助理前端UI重构统一执行计划.md

执行以第 5 份为统一入口。

关键统一口径：
- AI 中央区叫 AiCanvas，使用深色工作区。
- 右侧只保留 1 个 AgentWorkbenchPanel，内部 tab 收纳计划、步骤、审批、产物、审计、边界。
- 阶段 3 拆成 3a/3b/3c/3d/3e，每步独立验收。
- UI 栈使用 Tailwind v4 + reka-ui + lucide + CVA/clsx/tailwind-merge + vue-i18n。
- Element Plus 是迁移期依赖，Phase 3e 后删除。
- 不改后端 API、DTO、SSE、approval protocol、widget payload、router guard、Pinia store 方法签名。
- 不新增 Cloud 写入能力。
```
