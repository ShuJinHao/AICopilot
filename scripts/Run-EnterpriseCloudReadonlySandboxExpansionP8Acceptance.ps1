[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-cloud-readonly-sandbox-expansion-p8-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP7
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-cloud-readonly-sandbox-expansion-p8"
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

if (-not $SkipInheritedP7) {
    $results += Invoke-Step -Name "Inherited P7 Acceptance" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlySandboxAgentTrialP7Acceptance.ps1 `
            -ReportPath .\docs\enterprise-cloud-readonly-sandbox-agent-trial-p7-latest.md `
            -SkipFrontend `
            -SkipInheritedP6
    }
} else {
    $results += Invoke-Step -Name "Inherited P7 Acceptance Report Check" -Script {
        $p7Report = ".\docs\enterprise-cloud-readonly-sandbox-agent-trial-p7-latest.md"
        if (-not (Test-Path $p7Report)) {
            throw "P7 acceptance report is missing: $p7Report"
        }

        $content = Get-Content -LiteralPath $p7Report -Raw
        if ($content -notmatch "Run P7 Focused Backend Tests: PASSED" -or
            $content -notmatch "Run Cloud Readiness Contract Tests: PASSED") {
            throw "P7 acceptance report exists but does not show the required inherited checks as passed."
        }

        "Using existing P7 acceptance report: $p7Report"
    }
}

$results += Invoke-Step -Name "Enterprise CloudReadonly Sandbox Expansion P8 Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P8 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseCloudReadonlySandboxExpansionP8" `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "backendtests")
}

$results += Invoke-Step -Name "Run P7 And Contract Regression Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseCloudReadonlySandboxAgentTrialP7|Suite=FrontendIntegrationContract" `
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

    $results += Invoke-Step -Name "Frontend Controlled Sandbox Panel HTTP Smoke" -Script {
        $frontendRoot = Join-Path $repoRoot "src/vues/AICopilot.Web"
        $port = 5184
        $out = Join-Path $env:TEMP "aicopilot-p8-vite-smoke.out.log"
        $err = Join-Path $env:TEMP "aicopilot-p8-vite-smoke.err.log"
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
            if ($proc -and -not $proc.HasExited) {
                Stop-Process -Id $proc.Id -Force
            }
        }
    }
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Enterprise CloudReadonly Sandbox Expansion P8 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled by default")
$reportLines.Add("- Controlled Trial Boundary: SandboxControlledTrial; controlled free goals only; no production Cloud read")
$reportLines.Add("- Test Mode: fake sandbox client and contract fixtures; real sandbox endpoint/token remain optional inputs only")
$reportLines.Add("- Build Output: $buildOutputRoot for focused tests")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P8 Controlled Sandbox Evidence")
$reportLines.Add("")
$reportLines.Add("- Default Gate: CloudReadonly.Mode, CloudReadonly.Real, AllowProductionRead, CloudAiRead, and CloudReadonlySandboxControlledTrial remain disabled by default.")
$reportLines.Add("- Tool Boundary: query_cloud_data_readonly remains disabled, hidden, and non-executable; P8 continues using query_cloud_sandbox_readonly.")
$reportLines.Add("- Controlled Intent: free goals are first mapped to CloudSandboxGoalIntent with endpoint allowlist, time range, maxRows, artifact types, and approval flags.")
$reportLines.Add("- Endpoint Allowlist: devices, capacity_summary, device_logs, and pass_station_records are allowed; Recipe, Recipe version, write paths, production paths, and unknown endpoints are BlockedByPolicy.")
$reportLines.Add("- Result Markers: sourceType=CloudReadonly, sourceMode=CloudReadonlySandbox, isSandbox=true, isSimulation=false, sourceLabel=Cloud readonly Sandbox non-production, boundary=SandboxControlledTrial.")
$reportLines.Add("- Audit Shape: endpoint code, intent hash, status, duration, row count, truncated flag, query/result hash, and error code without token or full payload plaintext.")
$reportLines.Add("- Frontend Smoke: Agent workbench and Tool Registry expose controlled trial status and intent preview without token, API Key, connection string, password, or full payload.")
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
$reportLines.Add("- P8 proves controlled free-goal sandbox trial behavior with fake/fixture clients; it does not prove production Cloud read access.")
$reportLines.Add("- A real sandbox endpoint/token, if provided later, must stay under SandboxControlledTrial and must not enable Real CloudReadonly.")
$reportLines.Add("- Production Cloud data, free production queries, Cloud/Edge联动, and old-interface compatibility remain out of scope.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise CloudReadonly Sandbox Expansion P8 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
