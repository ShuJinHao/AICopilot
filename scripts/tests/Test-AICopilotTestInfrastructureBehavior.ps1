[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$inventoryScript = Join-Path $RepositoryRoot 'scripts/tests/Get-AICopilotTestInventory.ps1'
$reconciliationScript = Join-Path $RepositoryRoot 'scripts/tests/Confirm-AICopilotRequiredTestResults.ps1'
$coverageScript = Join-Path $RepositoryRoot 'scripts/tests/Confirm-AICopilotCoverage.ps1'
$mutationScript = Join-Path $RepositoryRoot 'scripts/tests/Confirm-AICopilotMutation.ps1'
$duplicationScript = Join-Path $RepositoryRoot 'scripts/tests/Measure-AICopilotDuplication.ps1'
$compatibilityScript = Join-Path $RepositoryRoot 'scripts/tests/Test-AICopilotCompatibilityInventory.ps1'
$declarationTransitionChecker = Join-Path $RepositoryRoot 'scripts/tests/Test-AICopilotTestDeclarationTransition.ps1'
. (Join-Path $RepositoryRoot 'scripts/tests/Resolve-AICopilotQualityBase.ps1')
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "aicopilot-test-infrastructure-$([Guid]::NewGuid().ToString('N'))"

function Write-FixtureFile {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Content
    )

    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    Set-Content -Path $Path -Value $Content -Encoding utf8
}

function Initialize-LedgerScanRoots {
    param([Parameter(Mandatory)] [string]$Root)

    foreach ($scanRoot in @(
            'src/core',
            'src/hosts',
            'src/infrastructure',
            'src/services',
            'src/shared',
            'src/testing',
            'src/vues/AICopilot.Web/src',
            'scripts/tests')) {
        New-Item -ItemType Directory -Path (Join-Path $Root $scanRoot) -Force | Out-Null
    }
}

function Add-ProjectXmlFragment {
    param(
        [Parameter(Mandatory)] [string]$ProjectPath,
        [Parameter(Mandatory)] [string]$Fragment
    )

    $projectText = Get-Content $ProjectPath -Raw
    if ($projectText -notmatch '</Project>') {
        throw "Fixture project has no closing Project element: $ProjectPath"
    }
    Write-FixtureFile $ProjectPath ($projectText.Replace('</Project>', "$Fragment`n</Project>"))
}

function New-InventoryFixture {
    param(
        [string]$SupportPath = 'src/testing/AICopilot.AgentWorkflowTestKit/AICopilot.AgentWorkflowTestKit.csproj',
        [string]$SupportIsTestProject = 'false',
        [string]$SupportPackageReference = '',
        [string]$SupportSource = 'internal static class SupportProgram { }',
        [string]$RunnerKind = 'Unit',
        [string]$RunnerRuntime = 'Pure',
        [string]$RunnerCadence = 'PR',
        [string]$RunnerProjectName = 'AICopilot.UnitTests',
        [string]$RunnerOwner = 'Fixture',
        [string]$RunnerRequired = 'true',
        [string]$RunnerProfile = 'Default',
        [string]$RunnerRuntimeDependenciesJson = '[]',
        [string]$RunnerClassificationOverridesJson = '',
        [string]$RunnerRole = '',
        [string]$RunnerPackageReference = '',
        [string]$RunnerProjectReference = '',
        [string]$RunnerImport = '',
        [string]$RunnerSource = 'internal static class RunnerProgram { }',
        [string]$SupportProjectReference = '',
        [string]$ReferencedProjectPath = '',
        [string]$ReferencedProjectContent = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
    )

    $root = Join-Path $tempRoot ([Guid]::NewGuid().ToString('N'))
    $hasProductionReference = -not [string]::IsNullOrWhiteSpace($ReferencedProjectPath) -and
        $ReferencedProjectPath.Replace('\', '/') -notmatch '^src/(tests|testing)/'
    $analyzerPath = 'src/analyzers/AICopilot.Architecture.Analyzers/AICopilot.Architecture.Analyzers.csproj'
    $runnerPath = "src/tests/$RunnerProjectName/$RunnerProjectName.csproj"
    $runnerRoleProperty = if ([string]::IsNullOrWhiteSpace($RunnerRole)) {
        ''
    }
    else {
        "    <AICopilotTestRole>$RunnerRole</AICopilotTestRole>"
    }
    $runnerItems = @(
        @(
            '<ProjectReference Include="../../testing/AICopilot.AgentWorkflowTestKit/AICopilot.AgentWorkflowTestKit.csproj" />',
            $RunnerPackageReference,
            $RunnerProjectReference
        ) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    $runnerItemGroup = if ($runnerItems.Count -eq 0) {
        ''
    }
    else {
        "  <ItemGroup>`n    $($runnerItems -join "`n    ")`n  </ItemGroup>"
    }
    $runnerProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <AICopilotTestKind>$RunnerKind</AICopilotTestKind>
    <AICopilotTestRuntime>$RunnerRuntime</AICopilotTestRuntime>
    <AICopilotTestCadence>$RunnerCadence</AICopilotTestCadence>
${runnerRoleProperty}
    <AICopilotTestOwner>$RunnerOwner</AICopilotTestOwner>
    <AICopilotRequired>$RunnerRequired</AICopilotRequired>
  </PropertyGroup>
$runnerItemGroup
$RunnerImport
</Project>
"@
    $supportItems = @(
        @($SupportPackageReference, $SupportProjectReference) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    $supportItemGroup = if ($supportItems.Count -eq 0) {
        ''
    }
    else {
        "  <ItemGroup>`n    $($supportItems -join "`n    ")`n  </ItemGroup>"
    }
    $supportProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>$SupportIsTestProject</IsTestProject>
    <AICopilotTestRole>Support</AICopilotTestRole>
    <AICopilotTestOwner>Fixture Support</AICopilotTestOwner>
    <AICopilotTestConsumers>$RunnerProjectName</AICopilotTestConsumers>
  </PropertyGroup>
$supportItemGroup
</Project>
"@
    $referencedSolutionEntry = if ([string]::IsNullOrWhiteSpace($ReferencedProjectPath)) {
        ''
    }
    else {
        "    <Project Path=`"$ReferencedProjectPath`" />"
    }
    $analyzerSolutionEntry = if ($hasProductionReference) {
        "    <Project Path=`"$analyzerPath`" />"
    }
    else {
        ''
    }
    $solution = @"
<Solution>
  <Folder Name="/tests/">
    <Project Path="$runnerPath" />
    <Project Path="$SupportPath" />
$referencedSolutionEntry
$analyzerSolutionEntry
  </Folder>
</Solution>
"@

    Write-FixtureFile (Join-Path $root 'AICopilot.slnx') $solution
    Write-FixtureFile `
        (Join-Path $root 'scripts/tests/aicopilot-test-classification.json') `
        @"
{
  "schemaVersion": 2,
  "projects": [
    {
      "projectName": "$RunnerProjectName",
      "defaults": {
        "testKind": "$RunnerKind",
        "capability": "Platform",
        "concern": "Functional",
        "profile": "$RunnerProfile",
        "risk": "P1",
        "ruleId": "AI-UNIT-001",
        "regressionId": "AI-REG-UNIT-FIXTURE",
        "runtimeDependencies": $RunnerRuntimeDependenciesJson
      }$RunnerClassificationOverridesJson
    }
  ]
}
"@
    Write-FixtureFile `
        (Join-Path $root 'scripts/tests/aicopilot-fixed-delay-allowlist.json') `
        '{ "schemaVersion": 1, "entries": [] }'
    Write-FixtureFile (Join-Path $root $runnerPath) $runnerProject
    Write-FixtureFile (Join-Path (Split-Path (Join-Path $root $runnerPath) -Parent) 'RunnerProgram.cs') $RunnerSource
    Write-FixtureFile (Join-Path $root $SupportPath) $supportProject
    Write-FixtureFile (Join-Path (Split-Path (Join-Path $root $SupportPath) -Parent) 'Program.cs') $SupportSource
    if (-not [string]::IsNullOrWhiteSpace($ReferencedProjectPath)) {
        $effectiveReferencedProjectContent = $ReferencedProjectContent
        if ($hasProductionReference) {
            $referencedDirectory = Split-Path (Join-Path $root $ReferencedProjectPath) -Parent
            $analyzerReference = [System.IO.Path]::GetRelativePath(
                $referencedDirectory,
                (Join-Path $root $analyzerPath)).Replace('\', '/')
            $lockedAnalyzerReference = @"
  <ItemGroup>
    <ProjectReference Include="$analyzerReference"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false"
                      PrivateAssets="all" />
  </ItemGroup>
"@
            $effectiveReferencedProjectContent = $effectiveReferencedProjectContent.Replace(
                '</Project>',
                "$lockedAnalyzerReference</Project>")
            Write-FixtureFile (Join-Path $root $analyzerPath) @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup>
</Project>
'@
            Write-FixtureFile `
                (Join-Path (Split-Path (Join-Path $root $analyzerPath) -Parent) 'AnalyzerMarker.cs') `
                'namespace AICopilot.Architecture.Analyzers; internal sealed class AnalyzerMarker { }'
        }
        Write-FixtureFile (Join-Path $root $ReferencedProjectPath) $effectiveReferencedProjectContent
    }
    & git -C $root init --quiet
    & git -C $root add -A
    & git -C $root -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' commit --quiet -m 'fixture baseline'
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create controlled Git baseline for inventory fixture '$root'."
    }
    & git -C $root update-ref refs/remotes/origin/main HEAD
    & git -C $root -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' `
        commit --quiet --allow-empty -m 'fixture candidate'
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create candidate commit for inventory fixture '$root'."
    }
    return $root
}

function Assert-Fails {
    param(
        [Parameter(Mandatory)] [scriptblock]$Action,
        [Parameter(Mandatory)] [string]$ExpectedMessagePattern
    )

    try {
        & $Action *> $null
    }
    catch {
        if ($_.Exception.Message -notmatch $ExpectedMessagePattern) {
            throw "Expected failure matching '$ExpectedMessagePattern', but got: $($_.Exception.Message)"
        }

        return
    }

    throw "Expected failure matching '$ExpectedMessagePattern', but the command succeeded."
}

function Build-InventoryFixtureRunner {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$ProjectName
    )

    & dotnet build `
        (Join-Path $Root "src/tests/$ProjectName/$ProjectName.csproj") `
        -c Release `
        -nologo `
        -v:quiet *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build controlled inventory fixture runner '$ProjectName'."
    }
}

function Write-ReconciliationFixture {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [int]$Total,
        [Parameter(Mandatory)] [int]$Executed,
        [Parameter(Mandatory)] [int]$Passed,
        [int]$ExpectedCount = $Total
    )

    $inventory = [pscustomobject]@{
        projects = @(
            [pscustomobject]@{
                projectName = 'RequiredRunner'
                role = 'Runner'
                required = $true
                caseCount = $ExpectedCount
            }
        )
    }
    Write-FixtureFile `
        -Path (Join-Path $Root 'artifacts/test-inventory.json') `
        -Content ($inventory | ConvertTo-Json -Depth 5)
    $trx = @"
<TestRun>
  <ResultSummary>
    <Counters total="$Total" executed="$Executed" passed="$Passed" failed="0" notExecuted="0" />
  </ResultSummary>
</TestRun>
"@
    Write-FixtureFile (Join-Path $Root 'artifacts/test-results/RequiredRunner.trx') $trx
}

