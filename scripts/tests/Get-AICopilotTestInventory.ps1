[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$OutputPath = 'artifacts/test-inventory.json',
    [string]$BaselinePath = 'scripts/tests/baselines/aicopilot-test-cases.json',
    [string]$ClassificationPath = 'scripts/tests/aicopilot-test-classification.json',
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release',
    [string]$BaseRef = 'origin/main',
    [switch]$UpdateBaseline,
    [switch]$SynchronizeRunnerBuildIdentity,
    [switch]$RunClassificationBehaviorFixture
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$global:LASTEXITCODE = 0
. (Join-Path $PSScriptRoot 'Resolve-AICopilotQualityBase.ps1')
$mergeBase = Resolve-AICopilotQualityBase -RepositoryRoot $RepositoryRoot -BaseRef $BaseRef
$BaselinePath = (Resolve-AICopilotCanonicalBaselinePath `
    -RepositoryRoot $RepositoryRoot `
    -BaselineKind TestCases `
    -BaselinePath $BaselinePath).FullPath

function Get-DirectProperty {
    param(
        [Parameter(Mandatory)] [xml]$ProjectXml,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$ProjectPath
    )

    $nodes = @(
        foreach ($group in @($ProjectXml.Project.PropertyGroup)) {
            @($group.ChildNodes | Where-Object { $_.LocalName -eq $Name })
        }
    )
    if ($nodes.Count -gt 1) {
        throw "$ProjectPath must declare direct $Name at most once; found $($nodes.Count) declarations."
    }
    if ($nodes.Count -eq 1) {
        return ([string]$nodes[0].InnerText).Trim()
    }

    return ''
}

$root = (Resolve-Path $RepositoryRoot).Path
$solutionPath = Join-Path $root 'AICopilot.slnx'
if (-not (Test-Path $solutionPath -PathType Leaf)) {
    throw "AICopilot solution was not found: $solutionPath"
}

$solutionText = (Get-Content $solutionPath -Raw).Replace('\\', '/')
[xml]$solutionXml = Get-Content $solutionPath -Raw
$solutionProjects = @(
    $solutionXml.SelectNodes('//*[local-name()="Project"]') |
        ForEach-Object { ([string]$_.Path).Replace('\\', '/').Trim() }
)
if ($solutionProjects.Count -ne @($solutionProjects | Sort-Object -Unique).Count -or
    @($solutionProjects | Where-Object {
        [string]::IsNullOrWhiteSpace($_) -or
        [System.IO.Path]::IsPathRooted($_) -or
        $_ -match '(^|/)\.\.(/|$)'
    }).Count -ne 0) {
    throw 'AICopilot.slnx contains an empty, duplicate, absolute, or escaping project path.'
}
$allSourceProjects = @(
    Get-ChildItem (Join-Path $root 'src') -Filter '*.csproj' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object { [System.IO.Path]::GetRelativePath($root, $_.FullName).Replace('\\', '/') } |
        Sort-Object
)
if (($solutionProjects | Sort-Object) -join "`n" -cne ($allSourceProjects -join "`n")) {
    $missing = @($allSourceProjects | Where-Object { $_ -notin $solutionProjects })
    $stale = @($solutionProjects | Where-Object { $_ -notin $allSourceProjects })
    throw "AICopilot.slnx must contain every src project exactly once. missing=$($missing -join ','), stale=$($stale -join ',')."
}

$criticalTargetNames = @(
    'Compile', 'ProjectReference', 'PackageReference', 'Analyzer', 'AssemblyName',
    'IsTestProject', 'AICopilotTestRole', 'AICopilotRequired', 'AICopilotTestKind',
    'AICopilotTestRuntime', 'AICopilotTestCadence', 'AICopilotTestOwner', 'RunAnalyzers',
    'RunAnalyzersDuringBuild', 'NoWarn')
$buildDefinitionFiles = @(
    Get-ChildItem $root -File -Recurse -Include '*.csproj', '*.props', '*.targets' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|artifacts)[\\/]' }
)
foreach ($buildDefinitionFile in $buildDefinitionFiles) {
    [xml]$buildXml = Get-Content $buildDefinitionFile.FullName -Raw
    foreach ($node in @($buildXml.SelectNodes('//*[local-name()="Target"]//*'))) {
        if ($node.LocalName -in $criticalTargetNames) {
            $relativeBuildFile = [System.IO.Path]::GetRelativePath($root, $buildDefinitionFile.FullName).Replace('\\', '/')
            throw "$relativeBuildFile mutates security-critical '$($node.LocalName)' inside a Target."
        }
    }
}

$projectFiles = @(
    Get-ChildItem (Join-Path $root 'src/tests'), (Join-Path $root 'src/testing') `
        -Filter '*.csproj' -File -Recurse |
        Sort-Object FullName
)
$inventory = [System.Collections.Generic.List[object]]::new()
$allowedKinds = @(
    'Unit',
    'Aggregate',
    'Application',
    'Workflow',
    'Contract',
    'Conformance',
    'Persistence',
    'Integration',
    'EndToEnd',
    'Deployment',
    'GoldenEval',
    'Architecture'
)
$allowedRuntimes = @('Pure', 'InProcess', 'Filesystem', 'Postgres', 'Aspire', 'LiveExternal')
$allowedCadences = @('PR', 'Manual')
$allowedCapabilities = @(
    'Platform', 'AgentWorkflow', 'RAG', 'Tooling', 'IdentitySecurity',
    'CloudReadOnly', 'Persistence', 'Frontend', 'Deployment'
)
$allowedConcerns = @('Functional', 'Security', 'Reliability', 'Compatibility', 'Accessibility', 'Performance')
$allowedProfiles = @('Default', 'Simulation', 'GoldenDataset', 'LiveExternal')
$allowedRisks = @('P0', 'P1', 'P2')
$allowedRuntimeDependencies = @(
    'Filesystem', 'Docker', 'Aspire', 'Postgres', 'RabbitMQ', 'Redis', 'Qdrant', 'LiveExternal')
$supportProjectAllowlist = @(
    'src/testing/AICopilot.AgentWorkflowTestKit/AICopilot.AgentWorkflowTestKit.csproj',
    'src/testing/AICopilot.AspireIntegrationTestKit/AICopilot.AspireIntegrationTestKit.csproj',
    'src/testing/AICopilot.FilesystemTestKit/AICopilot.FilesystemTestKit.csproj',
    'src/testing/AICopilot.PersistenceTestKit/AICopilot.PersistenceTestKit.csproj',
    'src/testing/AICopilot.ToolPluginTestKit/AICopilot.ToolPluginTestKit.csproj'
)
$nonRequiredRunnerAllowlist = @{
    'AICopilot.CloudAiReadLiveTests' = 'Contract|LiveExternal|Manual|LiveExternal'
    'AICopilot.SimulationTests' = 'Application|Pure|Manual|Simulation'
    'AICopilot.SimulationDockerTests' = 'EndToEnd|Aspire|Manual|Simulation'
}
$inProcessRunnerAllowlist = @{
    'AICopilot.InProcessTests' = 'Integration|InProcessBoundary'
}

$resolvedClassificationPath = if ([System.IO.Path]::IsPathRooted($ClassificationPath)) {
    $ClassificationPath
} else {
    Join-Path $root $ClassificationPath
}
if (-not (Test-Path $resolvedClassificationPath -PathType Leaf)) {
    throw "Test classification ledger is missing: $resolvedClassificationPath"
}
$classificationDocument = Get-Content $resolvedClassificationPath -Raw | ConvertFrom-Json
if ([int]$classificationDocument.schemaVersion -ne 2) {
    throw "Unsupported test classification schemaVersion='$($classificationDocument.schemaVersion)'."
}
$classificationByProject = @{}
$classificationOverrideIds = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
$classificationOverrideSelectors = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($classificationProject in @($classificationDocument.projects)) {
    $classificationProjectName = [string]$classificationProject.projectName
    if ([string]::IsNullOrWhiteSpace($classificationProjectName) -or
        $classificationByProject.ContainsKey($classificationProjectName)) {
        throw "Test classification ledger has an empty or duplicate projectName '$classificationProjectName'."
    }
    if ($null -eq $classificationProject.defaults) {
        throw "$classificationProjectName is missing explicit runner defaults in the test classification ledger."
    }
    $classificationByProject[$classificationProjectName] = $classificationProject
    $classificationOverridesProperty = $classificationProject.PSObject.Properties['overrides']
    foreach ($override in @(
        if ($null -ne $classificationOverridesProperty) { $classificationOverridesProperty.Value }
    )) {
        $overrideId = "$classificationProjectName::$([string]$override.matchId)"
        if ([string]::IsNullOrWhiteSpace([string]$override.matchId) -or
            -not $classificationOverrideIds.Add($overrideId)) {
            throw "Test classification ledger has an empty or duplicate override id '$overrideId'."
        }
        if ([string]::IsNullOrWhiteSpace([string]$override.class)) {
            throw "$overrideId must declare an exact test class."
        }
        $methodProperty = $override.PSObject.Properties['method']
        $caseProperty = $override.PSObject.Properties['case']
        $method = if ($null -eq $methodProperty) { '' } else { [string]$methodProperty.Value }
        $case = if ($null -eq $caseProperty) { '' } else { [string]$caseProperty.Value }
        $selector = "$classificationProjectName::$([string]$override.class)::$method::$case"
        if (-not $classificationOverrideSelectors.Add($selector)) {
            throw "$overrideId duplicates an exact classification selector '$selector'."
        }
        if ((-not [string]::IsNullOrWhiteSpace($method) -or
            -not [string]::IsNullOrWhiteSpace($case)) -and
            $null -ne $override.PSObject.Properties['testKind']) {
            throw "$overrideId is an exact method/case override and must inherit class-level testKind."
        }
    }
}

function Assert-ClassificationDimensions {
    param(
        [Parameter(Mandatory)] [object]$Dimensions,
        [Parameter(Mandatory)] [string]$Context,
        [Parameter(Mandatory)] [string]$ExpectedProfile,
        [switch]$RequireRuntimeDependencies
    )

    foreach ($name in @('testKind', 'capability', 'concern', 'profile', 'risk', 'ruleId', 'regressionId')) {
        $property = $Dimensions.PSObject.Properties[$name]
        if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "$Context is missing explicit classification '$name'."
        }
    }
    if ([string]$Dimensions.testKind -notin $allowedKinds) {
        throw "$Context has invalid testKind='$($Dimensions.testKind)'."
    }
    if ([string]$Dimensions.capability -notin $allowedCapabilities) {
        throw "$Context has invalid capability='$($Dimensions.capability)'."
    }
    if ([string]$Dimensions.concern -notin $allowedConcerns) {
        throw "$Context has invalid concern='$($Dimensions.concern)'."
    }
    if ([string]$Dimensions.profile -notin $allowedProfiles -or
        [string]$Dimensions.profile -cne $ExpectedProfile) {
        throw "$Context has profile='$($Dimensions.profile)' but the runner requires '$ExpectedProfile'."
    }
    if ([string]$Dimensions.risk -notin $allowedRisks) {
        throw "$Context has invalid risk='$($Dimensions.risk)'."
    }
    if ($RequireRuntimeDependencies) {
        $runtimeDependenciesProperty = $Dimensions.PSObject.Properties['runtimeDependencies']
        if ($null -eq $runtimeDependenciesProperty) {
            throw "$Context is missing explicit runtimeDependencies."
        }
        if ($null -eq $runtimeDependenciesProperty.Value -or
            $runtimeDependenciesProperty.Value -is [string] -or
            $runtimeDependenciesProperty.Value -isnot [System.Collections.IEnumerable]) {
            throw "$Context runtimeDependencies must be an explicit JSON array."
        }
        $runtimeDependencies = @($runtimeDependenciesProperty.Value)
        $invalidRuntimeDependencies = @(
            $runtimeDependencies | Where-Object { [string]$_ -notin $allowedRuntimeDependencies }
        )
        if ($invalidRuntimeDependencies.Count -ne 0 -or
            @($runtimeDependencies | Group-Object | Where-Object Count -gt 1).Count -ne 0) {
            throw "$Context has invalid or duplicate runtimeDependencies."
        }
    }
}

