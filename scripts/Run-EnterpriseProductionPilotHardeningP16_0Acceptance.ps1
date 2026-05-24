[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-production-pilot-hardening-p16_0-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP15
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-production-pilot-hardening-p16_0"
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

function ConvertTo-ReportSafeOutput {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $safe = $Text
    $safe = [regex]::Replace($safe, [regex]::Escape($buildOutputRoot), "<temp-build-output>", "IgnoreCase")
    $safe = [regex]::Replace($safe, [regex]::Escape($repoRoot), "<local-repo>", "IgnoreCase")

    $userProfile = [Environment]::GetFolderPath("UserProfile")
    if (-not [string]::IsNullOrWhiteSpace($userProfile)) {
        $safe = [regex]::Replace($safe, [regex]::Escape($userProfile), "<user-profile>", "IgnoreCase")
    }

    return $safe
}

$results = @()

if (-not $SkipInheritedP15) {
    $results += Invoke-Step -Name "Inherited P15 Acceptance Report Check" -Script {
        $p15Report = ".\docs\enterprise-pilot-planning-p15-latest.md"
        if (-not (Test-Path $p15Report)) {
            throw "P15 acceptance report is missing: $p15Report"
        }

        $content = Get-Content -LiteralPath $p15Report -Raw
        foreach ($marker in @(
            "Inherited P14.2 Acceptance Report Check: PASSED",
            "P15 Planning Package Check: PASSED",
            "Frontend P15 Planning Playwright Smoke: PASSED",
            "P16 Blockers",
            "query_cloud_data_readonly remains disabled"
        )) {
            if ($content -notmatch [regex]::Escape($marker)) {
                throw "P15 report is missing inherited marker: $marker"
            }
        }

        "Using existing P15 acceptance report: $p15Report"
    }
}

$results += Invoke-Step -Name "Enterprise Production Pilot Hardening P16.0 Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "P16.0 Scope Document Check" -Script {
    $scopeDoc = Get-ChildItem .\docs -Filter "*P16_0*.md" |
        Where-Object { $_.Name -notlike "*latest*" } |
        Select-Object -First 1
    if (-not $scopeDoc) {
        throw "P16.0 scope document is missing: $scopeDoc"
    }

    $content = Get-Content -LiteralPath $scopeDoc.FullName -Raw
    foreach ($marker in @(
        "P16.0",
        "query_cloud_data_readonly",
        "ProductionPilotWindow",
        "ProductionControlledPilotIntent",
        "hash-only",
        "raw payload",
        "rows / raw business records"
    )) {
        if ($content -notmatch [regex]::Escape($marker)) {
            throw "P16.0 scope document is missing marker: $marker"
        }
    }

    "P16.0 scope document markers passed."
}

$results += Invoke-Step -Name "P16.0 Persistence Artifact Check" -Script {
    $migration = Get-ChildItem .\src\infrastructure\AICopilot.EntityFrameworkCore\Migrations\AiGatewayDbContext -Filter "*AddProductionPilotHardeningP160.cs" | Select-Object -First 1
    if (-not $migration) {
        throw "P16.0 migration is missing."
    }

    $content = (Get-Content -LiteralPath $migration.FullName -Raw) +
        "`n" + (Get-Content -LiteralPath ".\src\infrastructure\AICopilot.EntityFrameworkCore\Configuration\AiGateway\ProductionOperationConfiguration.cs" -Raw) +
        "`n" + (Get-Content -LiteralPath ".\src\infrastructure\AICopilot.EntityFrameworkCore\AiGatewayDbContext.cs" -Raw)

    foreach ($marker in @(
        "production_pilot_windows",
        "production_pilot_runs",
        "production_controlled_pilot_intents",
        "production_controlled_pilot_runs",
        "ProductionPilotWindows",
        "ProductionControlledPilotIntents"
    )) {
        if ($content -notmatch [regex]::Escape($marker)) {
            throw "P16.0 persistence artifact is missing marker: $marker"
        }
    }

    if ($content -match "(?i)(raw_payload|full_sql|api_key|connection_string)") {
        throw "P16.0 persistence artifact contains forbidden raw-payload or secret-like column marker."
    }

    "P16.0 persistence artifacts passed."
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P16.0 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseProductionPilotHardeningP16_0" `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "backendtests")
}

$results += Invoke-Step -Name "Run CloudReadonly Route Contract Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "FullyQualifiedName~CloudReadonlyReadinessRoutes_ShouldBeRoutable" `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "route-contract")
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
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Enterprise Production Pilot Hardening P16.0 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: <local-repo>")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; P16.0 closes engineering blockers before real Pilot execution review")
$reportLines.Add("- Default State: query_cloud_data_readonly remains disabled, hidden, and non-executable")
$reportLines.Add("- Forbidden: Cloud write, Recipe/version, free SQL, raw payload, rows/raw business records, token/API key/connection string output")
$reportLines.Add("- Retention: operations ledger is hash-only; P12/P13 persisted run stores do not persist rows")
$reportLines.Add("- Build Output: <temp-build-output>")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P16.0 Hardening Evidence")
$reportLines.Add("")
$reportLines.Add("- P12 Store: ProductionPilotWindow and ProductionPilotRun are persisted through AiGatewayDbContext.")
$reportLines.Add("- P13 Store: ProductionControlledPilotIntent and ProductionControlledPilotRun are persisted through AiGatewayDbContext.")
$reportLines.Add("- Restart Recovery: focused tests reconstruct repository-backed stores and recover P12/P13 pilot state.")
$reportLines.Add("- Artifact Backfill: final P12/P13 artifacts backfill ProductionPilotRunLedger artifact refs without raw rows.")
$reportLines.Add("- Rows Retention: runtime rows remain short-lived; operations, readiness, reports, and frontend evidence remain hash-only.")
$reportLines.Add("- Permissions/Concurrency: focused tests cover management permission metadata and emergency stop concurrent state consistency.")
$reportLines.Add("")
$reportLines.Add("## Details")
$reportLines.Add("")

foreach ($result in $results) {
    $reportLines.Add("### $($result.Name)")
    $reportLines.Add("")
    $reportLines.Add('```text')
    $safeOutput = ConvertTo-ReportSafeOutput -Text $result.Output
    foreach ($line in ($safeOutput -split "`r?`n")) {
        $reportLines.Add($line.TrimEnd())
    }
    $reportLines.Add('```')
    $reportLines.Add("")
}

$reportLines.Add("## Remaining Risk")
$reportLines.Add("")
$reportLines.Add("- P16.0 does not authorize real Pilot execution; it only prepares the engineering baseline for a later execution review.")
$reportLines.Add("- Real endpoint/token use remains outside CI and must stay behind Pilot Window, approval chain, rollback strategy, and emergency stop.")
$reportLines.Add("- P16.0 does not broaden production endpoints, does not enable Recipe/version reads, and does not introduce Cloud writes.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Production Pilot Hardening P16.0 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