function Write-WebAndDeploymentReconciliationFixture {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [int]$VitestCount = 165,
        [int]$PlaywrightCount = 43,
        [int]$DeploymentCount = 33
    )

    $vitest = [ordered]@{
        success = $true
        numTotalTests = $VitestCount
        numPassedTests = $VitestCount
        numFailedTests = 0
        numPendingTests = 0
        numTodoTests = 0
        testResults = @([ordered]@{ name = 'fixture' })
    }
    $playwright = [ordered]@{
        stats = [ordered]@{
            expected = $PlaywrightCount
            unexpected = 0
            flaky = 0
            skipped = 0
        }
    }
    $deploymentLines = [System.Collections.Generic.List[string]]::new()
    foreach ($index in 1..$DeploymentCount) {
        $deploymentLines.Add("TEST fixture-$index")
    }
    $deploymentLines.Add('NON_PRODUCTION_MECHANISM_TEST productionEligible=false result=passed')
    $deploymentLines.Add('All AICopilot deployment behavior tests passed.')

    Write-FixtureFile `
        (Join-Path $Root 'artifacts/test-results/vitest.json') `
        ($vitest | ConvertTo-Json -Depth 8)
    Write-FixtureFile `
        (Join-Path $Root 'artifacts/test-results/playwright.json') `
        ($playwright | ConvertTo-Json -Depth 8)
    Write-FixtureFile `
        (Join-Path $Root 'artifacts/test-results/deployment-behavior.log') `
        ($deploymentLines -join [Environment]::NewLine)
}

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    & $inventoryScript `
        -RepositoryRoot $RepositoryRoot `
        -RunClassificationBehaviorFixture *> $null

    $qualityBaseFixture = New-InventoryFixture
    $candidateHead = [string](& git -C $qualityBaseFixture rev-parse HEAD)
    Assert-Fails {
        Resolve-AICopilotQualityBase -RepositoryRoot $qualityBaseFixture -BaseRef $candidateHead
    } 'Base ref must not resolve to candidate HEAD'
    Assert-Fails {
        Resolve-AICopilotQualityBase -RepositoryRoot $qualityBaseFixture -BaseRef ''
    } 'non-empty, non-zero base ref'
    Assert-Fails {
        Resolve-AICopilotQualityBase -RepositoryRoot $qualityBaseFixture -BaseRef ('0' * 40)
    } 'non-empty, non-zero base ref'

    $oldAuthorName = $env:GIT_AUTHOR_NAME
    $oldAuthorEmail = $env:GIT_AUTHOR_EMAIL
    $oldCommitterName = $env:GIT_COMMITTER_NAME
    $oldCommitterEmail = $env:GIT_COMMITTER_EMAIL
    try {
        $env:GIT_AUTHOR_NAME = 'AICopilot Fixture'
        $env:GIT_AUTHOR_EMAIL = 'fixture@invalid'
        $env:GIT_COMMITTER_NAME = 'AICopilot Fixture'
        $env:GIT_COMMITTER_EMAIL = 'fixture@invalid'
        $fixtureTree = [string](& git -C $qualityBaseFixture rev-parse 'HEAD^{tree}')
        $nonAncestor = [string]('orphan' | & git -C $qualityBaseFixture commit-tree $fixtureTree.Trim())
    }
    finally {
        $env:GIT_AUTHOR_NAME = $oldAuthorName
        $env:GIT_AUTHOR_EMAIL = $oldAuthorEmail
        $env:GIT_COMMITTER_NAME = $oldCommitterName
        $env:GIT_COMMITTER_EMAIL = $oldCommitterEmail
    }
    Assert-Fails {
        Resolve-AICopilotQualityBase -RepositoryRoot $qualityBaseFixture -BaseRef $nonAncestor.Trim()
    } 'not an ancestor of candidate HEAD'
    Assert-Fails {
        Get-AICopilotBaselineContext `
            -RepositoryRoot $qualityBaseFixture `
            -BaseRef origin/main `
            -BaselineKind Duplication `
            -BaselinePath 'scripts/tests/baselines/renamed-duplication.json'
    } 'baseline identity is fixed'
    Assert-Fails {
        Get-AICopilotBaselineContext `
            -RepositoryRoot $qualityBaseFixture `
            -BaseRef origin/main `
            -BaselineKind Duplication `
            -BaselinePath (Join-Path $tempRoot 'external-duplication-baseline.json')
    } 'baseline path escapes the repository root'

    $bootstrapContext = Get-AICopilotBaselineContext `
        -RepositoryRoot $qualityBaseFixture `
        -BaseRef origin/main `
        -BaselineKind Mutation `
        -BaselinePath 'scripts/tests/baselines/aicopilot-mutation.json'
    if ([string]$bootstrapContext.Mode -cne 'Bootstrap' -or $LASTEXITCODE -ne 0) {
        throw "Missing base baseline must return Bootstrap without leaking a native exit code: mode=$($bootstrapContext.Mode), lastExit=$LASTEXITCODE."
    }

    $unapprovedSupportRoot = New-InventoryFixture `
        -SupportPath 'src/tests/AICopilot.UnapprovedSupport/AICopilot.UnapprovedSupport.csproj'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $unapprovedSupportRoot -OutputPath 'artifacts/test-inventory.json'
    } 'not in the fixed support-project allowlist'

    $testProjectSupportRoot = New-InventoryFixture -SupportIsTestProject 'true'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $testProjectSupportRoot -OutputPath 'artifacts/test-inventory.json'
    } 'must directly declare IsTestProject=false'

    $testSdkSupportRoot = New-InventoryFixture `
        -SupportPackageReference '<PackageReference Include="xunit" Version="2.9.3" /><PackageReference Include="FluentAssertions" Version="8.8.0" />'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $testSdkSupportRoot -OutputPath 'artifacts/test-inventory.json'
    } 'must not reference test SDK/framework packages: (?:xunit, FluentAssertions|FluentAssertions, xunit)'

    $factSupportRoot = New-InventoryFixture `
        -SupportSource 'internal sealed class HiddenTests { [Fact] public void Escaped() { } }'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $factSupportRoot -OutputPath 'artifacts/test-inventory.json'
    } 'must not declare Fact/Theory tests'

    $invalidKindRoot = New-InventoryFixture -RunnerKind 'Filesystem'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $invalidKindRoot -OutputPath 'artifacts/test-inventory.json'
    } 'invalid AICopilotTestKind'

    $legacyHttpKindRoot = New-InventoryFixture -RunnerKind 'HttpIntegration'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $legacyHttpKindRoot -OutputPath 'artifacts/test-inventory.json'
    } 'invalid AICopilotTestKind'

    $invalidRuntimeRoot = New-InventoryFixture -RunnerRuntime 'DockerRequired'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $invalidRuntimeRoot -OutputPath 'artifacts/test-inventory.json'
    } 'invalid AICopilotTestRuntime'

    $fixtureTestPackages = @'
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
<PackageReference Include="xunit" Version="2.9.3" />
<PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
'@
    $defaultsOnlyUnitSource = @'
using Xunit;
namespace AICopilot.UnitTests;
public sealed class DefaultsOnlyTests
{
    [Fact]
    public void UsesRunnerDefaultsWithoutOverrides() { }
}
'@
    $defaultsOnlyUnitRoot = New-InventoryFixture `
        -RunnerPackageReference $fixtureTestPackages `
        -RunnerSource $defaultsOnlyUnitSource
    Build-InventoryFixtureRunner -Root $defaultsOnlyUnitRoot -ProjectName 'AICopilot.UnitTests'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $defaultsOnlyUnitRoot -OutputPath 'artifacts/test-inventory.json'
    } 'Stable case baseline is missing'

    $inProcessFixtureSource = @'
using Xunit;
namespace AICopilot.InProcessTests;
public sealed class FixtureBoundaryTests
{
    [Fact]
    public void NoExternalResourceCase() { }
}
'@
    $validInProcessOverrides = ', "overrides": [{ "matchId": "fixture-boundary", "class": "AICopilot.InProcessTests.FixtureBoundaryTests", "testKind": "Unit", "capability": "Platform", "concern": "Functional", "profile": "Default", "risk": "P1", "ruleId": "AI-INPROCESS-001", "regressionId": "AI-REG-INPROCESS-FIXTURE", "runtimeDependencies": [] }]'
    $validInProcessRoot = New-InventoryFixture `
        -RunnerProjectName 'AICopilot.InProcessTests' `
        -RunnerKind 'Integration' `
        -RunnerRuntime 'InProcess' `
        -RunnerOwner 'InProcessBoundary' `
        -RunnerPackageReference $fixtureTestPackages `
        -RunnerSource $inProcessFixtureSource `
        -RunnerClassificationOverridesJson $validInProcessOverrides
    Build-InventoryFixtureRunner -Root $validInProcessRoot -ProjectName 'AICopilot.InProcessTests'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $validInProcessRoot -OutputPath 'artifacts/test-inventory.json'
    } 'Stable case baseline is missing'

    $missingInProcessClassRoot = New-InventoryFixture `
        -RunnerProjectName 'AICopilot.InProcessTests' `
        -RunnerKind 'Integration' `
        -RunnerRuntime 'InProcess' `
        -RunnerOwner 'InProcessBoundary' `
        -RunnerPackageReference $fixtureTestPackages `
        -RunnerSource $inProcessFixtureSource
    Build-InventoryFixtureRunner -Root $missingInProcessClassRoot -ProjectName 'AICopilot.InProcessTests'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $missingInProcessClassRoot -OutputPath 'artifacts/test-inventory.json'
    } 'requires exactly one class-level classification override'

    $duplicateInProcessOverrides = ', "overrides": [{ "matchId": "duplicate-a", "class": "AICopilot.InProcessTests.FixtureBoundaryTests", "testKind": "Unit" }, { "matchId": "duplicate-b", "class": "AICopilot.InProcessTests.FixtureBoundaryTests", "testKind": "Contract" }]'
    $duplicateInProcessClassRoot = New-InventoryFixture `
        -RunnerProjectName 'AICopilot.InProcessTests' `
        -RunnerKind 'Integration' `
        -RunnerRuntime 'InProcess' `
        -RunnerOwner 'InProcessBoundary' `
        -RunnerClassificationOverridesJson $duplicateInProcessOverrides
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $duplicateInProcessClassRoot -OutputPath 'artifacts/test-inventory.json'
    } 'duplicates an exact classification selector'

    $staleInProcessOverrides = ', "overrides": [{ "matchId": "fixture-boundary", "class": "AICopilot.InProcessTests.FixtureBoundaryTests", "testKind": "Unit", "runtimeDependencies": [] }, { "matchId": "removed-boundary", "class": "AICopilot.InProcessTests.RemovedBoundaryTests", "testKind": "Unit", "runtimeDependencies": [] }]'
    $staleInProcessClassRoot = New-InventoryFixture `
        -RunnerProjectName 'AICopilot.InProcessTests' `
        -RunnerKind 'Integration' `
        -RunnerRuntime 'InProcess' `
        -RunnerOwner 'InProcessBoundary' `
        -RunnerPackageReference $fixtureTestPackages `
        -RunnerSource $inProcessFixtureSource `
        -RunnerClassificationOverridesJson $staleInProcessOverrides
    Build-InventoryFixtureRunner -Root $staleInProcessClassRoot -ProjectName 'AICopilot.InProcessTests'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $staleInProcessClassRoot -OutputPath 'artifacts/test-inventory.json'
    } 'contains stale class-level classification overrides'

    $exactKindInProcessOverrides = ', "overrides": [{ "matchId": "fixture-boundary", "class": "AICopilot.InProcessTests.FixtureBoundaryTests", "testKind": "Unit", "runtimeDependencies": [] }, { "matchId": "exact-kind-escape", "class": "AICopilot.InProcessTests.FixtureBoundaryTests", "method": "NoExternalResourceCase", "testKind": "Contract" }]'
    $exactKindInProcessRoot = New-InventoryFixture `
        -RunnerProjectName 'AICopilot.InProcessTests' `
        -RunnerKind 'Integration' `
        -RunnerRuntime 'InProcess' `
        -RunnerOwner 'InProcessBoundary' `
        -RunnerClassificationOverridesJson $exactKindInProcessOverrides
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $exactKindInProcessRoot -OutputPath 'artifacts/test-inventory.json'
    } 'exact method/case override and must inherit class-level testKind'

    $forgedInProcessRoot = New-InventoryFixture `
        -RunnerProjectName 'AICopilot.ForgedInProcessTests' `
        -RunnerKind 'Integration' `
        -RunnerRuntime 'InProcess' `
        -RunnerOwner 'InProcessBoundary'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $forgedInProcessRoot -OutputPath 'artifacts/test-inventory.json'
    } 'uses Runtime=InProcess but is not the locked in-process runner'

    $resourceDefaultInProcessRoot = New-InventoryFixture `
        -RunnerProjectName 'AICopilot.InProcessTests' `
        -RunnerKind 'Integration' `
        -RunnerRuntime 'InProcess' `
        -RunnerOwner 'InProcessBoundary' `
        -RunnerRuntimeDependenciesJson '["Docker"]'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $resourceDefaultInProcessRoot -OutputPath 'artifacts/test-inventory.json'
    } 'is InProcess and must declare no external runtimeDependencies'

    $inProcessReverseTupleRoot = New-InventoryFixture `
        -RunnerProjectName 'AICopilot.InProcessTests' `
        -RunnerKind 'Integration' `
        -RunnerRuntime 'Aspire' `
        -RunnerOwner 'InProcessBoundary'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $inProcessReverseTupleRoot -OutputPath 'artifacts/test-inventory.json'
    } 'changed the locked AICopilot.InProcessTests path/kind/runtime/owner/required/cadence tuple'

    $inProcessImportedOwnerRoot = New-InventoryFixture `
        -RunnerProjectName 'AICopilot.InProcessTests' `
        -RunnerKind 'Integration' `
        -RunnerRuntime 'InProcess' `
        -RunnerOwner 'InProcessBoundary'
    Write-FixtureFile (Join-Path $inProcessImportedOwnerRoot 'Directory.Build.targets') @'
<Project>
  <PropertyGroup Condition="'$(MSBuildProjectName)' == 'AICopilot.InProcessTests'">
    <AICopilotTestOwner>ImportedOwnerEscape</AICopilotTestOwner>
  </PropertyGroup>
</Project>
'@
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $inProcessImportedOwnerRoot -OutputPath 'artifacts/test-inventory.json'
    } 'direct and evaluated AICopilotTestOwner differ'

    foreach ($forbiddenInProcessPackage in @(
            'Aspire.Hosting.AppHost',
            'Npgsql.EntityFrameworkCore.PostgreSQL',
            'Testcontainers.PostgreSql',
            'Docker.DotNet',
            'StackExchange.Redis',
            'RabbitMQ.Client',
            'Qdrant.Client')) {
        $inProcessPackageRoot = New-InventoryFixture `
            -RunnerProjectName 'AICopilot.InProcessTests' `
            -RunnerKind 'Integration' `
            -RunnerRuntime 'InProcess' `
            -RunnerOwner 'InProcessBoundary' `
            -RunnerPackageReference "<PackageReference Include=`"$forbiddenInProcessPackage`" Version=`"1.0.0`" />"
        Assert-Fails {
            & $inventoryScript -RepositoryRoot $inProcessPackageRoot -OutputPath 'artifacts/test-inventory.json'
        } 'is InProcess and must not directly reference external-resource packages'
    }

    foreach ($forbiddenInProcessHost in @('AICopilot.AppHost', 'AICopilot.RagWorker')) {
        $forbiddenHostPath = "src/hosts/$forbiddenInProcessHost/$forbiddenInProcessHost.csproj"
        $inProcessHostGraphRoot = New-InventoryFixture `
            -RunnerProjectName 'AICopilot.InProcessTests' `
            -RunnerKind 'Integration' `
            -RunnerRuntime 'InProcess' `
            -RunnerOwner 'InProcessBoundary' `
            -SupportProjectReference "<ProjectReference Include=`"../../hosts/$forbiddenInProcessHost/$forbiddenInProcessHost.csproj`" />" `
            -ReferencedProjectPath $forbiddenHostPath
        Assert-Fails {
            & $inventoryScript -RepositoryRoot $inProcessHostGraphRoot -OutputPath 'artifacts/test-inventory.json'
        } 'evaluated project graph must not reach AppHost, RagWorker, AspireIntegrationTestKit, or PersistenceTestKit'
    }

    foreach ($forbiddenInProcessKit in @('AICopilot.AspireIntegrationTestKit', 'AICopilot.PersistenceTestKit')) {
        $forbiddenKitPath = "src/testing/$forbiddenInProcessKit/$forbiddenInProcessKit.csproj"
        $forbiddenKitProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>false</IsTestProject>
    <AICopilotTestRole>Support</AICopilotTestRole>
    <AICopilotTestOwner>Fixture Forbidden Kit</AICopilotTestOwner>
    <AICopilotTestConsumers>AICopilot.InProcessTests</AICopilotTestConsumers>
  </PropertyGroup>
</Project>
"@
        $inProcessKitGraphRoot = New-InventoryFixture `
            -RunnerProjectName 'AICopilot.InProcessTests' `
            -RunnerKind 'Integration' `
            -RunnerRuntime 'InProcess' `
            -RunnerOwner 'InProcessBoundary' `
            -RunnerProjectReference "<ProjectReference Include=`"../../testing/$forbiddenInProcessKit/$forbiddenInProcessKit.csproj`" />" `
            -ReferencedProjectPath $forbiddenKitPath `
            -ReferencedProjectContent $forbiddenKitProject
        Assert-Fails {
            & $inventoryScript -RepositoryRoot $inProcessKitGraphRoot -OutputPath 'artifacts/test-inventory.json'
        } 'evaluated project graph must not reach AppHost, RagWorker, AspireIntegrationTestKit, or PersistenceTestKit'
    }

    $nonInProcessKindDriftRoot = New-InventoryFixture
    $nonInProcessClassificationPath = Join-Path $nonInProcessKindDriftRoot `
        'scripts/tests/aicopilot-test-classification.json'
    $nonInProcessClassificationText = (Get-Content $nonInProcessClassificationPath -Raw).
        Replace('"testKind": "Unit"', '"testKind": "Contract"')
    Write-FixtureFile $nonInProcessClassificationPath $nonInProcessClassificationText
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $nonInProcessKindDriftRoot -OutputPath 'artifacts/test-inventory.json'
    } "classification default testKind='Contract' must equal project kind 'Unit'"

    $nonInProcessCaseDriftOverrides = ', "overrides": [{ "matchId": "case-kind-drift", "class": "AICopilot.UnitTests.FixtureBoundaryTests", "testKind": "Contract" }]'
    $nonInProcessCaseDriftSource = @'
