[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [Parameter(Mandatory)] [string]$ReportPath,
    [string]$BaselinePath = 'scripts/tests/baselines/aicopilot-mutation.json',
    [string]$OutputPath = 'artifacts/quality/aicopilot-mutation.json',
    [string]$BaseRef = 'origin/main',
    [switch]$UpdateBaseline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Resolve-AICopilotQualityBase.ps1')

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)] [string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Path))
}

function Get-RepositoryRelativePath {
    param([Parameter(Mandatory)] [string]$Path)

    return [IO.Path]::GetRelativePath(
        [IO.Path]::GetFullPath($RepositoryRoot),
        [IO.Path]::GetFullPath($Path)).Replace('\', '/')
}

$resolvedReportPath = Resolve-RepositoryPath $ReportPath
$resolvedBaselinePath = Resolve-RepositoryPath $BaselinePath
$resolvedOutputPath = Resolve-RepositoryPath $OutputPath
$baselineContext = Get-AICopilotBaselineContext `
    -RepositoryRoot $RepositoryRoot `
    -BaseRef $BaseRef `
    -BaselineKind Mutation `
    -BaselinePath $resolvedBaselinePath
$toolManifestPath = Join-Path $RepositoryRoot '.config/dotnet-tools.json'
$strykerConfigPath = Join-Path $RepositoryRoot 'src/tests/AICopilot.InProcessTests/stryker-config.json'

foreach ($requiredPath in @($resolvedReportPath, $toolManifestPath, $strykerConfigPath)) {
    if (-not (Test-Path $requiredPath -PathType Leaf)) {
        throw "Mutation evidence input does not exist: $requiredPath"
    }
}

$manifest = Get-Content $toolManifestPath -Raw | ConvertFrom-Json -Depth 16
$tool = $manifest.tools.'dotnet-stryker'
if ($null -eq $tool -or [string]$tool.version -ne '4.16.0' -or [bool]$tool.rollForward) {
    throw 'Mutation tool must remain dotnet-stryker 4.16.0 with rollForward=false.'
}

$config = (Get-Content $strykerConfigPath -Raw | ConvertFrom-Json -Depth 16).'stryker-config'
$mutate = @($config.mutate)
$reporters = @($config.reporters | ForEach-Object { ([string]$_).ToLowerInvariant() })
if ([string]$config.project -ne 'AICopilot.EntityFrameworkCore.csproj' -or
    [string]$config.'mutation-level' -ne 'Standard' -or
    $mutate.Count -ne 1 -or
    [string]$mutate[0] -ne 'Security/SecretStringEncryptor.cs' -or
    [string]$config.configuration -ne 'Release' -or
    [string]$config.'target-framework' -ne 'net10.0' -or
    -not [bool]$config.'break-on-initial-test-failure' -or
    [int]$config.thresholds.break -ne 0 -or
    -not $reporters.Contains('json') -or
    -not $reporters.Contains('markdown')) {
    throw 'Mutation configuration drifted from the fixed AI-SEC-011/012 secret-protection project/file/level/report contract.'
}

$report = Get-Content $resolvedReportPath -Raw | ConvertFrom-Json -Depth 128
if ([string]$report.schemaVersion -ne '2') {
    throw "Unsupported Stryker report schemaVersion '$($report.schemaVersion)'."
}

$expectedTargetFile = 'src/infrastructure/AICopilot.EntityFrameworkCore/Security/SecretStringEncryptor.cs'
$allowedStatuses = @(
    'Killed',
    'Survived',
    'Timeout',
    'NoCoverage',
    'Ignored',
    'CompileError',
    'RuntimeError'
)
$targetMutants = @()
$unexpectedActiveMutants = @()
$allMutants = @()
$fileSummaries = @()

foreach ($fileProperty in @($report.files.PSObject.Properties)) {
    $relativePath = Get-RepositoryRelativePath ([string]$fileProperty.Name)
    $mutants = @($fileProperty.Value.mutants)
    foreach ($mutant in $mutants) {
        $status = [string]$mutant.status
        if (-not $allowedStatuses.Contains($status)) {
            throw "Mutation report contains unfinished or unknown status '$status' in '$relativePath'."
        }
    }

    $active = @($mutants | Where-Object { $_.status -notin @('Ignored', 'CompileError') })
    if ($relativePath -eq $expectedTargetFile) {
        $targetMutants = $mutants
    }
    elseif ($active.Count -gt 0) {
        $unexpectedActiveMutants += $active
    }

    $allMutants += $mutants
    $fileSummaries += [ordered]@{
        path = $relativePath
        generated = $mutants.Count
        active = $active.Count
    }
}

if ($targetMutants.Count -eq 0) {
    throw "Mutation report contains no mutants for fixed target '$expectedTargetFile'."
}
if ($unexpectedActiveMutants.Count -gt 0) {
    throw "Mutation target drift detected: $($unexpectedActiveMutants.Count) active mutant(s) exist outside '$expectedTargetFile'."
}

$statusCounts = [ordered]@{}
foreach ($status in $allowedStatuses) {
    $statusCounts[$status] = @($targetMutants | Where-Object { $_.status -eq $status }).Count
}
$evaluated = [int]$statusCounts.Killed +
    [int]$statusCounts.Survived +
    [int]$statusCounts.Timeout +
    [int]$statusCounts.NoCoverage +
    [int]$statusCounts.RuntimeError
if ($evaluated -le 0) {
    throw "Mutation report has no evaluated mutants for '$expectedTargetFile'."
}

$detected = [int]$statusCounts.Killed
$mutationScore = [Math]::Round(100.0 * $detected / $evaluated, 6)
$targetMutantIds = @($targetMutants | ForEach-Object {
        if ($null -eq $_.PSObject.Properties['id'] -or
            [string]::IsNullOrWhiteSpace([string]$_.id) -or
            $null -eq $_.PSObject.Properties['mutatorName'] -or
            $null -eq $_.PSObject.Properties['replacement'] -or
            $null -eq $_.PSObject.Properties['location'] -or
            $null -eq $_.location.PSObject.Properties['start'] -or
            $null -eq $_.location.PSObject.Properties['end']) {
            throw 'Mutation report is incomplete: every target mutant requires id/mutatorName/replacement/location.'
        }
        [string]$_.id
    })
if (@($targetMutantIds | Sort-Object -Unique).Count -ne $targetMutants.Count) {
    throw 'Mutation report is incomplete: target mutant ids must be unique.'
}
if ($evaluated + [int]$statusCounts.Ignored + [int]$statusCounts.CompileError -ne $targetMutants.Count) {
    throw 'Mutation report status reconciliation failed for the candidate mutant set.'
}
$evaluatedRate = [Math]::Round(100.0 * $evaluated / $targetMutants.Count, 6)
$survivedRate = [Math]::Round(100.0 * [int]$statusCounts.Survived / $evaluated, 6)
$noCoverageRate = [Math]::Round(100.0 * [int]$statusCounts.NoCoverage / $evaluated, 6)
$timeoutRate = [Math]::Round(100.0 * [int]$statusCounts.Timeout / $evaluated, 6)
$runtimeErrorRate = [Math]::Round(100.0 * [int]$statusCounts.RuntimeError / $evaluated, 6)
$metrics = [ordered]@{
    toolPackage = 'dotnet-stryker'
    toolVersion = [string]$tool.version
    project = [string]$config.project
    targetFile = $expectedTargetFile
    mutationLevel = [string]$config.'mutation-level'
    generatedMutants = $targetMutants.Count
    evaluatedMutants = $evaluated
    evaluatedRate = $evaluatedRate
    mutationScore = $mutationScore
    killed = [int]$statusCounts.Killed
    survived = [int]$statusCounts.Survived
    noCoverage = [int]$statusCounts.NoCoverage
    timeout = [int]$statusCounts.Timeout
    runtimeError = [int]$statusCounts.RuntimeError
    ignored = [int]$statusCounts.Ignored
    compileError = [int]$statusCounts.CompileError
    unexpectedActiveMutants = $unexpectedActiveMutants.Count
    survivedRate = $survivedRate
    noCoverageRate = $noCoverageRate
    timeoutRate = $timeoutRate
    runtimeErrorRate = $runtimeErrorRate
}

$summary = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    reportPath = Get-RepositoryRelativePath $resolvedReportPath
    metrics = $metrics
    files = @($fileSummaries | Sort-Object path)
}
New-Item (Split-Path $resolvedOutputPath -Parent) -ItemType Directory -Force | Out-Null
$summary | ConvertTo-Json -Depth 16 | Set-Content $resolvedOutputPath -Encoding utf8NoBOM

