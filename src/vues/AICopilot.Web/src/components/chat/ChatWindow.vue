<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Connection, Fold, Promotion, Refresh, Warning } from '@element-plus/icons-vue'
import { useChatStore } from '@/stores/chatStore'
import MessageItem from './MessageItem.vue'
import SessionList from './SessionList.vue'

const store = useChatStore()
const inputValue = ref('')
const scrollContainer = ref<HTMLElement | null>(null)
const isMobile = ref(typeof window !== 'undefined' ? window.innerWidth < 980 : false)
const sessionDrawerVisible = ref(false)

const currentTitle = computed(() => store.currentSession?.title || '新会话')
const isInputDisabled = computed(() => store.isStreaming || store.isWaitingForApproval)
const approvalCount = computed(() =>
  store.currentMessages.flatMap((message) => message.chunks).filter((chunk) => chunk.type === 'ApprovalRequest').length
)
const widgetCount = computed(() =>
  store.currentMessages.flatMap((message) => message.chunks).filter((chunk) => chunk.type === 'Widget').length
)
const onsiteStatus = computed(() => {
  const expires = store.currentSession?.onsiteConfirmationExpiresAt
  if (!expires) return { label: '未确认', type: 'info' as const }
  return new Date(expires).getTime() > Date.now()
    ? { label: `有效至 ${new Date(expires).toLocaleTimeString('zh-CN', { hour12: false })}`, type: 'success' as const }
    : { label: '已过期', type: 'warning' as const }
})

const suggestions = [
  '查看 DEV-001 最近 24 小时设备日志，并给出根因线索',
  '列出 LINE-A 当前设备状态，生成关键指标和记录摘要',
  '查询 DEV-001 配方版本历史，只做只读分析',
  '根据最近产能数据说明异常波动，不执行任何控制动作'
]

async function sendMessage() {
  const content = inputValue.value.trim()
  if (!content || isInputDisabled.value) return
  inputValue.value = ''
  await store.sendMessage(content)
}

async function useSuggestion(text: string) {
  inputValue.value = text
  await sendMessage()
}

async function scrollToBottom() {
  await nextTick()
  if (scrollContainer.value) {
    scrollContainer.value.scrollTop = scrollContainer.value.scrollHeight
  }
}

function handleResize() {
  isMobile.value = window.innerWidth < 980
  if (!isMobile.value) sessionDrawerVisible.value = false
}

watch(() => store.currentMessages, () => void scrollToBottom(), { deep: true })
watch(() => store.currentSessionId, () => {
  sessionDrawerVisible.value = false
  void scrollToBottom()
})

onMounted(() => window.addEventListener('resize', handleResize))
onBeforeUnmount(() => window.removeEventListener('resize', handleResize))
</script>

<template>
  <div class="workspace">
    <SessionList v-if="!isMobile" class="sessions" />

    <section class="chat-main">
      <header class="chat-header">
        <div class="title-zone">
          <el-button v-if="isMobile" text :icon="Fold" @click="sessionDrawerVisible = true" />
          <div>
            <p class="page-kicker">Chat Workspace</p>
            <h1>{{ currentTitle }}</h1>
          </div>
        </div>
        <div class="toolbar">
          <el-tag :type="store.isStreaming ? 'warning' : 'success'">
            {{ store.isStreaming ? '生成中' : '就绪' }}
          </el-tag>
          <el-button :icon="Refresh" @click="store.currentSessionId && store.selectSession(store.currentSessionId, true)">
            刷新
          </el-button>
        </div>
      </header>

      <div ref="scrollContainer" class="message-viewport">
        <el-alert
          v-if="store.errorMessage"
          :title="store.errorMessage"
          type="error"
          show-icon
          :closable="false"
        />

        <el-skeleton v-if="store.isLoadingHistory" :rows="5" animated />

        <section v-if="store.currentMessages.length === 0 && !store.isLoadingHistory" class="empty-chat">
          <h2>开始一次只读分析</h2>
          <p>选择一个问题模板，或直接输入设备、日志、配方、产能或知识库问题。</p>
          <div class="suggestions">
            <button v-for="item in suggestions" :key="item" type="button" @click="useSuggestion(item)">
              {{ item }}
            </button>
          </div>
        </section>

        <div class="message-list">
          <MessageItem v-for="message in store.currentMessages" :key="message.timestamp" :message="message" />
        </div>
      </div>

      <footer class="composer">
        <el-input
          v-model="inputValue"
          type="textarea"
          :autosize="{ minRows: 1, maxRows: 5 }"
          :disabled="isInputDisabled"
          :placeholder="store.isWaitingForApproval ? '请先处理待审批请求' : '输入问题，Enter 发送，Shift + Enter 换行'"
          @keydown.enter.prevent="(event: KeyboardEvent) => { if (!event.shiftKey) sendMessage() }"
        />
        <el-button
          type="primary"
          :icon="Promotion"
          :disabled="isInputDisabled || !inputValue.trim()"
          @click="sendMessage"
        />
      </footer>
    </section>

    <aside class="context-panel">
      <section class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">运行边界</h2>
            <p class="panel-subtitle">本会话只读分析状态</p>
          </div>
          <el-icon><Warning /></el-icon>
        </div>
        <div class="panel-body boundary-list">
          <div>
            <span>现场确认</span>
            <el-tag :type="onsiteStatus.type">{{ onsiteStatus.label }}</el-tag>
          </div>
          <div>
            <span>审批请求</span>
            <strong>{{ approvalCount }}</strong>
          </div>
          <div>
            <span>图表组件</span>
            <strong>{{ widgetCount }}</strong>
          </div>
          <el-button type="primary" plain :icon="Connection" @click="store.confirmOnsitePresence(30)">
            确认在岗 30 分钟
          </el-button>
          <el-button @click="store.clearOnsitePresence()">清除在岗声明</el-button>
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">回答原则</h2>
            <p class="panel-subtitle">不会自动控制设备或写业务数据</p>
          </div>
        </div>
        <div class="panel-body rule-list">
          <span>外部资料只作为事实证据</span>
          <span>审批不能由 prompt 绕过</span>
          <span>SQL、库表和连接信息不进入最终回答</span>
          <span>现场动作必须人工执行</span>
        </div>
      </section>
    </aside>

    <el-drawer v-model="sessionDrawerVisible" size="310px" direction="ltr" title="会话">
      <SessionList />
    </el-drawer>
  </div>
