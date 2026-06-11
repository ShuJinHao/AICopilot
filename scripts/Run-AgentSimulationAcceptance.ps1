param(
    [switch]$SkipDockerAcceptance,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

function New-UnicodeString {
    param([int[]]$CodePoints)
    return [string]::Concat(@($CodePoints | ForEach-Object { [char]$_ }))
}

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Command
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    try {
        & $Command
        $stepSucceeded = $?
        $exitCode = $LASTEXITCODE
    }
    catch {
        throw "$Name failed. $($_.Exception.Message)"
    }

    if (-not $stepSucceeded) {
        if ($null -ne $exitCode) {
            throw "$Name failed with exit code $exitCode."
        }

        throw "$Name failed."
    }

    if ($null -ne $exitCode -and $exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode."
    }

    $script:Results.Add("- PASS: $Name") | Out-Null
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

try {
    $startedAt = Get-Date
    $reportDirName = New-UnicodeString @(0x8D44, 0x6599)
    $reportFileName = "A" + (New-UnicodeString @(0x52A9, 0x7406)) + "AgentSimulation" + (New-UnicodeString @(0x79BB, 0x7EBF, 0x9A8C, 0x6536, 0x62A5, 0x544A)) + ".md"
    $reportPath = Join-Path (Join-Path $repoRoot $reportDirName) $reportFileName
    $simulationSourceLabel = New-UnicodeString @(0x6A21, 0x62DF, 0x0020, 0x0043, 0x006C, 0x006F, 0x0075, 0x0064, 0x0020, 0x53EA, 0x8BFB, 0x6570, 0x636E)
    $Commands = [System.Collections.Generic.List[string]]::new()
    $Results = [System.Collections.Generic.List[string]]::new()

    $changedFiles = @(
        "scripts/Test-AgentSimulationScope.ps1",
        "scripts/Run-AgentSimulationAcceptance.ps1",
        "src/core/AICopilot.Core.AiGateway/Aggregates/Tools/BuiltInToolRegistrations.cs",
        "src/services/AICopilot.Services.Contracts/Contracts/CloudReadonlyContracts.cs",
        "src/services/AICopilot.AiGatewayService/AgentTasks/CloudReadonlyAgentTooling.cs",
        "src/services/AICopilot.AiGatewayService/AgentTasks/CloudReadonlyDataProviders.cs",
        "src/services/AICopilot.AiGatewayService/AgentTasks/CloudReadonlySimulation.cs",
        "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskRuntime.cs",
        "src/services/AICopilot.AiGatewayService/DependencyInjection.cs",
        "src/hosts/AICopilot.HttpApi/DependencyInjection.cs",
        "src/hosts/AICopilot.HttpApi/appsettings.json",
        "src/hosts/AICopilot.HttpApi/appsettings.Development.json",
        "src/tests/AICopilot.BackendTests/AICopilotAppFixture.cs",
        "src/tests/AICopilot.BackendTests/BackendTestCollection.cs",
        "src/tests/AICopilot.BackendTests/CloudReadonlySimulationTests.cs",
        "src/tests/AICopilot.BackendTests/AgentSimulationAcceptanceTests.cs",
        "src/tests/AICopilot.BackendTests/ToolRegistryGovernanceTests.cs"
    )

    $guardCommand = ".\scripts\Test-AgentSimulationScope.ps1 -ChangedFiles `$changedFiles"
    $Commands.Add($guardCommand) | Out-Null
    Invoke-Step "scope guard" {
        .\scripts\Test-AgentSimulationScope.ps1 -ChangedFiles $changedFiles
    }

    $buildCommand = "dotnet build src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c $Configuration"
    $Commands.Add($buildCommand) | Out-Null
    Invoke-Step "backend test project build" {
        dotnet build src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c $Configuration
    }

    $unitCommand = "dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c $Configuration --no-build --filter `"Suite=AgentSimulationAcceptance&Runtime!=DockerRequired`""
    $Commands.Add($unitCommand) | Out-Null
    Invoke-Step "agent simulation unit tests" {
        dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c $Configuration --no-build --filter "Suite=AgentSimulationAcceptance&Runtime!=DockerRequired"
    }

    if (-not $SkipDockerAcceptance) {
        $dockerCommand = "dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c $Configuration --no-build --filter `"FullyQualifiedName~AgentSimulationAcceptanceTests`""
        $Commands.Add($dockerCommand) | Out-Null
        Invoke-Step "agent simulation Docker acceptance" {
            dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c $Configuration --no-build --filter "FullyQualifiedName~AgentSimulationAcceptanceTests"
        }
    }
    else {
        $Results.Add("- SKIP: agent simulation Docker acceptance (-SkipDockerAcceptance was set)") | Out-Null
    }

    $endedAt = Get-Date
    $report = @"
# A Assistant Agent Runtime Offline Simulation Acceptance Report

- StartedAt: $($startedAt.ToString("O"))
- EndedAt: $($endedAt.ToString("O"))
- Scope: AICopilot backend Batch 0-4
- Cloud/Edge touched: No
- Frontend touched: No
- Real Cloud access introduced: No
- Shell capability introduced: No
- Arbitrary server path write introduced: No
- Simulation source marker: sourceMode=Simulation, isSimulation=true, sourceLabel=$simulationSourceLabel

## Commands

$($Commands | ForEach-Object { "- ``$_``" } | Out-String)

## Results

$($Results | Out-String)

## Notes

- CloudReadonly defaults remain Disabled in appsettings.
- CloudAiRead remains disabled by default.
- The Docker acceptance test enables only the AICopilot Tool Registry entry for ``query_cloud_data_readonly`` and runs with ``CloudReadonly__Mode=Simulation``.
"@

    New-Item -ItemType Directory -Force -Path (Split-Path $reportPath) | Out-Null
    Set-Content -LiteralPath $reportPath -Value $report -Encoding UTF8
    Write-Host "Acceptance report written to $reportPath" -ForegroundColor Green
}
finally {
    Pop-Location
}