if (-not (Test-Path $resolvedBaselinePath -PathType Leaf)) {
    throw "Mutation baseline does not exist: $resolvedBaselinePath"
}

function ConvertTo-MutationThresholds {
    param(
        [Parameter(Mandatory)] $Baseline,
        [Parameter(Mandatory)] [string]$Label
    )

    if ([string]$Baseline.toolPackage -ne 'dotnet-stryker' -or
        [string]$Baseline.toolVersion -ne [string]$tool.version -or
        [string]$Baseline.project -ne [string]$config.project -or
        [string]$Baseline.targetFile -ne $expectedTargetFile -or
        [string]$Baseline.mutationLevel -ne [string]$config.'mutation-level') {
        throw "$Label mutation baseline drifted from the tool/project/file/level contract."
    }

    if ([int]$Baseline.schemaVersion -ne 2) {
        throw "$Label mutation baseline must use schemaVersion 2."
    }
    foreach ($deprecated in @('expectedGeneratedMutants', 'expectedEvaluatedMutants', 'mutantIdentitySha256')) {
        if ($null -ne $Baseline.PSObject.Properties[$deprecated]) {
            throw "$Label mutation baseline must not freeze candidate source or mutant identity/count via '$deprecated'."
        }
    }

    $thresholdNames = @(
        'minimumMutationScore',
        'minimumEvaluatedRate',
        'maximumSurvivedRate',
        'maximumNoCoverageRate',
        'maximumTimeoutRate',
        'maximumRuntimeErrorRate'
    )
    foreach ($name in $thresholdNames) {
        $property = $Baseline.PSObject.Properties[$name]
        if ($null -eq $property -or
            [double]$property.Value -lt 0 -or
            [double]$property.Value -gt 100 -or
            [double]::IsNaN([double]$property.Value) -or
            [double]::IsInfinity([double]$property.Value)) {
            throw "$Label mutation baseline has invalid percentage threshold '$name'."
        }
    }

    return [pscustomobject]@{
        minimumMutationScore = [double]$Baseline.minimumMutationScore
        minimumEvaluatedRate = [double]$Baseline.minimumEvaluatedRate
        maximumSurvivedRate = [double]$Baseline.maximumSurvivedRate
        maximumNoCoverageRate = [double]$Baseline.maximumNoCoverageRate
        maximumTimeoutRate = [double]$Baseline.maximumTimeoutRate
        maximumRuntimeErrorRate = [double]$Baseline.maximumRuntimeErrorRate
    }
}