</template>

<style scoped>
.workspace {
  display: grid;
  grid-template-columns: 280px minmax(0, 1fr) 300px;
  height: 100%;
  min-height: 0;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  background: var(--app-surface);
  overflow: hidden;
}

.chat-main {
  display: flex;
  min-width: 0;
  min-height: 0;
  flex-direction: column;
}

.chat-header {
  display: flex;
  min-height: 76px;
  flex-shrink: 0;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  border-bottom: 1px solid var(--app-border);
  padding: 14px 16px;
}

.title-zone {
  display: flex;
  align-items: center;
  gap: 8px;
}

.chat-header h1 {
  margin: 0;
  font-size: 20px;
  font-weight: 800;
}

.message-viewport {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 18px;
  background: #f8fafc;
}

.message-list {
  display: grid;
  gap: 14px;
  max-width: 940px;
  margin: 0 auto;
}

.empty-chat {
  display: grid;
  gap: 12px;
  max-width: 760px;
  margin: 42px auto;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  padding: 20px;
  background: var(--app-surface);
}

.empty-chat h2 {
  margin: 0;
  font-size: 22px;
  font-weight: 800;
}

.empty-chat p {
  margin: 0;
  color: var(--app-text-muted);
}

.suggestions {
  display: grid;
  gap: 8px;
}

.suggestions button {
  border: 1px solid var(--app-border);
  border-radius: 8px;
  padding: 10px 12px;
  background: var(--app-surface-muted);
  color: var(--app-text);
  cursor: pointer;
  text-align: left;
}

.suggestions button:hover {
  border-color: var(--app-primary);
  color: var(--app-primary-strong);
}

.composer {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 44px;
  gap: 10px;
  flex-shrink: 0;
  border-top: 1px solid var(--app-border);
  padding: 14px;
  background: var(--app-surface);
}

.context-panel {
  display: grid;
  align-content: start;
  gap: 12px;
  min-width: 0;
  overflow-y: auto;
  border-left: 1px solid var(--app-border);
  background: var(--app-surface-muted);
  padding: 12px;
}

.boundary-list,
.rule-list {
  display: grid;
  gap: 10px;
}

.boundary-list > div {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.boundary-list span,
.rule-list span {
  color: var(--app-text-muted);
}

.boundary-list strong {
  font-size: 20px;
}

.rule-list span {
  border-left: 3px solid var(--app-primary);
  padding-left: 8px;
}

@media (max-width: 1180px) {
  .workspace {
    grid-template-columns: 260px minmax(0, 1fr);
  }

  .context-panel {
    display: none;
  }
}

@media (max-width: 980px) {
  .workspace {
    grid-template-columns: 1fr;
    height: auto;
    min-height: calc(100vh - 170px);
  }

  .sessions {
    display: none;
  }

  .message-viewport {
    min-height: 55vh;
  }
}
</style>
