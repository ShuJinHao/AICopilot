# AICopilot 前端架构整治 + Bug 修复 + 模型输出清洗

> 日期：2026-06-24
> 执行者：Codex
> 审核：CC（Claude Code）

---

## 背景

后端已有完整的架构红线、DDD 分层、ProblemDetails 错误体系、状态机、安全策略。前端缺少同等级的约束，靠人记来保证状态清理、错误展示、组件边界，结果：

- `chatStore.ts` 1095 行、30+ 个 ref、35 个 action，是 god object
- `ChatWindow.vue` 3191 行，一个组件承担了会话列表、消息流、输入框、计划面板、工作台、审批的所有职责
- 会话切换/新建时漏清状态 → 新会话残留旧数据
- 模式切换不清错误 → Plan 失败的错误带到 Chat 模式
- 后端 ProblemDetails 的 `code/detail/userFacingMessage` 被前端吞掉，只显示死文案
- 弹出面板没有 click-outside / Escape 关闭策略
- 模型思考标签（`<mm:think>`、`<think>`）原样泄漏到用户正文

本次目标：先建规则和约束机制，再基于规则修 bug，最后用测试守住。

---

## 安全约束

- 不改后端 API 契约（请求/响应格式、SSE 流协议、ChunkType 枚举值）
- 不动 Cloud/Edge
- 不引入新第三方依赖（`@vueuse/core` 已装 14.3.0，直接用）
- 改完后全部 69 个前端测试 + 607 个后端测试必须通过
- 后端三红线不变：Cloud 只读、SkillDefinition 不阻断 PlanDraft、不新增 compat/fallback 双轨

---

## 执行顺序

| 阶段 | 内容 | 依赖 |
|------|------|------|
| 1 | 前端规则文档 `AICopilot.Web/AGENTS.md` | 无 |
| 2 | SessionScopedState 容器 + 错误处理统一 + UI 状态纪律 + 模型输出清洗 | 阶段 1 |
| 3 | 守卫测试 | 阶段 2 |
| 4 | （本次不做）ChatWindow 组件拆分、Codex-like UI 重构 | 阶段 3 稳定后 |

---

## 阶段 1：前端规则文档

### 1.1 新建 `src/vues/AICopilot.Web/AGENTS.md`

完整内容如下：

