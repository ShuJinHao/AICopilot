[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-data-governance-p1_5-latest.md",
    [switch]$SkipFrontend
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$buildOutputRoot = Join-Path $env:TEMP "aicopilot-enterprise-data-governance-p1_5"
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

function Invoke-TemporaryPostgresMigrationSmoke {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        Write-Output "SKIPPED: Docker is not available; migration smoke requires an isolated PostgreSQL container."
        return
    }

    docker version --format "{{.Server.Version}}" *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Output "SKIPPED: Docker engine is not available; migration smoke requires an isolated PostgreSQL container."
        return
    }

    $image = docker images postgres:18-alpine --format "{{.Repository}}:{{.Tag}}" | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($image)) {
        Write-Output "SKIPPED: postgres:18-alpine image is not available locally; migration smoke did not pull network dependencies."
        return
    }

    $container = "aicopilot-p15-pg-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $migrationOutputRoot = Join-Path $buildOutputRoot "migrationworkapp"
    $previousConnectionString = [Environment]::GetEnvironmentVariable("ConnectionStrings__ai-copilot", "Process")
    $previousEncryptionKey = [Environment]::GetEnvironmentVariable("AICopilotSecurity__ApiKeyEncryptionKey", "Process")

    docker run -d --rm --name $container `
        -e POSTGRES_PASSWORD=p15_test_password `
        -e POSTGRES_DB=aicopilot_p15 `
        -p 127.0.0.1::5432 `
        postgres:18-alpine *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start temporary PostgreSQL container."
    }

    try {
        $ready = $false
        for ($i = 0; $i -lt 60; $i++) {
            docker exec $container pg_isready -U postgres -d aicopilot_p15 *> $null
            if ($LASTEXITCODE -eq 0) {
                $ready = $true
                break
            }

            Start-Sleep -Seconds 1
        }

        if (-not $ready) {
            throw "Temporary PostgreSQL container did not become ready."
        }

        $portLine = docker port $container 5432/tcp
        $port = ($portLine -split ":")[-1]
        $connectionString = "Host=127.0.0.1;Port=$port;Database=aicopilot_p15;Username=postgres;Password=p15_test_password"

        $buildOutput = dotnet build src/hosts/AICopilot.MigrationWorkApp/AICopilot.MigrationWorkApp.csproj `
            /m:1 /p:UseSharedCompilation=false `
            -o $migrationOutputRoot 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "MigrationWorkApp build failed.`n$buildOutput"
        }

        [Environment]::SetEnvironmentVariable("ConnectionStrings__ai-copilot", $connectionString, "Process")
        [Environment]::SetEnvironmentVariable("AICopilotSecurity__ApiKeyEncryptionKey", "p15-local-migration-key", "Process")

        $runOutput = dotnet (Join-Path $migrationOutputRoot "AICopilot.MigrationWorkApp.dll") 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "MigrationWorkApp execution failed.`n$runOutput"
        }

        $tableCheck = "select count(*) from information_schema.tables where table_schema in ('dataanalysis','rag','aigateway') and table_name in ('business_databases','knowledge_categories','knowledge_supplements','prompt_policies','prompt_policy_versions');"
        $tableCount = docker exec $container psql -U postgres -d aicopilot_p15 -tAc $tableCheck
        if ([int]$tableCount.Trim() -lt 5) {
            throw "Expected P1.5 migration tables were not all present. Count=$tableCount"
        }

        $rollbackCheck = "BEGIN; INSERT INTO aigateway.prompt_policies (id, code, name, usage, is_enabled, active_version_no, created_at, updated_at) VALUES (gen_random_uuid(), 'p15.rollback', 'P15 Rollback', 'TextToSql', true, null, now(), now()); SELECT count(*) FROM aigateway.prompt_policies WHERE code = 'p15.rollback'; ROLLBACK; SELECT count(*) FROM aigateway.prompt_policies WHERE code = 'p15.rollback';"
        $rollbackOutput = docker exec $container psql -U postgres -d aicopilot_p15 -tAc $rollbackCheck
        $counts = $rollbackOutput | Where-Object { $_.Trim() -match "^[0-9]+$" } | ForEach-Object { $_.Trim() }
        if ($counts.Count -lt 2 -or $counts[-2] -ne "1" -or $counts[-1] -ne "0") {
            throw "Rollback smoke check failed: $rollbackOutput"
        }

        Write-Output "Temporary PostgreSQL migration smoke passed. Tables=$($tableCount.Trim()); rollback=$($counts[-2])->$($counts[-1])."
    }
    finally {
        [Environment]::SetEnvironmentVariable("ConnectionStrings__ai-copilot", $previousConnectionString, "Process")
        [Environment]::SetEnvironmentVariable("AICopilotSecurity__ApiKeyEncryptionKey", $previousEncryptionKey, "Process")
        docker rm -f $container *> $null
    }
}

