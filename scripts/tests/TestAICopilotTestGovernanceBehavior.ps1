[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}

$ruleId = 'AI-TEST-GOV-001'
$policyPath = Join-Path $RepositoryRoot 'scripts/tests/TestAICopilotTestGovernancePolicy.ps1'
$reviewedBaselinePath = Join-Path $RepositoryRoot 'scripts/tests/baselines/aicopilot-test-governance.baseline.json'
$reviewedWaiverPath = Join-Path $RepositoryRoot 'scripts/tests/baselines/aicopilot-test-governance.waivers.json'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "aicopilot-test-governance-$([Guid]::NewGuid().ToString('N'))"
[void](New-Item $tempRoot -ItemType Directory -Force)

function Write-Utf8File {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    $directory = Split-Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [void](New-Item $directory -ItemType Directory -Force)
    }
    [System.IO.File]::WriteAllText($Path, $Value, [System.Text.UTF8Encoding]::new($false))
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)][object]$Value,
        [Parameter(Mandatory)][string]$Path
    )

    Write-Utf8File -Path $Path -Value "$(($Value | ConvertTo-Json -Depth 100))`n"
}

function Get-FixtureHash {
    param([Parameter(Mandatory)][string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function New-Traits {
    param(
        [string]$TestKind,
        [string]$Runtime,
        [string]$Risk,
        [string]$Capability,
        [string]$Owner,
        [string]$RegressionId
    )

    $traits = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace($TestKind)) { $traits.TestKind = @($TestKind) }
    if (-not [string]::IsNullOrWhiteSpace($Runtime)) { $traits.Runtime = @($Runtime) }
    if (-not [string]::IsNullOrWhiteSpace($Risk)) { $traits.Risk = @($Risk) }
    if (-not [string]::IsNullOrWhiteSpace($Capability)) { $traits.Capability = @($Capability) }
    if (-not [string]::IsNullOrWhiteSpace($Owner)) { $traits.Owner = @($Owner) }
    if (-not [string]::IsNullOrWhiteSpace($RegressionId)) { $traits.RegressionId = @($RegressionId) }
    return [pscustomobject]$traits
}

function New-TestRecord {
    param(
        [Parameter(Mandatory)][string]$Id,
        [string]$TypeName = 'Fixture.Tests.SampleTests',
        [string]$MethodName = 'Existing',
        [ValidateSet('Fact', 'Theory')][string]$AttributeCategory = 'Fact',
        [string]$TestAttributeType = 'Xunit.FactAttribute',
        [int]$InlineDataRows = 0,
        [string[]]$InlineDataSignatures = @(),
        [string[]]$ExecutionTypeNames = @(),
        [AllowNull()][object]$Traits = $null,
        [bool]$Disabled = $false
    )

    if ($null -eq $Traits) { $Traits = [pscustomobject]@{} }
    if ($ExecutionTypeNames.Count -eq 0) { $ExecutionTypeNames = @($TypeName) }
    if ($InlineDataRows -gt 0 -and $InlineDataSignatures.Count -eq 0) {
        $InlineDataSignatures = [string[]]@(1..$InlineDataRows | ForEach-Object { "fixture-inline-row-$_" })
    }

    $physicalId = "aicopilot-test-physical-v1:$(Get-FixtureHash "physical|$Id")"
    $logicalId = "aicopilot-test-decl-v1:$(Get-FixtureHash "logical|$Id")"
    $executionTypes = @($ExecutionTypeNames | ForEach-Object {
        [pscustomobject][ordered]@{
            id = "aicopilot-test-execution-v1:$(Get-FixtureHash "execution|$Id|$_")"
            name = $_
            traits = $Traits
        }
    })
    $projectedCases = if ($AttributeCategory -eq 'Theory' -and $InlineDataRows -gt 0) {
        $InlineDataRows * $executionTypes.Count
    }
    else {
        $executionTypes.Count
    }

    return [pscustomobject][ordered]@{
        id = $physicalId
        logicalId = $logicalId
        symbol = "$TypeName.$MethodName()"
        executionType = $TypeName
        declaringType = $TypeName
        methodName = $MethodName
        parameterSignature = ''
        attributeCategory = $AttributeCategory
        testAttributeType = $TestAttributeType
        testAttributePolicy = [pscustomobject][ordered]@{
            signature = "Skip=$(if ($Disabled) { 'disabled' } else { '' })|Explicit=False|SkipWhen=|SkipUnless=|SkipType=|SkipExceptions=|Timeout=0|DataPolicies="
            isDisabled = $Disabled
            skip = if ($Disabled) { 'disabled' } else { '' }
            explicit = $false
            skipWhen = ''
            skipUnless = ''
            skipType = ''
            skipExceptions = ''
            timeout = 0
        }
        inlineDataRows = $InlineDataRows
        inlineDataSignatures = [string[]]$InlineDataSignatures
        dynamicDataSources = [string[]]@()
        executionTypes = [object[]]$executionTypes
        projectedCases = $projectedCases
        traits = $Traits
    }
}

function New-Baseline {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Tests
    )

    $executionTemplates = if ($Tests.Count -eq 0) { 0 } else {
        [int](($Tests | ForEach-Object { @($_.executionTypes).Count } | Measure-Object -Sum).Sum)
    }
    $projectedCases = if ($Tests.Count -eq 0) { 0 } else {
        [int](($Tests | Measure-Object -Property projectedCases -Sum).Sum)
    }

    return [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        ruleId = $ruleId
        scanner = [pscustomobject][ordered]@{
            engine = 'behavior-fixture'
            activeDotnetSdk = 'fixture'
            metadataLoadContextSha256 = ('0' * 64)
        }
        # Synthetic snapshots intentionally reuse the reviewed registry instead of
        # maintaining a second list that could drift from the policy under test.
        allowedMetadata = ($script:reviewedAllowedMetadata | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20)
        projects = @([pscustomobject][ordered]@{
            projectPath = $ProjectPath
            projectName = $ProjectName
            isLegacy = $false
            freezeMode = 'None'
            frozenTypePatterns = [string[]]@()
            frozenSourceFiles = [string[]]@()
            allowedNewTestKinds = [string[]]@()
            allowedNewRuntimes = [string[]]@()
            forbiddenNewTestKinds = [string[]]@()
            discoveryCeilings = [object[]]@()
            protectBaselineRemovals = $true
            baselineDeclarations = $Tests.Count
            baselineExecutionTemplates = $executionTemplates
            baselineProjectedCases = $projectedCases
            tests = [object[]]$Tests
        })
    }
}

function New-Snapshot {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Tests
    )

    $executionTemplates = if ($Tests.Count -eq 0) { 0 } else {
        [int](($Tests | ForEach-Object { @($_.executionTypes).Count } | Measure-Object -Sum).Sum)
    }
    $projectedCases = if ($Tests.Count -eq 0) { 0 } else {
        [int](($Tests | Measure-Object -Property projectedCases -Sum).Sum)
    }

    return [pscustomobject][ordered]@{
        projectPath = $ProjectPath
        projectName = $ProjectName
        assemblyPath = 'fixture/Fixture.Tests.dll'
        assemblySha256 = ('1' * 64)
        declarations = $Tests.Count
        executionTemplates = $executionTemplates
        projectedCases = $projectedCases
        tests = [object[]]$Tests
    }
}