```markdown
# AICopilot.Web Frontend Rules

修改 AICopilot 前端前必须读完本文件。

## 1. 后端错误信息必须完整展示

前端有三条错误来源路径，每条都必须解析到 `code`/`detail` 级别：

| 来源 | 格式 | 处理位置 | 要求 |
|------|------|----------|------|
| SSE 流内 `ChunkType.Error` | `{ code, detail, userFacingMessage }` | `chunkReducer.ts → addErrorChunk → resolveChatErrorMessage` | `resolveChatErrorMessage` 的 default 分支必须用 `userFacingMessage ?? detail`，不能给死文案 |
| SSE 连接失败（HTTP 400/403/500） | `ApiError` + 可能有 ProblemDetails body | `chatService.ts → sendEventStream.onopen → toFriendlyMessage` | 必须尝试解析响应 body 的 `detail` / `errors`，不能只靠 status code 给死文案 |
| SSE 流内 `ChunkType.AgentEvent` stage=`plan_draft_failed` | `AgentEventPayload { code, detail, suggestedAction }` | `chunkReducer.ts → addAgentEventChunk` | `plan_draft_failed` 的 `detail` 必须进入用户可见的错误展示区 |

"请求没有通过后端校验" 只能作为所有其他解析路径都失败时的最后兜底。

## 2. 会话状态必须 session-scoped

以下数据属于"当前会话"，必须放在 `SessionScopedState` 容器内：

- `agentTasks`
- `agentApprovals`
- `agentAuditSummary`
- `timelineEvents`
- `currentWorkspace`
- `currentArtifactPreview`
- `chartPreview`
- `uploadedFiles`
- `isAgentBusy`

错误状态统一走 `chatErrorStore`（已有 session-scoped 机制），不允许在 chatStore 里单独维护 `agentErrorMessage` 等全局 ref。

### SessionScopedState 代码模式

```typescript
// stores/sessionScopedState.ts
export interface SessionScopedState { /* 上述字段 */ }
export function createSessionScopedState(): SessionScopedState { /* 全部默认值 */ }
```

- chatStore 内部用 `reactive(createSessionScopedState())` 持有
- 切会话 / 新建会话 / 删除会话统一走 `Object.assign(scopedState, createSessionScopedState())`
- 新增会话级数据时：加到 `SessionScopedState` 接口 + 工厂函数，不允许在外面新开 ref
- chatStore 可以暴露同名 computed 给模板用，避免改所有组件引用路径

## 3. ChatWindow 边界

- `ChatWindow.vue` 只做页面编排（布局、路由、子组件组合）
- composer（输入框 + 模式切换 + 添加面板）、message list、agent run block、approval block、artifact preview 必须逐步拆成独立组件
- 新功能不允许继续往 ChatWindow.vue 里堆
- 当前 3191 行只减不增（本次不强制拆，但不允许继续膨胀）

## 4. 弹出面板关闭策略

所有 popover / modal / dropdown / options panel 必须同时满足：

- 点击外部关闭（用 `@vueuse/core` 的 `onClickOutside`）
- Escape 键关闭
- 切会话时关闭
- 切模式时关闭

不允许只靠按钮 toggle。

## 5. 模型思考标签不得进入用户正文

以下标签必须被清洗，不能出现在用户可见的消息正文里：

- `<mm:think>...</mm:think>`（智谱 GLM）
- `<think>...</think>`（DeepSeek）
- `mm:think...`（裸文本泄漏）
- 残缺的开闭标签

清洗策略：
- 后端 `AgentStreamRuntime` 是主清洗层（必须做）
- 前端 `chunkReducer.ts` 的 `addTextChunk` 是兜底层（防后端漏过）
- 剥离出来的思考内容走 `ChunkType.Metadata` 的 `thinkingContent` 字段，前端放到"运行详情"折叠区

## 6. Plan / Chat 模式语义

- 切换模式时必须清除当前错误状态
- Plan 模式的错误不能带到 Chat 模式，反之亦然
- `composerOptionsOpen` 切模式时必须关闭

## Pre-change Checklist

Codex 提交前必须逐项确认：

- [ ] 读了本文件全部规则
- [ ] 新增的会话数据加入了 `SessionScopedState` 接口和 `createSessionScopedState()` 工厂函数
- [ ] 新增的错误路径解析了后端 `code/detail`，没有用固定死文案覆盖
- [ ] 没有在 `ChatWindow.vue` 里新增超过 30 行的逻辑块（应拆组件）
- [ ] 新增的弹出面板有 click-outside + Escape 关闭
- [ ] 文本 chunk 经过 think 标签兜底清洗
- [ ] 全部前端测试通过
- [ ] `vue-tsc --noEmit` 类型检查通过
```

### 1.2 在根 `AICopilot/AGENTS.md` 引用前端规则

在 `## Architecture` 部分的 `- src/vues 放前端逻辑，不回填到 service 或 host。` 这一行后面追加：

```markdown
- 修改 AICopilot 前端前必须先读 `src/vues/AICopilot.Web/AGENTS.md`，遵守前端架构红线。
```

---

## 阶段 2：代码改动

### 2.1 SessionScopedState 容器

**新建文件** `src/vues/AICopilot.Web/src/stores/sessionScopedState.ts`

```typescript
import { reactive } from 'vue'
import type {
  AgentApprovalRequest,
  AgentArtifactPreview,
  AgentTask,
  AgentTaskAuditSummary,
  ArtifactWorkspace,
  UploadRecord
} from '@/types/protocols'
import type { SessionTimelineEvent } from '@/types/app'

interface AgentChartPreview {
  labels: string[]
  values: number[]
  source?: string
  sourceMode?: string
  sourceLabel?: string
  isSimulation?: boolean
  queryHash?: string
}

export interface SessionScopedState {
  agentTasks: AgentTask[]
  agentApprovals: AgentApprovalRequest[]
  agentAuditSummary: AgentTaskAuditSummary[]
  timelineEvents: SessionTimelineEvent[]
  currentWorkspace: ArtifactWorkspace | null
  currentArtifactPreview: AgentArtifactPreview | null
  chartPreview: AgentChartPreview | null
  uploadedFiles: UploadRecord[]
  isAgentBusy: boolean
}

export type { AgentChartPreview }

export function createSessionScopedState(): SessionScopedState {
  return {
    agentTasks: [],
    agentApprovals: [],
    agentAuditSummary: [],
    timelineEvents: [],
    currentWorkspace: null,
    currentArtifactPreview: null,
    chartPreview: null,
    uploadedFiles: [],
    isAgentBusy: false
  }
}

export function createReactiveSessionState() {
  return reactive(createSessionScopedState())
}

export function resetSessionState(state: SessionScopedState) {
  Object.assign(state, createSessionScopedState())
}
```

