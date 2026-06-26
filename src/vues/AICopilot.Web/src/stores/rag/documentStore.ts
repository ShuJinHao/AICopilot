import { ref, type Ref } from 'vue'
import { RAG_STORE_MESSAGES } from '@/constants/messages'
import { ragService } from '@/services/ragService'
import { createDefaultUploadGovernanceForm } from '@/stores/ragFormFactories'
import { useAuthStore } from '@/stores/authStore'
import { toStoreErrorMessage } from '@/stores/useDialogCrud'
import type { RagDomainStates } from '@/stores/rag/ragStoreTypes'
import type { KnowledgeDocumentSummary, UploadDocumentGovernanceForm } from '@/types/app'

interface DocumentDomainOptions {
  selectedKnowledgeBaseId: Ref<string>
  documents: Ref<KnowledgeDocumentSummary[]>
  refreshKnowledgeBases: () => Promise<void>
}

export function useDocumentDomain(states: RagDomainStates, options: DocumentDomainOptions) {
  const authStore = useAuthStore()
  const uploadGovernanceForm = ref<UploadDocumentGovernanceForm>(
    createDefaultUploadGovernanceForm()
  )

  async function refreshDocuments() {
    if (!authStore.hasPermission('Rag.GetListDocuments')) {
      options.documents.value = []
      return
    }

    if (!options.selectedKnowledgeBaseId.value) {
      options.documents.value = []
      return
    }

    states.loadingStates.document = true
    try {
      options.documents.value = await ragService.getDocuments(options.selectedKnowledgeBaseId.value)
    } finally {
      states.loadingStates.document = false
    }
  }

  async function uploadDocument(file: File) {
    if (!options.selectedKnowledgeBaseId.value) {
      states.actionErrors.document = RAG_STORE_MESSAGES.selectKnowledgeBaseFirst
      return
    }

    states.loadingStates.document = true
    states.actionErrors.document = ''

    try {
      await ragService.uploadDocument(
        options.selectedKnowledgeBaseId.value,
        file,
        uploadGovernanceForm.value
      )
      await options.refreshKnowledgeBases()
      await refreshDocuments()
    } catch (error) {
      states.actionErrors.document = toStoreErrorMessage(
        error,
        RAG_STORE_MESSAGES.document.uploadFailed,
        RAG_STORE_MESSAGES.document.uploadForbidden
      )
      throw error
    } finally {
      states.loadingStates.document = false
    }
  }

  async function deleteDocument(id: number) {
    states.actionErrors.document = ''

    try {
      await ragService.deleteDocument(id)
      await options.refreshKnowledgeBases()
      await refreshDocuments()
    } catch (error) {
      states.actionErrors.document = toStoreErrorMessage(
        error,
        RAG_STORE_MESSAGES.document.deleteFailed,
        RAG_STORE_MESSAGES.document.deleteForbidden
      )
      throw error
    }
  }

  async function retryDocument(id: number) {
    states.actionErrors.document = ''

    try {
      await ragService.retryDocument(id)
      await refreshDocuments()
    } catch (error) {
      states.actionErrors.document = toStoreErrorMessage(
        error,
        RAG_STORE_MESSAGES.document.uploadFailed,
        RAG_STORE_MESSAGES.document.uploadForbidden
      )
      throw error
    }
  }

  return {
    uploadGovernanceForm,
    refreshDocuments,
    uploadDocument,
    deleteDocument,
    retryDocument
  }
}