function New-WaiverManifest {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Waivers)

    return [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        ruleId = $ruleId
        waivers = [object[]]$Waivers
    }
}

function Invoke-SnapshotValidation {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][object]$Waivers
    )

    $baselinePath = Join-Path $tempRoot "$Name.baseline.json"
    $snapshotPath = Join-Path $tempRoot "$Name.snapshot.json"
    $waiverPath = Join-Path $tempRoot "$Name.waivers.json"
    Write-JsonFile -Value $Baseline -Path $baselinePath
    Write-JsonFile -Value $Snapshot -Path $snapshotPath
    Write-JsonFile -Value $Waivers -Path $waiverPath

    $output = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateSnapshot `
        -RepositoryRoot $RepositoryRoot `
        -BaselinePath $baselinePath `
        -WaiverPath $waiverPath `
        -CurrentSnapshotPath $snapshotPath 2>&1

    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Assert-Accepted {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][object]$Waivers
    )

    $result = Invoke-SnapshotValidation -Name $Name -Baseline $Baseline -Snapshot $Snapshot -Waivers $Waivers
    if ($result.ExitCode -ne 0) {
        throw "Fixture '$Name' should pass:`n$($result.Output)"
    }
    Write-Host "Accepted AICopilot test-governance fixture: $Name"
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][object]$Waivers,
        [Parameter(Mandatory)][string]$ExpectedCode
    )

    $result = Invoke-SnapshotValidation -Name $Name -Baseline $Baseline -Snapshot $Snapshot -Waivers $Waivers
    if ($result.ExitCode -eq 0 -or -not $result.Output.Contains($ExpectedCode, [StringComparison]::Ordinal)) {
        throw "Fixture '$Name' should fail with $ExpectedCode; exit=$($result.ExitCode):`n$($result.Output)"
    }
    Write-Host "Rejected AICopilot test-governance fixture: $Name ($ExpectedCode)"
}

function Invoke-StaticValidation {
    param([Parameter(Mandatory)][string]$ValidationRoot)

    $output = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateStatic `
        -RepositoryRoot $ValidationRoot `
        -BaselinePath (Join-Path $ValidationRoot 'scripts/tests/baselines/aicopilot-test-governance.baseline.json') `
        -WaiverPath (Join-Path $ValidationRoot 'scripts/tests/baselines/aicopilot-test-governance.waivers.json') `
        -Configuration Release 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Assert-StaticRejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ValidationRoot,
        [Parameter(Mandatory)][scriptblock]$Mutate,
        [Parameter(Mandatory)][scriptblock]$Restore,
        [Parameter(Mandatory)][string]$ExpectedCode
    )

    & $Mutate
    try {
        $result = Invoke-StaticValidation -ValidationRoot $ValidationRoot
        if ($result.ExitCode -eq 0 -or -not $result.Output.Contains($ExpectedCode, [StringComparison]::Ordinal)) {
            throw "Static fixture '$Name' should fail with $ExpectedCode; exit=$($result.ExitCode):`n$($result.Output)"
        }
        Write-Host "Rejected AICopilot static-governance fixture: $Name ($ExpectedCode)"
    }
    finally {
        & $Restore
    }
}

function Copy-RepositoryFixture {
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [Parameter(Mandatory)][string]$DestinationRoot
    )

    $excludedDirectoryNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @('.git', '.vs', '.idea', 'bin', 'obj', 'node_modules', 'TestResults', 'artifacts')) {
        [void]$excludedDirectoryNames.Add($name)
    }

    function Copy-DirectoryContent {
        param([string]$Source, [string]$Destination)

        [void](New-Item $Destination -ItemType Directory -Force)
        foreach ($entry in Get-ChildItem $Source -Force) {
            if ($entry.PSIsContainer) {
                if (-not $excludedDirectoryNames.Contains($entry.Name)) {
                    Copy-DirectoryContent -Source $entry.FullName -Destination (Join-Path $Destination $entry.Name)
                }
                continue
            }
            [System.IO.File]::Copy($entry.FullName, (Join-Path $Destination $entry.Name), $true)
        }
    }

    Copy-DirectoryContent -Source $SourceRoot -Destination $DestinationRoot
}