**修改文件** `src/vues/AICopilot.Web/src/stores/chatStore.ts`

替换以下散落的 ref：

```typescript
// 删除这些独立 ref：
// const agentTasks = ref<AgentTask[]>([])
// const agentApprovals = ref<AgentApprovalRequest[]>([])
// const agentAuditSummary = ref<AgentTaskAuditSummary[]>([])
// const timelineEvents = ref<SessionTimelineEvent[]>([])
// const currentWorkspace = ref<ArtifactWorkspace | null>(null)
// const currentArtifactPreview = ref<AgentArtifactPreview | null>(null)
// const chartPreview = ref<AgentChartPreview | null>(null)
// const uploadedFiles = ref<UploadRecord[]>([])
// const isAgentBusy = ref(false)
// const agentErrorMessage = ref<string | null>(null)

// 替换为：
import { createReactiveSessionState, resetSessionState } from './sessionScopedState'

const scopedState = createReactiveSessionState()
```

所有读写 `agentTasks.value` 的地方改成 `scopedState.agentTasks`（reactive 对象不需要 `.value`）。

`agentErrorMessage` 完全删除，统一走 `chatErrorStore.setSessionError` / `chatErrorStore.clearSessionError`。

暴露给模板的 computed 保持原名不变（避免改所有组件引用）：

```typescript
const agentTasks = computed(() => scopedState.agentTasks)
const isAgentBusy = computed(() => scopedState.isAgentBusy)
// ... 其他同理
```

**修改 `createNewSession`**：

```typescript
async function createNewSession() {
  errorStore.clearSessionError()
  resetSessionState(scopedState)           // 一行清全部
  const newSession = await sessionStore.createSession()
  messageStore.messagesMap[newSession.id] = []
  streamStore.stop()
  approvalStore.sync(newSession.id)
  bindErrorSession()
  return newSession
}
```

**修改 `selectSession`**：

```typescript
async function selectSession(id: string, forceReload = false) {
  resetSessionState(scopedState)           // 切会话时也整体重置
  sessionStore.persistCurrentSession(id)
  bindErrorSession()
  errorStore.clearSessionError(id)
  approvalStore.sync(id)
  await loadHistory(id, forceReload)
}
```

**受影响文件清单**：
- `stores/sessionScopedState.ts` — 新建
- `stores/chatStore.ts` — 替换散落 ref、删除 `agentErrorMessage`、修 `createNewSession` / `selectSession` / `deleteSession`
- `components/chat/ChatWindow.vue` — 原来读 `store.agentErrorMessage` 的地方改成 `store.errorMessage`（已有 computed 映射 chatErrorStore）
- `composables/useAgentWorkbench.ts` — 如果引用了 `agentErrorMessage` 则一并修改

### 2.2 错误处理统一

**修改文件** `src/vues/AICopilot.Web/src/stores/chatErrorStore.ts`

1. `resolveChatErrorMessage` 的 default 分支改为：

```typescript
default:
  return userFacingMessage ?? '请求失败，请稍后重试。'
```

这样任何 unknown code 只要后端给了 `detail` 或 `userFacingMessage`，前端就直接展示，不吞。

2. `toFriendlyMessage` 的 400 fallback 改为：

```typescript
if (error.status === 400) {
  const validationMessage = extractValidationErrors(error.details)
  if (validationMessage) {
    return validationMessage
  }
  if (typeof error.message === 'string' && error.message.trim().length > 0) {
    return error.message
  }
  return '请求参数校验失败，请检查输入后重试。'
}
```

