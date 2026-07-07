const reloadAttemptedKey = 'aicopilot:stale-chunk-reload-attempted'

const staleChunkErrorPatterns = [
  /Failed to fetch dynamically imported module/i,
  /Importing a module script failed/i,
  /error loading dynamically imported module/i,
  /Loading chunk [\w-]+ failed/i,
  /ChunkLoadError/i,
  /Expected a JavaScript module script but the server responded with a MIME type of "text\/html"/i
]

type ReloadRuntime = {
  reload: () => void
  sessionStorage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>
}

function getBrowserReloadRuntime(): ReloadRuntime | null {
  if (typeof window === 'undefined') {
    return null
  }

  return {
    reload: () => window.location.reload(),
    sessionStorage: window.sessionStorage
  }
}

function safeRead(storage: ReloadRuntime['sessionStorage']) {
  try {
    return storage?.getItem(reloadAttemptedKey) ?? null
  } catch (error) {
    console.error('Failed to read stale chunk reload marker.', error)
    return null
  }
}

function safeWrite(storage: ReloadRuntime['sessionStorage']) {
  try {
    storage?.setItem(reloadAttemptedKey, '1')
  } catch (error) {
    console.error('Failed to write stale chunk reload marker.', error)
    // Reloading without the marker is still better than leaving the user stuck.
  }
}

function safeRemove(storage: ReloadRuntime['sessionStorage']) {
  try {
    storage?.removeItem(reloadAttemptedKey)
  } catch (error) {
    console.error('Failed to remove stale chunk reload marker.', error)
    // Ignore unavailable session storage.
  }
}

function errorMessage(error: unknown) {
  if (error instanceof Error) {
    return error.message
  }

  return String(error ?? '')
}

export function isStaleChunkLoadError(error: unknown) {
  const message = errorMessage(error)
  return staleChunkErrorPatterns.some((pattern) => pattern.test(message))
}

export function recoverFromStaleChunkLoad(
  error: unknown,
  runtime: ReloadRuntime | null = getBrowserReloadRuntime()
) {
  if (!runtime || !isStaleChunkLoadError(error)) {
    return false
  }

  if (safeRead(runtime.sessionStorage) === '1') {
    return false
  }

  safeWrite(runtime.sessionStorage)
  runtime.reload()
  return true
}

export function clearStaleChunkReloadFlag(
  runtime: Pick<ReloadRuntime, 'sessionStorage'> | null = getBrowserReloadRuntime()
) {
  if (!runtime) {
    return
  }

  safeRemove(runtime.sessionStorage)
}
