<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Fold, Plus, Promotion } from '@element-plus/icons-vue'
import { useChatStore } from '@/stores/chatStore'
import MessageItem from './MessageItem.vue'
import SessionList from './SessionList.vue'

const store = useChatStore()
const inputValue = ref('')
const scrollContainer = ref<HTMLElement | null>(null)
const isMobile = ref(typeof window !== 'undefined' ? window.innerWidth < 960 : false)
const sessionDrawerVisible = ref(false)

const acceptanceQuestionGroups = [
  {
    title: '设备诊断',
    questions: ['3 号叠片机昨晚报警，帮我看原因', 'DEV-001 为什么停机了？']
  },
  {
    title: '参数建议',
    questions: ['根据最近良率，给我一个配方建议', 'DEV-001 当前参数需要怎么调？']
  },
  {
    title: '日志根因',
    questions: ['帮我做一下最近报警的根因关联分析', '查看 DEV-001 最近日志的时间线']
  },
  {
    title: '工艺知识',
    questions: ['配方修改是覆盖还是新增版本？', '员工修改机台参数需要什么权限？']
  }
]

const currentTitle = computed(() => store.currentSession?.title || '智能助手')
const isInputDisabled = computed(() => store.isStreaming || store.isWaitingForApproval)
const inputPlaceholder = computed(() => {
  if (store.isWaitingForApproval) {
    return '请先处理上方的审批请求。'
  }

  if (store.isStreaming) {
    return 'AI 正在分析中...'
  }

  return '输入你的问题，Enter 发送，Shift+Enter 换行'
})

const onsiteStatusText = computed(() => {
  if (!store.currentSession?.onsiteConfirmationExpiresAt) {
    return '未设置'
  }

  const expiresAt = new Date(store.currentSession.onsiteConfirmationExpiresAt)
  if (expiresAt.getTime() <= Date.now()) {
    return '已过期'
  }

  return `有效至 ${expiresAt.toLocaleString('zh-CN', { hour12: false })}`
})

const onsiteStatusType = computed(() => {
  if (!store.currentSession?.onsiteConfirmationExpiresAt) {
    return 'info'
  }

  const expiresAt = new Date(store.currentSession.onsiteConfirmationExpiresAt)
  return expiresAt.getTime() > Date.now() ? 'success' : 'warning'
})

async function handleSend() {
  const content = inputValue.value.trim()
  if (!content || isInputDisabled.value) {
    return
  }

  inputValue.value = ''
  await store.sendMessage(content)
}

async function applySuggestion(question: string) {
  inputValue.value = question
  await handleSend()
}

async function handleConfirmOnsite() {
  await store.confirmOnsitePresence(30)
}

async function handleClearOnsite() {
  await store.clearOnsitePresence()
}

async function handleCreateSession() {
  await store.createNewSession()
}

async function scrollToBottom() {
  await nextTick()
  if (scrollContainer.value) {
    scrollContainer.value.scrollTop = scrollContainer.value.scrollHeight
  }
}

function handleResize() {
  isMobile.value = window.innerWidth < 960
  if (!isMobile.value) {
    sessionDrawerVisible.value = false
  }
}

watch(
  () => store.currentMessages,
  () => {
    void scrollToBottom()
  },
  { deep: true }
)

watch(
  () => store.currentSessionId,
  () => {
    void scrollToBottom()
    sessionDrawerVisible.value = false
  }
)

onMounted(() => {
  window.addEventListener('resize', handleResize)
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', handleResize)
})
</script>