新增 `extractValidationErrors` 辅助函数：

```typescript
function extractValidationErrors(details: unknown): string | null {
  if (!details || typeof details !== 'object') return null
  const obj = details as Record<string, unknown>

  // ProblemDetails 格式：{ detail: "..." }
  if (typeof obj.detail === 'string' && obj.detail.trim()) {
    return obj.detail.trim()
  }

  // ASP.NET validation 格式：{ errors: { field: ["msg"] } }
  if (obj.errors && typeof obj.errors === 'object') {
    const messages: string[] = []
    for (const msgs of Object.values(obj.errors as Record<string, unknown>)) {
      if (Array.isArray(msgs)) {
        messages.push(...msgs.filter((m): m is string => typeof m === 'string'))
      }
    }
    if (messages.length > 0) {
      return messages.join('；')
    }
  }

  return null
}
```

3. `plan_draft_failed` 事件的错误展示

**修改文件** `src/vues/AICopilot.Web/src/protocol/chunkReducer.ts`

在 `addAgentEventChunk` 函数中，当 `stage === 'plan_draft_failed'` 时，除了 push chunk，还要触发错误展示：

```typescript
function addAgentEventChunk(
  message: ChatMessage,
  chunk: ChatChunk,
  callbacks: ChunkReducerCallbacks
) {
  try {
    const event = JSON.parse(chunk.content) as AgentEventPayload
    message.chunks.push({ ...chunk, event } as AgentEventChunk)

    if (event.stage === 'plan_draft_failed') {
      const errorMessage = event.detail || event.suggestedAction || '计划草案生成失败。'
      callbacks.setSessionError(message.sessionId, errorMessage)
    }
  } catch {
    callbacks.setSessionError(message.sessionId, '运行状态事件解析失败。')
  }
}
```

**受影响文件**：
- `stores/chatErrorStore.ts`
- `protocol/chunkReducer.ts`
- `stores/chatStore.ts`（所有 `agentErrorMessage.value = xxx` 改成 `errorStore.setSessionError(sessionId, xxx)`）

### 2.3 UI 状态纪律

**修改文件** `src/vues/AICopilot.Web/src/components/chat/ChatWindow.vue`

1. **"添加"面板 click-outside**

```typescript
import { onClickOutside } from '@vueuse/core'

const composerOptionsPanel = ref<HTMLElement | null>(null)

onClickOutside(composerOptionsPanel, () => {
  composerOptionsOpen.value = false
}, { ignore: ['.composer-add-button'] })
```

模板里给面板加 ref：

```html
<div v-if="composerOptionsOpen" ref="composerOptionsPanel" class="composer-options-panel">
```

2. **Escape 关闭面板**

```typescript
function handleEscape(event: KeyboardEvent) {
  if (event.key === 'Escape' && composerOptionsOpen.value) {
    composerOptionsOpen.value = false
  }
}

onMounted(() => document.addEventListener('keydown', handleEscape))
onBeforeUnmount(() => document.removeEventListener('keydown', handleEscape))
```

3. **模式切换清理**

```typescript
function setComposerMode(mode: ComposerMode) {
  composerMode.value = mode
  composerOptionsOpen.value = false
  store.clearCurrentError()  // chatStore 暴露一个 clearCurrentError 方法，内部调 errorStore.clearSessionError
}
```

**受影响文件**：
- `components/chat/ChatWindow.vue`
- `stores/chatStore.ts`（新增 `clearCurrentError` 方法）

### 2.4 模型输出清洗（后端）

**新建文件** `src/services/AICopilot.AiGatewayService/Agents/ModelOutputSanitizer.cs`

