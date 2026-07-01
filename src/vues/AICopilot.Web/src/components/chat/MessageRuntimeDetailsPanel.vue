<script setup lang="ts">
import { computed } from 'vue'
import { Activity, BarChart3, Brain, Cpu, Gauge, Wrench } from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import type { ChatMessage } from '@/types/models'
import type { ChatRunStatus } from '@/stores/sessionScopedState'
import { buildRuntimeDetails } from '@/protocol/runtimeDetails'

const props = defineProps<{
  message: ChatMessage
  status?: ChatRunStatus | null
}>()

const details = computed(() => buildRuntimeDetails(props.message, props.status ?? null))
</script>

<template>
  <details v-if="details.count > 0" class="runtime-details">
    <summary>
      <Activity :size="16" />
      <span>运行详情</span>
      <AiTag tone="neutral">{{ details.count }} 项</AiTag>
    </summary>

    <div class="runtime-body">
      <section v-if="details.status" class="runtime-section">
        <div class="runtime-label">
          <Gauge :size="15" />
          <span>状态</span>
        </div>
        <div class="status-line">
          <AiTag :tone="details.status.tone">{{ details.status.phaseLabel }}</AiTag>
          <AiTag tone="neutral">{{ details.status.elapsedText }}</AiTag>
          <span>{{ details.status.summary }}</span>
        </div>
        <div v-if="details.status.facts.length > 0" class="runtime-tags">
          <AiTag v-for="fact in details.status.facts" :key="fact.key" :tone="fact.tone">
            {{ fact.text }}
          </AiTag>
        </div>
      </section>

      <section v-if="details.modelBadges.length > 0" class="runtime-section">
        <div class="runtime-label">
          <Cpu :size="15" />
          <span>模型</span>
        </div>
        <div class="runtime-tags">
          <AiTag v-for="badge in details.modelBadges" :key="badge.key" :tone="badge.tone">
            {{ badge.text }}
          </AiTag>
        </div>
      </section>

      <section v-if="details.events.length > 0" class="runtime-section">
        <div class="runtime-label">
          <Activity :size="15" />
          <span>过程</span>
        </div>
        <div class="runtime-list">
          <div v-for="event in details.events" :key="event.key" class="runtime-row two-line">
            <strong>{{ event.label }}</strong>
            <span>{{ event.detail }}</span>
            <AiTag :tone="event.tone">{{ event.statusText }}</AiTag>
          </div>
        </div>
      </section>

      <section v-if="details.intents.length > 0" class="runtime-section">
        <div class="runtime-label">
          <Brain :size="15" />
          <span>意图</span>
        </div>
        <div class="runtime-tags">
          <AiTag v-for="intent in details.intents" :key="intent.key" tone="neutral">
            {{ intent.name }} · {{ intent.confidenceText }}
          </AiTag>
        </div>
      </section>

      <section v-if="details.tools.length > 0" class="runtime-section">
        <div class="runtime-label">
          <Wrench :size="15" />
          <span>工具</span>
        </div>
        <div class="runtime-list">
          <div v-for="tool in details.tools" :key="tool.key" class="runtime-row tool-row">
            <div class="tool-title">
              <strong>{{ tool.name }}</strong>
              <AiTag :tone="tool.tone">{{ tool.statusText }}</AiTag>
            </div>
            <span>{{ tool.argsSummary }}</span>
            <span>{{ tool.resultSummary }}</span>
          </div>
        </div>
      </section>

      <section v-if="details.widgets.length > 0" class="runtime-section">
        <div class="runtime-label">
          <BarChart3 :size="15" />
          <span>结构化展示</span>
        </div>
        <div class="runtime-list">
          <div v-for="widget in details.widgets" :key="widget.key" class="runtime-row widget-row">
            <AiTag tone="blue">{{ widget.typeLabel }}</AiTag>
            <strong>{{ widget.title }}</strong>
            <span>{{ widget.summary }}</span>
          </div>
        </div>
      </section>
    </div>
  </details>
</template>

<style scoped>
.runtime-details {
  overflow: hidden;
  border: 1px solid var(--ai-border);
  border-radius: 12px;
  background: var(--ai-surface-soft);
}

.runtime-details summary {
  display: flex;
  min-height: 38px;
  cursor: pointer;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 900;
}

.runtime-body {
  display: grid;
  gap: 12px;
  border-top: 1px solid var(--ai-border);
  padding: 12px;
}

.runtime-section {
  display: grid;
  gap: 8px;
}

.runtime-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 900;
}

.runtime-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.status-line {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 760;
  line-height: 1.5;
}

.status-line span:last-child {
  min-width: 0;
  overflow-wrap: anywhere;
}

.runtime-list {
  display: grid;
  gap: 8px;
}

.runtime-row {
  min-width: 0;
  border: 1px solid rgba(63, 111, 115, 0.12);
  border-radius: 8px;
  padding: 8px 9px;
  background: rgba(255, 255, 255, 0.62);
}

.runtime-row strong,
.runtime-row span {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 12px;
  line-height: 1.45;
}

.runtime-row strong {
  color: var(--ai-text);
  font-weight: 900;
}

.runtime-row span {
  color: var(--ai-text-muted);
  font-weight: 750;
}

.two-line {
  display: grid;
  grid-template-columns: minmax(96px, 0.28fr) minmax(0, 1fr) auto;
  gap: 8px;
  align-items: center;
}

.tool-row {
  display: grid;
  gap: 7px;
}

.tool-title {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.widget-row {
  display: grid;
  grid-template-columns: auto minmax(96px, 0.28fr) minmax(0, 1fr);
  gap: 8px;
  align-items: center;
}

@media (max-width: 640px) {
  .two-line,
  .widget-row {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