try {
    foreach ($requiredPath in @($policyPath, $reviewedBaselinePath, $reviewedWaiverPath)) {
        if (-not (Test-Path $requiredPath -PathType Leaf)) {
            throw "Required AICopilot test-governance asset is missing: $requiredPath"
        }
    }

    $reviewedBaseline = Get-Content $reviewedBaselinePath -Raw | ConvertFrom-Json -Depth 100
    if ($reviewedBaseline.ruleId -ne $ruleId -or @($reviewedBaseline.projects).Count -ne 4) {
        throw 'Reviewed AICopilot baseline must use AI-TEST-GOV-001 and contain exactly four xUnit test projects.'
    }
    $reviewedRunnerCases = [int](($reviewedBaseline.projects | Measure-Object -Property baselineRunnerCases -Sum).Sum)
    if ($reviewedRunnerCases -ne 1022) {
        throw "Reviewed AICopilot baseline must contain 1022 Release runner cases; found $reviewedRunnerCases."
    }
    $script:reviewedAllowedMetadata = $reviewedBaseline.allowedMetadata

    $currentStatic = Invoke-StaticValidation -ValidationRoot $RepositoryRoot
    if ($currentStatic.ExitCode -ne 0) {
        throw "Current repository static policy should pass:`n$($currentStatic.Output)"
    }
    Write-Host 'Accepted AICopilot static-governance fixture: current-repository-static-policy'

    $normalizationOutput = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateRunnerCaseNormalization `
        -RepositoryRoot $RepositoryRoot 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Runner display-name normalization fixture should pass:`n$(($normalizationOutput | Out-String).Trim())"
    }
    Write-Host 'Accepted AICopilot runner display-name normalization fixture: cross-os-path-and-ordinal-order'

    $projectPath = 'src/tests/Fixture.Tests/Fixture.Tests.csproj'
    $projectName = 'Fixture.Tests'
    $emptyWaivers = New-WaiverManifest -Waivers @()
    $classified = New-Traits -TestKind Unit -Runtime Pure -Risk P1 -Capability TestGovernance -Owner AI.Tests
    $existing = New-TestRecord -Id 'existing'
    $newClassified = New-TestRecord -Id 'new-classified' -MethodName 'NewClassified' -Traits $classified

    Assert-Accepted -Name 'unchanged-baseline' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Waivers $emptyWaivers

    Assert-Rejected -Name 'baseline-fact-removal' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @()) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-REMOVAL"

    $unclassified = New-TestRecord -Id 'new-unclassified' -MethodName 'NewUnclassified'
    Assert-Rejected -Name 'new-test-without-classification' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $unclassified)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-CLASSIFICATION"

    Assert-Accepted -Name 'new-test-with-complete-classification' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newClassified)) `
        -Waivers $emptyWaivers

    $classifiedExisting = New-TestRecord -Id 'classified-existing' -MethodName 'ClassifiedExisting' -Traits $classified
    $classificationRemoved = New-TestRecord -Id 'classified-existing' -MethodName 'ClassifiedExisting'
    Assert-Rejected -Name 'existing-test-cannot-silently-remove-classification' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($classifiedExisting)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($classificationRemoved)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-CLASSIFICATION"

    $frozenBaseline = New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)
    $frozenBaseline.projects[0].freezeMode = 'All'
    Assert-Rejected -Name 'fully-frozen-project-cannot-add-classified-test' `
        -Baseline $frozenBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newClassified)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-FROZEN"

    $theoryFour = New-TestRecord -Id 'theory' -MethodName 'Rows' -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -InlineDataRows 4
    $theoryThree = New-TestRecord -Id 'theory' -MethodName 'Rows' -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -InlineDataRows 3 -Traits $classified
    Assert-Rejected -Name 'theory-inline-row-decrease' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryFour)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryThree)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-INLINE-DATA"

    $replacementSignatures = @('fixture-inline-row-1', 'fixture-inline-row-2', 'fixture-inline-row-3', 'fixture-inline-row-replacement')
    $theoryReplacement = New-TestRecord -Id 'theory' -MethodName 'Rows' -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -InlineDataRows 4 -InlineDataSignatures $replacementSignatures -Traits $classified
    Assert-Rejected -Name 'same-count-inline-row-replacement' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryFour)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryReplacement)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-INLINE-DATA"

    $dynamicOriginal = New-TestRecord -Id 'dynamic-data' -MethodName 'Rows' -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -Traits $classified
    $dynamicOriginal.dynamicDataSources = @('Xunit.MemberDataAttribute|ctor=System.String=T3JpZ2luYWw=')
    $dynamicReplacement = New-TestRecord -Id 'dynamic-data' -MethodName 'Rows' -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -Traits $classified
    $dynamicReplacement.dynamicDataSources = @('Xunit.MemberDataAttribute|ctor=System.String=UmVwbGFjZWQ=')
    Assert-Rejected -Name 'same-count-member-data-source-replacement' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($dynamicOriginal)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($dynamicReplacement)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-DISABLED"

    $twoExecutions = New-TestRecord -Id 'inherited' -TypeName 'Fixture.Tests.ContractBase' -ExecutionTypeNames @('Fixture.Tests.One', 'Fixture.Tests.Two')
    $oneExecution = New-TestRecord -Id 'inherited' -TypeName 'Fixture.Tests.ContractBase' -ExecutionTypeNames @('Fixture.Tests.One') -Traits $classified
    Assert-Rejected -Name 'inherited-execution-template-decrease' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($twoExecutions)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($oneExecution)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-CASE-DECREASE"

    $disabled = New-TestRecord -Id 'existing' -Traits $classified -Disabled $true
    Assert-Rejected -Name 'required-test-cannot-add-skip' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($disabled)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-DISABLED"

    $unknownCustomFact = New-TestRecord -Id 'unknown-custom-fact' -MethodName 'Custom' -TestAttributeType 'Fixture.Tests.CustomFactAttribute' -Traits $classified
    Assert-Rejected -Name 'unknown-custom-fact-fails-closed' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $unknownCustomFact)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-SCAN"

    $expiredWaiver = [pscustomobject][ordered]@{
        id = 'AI-TEST-GOV-001-W001'
        projectPath = $projectPath
        symbol = $existing.id
        changeKind = 'Remove'
        regressionId = 'AI-REG-001'
        targetProject = 'src/tests/Fixture.TargetTests/Fixture.TargetTests.csproj'
        testKind = 'Unit'
        owner = 'AI.Tests'
        reason = 'Expired behavior fixture; must not authorize removal.'
        approvedBy = 'ShuJinHao'
        expiresOn = [DateTime]::UtcNow.AddDays(-1).ToString('yyyy-MM-dd')
    }
    Assert-Rejected -Name 'expired-waiver-cannot-authorize-removal' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @()) `
        -Waivers (New-WaiverManifest -Waivers @($expiredWaiver)) `
        -ExpectedCode "$ruleId-WAIVER"

    $staticRoot = Join-Path $tempRoot 'static-repository'
    Copy-RepositoryFixture -SourceRoot $RepositoryRoot -DestinationRoot $staticRoot

    $staticBaselinePath = Join-Path $staticRoot 'scripts/tests/baselines/aicopilot-test-governance.baseline.json'
    $staticBaselineOriginal = Get-Content $staticBaselinePath -Raw
    Assert-StaticRejected -Name 'content-freeze-keys-cannot-be-replaced-at-same-count' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $staticBaselinePath -Value $staticBaselineOriginal.Replace(
                'src/tests/AICopilot.ArchitectureTests/DddAggregateBoundaryTests.cs',
                'AGENTS.md')
        } `
        -Restore { Write-Utf8File -Path $staticBaselinePath -Value $staticBaselineOriginal } `
        -ExpectedCode "$ruleId-BASELINE"

    $solutionPath = Join-Path $staticRoot 'AICopilot.slnx'
    $solutionOriginal = Get-Content $solutionPath -Raw
    Assert-StaticRejected -Name 'solution-cannot-drop-production-project' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $solutionPath -Value $solutionOriginal.Replace(
                '    <Project Path="src/hosts/AICopilot.DataWorker/AICopilot.DataWorker.csproj" />' + "`n",
                '')
        } `
        -Restore { Write-Utf8File -Path $solutionPath -Value $solutionOriginal } `
        -ExpectedCode "$ruleId-PROJECT"

    $globalJsonPath = Join-Path $staticRoot 'global.json'
    $globalJsonOriginal = Get-Content $globalJsonPath -Raw
    Assert-StaticRejected -Name 'global-json-cannot-drift-sdk-selection' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $globalJsonPath -Value $globalJsonOriginal.Replace('"latestFeature"', '"latestMajor"') } `
        -Restore { Write-Utf8File -Path $globalJsonPath -Value $globalJsonOriginal } `
        -ExpectedCode "$ruleId-CONFIG"

    $productionProjectPath = Join-Path $staticRoot 'src/shared/AICopilot.SharedKernel/AICopilot.SharedKernel.csproj'
    $productionProjectOriginal = Get-Content $productionProjectPath -Raw
    Assert-StaticRejected -Name 'mixed-case-msbuild-lifecycle-property-cannot-bypass-gate' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $productionProjectPath -Value $productionProjectOriginal.Replace(
                '</Project>',
                '  <PropertyGroup><directorybuildtargetspath>hidden.targets</directorybuildtargetspath></PropertyGroup></Project>')
        } `
        -Restore { Write-Utf8File -Path $productionProjectPath -Value $productionProjectOriginal } `
        -ExpectedCode "$ruleId-BYPASS"

    Assert-StaticRejected -Name 'project-restore-target-cannot-run-before-governance' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $productionProjectPath -Value $productionProjectOriginal.Replace(
                '<Project Sdk="Microsoft.NET.Sdk">',
                '<Project Sdk="Microsoft.NET.Sdk" InitialTargets="HiddenRestoreTarget"><Target Name="HiddenRestoreTarget" BeforeTargets="Restore" />')
        } `
        -Restore { Write-Utf8File -Path $productionProjectPath -Value $productionProjectOriginal } `
        -ExpectedCode "$ruleId-BYPASS"

    Assert-StaticRejected -Name 'indirect-project-reference-cannot-hide-test-dependency' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $productionProjectPath -Value $productionProjectOriginal.Replace(
                '</Project>',
                '  <PropertyGroup><HiddenTestProject>../../tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj</HiddenTestProject></PropertyGroup><ItemGroup><ProjectReference Include="$(HiddenTestProject)" /></ItemGroup></Project>')
        } `
        -Restore { Write-Utf8File -Path $productionProjectPath -Value $productionProjectOriginal } `
        -ExpectedCode "$ruleId-BYPASS"

    $hiddenPropsPath = Join-Path $staticRoot 'src/Directory.Build.props'
    Assert-StaticRejected -Name 'src-directory-build-props-cannot-shadow-reviewed-graph' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $hiddenPropsPath -Value '<Project />' } `
        -Restore { Remove-Item $hiddenPropsPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $npmrcPath = Join-Path $staticRoot 'src/vues/AICopilot.Web/.npmrc'
    Assert-StaticRejected -Name 'repository-npmrc-cannot-override-package-source' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $npmrcPath -Value 'registry=https://unreviewed.invalid/' } `
        -Restore { Remove-Item $npmrcPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CONFIG"

    $codeOwnersPath = Join-Path $staticRoot '.github/CODEOWNERS'
    $codeOwnersOriginal = Get-Content $codeOwnersPath -Raw
    Assert-StaticRejected -Name 'test-method-bodies-require-code-owner' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $codeOwnersPath -Value $codeOwnersOriginal.Replace("/src/tests/**/*.cs @ShuJinHao`n", '')
        } `
        -Restore { Write-Utf8File -Path $codeOwnersPath -Value $codeOwnersOriginal } `
        -ExpectedCode "$ruleId-CODEOWNER"

    Assert-StaticRejected -Name 'later-code-owner-rule-cannot-shadow-test-ownership' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $codeOwnersPath -Value "$($codeOwnersOriginal.TrimEnd())`n/src/tests/**/*.cs @UnreviewedOwner`n"
        } `
        -Restore { Write-Utf8File -Path $codeOwnersPath -Value $codeOwnersOriginal } `
        -ExpectedCode "$ruleId-CODEOWNER"

    $goldenCasePath = Join-Path $staticRoot 'src/tests/AICopilot.AiEvalTests/cases/approval-resume.json'
    $goldenCaseOriginal = Get-Content $goldenCasePath -Raw
    Assert-StaticRejected -Name 'same-count-golden-json-replacement' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $goldenCasePath -Value $goldenCaseOriginal.Replace('queryDeviceStatus', 'queryOtherStatus') } `
        -Restore { Write-Utf8File -Path $goldenCasePath -Value $goldenCaseOriginal } `
        -ExpectedCode "$ruleId-GOLDEN"

    $playwrightSmokePath = Join-Path $staticRoot 'src/vues/AICopilot.Web/tests/smoke/acceptance.spec.ts'
    $playwrightSmokeOriginal = Get-Content $playwrightSmokePath -Raw
    Assert-StaticRejected -Name 'playwright-cannot-add-runtime-skip' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $playwrightSmokePath -Value "$playwrightSmokeOriginal`ntest.skip(true, 'unreviewed skip')" } `
        -Restore { Write-Utf8File -Path $playwrightSmokePath -Value $playwrightSmokeOriginal } `
        -ExpectedCode "$ruleId-UI-SKIP"

    Assert-StaticRejected -Name 'playwright-cannot-hide-describe-skip' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $playwrightSmokePath -Value "$playwrightSmokeOriginal`ntest.describe.skip('hidden suite', () => {})" } `
        -Restore { Write-Utf8File -Path $playwrightSmokePath -Value $playwrightSmokeOriginal } `
        -ExpectedCode "$ruleId-UI-FROZEN"

    $vitestUnitPath = Join-Path $staticRoot 'src/vues/AICopilot.Web/tests/unit/agentRunNotice.spec.ts'
    $vitestUnitOriginal = Get-Content $vitestUnitPath -Raw
    Assert-StaticRejected -Name 'vitest-required-unit-cannot-add-skip' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $vitestUnitPath -Value "$vitestUnitOriginal`nit.skip('hidden unit', () => {})" } `
        -Restore { Write-Utf8File -Path $vitestUnitPath -Value $vitestUnitOriginal } `
        -ExpectedCode "$ruleId-DISABLED"

    $webPackagePath = Join-Path $staticRoot 'src/vues/AICopilot.Web/package.json'
    $webPackageOriginal = Get-Content $webPackagePath -Raw
    Assert-StaticRejected -Name 'vitest-script-cannot-be-replaced-with-noop' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $webPackagePath -Value $webPackageOriginal.Replace('"test:unit": "vitest run --config vitest.config.ts"', '"test:unit": "echo pass"') } `
        -Restore { Write-Utf8File -Path $webPackagePath -Value $webPackageOriginal } `
        -ExpectedCode "$ruleId-UI-FROZEN"

    $webPackageLockPath = Join-Path $staticRoot 'src/vues/AICopilot.Web/package-lock.json'
    $webPackageLockOriginal = Get-Content $webPackageLockPath -Raw
    Assert-StaticRejected -Name 'vitest-package-lock-cannot-drift' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $webPackageLockPath -Value "$($webPackageLockOriginal.TrimEnd()) `n" } `
        -Restore { Write-Utf8File -Path $webPackageLockPath -Value $webPackageLockOriginal } `
        -ExpectedCode "$ruleId-UI-FROZEN"

    $deploymentBehaviorPath = Join-Path $staticRoot 'deploy/enterprise-ai/tests/deployment-behavior.sh'
    $deploymentBehaviorOriginal = Get-Content $deploymentBehaviorPath -Raw
    Assert-StaticRejected -Name 'deployment-behavior-case-cannot-be-removed' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $deploymentBehaviorPath -Value $deploymentBehaviorOriginal.Replace("printf 'TEST malicious arithmetic payloads are rejected without command execution\n'`n", '') } `
        -Restore { Write-Utf8File -Path $deploymentBehaviorPath -Value $deploymentBehaviorOriginal } `
        -ExpectedCode "$ruleId-DEPLOYMENT-FROZEN"

    $reviewedAiSecSourcePath = Join-Path $staticRoot 'src/tests/AICopilot.BackendTests/IdentityManagerTestScope.cs'
    $reviewedAiSecSourceOriginal = Get-Content $reviewedAiSecSourcePath -Raw
    Assert-StaticRejected -Name 'reviewed-ai-sec-051-test-body-change' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $reviewedAiSecSourcePath -Value "$reviewedAiSecSourceOriginal`n// mutation" } `
        -Restore { Write-Utf8File -Path $reviewedAiSecSourcePath -Value $reviewedAiSecSourceOriginal } `
        -ExpectedCode "$ruleId-FROZEN"

    $backendBodyPath = Join-Path $staticRoot 'src/tests/AICopilot.BackendTests/AcceptanceBaselineTests.cs'
    $backendBodyOriginal = Get-Content $backendBodyPath -Raw
    Assert-StaticRejected -Name 'backend-test-body-cannot-be-hollowed' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $backendBodyPath -Value "$backendBodyOriginal`n// assertion-body mutation" } `
        -Restore { Write-Utf8File -Path $backendBodyPath -Value $backendBodyOriginal } `
        -ExpectedCode "$ruleId-FROZEN"

    $architectureSourcePath = Join-Path $staticRoot 'src/tests/AICopilot.ArchitectureTests/DddAggregateBoundaryTests.cs'
    $architectureSourceOriginal = Get-Content $architectureSourcePath -Raw
    Assert-StaticRejected -Name 'architecture-source-content-freeze' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $architectureSourcePath -Value "$architectureSourceOriginal`n// mutation" } `
        -Restore { Write-Utf8File -Path $architectureSourcePath -Value $architectureSourceOriginal } `
        -ExpectedCode "$ruleId-FROZEN"

    $liveSourcePath = Join-Path $staticRoot 'src/tests/AICopilot.CloudAiReadLiveTests/CloudAiReadLiveContractTests.cs'
    $liveSourceOriginal = Get-Content $liveSourcePath -Raw
    Assert-StaticRejected -Name 'live-contract-source-content-freeze' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $liveSourcePath -Value "$liveSourceOriginal`n// mutation" } `
        -Restore { Write-Utf8File -Path $liveSourcePath -Value $liveSourceOriginal } `
        -ExpectedCode "$ruleId-FROZEN"

    $duplicateWorkflowPath = Join-Path $staticRoot '.github/workflows/duplicate-cloud-check.yml'
    Assert-StaticRejected -Name 'duplicate-required-check-identity' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $duplicateWorkflowPath -Value @'
