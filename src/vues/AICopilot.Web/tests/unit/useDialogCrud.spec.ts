import { reactive, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import { useDialogCrud } from '@/stores/useDialogCrud'
import type { ConfigDialogMode } from '@/types/app'

type Domain = 'item'

interface TestForm {
  id?: string
  name: string
}

interface TestDetail {
  id: string
  name: string
}

interface HarnessOverrides {
  loadDetail?: (id: string) => Promise<TestDetail>
  saveForm?: (form: TestForm, mode: ConfigDialogMode) => Promise<void>
  deleteItem?: (id: string) => Promise<void>
  afterSave?: () => Promise<void>
  afterDelete?: (id: string) => Promise<void>
}

function createHarness(overrides: HarnessOverrides = {}) {
  const current = ref<TestForm>({ name: '' })
  const states = {
    loadingStates: reactive<Record<Domain, boolean>>({ item: false }),
    dialogStates: reactive<Record<Domain, boolean>>({ item: false }),
    dialogModes: reactive<Record<Domain, ConfigDialogMode>>({ item: 'create' }),
    submittingStates: reactive<Record<Domain, boolean>>({ item: false }),
    actionErrors: reactive<Record<Domain, string>>({ item: '' })
  }
  const callbacks = {
    loadDetail: vi.fn(async (id: string) => ({ id, name: 'loaded' })),
    saveForm: vi.fn(async () => {}),
    deleteItem: vi.fn(async () => {}),
    afterSave: vi.fn(async () => {}),
    afterDelete: vi.fn(async () => {})
  }
  const crud = useDialogCrud<Domain, TestForm, TestDetail, string>({
    domain: 'item',
    states,
    current,
    messages: {
      loadFailed: 'load failed',
      loadForbidden: 'load forbidden',
      saveFailed: 'save failed',
      saveForbidden: 'save forbidden',
      deleteFailed: 'delete failed',
      deleteForbidden: 'delete forbidden'
    },
    createEmptyForm: () => ({ name: '' }),
    toForm: (detail) => ({ id: detail.id, name: detail.name }),
    ...callbacks,
    ...overrides
  })

  return { current, states, callbacks, crud }
}

describe('useDialogCrud', () => {
  it('runs create, edit, save, delete lifecycle and refresh callbacks', async () => {
    const { current, states, callbacks, crud } = createHarness()

    crud.openCreateDialog()
    expect(states.dialogModes.item).toBe('create')
    expect(states.dialogStates.item).toBe(true)
    expect(current.value).toEqual({ name: '' })

    await crud.openEditDialog('item-1')
    expect(callbacks.loadDetail).toHaveBeenCalledWith('item-1')
    expect(states.dialogModes.item).toBe('edit')
    expect(current.value).toEqual({ id: 'item-1', name: 'loaded' })

    current.value.name = 'saved'
    await crud.saveDialog()
    expect(callbacks.saveForm).toHaveBeenCalledWith({ id: 'item-1', name: 'saved' }, 'edit')
    expect(callbacks.afterSave).toHaveBeenCalledOnce()
    expect(states.dialogStates.item).toBe(false)
    expect(current.value).toEqual({ name: '' })

    await crud.deleteDialog('item-1')
    expect(callbacks.deleteItem).toHaveBeenCalledWith('item-1')
    expect(callbacks.afterDelete).toHaveBeenCalledWith('item-1')
  })

  it('records detail loading failures and clears loading state', async () => {
    const { states, crud } = createHarness({
      loadDetail: vi.fn(async () => {
        throw new Error('boom')
      })
    })

    await expect(crud.openEditDialog('item-1')).rejects.toThrow('boom')

    expect(states.loadingStates.item).toBe(false)
    expect(states.actionErrors.item).toBe('load failed')
    expect(states.dialogStates.item).toBe(false)
  })

  it('records save failures and clears submitting state', async () => {
    const { states, crud } = createHarness({
      saveForm: vi.fn(async () => {
        throw new Error('boom')
      })
    })

    crud.openCreateDialog()
    await expect(crud.saveDialog()).rejects.toThrow('boom')

    expect(states.submittingStates.item).toBe(false)
    expect(states.actionErrors.item).toBe('save failed')
    expect(states.dialogStates.item).toBe(true)
  })

  it('records delete failures without closing dialogs', async () => {
    const { states, crud } = createHarness({
      deleteItem: vi.fn(async () => {
        throw new Error('boom')
      })
    })

    await expect(crud.deleteDialog('item-1')).rejects.toThrow('boom')

    expect(states.actionErrors.item).toBe('delete failed')
  })
})