using Xunit;
namespace AICopilot.UnitTests;
public sealed class FixtureBoundaryTests
{
    [Fact]
    public void CaseKindMustMatchRunner() { }
}
'@
    $nonInProcessCaseDriftRoot = New-InventoryFixture `
        -RunnerPackageReference $fixtureTestPackages `
        -RunnerSource $nonInProcessCaseDriftSource `
        -RunnerClassificationOverridesJson $nonInProcessCaseDriftOverrides
    Build-InventoryFixtureRunner -Root $nonInProcessCaseDriftRoot -ProjectName 'AICopilot.UnitTests'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $nonInProcessCaseDriftRoot -OutputPath 'artifacts/test-inventory.json'
    } "testKind='Contract' must equal its non-InProcess project kind 'Unit'"

    $invalidCadenceRoot = New-InventoryFixture -RunnerCadence 'Nightly'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $invalidCadenceRoot -OutputPath 'artifacts/test-inventory.json'
    } 'invalid AICopilotTestCadence'

    $invalidAnalyzerRoot = New-InventoryFixture -RunnerRole 'Analyzer' -RunnerKind 'Unit'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $invalidAnalyzerRoot -OutputPath 'artifacts/test-inventory.json'
    } 'Analyzer and must use TestKind=Architecture with Runtime=Pure or Filesystem'

    $applicationFilesystemRoot = New-InventoryFixture `
        -RunnerKind 'Application' `
        -RunnerRuntime 'Filesystem'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $applicationFilesystemRoot -OutputPath 'artifacts/test-inventory.json'
    } 'TestKind=Application and must use Runtime=Pure'

    $aggregateServiceRoot = New-InventoryFixture `
        -RunnerKind 'Aggregate' `
        -RunnerProjectReference '<ProjectReference Include="../../services/FixtureService/FixtureService.csproj" />' `
        -ReferencedProjectPath 'src/services/FixtureService/FixtureService.csproj'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $aggregateServiceRoot -OutputPath 'artifacts/test-inventory.json'
    } 'TestKind=Aggregate and may reference only core/shared production projects'

    $pureAnalyzerIoRoot = New-InventoryFixture `
        -RunnerRole 'Analyzer' `
        -RunnerKind 'Architecture' `
        -RunnerSource 'internal static class Fixture { public static void Write() => System.IO.File.WriteAllText("fixture", "value"); }'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $pureAnalyzerIoRoot -OutputPath 'artifacts/test-inventory.json'
    } 'Pure Analyzer but performs filesystem/process work'

    $legacyTraitRoot = New-InventoryFixture `
        -RunnerSource 'internal sealed class LegacyTests { [Trait("Runtime", "DockerRequired")] public void Escaped() { } }'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $legacyTraitRoot -OutputPath 'artifacts/test-inventory.json'
    } 'declares legacy Suite/Runtime traits'

    $fixedDelayRoot = New-InventoryFixture `
        -RunnerSource 'internal static class TimingFixture { public static async Task WaitAsync() { await Task.Delay(20); } }'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $fixedDelayRoot -OutputPath 'artifacts/test-inventory.json'
    } 'uses fixed Task.Delay\(20\) without exactly one readiness-polling allowlist entry'

    $supportFixedDelayRoot = New-InventoryFixture `
        -SupportSource 'internal static class TimingSupport { public static async Task WaitAsync() { await Task.Delay(25); } }'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $supportFixedDelayRoot -OutputPath 'artifacts/test-inventory.json'
    } 'src/testing/.+ uses fixed Task.Delay\(25\) without exactly one readiness-polling allowlist entry'

    $hostReferenceRoot = New-InventoryFixture `
        -RunnerKind 'Application' `
        -RunnerProjectReference '<ProjectReference Include="../../hosts/ForbiddenHost/ForbiddenHost.csproj" />' `
        -ReferencedProjectPath 'src/hosts/ForbiddenHost/ForbiddenHost.csproj'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $hostReferenceRoot -OutputPath 'artifacts/test-inventory.json'
    } 'TestKind=Application and must not depend on host, database, Aspire, or persistence fixtures'

    $implicitDirectoryBuildRoot = New-InventoryFixture `
        -RunnerKind 'Application' `
        -ReferencedProjectPath 'src/hosts/ForbiddenHost/ForbiddenHost.csproj'
    Write-FixtureFile `
        (Join-Path $implicitDirectoryBuildRoot 'Directory.Build.targets') `
        @'
<Project>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'AICopilot.UnitTests' And '$(Configuration)' == 'Release'">
    <ProjectReference Include="$(MSBuildThisFileDirectory)src/hosts/ForbiddenHost/ForbiddenHost.csproj" />
  </ItemGroup>
</Project>
'@
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $implicitDirectoryBuildRoot -OutputPath 'artifacts/test-inventory.json'
    } 'TestKind=Application and must not depend on host, database, Aspire, or persistence fixtures'

    $recursiveImportRoot = New-InventoryFixture `
        -RunnerKind 'Application' `
        -RunnerImport '  <Import Project="../../../build/outer.props" />' `
        -ReferencedProjectPath 'src/fixtures/AICopilot.PersistenceTestKit/AICopilot.PersistenceTestKit.csproj'
    Write-FixtureFile `
        (Join-Path $recursiveImportRoot 'build/outer.props') `
        @'
<Project>
  <PropertyGroup>
    <EnableImportedForbiddenEdge>true</EnableImportedForbiddenEdge>
  </PropertyGroup>
  <Import Project="$(MSBuildThisFileDirectory)nested/inner.targets" />
</Project>
'@
    Write-FixtureFile `
        (Join-Path $recursiveImportRoot 'build/nested/inner.targets') `
        @'
<Project>
  <ItemGroup Condition="'$(EnableImportedForbiddenEdge)' == 'true' And ('$(Configuration)' == 'Release' Or '$(Configuration)' == 'CI')">
    <ProjectReference Include="$(MSBuildThisFileDirectory)../../src/fixtures/AICopilot.PersistenceTestKit/AICopilot.PersistenceTestKit.csproj" />
  </ItemGroup>
