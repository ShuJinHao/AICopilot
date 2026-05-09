import { ref } from 'vue'
import { RAG_STORE_MESSAGES } from '@/constants/messages'
import { ragService } from '@/services/ragService'
import {
  createEmptyEmbeddingModelForm,
  toEmbeddingModelForm
} from '@/stores/ragFormFactories'
import { useAuthStore } from '@/stores/authStore'
import { useDialogCrud } from '@/stores/useDialogCrud'
import { toRagCrudStates, type RagDomainStates } from '@/stores/rag/ragStoreTypes'
import type { EmbeddingModelFormModel, EmbeddingModelSummary } from '@/types/app'

interface EmbeddingModelDomainOptions {
  refreshKnowledgeBases: () => Promise<void>
}

export function useEmbeddingModelDomain(
  states: RagDomainStates,
  options: EmbeddingModelDomainOptions
) {
  const authStore = useAuthStore()
  const embeddingModels = ref<EmbeddingModelSummary[]>([])
  const currentEmbeddingModel = ref<EmbeddingModelFormModel>(createEmptyEmbeddingModelForm())

  async function refreshEmbeddingModels() {
    if (!authStore.hasAnyPermission(['Rag.GetEmbeddingModel', 'Rag.GetListEmbeddingModels'])) {
      embeddingModels.value = []
      return
    }

    states.loadingStates.embeddingModel = true
    try {
      embeddingModels.value = await ragService.getEmbeddingModels()
    } finally {
      states.loadingStates.embeddingModel = false
    }
  }

  const embeddingModelCrud = useDialogCrud({
    domain: 'embeddingModel',
    states: toRagCrudStates(states),
    current: currentEmbeddingModel,
    messages: RAG_STORE_MESSAGES.embeddingModel,
    createEmptyForm: createEmptyEmbeddingModelForm,
    toForm: toEmbeddingModelForm,
    loadDetail: ragService.getEmbeddingModel,
    saveForm: async (form, mode) => {
      const payload = {
        ...form,
        name: form.name.trim(),
        provider: form.provider.trim(),
        baseUrl: form.baseUrl.trim(),
        modelName: form.modelName.trim(),
        apiKey: form.apiKey.trim()
      }

      if (mode === 'create') {
        await ragService.createEmbeddingModel(payload)
      } else {
        await ragService.updateEmbeddingModel(payload)
      }
    },
    deleteItem: async (id) => {
      await ragService.deleteEmbeddingModel(id)
    },
    afterSave: refreshEmbeddingModels,
    afterDelete: async () => {
      await refreshEmbeddingModels()
      await options.refreshKnowledgeBases()
    }
  })

  return {
    embeddingModels,
    currentEmbeddingModel,
    refreshEmbeddingModels,
    closeEmbeddingModelDialog: embeddingModelCrud.closeDialog,
    openCreateEmbeddingModelDialog: embeddingModelCrud.openCreateDialog,
    openEditEmbeddingModelDialog: embeddingModelCrud.openEditDialog,
    saveEmbeddingModel: embeddingModelCrud.saveDialog,
    deleteEmbeddingModel: embeddingModelCrud.deleteDialog
  }
}
