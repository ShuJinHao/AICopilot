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
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "aicopilot-test-infrastructure-$([Guid]::NewGuid().ToString('N'))"

function Write-FixtureFile {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Content
    )

    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    Set-Content -Path $Path -Value $Content -Encoding utf8
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
        [string]$RunnerRequired = 'true',
        [string]$RunnerProfile = 'Default',
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
    <AICopilotTestOwner>Fixture</AICopilotTestOwner>
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
  "schemaVersion": 1,
  "projects": [
    {
      "projectName": "$RunnerProjectName",
      "defaults": {
        "capability": "Platform",
        "concern": "Functional",
        "profile": "$RunnerProfile",
        "risk": "P1",
        "ruleId": "AI-UNIT-001",
        "regressionId": "AI-REG-UNIT-FIXTURE",
        "runtimeDependencies": []
      }
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
        [int]$VitestCount = 185,
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
    Write-WebAndDeploymentReconciliationFixture -Root $reconciliationRoot -VitestCount 184
    Assert-Fails {
        & $reconciliationScript `
            -RepositoryRoot $reconciliationRoot `
            -InventoryPath 'artifacts/test-inventory.json' `
            -ResultsDirectory 'artifacts/test-results' `
            -VitestPath 'artifacts/test-results/vitest.json' `
            -PlaywrightPath 'artifacts/test-results/playwright.json' `
            -DeploymentPath 'artifacts/test-results/deployment-behavior.log'
    } 'Vitest reconciliation failed: expected=185, discovered=184'

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
    if ($requiredWorkflow -match '(?m)^\s{2}mutation-report:\s*$' -or
        $requiredWorkflow -match '(?m)^\s*continue-on-error:\s*true\s*$') {
        throw 'Required mutation CI must be a blocking mutation-gate job without continue-on-error.'
    }
    foreach ($requiredCiFragment in @(
        "_.runtime -eq 'Pure'",
        'ForEach-Object -Parallel',
        "_.runtime -ne 'Pure'",
        'Test-AICopilotTestDeclarationTransition.ps1',
        'Test-AICopilotCompatibilityInventory.ps1',
        'Measure-AICopilotDuplication.ps1',
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
    $duplicationBaselinePath = Join-Path $duplicationFixtureRoot 'baseline.json'
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
    } 'Duplication baseline bootstrap is forbidden'
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
    & $duplicationScript `
        -RepositoryRoot $duplicationFixtureRoot `
        -BaselinePath $duplicationBaselinePath `
        -OutputPath $duplicationReportPath *> $null

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
    $mutationBaselinePath = Join-Path $mutationFixtureRoot 'mutation-baseline.json'
    $mutationSummaryPath = Join-Path $mutationFixtureRoot 'mutation-summary.json'
    $mutationTargetPath = Join-Path $RepositoryRoot 'src/infrastructure/AICopilot.SecretProtection/SecretStringEncryptor.cs'
    $mutationReport = [ordered]@{
        schemaVersion = '2'
        files = [ordered]@{
            $mutationTargetPath = [ordered]@{
                mutants = @(
                    [ordered]@{ status = 'Killed' },
                    [ordered]@{ status = 'Survived' }
                )
            }
        }
    }
    $mutationBaseline = [ordered]@{
        schemaVersion = 1
        toolPackage = 'dotnet-stryker'
        toolVersion = '4.16.0'
        project = 'AICopilot.SecretProtection.csproj'
        targetFile = 'src/infrastructure/AICopilot.SecretProtection/SecretStringEncryptor.cs'
        mutationLevel = 'Standard'
        expectedGeneratedMutants = 2
        expectedEvaluatedMutants = 2
        minimumMutationScore = 50
        maximumSurvived = 1
        maximumNoCoverage = 0
        maximumTimeout = 0
        maximumRuntimeError = 0
    }
    Write-FixtureFile $mutationReportPath ($mutationReport | ConvertTo-Json -Depth 16)
    Write-FixtureFile $mutationBaselinePath ($mutationBaseline | ConvertTo-Json -Depth 16)
    & $mutationScript `
        -RepositoryRoot $RepositoryRoot `
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
            -RepositoryRoot $RepositoryRoot `
            -ReportPath $mutationReportPath `
            -BaselinePath $mutationBaselinePath `
            -OutputPath $mutationSummaryPath
    } 'contains no mutants for fixed target'

    $unexpectedTargetPath = Join-Path $RepositoryRoot 'src/infrastructure/AICopilot.SecretProtection/SecretMigrationPolicy.cs'
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
            -RepositoryRoot $RepositoryRoot `
            -ReportPath $mutationReportPath `
            -BaselinePath $mutationBaselinePath `
            -OutputPath $mutationSummaryPath
    } 'Mutation target drift detected'

    Write-FixtureFile $mutationReportPath ($mutationReport | ConvertTo-Json -Depth 16)
    $mutationBaseline.minimumMutationScore = 51
    Write-FixtureFile $mutationBaselinePath ($mutationBaseline | ConvertTo-Json -Depth 16)
    Assert-Fails {
        & $mutationScript `
            -RepositoryRoot $RepositoryRoot `
            -ReportPath $mutationReportPath `
            -BaselinePath $mutationBaselinePath `
            -OutputPath $mutationSummaryPath
    } 'Mutation quality regressed'

    $compatibilityFixtureRoot = Join-Path $tempRoot 'compatibility'
    $compatibilityInventorySource = Join-Path $RepositoryRoot 'scripts/tests/aicopilot-compatibility-inventory.json'
    $compatibilityBaselineSource = Join-Path $RepositoryRoot 'scripts/tests/baselines/aicopilot-compatibility.json'
    $compatibilityInventoryPath = Join-Path $compatibilityFixtureRoot 'inventory.json'
    $compatibilityBaselinePath = Join-Path $compatibilityFixtureRoot 'baseline.json'
    $compatibilityOutputPath = Join-Path $compatibilityFixtureRoot 'output.json'

    $unclassifiedInventory = Get-Content $compatibilityInventorySource -Raw | ConvertFrom-Json -Depth 64
    $unclassifiedInventory.ordinaryAbstractions = @(
        $unclassifiedInventory.ordinaryAbstractions |
            Where-Object { $_.id -ne 'AI-ORDINARY-VISUALIZATION-ADAPTER' }
    )
    Write-FixtureFile $compatibilityInventoryPath ($unclassifiedInventory | ConvertTo-Json -Depth 64)
    Write-FixtureFile $compatibilityBaselinePath (Get-Content $compatibilityBaselineSource -Raw)
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $RepositoryRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } 'Compatibility signal must have exactly one disposition'

    $zeroConsumerInventory = Get-Content $compatibilityInventorySource -Raw | ConvertFrom-Json -Depth 64
    $visualizationItem = @($zeroConsumerInventory.ordinaryAbstractions | Where-Object {
            $_.id -eq 'AI-ORDINARY-VISUALIZATION-ADAPTER'
        })[0]
    $visualizationItem.callerScans[0].contains = '.NoSuchVisualizationCaller('
    Write-FixtureFile $compatibilityInventoryPath ($zeroConsumerInventory | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $RepositoryRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } 'has no production call sites; physically delete the abstraction'

    $fakeCallerVariants = [ordered]@{
        comment = @'
namespace Fixture;
internal sealed class LegacyAdapterShim { }
internal sealed class ConsumerMarker { }
// Runtime.GhostCompatibilityCall();
'@
        string = @'
namespace Fixture;
internal sealed class LegacyAdapterShim { }
internal sealed class ConsumerMarker
{
    private const string Example = "Runtime.GhostCompatibilityCall()";
}
'@
        declaration = @'
namespace Fixture;
internal sealed class LegacyAdapterShim
{
    internal static void GhostCompatibilityCall() { }
}
internal sealed class ConsumerMarker { }
'@
    }
    foreach ($fakeCallerVariant in $fakeCallerVariants.GetEnumerator()) {
        $fakeCallerRoot = Join-Path $tempRoot "compatibility-fake-caller-$($fakeCallerVariant.Key)"
        $fakeCallerSourcePath = Join-Path $fakeCallerRoot 'src/core/LegacyAdapterShim.cs'
        $fakeCallerInventoryPath = Join-Path $fakeCallerRoot 'inventory.json'
        $fakeCallerBaselinePath = Join-Path $fakeCallerRoot 'baseline.json'
        $fakeCallerOutputPath = Join-Path $fakeCallerRoot 'output.json'
        Write-FixtureFile $fakeCallerSourcePath ([string]$fakeCallerVariant.Value)
        Write-FixtureFile $fakeCallerInventoryPath @'
{
  "schemaVersion": 2,
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
          "contains": ".GhostCompatibilityCall(",
          "excludePaths": []
        }
      ],
      "replacement": "No compatibility wrapper.",
      "deletionCondition": "No real production invocation exists."
    }
  ]
}
'@
        Write-FixtureFile $fakeCallerBaselinePath @'
{
  "schemaVersion": 2,
  "candidateSignalCount": 1,
  "unclassifiedCompatibilitySignals": 0,
  "compatibilityItems": [],
  "ordinaryAbstractions": [
    {
      "id": "AI-ORDINARY-FAKE-CALLER",
      "callerScans": [
        { "id": "ghost-call", "maximumCallSites": 0 }
      ]
    }
  ]
}
'@
        Assert-Fails {
            & $compatibilityScript `
                -RepositoryRoot $fakeCallerRoot `
                -InventoryPath $fakeCallerInventoryPath `
                -BaselinePath $fakeCallerBaselinePath `
                -OutputPath $fakeCallerOutputPath
        } 'has no production call sites; physically delete the abstraction'
    }

    Write-FixtureFile $compatibilityInventoryPath (Get-Content $compatibilityInventorySource -Raw)
    $growthBaseline = Get-Content $compatibilityBaselineSource -Raw | ConvertFrom-Json -Depth 64
    $growthItem = @($growthBaseline.ordinaryAbstractions | Where-Object {
            $_.id -eq 'AI-ORDINARY-VISUALIZATION-ADAPTER'
        })[0]
    $growthScan = @($growthItem.callerScans | Where-Object { $_.id -eq 'chart-dataset' })[0]
    $growthScan.maximumCallSites = 0
    Write-FixtureFile $compatibilityBaselinePath ($growthBaseline | ConvertTo-Json -Depth 64)
    Assert-Fails {
        & $compatibilityScript `
            -RepositoryRoot $RepositoryRoot `
            -InventoryPath $compatibilityInventoryPath `
            -BaselinePath $compatibilityBaselinePath `
            -OutputPath $compatibilityOutputPath
    } 'Ordinary abstraction call sites grew'

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

    Write-Host 'AICopilot inventory/reconciliation/Simulation/CI/coverage/duplication/mutation/compatibility/declaration-transition behavior tests passed. cases=62; coverageOmissionGuards=15.'
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