</Project>
'@
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $recursiveImportRoot -OutputPath 'artifacts/test-inventory.json'
    } 'TestKind=Application and must not depend on host, database, Aspire, or persistence fixtures'

    $supportTransitiveRoot = New-InventoryFixture `
        -SupportProjectReference '<ProjectReference Include="../../hosts/ForbiddenHost/ForbiddenHost.csproj" />' `
        -ReferencedProjectPath 'src/hosts/ForbiddenHost/ForbiddenHost.csproj'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $supportTransitiveRoot -OutputPath 'artifacts/test-inventory.json'
    } 'Pure but reaches forbidden project src/hosts/ForbiddenHost/ForbiddenHost.csproj'

    foreach ($forbiddenPackage in @('Microsoft.EntityFrameworkCore', 'Dapper', 'Aspire.Hosting')) {
        $packageRoot = New-InventoryFixture `
            -RunnerPackageReference "<PackageReference Include=`"$forbiddenPackage`" Version=`"1.0.0`" />"
        Assert-Fails {
            & $inventoryScript -RepositoryRoot $packageRoot -OutputPath 'artifacts/test-inventory.json'
        } 'Pure but reaches forbidden package'
    }

    $staleFriendAssemblyRoot = New-InventoryFixture `
        -RunnerSource 'using System.Runtime.CompilerServices; [assembly: InternalsVisibleTo("AICopilot.FilesystemTests")] internal static class RunnerProgram { }'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $staleFriendAssemblyRoot -OutputPath 'artifacts/test-inventory.json'
    } 'InternalsVisibleTo targets removed or unknown test runners'

    $solutionMissingProjectRoot = New-InventoryFixture
    Write-FixtureFile `
        (Join-Path $solutionMissingProjectRoot 'src/core/AICopilot.Missing/AICopilot.Missing.csproj') `
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $solutionMissingProjectRoot -OutputPath 'artifacts/test-inventory.json'
    } 'AICopilot\.slnx must contain every src project exactly once'

    $targetMutationRoot = New-InventoryFixture
    Write-FixtureFile `
        (Join-Path $targetMutationRoot 'Directory.Build.targets') `
        @'
<Project>
  <Target Name="HideSourcesBeforeBuild" BeforeTargets="CoreCompile">
    <ItemGroup><Compile Remove="**/*.cs" /></ItemGroup>
  </Target>
</Project>
'@
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $targetMutationRoot -OutputPath 'artifacts/test-inventory.json'
    } "mutates security-critical 'Compile' inside a Target"

    $designTimeDivergenceRoot = New-InventoryFixture
    Write-FixtureFile `
        (Join-Path $designTimeDivergenceRoot 'Directory.Build.props') `
        @'
<Project>
  <PropertyGroup Condition="'$(DesignTimeBuild)' == 'true'">
    <NoWarn>AIARCH006</NoWarn>
  </PropertyGroup>
</Project>
'@
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $designTimeDivergenceRoot -OutputPath 'artifacts/test-inventory.json'
    } 'changes security-critical properties/items under DesignTimeBuild'

    $linkedCompileRoot = New-InventoryFixture
    Write-FixtureFile (Join-Path $linkedCompileRoot 'Shared.cs') 'internal sealed class LinkedEscape { }'
    Add-ProjectXmlFragment `
        -ProjectPath (Join-Path $linkedCompileRoot 'src/tests/AICopilot.UnitTests/AICopilot.UnitTests.csproj') `
        -Fragment '  <ItemGroup><Compile Include="../../../Shared.cs" Link="LinkedEscape.cs" /></ItemGroup>'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $linkedCompileRoot -OutputPath 'artifacts/test-inventory.json'
    } 'uses forbidden linked Compile item'

    $externalProjectReferenceRoot = New-InventoryFixture
    $externalProjectPath = Join-Path $tempRoot 'external/AICopilot.External.csproj'
    Write-FixtureFile `
        $externalProjectPath `
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
    Add-ProjectXmlFragment `
        -ProjectPath (Join-Path $externalProjectReferenceRoot 'src/tests/AICopilot.UnitTests/AICopilot.UnitTests.csproj') `
        -Fragment "  <ItemGroup><ProjectReference Include=`"$externalProjectPath`" /></ItemGroup>"
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $externalProjectReferenceRoot -OutputPath 'artifacts/test-inventory.json'
    } 'has an external, missing, or non-absolute ProjectReference'

    $productionFixturePath = 'src/services/FixtureService/FixtureService.csproj'

    $assemblySpoofRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
    Add-ProjectXmlFragment `
        -ProjectPath (Join-Path $assemblySpoofRoot $productionFixturePath) `
        -Fragment '  <PropertyGroup><AssemblyName>AICopilot.UnitTests</AssemblyName></PropertyGroup>'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $assemblySpoofRoot -OutputPath 'artifacts/test-inventory.json'
    } 'spoofs AssemblyName, IsTestProject, or AICopilotTestRole'

    $roleSpoofRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
    Add-ProjectXmlFragment `
        -ProjectPath (Join-Path $roleSpoofRoot $productionFixturePath) `
        -Fragment '  <PropertyGroup><AICopilotTestRole>Support</AICopilotTestRole></PropertyGroup>'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $roleSpoofRoot -OutputPath 'artifacts/test-inventory.json'
    } 'spoofs AssemblyName, IsTestProject, or AICopilotTestRole'

    $analyzerDisabledRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
    Add-ProjectXmlFragment `
        -ProjectPath (Join-Path $analyzerDisabledRoot $productionFixturePath) `
        -Fragment '  <PropertyGroup><RunAnalyzers>false</RunAnalyzers></PropertyGroup>'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $analyzerDisabledRoot -OutputPath 'artifacts/test-inventory.json'
    } 'disables or suppresses required AIARCH analyzers'

    $analyzerSuppressedRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
    Add-ProjectXmlFragment `
        -ProjectPath (Join-Path $analyzerSuppressedRoot $productionFixturePath) `
        -Fragment '  <PropertyGroup><NoWarn>AIARCH006</NoWarn></PropertyGroup>'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $analyzerSuppressedRoot -OutputPath 'artifacts/test-inventory.json'
    } 'disables or suppresses required AIARCH analyzers'

    $pragmaSuppressedRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
    Write-FixtureFile `
        (Join-Path $pragmaSuppressedRoot 'src/services/FixtureService/FixtureSource.cs') `
        "#pragma warning disable AIARCH006`nnamespace FixtureService; internal sealed class FixtureSource { }"
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $pragmaSuppressedRoot -OutputPath 'artifacts/test-inventory.json'
    } 'suppresses a required AIARCH diagnostic in production source'

    foreach ($attributeName in @('SuppressMessage', 'UnconditionalSuppressMessage')) {
        $attributeSuppressedRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
        Write-FixtureFile `
            (Join-Path $attributeSuppressedRoot 'src/services/FixtureService/FixtureSource.cs') `
            "using System.Diagnostics.CodeAnalysis; [assembly: $attributeName(`"Architecture`", `"AIARCH007`")] namespace FixtureService; internal sealed class FixtureSource { }"
        Assert-Fails {
            & $inventoryScript -RepositoryRoot $attributeSuppressedRoot -OutputPath 'artifacts/test-inventory.json'
        } 'suppresses a required AIARCH diagnostic in production source'
    }

    $editorConfigSuppressedRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
    Write-FixtureFile `
        (Join-Path $editorConfigSuppressedRoot '.editorconfig') `
        "root = true`n[*.cs]`ndotnet_diagnostic.AIARCH001.severity = none"
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $editorConfigSuppressedRoot -OutputPath 'artifacts/test-inventory.json'
    } 'configures required AIARCH analyzer severity'

    $globalConfigSuppressedRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
    Write-FixtureFile `
        (Join-Path $globalConfigSuppressedRoot 'build/Architecture.globalconfig') `
        "is_global = true`ndotnet_diagnostic.AIARCH004.severity = warning"
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $globalConfigSuppressedRoot -OutputPath 'artifacts/test-inventory.json'
    } 'configures required AIARCH analyzer severity'

    $analyzerReferenceRemovedRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
    $analyzerReferenceProjectPath = Join-Path $analyzerReferenceRemovedRoot $productionFixturePath
    $analyzerReferenceProjectText = Get-Content $analyzerReferenceProjectPath -Raw
    $analyzerReferenceProjectText = $analyzerReferenceProjectText -replace `
        '(?s)\s*<ItemGroup>\s*<ProjectReference Include="[^"]*AICopilot\.Architecture\.Analyzers\.csproj".*?</ItemGroup>', `
        ''
    Write-FixtureFile $analyzerReferenceProjectPath $analyzerReferenceProjectText
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $analyzerReferenceRemovedRoot -OutputPath 'artifacts/test-inventory.json'
    } 'must evaluate exactly one locked architecture Analyzer reference'

    $productionToRunnerRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
    Add-ProjectXmlFragment `
        -ProjectPath (Join-Path $productionToRunnerRoot $productionFixturePath) `
        -Fragment '  <ItemGroup><ProjectReference Include="../../tests/AICopilot.UnitTests/AICopilot.UnitTests.csproj" /></ItemGroup>'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $productionToRunnerRoot -OutputPath 'artifacts/test-inventory.json'
    } 'must not reference any test runner/support project'

    $unreferencedSourceRoot = New-InventoryFixture -ReferencedProjectPath $productionFixturePath
    Write-FixtureFile `
        (Join-Path $unreferencedSourceRoot 'src/services/FixtureService/Hidden.cs') `
        'namespace FixtureService; internal sealed class Hidden { }'
    Add-ProjectXmlFragment `
        -ProjectPath (Join-Path $unreferencedSourceRoot $productionFixturePath) `
        -Fragment '  <ItemGroup><Compile Remove="Hidden.cs" /></ItemGroup>'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $unreferencedSourceRoot -OutputPath 'artifacts/test-inventory.json'
    } 'contains unreferenced production source'

    $unapprovedManualRoot = New-InventoryFixture
    $unapprovedManualProjectPath = Join-Path $unapprovedManualRoot 'src/tests/AICopilot.UnitTests/AICopilot.UnitTests.csproj'
    $unapprovedManualProjectText = (Get-Content $unapprovedManualProjectPath -Raw).
        Replace('<AICopilotRequired>true</AICopilotRequired>', '<AICopilotRequired>false</AICopilotRequired>').
        Replace('<AICopilotTestCadence>PR</AICopilotTestCadence>', '<AICopilotTestCadence>Manual</AICopilotTestCadence>')
    Write-FixtureFile $unapprovedManualProjectPath $unapprovedManualProjectText
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $unapprovedManualRoot -OutputPath 'artifacts/test-inventory.json'
    } 'new or unapproved non-required runner; required downgrades and new Manual runners are forbidden'

    $requiredDowngradeRoot = New-InventoryFixture `
        -RunnerProjectName 'AICopilot.CloudAiReadLiveTests' `
        -RunnerKind 'Contract' `
        -RunnerRuntime 'LiveExternal' `
        -RunnerCadence 'Manual' `
        -RunnerProfile 'LiveExternal'
    $requiredDowngradeProjectPath = Join-Path $requiredDowngradeRoot `
        'src/tests/AICopilot.CloudAiReadLiveTests/AICopilot.CloudAiReadLiveTests.csproj'
    $requiredDowngradeProjectText = (Get-Content $requiredDowngradeProjectPath -Raw).
        Replace('<AICopilotRequired>true</AICopilotRequired>', '<AICopilotRequired>false</AICopilotRequired>')
    Write-FixtureFile $requiredDowngradeProjectPath $requiredDowngradeProjectText
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $requiredDowngradeRoot -OutputPath 'artifacts/test-inventory.json'
    } 'downgraded AICopilotRequired from true to false relative to merge-base'

    $reconciliationRoot = Join-Path $tempRoot 'reconciliation'
    Write-ReconciliationFixture -Root $reconciliationRoot -Total 0 -Executed 0 -Passed 0
    Assert-Fails {
        & $reconciliationScript `
            -RepositoryRoot $reconciliationRoot `
            -InventoryPath 'artifacts/test-inventory.json' `
            -ResultsDirectory 'artifacts/test-results' `
            -OutputPath 'artifacts/test-results/summary.json'
    } 'required runner discovered no tests'

    Write-ReconciliationFixture -Root $reconciliationRoot -Total 1 -Executed 1 -Passed 1
    Write-WebAndDeploymentReconciliationFixture -Root $reconciliationRoot
    & $reconciliationScript `
        -RepositoryRoot $reconciliationRoot `
        -InventoryPath 'artifacts/test-inventory.json' `
        -ResultsDirectory 'artifacts/test-results' `
        -VitestPath 'artifacts/test-results/vitest.json' `
        -PlaywrightPath 'artifacts/test-results/playwright.json' `
        -DeploymentPath 'artifacts/test-results/deployment-behavior.log' `
        -OutputPath 'artifacts/test-results/summary.json' *> $null
    $summary = Get-Content (Join-Path $reconciliationRoot 'artifacts/test-results/summary.json') -Raw |
        ConvertFrom-Json
    if ([int]$summary.dotnet.discovered -ne 1 -or [int]$summary.dotnet.executed -ne 1) {
        throw 'A non-empty required runner did not reconcile to discovered=executed=1.'
    }

    Write-ReconciliationFixture -Root $reconciliationRoot -Total 1 -Executed 1 -Passed 1 -ExpectedCount 2
    Assert-Fails {
        & $reconciliationScript `
            -RepositoryRoot $reconciliationRoot `
            -InventoryPath 'artifacts/test-inventory.json' `
            -ResultsDirectory 'artifacts/test-results' `
            -OutputPath 'artifacts/test-results/summary.json'
    } 'expected=2, discovered=1'

    Write-ReconciliationFixture -Root $reconciliationRoot -Total 1 -Executed 1 -Passed 1
    Write-WebAndDeploymentReconciliationFixture -Root $reconciliationRoot -VitestCount 164
    Assert-Fails {
        & $reconciliationScript `
            -RepositoryRoot $reconciliationRoot `
            -InventoryPath 'artifacts/test-inventory.json' `
            -ResultsDirectory 'artifacts/test-results' `
            -VitestPath 'artifacts/test-results/vitest.json' `
            -PlaywrightPath 'artifacts/test-results/playwright.json' `
            -DeploymentPath 'artifacts/test-results/deployment-behavior.log'
    } 'Vitest reconciliation failed: expected=165, discovered=164'

    Write-WebAndDeploymentReconciliationFixture -Root $reconciliationRoot -PlaywrightCount 42
    Assert-Fails {
        & $reconciliationScript `
            -RepositoryRoot $reconciliationRoot `
            -InventoryPath 'artifacts/test-inventory.json' `
            -ResultsDirectory 'artifacts/test-results' `
            -VitestPath 'artifacts/test-results/vitest.json' `
            -PlaywrightPath 'artifacts/test-results/playwright.json' `
            -DeploymentPath 'artifacts/test-results/deployment-behavior.log'
    } 'Playwright reconciliation failed: expected=43, discovered=42'

    Write-WebAndDeploymentReconciliationFixture -Root $reconciliationRoot -DeploymentCount 32
    Assert-Fails {
        & $reconciliationScript `
            -RepositoryRoot $reconciliationRoot `
            -InventoryPath 'artifacts/test-inventory.json' `
            -ResultsDirectory 'artifacts/test-results' `
            -VitestPath 'artifacts/test-results/vitest.json' `
            -PlaywrightPath 'artifacts/test-results/playwright.json' `
            -DeploymentPath 'artifacts/test-results/deployment-behavior.log'
    } 'Deployment behavior reconciliation failed: expected=33, cases=32'

    $simulationScript = Get-Content (
        Join-Path $RepositoryRoot 'scripts/Run-AgentSimulationAcceptance.ps1') -Raw
    foreach ($forbiddenPattern in @(
        '(?i)--filter',
        '(?i)SkipDockerAcceptance',
        '(?i)Suite\s*=',
        '(?i)-ChangedFiles'
    )) {
        if ($simulationScript -match $forbiddenPattern) {
            throw "Simulation acceptance must execute physical runners without skip/filter indirection; found '$forbiddenPattern'."
        }
    }
    foreach ($requiredFragment in @(
        "docker info --format '{{.OSType}}'",
        'src/tests/AICopilot.SimulationTests/AICopilot.SimulationTests.csproj',
        'src/tests/AICopilot.SimulationDockerTests/AICopilot.SimulationDockerTests.csproj',
        'artifacts/simulation',
        'AICopilot.SimulationTests.trx',
        'AICopilot.SimulationDockerTests.trx',
        'ExpectedCount 12',
        'ExpectedCount 1'
    )) {
        if (-not $simulationScript.Contains($requiredFragment, [StringComparison]::Ordinal)) {
            throw "Simulation acceptance is missing required physical-runner fragment '$requiredFragment'."
        }
    }
    if ($simulationScript -match '(?i)(?:资料|docs?)[\\/]' -or
        $simulationScript -notmatch 'agent-simulation-acceptance\.md' -or
        $simulationScript -notmatch 'simulation-test-summary\.json') {
        throw 'Simulation acceptance evidence must be written only to the ignored artifacts/simulation path, never a documentation directory.'
    }

    $simulationWorkflow = Get-Content (
        Join-Path $RepositoryRoot '.github/workflows/aicopilot-simulation-release-candidate.yml') -Raw
    if ($simulationWorkflow -match '(?m)^\s*pull_request\s*:' -or
        $simulationWorkflow -notmatch '(?m)^\s*workflow_dispatch\s*:' -or
        $simulationWorkflow -notmatch '(?m)^\s*runs-on:\s*ubuntu-24\.04\s*$' -or
        $simulationWorkflow -notmatch '(?m)^\s*run:\s*docker info\s*$' -or
        $simulationWorkflow -notmatch '(?m)^\s*run:\s*\./scripts/Run-AgentSimulationAcceptance\.ps1 -Configuration Release\s*$' -or
        $simulationWorkflow -notmatch '(?m)^\s*if:\s*\$\{\{ always\(\) \}\}\s*$' -or
        $simulationWorkflow -notmatch '(?m)^\s*artifacts/simulation/agent-simulation-acceptance\.md\s*$' -or
        $simulationWorkflow -notmatch '(?m)^\s*artifacts/simulation/simulation-test-summary\.json\s*$' -or
        $simulationWorkflow -notmatch '(?m)^\s*artifacts/simulation/test-results/\*\.trx\s*$') {
        throw 'Simulation workflow must remain Manual-only on fixed Linux, run complete physical acceptance, and always upload report, summary, and both runner TRX files.'
    }

    function docker {
        'windows'
    }

    $nonLinuxPreflightFailed = $false
    try {
        & (Join-Path $RepositoryRoot 'scripts/Run-AgentSimulationAcceptance.ps1') `
            -Configuration Release `
            -EvidenceRoot (Join-Path $tempRoot 'simulation-negative') *> $null
    }
    catch {
        if ($_.Exception.Message -notmatch 'requires a Linux Docker daemon') {
            throw "Expected the non-Linux Docker preflight to fail safely, but got: $($_.Exception.Message)"
        }

        $nonLinuxPreflightFailed = $true
    }
    finally {
        Remove-Item Function:docker -ErrorAction SilentlyContinue
    }
    if (-not $nonLinuxPreflightFailed) {
        throw 'Simulation acceptance continued after a non-Linux Docker preflight.'
    }

    $requiredWorkflow = Get-Content (
        Join-Path $RepositoryRoot '.github/workflows/aicopilot-ci.yml') -Raw
    if ($requiredWorkflow -match '(?ms)^\s{2}push:\s*.*?^\s{4}paths:') {
        throw 'Required CI push must not use path filters; main push always executes the complete test ledger.'
    }
    if ($requiredWorkflow -match '(?m)^\s{2}schedule:\s*$') {
        throw 'Required CI must not add an implicit scheduled authority source.'
    }
    if ($requiredWorkflow -match '(?m)^\s{2}workflow_dispatch:\s*$' -or
        $requiredWorkflow.Contains('quality_base_ref', [StringComparison]::Ordinal) -or
        $requiredWorkflow.Contains('build-test-snapshot', [StringComparison]::Ordinal) -or
        $requiredWorkflow.Contains('mutation-gate-snapshot', [StringComparison]::Ordinal)) {
        throw 'Required CI must not expose a manual quality-base input or snapshot path.'
    }
    if ($requiredWorkflow -match '(?m)^\s{2}mutation-report:\s*$' -or
        $requiredWorkflow -match '(?m)^\s*continue-on-error:\s*true\s*$') {
        throw 'Required mutation CI must be a blocking mutation-gate job without continue-on-error.'
    }
    foreach ($requiredCiFragment in @(
        "_.runtime -in @('Pure', 'InProcess')",
        'ForEach-Object -Parallel',
        "_.runtime -notin @('Pure', 'InProcess')",
        'Test-AICopilotTestDeclarationTransition.ps1',
        'Test-AICopilotCompatibilityInventory.ps1',
        'Measure-AICopilotDuplication.ps1',
        'fetch-depth: 0',
        "github.event_name == 'pull_request' && github.event.pull_request.base.sha",
        '|| github.event.before',
        '--collect "XPlat Code Coverage"',
        'Confirm-AICopilotCoverage.ps1',
        'mutation-gate:',
        'Invoke-AICopilotMutation.ps1',
        'artifacts/mutation/**',
        'artifacts/quality/aicopilot-mutation.json'
    )) {
        if (-not $requiredWorkflow.Contains($requiredCiFragment, [StringComparison]::Ordinal)) {
            throw "Required CI is missing pure-parallel/resource-serial evidence '$requiredCiFragment'."
        }
    }
    $qualityBaseSource = Get-Content (
        Join-Path $RepositoryRoot 'scripts/tests/Resolve-AICopilotQualityBase.ps1') -Raw
    foreach ($qualityBaseFragment in @(
        'Base ref must not resolve to candidate HEAD',
        'not an ancestor of candidate HEAD',
        'non-empty, non-zero base ref',
        "Mode = 'Ratchet'",
        "Mode = 'Bootstrap'"
    )) {
        if (-not $qualityBaseSource.Contains($qualityBaseFragment, [StringComparison]::Ordinal)) {
            throw "Quality baseline resolver is missing fail-closed evidence '$qualityBaseFragment'."
        }
    }
    if ($requiredWorkflow -notmatch '(?s)foreach \(\$project in \$resource\).*?--collect "XPlat Code Coverage"') {
        throw 'Every required resource runner must emit its own bound coverage report.'
    }

    & $coverageScript -RunGuardSelfTest *> $null
    $coverageSource = Get-Content $coverageScript -Raw
    foreach ($requiredCoverageFragment in @(
        'MetadataReaderProvider]::FromPortablePdbStream',
        'Portable-PDB production source evidence',
        'Merge-WithAuthoritativeUniverse',
        'Assert-ObservedProductionCoverage',
        'Assert-ReviewedUniverseUpdate',
        'Resolve-LogicalCoverageCopies',
        'Assert-CoverageDigestOwner',
        'Assert-TrxStorageMatchesRunner',
        'Get-CoberturaLineNodes',
        'Resolve-CoberturaProductionSourcePath',
        'executableSourceIds',
        'Authoritative coverage requires one clean committed HEAD',
        'cannot be accepted by UpdateBaseline'
    )) {
        if (-not $coverageSource.Contains($requiredCoverageFragment, [StringComparison]::Ordinal)) {
            throw "Coverage omission guard is missing authoritative evidence '$requiredCoverageFragment'."
        }
    }

    $duplicationFixtureRoot = Join-Path $tempRoot 'duplication'
    $duplicationBaselinePath = Join-Path $duplicationFixtureRoot 'scripts/tests/baselines/aicopilot-duplication.json'
    $duplicationReportPath = Join-Path $duplicationFixtureRoot 'report.json'
    $firstDuplicateBlock = @'
namespace Fixture;
internal static class FirstClone
{
    public static int Run(int value)
    {
        var alpha = value + 1;
        var beta = alpha + 2;
        var gamma = beta + 3;
        var delta = gamma + 4;
        var epsilon = delta + 5;
        var zeta = epsilon + 6;
        var eta = zeta + 7;
        var theta = eta + 8;
        return theta;
    }
}
'@
    $secondDuplicateBlock = $firstDuplicateBlock.Replace('FirstClone', 'SecondClone')
    Write-FixtureFile (Join-Path $duplicationFixtureRoot 'src/core/FirstClone.cs') $firstDuplicateBlock
    Write-FixtureFile (Join-Path $duplicationFixtureRoot 'src/core/SecondClone.cs') $secondDuplicateBlock
    Assert-Fails {
        & $duplicationScript `
            -RepositoryRoot $duplicationFixtureRoot `
            -BaselinePath $duplicationBaselinePath `
            -OutputPath $duplicationReportPath `
            -UpdateBaseline
    } 'Duplication baseline does not exist'
    $reviewedReport = Get-Content $duplicationReportPath -Raw | ConvertFrom-Json -Depth 16
    $reviewedBaseline = [ordered]@{
        schemaVersion = 4
        instanceIdentity = 'path+line'
        categories = [ordered]@{}
    }
    foreach ($categoryProperty in $reviewedReport.categories.PSObject.Properties) {
        $signatureMaximum = [ordered]@{}
        foreach ($group in @($categoryProperty.Value.groups)) {
            $signatureMaximum[[string]$group.signature] = [ordered]@{
                maximumInstanceCount = [int]$group.instanceCount
                maximumDuplicatedLines = [int]$group.duplicatedLines
                maximumDuplicatedTokens = [int]$group.duplicatedTokens
            }
        }
        $reviewedBaseline.categories[$categoryProperty.Name] = [ordered]@{
            maximum = $categoryProperty.Value.metrics
            signatures = $signatureMaximum
        }
    }
    Write-FixtureFile $duplicationBaselinePath ($reviewedBaseline | ConvertTo-Json -Depth 12)
    & git -C $duplicationFixtureRoot init --quiet
    & git -C $duplicationFixtureRoot add src
    & git -C $duplicationFixtureRoot -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' `
        commit --quiet -m 'duplication source base'
    & git -C $duplicationFixtureRoot update-ref refs/remotes/origin/main HEAD
    & git -C $duplicationFixtureRoot add scripts/tests/baselines/aicopilot-duplication.json
    & git -C $duplicationFixtureRoot -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' `
        commit --quiet -m 'initial duplication baseline candidate'
    & $duplicationScript `
        -RepositoryRoot $duplicationFixtureRoot `
        -BaselinePath $duplicationBaselinePath `
        -OutputPath $duplicationReportPath *> $null
    & git -C $duplicationFixtureRoot update-ref refs/remotes/origin/main HEAD
    & git -C $duplicationFixtureRoot -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' `
        commit --quiet --allow-empty -m 'duplication ratchet candidate'

    $swappedDuplicateBlock = @'
