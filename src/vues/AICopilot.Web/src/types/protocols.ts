export interface Session {
  id: string
  title: string
  onsiteConfirmedAt?: string | null
  onsiteConfirmedBy?: string | null
  onsiteConfirmationExpiresAt?: string | null
}

export enum MessageRole {
  User = 'User',
  Assistant = 'Assistant'
}

export enum ChunkType {
  Error = 'Error',
  Text = 'Text',
  Metadata = 'Metadata',
  Intent = 'Intent',
  Widget = 'Widget',
  FunctionResult = 'FunctionResult',
  FunctionCall = 'FunctionCall',
  ApprovalRequest = 'ApprovalRequest'
}

export interface ChatChunk {
  source: string
  type: ChunkType
  content: string
}

export interface ChatModelMetadataPayload {
  finalModelId?: string | null
  finalModelName?: string | null
  routingModelId?: string | null
  routingModelName?: string | null
  contextWindowTokens?: number | null
  maxOutputTokens?: number | null
}

export interface ChatErrorPayload {
  code?: string
  detail?: string
  userFacingMessage?: string | null
}

export interface IntentResult {
  intent: string
  confidence: number
  reasoning?: string
  query?: string
}

export interface FunctionApprovalRequest {
  callId: string
  name: string
  runtimeName?: string | null
  targetType?: string | null
  targetName?: string | null
  toolName?: string | null
  args: string | Record<string, unknown>
  requiresOnsiteAttestation: boolean
  attestationExpiresAt?: string | null
}

export interface Widget {
  id: string
  type: string
  title: string
  description: string
  data: unknown
}

export interface UploadRecord {
  id: string
  scope: 'SessionTemp' | 'AgentInput' | 'KnowledgeBase' | string
  sessionId?: string | null
  agentTaskId?: string | null
  knowledgeBaseId?: string | null
  ragDocumentId?: number | null
  fileName: string
  contentType: string
  fileSize: number
  sha256: string
  status: string
  createdAt: string
}

export interface AgentStep {
  id: string
  stepIndex: number
  title: string
  description: string
  stepType: string
  status: string
  toolCode?: string | null
  requiresApproval: boolean
  errorMessage?: string | null
}

export interface AgentTaskFailureSummary {
  stepIndex?: number | null
  toolCode?: string | null
  errorCode: string
  safeMessage: string
  canRetry: boolean
  nextAction: string
}

export interface AgentTask {
  id: string
  taskCode: string
  sessionId: string
  title: string
  goal: string
  taskType: string
  status: string
  riskLevel: string
  modelId?: string | null
  workspaceId?: string | null
  workspaceCode?: string | null
  planJson: string
  finalSummary?: string | null
  createdAt: string
  updatedAt: string
  completedAt?: string | null
  steps: AgentStep[]
  pendingApprovalCount?: number
  lastFailureReason?: string | null
  canRun: boolean
  canRetry?: boolean
  canSubmitFinalReview: boolean
  canApproveFinal: boolean
  failureSummary?: AgentTaskFailureSummary | null
  activeRunAttemptId?: string | null
  runAttemptCount?: number
  isRunInProgress: boolean
  queuedRunId?: string | null
  runQueueStatus?: string | null
  isRunQueued: boolean
}

export interface AgentTrialScenario {
  id: string
  title: string
  description: string
  businessDomain: string
  defaultPrompt: string
  defaultDataSourceIds: string[]
  defaultArtifactTypes: string[]
  requiresApproval: boolean
  isSimulationOnly: boolean
  isCloudSandboxTrial?: boolean
  sourceMode?: string | null
  sourceLabel?: string | null
}

export interface CloudSandboxGoalTimeRange {
  from?: string | null
  to?: string | null
}

export interface CloudSandboxGoalIntent {
  intentId: string
  goalHash: string
  endpointCodes: string[]
  timeRange: CloudSandboxGoalTimeRange
  maxRows: number
  artifactTypes: string[]
  analysisType: string
  warnings: string[]
  rejectedReasons: string[]
  requiresToolApproval: boolean
  requiresFinalApproval: boolean
}

export interface CloudReadonlySandboxControlledPlan {
  task: AgentTask
  intent: CloudSandboxGoalIntent
}

export interface AgentApprovalRequest {
  id: string
  taskId: string
  workspaceCode?: string | null
  type: string
  targetId: string
  targetName: string
  riskLevel: string
  status: string
  reason?: string | null
  requestedAt: string
  decidedAt?: string | null
  decidedBy?: string | null
}

export interface AgentTaskAuditSummary {
  id: string
  taskId: string
  workspaceCode?: string | null
  actionCode: string
  targetType: string
  targetName: string
  result: string
  summary: string
  createdAt: string
  metadata: Record<string, string>
}

