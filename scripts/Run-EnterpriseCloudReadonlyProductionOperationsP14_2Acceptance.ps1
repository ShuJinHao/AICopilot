[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-cloud-readonly-production-operations-p14_2-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP14
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-cloud-readonly-production-operations-p14_2"
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
    param(
        [AllowNull()]
        [string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $safe = $Text
    $escapedBuildOutputRoot = [regex]::Escape($buildOutputRoot)
    $safe = [regex]::Replace($safe, $escapedBuildOutputRoot, "<temp-build-output>", "IgnoreCase")

    $escapedRepoRoot = [regex]::Escape($repoRoot)
    $safe = [regex]::Replace($safe, $escapedRepoRoot, "<local-repo>", "IgnoreCase")

    $userProfile = [Environment]::GetFolderPath("UserProfile")
    if (-not [string]::IsNullOrWhiteSpace($userProfile)) {
        $safe = [regex]::Replace($safe, [regex]::Escape($userProfile), "<user-profile>", "IgnoreCase")
    }

    return $safe
}

$results = @()

if (-not $SkipInheritedP14) {
    $results += Invoke-Step -Name "Inherited P14 Acceptance Report Check" -Script {
        $p14Report = ".\docs\enterprise-cloud-readonly-production-operations-p14-latest.md"
        if (-not (Test-Path $p14Report)) {
            throw "P14 acceptance report is missing: $p14Report"
        }

        $content = Get-Content -LiteralPath $p14Report -Raw
        if ($content -notmatch "Run P14 Focused Backend Tests: PASSED" -and
            $content -notmatch "Enterprise CloudReadonly Production Operations P14 Scope Guard: PASSED") {
            throw "P14 acceptance report exists but does not show required inherited checks as passed."
        }

        "Using existing P14 acceptance report: $p14Report"
    }
}

$results += Invoke-Step -Name "Enterprise CloudReadonly Production Operations P14.2 Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P14.2 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseCloudReadonlyProductionOperationsP14" `
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
$reportLines.Add("# AICopilot Enterprise CloudReadonly Production Operations P14.2 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: <local-repo>")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; P14.2 hardens Pilot operations persistence and P15 readiness only")
$reportLines.Add("- Default State: query_cloud_data_readonly remains disabled, hidden, and non-executable; P12/P13 gates still control production Pilot reads")
$reportLines.Add("- Retention: operations ledger is hash-only; rows/raw payload/full SQL/token/API key/connection string are not persisted or returned")
$reportLines.Add("- Build Output: <temp-build-output>")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P14.2 Production Operations Evidence")
$reportLines.Add("")
$reportLines.Add("- Persistence: emergency stop, incident, run ledger, and GA readiness assessment use ProductionOperations entities in AiGatewayDbContext.")
$reportLines.Add("- Emergency Stop: active state persists across store/service reconstruction and blocks both P12 and P13 production readonly Pilot execution.")
$reportLines.Add("- Ledger: only source mode, boundary, endpoint, approval status, duration, row count, truncation, query/result hash, artifact refs, and status are retained.")
$reportLines.Add("- P15 Readiness: ReadyForP15Planning requires P12 completed run evidence, P13 completed run evidence, final artifact references, clear emergency stop, no open high/critical incident, and protected production tools closed.")
$reportLines.Add("- Security: reports, tests, and UI assert no token, connection string, full SQL, rows, raw payload, or sensitive context is emitted.")
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
$reportLines.Add("- P14.2 does not broaden production endpoints, does not enable Recipe/version reads, and does not introduce Cloud writes.")
$reportLines.Add("- P14.2 readiness only permits P15 planning review; it is not GA and not full production rollout.")
$reportLines.Add("- Real endpoint/token smoke remains outside CI and must stay under explicit Pilot Window plus approval.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise CloudReadonly Production Operations P14.2 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
