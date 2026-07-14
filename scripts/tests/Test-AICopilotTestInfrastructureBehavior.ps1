[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$inventoryScript = Join-Path $RepositoryRoot 'scripts/tests/Get-AICopilotTestInventory.ps1'
$reconciliationScript = Join-Path $RepositoryRoot 'scripts/tests/Confirm-AICopilotRequiredTestResults.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "aicopilot-test-infrastructure-$([Guid]::NewGuid().ToString('N'))"

function Write-FixtureFile {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Content
    )

    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    Set-Content -Path $Path -Value $Content -Encoding utf8
}

function New-InventoryFixture {
    param(
        [string]$SupportPath = 'src/tests/AICopilot.Testing.McpServer/AICopilot.Testing.McpServer.csproj',
        [string]$SupportIsTestProject = 'false',
        [string]$SupportPackageReference = '',
        [string]$SupportSource = 'internal static class SupportProgram { }'
    )

    $root = Join-Path $tempRoot ([Guid]::NewGuid().ToString('N'))
    $runnerPath = 'src/tests/AICopilot.UnitTests/AICopilot.UnitTests.csproj'
    $runnerProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <AICopilotTestKind>Unit</AICopilotTestKind>
    <AICopilotTestRuntime>Pure</AICopilotTestRuntime>
    <AICopilotTestCadence>PR</AICopilotTestCadence>
    <AICopilotTestOwner>Fixture</AICopilotTestOwner>
    <AICopilotRequired>true</AICopilotRequired>
  </PropertyGroup>
</Project>
'@
    $supportPackages = if ([string]::IsNullOrWhiteSpace($SupportPackageReference)) {
        ''
    }
    else {
        "  <ItemGroup>`n    $SupportPackageReference`n  </ItemGroup>"
    }
    $supportProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>$SupportIsTestProject</IsTestProject>
    <AICopilotTestRole>Support</AICopilotTestRole>
  </PropertyGroup>
$supportPackages
</Project>
"@
    $solution = @"
<Solution>
  <Folder Name="/tests/">
    <Project Path="$runnerPath" />
    <Project Path="$SupportPath" />
  </Folder>
</Solution>
"@

    Write-FixtureFile (Join-Path $root 'AICopilot.slnx') $solution
    Write-FixtureFile (Join-Path $root $runnerPath) $runnerProject
    Write-FixtureFile (Join-Path $root $SupportPath) $supportProject
    Write-FixtureFile (Join-Path (Split-Path (Join-Path $root $SupportPath) -Parent) 'Program.cs') $SupportSource
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
        [Parameter(Mandatory)] [int]$Passed
    )

    $inventory = [pscustomobject]@{
        projects = @(
            [pscustomobject]@{
                projectName = 'RequiredRunner'
                role = 'Runner'
                required = $true
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

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    $validSupportRoot = New-InventoryFixture
    & $inventoryScript `
        -RepositoryRoot $validSupportRoot `
        -OutputPath 'artifacts/test-inventory.json' *> $null

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
        -SupportPackageReference '<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $testSdkSupportRoot -OutputPath 'artifacts/test-inventory.json'
    } 'must not reference test SDK/framework packages'

    $factSupportRoot = New-InventoryFixture `
        -SupportSource 'internal sealed class HiddenTests { [Fact] public void Escaped() { } }'
    Assert-Fails {
        & $inventoryScript -RepositoryRoot $factSupportRoot -OutputPath 'artifacts/test-inventory.json'
    } 'must not declare Fact/Theory tests'

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
    & $reconciliationScript `
        -RepositoryRoot $reconciliationRoot `
        -InventoryPath 'artifacts/test-inventory.json' `
        -ResultsDirectory 'artifacts/test-results' `
        -OutputPath 'artifacts/test-results/summary.json' *> $null
    $summary = Get-Content (Join-Path $reconciliationRoot 'artifacts/test-results/summary.json') -Raw |
        ConvertFrom-Json
    if ([int]$summary.dotnet.discovered -ne 1 -or [int]$summary.dotnet.executed -ne 1) {
        throw 'A non-empty required runner did not reconcile to discovered=executed=1.'
    }

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
            -Configuration Release *> $null
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

    Write-Host 'AICopilot inventory/reconciliation/Simulation behavior tests passed. cases=11.'
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