export interface ArtifactRecord {
  id: string
  name: string
  type: string
  status: string
  relativePath: string
  fileSize: number
  mimeType: string
  version: number
  updatedAt: string
  previewKind: 'chart' | 'json' | 'table' | 'markdown' | 'html' | 'pdf' | 'spreadsheet' | 'download' | string
  downloadUrl: string
  generatedByStepOrder?: number | null
  requiresApproval?: boolean
  approvalStatus?: string
  finalizedAt?: string | null
  artifactVersion?: number
  artifactStatus?: string
  sourceMode?: string | null
  boundary?: string | null
  isSimulation?: boolean
  isSandbox?: boolean
  sourceLabel?: string | null
  queryHash?: string | null
  resultHash?: string | null
  rowCount?: number
  isTruncated?: boolean
}

export interface ArtifactManifestItem {
  artifactId: string
  type: string
  name: string
  relativePath: string
  status: string
  version: number
  generatedByStep?: number | null
  downloadUrl: string
  createdAt: string
}

export interface ArtifactWorkspaceFile {
  name: string
  relativePath: string
  isDirectory: boolean
  fileSize: number
  updatedAt: string
}

export interface ArtifactWorkspace {
  id: string
  workspaceCode: string
  taskId: string
  status: string
  files: ArtifactWorkspaceFile[]
  artifacts: ArtifactRecord[]
  manifest?: ArtifactManifestItem[]
  draftArtifacts?: ArtifactRecord[]
  finalArtifacts?: ArtifactRecord[]
}

export interface AgentArtifactPreview {
  artifactId: string
  name: string
  artifactType: string
  previewKind: string
  artifactStatus: string
  artifactVersion: number
  relativePath: string
  fileSize: number
  mimeType: string
  sourceMode?: string | null
  boundary?: string | null
  isSimulation: boolean
  isSandbox: boolean
  sourceLabel?: string | null
  queryHash?: string | null
  resultHash?: string | null
  rowCount: number
  isTruncated: boolean
  content?: string | null
  columns: string[]
  rows: Array<Record<string, string>>
  metadata: Record<string, string>
}

export interface TrialCampaignSummary {
  scenarioRunCount: number
  passedRunCount: number
  failedRunCount: number
  blockedRunCount: number
  finalArtifactCount: number
  pendingApprovalCount: number
  unresolvedRiskCount: number
  queryHashCount: number
  resultHashCount: number
}

export interface TrialScenarioRun {
  runId: string
  campaignId: string
  scenarioId: string
  trialMode: string
  sourceMode: string
  boundary?: string | null
  taskId: string
  artifactIds: string[]
  queryHashes: string[]
  resultHashes: string[]
  approvalStatus: string
  status: string
  startedAt?: string | null
  completedAt?: string | null
}

export interface TrialRiskIssue {
  issueId: string
  campaignId: string
  severity: string
  category: string
  status: string
  owner?: string | null
  sourceRef?: string | null
  resolutionHash?: string | null
  createdAt: string
  updatedAt: string
}

export interface TrialCampaign {
  campaignId: string
  name: string
  status: string
  allowedSourceModes: string[]
  ownerDepartment?: string | null
  startAt?: string | null
  endAt?: string | null
  summary: TrialCampaignSummary
  description?: string | null
  readinessStatus: string
  createdAt: string
  updatedAt: string
  scenarioRuns: TrialScenarioRun[]
  risks: TrialRiskIssue[]
}

export interface PilotReadinessCheck {
  code: string
  label: string
  status: string
  isBlocking: boolean
  message: string
}

export interface PilotReadinessMetrics {
  scenarioRuns: number
  passedRuns: number
  finalArtifacts: number
  pendingApprovals: number
  unresolvedRisks: number
  queryHashSamples: number
  resultHashSamples: number
}

export interface PilotReadinessAssessment {
  campaignId: string
  status: string
  checks: PilotReadinessCheck[]
  blockers: string[]
  warnings: string[]
  metrics: PilotReadinessMetrics
  generatedAt: string
}

export interface TrialEvidenceMetric {
  code: string
  label: string
  value: number
}

export interface TrialEvidenceItem {
  evidenceType: string
  sourceMode: string
  boundary?: string | null
  status: string
  hashSamples: string[]
  referenceId: string
}

export interface TrialEvidencePackage {
  campaignId: string
  readinessStatus: string
  metrics: TrialEvidenceMetric[]
  evidenceItems: TrialEvidenceItem[]
  unresolvedRisks: TrialRiskIssue[]
  reportArtifactId?: string | null
  generatedAt: string
}

export interface CloudReadonlyPilotContractCheckSummary {
  total: number
  passed: number
  blockedByPolicy: number
  failed: number
  lastCheckedAt?: string | null
}

export interface CloudAiReadEndpointCheck {
  endpointCode: string
  method: string
  path: string
  policyStatus: string
  httpStatus?: number | null
  durationMs: number
  rowCount: number
  isTruncated: boolean
  resultHash?: string | null
  errorCode?: string | null
  status: string
}

export interface CloudReadonlyPilotConfigPackage {
  packageId: string
  allowedEndpointCodes: string[]
  maxTimeRangeDays: number
  maxRows: number
  timeoutMs: number
  approvalPolicy: string
  rollbackPolicy: string
  ownerDepartment: string
  evidenceRefs: string[]
  status: string
}

