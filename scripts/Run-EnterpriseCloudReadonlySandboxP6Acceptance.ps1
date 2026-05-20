[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-cloud-readonly-sandbox-p6-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP5
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-cloud-readonly-sandbox-p6"
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

if (-not $SkipInheritedP5) {
    $results += Invoke-Step -Name "Inherited P5 Acceptance" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlyReadinessP5Acceptance.ps1 `
            -ReportPath .\docs\enterprise-cloud-readonly-readiness-p5-latest.md `
            -SkipFrontend
    }
} else {
    $results += Invoke-Step -Name "Inherited P5 Acceptance Report Check" -Script {
        $p5Report = ".\docs\enterprise-cloud-readonly-readiness-p5-latest.md"
        if (-not (Test-Path $p5Report)) {
            throw "P5 acceptance report is missing: $p5Report"
        }

        $content = Get-Content -LiteralPath $p5Report -Raw
        if ($content -notmatch "Run P5 Focused Backend Tests: PASSED" -or
            $content -notmatch "Run Frontend Integration Contract Tests: PASSED") {
            throw "P5 acceptance report exists but does not show the required inherited checks as passed."
        }

        $p4Report = ".\docs\enterprise-tool-governance-p4-latest.md"
        if (-not (Test-Path $p4Report)) {
            throw "P4 acceptance report is missing: $p4Report"
        }

        $p4Content = Get-Content -LiteralPath $p4Report -Raw
        if ($p4Content -notmatch "Run P4 Focused Backend Tests: PASSED") {
            throw "P4 acceptance report exists but does not show the focused backend checks as passed."
        }

        "Using existing P5 acceptance report: $p5Report"
        "Using existing P4 acceptance report: $p4Report"
    }
}

$results += Invoke-Step -Name "Enterprise CloudReadonly Sandbox P6 Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P6 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseCloudReadonlySandboxP6" `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "backendtests")
}

$results += Invoke-Step -Name "Run Cloud Readiness Contract Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseCloudReadonlyReadinessP5|Suite=FrontendIntegrationContract" `
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

    $results += Invoke-Step -Name "Frontend Sandbox Panel HTTP Smoke" -Script {
        $frontendRoot = Join-Path $repoRoot "src/vues/AICopilot.Web"
        $port = 5182
        $out = Join-Path $env:TEMP "aicopilot-p6-vite-smoke.out.log"
        $err = Join-Path $env:TEMP "aicopilot-p6-vite-smoke.err.log"
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
                    $response = Invoke-WebRequest -Uri "http://127.0.0.1:$port/config" -UseBasicParsing -TimeoutSec 2
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

            "Frontend HTTP smoke passed at http://127.0.0.1:$port/config"
        } finally {
            if ($proc -and -not $proc.HasExited) {
                Stop-Process -Id $proc.Id -Force
            }
        }
    }
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Enterprise CloudReadonly Sandbox P6 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled by default")
$reportLines.Add("- Sandbox Boundary: SandboxSmokeOnly; no Agent Runtime real Cloud read is enabled")
$reportLines.Add("- Test Mode: fake sandbox client and contract fixtures; real Cloud endpoint/token are optional smoke inputs only")
$reportLines.Add("- Build Output: $buildOutputRoot for focused tests; AppHost contract tests use default Debug output so Aspire starts current binaries")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P6 Sandbox Evidence")
$reportLines.Add("")
$reportLines.Add("- Default Gate: CloudReadonly.Mode, CloudReadonly.Real, AllowProductionRead, CloudAiRead, and CloudReadonlySandbox remain disabled by default.")
$reportLines.Add("- Sandbox Config: RealSandboxSmoke uses CloudReadonlySandbox, not CloudReadonly.Mode=Real and not CloudAiRead.Enabled.")
$reportLines.Add("- Contract Endpoints: devices, capacity_summary, device_logs, and pass_station_records are the only smoke allowlist endpoints.")
$reportLines.Add("- Policy Rejection: Recipe, recipe version, write-semantics, unknown, and unsafe POST paths remain BlockedByPolicy.")
$reportLines.Add("- Tool Registry Gate: query_cloud_data_readonly remains disabled, hidden from Planner, and non-executable by Agent after sandbox smoke.")
$reportLines.Add("- Audit Shape: endpoint checks record endpoint code, method, path, status, duration, row count, truncated flag, result hash, and error code without token or payload plaintext.")
$reportLines.Add("- Frontend Smoke: config page exposes readiness and sandbox smoke status without token or full payload display when frontend checks are enabled.")
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
$reportLines.Add("- P6 proves sandbox-only readiness and fake contract behavior; it does not prove production Cloud read access.")
$reportLines.Add("- A real sandbox endpoint/token, if provided later, must still be used only as RealSandboxSmoke and must not enter Agent Runtime.")
$reportLines.Add("- P7 must add a separate controlled Agent Sandbox Trial gate before any real Cloud data can be used in Agent outputs.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise CloudReadonly Sandbox P6 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