name: aicopilot-ci
on:
  workflow_dispatch:
jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - run: true
'@
        } `
        -Restore { Remove-Item $duplicateWorkflowPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CI"

    $quotedDuplicateWorkflowPath = Join-Path $staticRoot '.github/workflows/duplicate-cloud-check-quoted.yml'
    Assert-StaticRejected -Name 'quoted-duplicate-workflow-name' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $quotedDuplicateWorkflowPath -Value @'
name: "aicopilot-ci"
on: { workflow_dispatch: {} }
jobs:
  other:
    runs-on: ubuntu-latest
    steps: []
'@
        } `
        -Restore { Remove-Item $quotedDuplicateWorkflowPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CI"

    $displayNameDuplicateWorkflowPath = Join-Path $staticRoot '.github/workflows/duplicate-cloud-check-display.yml'
    Assert-StaticRejected -Name 'duplicate-job-display-name' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $displayNameDuplicateWorkflowPath -Value @'
name: other-workflow
on: { workflow_dispatch: {} }
jobs:
  other:
    name: build-test
    runs-on: ubuntu-latest
    steps: []
'@
        } `
        -Restore { Remove-Item $displayNameDuplicateWorkflowPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CI"

    $inlineDuplicateWorkflowPath = Join-Path $staticRoot '.github/workflows/duplicate-cloud-check-inline.yml'
    Assert-StaticRejected -Name 'inline-duplicate-job-key' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $inlineDuplicateWorkflowPath -Value @'
name: other-workflow
on: { workflow_dispatch: {} }
jobs: { "build-test" : { runs-on: ubuntu-latest, steps: [] } }
'@
        } `
        -Restore { Remove-Item $inlineDuplicateWorkflowPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CI"

    $expressionDuplicateWorkflowPath = Join-Path $staticRoot '.github/workflows/expression-duplicate-check.yml'
    Assert-StaticRejected -Name 'expression-composed-duplicate-check-cannot-hide' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $expressionDuplicateWorkflowPath -Value @'
name: other-workflow
on: { workflow_dispatch: {} }
jobs:
  bypass:
    name: ${{ format('build-{0}', 'test') }}
    runs-on: ubuntu-latest
    steps: []
'@
        } `
        -Restore { Remove-Item $expressionDuplicateWorkflowPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CI"

    $canonicalWorkflowPath = Join-Path $staticRoot '.github/workflows/aicopilot-ci.yml'
    $canonicalWorkflowOriginal = Get-Content $canonicalWorkflowPath -Raw
    Assert-StaticRejected -Name 'canonical-workflow-cannot-add-duplicate-job' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $canonicalWorkflowPath -Value "$($canonicalWorkflowOriginal.TrimEnd())`n  bypass-check:`n    name: build-test`n    runs-on: ubuntu-latest`n    steps:`n      - run: true`n"
        } `
        -Restore { Write-Utf8File -Path $canonicalWorkflowPath -Value $canonicalWorkflowOriginal } `
        -ExpectedCode "$ruleId-CI"

    Assert-StaticRejected -Name 'canonical-workflow-cannot-add-pull-request-target' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $canonicalWorkflowPath -Value $canonicalWorkflowOriginal.Replace("on:`n", "on:`n  pull_request_target: {}`n") } `
        -Restore { Write-Utf8File -Path $canonicalWorkflowPath -Value $canonicalWorkflowOriginal } `
        -ExpectedCode "$ruleId-CI"

    $hiddenIsTestProject = Join-Path $staticRoot 'src/tests/Hidden.IsTestProject.Tests/Hidden.IsTestProject.Tests.csproj'
    Assert-StaticRejected -Name 'hidden-is-test-project' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenIsTestProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenIsTestProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-PROJECT"

    $hiddenMSTestSdkProject = Join-Path $staticRoot 'src/hidden/HiddenQualityGate/HiddenQualityGate.csproj'
    Assert-StaticRejected -Name 'mstest-sdk-cannot-hide-as-production-project' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenMSTestSdkProject -Value @'
<Project Sdk="MSTest.Sdk/3.8.3">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenMSTestSdkProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $dotHiddenProject = Join-Path $staticRoot '.hidden/Stealth/Stealth.csproj'
    Assert-StaticRejected -Name 'dot-directory-cannot-hide-test-project' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $dotHiddenProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Join-Path $staticRoot '.hidden') -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-PROJECT"

    $hiddenPackageProject = Join-Path $staticRoot 'src/tests/Hidden.Package.Tests/Hidden.Package.Tests.csproj'
    Assert-StaticRejected -Name 'hidden-test-package-project' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenPackageProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="xunit.v3" /></ItemGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenPackageProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-PROJECT"

    $hiddenNUnitProject = Join-Path $staticRoot 'src/Hidden.NUnit.Tests/Hidden.NUnit.Tests.csproj'
    Assert-StaticRejected -Name 'non-xunit-test-project-outside-reviewed-root' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenNUnitProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
  <ItemGroup><PackageReference Include="NUnit" /></ItemGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenNUnitProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $hiddenMSTestProject = Join-Path $staticRoot 'src/Hidden.MSTest.Tests/Hidden.MSTest.Tests.csproj'
    Assert-StaticRejected -Name 'mstest-project-outside-reviewed-root' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenMSTestProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
  <ItemGroup><PackageReference Include="MSTest.TestFramework" /></ItemGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenMSTestProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $hiddenTUnitProject = Join-Path $staticRoot 'src/Hidden.TUnit.Tests/Hidden.TUnit.Tests.csproj'
    Assert-StaticRejected -Name 'tunit-project-outside-reviewed-root' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenTUnitProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
  <ItemGroup><PackageReference Include="TUnit" /></ItemGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenTUnitProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $hiddenTestingPlatformProject = Join-Path $staticRoot 'src/Hidden.TestingPlatform.Tests/Hidden.TestingPlatform.Tests.csproj'
    Assert-StaticRejected -Name 'microsoft-testing-platform-project-outside-reviewed-root' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenTestingPlatformProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
  <ItemGroup><PackageReference Include="Microsoft.Testing.Platform" /></ItemGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenTestingPlatformProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $hiddenTestingPlatformMarkerProject = Join-Path $staticRoot 'src/Hidden.TestingPlatformMarker.Tests/Hidden.TestingPlatformMarker.Tests.csproj'
    Assert-StaticRejected -Name 'testing-platform-marker-outside-reviewed-root' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenTestingPlatformMarkerProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><TestingPlatformApplication>true</TestingPlatformApplication></PropertyGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenTestingPlatformMarkerProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $supportProject = Join-Path $staticRoot 'src/tests/AICopilot.Testing.McpServer/AICopilot.Testing.McpServer.csproj'
    $supportProjectOriginal = Get-Content $supportProject -Raw
    Assert-StaticRejected -Name 'support-host-cannot-become-hidden-test-project' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $supportProject -Value $supportProjectOriginal.Replace(
                '<OutputType>Exe</OutputType>',
                '<OutputType>Exe</OutputType><IsTestProject>true</IsTestProject>')
        } `
        -Restore { Write-Utf8File -Path $supportProject -Value $supportProjectOriginal } `
        -ExpectedCode "$ruleId-PROJECT"

    $supportProgram = Join-Path $staticRoot 'src/tests/AICopilot.Testing.McpServer/Program.cs'
    $supportProgramOriginal = Get-Content $supportProgram -Raw
    Assert-StaticRejected -Name 'support-host-cannot-hide-qualified-xunit-fact' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $supportProject -Value $supportProjectOriginal.Replace('</Project>', '  <ItemGroup><Reference Include="xunit.core"><HintPath>fake/xunit.core.dll</HintPath></Reference></ItemGroup></Project>')
            Write-Utf8File -Path $supportProgram -Value "$supportProgramOriginal`npublic sealed class HiddenSupportTest { [Xunit.Fact] public void Hidden() { } }"
        } `
        -Restore {
            Write-Utf8File -Path $supportProject -Value $supportProjectOriginal
            Write-Utf8File -Path $supportProgram -Value $supportProgramOriginal
        } `
        -ExpectedCode "$ruleId-BYPASS"

    $nestedSourceTargets = Join-Path $staticRoot 'src/Directory.Build.targets'
    Assert-StaticRejected -Name 'src-directory-build-targets-cannot-shadow-root-gate' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $nestedSourceTargets -Value '<Project />' } `
        -Restore { Remove-Item $nestedSourceTargets -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $dotHiddenTargets = Join-Path $staticRoot '.hidden/Directory.Build.targets'
    Assert-StaticRejected -Name 'dot-directory-cannot-hide-shadow-targets' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $dotHiddenTargets -Value '<Project />' } `
        -Restore { Remove-Item (Join-Path $staticRoot '.hidden') -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $serviceLayerProject = Join-Path $staticRoot 'src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj'
    $serviceLayerProjectOriginal = Get-Content $serviceLayerProject -Raw
    Assert-StaticRejected -Name 'conditional-is-test-project-cannot-bypass-build-gate' -ValidationRoot $staticRoot `
        -Mutate {
            $mutated = $serviceLayerProjectOriginal.Replace(
                '<IsTestProject>true</IsTestProject>',
                '<IsTestProject Condition="''$(CI)'' == ''true''">true</IsTestProject>')
            Write-Utf8File -Path $serviceLayerProject -Value $mutated
        } `
        -Restore { Write-Utf8File -Path $serviceLayerProject -Value $serviceLayerProjectOriginal } `
        -ExpectedCode "$ruleId-BYPASS"

    Assert-StaticRejected -Name 'project-runner-override' -ValidationRoot $staticRoot `
        -Mutate {
            $mutated = $serviceLayerProjectOriginal.Replace(
                '</Project>',
                "  <ItemGroup><None Include=`"disabled.runner.json`" Link=`"xunit.runner.json`" CopyToOutputDirectory=`"Always`" /></ItemGroup>`n</Project>")
            Write-Utf8File -Path $serviceLayerProject -Value $mutated
        } `
        -Restore { Write-Utf8File -Path $serviceLayerProject -Value $serviceLayerProjectOriginal } `
        -ExpectedCode "$ruleId-CONFIG"

    $assemblyRunnerConfig = Join-Path $staticRoot 'src/tests/AICopilot.BackendTests/AICopilot.BackendTests.xunit.runner.json'
    Assert-StaticRejected -Name 'assembly-specific-runner-config' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $assemblyRunnerConfig -Value "{`"failSkips`":false}`n" } `
        -Restore { Remove-Item $assemblyRunnerConfig -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CONFIG"

    $runSettingsPath = Join-Path $staticRoot 'aicopilot.runsettings'
    Assert-StaticRejected -Name 'runsettings-cannot-filter-required-tests' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $runSettingsPath -Value '<RunSettings><RunConfiguration><TestCaseFilter>Category!=Required</TestCaseFilter></RunConfiguration></RunSettings>' } `
        -Restore { Remove-Item $runSettingsPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CONFIG"

    $assertSkipPath = Join-Path $staticRoot 'src/tests/AICopilot.BackendTests/RuntimeSkipBypassFixture.cs'
    Assert-StaticRejected -Name 'runtime-assert-skip' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $assertSkipPath -Value 'namespace AICopilot.BackendTests; internal static class RuntimeSkipBypassFixture { public static void Bypass() => Xunit.Assert.Skip("disabled"); }' } `
        -Restore { Remove-Item $assertSkipPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-DISABLED"

    $aliasedAssertSkipPath = Join-Path $staticRoot 'src/tests/AICopilot.BackendTests/AliasedRuntimeSkipBypassFixture.cs'
    Assert-StaticRejected -Name 'aliased-runtime-skip' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $aliasedAssertSkipPath -Value 'using TestAssert = Xunit.Assert; namespace AICopilot.BackendTests; internal static class AliasedRuntimeSkipBypassFixture { public static void Bypass() => TestAssert.Skip("disabled"); }' } `
        -Restore { Remove-Item $aliasedAssertSkipPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-DISABLED"

    $workflowPath = Join-Path $staticRoot '.github/workflows/aicopilot-ci.yml'
    $workflowOriginal = Get-Content $workflowPath -Raw
    Assert-StaticRejected -Name 'required-job-timeout-cannot-exceed-25-minutes' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace('    timeout-minutes: 25', '    timeout-minutes: 60') } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedCode "$ruleId-CI"

    Assert-StaticRejected -Name 'required-command-cannot-hide-in-dead-shell-branch' -ValidationRoot $staticRoot `
        -Mutate {
            $requiredLine = '          ./scripts/tests/TestAICopilotTestGovernanceBehavior.ps1'
            if (-not $workflowOriginal.Contains($requiredLine, [StringComparison]::Ordinal)) {
                throw 'Dead-branch fixture could not locate the reviewed governance command.'
            }
            $deadBranch = @'
          if false; then
            ./scripts/tests/TestAICopilotTestGovernanceBehavior.ps1
          fi
'@
            Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace($requiredLine, $deadBranch.TrimEnd())
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedCode "$ruleId-CI"

    Assert-StaticRejected -Name 'deployment-evidence-pipeline-cannot-drop-pipefail' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace(
                "          set -euo pipefail`n          bash deploy/enterprise-ai/tests/deployment-behavior.sh 2>&1 | tee artifacts/test-results/deployment-behavior.log",
                "          set -eu`n          bash deploy/enterprise-ai/tests/deployment-behavior.sh 2>&1 | tee artifacts/test-results/deployment-behavior.log")
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedCode "$ruleId-CI-PIPELINE"

    Assert-StaticRejected -Name 'deployment-evidence-pipeline-cannot-drop-stderr' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace(
                'bash deploy/enterprise-ai/tests/deployment-behavior.sh 2>&1 | tee artifacts/test-results/deployment-behavior.log',
                'bash deploy/enterprise-ai/tests/deployment-behavior.sh | tee artifacts/test-results/deployment-behavior.log')
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedCode "$ruleId-CI-PIPELINE"

    $runnerFixtureRoot = Join-Path $tempRoot 'runner-output'
    $tamperedRunnerConfig = Join-Path $runnerFixtureRoot 'xunit.runner.json'
    Write-Utf8File -Path $tamperedRunnerConfig -Value "{`n  `"failSkips`": false`n}`n"
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $runnerOutput = & pwsh -NoLogo -NoProfile -File $policyPath `
            -Mode ValidateRunnerConfiguration `
            -RepositoryRoot $RepositoryRoot `
            -RunnerConfigPath $tamperedRunnerConfig 2>&1
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $runnerText = ($runnerOutput | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -or -not $runnerText.Contains("$ruleId-DISABLED", [StringComparison]::Ordinal)) {
        throw "Tampered output runner configuration should fail with $ruleId-DISABLED:`n$runnerText"
    }
    Write-Host "Rejected AICopilot output-governance fixture: failSkips=false ($ruleId-DISABLED)"

    Write-Host 'AICopilot test-governance behavior fixtures passed.'
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
