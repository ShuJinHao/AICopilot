import type { Ref } from 'vue'
import { ApiError, getProblemDetails } from '@/services/apiClient'
import type { CrudMessageSet } from '@/constants/messages'
import type { ConfigDialogMode } from '@/types/app'

interface DialogCrudStates<TDomain extends string> {
  loadingStates: Record<TDomain, boolean>
  dialogStates: Record<TDomain, boolean>
  dialogModes: Record<TDomain, ConfigDialogMode>
  submittingStates: Record<TDomain, boolean>
  actionErrors: Record<TDomain, string>
}

interface DialogCrudOptions<TDomain extends string, TForm, TDetail, TId> {
  domain: TDomain
  states: DialogCrudStates<TDomain>
  current: Ref<TForm>
  messages: CrudMessageSet
  createEmptyForm: () => TForm
  toForm: (detail: TDetail) => TForm
  loadDetail: (id: TId) => Promise<TDetail>
  saveForm: (form: TForm, mode: ConfigDialogMode) => Promise<void>
  deleteItem: (id: TId) => Promise<void>
  afterClose?: () => void
  afterOpenCreate?: () => void
  afterOpenEdit?: (detail: TDetail) => void
  afterSave?: () => Promise<void>
  afterDelete?: (id: TId) => Promise<void>
}

export function toStoreErrorMessage(error: unknown, fallback: string, forbiddenMessage: string) {
  if (error instanceof ApiError && error.status === 403) {
    return forbiddenMessage
  }

  if (error instanceof ApiError) {
    const problem = getProblemDetails(error.details)
    return problem?.detail || problem?.title || fallback
  }

  return fallback
}

export function useDialogCrud<TDomain extends string, TForm, TDetail, TId>(
  options: DialogCrudOptions<TDomain, TForm, TDetail, TId>
) {
  const { domain, states } = options

  function closeDialog() {
    states.dialogStates[domain] = false
    states.dialogModes[domain] = 'create'
    states.actionErrors[domain] = ''
    options.current.value = options.createEmptyForm()
    options.afterClose?.()
  }

  function openCreateDialog() {
    states.actionErrors[domain] = ''
    states.dialogModes[domain] = 'create'
    options.current.value = options.createEmptyForm()
    options.afterOpenCreate?.()
    states.dialogStates[domain] = true
  }

  async function openEditDialog(id: TId) {
    states.loadingStates[domain] = true
    states.actionErrors[domain] = ''

    try {
      const detail = await options.loadDetail(id)
      options.current.value = options.toForm(detail)
      options.afterOpenEdit?.(detail)
      states.dialogModes[domain] = 'edit'
      states.dialogStates[domain] = true
    } catch (error) {
      states.actionErrors[domain] = toStoreErrorMessage(
        error,
        options.messages.loadFailed,
        options.messages.loadForbidden
      )
      throw error
    } finally {
      states.loadingStates[domain] = false
    }
  }

  async function saveDialog() {
    states.submittingStates[domain] = true
    states.actionErrors[domain] = ''

    try {
      await options.saveForm(options.current.value, states.dialogModes[domain])
      await options.afterSave?.()
      closeDialog()
    } catch (error) {
      states.actionErrors[domain] = toStoreErrorMessage(
        error,
        options.messages.saveFailed,
        options.messages.saveForbidden
      )
      throw error
    } finally {
      states.submittingStates[domain] = false
    }
  }

  async function deleteDialog(id: TId) {
    states.actionErrors[domain] = ''

    try {
      await options.deleteItem(id)
      await options.afterDelete?.(id)
    } catch (error) {
      states.actionErrors[domain] = toStoreErrorMessage(
        error,
        options.messages.deleteFailed,
        options.messages.deleteForbidden
      )
      throw error
    }
  }

  return {
    closeDialog,
    openCreateDialog,
    openEditDialog,
    saveDialog,
    deleteDialog
  }
}
