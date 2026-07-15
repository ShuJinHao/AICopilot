[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [Parameter(Mandatory)] [string]$ReportPath,
    [string]$BaselinePath = 'scripts/tests/baselines/aicopilot-mutation.json',
    [string]$OutputPath = 'artifacts/quality/aicopilot-mutation.json',
    [switch]$UpdateBaseline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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
$toolManifestPath = Join-Path $RepositoryRoot '.config/dotnet-tools.json'
$strykerConfigPath = Join-Path $RepositoryRoot 'src/tests/AICopilot.UnitTests/stryker-config.json'

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
if ([string]$config.project -ne 'AICopilot.SecretProtection.csproj' -or
    [string]$config.'mutation-level' -ne 'Standard' -or
    $mutate.Count -ne 1 -or
    [string]$mutate[0] -ne 'SecretStringEncryptor.cs' -or
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

$expectedTargetFile = 'src/infrastructure/AICopilot.SecretProtection/SecretStringEncryptor.cs'
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
$metrics = [ordered]@{
    toolPackage = 'dotnet-stryker'
    toolVersion = [string]$tool.version
    project = [string]$config.project
    targetFile = $expectedTargetFile
    mutationLevel = [string]$config.'mutation-level'
    generatedMutants = $targetMutants.Count
    evaluatedMutants = $evaluated
    mutationScore = $mutationScore
    killed = [int]$statusCounts.Killed
    survived = [int]$statusCounts.Survived
    noCoverage = [int]$statusCounts.NoCoverage
    timeout = [int]$statusCounts.Timeout
    runtimeError = [int]$statusCounts.RuntimeError
    ignored = [int]$statusCounts.Ignored
    compileError = [int]$statusCounts.CompileError
    unexpectedActiveMutants = $unexpectedActiveMutants.Count
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

if ($UpdateBaseline) {
    $baseline = [ordered]@{
        schemaVersion = 1
        toolPackage = 'dotnet-stryker'
        toolVersion = [string]$tool.version
        project = [string]$config.project
        targetFile = $expectedTargetFile
        mutationLevel = [string]$config.'mutation-level'
        expectedGeneratedMutants = $targetMutants.Count
        expectedEvaluatedMutants = $evaluated
        minimumMutationScore = $mutationScore
        maximumSurvived = [int]$statusCounts.Survived
        maximumNoCoverage = [int]$statusCounts.NoCoverage
        maximumTimeout = [int]$statusCounts.Timeout
        maximumRuntimeError = [int]$statusCounts.RuntimeError
    }
    New-Item (Split-Path $resolvedBaselinePath -Parent) -ItemType Directory -Force | Out-Null
    $baseline | ConvertTo-Json | Set-Content $resolvedBaselinePath -Encoding utf8NoBOM
}

if (-not (Test-Path $resolvedBaselinePath -PathType Leaf)) {
    throw "Mutation baseline does not exist: $resolvedBaselinePath"
}
$expected = Get-Content $resolvedBaselinePath -Raw | ConvertFrom-Json -Depth 16
if ([int]$expected.schemaVersion -ne 1 -or
    [string]$expected.toolPackage -ne 'dotnet-stryker' -or
    [string]$expected.toolVersion -ne [string]$tool.version -or
    [string]$expected.project -ne [string]$config.project -or
    [string]$expected.targetFile -ne $expectedTargetFile -or
    [string]$expected.mutationLevel -ne [string]$config.'mutation-level') {
    throw 'Mutation baseline identity drifted from the fixed tool/project/file/level contract.'
}
if ($targetMutants.Count -ne [int]$expected.expectedGeneratedMutants -or
    $evaluated -ne [int]$expected.expectedEvaluatedMutants) {
    throw "Mutation set changed and requires an explicit reviewed baseline update: generated=$($targetMutants.Count)/$($expected.expectedGeneratedMutants), evaluated=$evaluated/$($expected.expectedEvaluatedMutants)."
}
if ($mutationScore -lt [double]$expected.minimumMutationScore -or
    [int]$statusCounts.Survived -gt [int]$expected.maximumSurvived -or
    [int]$statusCounts.NoCoverage -gt [int]$expected.maximumNoCoverage -or
    [int]$statusCounts.Timeout -gt [int]$expected.maximumTimeout -or
    [int]$statusCounts.RuntimeError -gt [int]$expected.maximumRuntimeError) {
    throw "Mutation quality regressed: score=$mutationScore/$($expected.minimumMutationScore), survived=$($statusCounts.Survived)/$($expected.maximumSurvived), noCoverage=$($statusCounts.NoCoverage)/$($expected.maximumNoCoverage), timeout=$($statusCounts.Timeout)/$($expected.maximumTimeout), runtimeError=$($statusCounts.RuntimeError)/$($expected.maximumRuntimeError)."
}

Write-Host "AICopilot mutation passed. target=$expectedTargetFile, mutants=$($targetMutants.Count), evaluated=$evaluated, score=$mutationScore, survived=$($statusCounts.Survived), noCoverage=$($statusCounts.NoCoverage)."
