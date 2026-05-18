import { reactive } from 'vue'

type ToastType = 'success' | 'error' | 'warning' | 'info'

interface ToastItem {
  id: number
  type: ToastType
  message: string
}

const toasts = reactive<ToastItem[]>([])
let nextToastId = 1

export function showAiToast(type: ToastType, message: string) {
  const id = nextToastId++
  toasts.push({ id, type, message })
  window.setTimeout(() => removeToast(id), 3200)
}

export function removeToast(id: number) {
  const index = toasts.findIndex((toast) => toast.id === id)
  if (index >= 0) {
    toasts.splice(index, 1)
  }
}

export function useAiToasts() {
  return {
    toasts,
    removeToast
  }
}

export async function confirmAiAction(message: string, title = '请确认') {
  return window.confirm(`${title}\n\n${message}`)
}
