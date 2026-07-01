<script setup lang="ts">
import { computed } from 'vue'
import {
  CircleCheck,
  Crosshair,
  Lightbulb,
  ListChecks,
  SearchCheck,
  ShieldAlert,
  Wrench
} from 'lucide-vue-next'
import type {
  DeviceLogAnswerSection,
  DeviceLogAnswerSectionKey,
  DeviceLogAnswerView
} from '@/protocol/deviceLogAnswerSections'
import { renderMarkdown } from '@/utils/markdown'

const props = defineProps<{
  answer: DeviceLogAnswerView
}>()

function section(key: DeviceLogAnswerSectionKey) {
  return props.answer.sections.find((item) => item.key === key) ?? null
}

const conclusion = computed(() => section('conclusion'))
const metrics = computed(() => section('metrics'))
const records = computed(() => section('records'))
const explanation = computed(() => section('explanation'))
const rootCause = computed(() => section('rootCause'))
const actions = computed(() => section('actions'))
const blockedActions = computed(() => section('blockedActions'))
const scope = computed(() => section('scope'))

function html(section: DeviceLogAnswerSection) {
  return renderMarkdown(section.content)
}
</script>

<template>
  <article class="device-log-answer">
    <section v-if="conclusion" class="answer-conclusion">
      <span class="section-kicker">
        <CircleCheck :size="15" />
        {{ conclusion.title }}
      </span>
      <div class="section-content conclusion-content" v-html="html(conclusion)" />
    </section>

    <div v-if="metrics || records" class="answer-grid primary-grid">
      <section v-if="metrics" class="answer-section metric-section">
        <span class="section-title">
          <ListChecks :size="15" />
          {{ metrics.title }}
        </span>
        <div class="section-content compact-content" v-html="html(metrics)" />
      </section>

      <section v-if="records" class="answer-section record-section">
        <span class="section-title">
          <SearchCheck :size="15" />
          {{ records.title }}
        </span>
        <div class="section-content compact-content" v-html="html(records)" />
      </section>
    </div>

    <div v-if="explanation || rootCause || actions" class="answer-grid analysis-grid">
      <section v-if="explanation" class="answer-section quiet-section">
        <span class="section-title">
          <Crosshair :size="15" />
          {{ explanation.title }}
        </span>
        <div class="section-content" v-html="html(explanation)" />
      </section>

      <section v-if="rootCause" class="answer-section quiet-section">
        <span class="section-title">
          <Lightbulb :size="15" />
          {{ rootCause.title }}
        </span>
        <div class="section-content" v-html="html(rootCause)" />
      </section>

      <section v-if="actions" class="answer-section quiet-section">
        <span class="section-title">
          <Wrench :size="15" />
          {{ actions.title }}
        </span>
        <div class="section-content" v-html="html(actions)" />
      </section>
    </div>

    <section v-if="blockedActions" class="boundary-section">
      <span class="section-title">
        <ShieldAlert :size="15" />
        {{ blockedActions.title }}
      </span>
      <div class="section-content boundary-content" v-html="html(blockedActions)" />
    </section>

    <section v-if="scope" class="scope-section">
      <span>{{ scope.title }}</span>
      <div class="scope-content" v-html="html(scope)" />
    </section>
  </article>
</template>

<style scoped>
.device-log-answer {
  display: grid;
  gap: 10px;
  min-width: 0;
}

.answer-conclusion,
.answer-section,
.boundary-section,
.scope-section {
  min-width: 0;
  border: 1px solid var(--ai-border);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.78);
}

.answer-conclusion {
  display: grid;
  gap: 8px;
  border-color: rgba(20, 184, 166, 0.2);
  padding: 12px 14px;
  background: linear-gradient(180deg, rgba(236, 254, 255, 0.88), rgba(255, 255, 255, 0.82));
}

.section-kicker,
.section-title {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 900;
  line-height: 1.3;
}

.section-kicker {
  color: #0f766e;
}

.answer-grid {
  display: grid;
  gap: 10px;
  min-width: 0;
}

.primary-grid {
  grid-template-columns: minmax(0, 0.42fr) minmax(0, 0.58fr);
}

.analysis-grid {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.answer-section {
  display: grid;
  align-content: start;
  gap: 8px;
  padding: 11px 12px;
}

.metric-section {
  border-color: rgba(200, 255, 61, 0.34);
  background: rgba(250, 255, 237, 0.76);
}

.record-section {
  border-color: rgba(147, 197, 253, 0.34);
}

.quiet-section {
  background: rgba(255, 255, 255, 0.62);
}

.boundary-section {
  display: grid;
  gap: 7px;
  border-style: dashed;
  padding: 10px 12px;
  background: rgba(248, 250, 252, 0.64);
}

.boundary-section .section-title {
  color: #92400e;
}

.scope-section {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 8px;
  align-items: start;
  padding: 8px 10px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 820;
}

.scope-section > span {
  color: var(--ai-text);
  font-weight: 930;
}

.section-content,
.scope-content {
  min-width: 0;
  color: var(--ai-text);
  overflow-wrap: anywhere;
  font-size: 13px;
  line-height: 1.58;
}

.conclusion-content {
  font-size: 14px;
  font-weight: 760;
  line-height: 1.62;
}

.compact-content {
  font-size: 12.5px;
  line-height: 1.52;
}

.boundary-content,
.scope-content {
  color: var(--ai-text-muted);
}

:deep(p) {
  margin: 0 0 7px;
}

:deep(p:last-child) {
  margin-bottom: 0;
}

:deep(ol),
:deep(ul) {
  display: grid;
  gap: 5px;
  margin: 0;
  padding-left: 18px;
}

:deep(li) {
  padding-left: 2px;
}

@media (max-width: 840px) {
  .primary-grid,
  .analysis-grid,
  .scope-section {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
