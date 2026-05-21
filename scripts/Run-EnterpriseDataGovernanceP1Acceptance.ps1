[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-data-governance-p1-latest.md",
    [switch]$SkipFrontend
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-data-governance-p1"
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

$results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj /m:1 /p:UseSharedCompilation=false -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Compile BackendTests Sources" -Script {
    dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-dependencies /m:1 /p:UseSharedCompilation=false
}

$results += Invoke-Step -Name "Run P0 And P1 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "FullyQualifiedName~EnterpriseDataGovernanceP0Tests|FullyQualifiedName~EnterpriseDataGovernanceP1Tests|FullyQualifiedName~TextToSqlReadOnlyTests|FullyQualifiedName~SqlGuardrailTests|FullyQualifiedName~ModelProviderReliabilityTests" `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "backendtests")
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
$reportLines.Add("# AICopilot Enterprise Data Governance P1 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled")
$reportLines.Add("- Test Mode: fake/mock model endpoints and SimulationBusiness data source; real API keys are not required")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P1 Evidence")
$reportLines.Add("")
$reportLines.Add("- Text-to-SQL: deterministic draft generation, schema allowlist, sensitive field blocklist, and read-only SQL guardrail are covered by focused backend tests.")
$reportLines.Add("- Agent/Data output: business query result contracts preserve sourceMode, isSimulation, sourceLabel, queryHash, row count, and truncation status.")
$reportLines.Add("- Prompt Policy: policy version activation and hash-only audit metadata are covered by compile and focused domain tests.")
$reportLines.Add("- RAG Governance: category/supplement commands, soft-delete semantics, and CriticalOverride effective-window behavior are covered.")
$reportLines.Add("- Model Pool: LeastInFlight, WeightedRoundRobin, concurrency saturation, sticky streaming, fallback, and circuit statistics are covered with mock endpoints.")
$reportLines.Add("- Secrets: API keys remain write-only in configuration-facing contracts; acceptance output does not print plaintext keys.")
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

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Data Governance P1 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
