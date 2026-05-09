import { ref } from 'vue'
import { RAG_STORE_MESSAGES } from '@/constants/messages'
import { ragService } from '@/services/ragService'
import {
  createEmptyDocumentGovernanceForm,
  toDocumentGovernanceForm
} from '@/stores/ragFormFactories'
import { toStoreErrorMessage } from '@/stores/useDialogCrud'
import type { RagDomainStates } from '@/stores/rag/ragStoreTypes'
import type { KnowledgeDocumentGovernanceForm, KnowledgeDocumentSummary } from '@/types/app'

interface DocumentGovernanceDomainOptions {
  refreshDocuments: () => Promise<void>
}

export function useDocumentGovernanceDomain(
  states: RagDomainStates,
  options: DocumentGovernanceDomainOptions
) {
  const currentDocumentGovernance = ref<KnowledgeDocumentGovernanceForm>(
    createEmptyDocumentGovernanceForm()
  )
  const currentDocumentGovernanceName = ref('')

  function closeDocumentGovernanceDialog() {
    states.dialogStates.documentGovernance = false
    states.actionErrors.document = ''
    currentDocumentGovernance.value = createEmptyDocumentGovernanceForm()
    currentDocumentGovernanceName.value = ''
  }

  function openEditDocumentGovernanceDialog(document: KnowledgeDocumentSummary) {
    states.actionErrors.document = ''
    currentDocumentGovernance.value = toDocumentGovernanceForm(document)
    currentDocumentGovernanceName.value = document.name
    states.dialogStates.documentGovernance = true
  }

  async function saveDocumentGovernance() {
    states.submittingStates.documentGovernance = true
    states.actionErrors.document = ''

    try {
      await ragService.updateDocumentGovernance(currentDocumentGovernance.value)
      await options.refreshDocuments()
      closeDocumentGovernanceDialog()
    } catch (error) {
      states.actionErrors.document = toStoreErrorMessage(
        error,
        RAG_STORE_MESSAGES.document.governanceSaveFailed,
        RAG_STORE_MESSAGES.document.governanceSaveForbidden
      )
      throw error
    } finally {
      states.submittingStates.documentGovernance = false
    }
  }

  return {
    currentDocumentGovernance,
    currentDocumentGovernanceName,
    closeDocumentGovernanceDialog,
    openEditDocumentGovernanceDialog,
    saveDocumentGovernance
  }
}