<template>
  <div class="chat-layout">
    <div v-if="!isMobile" class="sidebar-wrapper">
      <SessionList />
    </div>

    <div class="main-wrapper">
      <header class="chat-header">
        <div class="header-title">
          <el-button v-if="isMobile" text @click="sessionDrawerVisible = true">
            <el-icon><Fold /></el-icon>
          </el-button>
          <div>
            <h2>{{ currentTitle }}</h2>
            <p>AI 只做观测、诊断、建议和知识问答，不执行任何控制动作。</p>
          </div>
        </div>

        <div class="header-actions">
          <div class="onsite-status-card">
            <div class="onsite-status-title">人工在场</div>
            <el-tag :type="onsiteStatusType">{{ onsiteStatusText }}</el-tag>
            <div class="onsite-actions">
              <el-button size="small" @click="handleConfirmOnsite">确认在岗 30 分钟</el-button>
              <el-button size="small" text @click="handleClearOnsite">清除声明</el-button>
            </div>
          </div>

          <el-button type="primary" plain @click="handleCreateSession">
            <el-icon><Plus /></el-icon>
            新建会话
          </el-button>
        </div>
      </header>

      <main ref="scrollContainer" class="chat-viewport">
        <div class="messages-list">
          <el-alert
            v-if="store.errorMessage"
            class="message-alert"
            :title="store.errorMessage"
            type="error"
            show-icon
            :closable="false"
          />

          <el-skeleton v-if="store.isLoadingHistory" :rows="4" animated />

          <div v-if="store.currentMessages.length === 0 && !store.isLoadingHistory" class="welcome-banner">
            <h3>制造业 AI 助手</h3>
            <p>优先支持设备异常诊断、参数建议、日志根因关联和工艺知识问答。</p>

            <div class="acceptance-groups">
              <section v-for="group in acceptanceQuestionGroups" :key="group.title" class="acceptance-group">
                <div class="group-title">{{ group.title }}</div>
                <div class="suggestion-chips">
                  <el-tag
                    v-for="question in group.questions"
                    :key="question"
                    class="chip"
                    @click="applySuggestion(question)"
                  >
                    {{ question }}
                  </el-tag>
                </div>
              </section>
            </div>
          </div>

          <MessageItem
            v-for="message in store.currentMessages"
            :key="message.timestamp"
            :message="message"
          />
        </div>
      </main>

      <footer class="chat-input-area">
        <div class="input-container">
          <el-input
            v-model="inputValue"
            type="textarea"
            :autosize="{ minRows: 1, maxRows: 4 }"
            :placeholder="inputPlaceholder"
            :disabled="isInputDisabled"
            @keydown.enter.prevent="(event: KeyboardEvent) => { if (!event.shiftKey) handleSend() }"
          />
          <el-button
            type="primary"
            class="send-btn"
            :disabled="isInputDisabled || !inputValue.trim()"
            @click="handleSend"
          >
            <el-icon><Promotion /></el-icon>
          </el-button>
        </div>
        <div class="footer-tip">
          AI 生成内容可能不完全准确，涉及现场操作前请先人工复核。
        </div>
      </footer>
    </div>

    <el-drawer v-model="sessionDrawerVisible" :size="300" direction="ltr" title="会话列表">
      <SessionList />
    </el-drawer>
  </div>
</template>

<style scoped>
.chat-layout {
  display: flex;
  height: calc(100vh - 116px);
  width: 100%;
  overflow: hidden;
  border-radius: 26px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  background: rgba(255, 255, 255, 0.9);
  box-shadow: 0 16px 48px rgba(15, 23, 42, 0.08);
}

.sidebar-wrapper {
  width: 260px;
  flex-shrink: 0;
}

.main-wrapper {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  background-color: var(--bg-color-primary);
}

.chat-header {
  min-height: 84px;
  border-bottom: 1px solid var(--border-color);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  padding: 16px 24px;
}

.header-title {
  display: flex;
  align-items: center;
  gap: 12px;
}

.header-title h2 {
  margin: 0;
  font-weight: 700;
}

.header-title p {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--text-secondary);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.onsite-status-card {
  display: grid;
  gap: 6px;
  padding: 10px 12px;
  border-radius: 14px;
  background: rgba(248, 250, 252, 0.9);
  border: 1px solid rgba(148, 163, 184, 0.18);
}

.onsite-status-title {
  font-size: 12px;
  font-weight: 700;
  color: #334155;
}

.onsite-actions {
  display: flex;
  gap: 8px;
}

.chat-viewport {
  flex: 1;
  overflow-y: auto;
  padding: 24px;
  scroll-behavior: smooth;
}

.messages-list {
  max-width: 860px;
  margin: 0 auto;
  display: grid;
  gap: 16px;
}

.message-alert {
  margin-bottom: 6px;
}

.welcome-banner {
  display: grid;
  gap: 16px;
  padding: 20px 22px;
  border-radius: 24px;
  background: linear-gradient(135deg, rgba(15, 118, 110, 0.08), rgba(37, 99, 235, 0.08));
  border: 1px solid rgba(148, 163, 184, 0.18);
}

.welcome-banner h3 {
  margin: 0;
  font-size: 22px;
  color: #0f172a;
}

.welcome-banner p {
  margin: 0;
  color: #475569;
}

.acceptance-groups {
  display: grid;
  gap: 12px;
}

.acceptance-group {
  display: grid;
  gap: 8px;
}

.group-title {
  font-size: 13px;
  font-weight: 700;
  color: #0f172a;
}

.suggestion-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.chip {
  cursor: pointer;
}

.chat-input-area {
  padding: 24px;
  border-top: 1px solid var(--border-color);
  background-color: #fff;
}

.input-container {
  max-width: 860px;
  margin: 0 auto;
  display: flex;
  gap: 12px;
  align-items: flex-end;
}

.send-btn {
  height: 40px;
  width: 40px;
  border-radius: 8px;
}

.footer-tip {
  max-width: 860px;
  margin: 10px auto 0;
  color: var(--text-secondary);
  font-size: 12px;
}

@media (max-width: 960px) {
  .chat-layout {
    height: calc(100vh - 88px);
    border-radius: 18px;
  }

  .chat-header,
  .chat-viewport,
  .chat-input-area {
    padding: 16px;
  }

  .chat-header,
  .header-actions,
  .onsite-actions {
    flex-direction: column;
    align-items: stretch;
  }
}
</style>