$results = @()

$results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
    powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
}

$results += Invoke-Step -Name "Build EntityFrameworkCore" -Script {
    dotnet build src/infrastructure/AICopilot.EntityFrameworkCore/AICopilot.EntityFrameworkCore.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "efcore")
}

$results += Invoke-Step -Name "Build HttpApi" -Script {
    dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj `
        /m:1 /p:UseSharedCompilation=false `
        -o (Join-Path $buildOutputRoot "httpapi")
}

$results += Invoke-Step -Name "Temporary PostgreSQL Migration Smoke" -Script {
    Invoke-TemporaryPostgresMigrationSmoke
}

$results += Invoke-Step -Name "Run P0 P1 P1_5 Focused Backend Tests" -Script {
    dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj `
        --filter "FullyQualifiedName~EnterpriseDataGovernanceP0Tests|FullyQualifiedName~EnterpriseDataGovernanceP1Tests|FullyQualifiedName~EnterpriseDataGovernanceP15Tests|FullyQualifiedName~TextToSqlReadOnlyTests|FullyQualifiedName~SqlGuardrailTests|FullyQualifiedName~ModelProviderReliabilityTests|FullyQualifiedName~MigrationOwnershipTests" `
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
$reportLines.Add("# AICopilot Enterprise Data Governance P1.5 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: $repoRoot")
$reportLines.Add("- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled")
$reportLines.Add("- Test Mode: fake/mock model endpoints and SimulationBusiness data source; real API keys are not required")
$reportLines.Add("- Build Output: $buildOutputRoot")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## P1.5 Stabilization Evidence")
$reportLines.Add("")
$reportLines.Add("- Migrations: DataAnalysis, RAG, and AiGateway migration designer/snapshot metadata are verified by focused backend tests; when Docker/PostgreSQL is available, the acceptance entry also runs an isolated migration and rollback smoke.")
$reportLines.Add("- Scope Guard: git warning noise is suppressed while Cloud/Edge, Real CloudReadonly, shell, dangerous SQL, arbitrary path write, and plaintext secret checks remain active.")
$reportLines.Add("- SimulationBusiness: Small remains the CI/quick profile and Medium remains the local acceptance profile; seed SQL is idempotent and readonly-boundary oriented.")
$reportLines.Add("- Text-to-SQL and Agent: focused tests continue to require SimulationBusiness markers, query hash metadata, readonly guardrails, and deterministic fake/mock behavior.")
$reportLines.Add("- Prompt Policy/RAG/Model Pool: active policy version, hash-only audit metadata, CriticalOverride supplement windows, mock endpoint load, fallback, sticky streaming, and circuit statistics remain covered.")
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
$reportLines.Add("- P1.5 does not connect to a real Cloud database or require real model API keys.")
$reportLines.Add("- If Docker/PostgreSQL is unavailable, rerun the migration smoke against an explicitly approved temporary database before any production-like trial.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Data Governance P1.5 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