function Resolve-CaseRuntimeDependencies {
    param(
        [Parameter(Mandatory)] [object]$SelectedDimensions,
        [Parameter(Mandatory)] [object]$DefaultDimensions,
        [Parameter(Mandatory)] [string]$Context
    )

    $selectedProperty = $SelectedDimensions.PSObject.Properties['runtimeDependencies']
    $resolved = @(
        if ($null -ne $selectedProperty) {
            @($selectedProperty.Value)
        }
        else {
            @($DefaultDimensions.runtimeDependencies)
        }
    )
    $invalid = @($resolved | Where-Object { [string]$_ -notin $allowedRuntimeDependencies })
    if ($invalid.Count -ne 0 -or
        @($resolved | Group-Object | Where-Object Count -gt 1).Count -ne 0) {
        throw "$Context has invalid or duplicate runtimeDependencies."
    }

    return $resolved
}

function Merge-ClassificationDimensions {
    param(
        [Parameter(Mandatory)] [object]$Defaults,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$Overrides
    )

    $merged = [ordered]@{}
    foreach ($property in $Defaults.PSObject.Properties) {
        $merged[$property.Name] = $property.Value
    }
    foreach ($override in $Overrides) {
        foreach ($property in $override.PSObject.Properties) {
            if ($property.Name -notin @('matchId', 'class', 'method', 'case')) {
                $merged[$property.Name] = $property.Value
            }
        }
    }

    return [pscustomobject]$merged
}

function Assert-InProcessClassificationCoverage {
    param(
        [Parameter(Mandatory)] [object]$ClassificationProject,
        [Parameter(Mandatory)] [string[]]$DiscoveredClasses,
        [Parameter(Mandatory)] [string]$Context
    )

    $overridesProperty = $ClassificationProject.PSObject.Properties['overrides']
    $classOverrides = @(
        foreach ($override in @(
            if ($null -ne $overridesProperty) { $overridesProperty.Value }
        )) {
            $methodProperty = $override.PSObject.Properties['method']
            $caseProperty = $override.PSObject.Properties['case']
            if (($null -eq $methodProperty -or
                 [string]::IsNullOrWhiteSpace([string]$methodProperty.Value)) -and
                ($null -eq $caseProperty -or
                 [string]::IsNullOrWhiteSpace([string]$caseProperty.Value))) {
                $override
            }
        }
    )
    foreach ($className in @($DiscoveredClasses | Sort-Object -Unique)) {
        $matching = @($classOverrides | Where-Object { [string]$_.class -ceq $className })
        if ($matching.Count -ne 1) {
            throw "$Context discovered class '$className' requires exactly one class-level classification override."
        }
        if ($null -eq $matching[0].PSObject.Properties['testKind'] -or
            [string]::IsNullOrWhiteSpace([string]$matching[0].testKind)) {
            throw "$Context class '$className' override must declare testKind."
        }
    }
    $stale = @(
        $classOverrides | Where-Object { [string]$_.class -notin $DiscoveredClasses }
    )
    if ($stale.Count -ne 0) {
        throw "$Context contains stale class-level classification overrides: $(@($stale | ForEach-Object class) -join ', ')."
    }
}

function Assert-ClassificationFixtureFails {
    param(
        [Parameter(Mandatory)] [scriptblock]$Action,
        [Parameter(Mandatory)] [string]$ExpectedPattern
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch $ExpectedPattern) {
            throw "Classification behavior fixture expected '$ExpectedPattern' but got: $($_.Exception.Message)"
        }
        return
    }
    throw "Classification behavior fixture expected failure '$ExpectedPattern' but succeeded."
}

if ($RunClassificationBehaviorFixture) {
    $resourceDefaults = [pscustomobject]@{
        runtimeDependencies = @('Docker', 'Aspire', 'Postgres')
    }
    $explicitInProcessOverride = [pscustomobject]@{
        runtimeDependencies = @()
    }
    $inheritedOverride = [pscustomobject]@{}
    if (@(Resolve-CaseRuntimeDependencies `
            -SelectedDimensions $explicitInProcessOverride `
            -DefaultDimensions $resourceDefaults `
            -Context 'explicit empty override').Count -ne 0) {
        throw 'An explicit empty case override inherited external runtime dependencies.'
    }
    $inherited = @(Resolve-CaseRuntimeDependencies `
        -SelectedDimensions $inheritedOverride `
        -DefaultDimensions $resourceDefaults `
        -Context 'fallback override')
    if (($inherited -join ',') -cne 'Docker,Aspire,Postgres') {
        throw 'A case override without runtimeDependencies did not inherit runner defaults.'
    }

    $mixedDefaults = [pscustomobject]@{
        testKind = 'Integration'
        capability = 'Platform'
        runtimeDependencies = @('Docker', 'Aspire', 'Postgres')
    }
    $mergedDefaultsOnly = Merge-ClassificationDimensions `
        -Defaults $mixedDefaults `
        -Overrides @()
    if ([string]$mergedDefaultsOnly.testKind -cne 'Integration' -or
        [string]$mergedDefaultsOnly.capability -cne 'Platform' -or
        ($mergedDefaultsOnly.runtimeDependencies -join ',') -cne 'Docker,Aspire,Postgres') {
        throw 'A runner without classification overrides did not preserve its defaults.'
    }
    $unitClass = [pscustomobject]@{
        matchId = 'unit-class'
        class = 'Fixture.InProcessUnitTests'
        testKind = 'Unit'
        capability = 'Persistence'
        runtimeDependencies = @()
    }
    $exactMethod = [pscustomobject]@{
        matchId = 'exact-method'
        class = 'Fixture.InProcessUnitTests'
        method = 'ExactCase'
        capability = 'IdentitySecurity'
    }
    $mergedClass = Merge-ClassificationDimensions -Defaults $mixedDefaults -Overrides @($unitClass)
    if ([string]$mergedClass.testKind -cne 'Unit' -or
        @($mergedClass.runtimeDependencies).Count -ne 0) {
        throw 'An InProcess class override did not replace runner testKind/runtimeDependencies.'
    }
    $mergedMethod = Merge-ClassificationDimensions `
        -Defaults $mixedDefaults `
        -Overrides @($unitClass, $exactMethod)
    if ([string]$mergedMethod.testKind -cne 'Unit' -or
        [string]$mergedMethod.capability -cne 'IdentitySecurity' -or
        @($mergedMethod.runtimeDependencies).Count -ne 0) {
        throw 'An exact method override did not inherit class testKind/runtimeDependencies while replacing its exact dimension.'
    }

    $resourceClass = [pscustomobject]@{
        matchId = 'resource-class'
        class = 'Fixture.ResourceBoundaryTests'
        testKind = 'Integration'
        runtimeDependencies = @('Docker', 'Aspire', 'Postgres')
    }
    $exactEmptyDependencies = [pscustomobject]@{
        matchId = 'exact-empty'
        class = 'Fixture.ResourceBoundaryTests'
        method = 'NoResourceCase'
        runtimeDependencies = @()
    }
    $exactInheritDependencies = [pscustomobject]@{
        matchId = 'exact-inherit'
        class = 'Fixture.ResourceBoundaryTests'
        method = 'InheritedResourceCase'
    }
    $exactEmptyMerged = Merge-ClassificationDimensions `
        -Defaults $mixedDefaults `
        -Overrides @($resourceClass, $exactEmptyDependencies)
    if (@($exactEmptyMerged.runtimeDependencies).Count -ne 0) {
        throw 'An exact method runtimeDependencies=[] override did not replace class/default external dependencies.'
    }
    $exactInheritedMerged = Merge-ClassificationDimensions `
        -Defaults $mixedDefaults `
        -Overrides @($resourceClass, $exactInheritDependencies)
    if (($exactInheritedMerged.runtimeDependencies -join ',') -cne 'Docker,Aspire,Postgres') {
        throw 'An exact method override without runtimeDependencies did not inherit its class dependencies.'
    }

    $validMixedClassification = [pscustomobject]@{
        overrides = @(
            [pscustomobject]@{
                matchId = 'valid-unit'
                class = 'Fixture.ValidUnitTests'
                testKind = 'Unit'
                runtimeDependencies = @()
            },
            [pscustomobject]@{
                matchId = 'valid-contract'
                class = 'Fixture.ValidContractTests'
                testKind = 'Contract'
                runtimeDependencies = @()
            }
        )
    }
    Assert-InProcessClassificationCoverage `
        -ClassificationProject $validMixedClassification `
        -DiscoveredClasses @('Fixture.ValidUnitTests', 'Fixture.ValidContractTests') `
        -Context 'valid mixed-kind InProcess fixture'

    $missingKindClassification = [pscustomobject]@{
        overrides = @([pscustomobject]@{
            matchId = 'missing-kind'
            class = 'Fixture.MissingKindTests'
            runtimeDependencies = @()
        })
    }
    Assert-ClassificationFixtureFails {
        Assert-InProcessClassificationCoverage `
            -ClassificationProject $missingKindClassification `
            -DiscoveredClasses @('Fixture.MissingKindTests') `
            -Context 'missing-kind fixture'
    } 'must declare testKind'

    $duplicateClassClassification = [pscustomobject]@{
        overrides = @(
            [pscustomobject]@{ matchId = 'duplicate-a'; class = 'Fixture.DuplicateTests'; testKind = 'Unit' },
            [pscustomobject]@{ matchId = 'duplicate-b'; class = 'Fixture.DuplicateTests'; testKind = 'Contract' }
        )
    }
    Assert-ClassificationFixtureFails {
        Assert-InProcessClassificationCoverage `
            -ClassificationProject $duplicateClassClassification `
            -DiscoveredClasses @('Fixture.DuplicateTests') `
            -Context 'duplicate-class fixture'
    } 'requires exactly one class-level classification override'

    $staleClassClassification = [pscustomobject]@{
        overrides = @(
            [pscustomobject]@{ matchId = 'valid'; class = 'Fixture.CurrentTests'; testKind = 'Unit' },
            [pscustomobject]@{ matchId = 'stale'; class = 'Fixture.RemovedTests'; testKind = 'Unit' }
        )
    }
    Assert-ClassificationFixtureFails {
        Assert-InProcessClassificationCoverage `
            -ClassificationProject $staleClassClassification `
            -DiscoveredClasses @('Fixture.CurrentTests') `
            -Context 'stale-class fixture'
    } 'contains stale class-level classification overrides'

    Write-Host 'AICopilot classification behavior fixture passed: mixed InProcess testKind merge and runtime dependency precedence are deterministic.'
    return
}

$projectEvaluationCache = @{}