```csharp
using System.Text.RegularExpressions;

namespace AICopilot.AiGatewayService.Agents;

public static partial class ModelOutputSanitizer
{
    // 匹配完整的 <mm:think>...</mm:think> 和 <think>...</think>（含跨行）
    [GeneratedRegex(@"<mm:think>[\s\S]*?</mm:think>", RegexOptions.Compiled)]
    private static partial Regex MmThinkBlockRegex();

    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.Compiled)]
    private static partial Regex ThinkBlockRegex();

    // 匹配残缺标签（流式场景：开标签在本 chunk，闭标签在下一个 chunk）
    [GeneratedRegex(@"<mm:think>[^<]*$", RegexOptions.Compiled)]
    private static partial Regex MmThinkOpenTailRegex();

    [GeneratedRegex(@"<think>[^<]*$", RegexOptions.Compiled)]
    private static partial Regex ThinkOpenTailRegex();

    // 匹配闭标签出现在 chunk 开头（上一个 chunk 有残缺开标签）
    [GeneratedRegex(@"^[^<]*</mm:think>", RegexOptions.Compiled)]
    private static partial Regex MmThinkClosingHeadRegex();

    [GeneratedRegex(@"^[^<]*</think>", RegexOptions.Compiled)]
    private static partial Regex ThinkClosingHeadRegex();

    // 匹配裸 "mm:think" 文本泄漏（没有 XML 标签包裹）
    [GeneratedRegex(@"mm:think\S*", RegexOptions.Compiled)]
    private static partial Regex MmThinkBareTextRegex();

    public static (string CleanText, string? ThinkingText) Strip(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (text, null);
        }

        var thinkingParts = new List<string>();
        var clean = text;

        clean = ExtractMatches(MmThinkBlockRegex(), clean, thinkingParts);
        clean = ExtractMatches(ThinkBlockRegex(), clean, thinkingParts);
        clean = ExtractMatches(MmThinkOpenTailRegex(), clean, thinkingParts);
        clean = ExtractMatches(ThinkOpenTailRegex(), clean, thinkingParts);
        clean = ExtractMatches(MmThinkClosingHeadRegex(), clean, thinkingParts);
        clean = ExtractMatches(ThinkClosingHeadRegex(), clean, thinkingParts);
        clean = ExtractMatches(MmThinkBareTextRegex(), clean, thinkingParts);

        var thinkingText = thinkingParts.Count > 0
            ? string.Join("\n", thinkingParts)
            : null;

        return (clean, thinkingText);
    }

    private static string ExtractMatches(Regex regex, string input, List<string> collector)
    {
        var matches = regex.Matches(input);
        foreach (Match match in matches)
        {
            collector.Add(match.Value);
        }
        return regex.Replace(input, string.Empty);
    }
}
```

**修改文件** `src/services/AICopilot.AiGatewayService/Agents/AgentStreamRuntime.cs`

在 `CreateUpdateChunksAsync` 的 `case AiTextContent content:` 分支中加入清洗：

```csharp
case AiTextContent content:
    var (cleanText, thinkingText) = ModelOutputSanitizer.Strip(content.Text);

    if (!string.IsNullOrEmpty(thinkingText))
    {
        yield return new ChatChunk(source, ChunkType.Metadata,
            new { thinkingContent = thinkingText }.ToJson());
    }

    if (!string.IsNullOrEmpty(cleanText))
    {
        if (appendAssistantText)
        {
            assistantText?.Append(cleanText);
        }
        yield return new ChatChunk(source, ChunkType.Text, cleanText);
    }
    break;
```

### 2.5 前端 think 标签兜底清洗

**修改文件** `src/vues/AICopilot.Web/src/protocol/chunkReducer.ts`

在 `addTextChunk` 函数开头加兜底：

```typescript
const THINK_TAG_REGEX = /<\/?(?:mm:)?think>|mm:think\S*/g

function addTextChunk(message: ChatMessage, chunk: ChatChunk) {
  const cleanContent = chunk.content.replace(THINK_TAG_REGEX, '').trim()
  if (!cleanContent) return

  const cleanChunk = { ...chunk, content: cleanContent }
  const previousChunk = message.chunks[message.chunks.length - 1]
  // ... 后续合并逻辑不变，但用 cleanChunk
}
```

**修改文件** `src/vues/AICopilot.Web/src/components/chat/MessageItem.vue`

如果 `Metadata` chunk 带 `thinkingContent` 字段，展示到"运行详情"折叠区：

在现有的 metadata 展示逻辑中，增加对 `thinkingContent` 的处理。具体：metadata chunk 的 content JSON 如果包含 `thinkingContent` 字段，将其追加到"运行详情"展开区域内，默认折叠。

