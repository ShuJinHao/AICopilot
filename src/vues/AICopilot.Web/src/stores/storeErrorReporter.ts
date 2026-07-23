import { toFriendlyMessage } from './chatErrorStore'

export type ErrorReporter = (message: string) => void

export function reportLoadError(
  reportError: ErrorReporter | undefined,
  action: string,
  error: unknown,
) {
  reportError?.(`${action}失败：${toFriendlyMessage(error)}`)
}

export async function loadNullableResource<T>(
  load: () => Promise<T>,
  reportError: ErrorReporter | undefined,
  action: string,
  diagnostic: string,
): Promise<T | null> {
  try {
    return await load()
  } catch (error) {
    console.error(diagnostic, error)
    reportLoadError(reportError, action, error)
    return null
  }
}
