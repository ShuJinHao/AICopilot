[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-tool-governance-p4-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP3
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-tool-governance-p4"
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

if (-not $SkipInheritedP3) {
    $results += Invoke-Step -Name "Inherited P3 Acceptance" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseDynamicPlannerP3Acceptance.ps1 `
            -ReportPath .\docs\enterprise-dynamic-planner-p3-latest.md `
            -SkipFrontend
    }
}

$results += Invoke-Step -Name "Enterprise Tool Governance Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Build EntityFrameworkCore" -Script {
    dotnet build src/infrastructure/AICopilot.EntityFrameworkCore/AICopilot.EntityFrameworkCore.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "efcore")
}

$results += Invoke-Step -Name "Run P4 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseToolGovernanceP4|Suite=ToolRegistryGovernance|Suite=EnterpriseDynamicPlannerP3" `
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

    $results += Invoke-Step -Name "Frontend Tool Registry HTTP Smoke" -Script {
        $frontendRoot = Join-Path $repoRoot "src/vues/AICopilot.Web"
        $port = 5180
        $out = Join-Path $env:TEMP "aicopilot-p4-vite-smoke.out.log"
        $err = Join-Path $env:TEMP "aicopilot-p4-vite-smoke.err.log"
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
$reportLines.Add("# AICopilot Enterprise Tool Governance P4 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled")
$reportLines.Add("- Trial Data Source: SimulationBusiness / AI independent simulation business database")
$reportLines.Add("- Tool Runtime: built-in tools plus in-process Mock MCP provider only")
$reportLines.Add("- Test Mode: fake/mock planner endpoints and mock MCP tools; real API keys and real external MCP servers are not required")
$reportLines.Add("- Build Output: $buildOutputRoot")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P4 Tool Governance Evidence")
$reportLines.Add("")
$reportLines.Add("- Tool Catalog: Planner-visible tools are filtered by user permission, enabled state, risk level, data boundary, and SimulationBusiness context.")
$reportLines.Add("- Mock MCP: mock_mcp_health_check, mock_mcp_kpi_formula_lookup, mock_mcp_artifact_quality_check, and mock_mcp_external_ticket_preview are registered with catalog version evidence.")
$reportLines.Add("- Mock Only Boundary: appsettings keep Mcp.Runtime.Enabled=false and Mcp.Runtime.MockOnly=true; no real external MCP endpoint is default-enabled.")
$reportLines.Add("- Approval Boundary: High-risk mock external ticket preview requires Tool Approval; Critical tools are not executable in P4.")
$reportLines.Add("- Execution Audit: tool runs capture providerKind, isMock, toolRunId, toolCatalogVersion, duration, status, and resultHash without SQL plaintext or secrets.")
$reportLines.Add("- Agent Closure: P3 SimulationBusiness dynamic planner flow remains inherited, with queryHash and source markers preserved.")
$reportLines.Add("- Frontend Smoke: config page app shell is requested after build when frontend checks are enabled.")
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
$reportLines.Add("- P4 does not connect to real Cloud data or require real model API keys.")
$reportLines.Add("- P4 does not enable real external MCP servers; external side-effect tools are preview-only mock calls.")
$reportLines.Add("- Real CloudReadonly and controlled external MCP trials require separate authorization, endpoint configuration, and smoke-only rollout.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Tool Governance P4 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
