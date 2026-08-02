[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [string]$SelectionPath = 'artifacts/ci-selection.json',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ResultsDirectory = 'artifacts/ci-test-results',
    [switch]$CollectCoverage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path $RepositoryRoot).Path
$resolvedSelection = if ([IO.Path]::IsPathRooted($SelectionPath)) {
    [IO.Path]::GetFullPath($SelectionPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $SelectionPath))
}
if (-not (Test-Path $resolvedSelection -PathType Leaf)) {
    throw "CI selection is missing: $resolvedSelection"
}
$selection = Get-Content $resolvedSelection -Raw | ConvertFrom-Json
if ([int]$selection.schemaVersion -ne 3) {
    throw "Unsupported AICopilot CI selection schema: $($selection.schemaVersion)"
}
$projects = @($selection.selectedDotNetProjects)
$mode = [string]$selection.mode
$allowedCategories = @('Architecture', 'Security', 'Business', 'DeploymentContract', 'Quality', 'CrossProject')
$allowedByMode = @{
    Default = @('Architecture', 'Security', 'Business', 'DeploymentContract')
    Deployment = @('Architecture', 'Security', 'DeploymentContract')
    Quality = @('Architecture', 'Security', 'Quality')
    CrossProject = @('CrossProject')
    Full = $allowedCategories
}
if (-not $allowedByMode.ContainsKey($mode)) {
    throw "Unsupported AICopilot CI selection mode: $mode"
}
if ($CollectCoverage -and $mode -notin @('Quality', 'Full')) {
    throw "Coverage is a Quality operation and is forbidden for AICopilot CI mode '$mode'."
}
foreach ($project in $projects) {
    $categories = @($project.categories)
    if ($categories.Count -eq 0 -or @($categories | Where-Object {
                $_ -notin $allowedCategories -or $_ -notin $allowedByMode[$mode]
            }).Count -gt 0) {
        throw "AICopilot CI selection contains invalid categories for mode ${mode}: project=$($project.path) categories=$($categories -join ',')"
    }
    if ($categories -contains 'DeploymentContract' -and -not [bool]$selection.deploymentAffected) {
        throw "AICopilot DeploymentContract selection is not bound to an affected deployment path: $($project.path)"
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$project.testFilter) -and
        $categories -notcontains 'Security') {
        throw "AICopilot filtered selection must be classified as Security: $($project.path)"
    }
}

$resolvedResults = if ([IO.Path]::IsPathRooted($ResultsDirectory)) {
    [IO.Path]::GetFullPath($ResultsDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $ResultsDirectory))
}
[void](New-Item $resolvedResults -ItemType Directory -Force)
$results = [Collections.Generic.List[object]]::new()
$head = ((& git -C $root rev-parse HEAD 2>&1) -join "`n").Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw "AICopilot CI test execution is not bound to a full Git HEAD: $head"
}

