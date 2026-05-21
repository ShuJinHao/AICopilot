[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-cloud-readonly-pilot-readiness-p11-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP10
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-cloud-readonly-pilot-readiness-p11"
if (Test-Path $buildOutputRoot) {
    Remove-Item -LiteralPath $buildOutputRoot -Recurse -Force
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Script
    )

    Write-Host "==> $Name"
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $global:LASTEXITCODE = 0
        $output = & $Script 2>&1 | Out-String
        $succeeded = $LASTEXITCODE -eq 0
    } catch {
        $output = $_ | Out-String
        $succeeded = $false
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    [pscustomobject]@{
        Name      = $Name
        Succeeded = $succeeded
        Output    = $output.Trim()
    }
}

$results = @()

if (-not $SkipInheritedP10) {
    $results += Invoke-Step -Name "Inherited P10 Acceptance" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseTrialOperationsP10Acceptance.ps1 `
            -ReportPath .\docs\enterprise-trial-operations-p10-latest.md `
            -SkipFrontend `
            -SkipInheritedP9
    }
} else {
    $results += Invoke-Step -Name "Inherited P10 Acceptance Report Check" -Script {
        $p10Report = ".\docs\enterprise-trial-operations-p10-latest.md"
        if (-not (Test-Path $p10Report)) {
            throw "P10 acceptance report is missing: $p10Report"
        }

        $content = Get-Content -LiteralPath $p10Report -Raw
        if ($content -notmatch "Run P10 Focused Backend Tests: PASSED" -and
            $content -notmatch "Enterprise Trial Operations P10 Scope Guard: PASSED") {
            throw "P10 acceptance report exists but does not show the required inherited checks as passed."
        }

        "Using existing P10 acceptance report: $p10Report"
    }
}

$results += Invoke-Step -Name "Enterprise CloudReadonly Pilot Readiness P11 Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P11 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseCloudReadonlyPilotReadinessP11" `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "backendtests")
}

$results += Invoke-Step -Name "Run CloudReadonly Route Contract Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "FullyQualifiedName~CloudReadonlyReadinessRoutes_ShouldBeRoutable" `
        /m:1 /p:UseSharedCompilation=false
}

if (-not $SkipFrontend) {
    $results += Invoke-Step -Name "Build Frontend" -Script {
        Push-Location src/vues/AICopilot.Web
        try {
            npm run build
        } finally {
            Pop-Location
        }
    }

    $results += Invoke-Step -Name "Frontend Pilot Readiness HTTP Smoke" -Script {
        $frontendRoot = Join-Path $repoRoot "src/vues/AICopilot.Web"
        $port = 5191
        $out = Join-Path $env:TEMP "aicopilot-p11-vite-smoke.out.log"
        $err = Join-Path $env:TEMP "aicopilot-p11-vite-smoke.err.log"
        $proc = Start-Process -FilePath "npm.cmd" `
            -ArgumentList @("run", "dev", "--", "--host", "127.0.0.1", "--port", "$port", "--strictPort") `
            -WorkingDirectory $frontendRoot `
            -WindowStyle Hidden `
            -RedirectStandardOutput $out `
            -RedirectStandardError $err `
            -PassThru
        try {
            $ready = $false
            for ($i = 0; $i -lt 40; $i++) {
                try {
                    $response = Invoke-WebRequest -Uri "http://127.0.0.1:$port/chat" -UseBasicParsing -TimeoutSec 2
                    if ($response.StatusCode -eq 200 -and $response.Content -match 'id="app"') {
                        $ready = $true
                        break
                    }
                } catch {
                    Start-Sleep -Milliseconds 500
                }
            }

            if (-not $ready) {
                throw "Frontend HTTP smoke did not receive the Vite app shell."
            }

            "Frontend HTTP smoke passed at http://127.0.0.1:$port/chat"
        } finally {
            if ($proc) {
                $processIds = New-Object System.Collections.Generic.List[int]
                $pending = New-Object System.Collections.Generic.Queue[int]
                $pending.Enqueue($proc.Id)
                while ($pending.Count -gt 0) {
                    $parentId = $pending.Dequeue()
                    $processIds.Add($parentId)
                    Get-CimInstance Win32_Process -Filter "ParentProcessId = $parentId" -ErrorAction SilentlyContinue |
                        ForEach-Object { $pending.Enqueue([int]$_.ProcessId) }
                }

                foreach ($processId in ($processIds | Select-Object -Unique | Sort-Object -Descending)) {
                    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }

    $results += Invoke-Step -Name "Frontend Pilot Readiness Playwright Smoke" -Script {
        Push-Location src/vues/AICopilot.Web
        try {
            npm run test:smoke -- --grep "P11 pilot readiness"
        } finally {
            Pop-Location
        }
    }
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Enterprise CloudReadonly Pilot Readiness P11 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly and production tools remain disabled")
$reportLines.Add("- P11 Meaning: Pilot readiness rehearsal only; no real production Cloud data is read")
$reportLines.Add("- Build Output: $buildOutputRoot for focused tests")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P11 Pilot Readiness Evidence")
$reportLines.Add("")
$reportLines.Add("- Config Package: allowed endpoints, max rows, timeout, approval policy, rollback policy, owner department, and P10 evidence refs are represented without storing full SQL or payload.")
$reportLines.Add("- Gate: P10 ReadyForP11Planning, production tool closure, production read flags, approval rehearsal, and fake contract checks drive NotConfigured/CollectingEvidence/RehearsalReady/RehearsalPassed/Blocked/Failed.")
$reportLines.Add("- Contract Rehearsal: devices, capacity_summary, device_logs, and pass_station_records use fake production-like contracts with sourceMode=CloudReadonlyPilotReadiness and boundary=PilotReadinessRehearsal.")
$reportLines.Add("- Refusals: Recipe, Recipe version, write path, unknown endpoint, out-of-allowlist endpoint, and production-read flags are blocked by policy.")
$reportLines.Add("- Tool Registry: query_cloud_data_readonly and query_cloud_pilot_readiness_readonly remain disabled, hidden, and non-executable.")
$reportLines.Add("- Frontend: Playwright smoke covers the Agent trial panel P11 status, config package, approval rehearsal, fake contract checks, and explicit no-production-read markers.")
$reportLines.Add("")
$reportLines.Add("## Details")
$reportLines.Add("")

foreach ($result in $results) {
    $reportLines.Add("### $($result.Name)")
    $reportLines.Add("")
    $reportLines.Add('```text')
    foreach ($line in ($result.Output -split "`r?`n")) {
        $reportLines.Add($line.TrimEnd())
    }
    $reportLines.Add('```')
    $reportLines.Add("")
}

$reportLines.Add("## Remaining Risk")
$reportLines.Add("")
$reportLines.Add("- P11 does not enable Real CloudReadonly, production Agent queries, production tools, or Cloud/Edge linkage.")
$reportLines.Add("- P11 readiness evidence is not production Pilot authorization; a later P12 or standalone authorization plan must define endpoint, token, approvers, scope, rollback, and trial window.")
$reportLines.Add("- Fake contract rehearsal proves interface and governance behavior only; it does not prove real Cloud production endpoint availability.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise CloudReadonly Pilot Readiness P11 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