function Invoke-MSBuildProjectEvaluation {
    param(
        [Parameter(Mandatory)] [string]$ProjectPath,
        [string]$TargetFramework = '',
        [bool]$DesignTimeBuild = $false
    )

    $arguments = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in @(
        'msbuild',
        $ProjectPath,
        '-nologo',
        '-verbosity:quiet',
        '-getProperty:TargetFramework',
        '-getProperty:TargetFrameworks',
        '-getProperty:AssemblyName',
        '-getProperty:TargetPath',
        '-getProperty:DebugType',
        '-getProperty:IsTestProject',
        '-getProperty:AICopilotTestRole',
        '-getProperty:AICopilotRequired',
        '-getProperty:AICopilotTestKind',
        '-getProperty:AICopilotTestRuntime',
        '-getProperty:AICopilotTestCadence',
        '-getProperty:AICopilotTestOwner',
        '-getProperty:RunAnalyzers',
        '-getProperty:RunAnalyzersDuringBuild',
        '-getProperty:NoWarn',
        '-getItem:Compile',
        '-getItem:ProjectReference',
        '-getItem:PackageReference',
        '-getItem:Analyzer',
        "-property:Configuration=$Configuration",
        "-property:DesignTimeBuild=$($DesignTimeBuild.ToString().ToLowerInvariant())",
        '-property:BuildProjectReferences=false'
    )) {
        $arguments.Add($argument)
    }
    if (-not [string]::IsNullOrWhiteSpace($TargetFramework)) {
        $arguments.Add("-property:TargetFramework=$TargetFramework")
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "dotnet msbuild could not start for $ProjectPath."
        }
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "MSBuild evaluation failed for $ProjectPath (exit=$($process.ExitCode)): $($standardError.Trim()) $($standardOutput.Trim())"
        }
    }
    finally {
        $process.Dispose()
    }

    if ([string]::IsNullOrWhiteSpace($standardOutput)) {
        throw "MSBuild evaluation returned no JSON for $ProjectPath."
    }

    try {
        return $standardOutput | ConvertFrom-Json -Depth 64
    }
    catch {
        throw "MSBuild evaluation returned invalid JSON for ${ProjectPath}: $($_.Exception.Message)"
    }
}

function Test-PathInsideRepository {
    param([Parameter(Mandatory)] [string]$Path)

    $relative = [System.IO.Path]::GetRelativePath($root, [System.IO.Path]::GetFullPath($Path))
    return -not [System.IO.Path]::IsPathRooted($relative) -and
        $relative -ne '..' -and
        -not $relative.StartsWith("..$([System.IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal)
}

function Get-EvaluationProperty {
    param(
        [Parameter(Mandatory)] [object]$Evaluation,
        [Parameter(Mandatory)] [string]$Name
    )

    $property = $Evaluation.Properties.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return ''
    }
    return [string]$property.Value
}

function Get-EvaluationItems {
    param(
        [Parameter(Mandatory)] [object]$Evaluation,
        [Parameter(Mandatory)] [string]$Name
    )

    $itemsProperty = $Evaluation.PSObject.Properties['Items']
    if ($null -eq $itemsProperty) {
        throw 'MSBuild evaluation JSON is missing Items.'
    }
    $property = $itemsProperty.Value.PSObject.Properties[$Name]
    return @(
        if ($null -ne $property) { $property.Value }
    )
}

function Get-ItemMetadataValue {
    param(
        [Parameter(Mandatory)] [object]$Item,
        [Parameter(Mandatory)] [string]$Name
    )
    $property = $Item.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return ''
    }
    return [string]$property.Value
}

function ConvertTo-EvaluationSecuritySnapshot {
    param(
        [Parameter(Mandatory)] [object]$Evaluation,
        [Parameter(Mandatory)] [string]$ProjectPath
    )

    $propertyNames = @(
        'TargetFramework', 'TargetFrameworks', 'AssemblyName', 'IsTestProject',
        'TargetPath', 'DebugType',
        'AICopilotTestRole', 'AICopilotRequired', 'AICopilotTestKind',
        'AICopilotTestRuntime', 'AICopilotTestCadence', 'AICopilotTestOwner', 'RunAnalyzers',
        'RunAnalyzersDuringBuild', 'NoWarn')
    $properties = [ordered]@{}
    foreach ($name in $propertyNames) {
        $properties[$name] = Get-EvaluationProperty $Evaluation $name
    }

    $projectReferences = @(
        foreach ($item in @(Get-EvaluationItems $Evaluation 'ProjectReference')) {
            $fullPath = Get-ItemMetadataValue $item 'FullPath'
            if ([string]::IsNullOrWhiteSpace($fullPath) -or
                -not [System.IO.Path]::IsPathRooted($fullPath) -or
                -not (Test-PathInsideRepository $fullPath) -or
                -not (Test-Path $fullPath -PathType Leaf)) {
                throw "$ProjectPath has an external, missing, or non-absolute ProjectReference '$fullPath'."
            }
            [pscustomobject]@{
                path = [System.IO.Path]::GetFullPath($fullPath)
                outputItemType = Get-ItemMetadataValue $item 'OutputItemType'
                referenceOutputAssembly = Get-ItemMetadataValue $item 'ReferenceOutputAssembly'
                privateAssets = Get-ItemMetadataValue $item 'PrivateAssets'
            }
        }
    ) | Sort-Object path, outputItemType, referenceOutputAssembly, privateAssets

    $compile = @(
        foreach ($item in @(Get-EvaluationItems $Evaluation 'Compile')) {
            $fullPath = Get-ItemMetadataValue $item 'FullPath'
            $link = Get-ItemMetadataValue $item 'Link'
            if (-not [string]::IsNullOrWhiteSpace($link)) {
                throw "$ProjectPath uses forbidden linked Compile item '$link'."
            }
            if ([string]::IsNullOrWhiteSpace($fullPath) -or
                -not [System.IO.Path]::IsPathRooted($fullPath) -or
                -not (Test-PathInsideRepository $fullPath) -or
                -not (Test-Path $fullPath -PathType Leaf)) {
                throw "$ProjectPath has an external, missing, or non-absolute Compile item '$fullPath'."
            }
            [System.IO.Path]::GetFullPath($fullPath)
        }
    ) | Sort-Object -Unique

    $packages = @(
        foreach ($item in @(Get-EvaluationItems $Evaluation 'PackageReference')) {
            $identity = Get-ItemMetadataValue $item 'Identity'
            if ([string]::IsNullOrWhiteSpace($identity)) {
                throw "$ProjectPath has a PackageReference without Identity."
            }
            "$identity|$(Get-ItemMetadataValue $item 'Version')"
        }
    ) | Sort-Object -Unique

    $analyzers = @(
        foreach ($item in @(Get-EvaluationItems $Evaluation 'Analyzer')) {
            $fullPath = Get-ItemMetadataValue $item 'FullPath'
            if ([string]::IsNullOrWhiteSpace($fullPath) -or -not [System.IO.Path]::IsPathRooted($fullPath)) {
                throw "$ProjectPath has an Analyzer without an absolute FullPath."
            }
            [System.IO.Path]::GetFullPath($fullPath)
        }
    ) | Sort-Object -Unique

    return [pscustomobject]@{
        Properties = [pscustomobject]$properties
        ProjectReferences = @($projectReferences)
        Compile = @($compile)
        PackageReferences = @($packages)
        Analyzers = @($analyzers)
    }
}