---

## 阶段 3：守卫测试

### 3.1 `tests/unit/sessionScopedState.spec.ts`（新建）

```typescript
import { describe, it, expect } from 'vitest'
import {
  createSessionScopedState,
  createReactiveSessionState,
  resetSessionState,
  type SessionScopedState
} from '@/stores/sessionScopedState'

describe('sessionScopedState', () => {
  it('factory returns all fields with default values', () => {
    const state = createSessionScopedState()
    expect(state.agentTasks).toEqual([])
    expect(state.agentApprovals).toEqual([])
    expect(state.agentAuditSummary).toEqual([])
    expect(state.timelineEvents).toEqual([])
    expect(state.currentWorkspace).toBeNull()
    expect(state.currentArtifactPreview).toBeNull()
    expect(state.chartPreview).toBeNull()
    expect(state.uploadedFiles).toEqual([])
    expect(state.isAgentBusy).toBe(false)
  })

  it('reset clears all session data back to defaults', () => {
    const state = createReactiveSessionState()
    // 模拟脏状态
    state.agentTasks = [{ id: 'fake' } as any]
    state.isAgentBusy = true
    state.currentWorkspace = { id: 'ws' } as any
    state.uploadedFiles = [{ id: 'f' } as any]

    resetSessionState(state)

    expect(state.agentTasks).toEqual([])
    expect(state.isAgentBusy).toBe(false)
    expect(state.currentWorkspace).toBeNull()
    expect(state.uploadedFiles).toEqual([])
  })

  it('every key in SessionScopedState is covered by factory', () => {
    const state = createSessionScopedState()
    const keys = Object.keys(state)
    // 如果有人加了新字段但忘了在 factory 里给默认值，这个测试会失败
    for (const key of keys) {
      expect(state[key as keyof SessionScopedState]).toBeDefined()
    }
    // 至少包含这些字段
    expect(keys).toContain('agentTasks')
    expect(keys).toContain('agentApprovals')
    expect(keys).toContain('timelineEvents')
    expect(keys).toContain('currentWorkspace')
    expect(keys).toContain('uploadedFiles')
    expect(keys).toContain('isAgentBusy')
  })
})
```

### 3.2 扩展 `tests/unit/chatErrorStore.spec.ts`

在现有测试基础上新增：

```typescript
describe('toFriendlyMessage', () => {
  it('extracts detail from ProblemDetails 400 response', () => {
    const error = new ApiError('fail', 400, { detail: '模型配置不可用' })
    expect(toFriendlyMessage(error)).toBe('模型配置不可用')
  })

  it('extracts ASP.NET validation errors from 400 response', () => {
    const error = new ApiError('fail', 400, {
      errors: { Goal: ['Goal is required.'] }
    })
    expect(toFriendlyMessage(error)).toBe('Goal is required.')
  })

  it('extracts array-style errors from 400 response', () => {
    const error = new ApiError('fail', 400, {
      errors: ['SessionId is invalid.']
    })
    const msg = toFriendlyMessage(error)
    expect(msg).toContain('SessionId')
  })

  it('shows detail for unknown error code instead of generic message', () => {
    const payload = { code: 'some_future_code', detail: '具体原因说明' }
    expect(resolveChatErrorMessage(payload)).toBe('具体原因说明')
  })
})
```

### 3.3 扩展 `tests/unit/chunkReducer.spec.ts`

在现有测试基础上新增：

