[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-agent-workbench-p2-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP15
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-agent-workbench-p2"
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

if (-not $SkipInheritedP15) {
    $results += Invoke-Step -Name "Inherited P1.5 Acceptance" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseDataGovernanceP1_5Acceptance.ps1 `
            -ReportPath .\docs\enterprise-data-governance-p1_5-latest.md `
            -SkipFrontend
    }
}

$results += Invoke-Step -Name "Enterprise Agent Workbench Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P2 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "FullyQualifiedName~EnterpriseAgentWorkbenchP2Tests|FullyQualifiedName~AgentReportComposerTests|FullyQualifiedName~AgentArtifactGenerationTests|FullyQualifiedName~FrontendIntegrationContractTests" `
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
$reportLines.Add("# AICopilot Enterprise Agent Workbench P2 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled")
$reportLines.Add("- Trial Data Source: SimulationBusiness / AI independent simulation business database")
$reportLines.Add("- Test Mode: fake/mock model endpoints; real API keys are not required")
$reportLines.Add("- Build Output: $buildOutputRoot")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P2 Workbench Evidence")
$reportLines.Add("")
$reportLines.Add("- Templates: capacity-analysis, quality-defects, device-downtime, inventory-turnover, sales-delivery, employee-policy-rag.")
$reportLines.Add("- Plan Gate: scenario creation returns a waiting plan; execution still requires user approval before tools run.")
$reportLines.Add("- Query Evidence: Text-to-SQL results and artifacts carry sourceMode=SimulationBusiness, isSimulation=true, sourceLabel=AI independent simulation business database, and queryHash samples.")
$reportLines.Add("- Artifact Evidence: Markdown, HTML, PDF, PPTX, XLSX, and chart data preserve SimulationBusiness source markers where generated.")
$reportLines.Add("- RAG Scenario: employee-policy-rag keeps CriticalOverride and simulated policy language in the prompt boundary.")
$reportLines.Add("- Secrets: API keys, connection strings, tokens, and passwords are not printed in acceptance evidence.")
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
$reportLines.Add("- P2 still does not connect to real Cloud data or require real model API keys.")
$reportLines.Add("- SimulationBusiness replaces the total-plan Cloud readonly loop only for internal trial validation.")
$reportLines.Add("- A future Real CloudReadonly trial requires separate authorization, scope guard update, and smoke-only production-data handling.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Agent Workbench P2 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
