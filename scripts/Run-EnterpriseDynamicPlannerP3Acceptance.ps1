[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-dynamic-planner-p3-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP2
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-dynamic-planner-p3"
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

if (-not $SkipInheritedP2) {
    $results += Invoke-Step -Name "Inherited P2 Acceptance" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseAgentWorkbenchP2Acceptance.ps1 `
            -ReportPath .\docs\enterprise-agent-workbench-p2-latest.md `
            -SkipFrontend
    }
}

$results += Invoke-Step -Name "Enterprise Dynamic Planner Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P3 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "FullyQualifiedName~EnterpriseDynamicPlannerP3Tests|FullyQualifiedName~DynamicPlannerContractTests|FullyQualifiedName~ToolRegistryGovernanceTests.DynamicPlanner" `
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

    $results += Invoke-Step -Name "Frontend HTTP Smoke" -Script {
        $frontendRoot = Join-Path $repoRoot "src/vues/AICopilot.Web"
        $port = 5179
        $out = Join-Path $env:TEMP "aicopilot-p3-vite-smoke.out.log"
        $err = Join-Path $env:TEMP "aicopilot-p3-vite-smoke.err.log"
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
$reportLines.Add("# AICopilot Enterprise Dynamic Planner P3 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled")
$reportLines.Add("- Trial Data Source: SimulationBusiness / AI independent simulation business database")
$reportLines.Add("- Test Mode: fake/mock planner endpoints; real API keys are not required")
$reportLines.Add("- Build Output: $buildOutputRoot")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P3 Dynamic Planner Evidence")
$reportLines.Add("")
$reportLines.Add("- Dynamic Plan: fake/mock planner can produce a reviewed plan that persists plannerMode=Dynamic.")
$reportLines.Add("- Static Fallback: plannerMode=Auto falls back to StaticFallback when no enabled Planner model is available.")
$reportLines.Add("- Illegal Plan Rejection: SQL statement semantics, shell/path semantics, unauthorized tools, and non-SimulationBusiness data sources are rejected by backend guardrails.")
$reportLines.Add("- Forced Steps: backend records forcedStepCodes for BusinessDatabase query, summary, business chart, and final approval steps.")
$reportLines.Add("- Source Markers: Text-to-SQL results and artifacts continue to carry sourceMode=SimulationBusiness, isSimulation=true, sourceLabel, and queryHash evidence.")
$reportLines.Add("- Frontend Smoke: Vite app shell is requested at /chat after build when frontend checks are enabled.")
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
$reportLines.Add("- P3 still does not connect to real Cloud data or require real model API keys.")
$reportLines.Add("- Dynamic planner execution is backend-constrained; full MCP productization remains a later phase.")
$reportLines.Add("- Real CloudReadonly trial still requires separate authorization, Cloud input completion, and status checks.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Dynamic Planner P3 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