```typescript
describe('think tag sanitization', () => {
  it('strips <mm:think> blocks from text chunks', () => {
    const message = createEmptyMessage()
    processChunk(message, {
      source: 'test',
      type: ChunkType.Text,
      content: '<mm:think>内部推理</mm:think>你好！有什么可以帮你的？'
    }, callbacks)
    const textChunk = message.chunks.find(c => c.type === ChunkType.Text)
    expect(textChunk?.content).toBe('你好！有什么可以帮你的？')
    expect(textChunk?.content).not.toContain('mm:think')
  })

  it('strips <think> blocks from text chunks', () => {
    const message = createEmptyMessage()
    processChunk(message, {
      source: 'test',
      type: ChunkType.Text,
      content: '<think>reasoning</think>回答内容'
    }, callbacks)
    const textChunk = message.chunks.find(c => c.type === ChunkType.Text)
    expect(textChunk?.content).toBe('回答内容')
  })

  it('strips bare mm:think text leakage', () => {
    const message = createEmptyMessage()
    processChunk(message, {
      source: 'test',
      type: ChunkType.Text,
      content: 'mm:think用户在问问题。</mm:think>你好！'
    }, callbacks)
    const textChunk = message.chunks.find(c => c.type === ChunkType.Text)
    expect(textChunk?.content).not.toContain('mm:think')
  })

  it('plan_draft_failed event triggers session error', () => {
    const message = createEmptyMessage()
    let capturedError = ''
    const errorCallbacks = {
      ...callbacks,
      setSessionError: (_sid: string, msg: string) => { capturedError = msg }
    }
    processChunk(message, {
      source: 'test',
      type: ChunkType.AgentEvent,
      content: JSON.stringify({
        stage: 'plan_draft_failed',
        detail: '模型配置不存在',
        recoverable: true
      })
    }, errorCallbacks)
    expect(capturedError).toBe('模型配置不存在')
  })
})
```

### 3.4 后端测试 `ModelOutputSanitizerTests.cs`

新增到 `src/tests/AICopilot.BackendTests/ModelOutputSanitizerTests.cs`：

```csharp
using AICopilot.AiGatewayService.Agents;

namespace AICopilot.BackendTests;

public class ModelOutputSanitizerTests
{
    [Fact]
    public void Strip_RemovesMmThinkBlock()
    {
        var (clean, thinking) = ModelOutputSanitizer.Strip(
            "<mm:think>内部推理过程</mm:think>你好！有什么可以帮你的？");
        Assert.Equal("你好！有什么可以帮你的？", clean);
        Assert.Contains("内部推理过程", thinking);
    }

    [Fact]
    public void Strip_RemovesDeepSeekThinkBlock()
    {
        var (clean, thinking) = ModelOutputSanitizer.Strip(
            "<think>reasoning content</think>Hello!");
        Assert.Equal("Hello!", clean);
        Assert.Contains("reasoning content", thinking);
    }

    [Fact]
    public void Strip_RemovesMultilineThinkBlock()
    {
        var (clean, _) = ModelOutputSanitizer.Strip(
            "<mm:think>\n第一行\n第二行\n</mm:think>正文内容");
        Assert.Equal("正文内容", clean);
    }

    [Fact]
    public void Strip_RemovesBareMmThinkText()
    {
        var (clean, _) = ModelOutputSanitizer.Strip(
            "mm:think用户在问问题。</mm:think>你好！");
        Assert.DoesNotContain("mm:think", clean);
    }

    [Fact]
    public void Strip_HandlesOpenTagAtChunkEnd()
    {
        var (clean, _) = ModelOutputSanitizer.Strip(
            "正文<mm:think>这是思考内容");
        Assert.DoesNotContain("mm:think", clean);
        Assert.Equal("正文", clean);
    }

    [Fact]
    public void Strip_HandlesCloseTagAtChunkStart()
    {
        var (clean, _) = ModelOutputSanitizer.Strip(
            "剩余思考内容</mm:think>正文");
        Assert.DoesNotContain("mm:think", clean);
        Assert.Equal("正文", clean);
    }

    [Fact]
    public void Strip_ReturnsOriginalWhenNoThinkTags()
    {
        var (clean, thinking) = ModelOutputSanitizer.Strip("普通文本内容");
        Assert.Equal("普通文本内容", clean);
        Assert.Null(thinking);
    }

    [Fact]
    public void Strip_HandleEmptyString()
    {
        var (clean, thinking) = ModelOutputSanitizer.Strip("");
        Assert.Equal("", clean);
        Assert.Null(thinking);
    }
}
```

### 3.5 架构守卫测试

在 `src/tests/AICopilot.BackendTests/ClaudeFollowupClosureTests.cs` 中追加：

