param(
    [string[]]$ChangedFiles,
    [string]$BaseRef,
    [switch]$IncludeWorkingTree
)

$ErrorActionPreference = "Stop"

function Normalize-PathForGit {
    param([string]$Path)
    return ($Path -replace "\\", "/").Trim()
}

function Add-Error {
    param([string]$Message)
    $script:Errors.Add($Message) | Out-Null
}

$Errors = [System.Collections.Generic.List[string]]::new()

if (-not $ChangedFiles -or $ChangedFiles.Count -eq 0) {
    if ($BaseRef) {
        $ChangedFiles = git diff --name-only $BaseRef -- | ForEach-Object { Normalize-PathForGit $_ }
    }
    elseif ($IncludeWorkingTree) {
        $ChangedFiles = git diff --name-only -- | ForEach-Object { Normalize-PathForGit $_ }
        $ChangedFiles += git ls-files --others --exclude-standard | ForEach-Object { Normalize-PathForGit $_ }
    }
    else {
        $ChangedFiles = git diff --name-only --cached -- | ForEach-Object { Normalize-PathForGit $_ }
    }
}
else {
    $ChangedFiles = $ChangedFiles | ForEach-Object { Normalize-PathForGit $_ }
}

$ChangedFiles = $ChangedFiles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique

$forbiddenPathPatterns = @(
    "^IIoT\.CloudPlatform/",
    "^IIoT\.EdgeClient/",
    "^IIoT\.EdgeClient\.AvaloniaMigration/",
    "^\.\./IIoT\.CloudPlatform/",
    "^\.\./IIoT\.EdgeClient/",
    "^\.\./IIoT\.EdgeClient\.AvaloniaMigration/"
)

foreach ($file in $ChangedFiles) {
    foreach ($pattern in $forbiddenPathPatterns) {
        if ($file -match $pattern) {
            Add-Error "Forbidden cross-project path in AICopilot release-candidate batch: $file"
        }
    }
}

$packagePath = "src/vues/AICopilot.Web/package.json"
if (Test-Path $packagePath) {
    $package = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json
    if (-not $package.scripts."test:smoke") {
        Add-Error "$packagePath must expose npm run test:smoke."
    }
}
else {
    Add-Error "Missing frontend package manifest: $packagePath"
}

$chatServicePath = "src/vues/AICopilot.Web/src/services/chatService.ts"
if (Test-Path $chatServicePath) {
    $chatService = Get-Content -LiteralPath $chatServicePath -Raw
    if ($chatService -match "artifact/\$\{encodeURIComponent\(id\)\}/download") {
        Add-Error "Frontend must not construct artifact download URLs from artifact id."
    }
}

$errorStorePath = "src/vues/AICopilot.Web/src/stores/chatErrorStore.ts"
$requiredCodes = @(
    "missing_permission",
    "cloud_readonly_tool_disabled",
    "tool_requires_approval",
    "agent_task_run_queued",
    "agent_task_run_in_progress",
    "agent_worker_unavailable",
    "agent_worker_workspace_mismatch",
    "artifact_finalized",
    "workspace_manifest_invalid",
    "planner_model_unavailable",
    "tool_disabled",
    "tool_blocked",
    "tool_permission_denied",
    "agent_plan_invalid"
)
if (Test-Path $errorStorePath) {
    $errorStore = Get-Content -LiteralPath $errorStorePath -Raw
    foreach ($code in $requiredCodes) {
        if ($errorStore -notmatch [regex]::Escape($code)) {
            Add-Error "Frontend error mapping is missing $code."
        }
    }
}
else {
    Add-Error "Missing frontend error store: $errorStorePath"
}

if ($Errors.Count -gt 0) {
    Write-Host "Agent Workbench release-candidate scope guard failed:" -ForegroundColor Red
    foreach ($errorItem in $Errors) {
        Write-Host " - $errorItem" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Agent Workbench release-candidate scope guard passed. Checked $($ChangedFiles.Count) file(s)." -ForegroundColor Green
