<script setup lang="ts">
import { useRagStore } from '@/stores/ragStore'

const store = useRagStore()
</script>

<template>
  <el-drawer v-model="store.dialogStates.documentGovernance" size="520px" title="文档治理">
    <el-form label-position="top">
      <el-form-item label="文档">
        <el-input :model-value="store.currentDocumentGovernanceName" disabled />
      </el-form-item>
      <el-form-item label="文档等级">
        <el-select v-model="store.currentDocumentGovernance.classification">
          <el-option label="公开" value="Public" />
          <el-option label="内部" value="Internal" />
          <el-option label="敏感" value="Sensitive" />
          <el-option label="禁用" value="Forbidden" />
        </el-select>
      </el-form-item>
      <el-form-item label="来源类型">
        <el-select v-model="store.currentDocumentGovernance.sourceType">
          <el-option label="用户上传" value="UserUploaded" />
          <el-option label="业务规则" value="BusinessRule" />
          <el-option label="Cloud 只读文档" value="CloudReadOnlyApiDoc" />
          <el-option label="运维手册" value="Runbook" />
          <el-option label="外部资料" value="External" />
        </el-select>
      </el-form-item>
      <el-form-item label="已脱敏">
        <el-switch v-model="store.currentDocumentGovernance.isSanitized" />
      </el-form-item>
      <el-form-item label="允许进入回答">
        <el-switch v-model="store.currentDocumentGovernance.allowedForFinalPrompt" />
      </el-form-item>
      <el-form-item label="生效时间">
        <el-date-picker
          v-model="store.currentDocumentGovernance.effectiveFrom"
          type="datetime"
          value-format="YYYY-MM-DDTHH:mm:ss[Z]"
          clearable
        />
      </el-form-item>
      <el-form-item label="过期时间">
        <el-date-picker
          v-model="store.currentDocumentGovernance.effectiveTo"
          type="datetime"
          value-format="YYYY-MM-DDTHH:mm:ss[Z]"
          clearable
        />
      </el-form-item>
      <el-form-item label="阻断原因">
        <el-input
          v-model="store.currentDocumentGovernance.blockedReason"
          type="textarea"
          :rows="3"
          maxlength="500"
          show-word-limit
        />
      </el-form-item>
      <el-alert
        v-if="store.actionErrors.document"
        type="error"
        show-icon
        :closable="false"
        :title="store.actionErrors.document"
      />
    </el-form>
    <template #footer>
      <el-button @click="store.closeDocumentGovernanceDialog()">取消</el-button>
      <el-button
        type="primary"
        :loading="store.submittingStates.documentGovernance"
        @click="store.saveDocumentGovernance()"
      >
        保存
      </el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
:deep(.el-drawer__body) {
  overflow: auto;
}
</style>