export interface CloudReadonlyPilotReadinessStatus {
  status: string
  enabled: boolean
  evidencePackageId?: string | null
  configSummary?: CloudReadonlyPilotConfigPackage | null
  approvalRehearsalStatus: string
  contractCheckSummary: CloudReadonlyPilotContractCheckSummary
  blockers: string[]
  warnings: string[]
  lastCheckedAt?: string | null
}

export interface PilotApprovalRehearsalStep {
  code: string
  label: string
  status: string
  isBlocking: boolean
  auditRef: string
}

export interface PilotApprovalRehearsal {
  rehearsalId: string
  packageId: string
  steps: PilotApprovalRehearsalStep[]
  status: string
  approvers: string[]
  auditRefs: string[]
  generatedAt: string
}

export interface CloudReadonlyPilotContractRehearsal {
  packageId: string
  sourceMode: string
  boundary: string
  isProductionData: boolean
  checks: CloudAiReadEndpointCheck[]
  blockedSamples: string[]
  generatedAt: string
}

export interface CloudReadonlyProductionPilotStatus {
  status: string
  enabled: boolean
  pilotWindowId?: string | null
  windowStatus?: string | null
  allowedEndpointCodes: string[]
  approvalStatus: string
  toolVisible: boolean
  toolExecutable: boolean
  lastRunAt?: string | null
  blockers: string[]
  warnings: string[]
}

export interface CloudReadonlyProductionPilotWindow {
  windowId: string
  name: string
  status: string
  startAt: string
  endAt: string
  allowedEndpointCodes: string[]
  maxTimeRangeDays: number
  maxRows: number
  timeoutMs: number
  ownerDepartment: string
  approvalPolicy: string
  rollbackPolicy: string
}

export interface CloudProductionPilotQueryResult {
  endpointCode: string
  sourceType: string
  sourceMode: string
  isProductionData: boolean
  isSandbox: boolean
  isSimulation: boolean
  sourceLabel: string
  boundary: string
  pilotWindowId: string
  queryHash: string
  resultHash: string
  rowCount: number
  isTruncated: boolean
  approvalStatus: string
}

export interface CloudReadonlyProductionPilotScenarioResult {
  scenarioId: string
  scenarioTitle: string
  status: string
  queryResult: CloudProductionPilotQueryResult
  artifactTypes: string[]
  boundary: string
}

export interface AgentRunQueueSummary {
  queuedCount: number
  leasedCount: number
  succeededCount: number
  failedCount: number
  cancelledCount: number
  deadLetterCount: number
  staleLeasedCount: number
  oldestQueuedAt?: string | null
  averageWaitMs?: number | null
  averageRunMs?: number | null
  oldestQueuedWaitMs?: number | null
  activeWorkerCount: number
  workspaceMismatchCount: number
  generatedAt: string
}

export interface AgentRunQueueItem {
  id: string
  taskId: string
  triggerType: string
  status: string
  requestedBy: string
  runAttemptId?: string | null
  leaseId?: string | null
  leaseOwner?: string | null
  leaseExpiresAt?: string | null
  availableAt: string
  startedAt?: string | null
  completedAt?: string | null
  failureCode?: string | null
  safeMessage?: string | null
  createdAt: string
  updatedAt: string
}

export interface AgentRunQueuePage {
  items: AgentRunQueueItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPrevious: boolean
  hasNext: boolean
}

export interface AgentWorkerHeartbeat {
  id?: string
  workerId: string
  workerName: string
  startedAt?: string | null
  lastSeenAt?: string | null
  isActive: boolean
  activeQueueItemId?: string | null
  activeTaskId?: string | null
  workspaceRootHash: string
  version: string
  workspaceMatchesHttpApi: boolean
}

export interface AgentWorkerStatus {
  statusCode: string
  hasActiveWorkers: boolean
  workspaceConsistent: boolean
  httpApiWorkspaceRootHash: string
  activeWorkerCount: number
  queuedCount: number
  leasedCount: number
  staleLeasedCount: number
  oldestQueuedAt?: string | null
  generatedAt: string
  workers: AgentWorkerHeartbeat[]
}

export interface ChartWidget extends Widget {
  type: 'Chart'
  data: {
    category: 'Bar' | 'Line' | 'Pie'
    dataset: {
      dimensions: string[]
      source: Array<Record<string, unknown>>
    }
    encoding: {
      x: string
      y: string[]
      seriesName?: string
    }
  }
}

export interface StatsCardWidget extends Widget {
  type: 'StatsCard'
  data: {
    label: string
    value: string | number
    unit?: string
  }
}

export interface DataTableWidget extends Widget {
  type: 'DataTable'
  data: {
    columns: Array<{
      key: string
      label: string
      dataType: 'string' | 'number' | 'date' | 'boolean'
    }>
    rows: Array<Record<string, unknown>>
  }
}