namespace Fixture;
internal static class FirstClone
{
    public static int Calculate(int source)
    {
        var red = source * 11;
        var orange = red * 12;
        var yellow = orange * 13;
        var green = yellow * 14;
        var blue = green * 15;
        var indigo = blue * 16;
        var violet = indigo * 17;
        var black = violet * 18;
        return black;
    }
}
'@
    Write-FixtureFile (Join-Path $duplicationFixtureRoot 'src/core/FirstClone.cs') $swappedDuplicateBlock
    Write-FixtureFile `
        (Join-Path $duplicationFixtureRoot 'src/core/SecondClone.cs') `
        ($swappedDuplicateBlock.Replace('FirstClone', 'SecondClone'))
    Assert-Fails {
        & $duplicationScript `
            -RepositoryRoot $duplicationFixtureRoot `
            -BaselinePath $duplicationBaselinePath `
            -OutputPath $duplicationReportPath
    } 'Duplication signature grew'

    Write-FixtureFile (Join-Path $duplicationFixtureRoot 'src/core/FirstClone.cs') $firstDuplicateBlock
    Write-FixtureFile (Join-Path $duplicationFixtureRoot 'src/core/SecondClone.cs') $secondDuplicateBlock
    $renamedStructuralClone = @'
namespace Fixture;
internal static class RenamedStructure
{
    public static int Calculate(int source)
    {
        var red = source + 101;
        var orange = red + 202;
        var yellow = orange + 303;
        var green = yellow + 404;
        var blue = green + 505;
        var indigo = blue + 606;
        var violet = indigo + 707;
        var black = violet + 808;
        return black;
    }
}
'@
    Write-FixtureFile (Join-Path $duplicationFixtureRoot 'src/core/RenamedStructure.cs') $renamedStructuralClone
    Assert-Fails {
        & $duplicationScript `
            -RepositoryRoot $duplicationFixtureRoot `
            -BaselinePath $duplicationBaselinePath `
            -OutputPath $duplicationReportPath
    } 'Duplication signature instance metric grew:[\s\S]*category=productionStructural'
    Remove-Item (Join-Path $duplicationFixtureRoot 'src/core/RenamedStructure.cs') -Force

    Write-FixtureFile `
        (Join-Path $duplicationFixtureRoot 'src/core/ThirdClone.cs') `
        ($firstDuplicateBlock.Replace('FirstClone', 'ThirdClone'))
    Assert-Fails {
        & $duplicationScript `
            -RepositoryRoot $duplicationFixtureRoot `
            -BaselinePath $duplicationBaselinePath `
            -OutputPath $duplicationReportPath
    } 'Duplication signature instance metric grew'
    Assert-Fails {
        & $duplicationScript `
            -RepositoryRoot $duplicationFixtureRoot `
            -BaselinePath $duplicationBaselinePath `
            -OutputPath $duplicationReportPath `
            -UpdateBaseline
    } 'Duplication signature instance metric grew'

    Write-FixtureFile `
        (Join-Path $duplicationFixtureRoot 'src/core/FirstClone.cs') `
        ($firstDuplicateBlock + [Environment]::NewLine + $firstDuplicateBlock.Replace('FirstClone', 'SameFileClone'))
    Write-FixtureFile (Join-Path $duplicationFixtureRoot 'src/core/SecondClone.cs') $secondDuplicateBlock
    Remove-Item (Join-Path $duplicationFixtureRoot 'src/core/ThirdClone.cs') -Force
    Assert-Fails {
        & $duplicationScript `
            -RepositoryRoot $duplicationFixtureRoot `
            -BaselinePath $duplicationBaselinePath `
            -OutputPath $duplicationReportPath
    } 'Duplication signature instance metric grew'

    $mutationFixtureRoot = Join-Path $tempRoot 'mutation'
    $mutationReportPath = Join-Path $mutationFixtureRoot 'mutation-report.json'
    $mutationBaselinePath = Join-Path $mutationFixtureRoot 'scripts/tests/baselines/aicopilot-mutation.json'
    $mutationSummaryPath = Join-Path $mutationFixtureRoot 'mutation-summary.json'
    $mutationTargetPath = Join-Path $mutationFixtureRoot 'src/infrastructure/AICopilot.EntityFrameworkCore/Security/SecretStringEncryptor.cs'
    $mutationReport = [ordered]@{
        schemaVersion = '2'
        files = [ordered]@{
            $mutationTargetPath = [ordered]@{
                mutants = @(
                    [ordered]@{
                        id = '0'; mutatorName = 'Boolean'; replacement = 'false'; status = 'Killed'; static = $false
                        location = [ordered]@{
                            start = [ordered]@{ line = 1; column = 1 }
                            end = [ordered]@{ line = 1; column = 2 }
                        }
                    },
                    [ordered]@{
                        id = '1'; mutatorName = 'Boolean'; replacement = 'true'; status = 'Survived'; static = $false
                        location = [ordered]@{
                            start = [ordered]@{ line = 2; column = 1 }
                            end = [ordered]@{ line = 2; column = 2 }
                        }
                    }
                )
            }
        }
    }
    $mutationBaseline = [ordered]@{
        schemaVersion = 2
        toolPackage = 'dotnet-stryker'
        toolVersion = '4.16.0'
        project = 'AICopilot.EntityFrameworkCore.csproj'
        targetFile = 'src/infrastructure/AICopilot.EntityFrameworkCore/Security/SecretStringEncryptor.cs'
        mutationLevel = 'Standard'
        minimumMutationScore = 50
        minimumEvaluatedRate = 100
        maximumSurvivedRate = 50
        maximumNoCoverageRate = 0
        maximumTimeoutRate = 0
        maximumRuntimeErrorRate = 0
    }
    Write-FixtureFile $mutationTargetPath "line one`nline two`nline three"
    New-Item -ItemType Directory -Path (Join-Path $mutationFixtureRoot '.config') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $mutationFixtureRoot 'src/tests/AICopilot.InProcessTests') -Force | Out-Null
    Copy-Item (Join-Path $RepositoryRoot '.config/dotnet-tools.json') `
        (Join-Path $mutationFixtureRoot '.config/dotnet-tools.json')
    Copy-Item (Join-Path $RepositoryRoot 'src/tests/AICopilot.InProcessTests/stryker-config.json') `
        (Join-Path $mutationFixtureRoot 'src/tests/AICopilot.InProcessTests/stryker-config.json')
    Write-FixtureFile $mutationReportPath ($mutationReport | ConvertTo-Json -Depth 16)
    Write-FixtureFile $mutationBaselinePath ($mutationBaseline | ConvertTo-Json -Depth 16)
    & git -C $mutationFixtureRoot init --quiet
    & git -C $mutationFixtureRoot add -A
    & git -C $mutationFixtureRoot -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' `
        commit --quiet -m 'mutation baseline'
    & git -C $mutationFixtureRoot update-ref refs/remotes/origin/main HEAD
    & git -C $mutationFixtureRoot -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' `
        commit --quiet --allow-empty -m 'mutation candidate'
    & $mutationScript `
        -RepositoryRoot $mutationFixtureRoot `
        -ReportPath $mutationReportPath `
        -BaselinePath $mutationBaselinePath `
        -OutputPath $mutationSummaryPath *> $null

    $emptyMutationReport = [ordered]@{
        schemaVersion = '2'
        files = [ordered]@{}
    }
    Write-FixtureFile $mutationReportPath ($emptyMutationReport | ConvertTo-Json -Depth 16)
    Assert-Fails {
        & $mutationScript `
            -RepositoryRoot $mutationFixtureRoot `
            -ReportPath $mutationReportPath `
            -BaselinePath $mutationBaselinePath `
            -OutputPath $mutationSummaryPath
    } 'contains no mutants for fixed target'

    $unexpectedTargetPath = Join-Path $mutationFixtureRoot 'src/infrastructure/AICopilot.EntityFrameworkCore/Security/UnexpectedSecretPolicy.cs'
    $targetDriftReport = [ordered]@{
        schemaVersion = '2'
        files = [ordered]@{
            $mutationTargetPath = [ordered]@{
                mutants = @([ordered]@{ status = 'Killed' })
            }
            $unexpectedTargetPath = [ordered]@{
                mutants = @([ordered]@{ status = 'Survived' })
            }
        }
    }
    Write-FixtureFile $mutationReportPath ($targetDriftReport | ConvertTo-Json -Depth 16)
    Assert-Fails {
        & $mutationScript `
            -RepositoryRoot $mutationFixtureRoot `
            -ReportPath $mutationReportPath `
            -BaselinePath $mutationBaselinePath `
            -OutputPath $mutationSummaryPath
    } 'Mutation target drift detected'

    Write-FixtureFile $mutationReportPath ($mutationReport | ConvertTo-Json -Depth 16)
    $mutationBaseline.minimumMutationScore = 51
    Write-FixtureFile $mutationBaselinePath ($mutationBaseline | ConvertTo-Json -Depth 16)
    Assert-Fails {
        & $mutationScript `
            -RepositoryRoot $mutationFixtureRoot `
            -ReportPath $mutationReportPath `
            -BaselinePath $mutationBaselinePath `
            -OutputPath $mutationSummaryPath
    } 'Mutation quality regressed'

    $mutationBaseline.minimumMutationScore = 50
    Write-FixtureFile $mutationBaselinePath ($mutationBaseline | ConvertTo-Json -Depth 16)
    $degradedMutationReport = $mutationReport | ConvertTo-Json -Depth 16 | ConvertFrom-Json -Depth 16
    $degradedMutationTarget = @($degradedMutationReport.files.PSObject.Properties)[0].Value
    $degradedMutationTarget.mutants[0].status = 'Survived'
    Write-FixtureFile $mutationReportPath ($degradedMutationReport | ConvertTo-Json -Depth 16)
    Assert-Fails {
        & $mutationScript `
            -RepositoryRoot $mutationFixtureRoot `
            -ReportPath $mutationReportPath `
            -BaselinePath $mutationBaselinePath `
            -OutputPath $mutationSummaryPath `
            -UpdateBaseline
    } 'Mutation quality regressed'
    if ([double](Get-Content $mutationBaselinePath -Raw | ConvertFrom-Json).minimumMutationScore -ne 50) {
        throw 'Mutation UpdateBaseline rewrote the immutable threshold after a degraded run.'
    }

    $expandedMutationReport = $mutationReport | ConvertTo-Json -Depth 16 | ConvertFrom-Json -Depth 16
    $expandedMutationTarget = @($expandedMutationReport.files.PSObject.Properties)[0].Value
    $expandedMutationTarget.mutants += [pscustomobject]@{
        id = 'candidate-new-mutant'
        mutatorName = 'String'
        replacement = '"candidate"'
        status = 'Killed'
        static = $false
        location = [pscustomobject]@{
            start = [pscustomobject]@{ line = 3; column = 1 }
            end = [pscustomobject]@{ line = 3; column = 2 }
        }
    }
    Write-FixtureFile $mutationReportPath ($expandedMutationReport | ConvertTo-Json -Depth 16)
    & $mutationScript `
        -RepositoryRoot $mutationFixtureRoot `
        -ReportPath $mutationReportPath `
        -BaselinePath $mutationBaselinePath `
        -OutputPath $mutationSummaryPath `
        -UpdateBaseline *> $null
    $updatedMutationBaseline = Get-Content $mutationBaselinePath -Raw | ConvertFrom-Json
    if ($null -ne $updatedMutationBaseline.PSObject.Properties['expectedGeneratedMutants'] -or
        $null -ne $updatedMutationBaseline.PSObject.Properties['expectedEvaluatedMutants'] -or
        $null -ne $updatedMutationBaseline.PSObject.Properties['mutantIdentitySha256'] -or
        [double]$updatedMutationBaseline.minimumMutationScore -le 50) {
        throw 'Legitimate candidate source changes must produce a fresh mutant set without freezing count or identity.'
    }

    $duplicateMutationReport = $expandedMutationReport | ConvertTo-Json -Depth 16 | ConvertFrom-Json -Depth 16
    $duplicateMutationTarget = @($duplicateMutationReport.files.PSObject.Properties)[0].Value
    $duplicateMutationTarget.mutants[2].id = '1'
    Write-FixtureFile $mutationReportPath ($duplicateMutationReport | ConvertTo-Json -Depth 16)
    Assert-Fails {
        & $mutationScript `
            -RepositoryRoot $mutationFixtureRoot `
            -ReportPath $mutationReportPath `
            -BaselinePath $mutationBaselinePath `
            -OutputPath $mutationSummaryPath
    } 'target mutant ids must be unique'

    $compatibilityFixtureRoot = Join-Path $tempRoot 'compatibility'
    $compatibilitySourcePath = Join-Path $compatibilityFixtureRoot 'src/core/RuntimeAdapterShim.cs'
    $compatibilityInventoryPath = Join-Path $compatibilityFixtureRoot 'inventory.json'
    $compatibilityBaselinePath = Join-Path $compatibilityFixtureRoot 'scripts/tests/baselines/aicopilot-compatibility.json'
    $compatibilityOutputPath = Join-Path $compatibilityFixtureRoot 'output.json'
    Write-FixtureFile $compatibilitySourcePath @'
namespace Fixture;
internal static class RuntimeAdapterShim
{
    internal static void Run() { }
}
internal sealed class ConsumerMarker
{
    internal void Execute() => RuntimeAdapterShim.Run();
}
'@
    Write-FixtureFile $compatibilityInventoryPath @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {
      "AI-ORDINARY-RUNTIME-ADAPTER-FIXTURE": "T:Fixture.RuntimeAdapterShim"
    },
    "callerScans": {
      "AI-ORDINARY-RUNTIME-ADAPTER-FIXTURE/primary": ["M:Fixture.RuntimeAdapterShim.Run"]
    },
    "candidateDispositions": {
      "T:Fixture.RuntimeAdapterShim": "AI-ORDINARY-RUNTIME-ADAPTER-FIXTURE"
    }
  },
  "items": [],
  "ordinaryAbstractions": [
    {
      "id": "AI-ORDINARY-RUNTIME-ADAPTER-FIXTURE",
      "disposition": "ordinaryAbstraction",
      "decisionReason": "Behavior fixture only.",
      "producer": { "path": "src/core/RuntimeAdapterShim.cs", "contains": "RuntimeAdapterShim" },
      "consumers": [
        { "path": "src/core/RuntimeAdapterShim.cs", "contains": "ConsumerMarker" }
      ],
      "candidateEvidence": [
        { "path": "src/core/RuntimeAdapterShim.cs", "contains": "RuntimeAdapterShim" }
      ],
      "callerScans": [
        {
          "id": "primary",
          "roots": ["src/core"],
          "extensions": [".cs"],
          "contains": ".Run(",
          "excludePaths": []
        }
      ],
      "replacement": "notApplicable: behavior fixture ordinary abstraction",
      "deletionCondition": "No production lifecycle applies."
    }
  ]
}
'@
    $validCompatibilityInventory = Get-Content $compatibilityInventoryPath -Raw
    foreach ($scanRoot in @(
            'src/core',
            'src/hosts',
            'src/infrastructure',
            'src/services',
            'src/shared',
            'src/testing',
            'src/vues/AICopilot.Web/src',
            'scripts/tests')) {
        New-Item -ItemType Directory -Path (Join-Path $compatibilityFixtureRoot $scanRoot) -Force | Out-Null
    }
    $unclassifiedQualityToolPath = Join-Path $compatibilityFixtureRoot 'scripts/tests/LegacyQualityAdapter.ps1'
    Write-FixtureFile $unclassifiedQualityToolPath @'
