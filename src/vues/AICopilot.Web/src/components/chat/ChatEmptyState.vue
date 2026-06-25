<script setup lang="ts">
import { ChevronRight, Sparkles } from 'lucide-vue-next'

const emit = defineEmits<{
  useSuggestion: [text: string]
}>()

const suggestions = [
  '查看 DEV-001 最近 24 小时设备日志，并给出根因线索',
  '列出 LINE-A 当前设备状态，生成关键指标和记录摘要',
  '查询 DEV-001 配方版本历史，只做只读分析',
  '根据最近产能数据说明异常波动，不执行任何控制动作'
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
