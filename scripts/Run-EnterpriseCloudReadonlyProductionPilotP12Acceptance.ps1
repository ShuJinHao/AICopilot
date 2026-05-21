[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-cloud-readonly-production-pilot-p12-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP11
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-cloud-readonly-production-pilot-p12"
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

if (-not $SkipInheritedP11) {
    $results += Invoke-Step -Name "Inherited P11 Acceptance" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlyPilotReadinessP11Acceptance.ps1 `
            -ReportPath .\docs\enterprise-cloud-readonly-pilot-readiness-p11-latest.md `
            -SkipFrontend `
            -SkipInheritedP10
    }
} else {
    $results += Invoke-Step -Name "Inherited P11 Acceptance Report Check" -Script {
        $p11Report = ".\docs\enterprise-cloud-readonly-pilot-readiness-p11-latest.md"
        if (-not (Test-Path $p11Report)) {
            throw "P11 acceptance report is missing: $p11Report"
        }

        $content = Get-Content -LiteralPath $p11Report -Raw
        if ($content -notmatch "Run P11 Focused Backend Tests: PASSED" -and
            $content -notmatch "Enterprise CloudReadonly Pilot Readiness P11 Scope Guard: PASSED") {
            throw "P11 acceptance report exists but does not show required inherited checks as passed."
        }

        "Using existing P11 acceptance report: $p11Report"
    }
}

$results += Invoke-Step -Name "Enterprise CloudReadonly Production Pilot P12 Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P12 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseCloudReadonlyProductionPilotP12" `
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

    $results += Invoke-Step -Name "Frontend Production Pilot Playwright Smoke" -Script {
        Push-Location src/vues/AICopilot.Web
        try {
            npm run test:smoke -- --grep "P12 production readonly pilot"
        } finally {
            Pop-Location
        }
    }
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Enterprise CloudReadonly Production Pilot P12 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; production Pilot remains fixed-template, windowed, and approval-gated")
$reportLines.Add("- Default State: CloudReadonlyProductionPilot.Enabled=false; query_cloud_data_readonly remains disabled, hidden, and non-executable")
$reportLines.Add("- Build Output: $buildOutputRoot for focused tests")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P12 Production Pilot Evidence")
$reportLines.Add("")
$reportLines.Add("- Pilot Window: start/end, owner, approver policy, rollback policy, max time range, max rows, timeout, and endpoint allowlist are represented.")
$reportLines.Add("- Gate: P11 RehearsalPassed, default production flags, protected ToolRegistry state, CloudAiRead configuration, and approved window state determine Ready/Blocked.")
$reportLines.Add("- Allowed Endpoints: devices, capacity_summary, device_logs, pass_station_records only.")
$reportLines.Add("- Refusals: Recipe, Recipe version, write path, unknown endpoint, out-of-window endpoint, missing P11 gate, expired/paused window, over maxRows, and over time range are blocked.")
$reportLines.Add("- Tool Registry: query_cloud_data_readonly stays closed; query_cloud_production_pilot_readonly is disabled by default and only temporarily usable through the P12 Pilot Window gate.")
$reportLines.Add("- Outputs: sourceMode=CloudReadonlyProductionPilot, sourceLabel=Cloud production readonly Pilot, boundary=ProductionPilot, pilotWindowId, endpointCode, query/result hash, row count, truncation, and approval status are required.")
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
$reportLines.Add("- P12 does not open free-goal production queries, Recipe/version reads, write paths, or Cloud/Edge linkage.")
$reportLines.Add("- Real endpoint/token smoke remains optional and must be run only inside an explicit Pilot Window with approvals.")
$reportLines.Add("- Production Pilot status is an initial fixed-template gate, not a broad production rollout.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise CloudReadonly Production Pilot P12 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