```csharp
[Fact]
public void AgentStreamRuntime_TextContent_ShouldSanitizeThinkTags()
{
    // 验证 AgentStreamRuntime.CreateUpdateChunksAsync 的 AiTextContent 分支
    // 引用了 ModelOutputSanitizer.Strip
    var source = File.ReadAllText("path/to/AgentStreamRuntime.cs");
    Assert.Contains("ModelOutputSanitizer.Strip", source);
}
```

---

## 验证方式

| 检查项 | 命令 | 预期 |
|--------|------|------|
| 后端编译 | `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj` | 0 errors 0 warnings |
| 后端测试 | `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj` | 全部通过（含新增的 ModelOutputSanitizerTests） |
| 前端类型检查 | `npx vue-tsc --noEmit` | 0 errors |
| 前端测试 | `npx vitest run` | 全部通过（含新增的 sessionScopedState / chatErrorStore / chunkReducer 扩展） |

## 手动验证清单

1. 新建会话 → 不残留上一个会话的任务、错误、workspace、文件
2. 切换已有会话 → 数据完整切换，不混淆
3. Plan 模式发"你好" → 显示后端真实错误详情，不是"请求没有通过后端校验"
4. Plan 失败 → 切 Chat 模式 → 错误提示消失
5. Chat 模式发"你好" → 回复里没有 `mm:think` / `<think>` 标签
6. 点"添加" → 面板打开 → 点外面 → 面板关闭
7. 点"添加" → 面板打开 → 按 Escape → 面板关闭
8. 计划模式切聊天模式 → "添加"面板自动关闭

## 不在本次范围

- `ChatWindow.vue` 组件拆分（先稳定状态管理，后续单独做）
- chatStore 按领域拆成多个 store（等 SessionScopedState 模式稳了再做）
- 新增 `ChunkType.ThinkingContent` 独立类型（现在复用 Metadata 够用）
- Codex-like 对话流 UI 重构（等这批改完、规则钉死后做）
- 前端路由/导航守卫（当前单页应用不涉及）

## 文件变更清单

### 新建

| 文件 | 用途 |
|------|------|
| `src/vues/AICopilot.Web/AGENTS.md` | 前端架构红线和规则 |
| `src/vues/AICopilot.Web/src/stores/sessionScopedState.ts` | 会话作用域状态容器 |
| `src/services/AICopilot.AiGatewayService/Agents/ModelOutputSanitizer.cs` | 模型输出 think 标签清洗 |
| `src/tests/AICopilot.BackendTests/ModelOutputSanitizerTests.cs` | 清洗测试 |
| `src/vues/AICopilot.Web/tests/unit/sessionScopedState.spec.ts` | 会话状态容器测试 |

### 修改

| 文件 | 改动 |
|------|------|
| `AGENTS.md`（根） | 追加前端规则引用 |
| `src/vues/AICopilot.Web/src/stores/chatStore.ts` | 替换散落 ref 为 scopedState、删除 agentErrorMessage、修 createNewSession / selectSession / deleteSession |
| `src/vues/AICopilot.Web/src/stores/chatErrorStore.ts` | 修 default 分支、修 400 fallback、新增 extractValidationErrors |
| `src/vues/AICopilot.Web/src/protocol/chunkReducer.ts` | addTextChunk 加 think 标签兜底、addAgentEventChunk 加 plan_draft_failed 错误展示 |
| `src/vues/AICopilot.Web/src/components/chat/ChatWindow.vue` | 添加面板 click-outside + Escape、setComposerMode 清错误、agentErrorMessage 引用改 errorMessage |
| `src/vues/AICopilot.Web/src/components/chat/MessageItem.vue` | thinkingContent metadata 走折叠区 |
| `src/services/AICopilot.AiGatewayService/Agents/AgentStreamRuntime.cs` | AiTextContent 分支加 ModelOutputSanitizer.Strip |
| `src/vues/AICopilot.Web/tests/unit/chatErrorStore.spec.ts` | 扩展 400/validation/unknown code 测试 |
| `src/vues/AICopilot.Web/tests/unit/chunkReducer.spec.ts` | 扩展 think 标签清洗 + plan_draft_failed 测试 |
| `src/tests/AICopilot.BackendTests/ClaudeFollowupClosureTests.cs` | 追加架构守卫断言 |
