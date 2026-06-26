import { ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import type {
  AgentArtifactPreview,
  AgentTask,
  ArtifactRecord,
  ArtifactWorkspace,
  UploadRecord
} from '@/types/protocols'

export interface AgentChartPreview {
  labels: string[]
  values: number[]
  source?: string
  sourceMode?: string
  sourceLabel?: string
  isSimulation?: boolean
  queryHash?: string
}

export const useArtifactWorkspaceStore = defineStore('artifactWorkspace', () => {
  const uploadedFiles = ref<UploadRecord[]>([])
  const currentWorkspace = ref<ArtifactWorkspace | null>(null)
  const currentArtifactPreview = ref<AgentArtifactPreview | null>(null)
  const chartPreview = ref<AgentChartPreview | null>(null)

  function reset() {
    uploadedFiles.value = []
    currentWorkspace.value = null
    currentArtifactPreview.value = null
    chartPreview.value = null
  }

  async function refreshWorkspace(task: AgentTask) {
    if (!task.workspaceCode) {
      currentWorkspace.value = null
      currentArtifactPreview.value = null
      chartPreview.value = null
      return null
    }

    currentWorkspace.value = await chatService.getWorkspace(task.workspaceCode)
    const firstArtifact = currentWorkspace.value.artifacts[0]
    currentArtifactPreview.value = firstArtifact
      ? await loadArtifactPreview(firstArtifact.id)
      : null
    await refreshChartPreview()
    return currentWorkspace.value
  }

  async function loadArtifactPreview(artifactId: string) {
    try {
      currentArtifactPreview.value = await chatService.getArtifactPreview(artifactId)
      return currentArtifactPreview.value
    } catch {
      currentArtifactPreview.value = null
      return null
    }
  }

  async function refreshChartPreview() {
    const chartArtifact = currentWorkspace.value?.artifacts.find((artifact) => artifact.previewKind === 'chart')
    if (!chartArtifact) {
      chartPreview.value = null
      return
    }

    try {
      const blob = await chatService.downloadArtifact(chartArtifact.downloadUrl)
      const payload = JSON.parse(await blob.text()) as {
        labels?: unknown
        values?: unknown
        source?: unknown
        sourceMode?: unknown
        sourceLabel?: unknown
        isSimulation?: unknown
        queryHash?: unknown
        sourceInfo?: {
          sourceMode?: unknown
          sourceLabel?: unknown
          isSimulation?: unknown
          queryHash?: unknown
        }
      }
      const sourceInfo = payload.sourceInfo
      chartPreview.value = {
        labels: Array.isArray(payload.labels) ? payload.labels.map(String) : [],
        values: Array.isArray(payload.values) ? payload.values.map((value) => Number(value) || 0) : [],
        source: typeof payload.source === 'string' ? payload.source : undefined,
        sourceMode:
          typeof payload.sourceMode === 'string'
            ? payload.sourceMode
            : typeof sourceInfo?.sourceMode === 'string'
              ? sourceInfo.sourceMode
              : undefined,
        sourceLabel:
          typeof payload.sourceLabel === 'string'
            ? payload.sourceLabel
            : typeof sourceInfo?.sourceLabel === 'string'
              ? sourceInfo.sourceLabel
              : undefined,
        isSimulation:
          typeof payload.isSimulation === 'boolean'
            ? payload.isSimulation
            : typeof sourceInfo?.isSimulation === 'boolean'
              ? sourceInfo.isSimulation
              : undefined,
        queryHash:
          typeof payload.queryHash === 'string'
            ? payload.queryHash
            : typeof sourceInfo?.queryHash === 'string'
              ? sourceInfo.queryHash
              : undefined
      }
    } catch {
      chartPreview.value = null
    }
  }

  async function downloadArtifact(artifact: ArtifactRecord) {
    const blob = await chatService.downloadArtifact(artifact.downloadUrl)
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = artifact.name
    anchor.click()
    URL.revokeObjectURL(url)
  }

  async function uploadSessionFile(sessionId: string, file: File) {
    const uploaded = await chatService.uploadFile('SessionTemp', file, { sessionId })
    uploadedFiles.value.unshift(uploaded)
    return uploaded
  }

  return {
    uploadedFiles,
    currentWorkspace,
    currentArtifactPreview,
    chartPreview,
    reset,
    refreshWorkspace,
    loadArtifactPreview,
    refreshChartPreview,
    downloadArtifact,
    uploadSessionFile
  }
})
