[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-cloud-readonly-production-operations-p14-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP13
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-cloud-readonly-production-operations-p14"
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

if (-not $SkipInheritedP13) {
    $results += Invoke-Step -Name "Inherited P13 Acceptance Report Check" -Script {
        $p13Report = ".\docs\enterprise-cloud-readonly-production-controlled-p13-latest.md"
        if (-not (Test-Path $p13Report)) {
            throw "P13 acceptance report is missing: $p13Report"
        }

        $content = Get-Content -LiteralPath $p13Report -Raw
        if ($content -notmatch "Run P13 Focused Backend Tests: PASSED" -and
            $content -notmatch "Enterprise CloudReadonly Production Controlled P13 Scope Guard: PASSED") {
            throw "P13 acceptance report exists but does not show required inherited checks as passed."
        }

        "Using existing P13 acceptance report: $p13Report"
    }
}

$results += Invoke-Step -Name "Enterprise CloudReadonly Production Operations P14 Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P14 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseCloudReadonlyProductionOperationsP14" `
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

    $results += Invoke-Step -Name "Frontend Production Operations Playwright Smoke" -Script {
        Push-Location src/vues/AICopilot.Web
        try {
            npm run test:smoke -- --grep "P14 production operations"
        } finally {
            Pop-Location
        }
    }
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Enterprise CloudReadonly Production Operations P14 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; P14 is production readonly Pilot operations, not full production rollout")
$reportLines.Add("- Default State: query_cloud_data_readonly remains disabled, hidden, and non-executable; P12/P13 gates still control production Pilot reads")
$reportLines.Add("- Build Output: $buildOutputRoot for focused tests")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P14 Production Operations Evidence")
$reportLines.Add("")
$reportLines.Add("- Operations Ledger: combines P12 fixed-template and P13 controlled Pilot runs using source mode, boundary, endpoint, query/result hash, row count, truncation, approval status, and run status.")
$reportLines.Add("- Emergency Stop: runtime emergency stop blocks both P12 and P13 production readonly Pilot execution; clearing it does not bypass original gate/window/approval checks.")
$reportLines.Add("- Metrics: total runs, success/failure/rejection/timeout, endpoint distribution, row count, truncation, final artifact references, and open incidents are computed from sanitized summaries.")
$reportLines.Add("- P15 Readiness: NotEvaluated/CollectingEvidence/Blocked/ReadyForP15Planning is represented by the P14 operations status and GA readiness assessment; open high/critical incidents block planning.")
$reportLines.Add("- Security: reports and UI use endpoint/hash/status/duration/row count only; no token, connection string, full SQL, full payload, or sensitive context is emitted.")
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
$reportLines.Add("- P14 does not broaden production endpoints, does not enable Recipe/version reads, and does not introduce Cloud writes.")
$reportLines.Add("- Runtime stores are in-memory for this Pilot operations baseline; persistent long-term operations evidence can be introduced during P15 planning if required.")
$reportLines.Add("- P14 readiness only means the production readonly Pilot can be operated and reviewed; it is not GA.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise CloudReadonly Production Operations P14 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