$candidateBaseline = Get-Content $resolvedBaselinePath -Raw | ConvertFrom-Json -Depth 16
$candidateThresholds = ConvertTo-MutationThresholds `
    -Baseline $candidateBaseline `
    -Label 'Candidate'

$baseBaseline = if ($baselineContext.Mode -eq 'Ratchet') {
    $baselineContext.BaseBaselineJson | ConvertFrom-Json -Depth 16
} else {
    $candidateBaseline
}
$baseThresholds = ConvertTo-MutationThresholds `
    -Baseline $baseBaseline `
    -Label 'Base'

$minimumThresholdNames = @('minimumMutationScore', 'minimumEvaluatedRate')
$maximumThresholdNames = @(
    'maximumSurvivedRate',
    'maximumNoCoverageRate',
    'maximumTimeoutRate',
    'maximumRuntimeErrorRate'
)
if ($baselineContext.Mode -eq 'Ratchet') {
    foreach ($name in $minimumThresholdNames) {
        if ([double]$candidateThresholds.$name + 0.000001 -lt [double]$baseThresholds.$name) {
            throw "Candidate mutation baseline weakens base threshold '$name'."
        }
    }
    foreach ($name in $maximumThresholdNames) {
        if ([double]$candidateThresholds.$name - 0.000001 -gt [double]$baseThresholds.$name) {
            throw "Candidate mutation baseline weakens base threshold '$name'."
        }
    }
}

