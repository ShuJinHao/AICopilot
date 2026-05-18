<script setup lang="ts">
import AiButton from '@/components/ai/AiButton.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTextarea from '@/components/ai/AiTextarea.vue'
import { useRagStore } from '@/stores/ragStore'

const store = useRagStore()

const classificationOptions = [
  { label: '公开', value: 'Public' },
  { label: '内部', value: 'Internal' },
  { label: '敏感', value: 'Sensitive' },
  { label: '禁用', value: 'Forbidden' }
]
const sourceTypeOptions = [
  { label: '用户上传', value: 'UserUploaded' },
  { label: '业务规则', value: 'BusinessRule' },
  { label: 'Cloud 只读文档', value: 'CloudReadOnlyApiDoc' },
  { label: '运维手册', value: 'Runbook' },
  { label: '外部资料', value: 'External' }
]
</script>

<template>
  <AiDrawer v-model="store.dialogStates.documentGovernance" title="文档治理" width="540px">
    <div class="ai-form">
      <label><span>文档</span><AiInput :model-value="store.currentDocumentGovernanceName" disabled /></label>
      <label><span>文档等级</span><AiSelect v-model="store.currentDocumentGovernance.classification" :options="classificationOptions" /></label>
      <label><span>来源类型</span><AiSelect v-model="store.currentDocumentGovernance.sourceType" :options="sourceTypeOptions" /></label>
      <div class="form-row"><span>已脱敏</span><AiSwitch v-model="store.currentDocumentGovernance.isSanitized" /></div>
      <div class="form-row"><span>允许进入回答</span><AiSwitch v-model="store.currentDocumentGovernance.allowedForFinalPrompt" /></div>
      <label><span>生效时间</span><AiInput v-model="store.currentDocumentGovernance.effectiveFrom" placeholder="YYYY-MM-DDTHH:mm:ssZ" /></label>
      <label><span>过期时间</span><AiInput v-model="store.currentDocumentGovernance.effectiveTo" placeholder="YYYY-MM-DDTHH:mm:ssZ" /></label>
      <label><span>阻断原因</span><AiTextarea v-model="store.currentDocumentGovernance.blockedReason" :rows="3" /></label>
      <div v-if="store.actionErrors.document" class="error-note">{{ store.actionErrors.document }}</div>
      <footer>
        <AiButton @click="store.closeDocumentGovernanceDialog()">取消</AiButton>
        <AiButton variant="primary" :disabled="store.submittingStates.documentGovernance" @click="store.saveDocumentGovernance()">
          {{ store.submittingStates.documentGovernance ? '保存中' : '保存' }}
        </AiButton>
      </footer>
    </div>
  </AiDrawer>
</template>

<style scoped>
@import './shared-knowledge.css';
</style>
