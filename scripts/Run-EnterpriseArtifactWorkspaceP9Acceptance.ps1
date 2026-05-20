[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-artifact-workspace-p9-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP8
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-artifact-workspace-p9"
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

if (-not $SkipInheritedP8) {
    $results += Invoke-Step -Name "Inherited P8 Acceptance" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlySandboxExpansionP8Acceptance.ps1 `
            -ReportPath .\docs\enterprise-cloud-readonly-sandbox-expansion-p8-latest.md `
            -SkipFrontend `
            -SkipInheritedP7
    }
} else {
    $results += Invoke-Step -Name "Inherited P8 Acceptance Report Check" -Script {
        $p8Report = ".\docs\enterprise-cloud-readonly-sandbox-expansion-p8-latest.md"
        if (-not (Test-Path $p8Report)) {
            throw "P8 acceptance report is missing: $p8Report"
        }

        $content = Get-Content -LiteralPath $p8Report -Raw
        if ($content -notmatch "Run P8 Focused Backend Tests: PASSED" -or
            $content -notmatch "Run P7 And Contract Regression Tests: PASSED") {
            throw "P8 acceptance report exists but does not show the required inherited checks as passed."
        }

        "Using existing P8 acceptance report: $p8Report"
    }
}

$results += Invoke-Step -Name "Enterprise Artifact Workspace P9 Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Run P9 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=EnterpriseArtifactWorkspaceP9" `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "backendtests")
}

$results += Invoke-Step -Name "Run Artifact Workspace Regression Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "Suite=Batch8ArtifactVersioning|Suite=FrontendIntegrationContract" `
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

    $results += Invoke-Step -Name "Frontend Artifact Workspace HTTP Smoke" -Script {
        $frontendRoot = Join-Path $repoRoot "src/vues/AICopilot.Web"
        $port = 5185
        $out = Join-Path $env:TEMP "aicopilot-p9-vite-smoke.out.log"
        $err = Join-Path $env:TEMP "aicopilot-p9-vite-smoke.err.log"
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
$reportLines.Add("# AICopilot Enterprise Artifact Workspace P9 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled by default")
$reportLines.Add("- Artifact Sources: SimulationBusiness and CloudReadonlySandbox only")
$reportLines.Add("- Delivery Boundary: draft preview, revision, draft regeneration, final approval, final lock, and audit")
$reportLines.Add("- Build Output: $buildOutputRoot for focused tests")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P9 Artifact Workspace Evidence")
$reportLines.Add("")
$reportLines.Add("- Source Markers: artifact DTOs preserve sourceMode, boundary, isSimulation, isSandbox, sourceLabel, queryHash/resultHash, rowCount, and isTruncated.")
$reportLines.Add("- Draft Governance: draft regeneration increments artifactVersion and preserves source markers; revision comments are audited by hash.")
$reportLines.Add("- Preview Contract: Markdown/HTML text preview, PDF/PPTX metadata, XLSX/CSV row previews, chart JSON, and download metadata are exposed through artifact id only.")
$reportLines.Add("- Final Lock: final approval and finalize move artifacts to final paths, set finalizedAt, and prevent draft regeneration from overwriting final artifacts.")
$reportLines.Add("- Audit Shape: artifact id/version, source mode, hash, status, user, duration, and error code without token, API Key, connection string, full SQL, or full sandbox payload.")
$reportLines.Add("- Frontend Smoke: Agent workbench shows draft/final areas, version history basics, preview, source labels, hashes, row count, truncation, approval status, and audit summary.")
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
$reportLines.Add("- P9 proves the artifact delivery center over controlled SimulationBusiness and CloudReadonlySandbox sources; it does not prove production Cloud data access.")
$reportLines.Add("- P9 is not a full document collaboration or file-drive system; multi-user document coauthoring remains out of scope.")
$reportLines.Add("- Real CloudReadonly, production Agent queries, Cloud/Edge linkage, and old-interface compatibility remain out of scope.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Artifact Workspace P9 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