$QualityLegacyAdapter = 1
Write-Output $QualityLegacyAdapter
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $compatibilityFixtureRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } 'Compatibility signal must have exactly one disposition: scripts/tests/LegacyQualityAdapter.ps1'
    Write-FixtureFile $unclassifiedQualityToolPath @'
function Invoke-QualityFallbackWrapper { return 1 }
# Invoke-QualityFallbackWrapper
$example = 'Invoke-QualityFallbackWrapper'
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $compatibilityFixtureRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } "PowerShell compatibility signal 'Invoke-QualityFallbackWrapper'.*has no exact executable references"
    Write-FixtureFile $unclassifiedQualityToolPath @'
function Invoke-QualityFallbackWrapper { return 1 }
function Invoke-QualityEntry { Invoke-QualityFallbackWrapper }
Invoke-QualityEntry
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $compatibilityFixtureRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } 'Compatibility signal must have exactly one disposition: scripts/tests/LegacyQualityAdapter.ps1'
    Remove-Item $unclassifiedQualityToolPath -Force

    $unclassifiedInventory = $validCompatibilityInventory | ConvertFrom-Json -Depth 64
    $unclassifiedInventory.csharpSymbols.candidateDispositions.PSObject.Properties.Remove(
        'T:Fixture.RuntimeAdapterShim')
    Write-FixtureFile $compatibilityInventoryPath ($unclassifiedInventory | ConvertTo-Json -Depth 64)
    Write-FixtureFile $compatibilityBaselinePath @'
{
  "schemaVersion": 3,
  "unclassifiedCompatibilitySignals": 0,
  "compatibilityItems": []
}
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $compatibilityFixtureRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } 'C# compatibility signal disposition roster differs'

    $zeroConsumerInventory = $validCompatibilityInventory | ConvertFrom-Json -Depth 64
    $zeroConsumerInventory.ordinaryAbstractions[0].callerScans[0].contains = '.NoSuchVisualizationCaller('
    $zeroConsumerInventory.csharpSymbols.callerScans.'AI-ORDINARY-RUNTIME-ADAPTER-FIXTURE/primary' = @(
        'M:Fixture.DoesNotExist.NoSuchVisualizationCaller'
    )
    Write-FixtureFile $compatibilityInventoryPath ($zeroConsumerInventory | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $compatibilityFixtureRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } 'has no production call sites; physically delete the abstraction'

    Write-FixtureFile $compatibilitySourcePath @'
namespace Fixture;
internal static class RuntimeAdapterShim
{
    internal static void Run() { }
}
internal static class CurrentFallbackPolicy
{
    internal static void Apply() { }
}
internal sealed class ConsumerMarker
{
    internal void Execute()
    {
        RuntimeAdapterShim.Run();
        CurrentFallbackPolicy.Apply();
    }
}
'@
    $wrongBindingInventory = $validCompatibilityInventory | ConvertFrom-Json -Depth 64
    $wrongBindingInventory.csharpSymbols.producers | Add-Member `
        -NotePropertyName 'AI-ORDINARY-CURRENT-FALLBACK-FIXTURE' `
        -NotePropertyValue 'T:Fixture.CurrentFallbackPolicy'
    $wrongBindingInventory.csharpSymbols.callerScans | Add-Member `
        -NotePropertyName 'AI-ORDINARY-CURRENT-FALLBACK-FIXTURE/primary' `
        -NotePropertyValue @('M:Fixture.CurrentFallbackPolicy.Apply')
    $wrongBindingInventory.csharpSymbols.candidateDispositions | Add-Member `
        -NotePropertyName 'T:Fixture.CurrentFallbackPolicy' `
        -NotePropertyValue 'AI-ORDINARY-CURRENT-FALLBACK-FIXTURE'
    $wrongBindingInventory.csharpSymbols.candidateDispositions.'T:Fixture.RuntimeAdapterShim' =
        'AI-ORDINARY-CURRENT-FALLBACK-FIXTURE'
    $wrongBindingInventory.ordinaryAbstractions += [pscustomobject]@{
        id = 'AI-ORDINARY-CURRENT-FALLBACK-FIXTURE'
        disposition = 'ordinaryAbstraction'
        decisionReason = 'Behavior fixture only.'
        producer = [pscustomobject]@{
            path = 'src/core/RuntimeAdapterShim.cs'
            contains = 'CurrentFallbackPolicy'
        }
        consumers = @([pscustomobject]@{
                path = 'src/core/RuntimeAdapterShim.cs'
                contains = 'ConsumerMarker'
            })
        candidateEvidence = @([pscustomobject]@{
                path = 'src/core/RuntimeAdapterShim.cs'
                contains = 'CurrentFallbackPolicy'
            })
        callerScans = @([pscustomobject]@{
                id = 'primary'
                roots = @('src/core')
                extensions = @('.cs')
                contains = '.Apply('
                excludePaths = @()
            })
        replacement = 'notApplicable: behavior fixture ordinary abstraction'
        deletionCondition = 'No production lifecycle applies.'
    }
    Write-FixtureFile $compatibilityInventoryPath ($wrongBindingInventory | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $compatibilityFixtureRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } 'is not bound to exact candidate evidence'

    Write-FixtureFile $compatibilitySourcePath @'
namespace Fixture;
internal static class LegacyMigrationAdapter
{
    internal static void Run() { }
}
internal sealed class ConsumerMarker
{
    internal void Execute() => LegacyMigrationAdapter.Run();
}
'@
    $legacyOrdinaryInventory = $validCompatibilityInventory | ConvertFrom-Json -Depth 64
    $legacyOrdinaryInventory.csharpSymbols.producers.'AI-ORDINARY-RUNTIME-ADAPTER-FIXTURE' =
        'T:Fixture.LegacyMigrationAdapter'
    $legacyOrdinaryInventory.csharpSymbols.callerScans.'AI-ORDINARY-RUNTIME-ADAPTER-FIXTURE/primary' = @(
        'M:Fixture.LegacyMigrationAdapter.Run'
    )
    $legacyOrdinaryInventory.csharpSymbols.candidateDispositions.PSObject.Properties.Remove(
        'T:Fixture.RuntimeAdapterShim')
    $legacyOrdinaryInventory.csharpSymbols.candidateDispositions | Add-Member `
        -NotePropertyName 'T:Fixture.LegacyMigrationAdapter' `
        -NotePropertyValue 'AI-ORDINARY-RUNTIME-ADAPTER-FIXTURE'
    $legacyOrdinaryInventory.ordinaryAbstractions[0].producer.contains = 'LegacyMigrationAdapter'
    $legacyOrdinaryInventory.ordinaryAbstractions[0].candidateEvidence[0].contains = 'LegacyMigrationAdapter'
    Write-FixtureFile $compatibilityInventoryPath ($legacyOrdinaryInventory | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $compatibilityFixtureRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } 'must use an AI-COMPAT disposition'

    Write-FixtureFile $compatibilitySourcePath @'
namespace Fixture;
internal static class RuntimeAdapterShim
{
    internal static void Run() { }
}
internal sealed class ConsumerMarker
{
    internal void Execute() => RuntimeAdapterShim.Run();
}
'@
    $powerShellLegacyPath = Join-Path $compatibilityFixtureRoot 'scripts/tests/Invoke-LegacyQualityAdapter.ps1'
    Write-FixtureFile $powerShellLegacyPath @'
function Invoke-LegacyQualityAdapter { return 1 }
function Invoke-QualityEntry { Invoke-LegacyQualityAdapter }
Invoke-QualityEntry
'@
    $powerShellLegacyInventory = $validCompatibilityInventory | ConvertFrom-Json -Depth 64
    $powerShellLegacyInventory.ordinaryAbstractions += [pscustomobject]@{
        id = 'AI-ORDINARY-POWERSHELL-LEGACY-FIXTURE'
        disposition = 'ordinaryAbstraction'
        decisionReason = 'Behavior fixture only.'
        producer = [pscustomobject]@{
            path = 'scripts/tests/Invoke-LegacyQualityAdapter.ps1'
            contains = 'function Invoke-LegacyQualityAdapter'
        }
        consumers = @([pscustomobject]@{
                path = 'scripts/tests/Invoke-LegacyQualityAdapter.ps1'
                contains = 'function Invoke-QualityEntry'
            })
        candidateEvidence = @([pscustomobject]@{
                path = 'scripts/tests/Invoke-LegacyQualityAdapter.ps1'
                contains = 'Invoke-LegacyQualityAdapter'
            })
        callerScans = @([pscustomobject]@{
                id = 'primary'
                roots = @('scripts/tests')
                extensions = @('.ps1')
                contains = 'Invoke-LegacyQualityAdapter'
                excludePaths = @()
            })
        replacement = 'notApplicable: behavior fixture ordinary abstraction'
        deletionCondition = 'No production lifecycle applies.'
    }
    Write-FixtureFile $compatibilityInventoryPath ($powerShellLegacyInventory | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $compatibilityFixtureRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } "PowerShell migration signal 'Invoke-LegacyQualityAdapter' must use an AI-COMPAT disposition"
    Remove-Item $powerShellLegacyPath -Force

    $fakeCallerVariants = [ordered]@{
        comment = @'
namespace Fixture;
internal sealed class LegacyAdapterShim { }
internal sealed class ConsumerMarker { }
// Runtime.GhostCall();
'@
        string = @'
namespace Fixture;
internal sealed class LegacyAdapterShim { }
internal sealed class ConsumerMarker
{
    private const string Example = "Runtime.GhostCall()";
}
'@
        declaration = @'
namespace Fixture;
internal sealed class LegacyAdapterShim
{
    internal static void GhostCall() { }
}
internal sealed class ConsumerMarker { }
'@
        unrelated = @'
namespace Fixture;
internal sealed class LegacyAdapterShim
{
    internal static void GhostCall() { }
}
internal static class Unrelated
{
    internal static void GhostCall() { }
    internal static void Run() => GhostCall();
}
internal sealed class ConsumerMarker { }
'@
        methodGroup = @'
namespace Fixture;
internal sealed class LegacyAdapterShim
{
    internal static void GhostCall() { }
}
internal sealed class ConsumerMarker
{
    private readonly Action callback = LegacyAdapterShim.GhostCall;
}
'@
        typeof = @'
namespace Fixture;
internal sealed class LegacyAdapterShim
{
    internal static void GhostCall() { }
}
internal sealed class ConsumerMarker
{
    private static readonly Type RuntimeType = typeof(LegacyAdapterShim);
}
'@
        selfOnly = @'
namespace Fixture;
internal sealed class LegacyAdapterShim
{
    internal static void Run() => GhostCall();
    internal static void GhostCall() { }
}
internal sealed class ConsumerMarker { }
'@
    }
    foreach ($fakeCallerVariant in $fakeCallerVariants.GetEnumerator()) {
        $fakeCallerRoot = Join-Path $tempRoot "compatibility-fake-caller-$($fakeCallerVariant.Key)"
        $fakeCallerSourcePath = Join-Path $fakeCallerRoot 'src/core/LegacyAdapterShim.cs'
        $fakeCallerInventoryPath = Join-Path $fakeCallerRoot 'inventory.json'
        $fakeCallerBaselinePath = Join-Path $fakeCallerRoot 'scripts/tests/baselines/aicopilot-compatibility.json'
        $fakeCallerOutputPath = Join-Path $fakeCallerRoot 'output.json'
        Write-FixtureFile $fakeCallerSourcePath ([string]$fakeCallerVariant.Value)
        Write-FixtureFile $fakeCallerInventoryPath @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {
      "AI-ORDINARY-FAKE-CALLER": "T:Fixture.LegacyAdapterShim"
    },
    "callerScans": {
      "AI-ORDINARY-FAKE-CALLER/ghost-call": ["M:Fixture.LegacyAdapterShim.GhostCall"]
    },
    "candidateDispositions": {
      "T:Fixture.LegacyAdapterShim": "AI-ORDINARY-FAKE-CALLER"
    }
  },
  "items": [],
  "ordinaryAbstractions": [
    {
      "id": "AI-ORDINARY-FAKE-CALLER",
      "disposition": "ordinaryAbstraction",
      "decisionReason": "Behavior fixture only.",
      "producer": { "path": "src/core/LegacyAdapterShim.cs", "contains": "LegacyAdapterShim" },
      "consumers": [
        { "path": "src/core/LegacyAdapterShim.cs", "contains": "ConsumerMarker" }
      ],
      "candidateEvidence": [
        { "path": "src/core/LegacyAdapterShim.cs", "contains": "LegacyAdapterShim" }
      ],
      "callerScans": [
        {
          "id": "ghost-call",
          "roots": ["src/core"],
          "extensions": [".cs"],
          "contains": ".GhostCall(",
          "excludePaths": []
        }
      ],
      "replacement": "notApplicable: behavior fixture ordinary abstraction",
      "deletionCondition": "No real production invocation exists."
    }
  ]
}
'@
        Write-FixtureFile $fakeCallerBaselinePath @'
{
  "schemaVersion": 3,
  "unclassifiedCompatibilitySignals": 0,
  "compatibilityItems": []
}
'@
        Assert-Fails {
            & $compatibilityScript `
                -RepositoryRoot $fakeCallerRoot `
                -InventoryPath $fakeCallerInventoryPath `
                -BaselinePath $fakeCallerBaselinePath `
                -OutputPath $fakeCallerOutputPath
        } 'has no exact production references; physically delete it'
    }

    $deadMemberRoot = Join-Path $tempRoot 'compatibility-used-type-dead-member'
    $deadMemberSourcePath = Join-Path $deadMemberRoot 'src/core/RuntimeAdapterShim.cs'
    $deadMemberInventoryPath = Join-Path $deadMemberRoot 'inventory.json'
    $deadMemberBaselinePath = Join-Path $deadMemberRoot 'scripts/tests/baselines/aicopilot-compatibility.json'
    $deadMemberOutputPath = Join-Path $deadMemberRoot 'output.json'
    Write-FixtureFile $deadMemberSourcePath @'
