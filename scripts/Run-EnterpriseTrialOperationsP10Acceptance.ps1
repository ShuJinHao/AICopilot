[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-trial-operations-p10-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP9
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-trial-operations-p10"
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

if (-not $SkipInheritedP9) {
    $results += Invoke-Step -Name "Inherited P9 Acceptance" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseArtifactWorkspaceP9Acceptance.ps1 `
            -ReportPath .\docs\enterprise-artifact-workspace-p9-latest.md `
            -SkipFrontend `
            -SkipInheritedP8
    }
} else {
    $results += Invoke-Step -Name "Inherited P9 Acceptance Report Check" -Script {
        $p9Report = ".\docs\enterprise-artifact-workspace-p9-latest.md"
        if (-not (Test-Path $p9Report)) {
            throw "P9 acceptance report is missing: $p9Report"
        }

        $content = Get-Content -LiteralPath $p9Report -Raw
        if ($content -notmatch "Run P9 Focused Backend Tests: PASSED" -and
            $content -notmatch "Enterprise Artifact Workspace P9 Scope Guard: PASSED") {
            throw "P9 acceptance report exists but does not show the required inherited checks as passed."
        }

        "Using existing P9 acceptance report: $p9Report"
    }
}

$results += Invoke-Step -Name "Enterprise Trial Operations P10 Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P10 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseTrialOperationsP10" `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "backendtests")
}

$results += Invoke-Step -Name "Run Migration Ownership Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=MigrationOwnership" `
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

    $results += Invoke-Step -Name "Frontend Trial Operations HTTP Smoke" -Script {
        $frontendRoot = Join-Path $repoRoot "src/vues/AICopilot.Web"
        $port = 5190
        $out = Join-Path $env:TEMP "aicopilot-p10-vite-smoke.out.log"
        $err = Join-Path $env:TEMP "aicopilot-p10-vite-smoke.err.log"
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
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Enterprise Trial Operations P10 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly and production tools remain disabled")
$reportLines.Add("- Trial Sources: SimulationBusiness and CloudReadonlySandbox only")
$reportLines.Add("- P11 Gate Meaning: ReadyForP11Planning allows planning only; it does not enable production data")
$reportLines.Add("- Build Output: $buildOutputRoot for focused tests")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P10 Trial Operations Evidence")
$reportLines.Add("")
$reportLines.Add("- Campaign: Draft/Active/Paused/Completed/Archived campaign status and allowed source boundaries are persisted.")
$reportLines.Add("- Scenario Runs: Agent tasks can be attached only by reference with task id, artifact ids, source mode, boundary, query/result hashes, and approval status.")
$reportLines.Add("- Risk Register: Open/Mitigating/Resolved/ClosedAsOutOfScope risks are recorded by severity/category/source reference/resolution hash.")
$reportLines.Add("- Pilot Readiness: P9 final lock, sandbox/source boundaries, approval closure, Tool Registry production-tool closure, scope guard, and unresolved high/critical risks gate P11 planning.")
$reportLines.Add("- Evidence Package: output contains metrics, references, source modes, boundary and hash samples only; no token, API Key, connection string, full SQL, or full sandbox payload.")
$reportLines.Add("- Frontend: Agent workbench shows trial campaign, source boundaries, attached run records, readiness checks, and evidence metrics from backend responses.")
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
$reportLines.Add("- P10 does not enable Real CloudReadonly, production Agent queries, production tools, or Cloud/Edge linkage.")
$reportLines.Add("- ReadyForP11Planning is an evidence gate for planning P11 only; production Pilot still requires a separate explicit plan and authorization.")
$reportLines.Add("- P10 is not a full multi-user trial management suite; it is the minimum internal trial ledger, risk register, readiness gate, and sanitized evidence package.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Trial Operations P10 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