function Invoke-DotNetChecked {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$FailureMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

$resolvedProjects = @{}
foreach ($project in $projects) {
    $projectPath = Join-Path $root ([string]$project.path)
    if (-not (Test-Path $projectPath -PathType Leaf)) {
        throw "Selected test project is missing: $($project.path)"
    }
    $resolvedProjects[[string]$project.path] = [IO.Path]::GetFullPath($projectPath)
}

$analyzerFixtureSelected = @($projects | Where-Object {
        [string]$_.projectName -ceq 'AICopilot.AnalyzerFixtureTests'
    }).Count -gt 0
$analyzerProjectRelativePath = 'src/analyzers/AICopilot.Architecture.Analyzers/AICopilot.Architecture.Analyzers.csproj'
$analyzerProjectPath = [IO.Path]::GetFullPath((Join-Path $root $analyzerProjectRelativePath))
if ($analyzerFixtureSelected -and -not (Test-Path $analyzerProjectPath -PathType Leaf)) {
    throw "Selected Analyzer fixture prerequisite is missing: $analyzerProjectRelativePath"
}

$selectedGraphPath = ''
if ($projects.Count -gt 0) {
    $selectedGraphPath = Join-Path $resolvedResults 'AICopilot.CiSelected.slnx'
    $graphProjectPaths = @($projects | ForEach-Object {
            $resolvedProjects[[string]$_.path]
        })
    if ($analyzerFixtureSelected) {
        $graphProjectPaths += $analyzerProjectPath
    }
    $projectElements = @($graphProjectPaths | Sort-Object -Unique | ForEach-Object {
            $relativePath = [IO.Path]::GetRelativePath(
                $resolvedResults, $_).Replace('\', '/')
            "  <Project Path=`"$([Security.SecurityElement]::Escape($relativePath))`" />"
        })
    @(
        '<Solution>'
        $projectElements
        '</Solution>'
    ) | Set-Content $selectedGraphPath -Encoding utf8

    Invoke-DotNetChecked `
        -Arguments @('restore', $selectedGraphPath, '--nologo') `
        -FailureMessage 'Restore failed for the selected AICopilot test project graph.'
    Invoke-DotNetChecked `
        -Arguments @(
            'build',
            $selectedGraphPath,
            '-c', $Configuration,
            '--no-restore',
            '--disable-build-servers',
            '--nologo',
            "-p:SourceRevisionId=$head") `
        -FailureMessage 'Build failed for the selected AICopilot test project graph.'

    if ($analyzerFixtureSelected) {
        $analyzerOutputPath = Join-Path $root (
            "src/analyzers/AICopilot.Architecture.Analyzers/bin/$Configuration/netstandard2.0/AICopilot.Architecture.Analyzers.dll")
        if (-not (Test-Path $analyzerOutputPath -PathType Leaf)) {
            throw "Selected Analyzer fixture graph did not produce the $Configuration Analyzer output: $analyzerOutputPath"
        }
    }
}

$productionBuildRequired = [bool]$selection.productionBuildRequired
$productionGraphPath = Join-Path $root ([string]$selection.productionSolution)
if ($productionBuildRequired) {
    if (-not (Test-Path $productionGraphPath -PathType Leaf)) {
        throw "AICopilot production project graph is missing: $($selection.productionSolution)"
    }
    Invoke-DotNetChecked `
        -Arguments @('restore', $productionGraphPath, '--nologo') `
        -FailureMessage 'Restore failed for the AICopilot production project graph.'
    Invoke-DotNetChecked `
        -Arguments @(
            'build',
            $productionGraphPath,
            '-c', $Configuration,
            '--no-restore',
            '--disable-build-servers',
            '--nologo',
            "-p:SourceRevisionId=$head") `
        -FailureMessage 'Build failed for the AICopilot production project graph.'
}

foreach ($project in $projects) {
    $projectPath = $resolvedProjects[[string]$project.path]
    $projectResults = Join-Path $resolvedResults ([string]$project.projectName)
    [void](New-Item $projectResults -ItemType Directory -Force)
    $listArguments = @(
        'test',
        $projectPath,
        '-c', $Configuration,
        '--no-restore',
        '--no-build',
        '--disable-build-servers',
        '--nologo',
        "-p:SourceRevisionId=$head",
        '--list-tests'
    )
    if (-not [string]::IsNullOrWhiteSpace([string]$project.testFilter)) {
        $listArguments += @('--filter', [string]$project.testFilter)
    }
    $listOutput = @(& dotnet @listArguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Test discovery failed for selected AICopilot project: $($project.path)"
    }
    $listOutput | ForEach-Object { $_.ToString() } |
        Set-Content (Join-Path $projectResults "$($project.projectName).list-tests.txt") -Encoding utf8
    $listedTests = @($listOutput |
        ForEach-Object { $_.ToString().Trim() } |
        Where-Object { $_ -match '^AICopilot\.' })
    if ($listedTests.Count -eq 0) {
        throw "Selected AICopilot test filter discovered zero tests: project=$($project.path) filter=$($project.testFilter)"
    }

    $arguments = @(
        'test',
        $projectPath,
        '-c', $Configuration,
        '--no-restore',
        '--no-build',
        '--disable-build-servers',
        '--nologo',
        "-p:SourceRevisionId=$head",
        '--logger', "trx;LogFileName=$($project.projectName).trx",
        '--results-directory', $projectResults
    )
    if ($CollectCoverage) {
        $arguments += @('--collect', 'XPlat Code Coverage')
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$project.testFilter)) {
        $arguments += @('--filter', [string]$project.testFilter)
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Selected AICopilot test runner failed: $($project.path)"
    }

    $trxFiles = @(Get-ChildItem $projectResults -Filter '*.trx' -File -Recurse)
    if ($trxFiles.Count -ne 1) {
        throw "Expected one TRX for $($project.projectName), found $($trxFiles.Count)."
    }
    [xml]$trx = Get-Content $trxFiles[0].FullName -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $notExecuted = [int]$counters.notExecuted
    if ($total -le 0 -or $total -ne $executed -or $total -ne $passed -or
        $failed -ne 0 -or $notExecuted -ne 0) {
        throw "$($project.projectName) current discovery did not reconcile: discovered=$total executed=$executed passed=$passed failed=$failed skipped=$notExecuted"
    }
    $results.Add([ordered]@{
        projectName = [string]$project.projectName
        projectPath = [string]$project.path
        categories = @($project.categories)
        testFilter = [string]$project.testFilter
        runtime = [string]$project.runtime
        listed = $listedTests.Count
        discovered = $total
        executed = $executed
        passed = $passed
        failed = $failed
        skipped = $notExecuted
        trx = [IO.Path]::GetRelativePath($root, $trxFiles[0].FullName).Replace('\', '/')
    })
}

$selectionChangedFileSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($changedFile in @($selection.changedFiles)) {
    [void]$selectionChangedFileSet.Add($changedFile.ToString())
}
[string[]]$selectionChangedFiles = @($selectionChangedFileSet)
[Array]::Sort($selectionChangedFiles, [StringComparer]::Ordinal)
$selectionScopeBytes = [Text.UTF8Encoding]::new($false).GetBytes(
    [string]::Join("`n", $selectionChangedFiles))
$selectionScopeSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData($selectionScopeBytes)).ToLowerInvariant()
$inventoryPath = Join-Path $resolvedResults 'current-discovery.json'
$discoveredTotal = [int](($results |
        ForEach-Object { [int]$_['discovered'] } |
        Measure-Object -Sum).Sum)
[ordered]@{
    schemaVersion = 3
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    selectionMode = [string]$selection.mode
    sourceRevision = $head
    selectedCategories = @($selection.selectedCategories | Sort-Object -Unique)
    selectionScope = [ordered]@{
        kind = 'changed-files'
        count = $selectionChangedFiles.Count
        sha256 = $selectionScopeSha256
    }
    selectedProjects = $results.Count
    selectedTestGraph = if ([string]::IsNullOrWhiteSpace($selectedGraphPath)) {
        $null
    } else {
        [IO.Path]::GetRelativePath($root, $selectedGraphPath).Replace('\', '/')
    }
    productionBuild = [ordered]@{
        required = $productionBuildRequired
        graph = [string]$selection.productionSolution
    }
    discovered = $discoveredTotal
    projects = $results
} | ConvertTo-Json -Depth 8 | Set-Content $inventoryPath -Encoding utf8

Write-Host "AICOPILOT_CI_TESTS_OK projects=$($results.Count) discovered=$discoveredTotal output=$inventoryPath"