namespace Fixture;
internal sealed class RuntimeAdapterShim
{
    internal int Run() => 1;
    private int CurrentFallback() => CurrentFallback();
}
internal sealed class ConsumerMarker
{
    internal int Execute() => new RuntimeAdapterShim().Run();
}
'@
    Write-FixtureFile $deadMemberInventoryPath @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {
      "AI-ORDINARY-USED-TYPE-DEAD-MEMBER": "T:Fixture.RuntimeAdapterShim"
    },
    "callerScans": {
      "AI-ORDINARY-USED-TYPE-DEAD-MEMBER/primary": ["M:Fixture.RuntimeAdapterShim.Run"]
    },
    "candidateDispositions": {
      "T:Fixture.RuntimeAdapterShim": "AI-ORDINARY-USED-TYPE-DEAD-MEMBER",
      "M:Fixture.RuntimeAdapterShim.CurrentFallback": "AI-ORDINARY-USED-TYPE-DEAD-MEMBER"
    }
  },
  "items": [],
  "ordinaryAbstractions": [
    {
      "id": "AI-ORDINARY-USED-TYPE-DEAD-MEMBER",
      "disposition": "ordinaryAbstraction",
      "decisionReason": "Behavior fixture only.",
      "producer": { "path": "src/core/RuntimeAdapterShim.cs", "contains": "RuntimeAdapterShim" },
      "consumers": [
        { "path": "src/core/RuntimeAdapterShim.cs", "contains": "ConsumerMarker" }
      ],
      "candidateEvidence": [
        { "path": "src/core/RuntimeAdapterShim.cs", "contains": "RuntimeAdapterShim" },
        { "path": "src/core/RuntimeAdapterShim.cs", "contains": "CurrentFallback" }
      ],
      "callerScans": [
        {
          "id": "primary",
          "roots": ["src/core"],
          "extensions": [".cs"],
          "contains": ".Run(",
          "excludePaths": []
        }
      ],
      "replacement": "notApplicable: behavior fixture ordinary abstraction",
      "deletionCondition": "The type is used but the fallback member is dead."
    }
  ]
}
'@
    Write-FixtureFile $deadMemberBaselinePath @'
{
  "schemaVersion": 3,
  "unclassifiedCompatibilitySignals": 0,
  "compatibilityItems": []
}
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $deadMemberRoot `
            -InventoryPath $deadMemberInventoryPath `
            -BaselinePath $deadMemberBaselinePath `
            -OutputPath $deadMemberOutputPath
    } "CurrentFallback.*has no exact production references; physically delete it"

    $scriptSemanticRoot = Join-Path $tempRoot 'compatibility-script-semantics'
    Initialize-LedgerScanRoots $scriptSemanticRoot
    $scriptSemanticInventoryPath = Join-Path $scriptSemanticRoot 'inventory.json'
    $scriptSemanticBaselinePath = Join-Path $scriptSemanticRoot 'scripts/tests/baselines/aicopilot-compatibility.json'
    $scriptSemanticOutputPath = Join-Path $scriptSemanticRoot 'output.json'
    Write-FixtureFile (Join-Path $scriptSemanticRoot 'src/vues/AICopilot.Web/src/runtimePolicy.ts') @'
export class RuntimePolicy {
  currentFallbackMethod() { return 1 }
  currentFallbackField = () => 2
}
export const currentFallbackArrow = () => 3
export const runtimeTools = {
  currentFallbackObject() { return 4 },
  currentFallbackValue: () => 5
}
const runtime = new RuntimePolicy()
export function runCurrentPolicy() {
  return runtime.currentFallbackMethod() + runtime.currentFallbackField() +
    currentFallbackArrow() + runtimeTools.currentFallbackObject() +
    runtimeTools.currentFallbackValue()
}
runCurrentPolicy()
'@
    Write-FixtureFile (Join-Path $scriptSemanticRoot 'scripts/tests/quality-fallback.sh') @'
quality_fallback_wrapper() { echo ok; }
run_quality_entry() { quality_fallback_wrapper; }
run_quality_entry
'@
    Write-FixtureFile $scriptSemanticInventoryPath @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {},
    "callerScans": {},
    "candidateDispositions": {}
  },
  "items": [],
  "ordinaryAbstractions": [
    {
      "id": "AI-ORDINARY-TS-SIGNAL-SURFACES",
      "disposition": "ordinaryAbstraction",
      "decisionReason": "Behavior fixture only.",
      "producer": { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "export class RuntimePolicy" },
      "consumers": [
        { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "runCurrentPolicy()" }
      ],
      "candidateEvidence": [
        { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "Fallback" }
      ],
      "callerScans": [
        {
          "id": "method-call",
          "roots": ["src/vues/AICopilot.Web/src"],
          "extensions": [".ts"],
          "contains": "runtime.currentFallbackMethod(",
          "excludePaths": []
        }
      ],
      "replacement": "notApplicable: behavior fixture ordinary abstraction",
      "deletionCondition": "All exact TypeScript signals remain executable."
    },
    {
      "id": "AI-ORDINARY-SHELL-FALLBACK-SURFACE",
      "disposition": "ordinaryAbstraction",
      "decisionReason": "Behavior fixture only.",
      "producer": { "path": "scripts/tests/quality-fallback.sh", "contains": "quality_fallback_wrapper()" },
      "consumers": [
        { "path": "scripts/tests/quality-fallback.sh", "contains": "run_quality_entry()" }
      ],
      "candidateEvidence": [
        { "path": "scripts/tests/quality-fallback.sh", "contains": "quality_fallback_wrapper" }
      ],
      "callerScans": [
        {
          "id": "entry-call",
          "roots": ["scripts/tests"],
          "extensions": [".sh"],
          "contains": "quality_fallback_wrapper",
          "excludePaths": []
        }
      ],
      "replacement": "notApplicable: behavior fixture ordinary abstraction",
      "deletionCondition": "The shell helper remains executable from its entry."
    }
  ]
}
'@
    Write-FixtureFile $scriptSemanticBaselinePath @'
{
  "schemaVersion": 3,
  "unclassifiedCompatibilitySignals": 0,
  "compatibilityItems": []
}
'@
    & git -C $scriptSemanticRoot init --quiet
    & git -C $scriptSemanticRoot add -A
    & git -C $scriptSemanticRoot -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' `
        commit --quiet -m 'script semantic base'
    & git -C $scriptSemanticRoot update-ref refs/remotes/origin/main HEAD
    & git -C $scriptSemanticRoot -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' `
        commit --quiet --allow-empty -m 'script semantic candidate'
    & $compatibilityScript `
        -RepositoryRoot $scriptSemanticRoot `
        -InventoryPath $scriptSemanticInventoryPath `
        -BaselinePath $scriptSemanticBaselinePath `
        -OutputPath $scriptSemanticOutputPath *> $null

    $typescriptNegativeRoot = Join-Path $tempRoot 'compatibility-typescript-negatives'
    Initialize-LedgerScanRoots $typescriptNegativeRoot
    $typescriptNegativePath = Join-Path $typescriptNegativeRoot 'src/vues/AICopilot.Web/src/runtimePolicy.ts'
    $typescriptNegativeInventoryPath = Join-Path $typescriptNegativeRoot 'inventory.json'
    $typescriptNegativeBaselinePath = Join-Path $typescriptNegativeRoot 'scripts/tests/baselines/aicopilot-compatibility.json'
    $typescriptNegativeOutputPath = Join-Path $typescriptNegativeRoot 'output.json'
    Write-FixtureFile $typescriptNegativeBaselinePath @'
{
  "schemaVersion": 3,
  "unclassifiedCompatibilitySignals": 0,
  "compatibilityItems": []
}
'@
    $emptyScriptInventory = @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {},
    "callerScans": {},
    "candidateDispositions": {}
  },
  "items": [],
  "ordinaryAbstractions": []
}
'@
    Write-FixtureFile $typescriptNegativeInventoryPath $emptyScriptInventory
    Write-FixtureFile $typescriptNegativePath @'
export const currentFallback = () => 1
// currentFallback()
const example = 'currentFallback()'
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $typescriptNegativeRoot `
            -InventoryPath $typescriptNegativeInventoryPath `
            -BaselinePath $typescriptNegativeBaselinePath `
            -OutputPath $typescriptNegativeOutputPath
    } "TypeScript compatibility signal 'currentFallback'.*has no exact executable references"
    Write-FixtureFile $typescriptNegativePath @'
export class LegacyTypeAdapter { }
export const adapterKind = typeof LegacyTypeAdapter
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $typescriptNegativeRoot `
            -InventoryPath $typescriptNegativeInventoryPath `
            -BaselinePath $typescriptNegativeBaselinePath `
            -OutputPath $typescriptNegativeOutputPath
    } "TypeScript compatibility signal 'LegacyTypeAdapter'.*has no exact executable references"
    Write-FixtureFile $typescriptNegativePath @'
export interface LegacyTypeContract { value: string }
export function consume(value: LegacyTypeContract) { return value.value }
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $typescriptNegativeRoot `
            -InventoryPath $typescriptNegativeInventoryPath `
            -BaselinePath $typescriptNegativeBaselinePath `
            -OutputPath $typescriptNegativeOutputPath
    } "TypeScript compatibility signal 'LegacyTypeContract'.*has no exact executable references"

    Write-FixtureFile $typescriptNegativePath @'
export class RuntimePolicy {
  liveFallbackMethod() { return 1 }
  deadFallbackMethod() { return 2 }
}
const runtime = new RuntimePolicy()
runtime.liveFallbackMethod()
export const callback = runtime.deadFallbackMethod
'@
    Write-FixtureFile $typescriptNegativeInventoryPath @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {},
    "callerScans": {},
    "candidateDispositions": {}
  },
  "items": [],
  "ordinaryAbstractions": [
    {
      "id": "AI-ORDINARY-TS-LIVE-DEAD-SIGNALS",
      "disposition": "ordinaryAbstraction",
      "decisionReason": "Behavior fixture only.",
      "producer": { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "export class RuntimePolicy" },
      "consumers": [
        { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "runtime.liveFallbackMethod()" }
      ],
      "candidateEvidence": [
        { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "FallbackMethod" }
      ],
      "callerScans": [
        {
          "id": "live-method",
          "roots": ["src/vues/AICopilot.Web/src"],
          "extensions": [".ts"],
          "contains": "runtime.liveFallbackMethod(",
          "excludePaths": []
        }
      ],
      "replacement": "notApplicable: behavior fixture ordinary abstraction",
      "deletionCondition": "The dead method must not borrow the live method reference."
    }
  ]
}
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $typescriptNegativeRoot `
            -InventoryPath $typescriptNegativeInventoryPath `
            -BaselinePath $typescriptNegativeBaselinePath `
            -OutputPath $typescriptNegativeOutputPath
    } "TypeScript compatibility signal 'deadFallbackMethod'.*has no exact executable references"

    Write-FixtureFile $typescriptNegativePath @'
export const runtimeTools = {
  liveFallbackValue: () => 1,
  deadFallbackValue: () => 2
}
runtimeTools.liveFallbackValue()
'@
    Write-FixtureFile $typescriptNegativeInventoryPath @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {},
    "callerScans": {},
    "candidateDispositions": {}
  },
  "items": [],
  "ordinaryAbstractions": [
    {
      "id": "AI-ORDINARY-TS-DEAD-OBJECT-CALLABLE",
      "disposition": "ordinaryAbstraction",
      "decisionReason": "Behavior fixture only.",
      "producer": { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "export const runtimeTools" },
      "consumers": [
        { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "runtimeTools.liveFallbackValue()" }
      ],
      "candidateEvidence": [
        { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "FallbackValue" }
      ],
      "callerScans": [
        {
          "id": "live-object-call",
          "roots": ["src/vues/AICopilot.Web/src"],
          "extensions": [".ts"],
          "contains": "runtimeTools.liveFallbackValue(",
          "excludePaths": []
        }
      ],
      "replacement": "notApplicable: behavior fixture ordinary abstraction",
      "deletionCondition": "A callable object property needs its own exact invocation."
    }
  ]
}
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $typescriptNegativeRoot `
            -InventoryPath $typescriptNegativeInventoryPath `
            -BaselinePath $typescriptNegativeBaselinePath `
            -OutputPath $typescriptNegativeOutputPath
    } "TypeScript compatibility signal 'deadFallbackValue'.*has no exact executable references"

    Write-FixtureFile $typescriptNegativePath @'
export const legacyAdapter = () => 1
legacyAdapter()
'@
    Write-FixtureFile $typescriptNegativeInventoryPath @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {},
    "callerScans": {},
    "candidateDispositions": {}
  },
  "items": [],
  "ordinaryAbstractions": [
    {
      "id": "AI-ORDINARY-TS-LEGACY-SIGNAL",
      "disposition": "ordinaryAbstraction",
      "decisionReason": "Behavior fixture only.",
      "producer": { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "legacyAdapter =" },
      "consumers": [
        { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "legacyAdapter()" }
      ],
      "candidateEvidence": [
        { "path": "src/vues/AICopilot.Web/src/runtimePolicy.ts", "contains": "legacyAdapter" }
      ],
      "callerScans": [
        {
          "id": "legacy-call",
          "roots": ["src/vues/AICopilot.Web/src"],
          "extensions": [".ts"],
          "contains": "legacyAdapter(",
          "excludePaths": []
        }
      ],
      "replacement": "notApplicable: behavior fixture ordinary abstraction",
      "deletionCondition": "Migration names cannot be ordinary abstractions."
    }
  ]
}
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $typescriptNegativeRoot `
            -InventoryPath $typescriptNegativeInventoryPath `
            -BaselinePath $typescriptNegativeBaselinePath `
            -OutputPath $typescriptNegativeOutputPath
    } "TypeScript migration signal 'legacyAdapter' must use an AI-COMPAT disposition"

    $shellNegativePath = Join-Path $typescriptNegativeRoot 'scripts/tests/quality-fallback.sh'
    Write-FixtureFile $typescriptNegativePath 'export const currentPolicy = 1'
    Write-FixtureFile $typescriptNegativeInventoryPath $emptyScriptInventory
    Write-FixtureFile $shellNegativePath @'
