<script setup lang="ts">
import { ChevronRight, Sparkles } from 'lucide-vue-next'

const emit = defineEmits<{
  useSuggestion: [text: string]
}>()

const suggestions = [
  '查看 DEV-001 最近 24 小时设备日志，并给出根因线索',
  '查看 DEV-001 最后上报运行状态和心跳时间',
  '列出工序主数据，并说明正式字段边界',
  '列出 stable 通道、win-x64 运行时的已发布客户端版本'
]
</script>

<template>
  <section class="empty-chat">
    <div class="empty-chat-copy">
      <Sparkles :size="24" />
      <div>
        <h2>直接开始对话</h2>
        <p>输入问题后我会先直接回答；需要拆解步骤时，再手动切换到计划模式。</p>
      </div>
    </div>
    <div class="suggestions">
      <button v-for="item in suggestions" :key="item" type="button" @click="emit('useSuggestion', item)">
        {{ item }}
        <ChevronRight :size="17" />
      </button>
    </div>
  </section>
</template>