function Get-EvaluatedProjectDefinition {
    param([Parameter(Mandatory)] [string]$ProjectPath)

    $fullProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
    if (-not (Test-Path $fullProjectPath -PathType Leaf)) {
        throw "Cannot evaluate missing project: $fullProjectPath"
    }
    if ($projectEvaluationCache.ContainsKey($fullProjectPath)) {
        return $projectEvaluationCache[$fullProjectPath]
    }

    if (-not (Test-PathInsideRepository $fullProjectPath)) {
        throw "Project evaluation escaped the repository: $fullProjectPath"
    }

    $outerEvaluation = Invoke-MSBuildProjectEvaluation $fullProjectPath -DesignTimeBuild $false
    $targetFrameworks = @(
        (Get-EvaluationProperty $outerEvaluation 'TargetFrameworks').Split(
            ';', [StringSplitOptions]::RemoveEmptyEntries) |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($targetFrameworks.Count -eq 0) {
        $singleTargetFramework = (Get-EvaluationProperty $outerEvaluation 'TargetFramework').Trim()
        if ([string]::IsNullOrWhiteSpace($singleTargetFramework)) {
            throw "$fullProjectPath has no evaluated TargetFramework/TargetFrameworks and cannot be audited."
        }
        $targetFrameworks = @($singleTargetFramework)
    }

    $realSnapshots = [System.Collections.Generic.List[object]]::new()
    foreach ($targetFramework in $targetFrameworks) {
        $real = ConvertTo-EvaluationSecuritySnapshot `
            (Invoke-MSBuildProjectEvaluation $fullProjectPath $targetFramework $false) $fullProjectPath
        $design = ConvertTo-EvaluationSecuritySnapshot `
            (Invoke-MSBuildProjectEvaluation $fullProjectPath $targetFramework $true) $fullProjectPath
        if (($real | ConvertTo-Json -Depth 12 -Compress) -cne
            ($design | ConvertTo-Json -Depth 12 -Compress)) {
            throw "$fullProjectPath changes security-critical properties/items under DesignTimeBuild for '$targetFramework'."
        }
        $realSnapshots.Add($real)
    }

    $securityPropertyNames = @(
        'AssemblyName', 'IsTestProject', 'AICopilotTestRole', 'AICopilotRequired',
        'AICopilotTestKind', 'AICopilotTestRuntime', 'AICopilotTestCadence',
        'AICopilotTestOwner',
        'RunAnalyzers', 'RunAnalyzersDuringBuild', 'NoWarn')
    foreach ($name in $securityPropertyNames) {
        $values = @($realSnapshots | ForEach-Object { [string]$_.Properties.$name } | Sort-Object -Unique)
        if ($values.Count -ne 1) {
            throw "$fullProjectPath mutates evaluated $name across target frameworks."
        }
    }

    $allProjectReferenceDefinitions = @($realSnapshots | ForEach-Object { $_.ProjectReferences })
    $allPackages = @($realSnapshots | ForEach-Object { $_.PackageReferences })
    $allCompile = @($realSnapshots | ForEach-Object { $_.Compile })
    $allAnalyzers = @($realSnapshots | ForEach-Object { $_.Analyzers })
    $definition = [pscustomobject]@{
        TargetFrameworks = @($targetFrameworks)
        Properties = $realSnapshots[0].Properties
        ProjectReferences = @($allProjectReferenceDefinitions | ForEach-Object path | Sort-Object -Unique)
        ProjectReferenceDefinitions = @($allProjectReferenceDefinitions | Sort-Object path, outputItemType, referenceOutputAssembly, privateAssets -Unique)
        PackageReferences = @($allPackages | ForEach-Object { ($_ -split '\|', 2)[0] } | Sort-Object -Unique)
        Compile = @($allCompile | Sort-Object -Unique)
        Analyzers = @($allAnalyzers | Sort-Object -Unique)
    }
    $projectEvaluationCache[$fullProjectPath] = $definition
    return $definition
}

function Get-EvaluatedProjectReferences {
    param([Parameter(Mandatory)] [string]$ProjectPath)
    return @((Get-EvaluatedProjectDefinition $ProjectPath).ProjectReferences)
}

function Get-EvaluatedPackageReferences {
    param([Parameter(Mandatory)] [string]$ProjectPath)
    return @((Get-EvaluatedProjectDefinition $ProjectPath).PackageReferences)
}

foreach ($projectFile in $projectFiles) {
    [xml]$projectXml = Get-Content $projectFile.FullName -Raw
    $relativePath = [System.IO.Path]::GetRelativePath($root, $projectFile.FullName).Replace('\\', '/')
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
    $isTestProjectText = Get-DirectProperty $projectXml 'IsTestProject' $relativePath
    $isTestProject = $isTestProjectText -ieq 'true'
    $role = Get-DirectProperty $projectXml 'AICopilotTestRole' $relativePath
    $evaluatedDefinition = Get-EvaluatedProjectDefinition $projectFile.FullName
    if ([string]$evaluatedDefinition.Properties.AssemblyName -cne $projectName) {
        throw "$relativePath evaluated AssemblyName='$($evaluatedDefinition.Properties.AssemblyName)' but must equal '$projectName'."
    }
    if ([string]$evaluatedDefinition.Properties.IsTestProject -ine $isTestProjectText -or
        [string]$evaluatedDefinition.Properties.AICopilotTestRole -cne $role) {
        throw "$relativePath direct and evaluated IsTestProject/AICopilotTestRole differ."
    }

    if (-not $isTestProject -and $role -ne 'Support') {
        throw "$relativePath must declare either IsTestProject=true or AICopilotTestRole=Support."
    }

    if ($solutionText -notmatch [regex]::Escape("Path=`"$relativePath`"")) {
        throw "$relativePath is not included in AICopilot.slnx."
    }

    if ($role -eq 'Support') {
        if ($relativePath -notin $supportProjectAllowlist) {
            throw "$relativePath declares Support but is not in the fixed support-project allowlist."
        }

        if ($isTestProjectText -ine 'false') {
            throw "$relativePath is Support and must directly declare IsTestProject=false."
        }

        $supportOwner = Get-DirectProperty $projectXml 'AICopilotTestOwner' $relativePath
        $supportConsumersText = Get-DirectProperty $projectXml 'AICopilotTestConsumers' $relativePath
        if ([string]::IsNullOrWhiteSpace($supportOwner) -or
            [string]::IsNullOrWhiteSpace($supportConsumersText)) {
            throw "$relativePath is Support and must declare one owner plus its complete consumer list."
        }
        $supportConsumers = @(
            $supportConsumersText.Split(';', [StringSplitOptions]::RemoveEmptyEntries) |
                ForEach-Object { $_.Trim() }
        )
        if ($supportConsumers.Count -eq 0 -or
            @($supportConsumers | Group-Object | Where-Object Count -gt 1).Count -ne 0) {
            throw "$relativePath has an empty or duplicate AICopilotTestConsumers list."
        }

        $packageIds = @(Get-EvaluatedPackageReferences $projectFile.FullName)
        $forbiddenTestPackages = @($packageIds | Where-Object {
            $_ -ieq 'Microsoft.NET.Test.Sdk' -or
            $_ -ieq 'Microsoft.Testing.Platform' -or
            $_ -match '(?i)(^|\.)(xunit|nunit|mstest)(\.|$)' -or
            $_ -match '(?i)^(FluentAssertions|Shouldly)$'
        })
        if ($forbiddenTestPackages.Count -ne 0) {
            throw "$relativePath is Support and must not reference test SDK/framework packages: $($forbiddenTestPackages -join ', ')."
        }

        $supportSources = @(
            Get-ChildItem $projectFile.Directory.FullName -Filter '*.cs' -File -Recurse |
                Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        )
        foreach ($source in $supportSources) {
            if ((Get-Content $source.FullName -Raw) -match '(?m)\[\s*(?:Xunit\.)?(?:Fact|Theory)(?:Attribute)?\s*(?:\(|\])') {
                throw "$relativePath is Support and must not declare Fact/Theory tests; found in $($source.FullName)."
            }
        }

        $inventory.Add([pscustomobject]@{
            projectName = $projectName
            path = $relativePath
            role = 'Support'
            kind = $null
            runtime = $null
            cadence = $null
            owner = $supportOwner
            consumers = @($supportConsumers)
            required = $false
        })
        continue
    }

    $kind = Get-DirectProperty $projectXml 'AICopilotTestKind' $relativePath
    $runtime = Get-DirectProperty $projectXml 'AICopilotTestRuntime' $relativePath
    $cadence = Get-DirectProperty $projectXml 'AICopilotTestCadence' $relativePath
    $owner = Get-DirectProperty $projectXml 'AICopilotTestOwner' $relativePath
    $requiredText = Get-DirectProperty $projectXml 'AICopilotRequired' $relativePath

    foreach ($criticalMetadata in @{
        AICopilotRequired = $requiredText
        AICopilotTestKind = $kind
        AICopilotTestRuntime = $runtime
        AICopilotTestCadence = $cadence
        AICopilotTestOwner = $owner
    }.GetEnumerator()) {
        if ([string]$evaluatedDefinition.Properties.($criticalMetadata.Key) -cne [string]$criticalMetadata.Value) {
            throw "$relativePath direct and evaluated $($criticalMetadata.Key) differ."
        }
    }

    foreach ($entry in @{
        AICopilotTestKind = $kind
        AICopilotTestRuntime = $runtime
        AICopilotTestCadence = $cadence
        AICopilotTestOwner = $owner
        AICopilotRequired = $requiredText
    }.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.Value)) {
            throw "$relativePath is missing direct $($entry.Key) metadata."
        }
    }

    if ($requiredText -notin @('true', 'false')) {
        throw "$relativePath has invalid AICopilotRequired='$requiredText'; expected true or false."
    }
    if ($kind -notin $allowedKinds) {
        throw "$relativePath has invalid AICopilotTestKind='$kind'; allowed values: $($allowedKinds -join ', ')."
    }
    if ($runtime -notin $allowedRuntimes) {
        throw "$relativePath has invalid AICopilotTestRuntime='$runtime'; allowed values: $($allowedRuntimes -join ', ')."
    }
    if ($cadence -notin $allowedCadences) {
        throw "$relativePath has invalid AICopilotTestCadence='$cadence'; allowed values: $($allowedCadences -join ', ')."
    }
    if ($role -notin @('', 'Analyzer')) {
        throw "$relativePath has invalid runner AICopilotTestRole='$role'; expected empty or Analyzer."
    }
    if ($role -eq 'Analyzer' -and ($kind -ne 'Architecture' -or $runtime -notin @('Pure', 'Filesystem'))) {
        throw "$relativePath is Analyzer and must use TestKind=Architecture with Runtime=Pure or Filesystem."
    }
    if ($kind -in @('Aggregate', 'Application') -and $runtime -ne 'Pure') {
        throw "$relativePath is TestKind=$kind and must use Runtime=Pure; filesystem or persistence behavior belongs in its physical runner."
    }
    if ($runtime -eq 'InProcess') {
        if (-not $inProcessRunnerAllowlist.ContainsKey($projectName)) {
            throw "$relativePath uses Runtime=InProcess but is not the locked in-process runner."
        }
        $inProcessTuple = "$kind|$owner"
        if ($inProcessTuple -cne [string]$inProcessRunnerAllowlist[$projectName]) {
            throw "$relativePath changed its locked InProcess kind/owner tuple."
        }
    }
    if ($projectName -ceq 'AICopilot.InProcessTests') {
        $lockedInProcessPath = 'src/tests/AICopilot.InProcessTests/AICopilot.InProcessTests.csproj'
        if ($relativePath -cne $lockedInProcessPath -or
            $kind -cne 'Integration' -or
            $runtime -cne 'InProcess' -or
            $owner -cne 'InProcessBoundary' -or
            $requiredText -cne 'true' -or
            $cadence -cne 'PR') {
            throw "$relativePath changed the locked AICopilot.InProcessTests path/kind/runtime/owner/required/cadence tuple."
        }
    }
    if ($role -eq 'Analyzer' -and $runtime -eq 'Pure') {
        $analyzerSources = @(
            Get-ChildItem $projectFile.Directory.FullName -Filter '*.cs' -File -Recurse |
                Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        )
        foreach ($source in $analyzerSources) {
            $sourceText = Get-Content $source.FullName -Raw
            if ($sourceText -match '(?m)\b(?:File|Directory)\.(?:Write|Create|Delete)' -or
                $sourceText -match '\bPath\.GetTempPath\s*\(' -or
                $sourceText -match '\bnew\s+Process\s*\(') {
                throw "$relativePath is Pure Analyzer but performs filesystem/process work in $($source.Name); move it to a Filesystem Analyzer runner."
            }
        }
    }

    $required = $requiredText -eq 'true'
    if ($required -and $cadence -ne 'PR') {
        throw "$relativePath is required but does not use PR cadence."
    }

    if ($runtime -eq 'LiveExternal' -and ($required -or $cadence -ne 'Manual')) {
        throw "$relativePath uses LiveExternal and must be Manual with AICopilotRequired=false."
    }

    if ($kind -eq 'GoldenEval') {
        $embeddedDatasets = @(
            $projectXml.SelectNodes('/Project/ItemGroup/EmbeddedResource') |
                ForEach-Object { ([string]$_.Include).Replace('\', '/') } |
                Where-Object { $_ -match '(?i)^datasets/(?:.+/)*.+\.json$' }
        )
        if ($embeddedDatasets.Count -eq 0) {
            throw "$relativePath is GoldenEval but does not embed its versioned JSON datasets."
        }

        $datasetFiles = @(
            Get-ChildItem (Join-Path $projectFile.Directory.FullName 'datasets') `
                -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue
        )
        if ($datasetFiles.Count -eq 0) {
            throw "$relativePath is GoldenEval but has no versioned JSON dataset."
        }

        $datasetCaseIds = [System.Collections.Generic.List[string]]::new()
        foreach ($datasetFile in $datasetFiles) {
            $dataset = Get-Content $datasetFile.FullName -Raw | ConvertFrom-Json
            if ([int]$dataset.schemaVersion -le 0 -or
                [string]::IsNullOrWhiteSpace([string]$dataset.datasetVersion) -or
                @($dataset.cases).Count -eq 0) {
                throw "$relativePath has invalid Golden dataset metadata in $($datasetFile.Name)."
            }
            foreach ($datasetCase in @($dataset.cases)) {
                if ([string]::IsNullOrWhiteSpace([string]$datasetCase.id) -or
                    $null -eq $datasetCase.input -or $null -eq $datasetCase.expected) {
                    throw "$relativePath Golden dataset cases require non-empty id/input/expected in $($datasetFile.Name)."
                }
                $datasetCaseIds.Add([string]$datasetCase.id)
            }
        }
        $duplicateDatasetCases = @($datasetCaseIds | Group-Object | Where-Object Count -gt 1)
        if ($duplicateDatasetCases.Count -ne 0) {
            throw "$relativePath Golden dataset contains duplicate ids: $($duplicateDatasetCases.Name -join ', ')."
        }

        $goldenSources = @(
            Get-ChildItem $projectFile.Directory.FullName -Filter '*.cs' -File -Recurse |
                Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        )
        $goldenSourceText = ($goldenSources | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
        if ($goldenSourceText -match '(?i)FakeRuntime|SelfProving' -or
            $goldenSourceText -notmatch 'MemberData' -or
            $goldenSourceText -notmatch 'GetManifestResourceStream') {
            throw "$relativePath GoldenEval must use embedded versioned datasets and production paths; fake/self-proving evals are forbidden."
        }
    }

    $expectedProfile = if ($runtime -eq 'LiveExternal') {
        'LiveExternal'
    } elseif ($kind -eq 'GoldenEval') {
        'GoldenDataset'
    } elseif ($projectName -in @('AICopilot.SimulationTests', 'AICopilot.SimulationDockerTests')) {
        'Simulation'
    } else {
        'Default'
    }
    if (-not $classificationByProject.ContainsKey($projectName)) {
        throw "$relativePath has no explicit runner classification entry."
    }
    $projectClassification = $classificationByProject[$projectName]
    Assert-ClassificationDimensions `
        -Dimensions $projectClassification.defaults `
        -Context "$projectName defaults" `
        -ExpectedProfile $expectedProfile `
        -RequireRuntimeDependencies
    if ([string]$projectClassification.defaults.testKind -cne $kind) {
        throw "$relativePath classification default testKind='$($projectClassification.defaults.testKind)' must equal project kind '$kind'."
    }
    if ($runtime -eq 'InProcess' -and
        @($projectClassification.defaults.runtimeDependencies).Count -ne 0) {
        throw "$relativePath is InProcess and must declare no external runtimeDependencies."
    }

    if (-not $required) {
        if (-not $nonRequiredRunnerAllowlist.ContainsKey($projectName)) {
            throw "$relativePath is a new or unapproved non-required runner; required downgrades and new Manual runners are forbidden."
        }
        $nonRequiredTuple = "$kind|$runtime|$cadence|$([string]$projectClassification.defaults.profile)"
        if ($nonRequiredTuple -cne [string]$nonRequiredRunnerAllowlist[$projectName]) {
            throw "$relativePath changed the locked non-required kind/runtime/cadence/profile tuple."
        }
    } elseif ($cadence -eq 'Manual') {
        throw "$relativePath is required but declares Manual cadence."
    }

    $inventory.Add([pscustomobject]@{
        projectName = $projectName
        path = $relativePath
        role = 'Runner'
        kind = $kind
        runtime = $runtime
        cadence = $cadence
        owner = $owner
        required = $required
        capability = [string]$projectClassification.defaults.capability
        concern = [string]$projectClassification.defaults.concern
        profile = [string]$projectClassification.defaults.profile
        risk = [string]$projectClassification.defaults.risk
        ruleId = [string]$projectClassification.defaults.ruleId
        regressionId = [string]$projectClassification.defaults.regressionId
        runtimeDependencies = @($projectClassification.defaults.runtimeDependencies)
    })
}

foreach ($project in @($inventory | Where-Object role -eq 'Runner')) {
    $baselineText = (& git -C $root show "$mergeBase`:$($project.path)" 2>$null) -join "`n"
    if ([string]::IsNullOrWhiteSpace($baselineText)) {
        if (-not $project.required -and
            -not $nonRequiredRunnerAllowlist.ContainsKey([string]$project.projectName)) {
            throw "$($project.path) is a new non-required/Manual runner relative to merge-base $mergeBase."
        }
        continue
    }
    if ($baselineText -match '<AICopilotRequired>\s*true\s*</AICopilotRequired>' -and
        -not $project.required) {
        throw "$($project.path) downgraded AICopilotRequired from true to false relative to merge-base $mergeBase."
    }
}

$runnerProjectNames = @($inventory | Where-Object role -eq 'Runner' | ForEach-Object projectName)
$unusedClassificationProjects = @(
    $classificationByProject.Keys | Where-Object { $_ -notin $runnerProjectNames }
)
if ($unusedClassificationProjects.Count -ne 0) {
    throw "Test classification ledger contains stale runner entries: $($unusedClassificationProjects -join ', ')."
}

if (@($inventory | Where-Object { $_.role -eq 'Runner' -and $_.required }).Count -eq 0) {
    throw 'No required AICopilot test runner was discovered.'
}

$runnerByFullPath = @{}
foreach ($project in @($inventory | Where-Object role -eq 'Runner')) {
    $runnerByFullPath[[System.IO.Path]::GetFullPath((Join-Path $root $project.path))] = $project
}

foreach ($project in @($inventory | Where-Object role -eq 'Runner')) {
    $projectPath = [System.IO.Path]::GetFullPath((Join-Path $root $project.path))
    $directReferences = @(Get-EvaluatedProjectReferences $projectPath)
    $directReferenceNames = @($directReferences | ForEach-Object { [System.IO.Path]::GetFileName($_) })
    $directPackageNames = @(Get-EvaluatedPackageReferences $projectPath)
    foreach ($reference in $directReferences) {
        if ($runnerByFullPath.ContainsKey($reference)) {
            throw "$($project.path) must not reference runner project $($runnerByFullPath[$reference].path)."
        }
    }

    if ($project.kind -eq 'Aggregate') {
        $invalidAggregateReferences = @(
            $directReferences | Where-Object {
                $referencePath = [System.IO.Path]::GetRelativePath($root, $_).Replace('\', '/')
                $referencePath -notmatch '^src/(core|shared)/'
            } | ForEach-Object {
                [System.IO.Path]::GetRelativePath($root, $_).Replace('\', '/')
            }
        )
        if ($invalidAggregateReferences.Count -ne 0) {
            throw "$($project.path) is TestKind=Aggregate and may reference only core/shared production projects; invalid=$($invalidAggregateReferences -join ',')."
        }
    }

    if ($project.kind -eq 'Application') {
        $invalidApplicationReferences = @(
            $directReferences | Where-Object {
                $referencePath = [System.IO.Path]::GetRelativePath($root, $_).Replace('\', '/')
                $referencePath.StartsWith('src/hosts/', [StringComparison]::OrdinalIgnoreCase) -or
                [System.IO.Path]::GetFileName($_) -in @(
                    'AICopilot.EntityFrameworkCore.csproj',
                    'AICopilot.Dapper.csproj',
                    'AICopilot.AspireIntegrationTestKit.csproj',
                    'AICopilot.PersistenceTestKit.csproj')
            } | ForEach-Object {
                [System.IO.Path]::GetRelativePath($root, $_).Replace('\', '/')
            }
        )
        if ($invalidApplicationReferences.Count -ne 0) {
            throw "$($project.path) is TestKind=Application and must not depend on host, database, Aspire, or persistence fixtures; invalid=$($invalidApplicationReferences -join ',')."
        }
    }

    if ($project.kind -eq 'Integration') {
        if ($project.runtime -eq 'Aspire' -and
            'AICopilot.AspireIntegrationTestKit.csproj' -notin $directReferenceNames) {
            throw "$($project.path) is Aspire Integration and must prove a direct Aspire runtime fixture reference."
        }
        if ($project.runtime -notin @('Aspire', 'InProcess')) {
            throw "$($project.path) is Integration and must use Runtime=Aspire or locked Runtime=InProcess."
        }
    }
    if ($project.runtime -eq 'InProcess') {
        $inProcessVisited = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        $inProcessPending = [System.Collections.Generic.Queue[string]]::new()
        $inProcessPending.Enqueue($projectPath)
        $forbiddenInProcessReferences = [System.Collections.Generic.List[string]]::new()
        while ($inProcessPending.Count -gt 0) {
            $currentInProcessProject = $inProcessPending.Dequeue()
            if (-not $inProcessVisited.Add($currentInProcessProject)) {
                continue
            }
            foreach ($inProcessReference in @(Get-EvaluatedProjectReferences $currentInProcessProject)) {
                if ([System.IO.Path]::GetFileName($inProcessReference) -in @(
                        'AICopilot.AppHost.csproj',
                        'AICopilot.RagWorker.csproj',
                        'AICopilot.AspireIntegrationTestKit.csproj',
                        'AICopilot.PersistenceTestKit.csproj')) {
                    $forbiddenInProcessReferences.Add(
                        [System.IO.Path]::GetRelativePath($root, $inProcessReference).Replace('\', '/'))
                }
                $inProcessPending.Enqueue($inProcessReference)
            }
        }
        if ($forbiddenInProcessReferences.Count -ne 0) {
            throw "$($project.path) is InProcess and its evaluated project graph must not reach AppHost, RagWorker, AspireIntegrationTestKit, or PersistenceTestKit; invalid=$(@($forbiddenInProcessReferences | Sort-Object -Unique) -join ',')."
        }
        $forbiddenInProcessPackages = @(
            $directPackageNames | Where-Object {
                $_ -match '(?i)^(?:Aspire\.Hosting|Npgsql|Testcontainers|Docker|RabbitMQ|Qdrant)(?:\.|$)' -or
                $_ -match '(?i)(?:^|\.)Redis(?:\.|$)'
            }
        )
        if ($forbiddenInProcessPackages.Count -ne 0) {
            throw "$($project.path) is InProcess and must not directly reference external-resource packages; invalid=$($forbiddenInProcessPackages -join ',')."
        }
    }
    if ($project.kind -eq 'EndToEnd' -and
        ($project.runtime -ne 'Aspire' -or
         'AICopilot.AspireIntegrationTestKit.csproj' -notin $directReferenceNames)) {
        throw "$($project.path) is EndToEnd and must prove a direct cross-service Aspire fixture reference."
    }
    if ($project.kind -eq 'Persistence' -and
        ($project.runtime -notin @('Postgres', 'Filesystem') -or
         'AICopilot.PersistenceTestKit.csproj' -notin $directReferenceNames)) {
        throw "$($project.path) is Persistence and must prove a direct Postgres/filesystem persistence fixture reference."
    }

    if ($project.runtime -ne 'Pure') {
        continue
    }

    $visited = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $pending = [System.Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($projectPath)
    while ($pending.Count -gt 0) {
        $current = $pending.Dequeue()
        if (-not $visited.Add($current)) {
            continue
        }
        $currentRelativePath = [System.IO.Path]::GetRelativePath($root, $current).Replace('\', '/')
        $currentProjectName = [System.IO.Path]::GetFileName($current)
        if ($currentRelativePath.StartsWith('src/hosts/', [StringComparison]::OrdinalIgnoreCase) -or
            $currentProjectName -in @(
                'AICopilot.EntityFrameworkCore.csproj',
                'AICopilot.Dapper.csproj',
                'AICopilot.AspireIntegrationTestKit.csproj',
                'AICopilot.FilesystemTestKit.csproj',
                'AICopilot.PersistenceTestKit.csproj'
            ) -or
            $currentProjectName -match '(?i)Aspire') {
            throw "$($project.path) is Pure but reaches forbidden project $currentRelativePath."
        }

        $forbiddenPackages = @(
            Get-EvaluatedPackageReferences $current |
                Where-Object { $_ -match '(?i)(EntityFrameworkCore|(^|\.)Dapper($|\.)|Aspire)' }
        )
        if ($forbiddenPackages.Count -ne 0) {
            throw "$($project.path) is Pure but reaches forbidden package(s) through ${currentRelativePath}: $($forbiddenPackages -join ', ')."
        }

        foreach ($reference in @(Get-EvaluatedProjectReferences $current)) {
            if (Test-Path $reference -PathType Leaf) {
                $pending.Enqueue($reference)
            }
        }
    }
}

$supportProjects = @($inventory | Where-Object role -eq 'Support')
foreach ($support in $supportProjects) {
    $supportPath = [System.IO.Path]::GetFullPath((Join-Path $root $support.path))
    $actualConsumers = @(
        foreach ($runner in @($inventory | Where-Object role -eq 'Runner')) {
            $runnerPath = [System.IO.Path]::GetFullPath((Join-Path $root $runner.path))
            if ($supportPath -in @(Get-EvaluatedProjectReferences $runnerPath)) {
                $runner.projectName
            }
        }
    ) | Sort-Object
    $declaredConsumers = @($support.consumers) | Sort-Object
    if (($actualConsumers -join ';') -cne ($declaredConsumers -join ';')) {
        throw "$($support.path) consumer inventory differs from direct project references. declared=$($declaredConsumers -join ','), actual=$($actualConsumers -join ',')."
    }
}

$nonProductionProjectPaths = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($nonProductionProject in @($inventory)) {
    [void]$nonProductionProjectPaths.Add(
        [System.IO.Path]::GetFullPath((Join-Path $root $nonProductionProject.path)))
}
$productionProjects = @(
    Get-ChildItem (Join-Path $root 'src') -Filter '*.csproj' -File -Recurse |
        Where-Object {
            $_.FullName -notmatch '[\\/](tests|testing)[\\/]' -and
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        }
)
$analyzerProjectPath = [System.IO.Path]::GetFullPath((Join-Path $root `
    'src/analyzers/AICopilot.Architecture.Analyzers/AICopilot.Architecture.Analyzers.csproj'))
$sourceAnalyzerSuppressionPatterns = @(
    '(?im)^\s*#pragma\s+warning\s+disable[^\r\n]*\bAIARCH00[1-7]\b',
    '(?is)\[\s*(?:assembly\s*:\s*)?(?:global::)?(?:System\.Diagnostics\.CodeAnalysis\.)?(?:Unconditional)?SuppressMessage(?:Attribute)?\s*\([^\)]*\bAIARCH00[1-7]\b'
)
$configAnalyzerSuppressionPattern = '(?im)^\s*dotnet_diagnostic\.AIARCH00[1-7]\.severity\s*='
$analyzerConfigurationFiles = @(
    Get-ChildItem $root -File -Recurse -Force |
        Where-Object {
            ($_.Name -eq '.editorconfig' -or
                $_.Name -eq '.globalconfig' -or
                $_.Name -like '*.editorconfig' -or
                $_.Name -like '*.globalconfig') -and
            $_.FullName -notmatch '[\\/](\.git|artifacts|bin|obj|node_modules)[\\/]'
        }
)
foreach ($configurationFile in $analyzerConfigurationFiles) {
    if ((Get-Content $configurationFile.FullName -Raw) -match $configAnalyzerSuppressionPattern) {
        $relativeConfigurationFile = [System.IO.Path]::GetRelativePath(
            $root,
            $configurationFile.FullName).Replace('\', '/')
        throw "$relativeConfigurationFile configures required AIARCH analyzer severity; AIARCH diagnostics are NotConfigurable."
    }
}

# Validate source/project policy for the complete production set before requiring build
# evidence. Controlled negative fixtures intentionally have no bin/obj output; a PDB
# error from an unrelated project must not mask the policy violation under test.
foreach ($productionProject in $productionProjects) {
    $productionRelativePath = [System.IO.Path]::GetRelativePath(
        $root,
        $productionProject.FullName).Replace('\', '/')
    $definition = Get-EvaluatedProjectDefinition $productionProject.FullName
    $expectedAssemblyName = [System.IO.Path]::GetFileNameWithoutExtension($productionProject.Name)
    if ([string]$definition.Properties.AssemblyName -cne $expectedAssemblyName -or
        [string]$definition.Properties.IsTestProject -ieq 'true' -or
        -not [string]::IsNullOrWhiteSpace([string]$definition.Properties.AICopilotTestRole)) {
        throw "$productionRelativePath spoofs AssemblyName, IsTestProject, or AICopilotTestRole."
    }
    if ([string]$definition.Properties.RunAnalyzers -ieq 'false' -or
        [string]$definition.Properties.RunAnalyzersDuringBuild -ieq 'false' -or
        [string]$definition.Properties.NoWarn -match '(?i)(^|[;,\s])AIARCH\d*($|[;,\s])') {
        throw "$productionRelativePath disables or suppresses required AIARCH analyzers."
    }

    if ([System.IO.Path]::GetFullPath($productionProject.FullName) -ne $analyzerProjectPath) {
        $architectureAnalyzerReferences = @(
            $definition.ProjectReferenceDefinitions | Where-Object {
                $_.path -eq $analyzerProjectPath -and
                $_.outputItemType -ceq 'Analyzer' -and
                $_.referenceOutputAssembly -ieq 'false' -and
                $_.privateAssets -ceq 'all'
            }
        )
        if ($architectureAnalyzerReferences.Count -ne 1) {
            throw "$productionRelativePath must evaluate exactly one locked architecture Analyzer reference."
        }
    }

    foreach ($reference in @($definition.ProjectReferences)) {
        if ($nonProductionProjectPaths.Contains($reference)) {
            throw "$productionRelativePath must not reference any test runner/support project $reference."
        }
    }

    $compiledSources = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($compilePath in @($definition.Compile)) {
        [void]$compiledSources.Add([System.IO.Path]::GetFullPath($compilePath))
    }
    $physicalSources = @(
        Get-ChildItem $productionProject.Directory.FullName -Filter '*.cs' -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    )
    foreach ($source in $physicalSources) {
        if (-not $compiledSources.Contains($source.FullName)) {
            $sourceRelativePath = [System.IO.Path]::GetRelativePath($root, $source.FullName).Replace('\', '/')
            throw "$productionRelativePath contains unreferenced production source '$sourceRelativePath'."
        }
        $sourceText = Get-Content $source.FullName -Raw
        if (@($sourceAnalyzerSuppressionPatterns | Where-Object { $sourceText -match $_ }).Count -ne 0) {
            $sourceRelativePath = [System.IO.Path]::GetRelativePath($root, $source.FullName).Replace('\', '/')
            throw "$sourceRelativePath suppresses a required AIARCH diagnostic in production source; AIARCH diagnostics are NotConfigurable."
        }
    }
}

$productionUniverse = [System.Collections.Generic.List[object]]::new()
$productionAssemblies = [System.Collections.Generic.List[object]]::new()
foreach ($productionProject in $productionProjects) {
    $productionRelativePath = [System.IO.Path]::GetRelativePath(
        $root,
        $productionProject.FullName).Replace('\', '/')
    $definition = Get-EvaluatedProjectDefinition $productionProject.FullName
    $expectedAssemblyName = [System.IO.Path]::GetFileNameWithoutExtension($productionProject.Name)
    if ([string]$definition.Properties.AssemblyName -cne $expectedAssemblyName -or
        [string]$definition.Properties.IsTestProject -ieq 'true' -or
        -not [string]::IsNullOrWhiteSpace([string]$definition.Properties.AICopilotTestRole)) {
        throw "$productionRelativePath spoofs AssemblyName, IsTestProject, or AICopilotTestRole."
    }
    if ([string]$definition.Properties.RunAnalyzers -ieq 'false' -or
        [string]$definition.Properties.RunAnalyzersDuringBuild -ieq 'false' -or
        [string]$definition.Properties.NoWarn -match '(?i)(^|[;,\s])AIARCH\d*($|[;,\s])') {
        throw "$productionRelativePath disables or suppresses required AIARCH analyzers."
    }

    if ([System.IO.Path]::GetFullPath($productionProject.FullName) -ne $analyzerProjectPath) {
        $architectureAnalyzerReferences = @(
            $definition.ProjectReferenceDefinitions | Where-Object {
                $_.path -eq $analyzerProjectPath -and
                $_.outputItemType -ceq 'Analyzer' -and
                $_.referenceOutputAssembly -ieq 'false' -and
                $_.privateAssets -ceq 'all'
            }
        )
        if ($architectureAnalyzerReferences.Count -ne 1) {
            throw "$productionRelativePath must evaluate exactly one locked architecture Analyzer reference."
        }
    }

    foreach ($reference in @($definition.ProjectReferences)) {
        if ($nonProductionProjectPaths.Contains($reference)) {
            $productionRelativePath = [System.IO.Path]::GetRelativePath(
                $root,
                $productionProject.FullName).Replace('\', '/')
            throw "$productionRelativePath must not reference any test runner/support project $reference."
        }
    }

    $compiledSources = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($compilePath in @($definition.Compile)) {
        [void]$compiledSources.Add([System.IO.Path]::GetFullPath($compilePath))
    }
    $physicalSources = @(
        Get-ChildItem $productionProject.Directory.FullName -Filter '*.cs' -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    )
    foreach ($source in $physicalSources) {
        if (-not $compiledSources.Contains($source.FullName)) {
            $sourceRelativePath = [System.IO.Path]::GetRelativePath($root, $source.FullName).Replace('\', '/')
            throw "$productionRelativePath contains unreferenced production source '$sourceRelativePath'."
        }
        $productionUniverse.Add([pscustomobject]@{
            assembly = $expectedAssemblyName
            project = $productionRelativePath
            source = [System.IO.Path]::GetRelativePath($root, $source.FullName).Replace('\', '/')
            sha256 = (Get-FileHash $source.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }

    if ($definition.TargetFrameworks.Count -ne 1) {
        throw "$productionRelativePath must evaluate exactly one production target framework for authoritative coverage evidence."
    }
    $targetPath = [System.IO.Path]::GetFullPath([string]$definition.Properties.TargetPath)
    $pdbPath = [System.IO.Path]::ChangeExtension($targetPath, '.pdb')
    if (-not (Test-PathInsideRepository $targetPath) -or
        -not (Test-PathInsideRepository $pdbPath) -or
        -not (Test-Path $targetPath -PathType Leaf) -or
        -not (Test-Path $pdbPath -PathType Leaf) -or
        [string]$definition.Properties.DebugType -cne 'portable' -or
        [System.IO.Path]::GetFileNameWithoutExtension($targetPath) -cne $expectedAssemblyName) {
        throw "Production coverage requires a current in-repository assembly and portable PDB: project=$productionRelativePath target=$targetPath pdb=$pdbPath debugType=$($definition.Properties.DebugType)."
    }
    $productionAssemblies.Add([pscustomobject]@{
        project = $productionRelativePath
        assembly = $expectedAssemblyName
        targetFramework = [string]$definition.TargetFrameworks[0]
        assemblyPath = [System.IO.Path]::GetRelativePath($root, $targetPath).Replace('\', '/')
        pdbPath = [System.IO.Path]::GetRelativePath($root, $pdbPath).Replace('\', '/')
        assemblySha256 = (Get-FileHash $targetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        pdbSha256 = (Get-FileHash $pdbPath -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}

$productionUniverseEntries = @($productionUniverse | Sort-Object assembly, project, source)
$productionAssemblyEntries = @($productionAssemblies | Sort-Object assembly, project)
$productionProjectsFromSources = @(
    $productionUniverseEntries |
        ForEach-Object { [string]$_.project } |
        Sort-Object -Unique
)
$productionProjectsFromAssemblies = @(
    $productionAssemblyEntries |
        ForEach-Object { [string]$_.project } |
        Sort-Object -Unique
)
if ($productionAssemblyEntries.Count -ne $productionProjects.Count -or
    ($productionProjectsFromSources -join "`n") -cne ($productionProjectsFromAssemblies -join "`n")) {
    throw "Production coverage assembly evidence omitted an evaluated project: expected=$($productionProjectsFromSources -join ','), actual=$($productionProjectsFromAssemblies -join ',')."
}
$productionUniverseCanonical = ($productionUniverseEntries | ForEach-Object {
    "$($_.assembly)`0$($_.project)`0$($_.source)`0$($_.sha256)"
}) -join "`n"
$productionUniverseSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($productionUniverseCanonical))).ToLowerInvariant()
$productionUniverseIdentityCanonical = ($productionUniverseEntries | ForEach-Object {
    "$($_.assembly)`0$($_.project)`0$($_.source)"
}) -join "`n"
$productionUniverseIdentitySha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($productionUniverseIdentityCanonical))).ToLowerInvariant()
$repositoryHead = (& git -C $root rev-parse HEAD 2>$null | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace([string]$repositoryHead)) {
    throw 'Cannot bind test inventory to the current repository HEAD.'
}
$repositoryStatus = @(& git -C $root status --porcelain=v1 --untracked-files=all 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw 'Cannot determine whether the repository is clean for authoritative evidence binding.'
}
$repositoryClean = $repositoryStatus.Count -eq 0

$runnerAssemblyNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($runner in @($inventory | Where-Object role -eq 'Runner')) {
    [void]$runnerAssemblyNames.Add([string]$runner.projectName)
}
$staleTestFriendAssemblies = [System.Collections.Generic.List[string]]::new()
$friendAssemblySources = @(
    Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
)
foreach ($source in $friendAssemblySources) {
    $sourceRelativePath = [System.IO.Path]::GetRelativePath($root, $source.FullName).Replace('\', '/')
    $sourceText = Get-Content $source.FullName -Raw
    foreach ($friendMatch in [regex]::Matches(
        $sourceText,
        'InternalsVisibleTo\s*\(\s*"(?<assembly>AICopilot\.[^",]+Tests)"')) {
        $friendAssembly = [string]$friendMatch.Groups['assembly'].Value
        if (-not $runnerAssemblyNames.Contains($friendAssembly)) {
            $staleTestFriendAssemblies.Add("$sourceRelativePath->$friendAssembly")
        }
    }
}
if ($staleTestFriendAssemblies.Count -ne 0) {
    throw "InternalsVisibleTo targets removed or unknown test runners: $($staleTestFriendAssemblies -join ', ')."
}

$delayAllowlistPath = Join-Path $root 'scripts/tests/aicopilot-fixed-delay-allowlist.json'
if (-not (Test-Path $delayAllowlistPath -PathType Leaf)) {
    throw "Fixed-delay allowlist is missing: $delayAllowlistPath"
}
$delayAllowlist = Get-Content $delayAllowlistPath -Raw | ConvertFrom-Json
if ([int]$delayAllowlist.schemaVersion -ne 1) {
    throw "Unsupported fixed-delay allowlist schemaVersion='$($delayAllowlist.schemaVersion)'."
}
$usedDelayAllowlistEntries = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
$testSources = @(
    Get-ChildItem (Join-Path $root 'src/tests'), (Join-Path $root 'src/testing') `
        -Filter '*.cs' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
)
foreach ($testSource in $testSources) {
    $sourceRelativePath = [System.IO.Path]::GetRelativePath(
        $root,
        $testSource.FullName).Replace('\', '/')
    $sourceText = Get-Content $testSource.FullName -Raw
    if ($sourceText -match '(?m)\[\s*Trait\s*\(\s*"(?:Suite|Runtime)"') {
        throw "$sourceRelativePath declares legacy Suite/Runtime traits; canonical inventory metadata is the only classification source."
    }
    foreach ($delayMatch in [regex]::Matches(
        $sourceText,
        'Task\.Delay\s*\(\s*(?<arguments>[^\r\n;]+)\)\s*;')) {
        $arguments = [string]$delayMatch.Groups['arguments'].Value
        if ($arguments.TrimStart().StartsWith('Timeout.InfiniteTimeSpan', [StringComparison]::Ordinal)) {
            continue
        }
        $delayExpression = $arguments.Split(',')[0].Trim()
        $matchingEntries = @(
            @($delayAllowlist.entries) | Where-Object {
                [string]$_.source -ceq $sourceRelativePath -and
                [string]$_.delayExpression -ceq $delayExpression
            }
        )
        if ($matchingEntries.Count -ne 1) {
            throw "$sourceRelativePath uses fixed Task.Delay($delayExpression) without exactly one readiness-polling allowlist entry."
        }
        $entry = $matchingEntries[0]
        if ([string]::IsNullOrWhiteSpace([string]$entry.member) -or
            [string]::IsNullOrWhiteSpace([string]$entry.reason) -or
            [int]$entry.maximumTotalSeconds -le 0 -or
            -not $sourceText.Contains([string]$entry.member, [StringComparison]::Ordinal) -or
            -not $sourceText.Contains([string]$entry.timeoutEvidence, [StringComparison]::Ordinal)) {
            throw "$sourceRelativePath has an incomplete fixed-delay readiness allowlist entry."
        }
        $entryId = "$sourceRelativePath::$([string]$entry.member)::$delayExpression"
        if (-not $usedDelayAllowlistEntries.Add($entryId)) {
            throw "$sourceRelativePath has more than one fixed delay covered by the same allowlist entry '$entryId'."
        }
    }
}
$declaredDelayAllowlistEntries = @(
    @($delayAllowlist.entries) | ForEach-Object {
        "$([string]$_.source)::$([string]$_.member)::$([string]$_.delayExpression)"
    }
)
$unusedDelayAllowlistEntries = @(
    $declaredDelayAllowlistEntries | Where-Object { -not $usedDelayAllowlistEntries.Contains($_) }
)
if ($unusedDelayAllowlistEntries.Count -ne 0) {
    throw "Fixed-delay allowlist contains stale entries: $($unusedDelayAllowlistEntries -join ', ')."
}

$productionAssemblyByName = @{}
$productionAssemblyByProjectPath = @{}
foreach ($assemblyEvidence in $productionAssemblyEntries) {
    $assemblyName = [string]$assemblyEvidence.assembly
    if ($productionAssemblyByName.ContainsKey($assemblyName)) {
        throw "Production assembly evidence contains duplicate assembly '$assemblyName'."
    }
    $productionAssemblyByName[$assemblyName] = $assemblyEvidence
    $productionProjectPath = [System.IO.Path]::GetFullPath(
        (Join-Path $root ([string]$assemblyEvidence.project)))
    if ($productionAssemblyByProjectPath.ContainsKey($productionProjectPath)) {
        throw "Production assembly evidence contains duplicate project '$productionProjectPath'."
    }
    $productionAssemblyByProjectPath[$productionProjectPath] = $assemblyEvidence
}

function Get-RunnerProductionAssemblyClosure {
    param([Parameter(Mandatory)][string]$RunnerProjectPath)

    $visited = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $pending = [System.Collections.Generic.Queue[string]]::new()
    $assemblyNames = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $pending.Enqueue([System.IO.Path]::GetFullPath($RunnerProjectPath))
    while ($pending.Count -gt 0) {
        $currentProjectPath = $pending.Dequeue()
        if (-not $visited.Add($currentProjectPath)) {
            continue
        }
        $currentDefinition = Get-EvaluatedProjectDefinition $currentProjectPath
        foreach ($reference in @($currentDefinition.ProjectReferenceDefinitions)) {
            if ([string]$reference.outputItemType -ieq 'Analyzer' -or
                [string]$reference.referenceOutputAssembly -ieq 'false') {
                continue
            }
            $referencePath = [System.IO.Path]::GetFullPath([string]$reference.path)
            if ($productionAssemblyByProjectPath.ContainsKey($referencePath)) {
                $expectedAssembly = $productionAssemblyByProjectPath[$referencePath]
                [void]$assemblyNames.Add([string]$expectedAssembly.assembly)
                $pending.Enqueue($referencePath)
                continue
            }
            if ($nonProductionProjectPaths.Contains($referencePath)) {
                $pending.Enqueue($referencePath)
                continue
            }
            throw "Runner production closure reached an unclassified project reference: runner=$RunnerProjectPath reference=$referencePath."
        }
    }
    return @($assemblyNames | Sort-Object)
}

$runnerBuildIdentityRecords = [System.Collections.Generic.List[object]]::new()
foreach ($runner in @($inventory | Where-Object { $_.role -eq 'Runner' -and [bool]$_.required })) {
    $runnerProjectPath = [System.IO.Path]::GetFullPath((Join-Path $root $runner.path))
    $runnerDefinition = Get-EvaluatedProjectDefinition $runnerProjectPath
    $runnerTargetPath = [System.IO.Path]::GetFullPath([string]$runnerDefinition.Properties.TargetPath)
    if (-not (Test-PathInsideRepository $runnerTargetPath) -or
        -not (Test-Path $runnerTargetPath -PathType Leaf) -or
        [System.IO.Path]::GetFileNameWithoutExtension($runnerTargetPath) -cne [string]$runner.projectName) {
        throw "Required runner build identity needs its current in-repository target: project=$($runner.path) target=$runnerTargetPath."
    }
    $runnerOutputDirectory = Split-Path $runnerTargetPath -Parent
    $expectedAssemblyNames = @(Get-RunnerProductionAssemblyClosure $runnerProjectPath)
    $expectedAssemblyNameSet = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($expectedAssemblyName in $expectedAssemblyNames) {
        [void]$expectedAssemblyNameSet.Add($expectedAssemblyName)
    }
    $loadedProductionAssemblies = [System.Collections.Generic.List[object]]::new()
    foreach ($assemblyName in @($productionAssemblyByName.Keys | Sort-Object)) {
        $expectedAssembly = $productionAssemblyByName[$assemblyName]
        $runnerAssemblyPath = Join-Path $runnerOutputDirectory "$assemblyName.dll"
        $runnerPdbPath = Join-Path $runnerOutputDirectory "$assemblyName.pdb"
        $runnerAssemblyExists = Test-Path $runnerAssemblyPath -PathType Leaf
        $runnerPdbExists = Test-Path $runnerPdbPath -PathType Leaf
        $assemblyExpected = $expectedAssemblyNameSet.Contains($assemblyName)
        if (-not $assemblyExpected -and -not $runnerAssemblyExists -and -not $runnerPdbExists) {
            continue
        }
        if (-not $assemblyExpected) {
            throw "$($runner.projectName) runner output contains production assembly/PDB outside its evaluated runtime ProjectReference closure: assembly=$assemblyName dll=$runnerAssemblyExists pdb=$runnerPdbExists."
        }
        if (-not $runnerAssemblyExists -or -not $runnerPdbExists) {
            throw "$($runner.projectName) runner output is missing a complete evaluated-closure production DLL/PDB pair: assembly=$assemblyName dll=$runnerAssemblyExists pdb=$runnerPdbExists."
        }

        $preSynchronizationAssemblySha256 = (Get-FileHash $runnerAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $preSynchronizationPdbSha256 = (Get-FileHash $runnerPdbPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $synchronized = $false
        if ($preSynchronizationAssemblySha256 -cne [string]$expectedAssembly.assemblySha256 -or
            $preSynchronizationPdbSha256 -cne [string]$expectedAssembly.pdbSha256) {
            if (-not $SynchronizeRunnerBuildIdentity) {
                throw "$($runner.projectName) runner build identity differs from the inventory-bound production output: assembly=$assemblyName expectedDll=$([string]$expectedAssembly.assemblySha256) actualDll=$preSynchronizationAssemblySha256 expectedPdb=$([string]$expectedAssembly.pdbSha256) actualPdb=$preSynchronizationPdbSha256. The canonical build must pass one explicit SourceRevisionId derived from git rev-parse HEAD, then intentionally synchronize existing runner copies before inventory binding."
            }
            $canonicalAssemblyPath = [System.IO.Path]::GetFullPath(
                (Join-Path $root ([string]$expectedAssembly.assemblyPath)))
            $canonicalPdbPath = [System.IO.Path]::GetFullPath(
                (Join-Path $root ([string]$expectedAssembly.pdbPath)))
            Copy-Item $canonicalAssemblyPath $runnerAssemblyPath -Force
            Copy-Item $canonicalPdbPath $runnerPdbPath -Force
            $synchronized = $true
        }

        $actualAssemblySha256 = (Get-FileHash $runnerAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $actualPdbSha256 = (Get-FileHash $runnerPdbPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualAssemblySha256 -cne [string]$expectedAssembly.assemblySha256 -or
            $actualPdbSha256 -cne [string]$expectedAssembly.pdbSha256) {
            throw "$($runner.projectName) runner build identity synchronization did not produce the inventory-bound bytes: assembly=$assemblyName expectedDll=$([string]$expectedAssembly.assemblySha256) actualDll=$actualAssemblySha256 expectedPdb=$([string]$expectedAssembly.pdbSha256) actualPdb=$actualPdbSha256."
        }

        $loadedProductionAssemblies.Add([pscustomobject]@{
            assembly = $assemblyName
            synchronized = $synchronized
            preSynchronizationAssemblySha256 = $preSynchronizationAssemblySha256
            preSynchronizationPdbSha256 = $preSynchronizationPdbSha256
            assemblySha256 = $actualAssemblySha256
            pdbSha256 = $actualPdbSha256
        })
    }

    $runnerBuildIdentityRecords.Add([pscustomobject]@{
        projectName = [string]$runner.projectName
        targetPath = [System.IO.Path]::GetRelativePath($root, $runnerTargetPath).Replace('\', '/')
        productionAssemblyCount = $loadedProductionAssemblies.Count
        synchronizedAssemblyCount = @($loadedProductionAssemblies | Where-Object synchronized).Count
        productionAssemblies = @($loadedProductionAssemblies)
    })
}

$runnerBuildIdentityEntries = @($runnerBuildIdentityRecords | Sort-Object projectName)
$runnerBuildIdentityCanonical = ($runnerBuildIdentityEntries | ForEach-Object {
    $runnerIdentity = $_
    @(
        "$([string]$runnerIdentity.projectName)`0$([string]$runnerIdentity.targetPath)`0$([int]$runnerIdentity.productionAssemblyCount)`0$([int]$runnerIdentity.synchronizedAssemblyCount)"
        @($runnerIdentity.productionAssemblies | Sort-Object assembly | ForEach-Object {
            "$([string]$runnerIdentity.projectName)`0$([string]$_.assembly)`0$([bool]$_.synchronized)`0$([string]$_.preSynchronizationAssemblySha256)`0$([string]$_.preSynchronizationPdbSha256)`0$([string]$_.assemblySha256)`0$([string]$_.pdbSha256)"
        })
    ) -join "`n"
}) -join "`n"
$runnerBuildIdentitySha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($runnerBuildIdentityCanonical))).ToLowerInvariant()
$runnerBuildIdentitySynchronizedAssemblyCount = [int]((
    $runnerBuildIdentityEntries |
        ForEach-Object { [int]$_.synchronizedAssemblyCount } |
        Measure-Object -Sum).Sum)

function Get-CaseDimensions {
    param(
        [Parameter(Mandatory)] [string]$CaseId,
        [Parameter(Mandatory)] [string]$ClassName,
        [Parameter(Mandatory)] [string]$MethodName,
        [Parameter(Mandatory)] [object]$Project
    )

    $classificationProject = $classificationByProject[$Project.projectName]
    $overridesProperty = $classificationProject.PSObject.Properties['overrides']
    $classOverrides = [System.Collections.Generic.List[object]]::new()
    $exactCandidates = [System.Collections.Generic.List[object]]::new()
    foreach ($override in @(
        if ($null -ne $overridesProperty) { $overridesProperty.Value }
    )) {
        if ([string]$override.class -cne $ClassName) {
            continue
        }
        $methodProperty = $override.PSObject.Properties['method']
        $caseProperty = $override.PSObject.Properties['case']
        $methodSelector = if ($null -eq $methodProperty) { '' } else { [string]$methodProperty.Value }
        $caseSelector = if ($null -eq $caseProperty) { '' } else { [string]$caseProperty.Value }
        if ([string]::IsNullOrWhiteSpace($methodSelector) -and
            [string]::IsNullOrWhiteSpace($caseSelector)) {
            $classOverrides.Add($override)
            continue
        }
        if (-not [string]::IsNullOrWhiteSpace($methodSelector) -and
            $methodSelector -cne $MethodName) {
            continue
        }
        if (-not [string]::IsNullOrWhiteSpace($caseSelector) -and
            $caseSelector -cne $CaseId) {
            continue
        }
        $exactCandidates.Add($override)
    }

    if ($classOverrides.Count -gt 1) {
        throw "$CaseId matches more than one class-level classification override."
    }
    if ([string]$Project.runtime -ceq 'InProcess' -and $classOverrides.Count -ne 1) {
        throw "$CaseId belongs to the InProcess runner and requires exactly one class-level classification override."
    }
    if ($classOverrides.Count -eq 1) {
        $classOverride = $classOverrides[0]
        if ([string]$Project.runtime -ceq 'InProcess' -and
            ($null -eq $classOverride.PSObject.Properties['testKind'] -or
             [string]::IsNullOrWhiteSpace([string]$classOverride.testKind))) {
            throw "$CaseId belongs to the InProcess runner and its class override must declare testKind."
        }
        [void]$usedClassificationOverrideIds.Add(
            "$($Project.projectName)::$([string]$classOverride.matchId)")
    }

    $matchingExactOverrides = @($exactCandidates)
    if ($matchingExactOverrides.Count -gt 1) {
        throw "$CaseId matches more than one exact method/case classification override."
    }
    if ($matchingExactOverrides.Count -eq 1) {
        [void]$usedClassificationOverrideIds.Add(
            "$($Project.projectName)::$([string]$matchingExactOverrides[0].matchId)")
    }

    $mergeOverrides = @()
    if ($classOverrides.Count -eq 1) {
        $mergeOverrides += $classOverrides[0]
    }
    if ($matchingExactOverrides.Count -eq 1) {
        $mergeOverrides += $matchingExactOverrides[0]
    }
    $dimensions = Merge-ClassificationDimensions `
        -Defaults $classificationProject.defaults `
        -Overrides $mergeOverrides
    Assert-ClassificationDimensions `
        -Dimensions $dimensions `
        -Context "case '$CaseId'" `
        -ExpectedProfile ([string]$Project.profile) `
        -RequireRuntimeDependencies
    if ([string]$Project.runtime -cne 'InProcess' -and
        [string]$dimensions.testKind -cne [string]$Project.kind) {
        throw "case '$CaseId' testKind='$($dimensions.testKind)' must equal its non-InProcess project kind '$($Project.kind)'."
    }
    $runtimeDependencies = @(Resolve-CaseRuntimeDependencies `
        -SelectedDimensions $dimensions `
        -DefaultDimensions $classificationProject.defaults `
        -Context "case '$CaseId'")
    if ([string]$Project.runtime -ceq 'InProcess' -and $runtimeDependencies.Count -ne 0) {
        throw "case '$CaseId' belongs to an InProcess runner and must not declare external runtimeDependencies."
    }

    return [pscustomobject]@{
        testKind = [string]$dimensions.testKind
        capability = [string]$dimensions.capability
        concern = [string]$dimensions.concern
        profile = [string]$dimensions.profile
        risk = [string]$dimensions.risk
        ruleId = [string]$dimensions.ruleId
        regressionId = [string]$dimensions.regressionId
        runtimeDependencies = @($runtimeDependencies)
    }
}

$cases = [System.Collections.Generic.List[object]]::new()
$usedClassificationOverrideIds = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($project in @($inventory | Where-Object role -eq 'Runner')) {
    $projectPath = Join-Path $root $project.path
    $discoveryOutput = @(
        & dotnet test $projectPath -c $Configuration --no-build --no-restore --list-tests --nologo -v:quiet 2>&1 |
            ForEach-Object { [string]$_ }
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Real discovery failed for $($project.path):`n$($discoveryOutput -join [Environment]::NewLine)"
    }

    $caseIds = @(
        $discoveryOutput |
            Where-Object { $_ -match '^\s{4}\S' -and $_.Trim().StartsWith("$($project.projectName).", [StringComparison]::Ordinal) } |
            ForEach-Object { $_.Trim() }
    )
    if ($caseIds.Count -eq 0) {
        throw "Runner $($project.path) discovered no cases. Raw discovery output:`n$($discoveryOutput -join [Environment]::NewLine)"
    }
    $duplicateProjectCases = @($caseIds | Group-Object | Where-Object Count -gt 1)
    if ($duplicateProjectCases.Count -ne 0) {
        throw "Runner $($project.path) discovered duplicate case identities: $($duplicateProjectCases.Name -join ', ')"
    }

    if ([string]$project.runtime -ceq 'InProcess') {
        $discoveredClasses = @(
            foreach ($caseId in $caseIds) {
                $signature = if ($caseId.Contains('(')) {
                    $caseId.Substring(0, $caseId.IndexOf('('))
                } else {
                    $caseId
                }
                $methodSeparator = $signature.LastIndexOf('.')
                $signature.Substring(0, $methodSeparator)
            }
        )
        Assert-InProcessClassificationCoverage `
            -ClassificationProject $classificationByProject[$project.projectName] `
            -DiscoveredClasses $discoveredClasses `
            -Context $project.projectName
    }

    $project | Add-Member -NotePropertyName caseCount -NotePropertyValue $caseIds.Count

    foreach ($caseId in $caseIds) {
        $signature = if ($caseId.Contains('(')) { $caseId.Substring(0, $caseId.IndexOf('(')) } else { $caseId }
        $methodSeparator = $signature.LastIndexOf('.')
        $methodName = $signature.Substring($methodSeparator + 1)
        $className = $signature.Substring(0, $methodSeparator)
        $dimensions = Get-CaseDimensions `
            -CaseId $caseId `
            -ClassName $className `
            -MethodName $methodName `
            -Project $project
        $cases.Add([pscustomobject]@{
            assembly = $project.projectName
            class = $className
            method = $methodName
            case = $caseId
            kind = $dimensions.testKind
            runtime = $project.runtime
            capability = $dimensions.capability
            concern = $dimensions.concern
            profile = $dimensions.profile
            risk = $dimensions.risk
            ruleId = $dimensions.ruleId
            regressionId = $dimensions.regressionId
            runtimeDependencies = @($dimensions.runtimeDependencies)
            cadence = $project.cadence
            owner = $project.owner
            required = [bool]$project.required
            skipExpected = $false
            status = 'Expected'
        })
    }
}

$unusedClassificationOverrides = @(
    $classificationOverrideIds | Where-Object { -not $usedClassificationOverrideIds.Contains($_) }
)
if ($unusedClassificationOverrides.Count -ne 0) {
    throw "Test classification ledger contains stale or unmatched overrides: $($unusedClassificationOverrides -join ', ')."
}

$duplicateCases = @($cases | Group-Object case | Where-Object Count -gt 1)
if ($duplicateCases.Count -ne 0) {
    throw "Case inventory contains duplicate identities: $($duplicateCases.Name -join ', ')"
}

$resolvedBaselinePath = if ([System.IO.Path]::IsPathRooted($BaselinePath)) {
    $BaselinePath
} else {
    Join-Path $root $BaselinePath
}
$baselineDocument = [pscustomobject]@{
    schemaVersion = 2
    cases = @($cases | Sort-Object assembly, case)
}
$baselineJson = $baselineDocument | ConvertTo-Json -Depth 6
if ($UpdateBaseline) {
    New-Item -ItemType Directory -Path (Split-Path $resolvedBaselinePath -Parent) -Force | Out-Null
    $baselineJson | Set-Content $resolvedBaselinePath -Encoding utf8
    Write-Host "Case baseline updated: $resolvedBaselinePath"
} else {
    if (-not (Test-Path $resolvedBaselinePath -PathType Leaf)) {
        throw "Stable case baseline is missing: $resolvedBaselinePath"
    }
    $expectedBaseline = Get-Content $resolvedBaselinePath -Raw | ConvertFrom-Json
    $expectedJson = ([pscustomobject]@{
        schemaVersion = [int]$expectedBaseline.schemaVersion
        cases = @($expectedBaseline.cases | Sort-Object assembly, case)
    } | ConvertTo-Json -Depth 6)
    if ($expectedJson -cne $baselineJson) {
        throw "Real discovery differs from stable case baseline. Review the case migration and run this script explicitly with -UpdateBaseline only for an intentional inventory change."
    }
}

$resolvedOutputPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $root $OutputPath
}

$outputDirectory = Split-Path $resolvedOutputPath -Parent
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$document = [pscustomobject]@{
    schemaVersion = 3
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    repositoryHead = ([string]$repositoryHead).Trim()
    repositoryClean = $repositoryClean
    solution = 'AICopilot.slnx'
    productionUniverse = [pscustomobject]@{
        assemblyCount = @($productionUniverseEntries.assembly | Sort-Object -Unique).Count
        sourceCount = $productionUniverseEntries.Count
        sha256 = $productionUniverseSha256
        identitySha256 = $productionUniverseIdentitySha256
        sources = @($productionUniverseEntries)
    }
    productionAssemblies = @($productionAssemblyEntries)
    runnerBuildIdentity = [pscustomobject]@{
        runnerCount = $runnerBuildIdentityEntries.Count
        synchronizedAssemblyCount = $runnerBuildIdentitySynchronizedAssemblyCount
        sha256 = $runnerBuildIdentitySha256
        runners = @($runnerBuildIdentityEntries)
    }
    projects = @($inventory)
    cases = @($cases | Sort-Object assembly, case)
}
$document | ConvertTo-Json -Depth 5 | Set-Content $resolvedOutputPath -Encoding utf8

$requiredCount = @($inventory | Where-Object { $_.role -eq 'Runner' -and $_.required }).Count
$manualCount = @($inventory | Where-Object { $_.role -eq 'Runner' -and -not $_.required }).Count
Write-Host "AICopilot test inventory: required=$requiredCount, manual=$manualCount, support=$(@($inventory | Where-Object role -eq 'Support').Count)."
Write-Host "Inventory written to $resolvedOutputPath"
