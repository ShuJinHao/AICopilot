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
    "^src/vues/",
    "^IIoT\.CloudPlatform/",
    "^IIoT\.EdgeClient/",
    "^\.\./IIoT\.CloudPlatform/",
    "^\.\./IIoT\.EdgeClient/"
)

foreach ($file in $ChangedFiles) {
    foreach ($pattern in $forbiddenPathPatterns) {
        if ($file -match $pattern) {
            Add-Error "Forbidden path in Simulation batch: $file"
        }
    }
}

$csharpFiles = $ChangedFiles |
    Where-Object { $_ -like "src/*.cs" -or $_ -like "src/**/*.cs" } |
    Where-Object { Test-Path $_ }

foreach ($file in $csharpFiles) {
    $content = Get-Content -LiteralPath $file -Raw
    if ($content -match "ToolProviderType\.Shell|toolCode\s*=\s*""shell""|cmd\.exe|powershell\.exe|/bin/sh") {
        Add-Error "Shell capability pattern found in $file."
    }
    if ($content -match "\b(select|insert|update|delete|drop|alter|truncate|merge)\b.+\b(from|into|table|set)\b" -and
        $file -notmatch "DataAnalysis|TextToSql|SqlGuardrail|CloudReadonlyAgentTooling|AgentTaskRuntime|ArchitectureTests|BackendTests") {
        Add-Error "Unexpected SQL-like pattern found in $file."
    }
}

$simulationFile = "src/services/AICopilot.AiGatewayService/AgentTasks/CloudReadonlySimulation.cs"
if (Test-Path $simulationFile) {
    $simulationContent = Get-Content -LiteralPath $simulationFile -Raw
    foreach ($required in @("SimulationSourceMode", "SimulationSourceLabel", "isSimulation")) {
        if ($simulationContent -notmatch [regex]::Escape($required)) {
            Add-Error "Simulation marker '$required' is missing from $simulationFile."
        }
    }
}
else {
    Add-Error "Simulation dataset file is missing: $simulationFile"
}

if ($Errors.Count -gt 0) {
    Write-Host "Agent Simulation scope guard failed:" -ForegroundColor Red
    foreach ($errorItem in $Errors) {
        Write-Host " - $errorItem" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Agent Simulation scope guard passed. Checked $($ChangedFiles.Count) candidate file(s)." -ForegroundColor Green
