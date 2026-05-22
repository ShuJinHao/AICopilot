[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-pilot-planning-p15-latest.md",
    [switch]$SkipFrontend,
    [switch]$SkipInheritedP142
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-pilot-planning-p15"
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

function Get-GitAndPrSummary {
    $head = (git rev-parse HEAD 2>$null | Out-String).Trim()
    $branch = (git branch --show-current 2>$null | Out-String).Trim()
    $statusLines = @(git status --short 2>$null)
    $workingTree = if ($statusLines.Count -gt 0) {
        "dirty - local P15 changes are not covered by GitHub CI until committed and pushed"
    } else {
        "clean"
    }
    $pr = "not checked"
    $ci = "not checked"

    try {
        $json = gh pr view 48 --json headRefOid,statusCheckRollup,url 2>$null | ConvertFrom-Json
        if ($json) {
            $pr = "PR #48 head $($json.headRefOid) $($json.url)"
            $check = $json.statusCheckRollup | Where-Object { $_.name -eq "simulation-rc" } | Select-Object -First 1
            if ($check) {
                $ci = "simulation-rc status=$($check.status) conclusion=$($check.conclusion)"
            }
        }
    } catch {
        $pr = "gh unavailable or PR lookup failed"
        $ci = "gh unavailable or PR lookup failed"
    }

    return [pscustomobject]@{
        Head = $head
        Branch = $branch
        WorkingTree = $workingTree
        PullRequest = $pr
        Ci = $ci
    }
}

$results = @()

if (-not $SkipInheritedP142) {
    $results += Invoke-Step -Name "Inherited P14.2 Acceptance Report Check" -Script {
        $p142Report = ".\docs\enterprise-cloud-readonly-production-operations-p14_2-latest.md"
        if (-not (Test-Path $p142Report)) {
            throw "P14.2 acceptance report is missing: $p142Report"
        }

        $content = Get-Content -LiteralPath $p142Report -Raw
        foreach ($marker in @(
            "Enterprise CloudReadonly Production Operations P14.2 Scope Guard: PASSED",
            "Run P14.2 Focused Backend Tests: PASSED",
            "Frontend Production Operations Playwright Smoke: PASSED",
            "P15 Readiness: ReadyForP15Planning requires P12 completed run evidence"
        )) {
            if ($content -notmatch [regex]::Escape($marker)) {
                throw "P14.2 report is missing inherited marker: $marker"
            }
        }

        "Using existing P14.2 acceptance report: $p142Report"
    }
}

$results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "P15 Planning Package Check" -Script {
    $scopeDoc = ".\docs\enterprise-pilot-planning-p15-scope.md"
    $planDoc = ".\docs\enterprise-pilot-planning-p15-plan.md"

    foreach ($file in @($scopeDoc, $planDoc)) {
        if (-not (Test-Path $file)) {
            throw "Missing P15 planning file: $file"
        }
    }

    $content = (Get-Content -LiteralPath $scopeDoc -Raw) + "`n" + (Get-Content -LiteralPath $planDoc -Raw)
    foreach ($marker in @(
        "planning and authorization gate",
        "not P16",
        "not GA",
        "query_cloud_data_readonly",
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records",
        "ProductionPilotWindow",
        "ProductionPilotRun",
        "ProductionControlledPilotIntent",
        "ProductionControlledPilotRun",
        "final artifact refs",
        "TTL",
        "ReadyForP16Execution"
    )) {
        if ($content -notmatch [regex]::Escape($marker)) {
            throw "P15 planning package is missing marker: $marker"
        }
    }

    if ($content -match "(?i)(token\s*[:=]\s*[^,\r\n]+|api key\s*[:=]\s*[^,\r\n]+|connection string\s*[:=]\s*[^,\r\n]+)") {
        throw "P15 planning package contains a secret-like literal."
    }

    "P15 planning package markers passed."
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

    $results += Invoke-Step -Name "Frontend P15 Planning Playwright Smoke" -Script {
        Push-Location src/vues/AICopilot.Web
        try {
            npm run test:smoke -- --grep "P15 planning"
        } finally {
            Pop-Location
        }
    }
}

$gitSummary = Get-GitAndPrSummary

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Enterprise Pilot Planning P15 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: <local-repo>")
$reportLines.Add("- ReportGeneratedFromHead: $($gitSummary.Head)")
$reportLines.Add("- Branch: $($gitSummary.Branch)")
$reportLines.Add("- WorkingTree: $($gitSummary.WorkingTree)")
$reportLines.Add("- PullRequestAtGeneration: $($gitSummary.PullRequest)")
$reportLines.Add("- GitHubCIAtGeneration: $($gitSummary.Ci) (covers the PR head visible when this report was generated)")
$reportLines.Add("- SubmissionNote: after committing this report refresh, use PR #48 current head and GitHub checks as the authoritative submitted-state evidence")
$reportLines.Add("- Boundary: P15 is planning and authorization only; it is not P16 execution and not GA")
$reportLines.Add("- Default State: query_cloud_data_readonly remains disabled, hidden, and non-executable")
$reportLines.Add("- Forbidden: Cloud write, Recipe/version, free SQL, raw payload, rows, token/API key/connection string output")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P15 Planning Evidence")
$reportLines.Add("")
$reportLines.Add("- Pilot users: 5-10 internal users.")
$reportLines.Add("- Roles: Admin, TrialManager, Approver, Operator, Viewer.")
$reportLines.Add("- Endpoints: devices, capacity_summary, device_logs, pass_station_records.")
$reportLines.Add("- Limits: latest 7 days, default maxRows=50.")
$reportLines.Add("- Outputs: Markdown, HTML, PDF, PPTX, XLSX drafts and final-approved artifacts.")
$reportLines.Add("- Data retention: operations ledger remains hash-only; P12/P13 rows retention requires P16 blocker closure.")
$reportLines.Add("- Go/No-Go: P15 may be ReadyForP16Planning, but must not be ReadyForP16Execution while blockers remain.")
$reportLines.Add("")
$reportLines.Add("## P16 Blockers")
$reportLines.Add("")
$reportLines.Add("- Persist P12 ProductionPilotWindow and ProductionPilotRun.")
$reportLines.Add("- Persist P13 ProductionControlledPilotIntent and ProductionControlledPilotRun.")
$reportLines.Add("- Automatically backfill final artifact refs into ProductionPilotRunLedger.")
$reportLines.Add("- Define P12/P13 rows retention, masking, TTL, download, and artifact-use policy.")
$reportLines.Add("- Add P14 operations permission smoke for ordinary-user rejection and authorized-manager success.")
$reportLines.Add("- Add long-running and concurrency validation for multi-user Pilot operations.")
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
$reportLines.Add("- P15 does not implement P12/P13 store persistence or artifact-ref backfill.")
$reportLines.Add("- Real endpoint/token smoke remains outside P15 and must wait for a P16 Pilot Window plus approval.")
$reportLines.Add("- P15 planning is not GA and not full production rollout.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Pilot Planning P15 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