quality_fallback_wrapper() { echo ok; }
# quality_fallback_wrapper
example='quality_fallback_wrapper'
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $typescriptNegativeRoot `
            -InventoryPath $typescriptNegativeInventoryPath `
            -BaselinePath $typescriptNegativeBaselinePath `
            -OutputPath $typescriptNegativeOutputPath
    } "Shell compatibility signal 'quality_fallback_wrapper'.*has no exact executable references"

    Write-FixtureFile $shellNegativePath @'
legacy_adapter() { echo ok; }
run_quality_entry() { legacy_adapter; }
run_quality_entry
'@
    Write-FixtureFile $typescriptNegativeInventoryPath @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {},
    "callerScans": {},
    "candidateDispositions": {}
  },
  "items": [],
  "ordinaryAbstractions": [
    {
      "id": "AI-ORDINARY-SHELL-LEGACY-SIGNAL",
      "disposition": "ordinaryAbstraction",
      "decisionReason": "Behavior fixture only.",
      "producer": { "path": "scripts/tests/quality-fallback.sh", "contains": "legacy_adapter()" },
      "consumers": [
        { "path": "scripts/tests/quality-fallback.sh", "contains": "run_quality_entry()" }
      ],
      "candidateEvidence": [
        { "path": "scripts/tests/quality-fallback.sh", "contains": "legacy_adapter" }
      ],
      "callerScans": [
        {
          "id": "legacy-call",
          "roots": ["scripts/tests"],
          "extensions": [".sh"],
          "contains": "legacy_adapter",
          "excludePaths": []
        }
      ],
      "replacement": "notApplicable: behavior fixture ordinary abstraction",
      "deletionCondition": "Migration names cannot be ordinary abstractions."
    }
  ]
}
'@
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $typescriptNegativeRoot `
            -InventoryPath $typescriptNegativeInventoryPath `
            -BaselinePath $typescriptNegativeBaselinePath `
            -OutputPath $typescriptNegativeOutputPath
    } "Shell migration signal 'legacy_adapter' must use an AI-COMPAT disposition"

    $ordinaryEvolutionRoot = Join-Path $tempRoot 'compatibility-ordinary-evolution'
    foreach ($scanRoot in @(
            'src/core',
            'src/hosts',
            'src/infrastructure',
            'src/services',
            'src/shared',
            'src/testing',
            'src/vues/AICopilot.Web/src',
            'scripts/tests')) {
        New-Item -ItemType Directory -Path (Join-Path $ordinaryEvolutionRoot $scanRoot) -Force | Out-Null
    }
    $ordinaryEvolutionSourcePath = Join-Path $ordinaryEvolutionRoot 'src/core/CurrentFallbackPolicy.cs'
    $ordinaryEvolutionInventoryPath = Join-Path $ordinaryEvolutionRoot 'inventory.json'
    $ordinaryEvolutionBaselinePath = Join-Path $ordinaryEvolutionRoot 'scripts/tests/baselines/aicopilot-compatibility.json'
    $ordinaryEvolutionOutputPath = Join-Path $ordinaryEvolutionRoot 'output.json'
    Write-FixtureFile $ordinaryEvolutionSourcePath @'
namespace Fixture;
internal static class CurrentFallbackPolicy
{
    internal static int Apply() => CurrentFallback();
    private static int CurrentFallback() => 1;
}
internal sealed class CurrentConsumer
{
    internal int Run() => CurrentFallbackPolicy.Apply();
}
'@
    Write-FixtureFile $ordinaryEvolutionInventoryPath @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {
      "AI-ORDINARY-CURRENT-FALLBACK-POLICY": "T:Fixture.CurrentFallbackPolicy"
    },
    "callerScans": {
      "AI-ORDINARY-CURRENT-FALLBACK-POLICY/current-callers": ["M:Fixture.CurrentFallbackPolicy.Apply"]
    },
    "candidateDispositions": {
      "T:Fixture.CurrentFallbackPolicy": "AI-ORDINARY-CURRENT-FALLBACK-POLICY",
      "M:Fixture.CurrentFallbackPolicy.CurrentFallback": "AI-ORDINARY-CURRENT-FALLBACK-POLICY"
    }
  },
  "items": [],
  "ordinaryAbstractions": [
    {
      "id": "AI-ORDINARY-CURRENT-FALLBACK-POLICY",
      "disposition": "ordinaryAbstraction",
      "decisionReason": "This is the current deterministic safety policy, not a bridge to an older contract.",
      "producer": { "path": "src/core/CurrentFallbackPolicy.cs", "contains": "internal static class CurrentFallbackPolicy" },
      "consumers": [
        { "path": "src/core/CurrentFallbackPolicy.cs", "contains": "internal sealed class CurrentConsumer" }
      ],
      "candidateEvidence": [
        { "path": "src/core/CurrentFallbackPolicy.cs", "contains": "CurrentFallbackPolicy" },
        { "path": "src/core/CurrentFallbackPolicy.cs", "contains": "CurrentFallback" }
      ],
      "callerScans": [
        {
          "id": "current-callers",
          "roots": ["src/core"],
          "extensions": [".cs"],
          "contains": ".Apply(",
          "excludePaths": []
        }
      ],
      "replacement": "notApplicable: current deterministic safety policy",
      "deletionCondition": "Delete only when the current safety policy is retired."
    }
  ]
}
'@
    Write-FixtureFile $ordinaryEvolutionBaselinePath @'
{
  "schemaVersion": 3,
  "unclassifiedCompatibilitySignals": 0,
  "compatibilityItems": []
}
'@
    & git -C $ordinaryEvolutionRoot init --quiet
    & git -C $ordinaryEvolutionRoot add -A
    & git -C $ordinaryEvolutionRoot -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' `
        commit --quiet -m 'ordinary abstraction base'
    & git -C $ordinaryEvolutionRoot update-ref refs/remotes/origin/main HEAD
    & git -C $ordinaryEvolutionRoot -c user.name='AICopilot Fixture' -c user.email='fixture@invalid' `
        commit --quiet --allow-empty -m 'ordinary abstraction candidate'
    & $compatibilityScript `
        -RepositoryRoot $ordinaryEvolutionRoot `
        -InventoryPath $ordinaryEvolutionInventoryPath `
        -BaselinePath $ordinaryEvolutionBaselinePath `
        -OutputPath $ordinaryEvolutionOutputPath *> $null

    Write-FixtureFile $ordinaryEvolutionSourcePath @'
namespace Fixture;
internal static class CurrentFallbackPolicy
{
    internal static int Apply() => CurrentFallback();
    private static int CurrentFallback() => 1;
}
internal sealed class CurrentConsumer
{
    internal int Run() => CurrentFallbackPolicy.Apply();
    internal int RunAgain() => CurrentFallbackPolicy.Apply();
}
'@
    & $compatibilityScript `
        -RepositoryRoot $ordinaryEvolutionRoot `
        -InventoryPath $ordinaryEvolutionInventoryPath `
        -BaselinePath $ordinaryEvolutionBaselinePath `
        -OutputPath $ordinaryEvolutionOutputPath *> $null

    Write-FixtureFile $ordinaryEvolutionSourcePath @'
namespace Fixture;
internal static class LegacyMigrationAdapter
{
    internal static int Run() => 1;
}
internal sealed class MigrationConsumer
{
    internal int Execute() => LegacyMigrationAdapter.Run();
}
internal sealed class MigrationCoverageMarker { }
'@
    Write-FixtureFile $ordinaryEvolutionInventoryPath @'
{
  "schemaVersion": 3,
  "csharpSymbols": {
    "producers": {
      "AI-COMPAT-NEW-MIGRATION": "T:Fixture.LegacyMigrationAdapter"
    },
    "callerScans": {
      "AI-COMPAT-NEW-MIGRATION/primary": ["M:Fixture.LegacyMigrationAdapter.Run"]
    },
    "candidateDispositions": {
      "T:Fixture.LegacyMigrationAdapter": "AI-COMPAT-NEW-MIGRATION"
    }
  },
  "items": [
    {
      "id": "AI-COMPAT-NEW-MIGRATION",
      "producer": { "path": "src/core/CurrentFallbackPolicy.cs", "contains": "internal static class LegacyMigrationAdapter" },
      "consumer": { "path": "src/core/CurrentFallbackPolicy.cs", "contains": "internal sealed class MigrationConsumer" },
      "callEvidence": [
        { "path": "src/core/CurrentFallbackPolicy.cs", "contains": "LegacyMigrationAdapter.Run()" }
      ],
      "candidateEvidence": [
        { "path": "src/core/CurrentFallbackPolicy.cs", "contains": "LegacyMigrationAdapter" }
      ],
      "callerScan": {
        "roots": ["src/core"],
        "extensions": [".cs"],
        "contains": ".Run(",
        "excludePaths": []
      },
      "replacement": "Current contract without the migration adapter.",
      "deletionCondition": "Migration has completed.",
      "latestDeletionBatch": "2099-12",
      "deletionDeadline": "2099-12-31",
      "coverageTests": [
        { "path": "src/core/CurrentFallbackPolicy.cs", "contains": "MigrationCoverageMarker" }
      ]
    }
  ],
  "ordinaryAbstractions": []
}
'@
    $validMigrationCompatibilityInventory = Get-Content $ordinaryEvolutionInventoryPath -Raw
    $missingCoverageCompatibilityInventory = $validMigrationCompatibilityInventory |
        ConvertFrom-Json -Depth 64
    $missingCoverageCompatibilityInventory.items[0].coverageTests[0].path =
        'src/core/MissingMigrationCoverage.cs'
    Write-FixtureFile `
        $ordinaryEvolutionInventoryPath `
        ($missingCoverageCompatibilityInventory | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $ordinaryEvolutionRoot `
            -InventoryPath $ordinaryEvolutionInventoryPath `
            -BaselinePath $ordinaryEvolutionBaselinePath `
            -OutputPath $ordinaryEvolutionOutputPath
    } 'coverageTests\[0\] references a missing file'
    Write-FixtureFile $ordinaryEvolutionInventoryPath $validMigrationCompatibilityInventory

    $staleCoverageCompatibilityInventory = $validMigrationCompatibilityInventory |
        ConvertFrom-Json -Depth 64
    $staleCoverageCompatibilityInventory.items[0].coverageTests[0].contains =
        'RetiredMigrationCoverageMarker'
    Write-FixtureFile `
        $ordinaryEvolutionInventoryPath `
        ($staleCoverageCompatibilityInventory | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $ordinaryEvolutionRoot `
            -InventoryPath $ordinaryEvolutionInventoryPath `
            -BaselinePath $ordinaryEvolutionBaselinePath `
            -OutputPath $ordinaryEvolutionOutputPath
    } 'coverageTests\[0\] is stale'
    Write-FixtureFile $ordinaryEvolutionInventoryPath $validMigrationCompatibilityInventory

    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $ordinaryEvolutionRoot `
            -InventoryPath $ordinaryEvolutionInventoryPath `
            -BaselinePath $ordinaryEvolutionBaselinePath `
            -OutputPath $ordinaryEvolutionOutputPath
    } 'Compatibility IDs differ from the comparison baseline'

    $transitionFixtureRoot = Join-Path $tempRoot 'declaration-transition'
    $generatedTransitionPath = Join-Path $transitionFixtureRoot 'generated.json'
    $controlledTransitionPath = Join-Path $RepositoryRoot 'scripts/tests/baselines/aicopilot-test-declaration-transition.json'
    New-Item -ItemType Directory -Path $transitionFixtureRoot -Force | Out-Null
    Copy-Item $controlledTransitionPath $generatedTransitionPath
    & $declarationTransitionChecker `
        -RepositoryRoot $RepositoryRoot `
        -LedgerPath $generatedTransitionPath *> $null

    $missingTransition = Get-Content $generatedTransitionPath -Raw | ConvertFrom-Json -Depth 64
    $missingTransition.transitions = @($missingTransition.transitions | Select-Object -Skip 1)
    Write-FixtureFile $generatedTransitionPath ($missingTransition | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $declarationTransitionChecker `
            -RepositoryRoot $RepositoryRoot `
            -LedgerPath $generatedTransitionPath
    } 'exactly 785 records'

    $unknownReplacement = Get-Content $controlledTransitionPath -Raw | ConvertFrom-Json -Depth 64
    $replacementRecord = @($unknownReplacement.transitions | Where-Object {
            $_.disposition -eq 'replaced' -and
            @($_.replacementIds | Where-Object { -not ([string]$_).StartsWith('ANALYZER:') }).Count -gt 0
        })[0]
    $replacementRecord.replacementIds = @('AICopilot.UnitTests.DoesNotExist.MissingBehavior')
    Write-FixtureFile $generatedTransitionPath ($unknownReplacement | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $declarationTransitionChecker `
            -RepositoryRoot $RepositoryRoot `
            -LedgerPath $generatedTransitionPath
    } 'Declaration transition disposition/replacement/reason content differs from the frozen controlled review'

    $unknownAnalyzer = Get-Content $controlledTransitionPath -Raw | ConvertFrom-Json -Depth 64
    $analyzerRecord = @($unknownAnalyzer.transitions | Where-Object {
            @($_.replacementIds) -contains 'ANALYZER:AIARCH007'
        })[0]
    $analyzerRecord.replacementIds = @('ANALYZER:AIARCH999')
    Write-FixtureFile $generatedTransitionPath ($unknownAnalyzer | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $declarationTransitionChecker `
            -RepositoryRoot $RepositoryRoot `
            -LedgerPath $generatedTransitionPath
    } 'Declaration transition disposition/replacement/reason content differs from the frozen controlled review'

    $knownReplacementTamper = Get-Content $controlledTransitionPath -Raw | ConvertFrom-Json -Depth 64
    $knownReplacementTamper.transitions[0].replacementIds = @('ANALYZER:AIARCH005')
    Write-FixtureFile $generatedTransitionPath ($knownReplacementTamper | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $declarationTransitionChecker `
            -RepositoryRoot $RepositoryRoot `
            -LedgerPath $generatedTransitionPath
    } 'Declaration transition disposition/replacement/reason content differs from the frozen controlled review'

    $reasonTamper = Get-Content $controlledTransitionPath -Raw | ConvertFrom-Json -Depth 64
    $reasonTamper.transitions[0].reason = "$($reasonTamper.transitions[0].reason) Tampered."
    Write-FixtureFile $generatedTransitionPath ($reasonTamper | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $declarationTransitionChecker `
            -RepositoryRoot $RepositoryRoot `
            -LedgerPath $generatedTransitionPath
    } 'Declaration transition disposition/replacement/reason content differs from the frozen controlled review'

    Write-Host 'AICopilot inventory/reconciliation/Simulation/CI/coverage/duplication/mutation/compatibility/declaration-transition behavior tests passed. cases=93; coverageOmissionGuards=16.'
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