if ($baselineContext.Mode -eq 'Bootstrap' -and -not $UpdateBaseline) {
    $actualThresholds = [ordered]@{
        minimumMutationScore = $mutationScore
        minimumEvaluatedRate = $evaluatedRate
        maximumSurvivedRate = $survivedRate
        maximumNoCoverageRate = $noCoverageRate
        maximumTimeoutRate = $timeoutRate
        maximumRuntimeErrorRate = $runtimeErrorRate
    }
    foreach ($name in @($minimumThresholdNames) + @($maximumThresholdNames)) {
        if ([Math]::Abs([double]$candidateThresholds.$name - [double]$actualThresholds[$name]) -gt 0.000001) {
            throw "Initial mutation baseline must exactly reconcile candidate quality '$name': actual=$($actualThresholds[$name]) baseline=$($candidateThresholds.$name)."
        }
    }
}

if (-not ($baselineContext.Mode -eq 'Bootstrap' -and $UpdateBaseline) -and
    ($mutationScore + 0.000001 -lt [double]$candidateThresholds.minimumMutationScore -or
     $evaluatedRate + 0.000001 -lt [double]$candidateThresholds.minimumEvaluatedRate -or
     $survivedRate - 0.000001 -gt [double]$candidateThresholds.maximumSurvivedRate -or
     $noCoverageRate - 0.000001 -gt [double]$candidateThresholds.maximumNoCoverageRate -or
     $timeoutRate - 0.000001 -gt [double]$candidateThresholds.maximumTimeoutRate -or
     $runtimeErrorRate - 0.000001 -gt [double]$candidateThresholds.maximumRuntimeErrorRate)) {
    throw "Mutation quality regressed: score=$mutationScore/$($candidateThresholds.minimumMutationScore), evaluatedRate=$evaluatedRate/$($candidateThresholds.minimumEvaluatedRate), survivedRate=$survivedRate/$($candidateThresholds.maximumSurvivedRate), noCoverageRate=$noCoverageRate/$($candidateThresholds.maximumNoCoverageRate), timeoutRate=$timeoutRate/$($candidateThresholds.maximumTimeoutRate), runtimeErrorRate=$runtimeErrorRate/$($candidateThresholds.maximumRuntimeErrorRate)."
}

if ($UpdateBaseline) {
    $updatedThresholds = if ($baselineContext.Mode -eq 'Bootstrap') {
        [ordered]@{
            minimumMutationScore = $mutationScore
            minimumEvaluatedRate = $evaluatedRate
            maximumSurvivedRate = $survivedRate
            maximumNoCoverageRate = $noCoverageRate
            maximumTimeoutRate = $timeoutRate
            maximumRuntimeErrorRate = $runtimeErrorRate
        }
    }
    else {
        [ordered]@{
            minimumMutationScore = [Math]::Max([double]$candidateThresholds.minimumMutationScore, $mutationScore)
            minimumEvaluatedRate = [Math]::Max([double]$candidateThresholds.minimumEvaluatedRate, $evaluatedRate)
            maximumSurvivedRate = [Math]::Min([double]$candidateThresholds.maximumSurvivedRate, $survivedRate)
            maximumNoCoverageRate = [Math]::Min([double]$candidateThresholds.maximumNoCoverageRate, $noCoverageRate)
            maximumTimeoutRate = [Math]::Min([double]$candidateThresholds.maximumTimeoutRate, $timeoutRate)
            maximumRuntimeErrorRate = [Math]::Min([double]$candidateThresholds.maximumRuntimeErrorRate, $runtimeErrorRate)
        }
    }
    $baseline = [ordered]@{
        schemaVersion = 2
        toolPackage = 'dotnet-stryker'
        toolVersion = [string]$tool.version
        project = [string]$config.project
        targetFile = $expectedTargetFile
        mutationLevel = [string]$config.'mutation-level'
        minimumMutationScore = $updatedThresholds.minimumMutationScore
        minimumEvaluatedRate = $updatedThresholds.minimumEvaluatedRate
        maximumSurvivedRate = $updatedThresholds.maximumSurvivedRate
        maximumNoCoverageRate = $updatedThresholds.maximumNoCoverageRate
        maximumTimeoutRate = $updatedThresholds.maximumTimeoutRate
        maximumRuntimeErrorRate = $updatedThresholds.maximumRuntimeErrorRate
    }
    $baseline | ConvertTo-Json | Set-Content $resolvedBaselinePath -Encoding utf8NoBOM
}

Write-Host "AICopilot mutation passed. target=$expectedTargetFile, mutants=$($targetMutants.Count), evaluated=$evaluated, score=$mutationScore, survived=$($statusCounts.Survived), noCoverage=$($statusCounts.NoCoverage)."
