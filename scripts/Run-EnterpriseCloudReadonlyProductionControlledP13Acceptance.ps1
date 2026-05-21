[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-cloud-readonly-production-controlled-p13-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP12
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-cloud-readonly-production-controlled-p13"
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

if (-not $SkipInheritedP12) {
    $results += Invoke-Step -Name "Inherited P12 Acceptance" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlyProductionPilotP12Acceptance.ps1 `
            -ReportPath (Join-Path $buildOutputRoot "enterprise-cloud-readonly-production-pilot-p12-inherited.md") `
            -SkipFrontend `
            -SkipInheritedP11
    }
} else {
    $results += Invoke-Step -Name "Inherited P12 Acceptance Report Check" -Script {
        $p12Report = ".\docs\enterprise-cloud-readonly-production-pilot-p12-latest.md"
        if (-not (Test-Path $p12Report)) {
            throw "P12 acceptance report is missing: $p12Report"
        }

        $content = Get-Content -LiteralPath $p12Report -Raw
        if ($content -notmatch "Run P12 Focused Backend Tests: PASSED" -and
            $content -notmatch "Enterprise CloudReadonly Production Pilot P12 Scope Guard: PASSED") {
            throw "P12 acceptance report exists but does not show required inherited checks as passed."
        }

        "Using existing P12 acceptance report: $p12Report"
    }
}

$results += Invoke-Step -Name "Enterprise CloudReadonly Production Controlled P13 Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P13 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseCloudReadonlyProductionControlledPilotP13" `
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

    $results += Invoke-Step -Name "Frontend Production Controlled Pilot Playwright Smoke" -Script {
        Push-Location src/vues/AICopilot.Web
        try {
            npm run test:smoke -- --grep "P13 production controlled pilot"
        } finally {
            Pop-Location
        }
    }
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Enterprise CloudReadonly Production Controlled Pilot P13 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; production controlled Pilot remains intent-mapped, windowed, approval-gated, and endpoint allowlisted")
$reportLines.Add("- Default State: CloudReadonlyProductionControlledPilot.Enabled=false; FreeGoalEnabled=false; query_cloud_data_readonly remains disabled, hidden, and non-executable")
$reportLines.Add("- Build Output: $buildOutputRoot for focused tests")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P13 Production Controlled Pilot Evidence")
$reportLines.Add("")
$reportLines.Add("- Controlled Intent: user goals are mapped to CloudProductionGoalIntent before any tool can run.")
$reportLines.Add("- Allowed Endpoints: devices, capacity_summary, device_logs, pass_station_records only.")
$reportLines.Add("- Gate: P12 Ready, P13 config, free-goal flag, default production flags, protected ToolRegistry state, CloudAiRead configuration, and Pilot Window intersection determine Ready/Blocked.")
$reportLines.Add("- Refusals: Recipe, Recipe version, write path, unknown endpoint, SQL/payload semantics, out-of-window endpoint, over maxRows, and over time range are blocked.")
$reportLines.Add("- Tool Registry: query_cloud_data_readonly stays closed; query_cloud_production_controlled_readonly is disabled by default and only temporarily usable through the P13 controlled Pilot gate.")
$reportLines.Add("- Outputs: sourceMode=CloudReadonlyProductionControlledPilot, sourceLabel=Cloud production readonly Controlled Pilot, boundary=ProductionControlledPilot, pilotWindowId, intentId, endpointCode, query/result hash, row count, truncation, and approval status are required.")
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
$reportLines.Add("- P13 does not open arbitrary production queries, Recipe/version reads, write paths, Cloud writes, or Cloud/Edge linkage.")
$reportLines.Add("- Real endpoint/token smoke remains optional and must be run only inside an explicit Pilot Window with approvals.")
$reportLines.Add("- P13 is a controlled Pilot, not a broad production rollout.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise CloudReadonly Production Controlled Pilot P13 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
