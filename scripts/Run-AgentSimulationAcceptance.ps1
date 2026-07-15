param(
    [string]$Configuration = "Debug",
    [string]$EvidenceRoot = "artifacts/simulation"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

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

function Confirm-SimulationTrx {
    param(
        [Parameter(Mandatory)] [string]$ProjectName,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [int]$ExpectedCount
    )

    if (-not (Test-Path $Path -PathType Leaf)) {
        throw "Simulation TRX is missing for ${ProjectName}: $Path"
    }

    [xml]$trx = Get-Content $Path -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ($null -eq $counters) {
        throw "Simulation TRX counters are missing for ${ProjectName}: $Path"
    }

    $discovered = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $skipped = [int]$counters.notExecuted
    if ($discovered -ne $ExpectedCount -or
        $executed -ne $ExpectedCount -or
        $passed -ne $ExpectedCount -or
        $failed -ne 0 -or
        $skipped -ne 0) {
        throw "${ProjectName} reconciliation failed: expected=$ExpectedCount, discovered=$discovered, executed=$executed, passed=$passed, failed=$failed, skipped=$skipped"
    }

    return [pscustomobject]@{
        projectName = $ProjectName
        discovered = $discovered
        executed = $executed
        passed = $passed
        failed = $failed
        skipped = $skipped
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$evidenceDirectory = if ([System.IO.Path]::IsPathRooted($EvidenceRoot)) {
    [System.IO.Path]::GetFullPath($EvidenceRoot)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceRoot))
}
$resultsDirectory = Join-Path $evidenceDirectory "test-results"
$reportPath = Join-Path $evidenceDirectory "agent-simulation-acceptance.md"
$summaryPath = Join-Path $evidenceDirectory "simulation-test-summary.json"
$Commands = [System.Collections.Generic.List[string]]::new()
$Results = [System.Collections.Generic.List[string]]::new()
$startedAt = Get-Date
$outcome = "Failed"
$failure = $null
$pureResult = $null
$dockerResult = $null

New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
Remove-Item $resultsDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $reportPath, $summaryPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null

Push-Location $repoRoot

try {
    try {
        $dockerPreflightCommand = "docker info --format '{{.OSType}}'"
        $Commands.Add($dockerPreflightCommand) | Out-Null
        Invoke-Step "Linux Docker preflight" {
            $dockerOutputLines = @(& docker info --format '{{.OSType}}' 2>&1)
            $dockerExitCode = $LASTEXITCODE
            $dockerRawOutput = ($dockerOutputLines | ForEach-Object { [string]$_ }) -join "`n"
            Write-Host "Docker preflight raw stdout (exit=$dockerExitCode): $dockerRawOutput"
            if ($dockerExitCode -ne 0) {
                throw "Simulation Docker preflight command failed with exit code $dockerExitCode. RawOutput=$dockerRawOutput"
            }

            $dockerOperatingSystem = $dockerRawOutput.Trim()
            if (-not [string]::Equals(
                $dockerOperatingSystem,
                "linux",
                [StringComparison]::OrdinalIgnoreCase)) {
                throw "Simulation Docker acceptance requires a Linux Docker daemon; detected '$dockerOperatingSystem'. RawOutput=$dockerRawOutput"
            }
        }

        $pureProject = "src/tests/AICopilot.SimulationTests/AICopilot.SimulationTests.csproj"
        $dockerProject = "src/tests/AICopilot.SimulationDockerTests/AICopilot.SimulationDockerTests.csproj"

        $pureBuildCommand = "dotnet build $pureProject -c $Configuration"
        $Commands.Add($pureBuildCommand) | Out-Null
        Invoke-Step "Simulation pure runner build" {
            dotnet build $pureProject -c $Configuration
        }

        $dockerBuildCommand = "dotnet build $dockerProject -c $Configuration"
        $Commands.Add($dockerBuildCommand) | Out-Null
        Invoke-Step "Simulation Docker runner build" {
            dotnet build $dockerProject -c $Configuration
        }

        $pureTrxPath = Join-Path $resultsDirectory "AICopilot.SimulationTests.trx"
        $pureTestCommand = "dotnet test $pureProject -c $Configuration --no-build --no-restore --logger trx;LogFileName=AICopilot.SimulationTests.trx --results-directory $resultsDirectory"
        $Commands.Add($pureTestCommand) | Out-Null
        Invoke-Step "Simulation pure acceptance" {
            dotnet test $pureProject `
                -c $Configuration `
                --no-build `
                --no-restore `
                --logger "trx;LogFileName=AICopilot.SimulationTests.trx" `
                --results-directory $resultsDirectory
        }
        Invoke-Step "Simulation pure reconciliation (12/12)" {
            $script:pureResult = Confirm-SimulationTrx `
                -ProjectName "AICopilot.SimulationTests" `
                -Path $pureTrxPath `
                -ExpectedCount 12
        }

        $dockerTrxPath = Join-Path $resultsDirectory "AICopilot.SimulationDockerTests.trx"
        $dockerTestCommand = "dotnet test $dockerProject -c $Configuration --no-build --no-restore --logger trx;LogFileName=AICopilot.SimulationDockerTests.trx --results-directory $resultsDirectory"
        $Commands.Add($dockerTestCommand) | Out-Null
        Invoke-Step "Simulation Docker acceptance" {
            dotnet test $dockerProject `
                -c $Configuration `
                --no-build `
                --no-restore `
                --logger "trx;LogFileName=AICopilot.SimulationDockerTests.trx" `
                --results-directory $resultsDirectory
        }
        Invoke-Step "Simulation Docker reconciliation (1/1)" {
            $script:dockerResult = Confirm-SimulationTrx `
                -ProjectName "AICopilot.SimulationDockerTests" `
                -Path $dockerTrxPath `
                -ExpectedCount 1
        }

        $outcome = "Passed"
    }
    catch {
        $failure = $_
        $Results.Add("- FAIL: $($_.Exception.Message)") | Out-Null
    }
    finally {
        $endedAt = Get-Date
        $failureSummary = if ($null -eq $failure) {
            $null
        }
        else {
            [pscustomobject]@{
                exceptionType = $failure.Exception.GetType().FullName
                message = $failure.Exception.Message
            }
        }
        $summary = [pscustomobject]@{
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
            outcome = $outcome
            expected = [pscustomobject]@{ pure = 12; docker = 1 }
            pure = $pureResult
            docker = $dockerResult
            failure = $failureSummary
        }
        $summary | ConvertTo-Json -Depth 6 | Set-Content $summaryPath -Encoding UTF8

        $pureDiscovered = if ($null -eq $pureResult) { "n/a" } else { $pureResult.discovered }
        $pureExecuted = if ($null -eq $pureResult) { "n/a" } else { $pureResult.executed }
        $dockerDiscovered = if ($null -eq $dockerResult) { "n/a" } else { $dockerResult.discovered }
        $dockerExecuted = if ($null -eq $dockerResult) { "n/a" } else { $dockerResult.executed }
        $totalFailed = if ($null -eq $pureResult -or $null -eq $dockerResult) {
            "n/a"
        }
        else {
            [int]$pureResult.failed + [int]$dockerResult.failed
        }
        $totalSkipped = if ($null -eq $pureResult -or $null -eq $dockerResult) {
            "n/a"
        }
        else {
            [int]$pureResult.skipped + [int]$dockerResult.skipped
        }

        $report = @"
# A Assistant Agent Runtime Offline Simulation Acceptance Report

- StartedAt: $($startedAt.ToString("O"))
- EndedAt: $($endedAt.ToString("O"))
- Outcome: $outcome
- Scope: AICopilot physical Simulation pure and Docker runners
- Cloud/Edge touched: No
- Frontend touched: No
- Real Cloud access introduced: No
- Shell capability introduced: No
- Arbitrary server path write introduced: No
- Simulation source marker: sourceMode=Simulation, isSimulation=true, sourceLabel=Simulated Cloud read-only data

## Commands

$($Commands | ForEach-Object { "- ``$_``" } | Out-String)

## Results

$($Results | Out-String)

## Reconciliation

- Pure runner expected/discovered/executed: 12/$pureDiscovered/$pureExecuted
- Docker runner expected/discovered/executed: 1/$dockerDiscovered/$dockerExecuted
- Failed/skipped: $totalFailed/$totalSkipped

## Notes

- CloudReadonly defaults remain Disabled in appsettings.
- CloudAiRead remains disabled by default.
- The Docker acceptance test enables only the AICopilot Tool Registry entry for ``query_cloud_data_readonly`` and runs with ``CloudReadonly__Mode=Simulation``.
"@

        Set-Content -LiteralPath $reportPath -Value $report -Encoding UTF8
        Write-Host "Acceptance evidence written to $evidenceDirectory" -ForegroundColor Green
    }

    if ($null -ne $failure) {
        throw $failure
    }
}
finally {
    Pop-Location
}
