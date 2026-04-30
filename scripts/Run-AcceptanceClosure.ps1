[CmdletBinding()]
param(
    [string]$ReportPath = ".\资料\acceptance-closure-latest.md"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

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

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj /m:1 /p:UseSharedCompilation=false
}
$results += Invoke-Step -Name "Build AppHost" -Script {
    dotnet build src/hosts/AICopilot.AppHost/AICopilot.AppHost.csproj /m:1 /p:UseSharedCompilation=false
}
$results += Invoke-Step -Name "Build BackendTests" -Script {
    dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj /m:1 /p:UseSharedCompilation=false
}
$results += Invoke-Step -Name "Build Frontend" -Script {
    Push-Location src/vues/AICopilot.Web
    try {
        npm run build
    } finally {
        Pop-Location
    }
}
$results += Invoke-Step -Name "Check Diff Whitespace" -Script {
    git diff --check
}
$results += Invoke-Step -Name "Check Architecture Boundaries" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-ArchitectureBoundaries.ps1
}
$results += Invoke-Step -Name "Run Focused Unit Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~SqlGuardrailTests|FullyQualifiedName~SemanticSqlGenerationTests|FullyQualifiedName~TextToSqlReadOnlyTests|FullyQualifiedName~AgentScopeLifecycleTests"
}

$dockerInfo = Invoke-Step -Name "Check Docker" -Script {
    cmd /c "docker info 2>nul"
}
$results += $dockerInfo

if ($dockerInfo.Succeeded) {
    $results += Invoke-Step -Name "Run Migration And Redis Verification Tests" -Script {
        dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~AcceptanceClosureVerificationTests"
    }
    $results += Invoke-Step -Name "Run Runtime Smoke - Missing Template Error Chunk" -Script {
        dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~Phase25RuntimeSmokeTests.ChatFlow_ShouldReturnConfigurationErrorChunk_WhenSessionTemplateIsMissing"
    }
    $results += Invoke-Step -Name "Run Runtime Smoke - Concurrent Approval Idempotency" -Script {
        dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~Phase25RuntimeSmokeTests.ApprovalDecision_ShouldOnlyExecuteOnce_WhenSameCallIsSubmittedConcurrently"
    }
    $results += Invoke-Step -Name "Run Runtime Smoke - Onsite Attestation Gate" -Script {
        dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~Phase43SafetyQualityTests.ApprovalDecision_ShouldRequireValidOnsiteAttestation_AndExplicitReconfirmation"
    }
} else {
    Write-Warning "Docker server is unavailable. Skipping container-backed schema, Redis, and runtime smoke checks."
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Acceptance Closure Report")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## Details")
$reportLines.Add("")

foreach ($result in $results) {
    $reportLines.Add("### $($result.Name)")
    $reportLines.Add("")
    $reportLines.Add('```text')
    foreach ($line in ($result.Output -split "`r?`n")) {
        $reportLines.Add($line)
    }
    $reportLines.Add('```')
    $reportLines.Add("")
}

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Acceptance report written to: $ReportPath"
