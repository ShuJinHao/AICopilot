[CmdletBinding()]
param(
    [string]$ValidatorPath = (Join-Path $PSScriptRoot 'ValidateAICopilotGovernanceMigration.v1.ps1')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ValidatorPath = (Resolve-Path $ValidatorPath).Path
$TrustedWrapperPath = (Resolve-Path (Join-Path $PSScriptRoot 'InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1')).Path
$TrustedWrapperRelativePath = 'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1'
$SchemaPath = (Resolve-Path (Join-Path $PSScriptRoot 'aicopilot-governance-migration-receipt.schema.json')).Path
$SelfPath = $MyInvocation.MyCommand.Path
$Now = [DateTimeOffset]::UtcNow
$IssuedAtUtc = $Now.AddMinutes(-5).ToString('yyyy-MM-ddTHH:mm:ssZ')
$ExpiresAtUtc = $Now.AddDays(6).ToString('yyyy-MM-ddTHH:mm:ssZ')
$MigrationId = 'AI-TEST-GOV-MIG-SELFTEST-001'
$ReceiptRelativePath = "scripts/tests/baselines/migrations/pending/$MigrationId.json"
$ConsumedRelativePath = "scripts/tests/baselines/migrations/consumed/$MigrationId.json"
$CancelledRelativePath = "scripts/tests/baselines/migrations/cancelled/$MigrationId.json"
$script:Passed = 0
$script:Failed = 0
$script:ExpectedSelfTests = 92
$script:TempRoots = [Collections.Generic.List[string]]::new()

function Write-Utf8File {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $directory = Split-Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function ConvertFrom-TestJsonElement {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $value = [ordered]@{}
            foreach ($property in $Element.EnumerateObject()) {
                $value[$property.Name] = ConvertFrom-TestJsonElement -Element $property.Value
            }
            return [pscustomobject]$value
        }
        ([Text.Json.JsonValueKind]::Array) {
            $items = [Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) {
                $items.Add((ConvertFrom-TestJsonElement -Element $item))
            }
            return ,$items.ToArray()
        }
        ([Text.Json.JsonValueKind]::String) { return $Element.GetString() }
        ([Text.Json.JsonValueKind]::Number) {
            $integer = [long]0
            if ($Element.TryGetInt64([ref]$integer)) { return $integer }
            return $Element.GetDecimal()
        }
        ([Text.Json.JsonValueKind]::True) { return $true }
        ([Text.Json.JsonValueKind]::False) { return $false }
        ([Text.Json.JsonValueKind]::Null) { return $null }
        default { throw "Unsupported JSON value kind '$($Element.ValueKind)' in self-test." }
    }
}

function ConvertFrom-TestJson {
    param([Parameter(Mandatory)][string]$Json)

    $document = [Text.Json.JsonDocument]::Parse($Json)
    try { return ConvertFrom-TestJsonElement -Element $document.RootElement }
    finally { $document.Dispose() }
}

function Get-RequiredWorkflowContent {
    return @'
name: aicopilot-ci

on:
  workflow_dispatch:
  push:
    branches: [main]
    paths:
      - "src/**"
      - "deploy/**"
      - "docs/**"
      - "scripts/**"
      - ".dockerignore"
      - ".gitattributes"
      - "AGENTS.md"
      - "Directory.Build.props"
      - "Directory.Build.targets"
      - "global.json"
      - "AICopilot.slnx"
      - ".github/CODEOWNERS"
      - ".github/workflows/**"
  pull_request: {}

permissions:
  contents: read

jobs:
  migration-validator-selftest:
    runs-on: ubuntu-24.04
    timeout-minutes: 25

    steps:
      - name: Checkout untrusted candidate validator
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7
        with:
          fetch-depth: 0
          persist-credentials: false
          ref: ${{ github.event.pull_request.head.sha || github.sha }}

      - name: Fetch trusted origin/main without persisted credentials
        shell: pwsh
        env:
          AICOPILOT_GITHUB_TOKEN: ${{ github.token }}
        run: |
          if ([string]::IsNullOrWhiteSpace($env:AICOPILOT_GITHUB_TOKEN)) {
            throw 'A read-only GitHub token is required to fetch trusted origin/main.'
          }
          $authorization = [Convert]::ToBase64String(
            [Text.Encoding]::UTF8.GetBytes("x-access-token:$($env:AICOPILOT_GITHUB_TOKEN)"))
          Write-Output "::add-mask::$authorization"
          & git -c "http.extraheader=AUTHORIZATION: basic $authorization" fetch --no-tags origin '+refs/heads/main:refs/remotes/origin/main'
          if ($LASTEXITCODE -ne 0) { throw 'Could not fetch trusted origin/main.' }
          Remove-Item Env:AICOPILOT_GITHUB_TOKEN -ErrorAction SilentlyContinue
          Remove-Variable authorization -ErrorAction SilentlyContinue

      - name: Run base-owned AICopilot governance migration self-tests
        shell: pwsh
        run: |
          # AI-TEST-GOV-MIG-TRUSTED-SELFTEST-V1
          $eventName = '${{ github.event_name }}'
          if ($eventName -eq 'workflow_dispatch') {
            if ('${{ github.ref }}' -ne 'refs/heads/main') {
              throw 'Manual AICopilot governance self-tests must run from refs/heads/main.'
            }
            $trustedBase = (git rev-parse origin/main | Out-String).Trim()
          }
          else {
            $trustedBase = '${{ github.event.pull_request.base.sha || github.event.before }}'
          }
          $candidateValidator = (Resolve-Path 'scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1').Path
          $trustedHarnessAssets = @(
            'scripts/tests/baselines/migrations/TestAICopilotGovernanceMigrationValidator.v1.ps1',
            'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1',
            'scripts/tests/baselines/migrations/aicopilot-governance-migration-receipt.schema.json'
          )
          $temporaryHarnessRoot = Join-Path $env:RUNNER_TEMP "aicopilot-governance-migration-harness-$([Guid]::NewGuid().ToString('N'))"
          try {
            [IO.Directory]::CreateDirectory($temporaryHarnessRoot) | Out-Null
            foreach ($assetPath in $trustedHarnessAssets) {
              $entry = (git ls-tree $trustedBase -- $assetPath | Out-String).Trim()
              $entryPattern = '^100644 blob (?<ObjectId>[0-9a-f]+)\t' + [regex]::Escape($assetPath) + '$'
              if ($LASTEXITCODE -ne 0 -or $entry -notmatch $entryPattern) {
                throw "Trusted base does not contain reviewed mode-100644 harness asset '$assetPath'."
              }
              $objectId = $Matches.ObjectId
              $destination = Join-Path $temporaryHarnessRoot ([IO.Path]::GetFileName($assetPath))
              & git cat-file blob $objectId > $destination
              if ($LASTEXITCODE -ne 0) { throw "Could not extract trusted harness asset '$assetPath'." }
              $extractedObjectId = (git hash-object --no-filters -- $destination | Out-String).Trim()
              if ($LASTEXITCODE -ne 0 -or $extractedObjectId -cne $objectId) {
                throw "Extracted harness asset differs from trusted Git blob '$assetPath'."
              }
            }
            $temporarySelfTest = Join-Path $temporaryHarnessRoot 'TestAICopilotGovernanceMigrationValidator.v1.ps1'
            & pwsh -NoLogo -NoProfile -NonInteractive -File $temporarySelfTest `
              -ValidatorPath $candidateValidator
            if ($LASTEXITCODE -ne 0) {
              throw "Trusted AICopilot governance migration self-tests failed with exit code $LASTEXITCODE."
            }
          }
          finally {
            Remove-Item $temporaryHarnessRoot -Recurse -Force -ErrorAction SilentlyContinue
          }

  build-test:
    runs-on: ubuntu-24.04
    timeout-minutes: 25

    steps:
      - name: Checkout
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7
        with:
          fetch-depth: 0
          persist-credentials: false
          ref: ${{ github.event.pull_request.head.sha || github.sha }}

      - name: Fetch trusted origin/main without persisted credentials
        shell: pwsh
        env:
          AICOPILOT_GITHUB_TOKEN: ${{ github.token }}
        run: |
          if ([string]::IsNullOrWhiteSpace($env:AICOPILOT_GITHUB_TOKEN)) {
            throw 'A read-only GitHub token is required to fetch trusted origin/main.'
          }
          $authorization = [Convert]::ToBase64String(
            [Text.Encoding]::UTF8.GetBytes("x-access-token:$($env:AICOPILOT_GITHUB_TOKEN)"))
          Write-Output "::add-mask::$authorization"
          & git -c "http.extraheader=AUTHORIZATION: basic $authorization" fetch --no-tags origin '+refs/heads/main:refs/remotes/origin/main'
          if ($LASTEXITCODE -ne 0) { throw 'Could not fetch trusted origin/main.' }
          Remove-Item Env:AICOPILOT_GITHUB_TOKEN -ErrorAction SilentlyContinue
          Remove-Variable authorization -ErrorAction SilentlyContinue
      - name: Validate trusted AICopilot governance migration
        shell: pwsh
        run: |
          # AI-TEST-GOV-MIG-TRUSTED-EXECUTOR-V1
          $eventName = '${{ github.event_name }}'
          $candidate = '${{ github.event.pull_request.head.sha || github.sha }}'
          if ($eventName -eq 'workflow_dispatch') {
            if ('${{ github.ref }}' -ne 'refs/heads/main') {
              throw 'Manual AICopilot governance validation must run from refs/heads/main.'
            }
            $trustedBase = (git rev-parse origin/main | Out-String).Trim()
            $relationship = 'HeadAncestorOfBase'
          }
          else {
            $trustedBase = '${{ github.event.pull_request.base.sha || github.event.before }}'
            $relationship = 'BaseAncestorOfHead'
          }
          $trustedWrapperPath = 'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1'
          $entry = (git ls-tree $trustedBase -- $trustedWrapperPath | Out-String).Trim()
          $entryPattern = '^100644 blob (?<ObjectId>[0-9a-f]+)\t' + [regex]::Escape($trustedWrapperPath) + '$'
          if ($LASTEXITCODE -ne 0 -or $entry -notmatch $entryPattern) {
            throw 'Trusted base does not contain the reviewed AICopilot migration wrapper.'
          }
          $temporaryWrapper = Join-Path $env:RUNNER_TEMP 'aicopilot-governance-migration-wrapper.ps1'
          try {
            & git cat-file blob $Matches.ObjectId > $temporaryWrapper
            if ($LASTEXITCODE -ne 0) { throw 'Could not extract the trusted AICopilot migration wrapper.' }
            $extractedObjectId = (git hash-object --no-filters -- $temporaryWrapper | Out-String).Trim()
            if ($LASTEXITCODE -ne 0 -or $extractedObjectId -cne $Matches.ObjectId) {
              throw 'Extracted AICopilot migration wrapper differs from the trusted Git blob.'
            }
            & pwsh -NoLogo -NoProfile -NonInteractive -File $temporaryWrapper `
              -RepositoryRoot . `
              -TrustedBaseRevision $trustedBase `
              -CandidateRevision $candidate `
              -AnchorRelationship $relationship
            if ($LASTEXITCODE -ne 0) {
              throw "Trusted AICopilot migration validation failed with exit code $LASTEXITCODE."
            }
          }
          finally {
            Remove-Item $temporaryWrapper -Force -ErrorAction SilentlyContinue
          }
      - name: Run AICopilot test governance self-tests
        shell: pwsh
        run: |
          ./scripts/tests/TestAICopilotTestGovernanceBehavior.ps1
          ./scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release

      - name: Setup .NET
        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5
        with:
          dotnet-version: "10.0.301"

      - name: Setup Node
        uses: actions/setup-node@48b55a011bda9f5d6aeb4c2d9c7362e8dae4041e # v6
        with:
          node-version: "22"
          cache: "npm"
          cache-dependency-path: "src/vues/AICopilot.Web/package-lock.json"

      - name: Verify .NET SDK
        run: test "$(dotnet --version)" = "10.0.301"

      - name: Restore AICopilot solution
        run: dotnet restore AICopilot.slnx

      - name: Restore web dependencies
        working-directory: src/vues/AICopilot.Web
        run: npm ci

      - name: Enforce incremental deployment policy
        shell: pwsh
        run: ./deploy/enterprise-ai/tests/TestDeploymentPolicy.ps1

      - name: Require Linux Docker runtime
        run: docker info

      - name: Build AICopilot solution
        run: dotnet build AICopilot.slnx -c Release --no-restore

      - name: Validate AICopilot test repository and discovery
        shell: pwsh
        run: |
          ./scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateRepository -Configuration Release
          ./scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateDiscovery -Configuration Release

      - name: Run architecture tests
        run: dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=architecture.trx" --results-directory artifacts/test-results

      - name: Run deterministic AI eval tests
        run: dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=ai-eval.trx" --results-directory artifacts/test-results

      - name: Run backend tests
        run: dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=backend.trx" --results-directory artifacts/test-results

      - name: Run deployment behavior tests
        shell: bash
        run: |
          set -euo pipefail
          bash deploy/enterprise-ai/tests/deployment-behavior.sh 2>&1 | tee artifacts/test-results/deployment-behavior.log

      - name: Test and build web app
        working-directory: src/vues/AICopilot.Web
        run: |
          npm run test:unit -- --reporter=json --outputFile=../../../artifacts/test-results/vitest.json
          npm run build

      - name: Reconcile required test results
        shell: pwsh
        run: |
          $expected = @{
            'architecture.trx' = 91
            'ai-eval.trx' = 6
            'backend.trx' = 924
          }
          foreach ($entry in $expected.GetEnumerator()) {
            $path = Join-Path 'artifacts/test-results' $entry.Key
            if (-not (Test-Path $path -PathType Leaf)) {
              throw "Missing required TRX: $path"
            }
            [xml]$trx = Get-Content $path -Raw
            $counters = $trx.TestRun.ResultSummary.Counters
            $total = [int]$counters.total
            $passed = [int]$counters.passed
            $failed = [int]$counters.failed
            $notExecuted = [int]$counters.notExecuted
            if ($total -ne $entry.Value -or $passed -ne $entry.Value -or $failed -ne 0 -or $notExecuted -ne 0) {
              throw "$($entry.Key) reconciliation failed: expected=$($entry.Value), total=$total, passed=$passed, failed=$failed, notExecuted=$notExecuted"
            }
          }

          $vitestPath = 'artifacts/test-results/vitest.json'
          if (-not (Test-Path $vitestPath -PathType Leaf)) {
            throw "Missing required Vitest result: $vitestPath"
          }
          $vitest = Get-Content $vitestPath -Raw | ConvertFrom-Json
          if (-not [bool]$vitest.success -or
              [int]$vitest.numTotalTests -ne 184 -or
              [int]$vitest.numPassedTests -ne 184 -or
              [int]$vitest.numFailedTests -ne 0 -or
              [int]$vitest.numPendingTests -ne 0 -or
              @($vitest.testResults).Count -ne 31) {
            throw "Vitest reconciliation failed: files=$(@($vitest.testResults).Count), total=$($vitest.numTotalTests), passed=$($vitest.numPassedTests), failed=$($vitest.numFailedTests), pending=$($vitest.numPendingTests)"
          }

          $deploymentPath = 'artifacts/test-results/deployment-behavior.log'
          if (-not (Test-Path $deploymentPath -PathType Leaf)) {
            throw "Missing required deployment behavior log: $deploymentPath"
          }
          $deploymentLines = @(Get-Content $deploymentPath)
          $deploymentCases = @($deploymentLines | Where-Object { $_ -match '^TEST ' }).Count
          if ($deploymentCases -ne 33 -or
              @($deploymentLines | Where-Object { $_ -eq 'NON_PRODUCTION_MECHANISM_TEST productionEligible=false result=passed' }).Count -ne 1 -or
              @($deploymentLines | Where-Object { $_ -eq 'All AICopilot deployment behavior tests passed.' }).Count -ne 1) {
            throw "Deployment behavior reconciliation failed: cases=$deploymentCases"
          }

      - name: Upload required test evidence
        if: always()
        uses: actions/upload-artifact@b7c566a772e6b6bfb58ed0dc250532a479d7789f # v6
        with:
          name: aicopilot-required-test-results
          path: artifacts/test-results/**
          if-no-files-found: warn

  required-final:
    needs:
      - migration-validator-selftest
      - build-test
    if: ${{ always() }}
    runs-on: ubuntu-24.04
    timeout-minutes: 1

    steps:
      - name: Require successful AICopilot self-test and build-test
        shell: bash
        env:
          MIGRATION_SELFTEST_RESULT: ${{ needs.migration-validator-selftest.result }}
          BUILD_TEST_RESULT: ${{ needs.build-test.result }}
        run: |
          test "$MIGRATION_SELFTEST_RESULT" = "success"
          test "$BUILD_TEST_RESULT" = "success"
'@
}
function Invoke-Git {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$Capture
    )

    $output = & git -C $Root @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in ${Root}: $($output -join [Environment]::NewLine)"
    }
    if ($Capture) { return ($output | Out-String).Trim() }
}

function Export-TestGitBlob {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ObjectId,
        [Parameter(Mandatory)][string]$Destination
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @('-C', $Root, 'cat-file', 'blob', $ObjectId)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw 'Could not start trusted wrapper extraction.' }
    $errorTask = $process.StandardError.ReadToEndAsync()
    try {
        $stream = [IO.File]::Open(
            $Destination,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        try { $process.StandardOutput.BaseStream.CopyTo($stream) }
        finally { $stream.Dispose() }
        $process.WaitForExit()
        $errorText = $errorTask.GetAwaiter().GetResult().Trim()
        if ($process.ExitCode -ne 0) {
            throw "Trusted wrapper extraction failed: $errorText"
        }
    }
    finally {
        $process.Dispose()
    }
}

function Commit-All {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Message
    )

    Invoke-Git -Root $Root -Arguments @('add', '--all')
    Invoke-Git -Root $Root -Arguments @('commit', '--quiet', '-m', $Message)
    return Invoke-Git -Root $Root -Arguments @('rev-parse', 'HEAD') -Capture
}

function New-BaseFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) "aicopilot-governance-migration-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $script:TempRoots.Add($root)
    Invoke-Git -Root $root -Arguments @('init', '--quiet', '--initial-branch=main', '--object-format=sha1')
    Invoke-Git -Root $root -Arguments @('config', 'user.name', 'AICopilot Governance Migration Self Test')
    Invoke-Git -Root $root -Arguments @('config', 'user.email', 'aicopilot-governance-migration@example.invalid')
    Invoke-Git -Root $root -Arguments @('config', 'commit.gpgsign', 'false')
    Invoke-Git -Root $root -Arguments @('config', 'core.autocrlf', 'false')
    Invoke-Git -Root $root -Arguments @('config', 'core.safecrlf', 'true')
    $emptyHooks = Join-Path $root '.empty-git-hooks'
    [IO.Directory]::CreateDirectory($emptyHooks) | Out-Null
    Invoke-Git -Root $root -Arguments @('config', 'core.hooksPath', $emptyHooks)

    Write-Utf8File -Path (Join-Path $root 'AICopilot.slnx') -Content @'
<Solution>
  <Project Path="src/tests/Sample.Tests/Sample.Tests.csproj" />
</Solution>
'@
    Write-Utf8File -Path (Join-Path $root 'src/tests/Sample.Tests/Sample.Tests.csproj') -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
'@
    Write-Utf8File -Path (Join-Path $root 'src/tests/Sample.Tests/SampleTests.cs') -Content "public sealed class SampleTests { }`n"
    Write-Utf8File -Path (Join-Path $root '.github/workflows/aicopilot-ci.yml') -Content "name: old`n"
    Write-Utf8File -Path (Join-Path $root 'src/vues/AICopilot.Web/tests/unit/sample.spec.ts') -Content "export const unitCase = true`n"
    Write-Utf8File -Path (Join-Path $root 'src/vues/AICopilot.Web/tests/smoke/acceptance.spec.ts') -Content "export const smokeCase = true`n"
    Write-Utf8File -Path (Join-Path $root 'src/vues/AICopilot.Web/tests/smoke/start-smoke.mjs') -Content "export const startSmoke = true`n"
    Write-Utf8File -Path (Join-Path $root 'src/vues/AICopilot.Web/package.json') -Content "{}`n"
    Write-Utf8File -Path (Join-Path $root 'src/vues/AICopilot.Web/package-lock.json') -Content "{}`n"
    Write-Utf8File -Path (Join-Path $root 'src/vues/AICopilot.Web/vitest.config.ts') -Content "export default {}`n"
    Write-Utf8File -Path (Join-Path $root 'src/vues/AICopilot.Web/playwright.smoke.config.ts') -Content "export default {}`n"
    Write-Utf8File -Path (Join-Path $root 'deploy/enterprise-ai/tests/deployment-behavior.sh') -Content "#!/usr/bin/env bash`n"
    Write-Utf8File -Path (Join-Path $root 'deploy/windows/tests/TestDeployment.ps1') -Content "Write-Host pass`n"
    Write-Utf8File -Path (Join-Path $root 'scripts/tests/TestAICopilotTestGovernancePolicy.ps1') -Content "# reviewed governance policy`n"
    Write-Utf8File -Path (Join-Path $root 'src/App/Program.cs') -Content "internal static class Program { }`n"
    Write-Utf8File -Path (Join-Path $root 'scripts/tests/baselines/aicopilot-test-governance.baseline.json') -Content @'
{
  "schemaVersion": "1.0",
  "ruleId": "AI-TEST-GOV-001",
  "projects": [
    {
      "projectPath": "src/tests/Sample.Tests/Sample.Tests.csproj",
      "baselineDeclarations": 1,
      "baselineExecutionTemplates": 1,
      "baselineProjectedCases": 1,
      "baselineRunnerCases": 1
    }
  ]
}
'@
    $validatorTarget = Join-Path $root 'scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1'
    $wrapperTarget = Join-Path $root 'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1'
    $selfTarget = Join-Path $root 'scripts/tests/baselines/migrations/TestAICopilotGovernanceMigrationValidator.v1.ps1'
    $schemaTarget = Join-Path $root 'scripts/tests/baselines/migrations/aicopilot-governance-migration-receipt.schema.json'
    [IO.Directory]::CreateDirectory((Split-Path $validatorTarget -Parent)) | Out-Null
    [IO.File]::Copy($ValidatorPath, $validatorTarget, $true)
    [IO.File]::Copy($TrustedWrapperPath, $wrapperTarget, $true)
    [IO.File]::Copy($SelfPath, $selfTarget, $true)
    [IO.File]::Copy($SchemaPath, $schemaTarget, $true)

    $base = Commit-All -Root $root -Message 'base'
    return [pscustomobject]@{ Root = $root; Base = $base }
}

function New-TemplateCandidate {
    param([Parameter(Mandatory)][object]$Fixture)

    Write-Utf8File -Path (Join-Path $Fixture.Root '.github/workflows/aicopilot-ci.yml') -Content "$(Get-RequiredWorkflowContent)`n"
    Write-Utf8File -Path (Join-Path $Fixture.Root 'src/App/Program.cs') -Content "internal static class Program { internal const int Version = 2; }`n"
    $template = Commit-All -Root $Fixture.Root -Message 'template candidate'
    $Fixture | Add-Member -NotePropertyName Template -NotePropertyValue $template -Force
    return $Fixture
}

function New-ReceiptJson {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [int]$ExpectedTargetWorkflowFiles = 1,
        [object]$DescribeResult
    )

    if ($null -eq $DescribeResult) {
        $DescribeResult = Invoke-DescribeResult `
            -Fixture $Fixture `
            -RuleIdsCsv 'AI-ARCH-001'
    }
    if ($DescribeResult.ExitCode -ne 0) {
        throw "Describe failed: $($DescribeResult.Output)"
    }
    $json = ([string]$DescribeResult.Output).Trim()
    $receipt = ConvertFrom-TestJson -Json $json
    foreach ($stateName in @('source', 'target')) {
        $counts = $receipt.$stateName.counts
        $expectedWorkflowFiles = if ($stateName -ceq 'target') {
            $ExpectedTargetWorkflowFiles
        } else {
            1
        }
        $expected = [ordered]@{
            repositoryProjects = 1
            testProjects = 1
            testSourceFiles = 1
            declarations = 1
            executionTemplates = 1
            projectedCases = 1
            runnerCases = 1
            vitestSourceFiles = 1
            playwrightSourceFiles = 2
            deploymentTestAssets = 2
            workflowFiles = $expectedWorkflowFiles
        }
        foreach ($entry in $expected.GetEnumerator()) {
            if ([long]$counts.($entry.Key) -ne [long]$entry.Value) {
                throw "Describe $stateName count '$($entry.Key)' was $($counts.($entry.Key)); expected $($entry.Value)."
            }
        }
    }
    return $json
}

function Invoke-DescribeResult {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][string]$RuleIdsCsv,
        [string]$Relationship = 'BaseAncestorOfHead',
        [string]$OmitArgument
    )

    $arguments = @(
        '-File', $ValidatorPath,
        '-Mode', 'Describe',
        '-RepositoryRoot', $Fixture.Root,
        '-TrustedBaseRevision', $Fixture.Base,
        '-CandidateRevision', $Fixture.Template,
        '-AnchorRelationship', $Relationship,
        '-IssuedAtUtc', $IssuedAtUtc,
        '-ExpiresAtUtc', $ExpiresAtUtc)
    $describeArguments = [ordered]@{
        MigrationId = $MigrationId
        RuleIdsCsv = $RuleIdsCsv
        Owner = 'AI.Architecture'
        ApprovedBy = 'ShuJinHao'
        Reason = 'Self-test receipt for one exact workflow migration.'
    }
    foreach ($entry in $describeArguments.GetEnumerator()) {
        if ([string]$entry.Key -ceq $OmitArgument) { continue }
        $arguments += "-$($entry.Key)"
        $arguments += [string]$entry.Value
    }
    return Invoke-PowerShellResult -Arguments $arguments
}

function New-AuthorizationFixture {
    param(
        [scriptblock]$MutateReceipt,
        [scriptblock]$MutateTemplate,
        [switch]$AddAuthorizationNoise,
        [switch]$AddSecondPending,
        [int]$ExpectedTargetWorkflowFiles = 1
    )

    $fixture = New-TemplateCandidate -Fixture (New-BaseFixture)
    if ($null -ne $MutateTemplate) {
        Invoke-Git -Root $fixture.Root -Arguments @('checkout', '--quiet', $fixture.Template)
        & $MutateTemplate $fixture.Root
        Invoke-Git -Root $fixture.Root -Arguments @('add', '--all')
        Invoke-Git -Root $fixture.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
        $fixture.Template = Invoke-Git -Root $fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    }
    $describeResult = Invoke-DescribeResult `
        -Fixture $fixture `
        -RuleIdsCsv 'AI-ARCH-001'
    if ($describeResult.ExitCode -ne 0) {
        if ($null -eq $MutateTemplate -or
            $describeResult.Output -notmatch 'AI-TEST-GOV-MIG-001-TRUST') {
            throw "Describe failed: $($describeResult.Output)"
        }

        # Static workflow closure now runs before a receipt can be issued. Preserve
        # that exact rejection instead of fabricating a receipt for consumption.
        $fixture | Add-Member -NotePropertyName DescribeRejected -NotePropertyValue $true -Force
        $fixture | Add-Member -NotePropertyName DescribeResult -NotePropertyValue $describeResult -Force
        $fixture | Add-Member -NotePropertyName Authorization -NotePropertyValue $fixture.Base -Force
        $fixture | Add-Member -NotePropertyName Candidate -NotePropertyValue $fixture.Template -Force
        return $fixture
    }
    $receiptJson = New-ReceiptJson `
        -Fixture $fixture `
        -ExpectedTargetWorkflowFiles $ExpectedTargetWorkflowFiles `
        -DescribeResult $describeResult
    Invoke-Git -Root $fixture.Root -Arguments @('checkout', '--quiet', $fixture.Base)
    if ($null -ne $MutateReceipt) {
        $receiptJson = & $MutateReceipt $receiptJson
    }
    Write-Utf8File -Path (Join-Path $fixture.Root $ReceiptRelativePath) -Content "$receiptJson`n"
    if ($AddAuthorizationNoise) {
        Write-Utf8File -Path (Join-Path $fixture.Root 'docs/noise.md') -Content "authorization noise`n"
    }
    if ($AddSecondPending) {
        $second = $receiptJson.Replace($MigrationId, 'AI-TEST-GOV-MIG-SELFTEST-002')
        Write-Utf8File -Path (Join-Path $fixture.Root 'scripts/tests/baselines/migrations/pending/AI-TEST-GOV-MIG-SELFTEST-002.json') -Content "$second`n"
    }
    $authorization = Commit-All -Root $fixture.Root -Message 'authorize migration'
    $fixture | Add-Member -NotePropertyName Authorization -NotePropertyValue $authorization -Force
    return $fixture
}

function New-TrustTemplateFixture {
    $fixture = New-BaseFixture
    Write-Utf8File -Path (Join-Path $fixture.Root '.github/workflows/aicopilot-ci.yml') -Content "$(Get-RequiredWorkflowContent)`n"
    $fixture.Base = Commit-All -Root $fixture.Root -Message 'integrated trusted workflow base'
    [IO.File]::AppendAllText(
        (Join-Path $fixture.Root 'scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1'),
        "# reviewed trust upgrade candidate`n",
        [Text.UTF8Encoding]::new($false))
    $template = Commit-All -Root $fixture.Root -Message 'trust upgrade template'
    $fixture | Add-Member -NotePropertyName Template -NotePropertyValue $template -Force
    return $fixture
}

function New-TrustUpgradeFixture {
    $fixture = New-TrustTemplateFixture
    $result = Invoke-DescribeResult `
        -Fixture $fixture `
        -RuleIdsCsv 'AI-TEST-GOV-TRUST-UPGRADE-001'
    if ($result.ExitCode -ne 0) {
        throw "Trust upgrade Describe failed: $($result.Output)"
    }
    Invoke-Git -Root $fixture.Root -Arguments @('checkout', '--quiet', $fixture.Base)
    Write-Utf8File -Path (Join-Path $fixture.Root $ReceiptRelativePath) -Content "$($result.Output)`n"
    $authorization = Commit-All -Root $fixture.Root -Message 'authorize trust upgrade'
    $fixture | Add-Member -NotePropertyName Authorization -NotePropertyValue $authorization -Force
    return $fixture
}

function Complete-Cancellation {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [switch]$AlterCancelled,
        [switch]$AddExtraPath,
        [switch]$ExecutableMode
    )

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Fixture.Authorization)
    $pending = Join-Path $Fixture.Root $ReceiptRelativePath
    $cancelled = Join-Path $Fixture.Root $CancelledRelativePath
    [IO.Directory]::CreateDirectory((Split-Path $cancelled -Parent)) | Out-Null
    [IO.File]::Move($pending, $cancelled)
    if ($AlterCancelled) {
        [IO.File]::AppendAllText($cancelled, " `n", [Text.UTF8Encoding]::new($false))
    }
    if ($AddExtraPath) {
        Write-Utf8File -Path (Join-Path $Fixture.Root 'docs/cancellation-noise.md') -Content "noise`n"
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('add', '--all')
    if ($ExecutableMode) {
        Invoke-Git -Root $Fixture.Root -Arguments @('update-index', '--chmod=+x', '--', $CancelledRelativePath)
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('commit', '--quiet', '-m', 'cancel migration')
    $candidate = Invoke-Git -Root $Fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    $Fixture | Add-Member -NotePropertyName Candidate -NotePropertyValue $candidate -Force
    return $Fixture
}

function Complete-Candidate {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [switch]$SkipMove,
        [switch]$AlterConsumed,
        [switch]$AddExtraPath,
        [switch]$RemoveExpectedPath,
        [switch]$ModifyCandidateValidator
    )

    if ($Fixture.PSObject.Properties['DescribeRejected'] -and $Fixture.DescribeRejected) {
        return $Fixture
    }

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Fixture.Authorization)
    Invoke-Git -Root $Fixture.Root -Arguments @('cherry-pick', '--quiet', $Fixture.Template)
    if ($RemoveExpectedPath) {
        Invoke-Git -Root $Fixture.Root -Arguments @('checkout', "$($Fixture.Authorization)^", '--', '.github/workflows/aicopilot-ci.yml')
    }
    if (-not $SkipMove) {
        $pending = Join-Path $Fixture.Root $ReceiptRelativePath
        $consumed = Join-Path $Fixture.Root $ConsumedRelativePath
        [IO.Directory]::CreateDirectory((Split-Path $consumed -Parent)) | Out-Null
        [IO.File]::Move($pending, $consumed)
        if ($AlterConsumed) {
            [IO.File]::AppendAllText($consumed, " `n", [Text.UTF8Encoding]::new($false))
        }
    }
    if ($AddExtraPath) {
        Write-Utf8File -Path (Join-Path $Fixture.Root 'src/App/Extra.cs') -Content "internal sealed class Extra { }`n"
    }
    if ($ModifyCandidateValidator) {
        [IO.File]::AppendAllText(
            (Join-Path $Fixture.Root 'scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1'),
            "# candidate bypass`n",
            [Text.UTF8Encoding]::new($false))
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('add', '--all')
    Invoke-Git -Root $Fixture.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $candidate = Invoke-Git -Root $Fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    $Fixture | Add-Member -NotePropertyName Candidate -NotePropertyValue $candidate -Force
    return $Fixture
}

function Invoke-Validation {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][string]$Base,
        [Parameter(Mandatory)][string]$Candidate,
        [string]$Relationship = 'BaseAncestorOfHead'
    )

    if ($Fixture.PSObject.Properties['DescribeRejected'] -and $Fixture.DescribeRejected) {
        return $Fixture.DescribeResult
    }

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Candidate)
    $executorPath = $TrustedWrapperPath
    $temporaryWrapper = $null
    if ($Base -match '^[0-9A-Fa-f]{40}$') {
        & git -C $Fixture.Root cat-file -e "$Base^{commit}" 2>$null
        if ($LASTEXITCODE -eq 0) {
            $entry = Invoke-Git `
                -Root $Fixture.Root `
                -Arguments @('ls-tree', $Base, '--', $TrustedWrapperRelativePath) `
                -Capture
            $entryMatch = [regex]::Match(
                $entry,
                '^100644 blob (?<ObjectId>[0-9a-f]+)\t' +
                    [regex]::Escape($TrustedWrapperRelativePath) + '$')
            if (-not $entryMatch.Success) {
                throw 'Trusted self-test base does not contain the reviewed wrapper.'
            }
            $temporaryWrapper = Join-Path ([IO.Path]::GetTempPath()) (
                "$([Guid]::NewGuid().ToString('N')).trusted-wrapper.ps1")
            Export-TestGitBlob `
                -Root $Fixture.Root `
                -ObjectId $entryMatch.Groups['ObjectId'].Value `
                -Destination $temporaryWrapper
            $actualObjectId = Invoke-Git `
                -Root $Fixture.Root `
                -Arguments @('hash-object', '--no-filters', '--', $temporaryWrapper) `
                -Capture
            if ($actualObjectId -cne $entryMatch.Groups['ObjectId'].Value) {
                throw 'Extracted self-test wrapper differs from its trusted Git blob.'
            }
            $executorPath = $temporaryWrapper
        }
    }

    try {
        $arguments = @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-File', $executorPath,
            '-RepositoryRoot', $Fixture.Root,
            '-TrustedBaseRevision', $Base,
            '-CandidateRevision', $Candidate,
            '-AnchorRelationship', $Relationship)
        $output = & pwsh @arguments 2>&1
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = ($output | Out-String).Trim()
        }
    }
    finally {
        if ($null -ne $temporaryWrapper) {
            Remove-Item $temporaryWrapper -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-PowerShellResult {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & pwsh -NoLogo -NoProfile -NonInteractive @Arguments 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Amend-AuthorizationReceiptBytes {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][byte[]]$Bytes,
        [switch]$ExecutableMode
    )

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Fixture.Authorization)
    [IO.File]::WriteAllBytes((Join-Path $Fixture.Root $ReceiptRelativePath), $Bytes)
    Invoke-Git -Root $Fixture.Root -Arguments @('add', '--all')
    if ($ExecutableMode) {
        Invoke-Git -Root $Fixture.Root -Arguments @('update-index', '--chmod=+x', '--', $ReceiptRelativePath)
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $Fixture.Authorization = Invoke-Git -Root $Fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    return $Fixture
}

function Assert-Pass {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action,
        [string]$ExpectedText
    )

    try {
        $result = & $Action
        if ($result.ExitCode -ne 0) {
            throw "expected success but exit=$($result.ExitCode): $($result.Output)"
        }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedText) -and
            $result.Output -notmatch [regex]::Escape($ExpectedText)) {
            throw "expected output '$ExpectedText' but got: $($result.Output)"
        }
        $script:Passed++
        Write-Host "PASS $Name"
    }
    catch {
        $script:Failed++
        Write-Host "FAIL $Name -- $($_.Exception.Message)"
    }
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ExpectedCode,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    try {
        $result = & $Action
        if ($result.ExitCode -eq 0) {
            throw "expected rejection but validator passed: $($result.Output)"
        }
        if ($result.Output -notmatch [regex]::Escape("AI-TEST-GOV-MIG-001-$ExpectedCode")) {
            throw "expected $ExpectedCode but got: $($result.Output)"
        }
        $script:Passed++
        Write-Host "PASS $Name (rejected $ExpectedCode)"
    }
    catch {
        $script:Failed++
        Write-Host "FAIL $Name -- $($_.Exception.Message)"
    }
}

function Assert-OrdinalSequenceEqual {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Actual,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Expected,
        [Parameter(Mandatory)][string]$Location
    )

    if ($Actual.Count -ne $Expected.Count) {
        throw "$Location count differs: actual=$($Actual.Count) expected=$($Expected.Count)."
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ([string]$Actual[$index] -cne $Expected[$index]) {
            throw "$Location differs at ordinal index ${index}: actual='$($Actual[$index])' expected='$($Expected[$index])'."
        }
    }
}

function Get-LiteralArrayAssignmentValues {
    param(
        [Parameter(Mandatory)][Management.Automation.Language.ScriptBlockAst]$Ast,
        [Parameter(Mandatory)][string]$VariablePath
    )

    $assignments = @($Ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -ceq $VariablePath
    }, $true))
    if ($assignments.Count -ne 1) {
        throw "Expected one literal assignment for '$VariablePath'; found $($assignments.Count)."
    }
    $right = $assignments[0].Right
    $strings = @($right.FindAll({
        param($node)
        $node -is [Management.Automation.Language.StringConstantExpressionAst]
    }, $true) | ForEach-Object { $_.Value })
    if ($strings.Count -eq 0) {
        throw "Literal assignment '$VariablePath' contains no string values."
    }
    return $strings
}

function Get-LiteralScalarAssignmentValue {
    param(
        [Parameter(Mandatory)][Management.Automation.Language.ScriptBlockAst]$Ast,
        [Parameter(Mandatory)][string]$VariablePath
    )

    $assignments = @($Ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -ceq $VariablePath
    }, $true))
    if ($assignments.Count -ne 1) {
        throw "Expected one literal assignment for '$VariablePath'; found $($assignments.Count)."
    }
    $right = $assignments[0].Right
    if ($right -isnot [Management.Automation.Language.CommandExpressionAst] -or
        $right.Expression -isnot [Management.Automation.Language.ConstantExpressionAst]) {
        throw "Literal assignment '$VariablePath' is not one scalar constant."
    }
    return $right.Expression.Value
}

try {
    Assert-Pass -Name 'reference schema locks reviewed receipt constants' -Action {
        $schema = ConvertFrom-TestJson -Json ([IO.File]::ReadAllText($SchemaPath))
        $required = @($schema.required)
        $expectedRequired = @(
            'schemaVersion', 'ruleId', 'migrationId', 'issuedAgainstRevision',
            'issuedAtUtc', 'expiresAtUtc', 'owner', 'approvedBy', 'reason',
            'ruleIds', 'source', 'target', 'projectChanges', 'changes')
        $expectedOwners = @(
            'AI.Architecture',
            'AI.AgentWorkflow',
            'AI.Persistence',
            'AI.Tests',
            'AI.Deployment',
            'AI.Security',
            'AI.Web'
        )
        $expectedApprovers = @('ShuJinHao')
        $expectedRuleIds = @(
            'AI-ARCH-001',
            'AI-EVAL-001',
            'AI-TEST-001',
            'AI-TEST-002',
            'AI-TEST-003',
            'AI-TEST-004',
            'AI-TEST-DUP-001',
            'AI-TEST-GOV-001',
            'AI-TEST-UI-001',
            'AI-TEST-GOV-TRUST-UPGRADE-001'
        )
        $expectedCountFields = @(
            'repositoryProjects',
            'testProjects',
            'testSourceFiles',
            'declarations',
            'executionTemplates',
            'projectedCases',
            'runnerCases',
            'vitestSourceFiles',
            'playwrightSourceFiles',
            'deploymentTestAssets',
            'workflowFiles'
        )
        $expectedDescribeRequiredArguments = @(
            'MigrationId',
            'RuleIdsCsv',
            'Owner',
            'ApprovedBy',
            'Reason'
        )
        $expectedReservedWorkflowTrustTokens = @(
            'aicopilot-ci',
            'migration-validator-selftest',
            'build-test',
            'required-final',
            'aicopilot-ci/required-final',
            'aicopilot-ci / required-final',
            'AI-TEST-GOV-MIG-TRUSTED-EXECUTOR-V1',
            'AI-TEST-GOV-MIG-TRUSTED-SELFTEST-V1',
            'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1',
            'scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1',
            'scripts/tests/baselines/migrations/TestAICopilotGovernanceMigrationValidator.v1.ps1',
            'scripts/tests/baselines/migrations/aicopilot-governance-migration-receipt.schema.json'
        )
        $expectedNonCanonicalIdentityGrammar = [ordered]@{
            'script:NonCanonicalWorkflowTopLevelKeyPattern' = '^(?<Key>[A-Za-z][A-Za-z0-9_-]*):(?:[ \t].*)?$'
            'script:NonCanonicalWorkflowNamePattern' = '^name: (?<Name>[A-Za-z0-9][A-Za-z0-9 ._()/+-]*)$'
            'script:NonCanonicalWorkflowJobsHeader' = 'jobs:'
            'script:NonCanonicalWorkflowJobIdPattern' = '^  (?<JobId>[A-Za-z_][A-Za-z0-9_-]*):$'
            'script:NonCanonicalWorkflowDirectJobPropertyPattern' = '^    (?<Key>[A-Za-z][A-Za-z0-9_-]*):(?:[ \t].*)?$'
            'script:NonCanonicalWorkflowDirectJobNamePattern' = '^    name: (?<Name>[A-Za-z0-9][A-Za-z0-9 ._()/+-]*)$'
        }
        Assert-OrdinalSequenceEqual -Actual $required -Expected $expectedRequired -Location 'receipt required fields'
        Assert-OrdinalSequenceEqual `
            -Actual @($schema.properties.owner.enum) `
            -Expected $expectedOwners `
            -Location 'owner registry'
        Assert-OrdinalSequenceEqual `
            -Actual @($schema.properties.ruleIds.items.enum) `
            -Expected $expectedRuleIds `
            -Location 'rule ID registry'
        Assert-OrdinalSequenceEqual `
            -Actual @([string]$schema.properties.approvedBy.const) `
            -Expected $expectedApprovers `
            -Location 'approver registry'
        Assert-OrdinalSequenceEqual `
            -Actual @($schema.allOf[0].then.properties.ruleIds.const) `
            -Expected @('AI-TEST-GOV-TRUST-UPGRADE-001') `
            -Location 'trust upgrade singleton schema'
        Assert-OrdinalSequenceEqual `
            -Actual @($schema.'$defs'.counts.required) `
            -Expected $expectedCountFields `
            -Location 'count required fields'
        Assert-OrdinalSequenceEqual `
            -Actual @($schema.'$defs'.counts.properties.PSObject.Properties.Name) `
            -Expected $expectedCountFields `
            -Location 'count property registry'
        $tokens = $null
        $parseErrors = $null
        $validatorAst = [Management.Automation.Language.Parser]::ParseFile(
            $ValidatorPath,
            [ref]$tokens,
            [ref]$parseErrors)
        if (@($parseErrors).Count -ne 0) {
            throw "Candidate validator AST parse failed: $(@($parseErrors.Message) -join '; ')"
        }
        Assert-OrdinalSequenceEqual `
            -Actual @(Get-LiteralArrayAssignmentValues -Ast $validatorAst -VariablePath 'script:ApprovedOwners') `
            -Expected $expectedOwners `
            -Location 'runtime owner registry'
        Assert-OrdinalSequenceEqual `
            -Actual @(Get-LiteralArrayAssignmentValues -Ast $validatorAst -VariablePath 'script:ApprovedGovernedRuleIds') `
            -Expected $expectedRuleIds `
            -Location 'runtime rule ID registry'
        Assert-OrdinalSequenceEqual `
            -Actual @(Get-LiteralArrayAssignmentValues -Ast $validatorAst -VariablePath 'script:ApprovedApprovers') `
            -Expected $expectedApprovers `
            -Location 'runtime approver registry'
        Assert-OrdinalSequenceEqual `
            -Actual @(Get-LiteralArrayAssignmentValues `
                -Ast $validatorAst `
                -VariablePath 'script:DescribeRequiredArgumentNames') `
            -Expected $expectedDescribeRequiredArguments `
            -Location 'runtime Describe required-argument registry'
        Assert-OrdinalSequenceEqual `
            -Actual @(Get-LiteralArrayAssignmentValues `
                -Ast $validatorAst `
                -VariablePath 'script:ReservedWorkflowTrustReferenceTokens') `
            -Expected $expectedReservedWorkflowTrustTokens `
            -Location 'runtime reserved workflow trust-token registry'
        foreach ($grammarEntry in $expectedNonCanonicalIdentityGrammar.GetEnumerator()) {
            $actualGrammarValue = [string](Get-LiteralScalarAssignmentValue `
                -Ast $validatorAst `
                -VariablePath ([string]$grammarEntry.Key))
            if ($actualGrammarValue -cne [string]$grammarEntry.Value) {
                throw "runtime non-canonical workflow identity grammar '$($grammarEntry.Key)' differs from its reviewed literal."
            }
        }
        if ([string](Get-LiteralScalarAssignmentValue `
                -Ast $validatorAst `
                -VariablePath 'script:ReceiptSchemaVersion') -cne
                [string]$schema.properties.schemaVersion.const -or
            [string](Get-LiteralScalarAssignmentValue `
                -Ast $validatorAst `
                -VariablePath 'script:RuleId') -cne
                [string]$schema.properties.ruleId.const -or
            [long](Get-LiteralScalarAssignmentValue `
                -Ast $validatorAst `
                -VariablePath 'script:MaximumReceiptChanges') -ne
                [long]$schema.properties.changes.maxItems -or
            [string](Get-LiteralScalarAssignmentValue `
                -Ast $validatorAst `
                -VariablePath 'script:DescribeAnchorRelationship') -cne
                'BaseAncestorOfHead' -or
            [string](Get-LiteralScalarAssignmentValue `
                -Ast $validatorAst `
                -VariablePath 'script:CanonicalWorkflowPath') -cne
                '.github/workflows/aicopilot-ci.yml' -or
            [string](Get-LiteralScalarAssignmentValue `
                -Ast $validatorAst `
                -VariablePath 'script:CanonicalWorkflowName') -cne
                'aicopilot-ci' -or
            [string](Get-LiteralScalarAssignmentValue `
                -Ast $validatorAst `
                -VariablePath 'script:RequiredFinalJobId') -cne
                'required-final') {
            throw 'runtime scalar constants differ from the reference schema.'
        }
        $runtimeFixture = New-TemplateCandidate -Fixture (New-BaseFixture)
        $runtimeDescription = Invoke-DescribeResult `
            -Fixture $runtimeFixture `
            -RuleIdsCsv 'AI-ARCH-001'
        if ($runtimeDescription.ExitCode -ne 0) {
            throw "Could not inspect runtime count registry: $($runtimeDescription.Output)"
        }
        foreach ($requiredArgumentName in $expectedDescribeRequiredArguments) {
            $missingArgumentResult = Invoke-DescribeResult `
                -Fixture $runtimeFixture `
                -RuleIdsCsv 'AI-ARCH-001' `
                -OmitArgument $requiredArgumentName
            if ($missingArgumentResult.ExitCode -eq 0 -or
                $missingArgumentResult.Output -notmatch 'AI-TEST-GOV-MIG-001-DESCRIBE') {
                throw "Describe missing '$requiredArgumentName' did not fail closed: $($missingArgumentResult.Output)"
            }
        }
        $wrongDescribeAnchor = Invoke-DescribeResult `
            -Fixture $runtimeFixture `
            -RuleIdsCsv 'AI-ARCH-001' `
            -Relationship 'HeadAncestorOfBase'
        if ($wrongDescribeAnchor.ExitCode -eq 0 -or
            $wrongDescribeAnchor.Output -notmatch 'AI-TEST-GOV-MIG-001-DESCRIBE') {
            throw "Describe accepted a non-reviewed anchor relationship: $($wrongDescribeAnchor.Output)"
        }
        $runtimeReceipt = ConvertFrom-TestJson -Json $runtimeDescription.Output
        if ([string]$runtimeReceipt.schemaVersion -cne [string]$schema.properties.schemaVersion.const -or
            [string]$runtimeReceipt.ruleId -cne [string]$schema.properties.ruleId.const -or
            [string]$runtimeReceipt.approvedBy -cne [string]$schema.properties.approvedBy.const) {
            throw 'Describe output differs from the reference schema constants.'
        }
        foreach ($stateName in @('source', 'target')) {
            Assert-OrdinalSequenceEqual `
                -Actual @($runtimeReceipt.$stateName.counts.PSObject.Properties.Name) `
                -Expected $expectedCountFields `
                -Location "runtime $stateName count registry"
        }
        if ($schema.additionalProperties -ne $false -or
            [string]$schema.properties.schemaVersion.const -cne '1.0' -or
            [string]$schema.properties.ruleId.const -cne 'AI-TEST-GOV-MIG-001' -or
            [string]$schema.properties.approvedBy.const -cne 'ShuJinHao' -or
            [long]$schema.properties.changes.maxItems -ne 5000 -or
            -not ([string]$schema.'$comment').Contains('is authoritative', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'reference schema constants drifted from the runtime validator contract.'
        }
        return [pscustomobject]@{ ExitCode = 0; Output = 'schema parity passed' }
    }

    $immutable = New-BaseFixture
    Assert-Pass -Name 'immutable transition' -ExpectedText 'transition is immutable' -Action {
        Invoke-Validation -Fixture $immutable -Base $immutable.Base -Candidate $immutable.Base
    }

    $withoutReceipt = New-TemplateCandidate -Fixture (New-BaseFixture)
    Assert-Rejected -Name 'protected change without receipt' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $withoutReceipt -Base $withoutReceipt.Base -Candidate $withoutReceipt.Template
    }

    $describeRules = New-TemplateCandidate -Fixture (New-BaseFixture)
    Assert-Rejected -Name 'RuleIdsCsv rejects whitespace' -ExpectedCode 'DESCRIBE' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'AI-ARCH-001, AI-TEST-001'
    }
    Assert-Rejected -Name 'RuleIdsCsv rejects empty item' -ExpectedCode 'DESCRIBE' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'AI-ARCH-001,,AI-TEST-001'
    }
    Assert-Rejected -Name 'RuleIdsCsv rejects lowercase rule ID' -ExpectedCode 'RECEIPT' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'ai-arch-001'
    }
    Assert-Rejected -Name 'RuleIdsCsv rejects unregistered rule ID' -ExpectedCode 'RECEIPT' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'AAA-001'
    }
    Assert-Pass -Name 'RuleIdsCsv output is ordinal-sorted and unique' -Action {
        $result = Invoke-DescribeResult `
            -Fixture $describeRules `
            -RuleIdsCsv 'AI-TEST-001,AI-ARCH-001,AI-ARCH-001'
        if ($result.ExitCode -eq 0) {
            $receipt = ConvertFrom-TestJson -Json $result.Output
            $actual = @($receipt.ruleIds)
            if ($actual.Count -ne 2 -or
                $actual[0] -cne 'AI-ARCH-001' -or
                $actual[1] -cne 'AI-TEST-001') {
                throw "unexpected normalized ruleIds: $($actual -join ',')"
            }
        }
        return $result
    }

    $duplicateBaseline = New-TemplateCandidate -Fixture (New-BaseFixture)
    Invoke-Git -Root $duplicateBaseline.Root -Arguments @('checkout', '--quiet', $duplicateBaseline.Template)
    $baselinePath = Join-Path $duplicateBaseline.Root 'scripts/tests/baselines/aicopilot-test-governance.baseline.json'
    $baselineJson = [IO.File]::ReadAllText($baselinePath).Replace(
        '"projects":',
        '"projects":[],"projects":')
    Write-Utf8File -Path $baselinePath -Content $baselineJson
    Invoke-Git -Root $duplicateBaseline.Root -Arguments @('add', '--all')
    Invoke-Git -Root $duplicateBaseline.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $duplicateBaseline.Template = Invoke-Git -Root $duplicateBaseline.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'baseline duplicate JSON key is rejected' -ExpectedCode 'COUNTS' -Action {
        Invoke-DescribeResult -Fixture $duplicateBaseline -RuleIdsCsv 'AI-TEST-GOV-001'
    }

    $valid = New-AuthorizationFixture
    Assert-Pass -Name 'authorization-only transition' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $valid -Base $valid.Base -Candidate $valid.Authorization
    }
    $valid = Complete-Candidate -Fixture $valid
    Assert-Pass -Name 'receipt consumption' -ExpectedText 'receipt consumed' -Action {
        Invoke-Validation -Fixture $valid -Base $valid.Authorization -Candidate $valid.Candidate
    }

    $noise = New-AuthorizationFixture -AddAuthorizationNoise
    Assert-Rejected -Name 'authorization commit contains another file' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $noise -Base $noise.Base -Candidate $noise.Authorization
    }

    $second = New-AuthorizationFixture -AddSecondPending
    Assert-Rejected -Name 'two pending receipts in one authorization' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $second -Base $second.Base -Candidate $second.Authorization
    }

    $missing = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.PSObject.Properties.Remove('reason')
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'missing receipt field' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $missing -Base $missing.Base -Candidate $missing.Authorization
    }

    $unknown = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt | Add-Member -NotePropertyName command -NotePropertyValue 'Invoke-Expression'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'unknown executable receipt field' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $unknown -Base $unknown.Base -Candidate $unknown.Authorization
    }

    $duplicate = New-AuthorizationFixture -MutateReceipt {
        param($json)
        return $json -replace '"reason"\s*:', '"reason":"duplicate","reason":'
    }
    Assert-Rejected -Name 'duplicate JSON key' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $duplicate -Base $duplicate.Base -Candidate $duplicate.Authorization
    }

    $expired = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.issuedAtUtc = $Now.AddDays(-3).ToString('yyyy-MM-ddTHH:mm:ssZ')
        $receipt.expiresAtUtc = $Now.AddDays(-2).ToString('yyyy-MM-ddTHH:mm:ssZ')
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'expired receipt' -ExpectedCode 'EXPIRY' -Action {
        Invoke-Validation -Fixture $expired -Base $expired.Base -Candidate $expired.Authorization
    }

    $tooLong = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.expiresAtUtc = $Now.AddDays(8).ToString('yyyy-MM-ddTHH:mm:ssZ')
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'receipt lifetime exceeds seven days' -ExpectedCode 'EXPIRY' -Action {
        Invoke-Validation -Fixture $tooLong -Base $tooLong.Base -Candidate $tooLong.Authorization
    }

    $wrongBase = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.issuedAgainstRevision = '1111111111111111111111111111111111111111'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'wrong issued-against revision' -ExpectedCode 'AUTHORIZATION' -Action {
        Invoke-Validation -Fixture $wrongBase -Base $wrongBase.Base -Candidate $wrongBase.Authorization
    }

    $traversal = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = '../escape.yml'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'path traversal in receipt' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $traversal -Base $traversal.Base -Candidate $traversal.Authorization
    }

    $wildcard = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/tests/*.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'wildcard path in receipt' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $wildcard -Base $wildcard.Base -Candidate $wildcard.Authorization
    }

    $wrongMode = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].afterMode = '120000'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'symlink mode in receipt' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $wrongMode -Base $wrongMode.Base -Candidate $wrongMode.Authorization
    }

    $caseCollision = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $clone = ConvertFrom-TestJson -Json ($receipt.changes[0] | ConvertTo-Json -Depth 10)
        $clone.path = ([string]$clone.path).ToUpperInvariant()
        $receipt.changes = @($receipt.changes) + @($clone)
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'case-colliding receipt paths' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $caseCollision -Base $caseCollision.Base -Candidate $caseCollision.Authorization
    }

    $wrongHash = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].afterSha256 = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Pass -Name 'authorization accepts reviewed future descriptor' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $wrongHash -Base $wrongHash.Base -Candidate $wrongHash.Authorization
    }
    $wrongHash = Complete-Candidate -Fixture $wrongHash
    Assert-Rejected -Name 'consumption rejects wrong file hash' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $wrongHash -Base $wrongHash.Authorization -Candidate $wrongHash.Candidate
    }

    $countDrift = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.target.counts.runnerCases = [int]$receipt.target.counts.runnerCases + 1
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Pass -Name 'authorization records future count claim' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $countDrift -Base $countDrift.Base -Candidate $countDrift.Authorization
    }
    $countDrift = Complete-Candidate -Fixture $countDrift
    Assert-Rejected -Name 'consumption rejects runner count drift' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $countDrift -Base $countDrift.Authorization -Candidate $countDrift.Candidate
    }

    $manifestDrift = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.target.protectedManifestSha256 = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
        return $receipt | ConvertTo-Json -Depth 100
    }
    $manifestDrift = Complete-Candidate -Fixture $manifestDrift
    Assert-Rejected -Name 'consumption rejects protected manifest drift' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $manifestDrift -Base $manifestDrift.Authorization -Candidate $manifestDrift.Candidate
    }

    $noMove = Complete-Candidate -Fixture (New-AuthorizationFixture) -SkipMove
    Assert-Rejected -Name 'pending receipt not moved' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation -Fixture $noMove -Base $noMove.Authorization -Candidate $noMove.Candidate
    }

    $altered = Complete-Candidate -Fixture (New-AuthorizationFixture) -AlterConsumed
    Assert-Rejected -Name 'consumed receipt blob changed' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation -Fixture $altered -Base $altered.Authorization -Candidate $altered.Candidate
    }

    $extra = Complete-Candidate -Fixture (New-AuthorizationFixture) -AddExtraPath
    Assert-Rejected -Name 'candidate has extra path' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $extra -Base $extra.Authorization -Candidate $extra.Candidate
    }

    $fewer = Complete-Candidate -Fixture (New-AuthorizationFixture) -RemoveExpectedPath
    Assert-Rejected -Name 'candidate omits expected path' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $fewer -Base $fewer.Authorization -Candidate $fewer.Candidate
    }

    $candidateBypass = Complete-Candidate -Fixture (New-AuthorizationFixture) -ModifyCandidateValidator
    Assert-Rejected -Name 'candidate validator cannot self-authorize' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $candidateBypass -Base $candidateBypass.Authorization -Candidate $candidateBypass.Candidate
    }

    $wrongPropertyCase = New-AuthorizationFixture -MutateReceipt {
        param($json)
        return $json -replace '"reason"\s*:', '"Reason":'
    }
    Assert-Rejected -Name 'receipt property names are case-sensitive' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $wrongPropertyCase -Base $wrongPropertyCase.Base -Candidate $wrongPropertyCase.Authorization
    }

    $nestedDuplicate = New-AuthorizationFixture -MutateReceipt {
        param($json)
        return $json -replace '"baselineSha256"\s*:', '"baselineSha256":"duplicate","baselineSha256":'
    }
    Assert-Rejected -Name 'nested duplicate JSON key' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $nestedDuplicate -Base $nestedDuplicate.Base -Candidate $nestedDuplicate.Authorization
    }

    $scalarRuleIds = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.ruleIds = 'AI-ARCH-001'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'ruleIds scalar is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $scalarRuleIds -Base $scalarRuleIds.Base -Candidate $scalarRuleIds.Authorization
    }

    $scalarChanges = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes = $receipt.changes[0]
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'changes scalar is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $scalarChanges -Base $scalarChanges.Base -Candidate $scalarChanges.Authorization
    }

    $nullProjectArray = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.projectChanges.added = $null
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'null project array is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $nullProjectArray -Base $nullProjectArray.Base -Candidate $nullProjectArray.Authorization
    }

    foreach ($registryMutation in @(
        @{ Name = 'owner registry is case-sensitive'; Property = 'owner'; Value = 'ai.architecture' },
        @{ Name = 'approver registry is case-sensitive'; Property = 'approvedBy'; Value = 'shujinhao' },
        @{ Name = 'governance rule ID is case-sensitive'; Property = 'ruleId'; Value = 'ai-test-gov-mig-001' }
    )) {
        $propertyName = [string]$registryMutation.Property
        $propertyValue = [string]$registryMutation.Value
        $registryCase = New-AuthorizationFixture -MutateReceipt {
            param($json)
            $receipt = ConvertFrom-TestJson -Json $json
            $receipt.$propertyName = $propertyValue
            return $receipt | ConvertTo-Json -Depth 100
        }
        Assert-Rejected -Name ([string]$registryMutation.Name) -ExpectedCode 'RECEIPT' -Action {
            Invoke-Validation -Fixture $registryCase -Base $registryCase.Base -Candidate $registryCase.Authorization
        }
    }

    $windowsReserved = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/tests/CON.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows reserved path is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $windowsReserved -Base $windowsReserved.Base -Candidate $windowsReserved.Authorization
    }

    $trailingDot = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/tests/bad./Case.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows trailing-dot segment is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $trailingDot -Base $trailingDot.Base -Candidate $trailingDot.Authorization
    }

    $superscriptDevice = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/tests/COM¹.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows superscript device name is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $superscriptDevice -Base $superscriptDevice.Base -Candidate $superscriptDevice.Authorization
    }

    $longComponent = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = "src/tests/$('a' * 256).cs"
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows overlong path component is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $longComponent -Base $longComponent.Base -Candidate $longComponent.Authorization
    }

    $countDecrease = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.target.counts.runnerCases = [long]$receipt.source.counts.runnerCases - 1
        return $receipt | ConvertTo-Json -Depth 100
    }
    $missingRosterProject = New-TemplateCandidate -Fixture (New-BaseFixture)
    Invoke-Git -Root $missingRosterProject.Root -Arguments @(
        'checkout', '--quiet', $missingRosterProject.Template)
    $missingRosterBaseline = Join-Path $missingRosterProject.Root 'scripts/tests/baselines/aicopilot-test-governance.baseline.json'
    $missingRosterText = [IO.File]::ReadAllText($missingRosterBaseline).Replace(
        'src/tests/Sample.Tests/Sample.Tests.csproj',
        'src/tests/Missing.Tests/Missing.Tests.csproj')
    Write-Utf8File -Path $missingRosterBaseline -Content $missingRosterText
    Invoke-Git -Root $missingRosterProject.Root -Arguments @('add', '--all')
    Invoke-Git -Root $missingRosterProject.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $missingRosterProject.Template = Invoke-Git `
        -Root $missingRosterProject.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture

    $outsideSolutionProject = New-TemplateCandidate -Fixture (New-BaseFixture)
    Invoke-Git -Root $outsideSolutionProject.Root -Arguments @(
        'checkout', '--quiet', $outsideSolutionProject.Template)
    $outsideSolutionBaseline = Join-Path $outsideSolutionProject.Root 'scripts/tests/baselines/aicopilot-test-governance.baseline.json'
    $outsideSolutionText = [IO.File]::ReadAllText($outsideSolutionBaseline).Replace(
        'src/tests/Sample.Tests/Sample.Tests.csproj',
        'src/tests/Outside.Tests/Outside.Tests.csproj')
    Write-Utf8File -Path $outsideSolutionBaseline -Content $outsideSolutionText
    Write-Utf8File `
        -Path (Join-Path $outsideSolutionProject.Root 'src/tests/Outside.Tests/Outside.Tests.csproj') `
        -Content "<Project Sdk=`"Microsoft.NET.Sdk`" />`n"
    Invoke-Git -Root $outsideSolutionProject.Root -Arguments @('add', '--all')
    Invoke-Git -Root $outsideSolutionProject.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $outsideSolutionProject.Template = Invoke-Git `
        -Root $outsideSolutionProject.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture

    $executableRosterProject = New-TemplateCandidate -Fixture (New-BaseFixture)
    Invoke-Git -Root $executableRosterProject.Root -Arguments @(
        'checkout', '--quiet', $executableRosterProject.Template)
    Invoke-Git -Root $executableRosterProject.Root -Arguments @(
        'update-index', '--chmod=+x', '--', 'src/tests/Sample.Tests/Sample.Tests.csproj')
    Invoke-Git -Root $executableRosterProject.Root -Arguments @(
        'commit', '--quiet', '--amend', '--no-edit')
    $executableRosterProject.Template = Invoke-Git `
        -Root $executableRosterProject.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture

    Assert-Rejected -Name 'target evidence cannot decrease and baseline roster must be tracked mode-100644 solution projects' -ExpectedCode 'COUNTS' -Action {
        foreach ($fixture in @(
            $missingRosterProject,
            $outsideSolutionProject,
            $executableRosterProject
        )) {
            $result = Invoke-DescribeResult -Fixture $fixture -RuleIdsCsv 'AI-TEST-GOV-001'
            if ($result.ExitCode -eq 0 -or
                $result.Output -notmatch 'AI-TEST-GOV-MIG-001-COUNTS') {
                throw "baseline roster mutation was not rejected as COUNTS: $($result.Output)"
            }
        }
        Invoke-Validation -Fixture $countDecrease -Base $countDecrease.Base -Candidate $countDecrease.Authorization
    }

    $invalidUtf8 = New-AuthorizationFixture
    $invalidUtf8 = Amend-AuthorizationReceiptBytes -Fixture $invalidUtf8 -Bytes ([byte[]]@(0x7B, 0xFF, 0x7D))
    Assert-Rejected -Name 'invalid UTF-8 receipt is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $invalidUtf8 -Base $invalidUtf8.Base -Candidate $invalidUtf8.Authorization
    }

    $oversized = New-AuthorizationFixture
    $oversized = Amend-AuthorizationReceiptBytes -Fixture $oversized -Bytes (
        [Text.Encoding]::UTF8.GetBytes(' ' * (1MB + 1)))
    Assert-Rejected -Name 'oversized receipt is rejected before parsing' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $oversized -Base $oversized.Base -Candidate $oversized.Authorization
    }

    $executablePending = New-AuthorizationFixture
    $pendingBytes = [IO.File]::ReadAllBytes((Join-Path $executablePending.Root $ReceiptRelativePath))
    $executablePending = Amend-AuthorizationReceiptBytes -Fixture $executablePending -Bytes $pendingBytes -ExecutableMode
    Assert-Rejected -Name 'pending receipt executable mode is rejected' -ExpectedCode 'AUTHORIZATION' -Action {
        Invoke-Validation -Fixture $executablePending -Base $executablePending.Base -Candidate $executablePending.Authorization
    }

    $missingTrustMarker = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            'AI-TEST-GOV-MIG-TRUSTED-EXECUTOR-V1',
            'AI-TEST-GOV-MIG-NOT-TRUSTED')
        Write-Utf8File -Path $path -Content $text
    }
    $missingTrustMarker = Complete-Candidate -Fixture $missingTrustMarker

    $changedCanonicalWorkflowName = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            'name: aicopilot-ci',
            'name: renamed-aicopilot-ci')
        Write-Utf8File -Path $path -Content $text
    }
    $changedCanonicalWorkflowName = Complete-Candidate -Fixture $changedCanonicalWorkflowName
    Assert-Rejected -Name 'Describe refuses workflow identity or trusted-marker drift before issuing a receipt' -ExpectedCode 'TRUST' -Action {
        foreach ($fixture in @($changedCanonicalWorkflowName, $missingTrustMarker)) {
            if (-not $fixture.DescribeRejected) {
                throw 'workflow closure mutation reached receipt authorization instead of failing Describe.'
            }
            $result = Invoke-Validation `
                -Fixture $fixture `
                -Base $fixture.Authorization `
                -Candidate $fixture.Candidate
            if ($result.ExitCode -eq 0 -or
                $result.Output -notmatch 'AI-TEST-GOV-MIG-001-TRUST') {
                throw "Describe workflow mutation was not rejected as TRUST: $($result.Output)"
            }
        }
        Invoke-Validation `
            -Fixture $missingTrustMarker `
            -Base $missingTrustMarker.Authorization `
            -Candidate $missingTrustMarker.Candidate
    }

    $preGateStep = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Validate trusted AICopilot governance migration',
            "      - name: Pre-gate command`n        shell: pwsh`n        run: Write-Host bypass`n      - name: Validate trusted AICopilot governance migration")
        Write-Utf8File -Path $path -Content $text
    }
    $preGateStep = Complete-Candidate -Fixture $preGateStep
    Assert-Rejected -Name 'workflow step before trusted gate is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $preGateStep -Base $preGateStep.Authorization -Candidate $preGateStep.Candidate
    }

    $alternatePreGateStep = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Validate trusted AICopilot governance migration',
            "      -`n        name: Alternate pre-gate command`n        shell: pwsh`n        run: Write-Host bypass`n      - name: Validate trusted AICopilot governance migration")
        Write-Utf8File -Path $path -Content $text
    }
    $alternatePreGateStep = Complete-Candidate -Fixture $alternatePreGateStep
    Assert-Rejected -Name 'alternate YAML step before trusted gate is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $alternatePreGateStep `
            -Base $alternatePreGateStep.Authorization `
            -Candidate $alternatePreGateStep.Candidate
    }

    $spoofedCheckout = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7',
            "        uses: attacker/checkout@0123456789012345678901234567890123456789`n        # uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7")
        Write-Utf8File -Path $path -Content $text
    }
    $spoofedCheckout = Complete-Candidate -Fixture $spoofedCheckout
    Assert-Rejected -Name 'comment cannot spoof pinned checkout action' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $spoofedCheckout -Base $spoofedCheckout.Authorization -Candidate $spoofedCheckout.Candidate
    }

    $softFailedGate = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Setup .NET',
            "        continue-on-error: true`n      - name: Setup .NET")
        Write-Utf8File -Path $path -Content $text
    }
    $softFailedGate = Complete-Candidate -Fixture $softFailedGate
    Assert-Rejected -Name 'trusted gate cannot continue on error' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $softFailedGate -Base $softFailedGate.Authorization -Candidate $softFailedGate.Candidate
    }

    $conditionalGate = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Setup .NET',
            "        if: false`n      - name: Setup .NET")
        Write-Utf8File -Path $path -Content $text
    }
    $conditionalGate = Complete-Candidate -Fixture $conditionalGate
    Assert-Rejected -Name 'trusted gate cannot be conditional' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $conditionalGate -Base $conditionalGate.Authorization -Candidate $conditionalGate.Candidate
    }

    $changedRunner = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '    runs-on: ubuntu-24.04',
            '    runs-on: self-hosted')
        Write-Utf8File -Path $path -Content $text
    }
    $changedRunner = Complete-Candidate -Fixture $changedRunner
    Assert-Rejected -Name 'trusted job runner is pinned' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $changedRunner -Base $changedRunner.Authorization -Candidate $changedRunner.Candidate
    }

    $changedWorkflowEnvelope = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "jobs:`n",
            "permissions: write-all`njobs:`n")
        Write-Utf8File -Path $path -Content $text
    }
    $changedWorkflowEnvelope = Complete-Candidate -Fixture $changedWorkflowEnvelope
    Assert-Rejected -Name 'workflow trigger and permissions envelope is pinned' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $changedWorkflowEnvelope `
            -Base $changedWorkflowEnvelope.Authorization `
            -Candidate $changedWorkflowEnvelope.Candidate
    }

    $jobLevelEnvironment = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).TrimEnd("`r", "`n")
        Write-Utf8File `
            -Path $path `
            -Content "$text`n    env:`n      PATH: candidate-controlled-path`n"
    }
    $jobLevelEnvironment = Complete-Candidate -Fixture $jobLevelEnvironment
    $truncatedRequiredSuffix = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path)
        $suffixStart = $text.IndexOf("      - name: Setup Node`n", [StringComparison]::Ordinal)
        if ($suffixStart -lt 0) { throw 'Could not find the canonical required suffix.' }
        Write-Utf8File -Path $path -Content $text.Substring(0, $suffixStart)
    }
    $truncatedRequiredSuffix = Complete-Candidate -Fixture $truncatedRequiredSuffix

    $missingSelfTestJob = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path)
        $selfTestStart = $text.IndexOf("  migration-validator-selftest:`n", [StringComparison]::Ordinal)
        $buildStart = $text.IndexOf("  build-test:`n", [StringComparison]::Ordinal)
        if ($selfTestStart -lt 0 -or $buildStart -le $selfTestStart) {
            throw 'Could not isolate the canonical validator self-test job.'
        }
        Write-Utf8File `
            -Path $path `
            -Content ($text.Substring(0, $selfTestStart) + $text.Substring($buildStart))
    }
    $missingSelfTestJob = Complete-Candidate -Fixture $missingSelfTestJob

    $missingFinalJob = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path)
        $finalStart = $text.IndexOf("  required-final:`n", [StringComparison]::Ordinal)
        if ($finalStart -lt 0) { throw 'Could not find the canonical required-final job.' }
        Write-Utf8File -Path $path -Content $text.Substring(0, $finalStart)
    }
    $missingFinalJob = Complete-Candidate -Fixture $missingFinalJob

    $missingFinalNeeds = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "    needs:`n      - migration-validator-selftest`n      - build-test`n",
            '')
        Write-Utf8File -Path $path -Content $text
    }
    $missingFinalNeeds = Complete-Candidate -Fixture $missingFinalNeeds

    $missingFinalAlways = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '    if: ${{ always() }}' + "`n",
            '')
        Write-Utf8File -Path $path -Content $text
    }
    $missingFinalAlways = Complete-Candidate -Fixture $missingFinalAlways

    $softFinalResult = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '          test "$BUILD_TEST_RESULT" = "success"',
            '          echo "$BUILD_TEST_RESULT"')
        Write-Utf8File -Path $path -Content $text
    }
    $softFinalResult = Complete-Candidate -Fixture $softFinalResult

    $serializedBuildJob = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "  build-test:`n    runs-on: ubuntu-24.04",
            "  build-test:`n    needs: migration-validator-selftest`n    runs-on: ubuntu-24.04")
        Write-Utf8File -Path $path -Content $text
    }
    $serializedBuildJob = Complete-Candidate -Fixture $serializedBuildJob

    $crossJobArtifactTransfer = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "`n  build-test:`n",
            @'

      - name: Export candidate validator to authoritative job
        uses: actions/upload-artifact@b7c566a772e6b6bfb58ed0dc250532a479d7789f # v6
        with:
          name: candidate-validator
          path: scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1

  build-test:
'@).Replace(
            '      - name: Validate trusted AICopilot governance migration',
            @'
      - name: Import candidate validator from self-test job
        uses: actions/download-artifact@v4
        with:
          name: candidate-validator

      - name: Validate trusted AICopilot governance migration
'@)
        Write-Utf8File -Path $path -Content $text
    }
    $crossJobArtifactTransfer = Complete-Candidate -Fixture $crossJobArtifactTransfer

    $crossJobCacheTransfer = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "`n  build-test:`n",
            @'

      - name: Export candidate validator through a shared cache
        uses: actions/cache@v4
        with:
          path: scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1
          key: candidate-validator

  build-test:
'@).Replace(
            '      - name: Validate trusted AICopilot governance migration',
            @'
      - name: Restore candidate validator through a shared cache
        uses: actions/cache@v4
        with:
          path: scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1
          key: candidate-validator

      - name: Validate trusted AICopilot governance migration
'@)
        Write-Utf8File -Path $path -Content $text
    }
    $crossJobCacheTransfer = Complete-Candidate -Fixture $crossJobCacheTransfer

    $missingScopedFetchToken = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '          AICOPILOT_GITHUB_TOKEN: ${{ github.token }}' + "`n",
            '')
        Write-Utf8File -Path $path -Content $text
    }
    $missingScopedFetchToken = Complete-Candidate -Fixture $missingScopedFetchToken

    $persistedFetchCredential = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '          & git -c "http.extraheader=AUTHORIZATION: basic $authorization" fetch --no-tags origin',
            "          git config --local http.extraheader `"AUTHORIZATION: basic `$authorization`"`n          & git fetch --no-tags origin")
        Write-Utf8File -Path $path -Content $text
    }
    $persistedFetchCredential = Complete-Candidate -Fixture $persistedFetchCredential

    $trailingCandidateStep = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).TrimEnd("`r", "`n")
        Write-Utf8File `
            -Path $path `
            -Content "$text`n      - name: Candidate command after required suffix`n        run: echo bypass`n"
    }
    $trailingCandidateStep = Complete-Candidate -Fixture $trailingCandidateStep

    $quotedFlowJob = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).TrimEnd("`r", "`n")
        Write-Utf8File `
            -Path $path `
            -Content "$text`n  `"elevated`": { runs-on: ubuntu-24.04, permissions: write-all, steps: [{ run: `"echo bypass`" }] }`n"
    }
    $quotedFlowJob = Complete-Candidate -Fixture $quotedFlowJob

    $competingWorkflowIdentity = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/competing-required-context.yml') `
                -Content @'
name: aicopilot-ci
on:
  workflow_dispatch:
jobs:
  required-final:
    runs-on: ubuntu-24.04
    steps:
      - run: echo competing-context
'@
        }
    $competingWorkflowIdentity = Complete-Candidate -Fixture $competingWorkflowIdentity

    $competingMigrationSelfTestJobId = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/competing-migration-selftest.yml') `
                -Content @'
name: independent-migration-selftest
on:
  workflow_dispatch:
jobs:
  migration-validator-selftest:
    runs-on: ubuntu-24.04
    steps:
      - run: echo competing-selftest-context
'@
        }
    $competingMigrationSelfTestJobId = Complete-Candidate `
        -Fixture $competingMigrationSelfTestJobId

    $escapedBuildTestJobId = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/escaped-build-test-job.yml') `
                -Content @'
name: independent-escaped-build-job
on:
  workflow_dispatch:
jobs:
  "build\u002dtest":
    runs-on: ubuntu-24.04
    steps:
      - run: echo escaped-build-context
'@
        }
    $escapedBuildTestJobId = Complete-Candidate -Fixture $escapedBuildTestJobId

    $expressionWorkflowIdentity = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/expression-required-context.yaml') `
                -Content @'
name: ${{ format('{0}{1}', 'aicopilot-', 'ci') }}
on:
  workflow_dispatch:
jobs:
  competing:
    name: ${{ format('{0}-{1}', 'required', 'final') }}
    runs-on: ubuntu-24.04
    steps:
      - run: echo expression-context
'@
        }
    $expressionWorkflowIdentity = Complete-Candidate -Fixture $expressionWorkflowIdentity

    $escapedWorkflowName = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/escaped-workflow-name.yml') `
                -Content @'
name: "aicopilot\u002dci"
on:
  workflow_dispatch:
jobs:
  inspect:
    runs-on: ubuntu-24.04
    steps:
      - run: echo escaped-name
'@
        }
    $escapedWorkflowName = Complete-Candidate -Fixture $escapedWorkflowName

    $escapedWorkflowJobId = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/escaped-job-id.yaml') `
                -Content @'
name: independent-escaped-job
on:
  workflow_dispatch:
jobs:
  "required\u002dfinal":
    runs-on: ubuntu-24.04
    steps:
      - run: echo escaped-job
'@
        }
    $escapedWorkflowJobId = Complete-Candidate -Fixture $escapedWorkflowJobId

    $blockWorkflowIdentity = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/block-workflow-name.yml') `
                -Content @'
name: >-
  independent-block-name
on:
  workflow_dispatch:
jobs:
  inspect:
    runs-on: ubuntu-24.04
    steps:
      - run: echo block-name
'@
        }
    $blockWorkflowIdentity = Complete-Candidate -Fixture $blockWorkflowIdentity

    $flowWorkflowJobs = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/flow-workflow-jobs.yml') `
                -Content @'
name: independent-flow-jobs
on:
  workflow_dispatch:
jobs: { inspect: { runs-on: ubuntu-24.04, steps: [{ run: echo flow-jobs }] } }
'@
        }
    $flowWorkflowJobs = Complete-Candidate -Fixture $flowWorkflowJobs

    $anchoredWorkflowJob = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/anchored-workflow-job.yml') `
                -Content @'
name: independent-anchored-job
on:
  workflow_dispatch:
jobs:
  inspect: &inspect-job
    runs-on: ubuntu-24.04
    steps:
      - run: echo anchored-job
'@
        }
    $anchoredWorkflowJob = Complete-Candidate -Fixture $anchoredWorkflowJob

    $aliasedWorkflowJobs = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/aliased-workflow-jobs.yml') `
                -Content @'
name: independent-aliased-jobs
on:
  workflow_dispatch:
shared-jobs: &shared-jobs
  inspect:
    runs-on: ubuntu-24.04
    steps:
      - run: echo aliased-jobs
jobs: *shared-jobs
'@
        }
    $aliasedWorkflowJobs = Complete-Candidate -Fixture $aliasedWorkflowJobs

    $competingWorkflowTrustReference = New-AuthorizationFixture `
        -ExpectedTargetWorkflowFiles 2 `
        -MutateTemplate {
            param($root)
            Write-Utf8File `
                -Path (Join-Path $root '.github/workflows/competing-trust-reference.yml') `
                -Content @'
name: independent-governance-check
on:
  workflow_dispatch:
jobs:
  inspect:
    runs-on: ubuntu-24.04
    steps:
      - shell: pwsh
        run: |
          # AI-TEST-GOV-MIG-TRUSTED-EXECUTOR-V1
          Write-Host 'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1'
          Write-Host 'scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1'
          Write-Host 'scripts/tests/baselines/migrations/TestAICopilotGovernanceMigrationValidator.v1.ps1'
          Write-Host 'scripts/tests/baselines/migrations/aicopilot-governance-migration-receipt.schema.json'
'@
        }
    $competingWorkflowTrustReference = Complete-Candidate `
        -Fixture $competingWorkflowTrustReference

    Assert-Rejected -Name 'complete required workflow rejects suffix deletion, trailing commands/jobs and job environment' -ExpectedCode 'TRUST' -Action {
        foreach ($fixture in @(
            $truncatedRequiredSuffix,
            $missingSelfTestJob,
            $missingFinalJob,
            $missingFinalNeeds,
            $missingFinalAlways,
            $softFinalResult,
            $serializedBuildJob,
            $crossJobArtifactTransfer,
            $crossJobCacheTransfer,
            $missingScopedFetchToken,
            $persistedFetchCredential,
            $trailingCandidateStep,
            $quotedFlowJob,
            $competingWorkflowIdentity,
            $competingMigrationSelfTestJobId,
            $escapedBuildTestJobId,
            $expressionWorkflowIdentity,
            $escapedWorkflowName,
            $escapedWorkflowJobId,
            $blockWorkflowIdentity,
            $flowWorkflowJobs,
            $anchoredWorkflowJob,
            $aliasedWorkflowJobs,
            $competingWorkflowTrustReference)) {
            $result = Invoke-Validation `
                -Fixture $fixture `
                -Base $fixture.Authorization `
                -Candidate $fixture.Candidate
            if ($result.ExitCode -eq 0 -or
                $result.Output -notmatch 'AI-TEST-GOV-MIG-001-TRUST') {
                throw "complete workflow mutation was not rejected as TRUST: $($result.Output)"
            }
        }
        Invoke-Validation `
            -Fixture $jobLevelEnvironment `
            -Base $jobLevelEnvironment.Authorization `
            -Candidate $jobLevelEnvironment.Candidate
    }

    $quotedJobLevelEnvironment = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).TrimEnd("`r", "`n")
        Write-Utf8File `
            -Path $path `
            -Content "$text`n    `"env`":`n      PATH: candidate-controlled-path`n"
    }
    $quotedJobLevelEnvironment = Complete-Candidate -Fixture $quotedJobLevelEnvironment
    Assert-Rejected -Name 'quoted job-level environment cannot bypass closure' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $quotedJobLevelEnvironment `
            -Base $quotedJobLevelEnvironment.Authorization `
            -Candidate $quotedJobLevelEnvironment.Candidate
    }

    $quotedTopLevelPermissions = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).TrimEnd("`r", "`n")
        Write-Utf8File `
            -Path $path `
            -Content "$text`n`"permissions`": write-all`n"
    }
    $quotedTopLevelPermissions = Complete-Candidate -Fixture $quotedTopLevelPermissions
    Assert-Rejected -Name 'quoted top-level permissions cannot bypass closure' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $quotedTopLevelPermissions `
            -Base $quotedTopLevelPermissions.Authorization `
            -Candidate $quotedTopLevelPermissions.Candidate
    }

    $disabledTrustedJob = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "  build-test:`n    runs-on: ubuntu-24.04",
            "  build-test:`n    if : false`n    runs-on: ubuntu-24.04")
        Write-Utf8File -Path $path -Content $text
    }
    $disabledTrustedJob = Complete-Candidate -Fixture $disabledTrustedJob
    Assert-Rejected -Name 'disabled trusted workflow job is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $disabledTrustedJob -Base $disabledTrustedJob.Authorization -Candidate $disabledTrustedJob.Candidate
    }

    $candidateBuild = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Run AICopilot test governance self-tests',
            @'
      - name: Execute candidate validator self-test inside authoritative build-test
        shell: pwsh
        run: ./scripts/tests/baselines/migrations/TestAICopilotGovernanceMigrationValidator.v1.ps1 -ValidatorPath ./scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1

      - name: Run AICopilot test governance self-tests
'@)
        Write-Utf8File -Path $path -Content $text
    }
    $candidateBuild = Complete-Candidate -Fixture $candidateBuild

    $candidateFinal = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Require successful AICopilot self-test and build-test',
            @'
      - name: Execute candidate validator inside required-final
        shell: pwsh
        run: ./scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1 -TrustedBaseRevision HEAD -CandidateRevision HEAD

      - name: Require successful AICopilot self-test and build-test
'@)
        Write-Utf8File -Path $path -Content $text
    }
    $candidateFinal = Complete-Candidate -Fixture $candidateFinal

    $checkoutFinal = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/aicopilot-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Require successful AICopilot self-test and build-test',
            @'
      - name: Checkout candidate in required-final
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7

      - name: Require successful AICopilot self-test and build-test
'@)
        Write-Utf8File -Path $path -Content $text
    }
    $checkoutFinal = Complete-Candidate -Fixture $checkoutFinal
    Assert-Rejected -Name 'required-final cannot check out or execute candidate content' -ExpectedCode 'TRUST' -Action {
        foreach ($fixture in @($candidateBuild, $candidateFinal)) {
            $result = Invoke-Validation `
                -Fixture $fixture `
                -Base $fixture.Authorization `
                -Candidate $fixture.Candidate
            if ($result.ExitCode -eq 0 -or
                $result.Output -notmatch 'AI-TEST-GOV-MIG-001-TRUST') {
                throw "candidate execution mutation was not rejected as TRUST: $($result.Output)"
            }
        }
        Invoke-Validation `
            -Fixture $checkoutFinal `
            -Base $checkoutFinal.Authorization `
            -Candidate $checkoutFinal.Candidate
    }

    $ordinaryTrustChange = New-TrustTemplateFixture
    $baselineAndPolicy = New-TemplateCandidate -Fixture (New-BaseFixture)
    Invoke-Git -Root $baselineAndPolicy.Root -Arguments @('checkout', '--quiet', $baselineAndPolicy.Template)
    [IO.File]::AppendAllText(
        (Join-Path $baselineAndPolicy.Root 'scripts/tests/baselines/aicopilot-test-governance.baseline.json'),
        " `n",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::AppendAllText(
        (Join-Path $baselineAndPolicy.Root 'scripts/tests/TestAICopilotTestGovernancePolicy.ps1'),
        "# candidate policy change`n",
        [Text.UTF8Encoding]::new($false))
    Invoke-Git -Root $baselineAndPolicy.Root -Arguments @('add', '--all')
    Invoke-Git -Root $baselineAndPolicy.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $baselineAndPolicy.Template = Invoke-Git -Root $baselineAndPolicy.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'ordinary receipt cannot replace trust implementation' -ExpectedCode 'TRUST' -Action {
        $mixedResult = Invoke-DescribeResult -Fixture $baselineAndPolicy -RuleIdsCsv 'AI-TEST-GOV-001'
        if ($mixedResult.ExitCode -eq 0 -or
            $mixedResult.Output -notmatch 'AI-TEST-GOV-MIG-001-POLICY') {
            throw "baseline/policy co-change was not rejected as POLICY: $($mixedResult.Output)"
        }
        Invoke-DescribeResult -Fixture $ordinaryTrustChange -RuleIdsCsv 'AI-ARCH-001'
    }

    $mixedTrustChange = New-TrustTemplateFixture
    Invoke-Git -Root $mixedTrustChange.Root -Arguments @('checkout', '--quiet', $mixedTrustChange.Template)
    Write-Utf8File -Path (Join-Path $mixedTrustChange.Root 'src/App/Mixed.cs') -Content "internal sealed class Mixed { }`n"
    Invoke-Git -Root $mixedTrustChange.Root -Arguments @('add', '--all')
    Invoke-Git -Root $mixedTrustChange.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $mixedTrustChange.Template = Invoke-Git -Root $mixedTrustChange.Root -Arguments @('rev-parse', 'HEAD') -Capture
    $duplicateTrustReceipt = New-TrustUpgradeFixture
    $duplicateTrustReceiptObject = ConvertFrom-TestJson -Json (
        [IO.File]::ReadAllText((Join-Path $duplicateTrustReceipt.Root $ReceiptRelativePath)))
    $duplicateTrustReceiptObject.ruleIds = @(
        'AI-TEST-GOV-TRUST-UPGRADE-001',
        'AI-TEST-GOV-TRUST-UPGRADE-001'
    )
    $duplicateTrustReceiptJson = $duplicateTrustReceiptObject | ConvertTo-Json -Depth 100
    $duplicateTrustReceipt = Amend-AuthorizationReceiptBytes `
        -Fixture $duplicateTrustReceipt `
        -Bytes ([Text.UTF8Encoding]::new($false).GetBytes("$duplicateTrustReceiptJson`n"))
    Assert-Rejected -Name 'trust upgrade cannot mix ordinary paths' -ExpectedCode 'TRUST' -Action {
        $mixedRuleResult = Invoke-DescribeResult `
            -Fixture $ordinaryTrustChange `
            -RuleIdsCsv 'AI-ARCH-001,AI-TEST-GOV-TRUST-UPGRADE-001'
        if ($mixedRuleResult.ExitCode -eq 0 -or
            $mixedRuleResult.Output -notmatch 'AI-TEST-GOV-MIG-001-TRUST') {
            throw "trust upgrade rule ID mixture was not rejected as TRUST: $($mixedRuleResult.Output)"
        }
        $duplicateRuleResult = Invoke-DescribeResult `
            -Fixture $ordinaryTrustChange `
            -RuleIdsCsv 'AI-TEST-GOV-TRUST-UPGRADE-001,AI-TEST-GOV-TRUST-UPGRADE-001'
        if ($duplicateRuleResult.ExitCode -eq 0 -or
            $duplicateRuleResult.Output -notmatch 'AI-TEST-GOV-MIG-001-TRUST') {
            throw "duplicate raw TrustUpgrade RuleIdsCsv was not rejected as TRUST: $($duplicateRuleResult.Output)"
        }
        $duplicateReceiptResult = Invoke-Validation `
            -Fixture $duplicateTrustReceipt `
            -Base $duplicateTrustReceipt.Base `
            -Candidate $duplicateTrustReceipt.Authorization
        if ($duplicateReceiptResult.ExitCode -eq 0 -or
            $duplicateReceiptResult.Output -notmatch 'AI-TEST-GOV-MIG-001-TRUST') {
            throw "duplicate raw TrustUpgrade receipt was not rejected as TRUST: $($duplicateReceiptResult.Output)"
        }
        Invoke-DescribeResult `
            -Fixture $mixedTrustChange `
            -RuleIdsCsv 'AI-TEST-GOV-TRUST-UPGRADE-001'
    }

    $extraTrustAsset = New-TrustTemplateFixture
    Invoke-Git -Root $extraTrustAsset.Root -Arguments @('checkout', '--quiet', $extraTrustAsset.Template)
    Write-Utf8File `
        -Path (Join-Path $extraTrustAsset.Root 'scripts/tests/baselines/migrations/FutureTrustBypass.ps1') `
        -Content "throw 'candidate trust bypass'`n"
    Invoke-Git -Root $extraTrustAsset.Root -Arguments @('add', '--all')
    Invoke-Git -Root $extraTrustAsset.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $extraTrustAsset.Template = Invoke-Git -Root $extraTrustAsset.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'trust upgrade rejects unregistered implementation path' -ExpectedCode 'TRUST' -Action {
        Invoke-DescribeResult `
            -Fixture $extraTrustAsset `
            -RuleIdsCsv 'AI-TEST-GOV-TRUST-UPGRADE-001'
    }

    Assert-Rejected -Name 'v1 trust upgrade rejects wrapper schema and self-test replacement' -ExpectedCode 'TRUST' -Action {
        $lastResult = $null
        foreach ($harnessPath in @(
            'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1',
            'scripts/tests/baselines/migrations/aicopilot-governance-migration-receipt.schema.json',
            'scripts/tests/baselines/migrations/TestAICopilotGovernanceMigrationValidator.v1.ps1'
        )) {
            $harnessChange = New-TrustTemplateFixture
            Invoke-Git -Root $harnessChange.Root -Arguments @(
                'checkout', '--quiet', $harnessChange.Template)
            [IO.File]::AppendAllText(
                (Join-Path $harnessChange.Root $harnessPath),
                " `n",
                [Text.UTF8Encoding]::new($false))
            Invoke-Git -Root $harnessChange.Root -Arguments @(
                'add', '--all')
            Invoke-Git -Root $harnessChange.Root -Arguments @(
                'commit', '--quiet', '--amend', '--no-edit')
            $harnessChange.Template = Invoke-Git `
                -Root $harnessChange.Root `
                -Arguments @('rev-parse', 'HEAD') `
                -Capture
            $lastResult = Invoke-DescribeResult `
                -Fixture $harnessChange `
                -RuleIdsCsv 'AI-TEST-GOV-TRUST-UPGRADE-001'
            if ($lastResult.ExitCode -eq 0 -or
                $lastResult.Output -notmatch 'AI-TEST-GOV-MIG-001-TRUST') {
                throw "v1 harness replacement was not rejected for '$harnessPath': $($lastResult.Output)"
            }
        }
        return $lastResult
    }

    $trustUpgrade = New-TrustUpgradeFixture
    Assert-Pass -Name 'isolated trust upgrade authorization' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $trustUpgrade -Base $trustUpgrade.Base -Candidate $trustUpgrade.Authorization
    }
    $trustUpgrade = Complete-Candidate -Fixture $trustUpgrade
    Assert-Pass -Name 'isolated trust upgrade consumption' -ExpectedText 'receipt consumed' -Action {
        Invoke-Validation -Fixture $trustUpgrade -Base $trustUpgrade.Authorization -Candidate $trustUpgrade.Candidate
    }

    $cancelled = Complete-Cancellation -Fixture (New-AuthorizationFixture)
    Assert-Pass -Name 'pending receipt can be cancelled byte-for-byte' -ExpectedText 'receipt cancelled' -Action {
        Invoke-Validation -Fixture $cancelled -Base $cancelled.Authorization -Candidate $cancelled.Candidate
    }

    $expiredCancellation = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.issuedAtUtc = $Now.AddDays(-3).ToString('yyyy-MM-ddTHH:mm:ssZ')
        $receipt.expiresAtUtc = $Now.AddDays(-2).ToString('yyyy-MM-ddTHH:mm:ssZ')
        return $receipt | ConvertTo-Json -Depth 100
    }
    $expiredCancellation = Complete-Cancellation -Fixture $expiredCancellation
    Assert-Pass -Name 'expired pending receipt can be cancelled for recovery' -ExpectedText 'receipt cancelled' -Action {
        Invoke-Validation -Fixture $expiredCancellation -Base $expiredCancellation.Authorization -Candidate $expiredCancellation.Candidate
    }

    $alteredCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture) -AlterCancelled
    Assert-Rejected -Name 'altered cancelled receipt is rejected' -ExpectedCode 'CANCEL' -Action {
        Invoke-Validation -Fixture $alteredCancellation -Base $alteredCancellation.Authorization -Candidate $alteredCancellation.Candidate
    }

    $noisyCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture) -AddExtraPath
    Assert-Rejected -Name 'cancellation cannot carry another path' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation -Fixture $noisyCancellation -Base $noisyCancellation.Authorization -Candidate $noisyCancellation.Candidate
    }

    $executableCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture) -ExecutableMode
    Assert-Rejected -Name 'cancelled receipt executable mode is rejected' -ExpectedCode 'CANCEL' -Action {
        Invoke-Validation -Fixture $executableCancellation -Base $executableCancellation.Authorization -Candidate $executableCancellation.Candidate
    }

    $cancelReplay = Complete-Cancellation -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $cancelReplay.Root -Arguments @('checkout', '--quiet', $cancelReplay.Candidate)
    [IO.Directory]::CreateDirectory((Split-Path (Join-Path $cancelReplay.Root $ReceiptRelativePath) -Parent)) | Out-Null
    [IO.File]::Copy(
        (Join-Path $cancelReplay.Root $CancelledRelativePath),
        (Join-Path $cancelReplay.Root $ReceiptRelativePath),
        $true)
    $cancelReplayAttempt = Commit-All -Root $cancelReplay.Root -Message 'attempt cancelled replay'
    Assert-Rejected -Name 'cancelled migration ID cannot replay' -ExpectedCode 'REPLAY' -Action {
        Invoke-Validation -Fixture $cancelReplay -Base $cancelReplay.Candidate -Candidate $cancelReplayAttempt
    }

    $nonDirectAuthorization = New-AuthorizationFixture
    Invoke-Git -Root $nonDirectAuthorization.Root -Arguments @(
        'checkout', '--quiet', $nonDirectAuthorization.Authorization)
    Invoke-Git -Root $nonDirectAuthorization.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'authorization descendant')
    $authorizationDescendant = Invoke-Git `
        -Root $nonDirectAuthorization.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Assert-Rejected -Name 'authorization must be a direct single-parent commit' -ExpectedCode 'AUTHORIZATION' -Action {
        Invoke-Validation `
            -Fixture $nonDirectAuthorization `
            -Base $nonDirectAuthorization.Base `
            -Candidate $authorizationDescendant
    }

    $nonDirectConsumption = Complete-Candidate -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $nonDirectConsumption.Root -Arguments @('checkout', '--quiet', $nonDirectConsumption.Candidate)
    Invoke-Git -Root $nonDirectConsumption.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'consumption descendant')
    $consumptionDescendant = Invoke-Git `
        -Root $nonDirectConsumption.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Assert-Rejected -Name 'consumption must be a direct single-parent commit' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation `
            -Fixture $nonDirectConsumption `
            -Base $nonDirectConsumption.Authorization `
            -Candidate $consumptionDescendant
    }

    $mergeConsumption = Complete-Candidate -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'checkout', '--quiet', '-b', 'side-parent', $mergeConsumption.Authorization)
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'side parent')
    $sideParent = Invoke-Git -Root $mergeConsumption.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'checkout', '--quiet', $mergeConsumption.Candidate)
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'synthetic merge shape', $sideParent)
    $mergeCandidate = Invoke-Git -Root $mergeConsumption.Root -Arguments @('rev-parse', 'HEAD') -Capture

    $ordinaryMergeRange = New-BaseFixture
    Invoke-Git -Root $ordinaryMergeRange.Root -Arguments @(
        'checkout', '--quiet', '-b', 'ordinary-side', $ordinaryMergeRange.Base)
    Write-Utf8File `
        -Path (Join-Path $ordinaryMergeRange.Root 'docs/ordinary-side.md') `
        -Content "side`n"
    $ordinarySide = Commit-All -Root $ordinaryMergeRange.Root -Message 'ordinary side'
    Invoke-Git -Root $ordinaryMergeRange.Root -Arguments @(
        'checkout', '--quiet', $ordinaryMergeRange.Base)
    Write-Utf8File `
        -Path (Join-Path $ordinaryMergeRange.Root 'docs/ordinary-main.md') `
        -Content "main`n"
    [void](Commit-All -Root $ordinaryMergeRange.Root -Message 'ordinary main')
    Invoke-Git -Root $ordinaryMergeRange.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'ordinary merge range', $ordinarySide)
    $ordinaryMergeCandidate = Invoke-Git `
        -Root $ordinaryMergeRange.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    $ordinaryMergeRange | Add-Member `
        -NotePropertyName Template `
        -NotePropertyValue $ordinaryMergeCandidate `
        -Force

    Assert-Rejected -Name 'merge commits are rejected in ordinary and consumption v1 ranges' -ExpectedCode 'HISTORY' -Action {
        $describeMergeResult = Invoke-DescribeResult `
            -Fixture $ordinaryMergeRange `
            -RuleIdsCsv 'AI-ARCH-001'
        if ($describeMergeResult.ExitCode -eq 0 -or
            $describeMergeResult.Output -notmatch 'AI-TEST-GOV-MIG-001-HISTORY') {
            throw "Describe merge range was not rejected as HISTORY: $($describeMergeResult.Output)"
        }
        $ordinaryResult = Invoke-Validation `
            -Fixture $ordinaryMergeRange `
            -Base $ordinaryMergeRange.Base `
            -Candidate $ordinaryMergeCandidate
        if ($ordinaryResult.ExitCode -eq 0 -or
            $ordinaryResult.Output -notmatch 'AI-TEST-GOV-MIG-001-HISTORY') {
            throw "ordinary merge range was not rejected as HISTORY: $($ordinaryResult.Output)"
        }
        Invoke-Validation `
            -Fixture $mergeConsumption `
            -Base $mergeConsumption.Authorization `
            -Candidate $mergeCandidate
    }

    $nonDirectCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $nonDirectCancellation.Root -Arguments @('checkout', '--quiet', $nonDirectCancellation.Candidate)
    Invoke-Git -Root $nonDirectCancellation.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'cancellation descendant')
    $cancellationDescendant = Invoke-Git `
        -Root $nonDirectCancellation.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Assert-Rejected -Name 'cancellation must be a direct single-parent commit' -ExpectedCode 'CANCEL' -Action {
        Invoke-Validation `
            -Fixture $nonDirectCancellation `
            -Base $nonDirectCancellation.Authorization `
            -Candidate $cancellationDescendant
    }

    $replay = Complete-Candidate -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $replay.Root -Arguments @('checkout', '--quiet', $replay.Candidate)
    [IO.Directory]::CreateDirectory((Split-Path (Join-Path $replay.Root $ReceiptRelativePath) -Parent)) | Out-Null
    [IO.File]::Copy(
        (Join-Path $replay.Root $ConsumedRelativePath),
        (Join-Path $replay.Root $ReceiptRelativePath),
        $true)
    $replayAttempt = Commit-All -Root $replay.Root -Message 'attempt replay'
    Assert-Rejected -Name 'consumed migration ID cannot replay' -ExpectedCode 'REPLAY' -Action {
        Invoke-Validation -Fixture $replay -Base $replay.Candidate -Candidate $replayAttempt
    }

    $zero = New-BaseFixture
    Assert-Rejected -Name 'zero trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $zero -Base '0000000000000000000000000000000000000000' -Candidate $zero.Base
    }

    Assert-Rejected -Name 'short trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $zero -Base $zero.Base.Substring(0, 12) -Candidate $zero.Base
    }

    Assert-Rejected -Name 'symbolic trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $zero -Base 'HEAD' -Candidate $zero.Base
    }

    Assert-Rejected -Name 'unexpected positional argument rejected' -ExpectedCode 'PARAMETER' -Action {
        Invoke-PowerShellResult -Arguments @(
            '-File', $TrustedWrapperPath,
            '-RepositoryRoot', $zero.Root,
            '-TrustedBaseRevision', $zero.Base,
            '-CandidateRevision', $zero.Base,
            'unexpected')
    }

    $candidateNotHead = New-BaseFixture
    Write-Utf8File -Path (Join-Path $candidateNotHead.Root 'docs/later.md') -Content "later`n"
    $laterHead = Commit-All -Root $candidateNotHead.Root -Message 'later head'
    Assert-Rejected -Name 'candidate revision must equal checked-out HEAD' -ExpectedCode 'REVISION' -Action {
        Invoke-PowerShellResult -Arguments @(
            '-File', $TrustedWrapperPath,
            '-RepositoryRoot', $candidateNotHead.Root,
            '-TrustedBaseRevision', $candidateNotHead.Base,
            '-CandidateRevision', $candidateNotHead.Base)
    }

    $analyzerBypass = New-BaseFixture
    Write-Utf8File -Path (Join-Path $analyzerBypass.Root 'src/Analyzers/Bypass.cs') -Content "internal sealed class Bypass { }`n"
    $analyzerCandidate = Commit-All -Root $analyzerBypass.Root -Message 'attempt analyzer bypass'

    $webTestBypass = New-BaseFixture
    Write-Utf8File `
        -Path (Join-Path $webTestBypass.Root 'src/vues/AICopilot.Web/tests/smoke/extra.test.ts') `
        -Content "export const alternatePlaywrightCase = true`n"
    Write-Utf8File `
        -Path (Join-Path $webTestBypass.Root 'src/vues/AICopilot.Web/tests/smoke/start-smoke.mjs') `
        -Content "export const startSmoke = 'modified without receipt'`n"
    Write-Utf8File `
        -Path (Join-Path $webTestBypass.Root '.github/workflows/aicopilot-ci.yml') `
        -Content "$(Get-RequiredWorkflowContent)`n"
    $webTestCandidate = Commit-All -Root $webTestBypass.Root -Message 'attempt Web test asset bypass'
    $webTestBypass | Add-Member -NotePropertyName Template -NotePropertyValue $webTestCandidate -Force

    Assert-Rejected -Name 'analyzer and Web test assets cannot change without receipt' -ExpectedCode 'IMMUTABLE' -Action {
        $description = Invoke-DescribeResult -Fixture $webTestBypass -RuleIdsCsv 'AI-TEST-UI-001'
        if ($description.ExitCode -ne 0) {
            throw "could not inspect Web test asset state delta: $($description.Output)"
        }
        $receipt = ConvertFrom-TestJson -Json $description.Output
        if ([long]$receipt.source.counts.playwrightSourceFiles -ne 2 -or
            [long]$receipt.target.counts.playwrightSourceFiles -ne 3 -or
            [string]$receipt.source.protectedManifestSha256 -ceq
                [string]$receipt.target.protectedManifestSha256) {
            throw 'Web .test.ts/helper mutation did not change the reviewed count and protected manifest.'
        }

        $analyzerResult = Invoke-Validation `
            -Fixture $analyzerBypass `
            -Base $analyzerBypass.Base `
            -Candidate $analyzerCandidate
        if ($analyzerResult.ExitCode -eq 0 -or
            $analyzerResult.Output -notmatch 'AI-TEST-GOV-MIG-001-IMMUTABLE') {
            throw "analyzer bypass was not rejected as IMMUTABLE: $($analyzerResult.Output)"
        }
        Invoke-Validation `
            -Fixture $webTestBypass `
            -Base $webTestBypass.Base `
            -Candidate $webTestCandidate
    }

    $wrapperBypass = New-BaseFixture
    Write-Utf8File `
        -Path (Join-Path $wrapperBypass.Root 'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1') `
        -Content "exit 0`n"
    $wrapperCandidate = Commit-All -Root $wrapperBypass.Root -Message 'attempt wrapper bypass'
    Assert-Rejected -Name 'base-extracted wrapper ignores candidate wrapper bypass' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $wrapperBypass -Base $wrapperBypass.Base -Candidate $wrapperCandidate
    }

    $releaseAnchor = New-BaseFixture
    Write-Utf8File -Path (Join-Path $releaseAnchor.Root 'docs/main-ahead.md') -Content "main ahead`n"
    $trustedMain = Commit-All -Root $releaseAnchor.Root -Message 'unprotected main advance'
    Assert-Pass -Name 'release anchor allows unprotected main advance' -ExpectedText 'trusted release anchor passed' -Action {
        Invoke-Validation `
            -Fixture $releaseAnchor `
            -Base $trustedMain `
            -Candidate $releaseAnchor.Base `
            -Relationship 'HeadAncestorOfBase'
    }

    $releaseProtected = New-BaseFixture
    Write-Utf8File -Path (Join-Path $releaseProtected.Root 'src/tests/Sample.Tests/NewCase.cs') -Content "internal sealed class NewCase { }`n"
    $protectedMain = Commit-All -Root $releaseProtected.Root -Message 'protected main advance'
    Assert-Rejected -Name 'release anchor rejects protected drift' -ExpectedCode 'RELEASE' -Action {
        Invoke-Validation `
            -Fixture $releaseProtected `
            -Base $protectedMain `
            -Candidate $releaseProtected.Base `
            -Relationship 'HeadAncestorOfBase'
    }

    $wrongReleaseDirection = New-TemplateCandidate -Fixture (New-BaseFixture)
    Assert-Rejected -Name 'release anchor rejects wrong ancestry direction' -ExpectedCode 'ANCESTRY' -Action {
        Invoke-Validation `
            -Fixture $wrongReleaseDirection `
            -Base $wrongReleaseDirection.Base `
            -Candidate $wrongReleaseDirection.Template `
            -Relationship 'HeadAncestorOfBase'
    }

    $sideRelease = New-BaseFixture
    Invoke-Git -Root $sideRelease.Root -Arguments @('checkout', '--quiet', '-b', 'release-side', $sideRelease.Base)
    Write-Utf8File -Path (Join-Path $sideRelease.Root 'docs/side-release.md') -Content "side`n"
    $sideReleaseCandidate = Commit-All -Root $sideRelease.Root -Message 'side release candidate'
    Invoke-Git -Root $sideRelease.Root -Arguments @('checkout', '--quiet', $sideRelease.Base)
    Write-Utf8File -Path (Join-Path $sideRelease.Root 'docs/main-first-parent.md') -Content "main`n"
    [void](Commit-All -Root $sideRelease.Root -Message 'main first-parent advance')
    Invoke-Git -Root $sideRelease.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'merge side release', $sideReleaseCandidate)
    $sideReleaseTrustedMain = Invoke-Git -Root $sideRelease.Root -Arguments @('rev-parse', 'HEAD') -Capture

    $firstParentMergeRelease = New-BaseFixture
    $firstParentReleaseCandidate = $firstParentMergeRelease.Base
    Write-Utf8File `
        -Path (Join-Path $firstParentMergeRelease.Root 'docs/release-main.md') `
        -Content "main`n"
    $releaseMainBeforeMerge = Commit-All `
        -Root $firstParentMergeRelease.Root `
        -Message 'release main before merge'
    Invoke-Git -Root $firstParentMergeRelease.Root -Arguments @(
        'checkout', '--quiet', '-b', 'release-linear-side', $releaseMainBeforeMerge)
    Write-Utf8File `
        -Path (Join-Path $firstParentMergeRelease.Root 'docs/release-side-merge.md') `
        -Content "side`n"
    $releaseLinearSide = Commit-All `
        -Root $firstParentMergeRelease.Root `
        -Message 'release linear side'
    Invoke-Git -Root $firstParentMergeRelease.Root -Arguments @(
        'checkout', '--quiet', $releaseMainBeforeMerge)
    Invoke-Git -Root $firstParentMergeRelease.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'release first-parent merge', $releaseLinearSide)
    $firstParentMergeTrustedMain = Invoke-Git `
        -Root $firstParentMergeRelease.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture

    $mergeEndpointRelease = New-BaseFixture
    Invoke-Git -Root $mergeEndpointRelease.Root -Arguments @(
        'checkout', '--quiet', '-b', 'endpoint-side', $mergeEndpointRelease.Base)
    Write-Utf8File `
        -Path (Join-Path $mergeEndpointRelease.Root 'docs/endpoint-side.md') `
        -Content "endpoint side`n"
    $endpointSide = Commit-All `
        -Root $mergeEndpointRelease.Root `
        -Message 'release endpoint side'
    Invoke-Git -Root $mergeEndpointRelease.Root -Arguments @(
        'checkout', '--quiet', $mergeEndpointRelease.Base)
    Write-Utf8File `
        -Path (Join-Path $mergeEndpointRelease.Root 'docs/endpoint-main.md') `
        -Content "endpoint main`n"
    [void](Commit-All `
        -Root $mergeEndpointRelease.Root `
        -Message 'release endpoint main')
    Invoke-Git -Root $mergeEndpointRelease.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'untrusted release merge endpoint', $endpointSide)
    $mergeReleaseCandidate = Invoke-Git `
        -Root $mergeEndpointRelease.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Write-Utf8File `
        -Path (Join-Path $mergeEndpointRelease.Root 'docs/trusted-after-endpoint.md') `
        -Content "trusted child`n"
    $mergeEndpointTrustedMain = Commit-All `
        -Root $mergeEndpointRelease.Root `
        -Message 'trusted single-parent child'

    Assert-Rejected -Name 'release anchor requires first-parent membership and a merge-free interval including its untrusted endpoint' -ExpectedCode 'ANCESTRY' -Action {
        $mergeEndpointResult = Invoke-Validation `
            -Fixture $mergeEndpointRelease `
            -Base $mergeEndpointTrustedMain `
            -Candidate $mergeReleaseCandidate `
            -Relationship 'HeadAncestorOfBase'
        if ($mergeEndpointResult.ExitCode -eq 0 -or
            $mergeEndpointResult.Output -notmatch 'AI-TEST-GOV-MIG-001-HISTORY') {
            throw "untrusted merge endpoint was not rejected as HISTORY: $($mergeEndpointResult.Output)"
        }
        $linearReleaseResult = Invoke-Validation `
            -Fixture $firstParentMergeRelease `
            -Base $firstParentMergeTrustedMain `
            -Candidate $firstParentReleaseCandidate `
            -Relationship 'HeadAncestorOfBase'
        if ($linearReleaseResult.ExitCode -eq 0 -or
            $linearReleaseResult.Output -notmatch 'AI-TEST-GOV-MIG-001-HISTORY') {
            throw "first-parent release merge was not rejected as HISTORY: $($linearReleaseResult.Output)"
        }
        Invoke-Validation `
            -Fixture $sideRelease `
            -Base $sideReleaseTrustedMain `
            -Candidate $sideReleaseCandidate `
            -Relationship 'HeadAncestorOfBase'
    }

    $orphan = New-BaseFixture
    Invoke-Git -Root $orphan.Root -Arguments @('checkout', '--quiet', '--orphan', 'orphan-line')
    Invoke-Git -Root $orphan.Root -Arguments @('rm', '--quiet', '-f', '-r', '--ignore-unmatch', '.')
    Write-Utf8File -Path (Join-Path $orphan.Root 'orphan.txt') -Content "orphan`n"
    $orphanCandidate = Commit-All -Root $orphan.Root -Message 'orphan commit'
    Assert-Rejected -Name 'same-repository orphan history rejected' -ExpectedCode 'ANCESTRY' -Action {
        Invoke-Validation -Fixture $orphan -Base $orphan.Base -Candidate $orphanCandidate
    }

    $unrelatedLeft = New-BaseFixture
    $unrelatedRight = New-BaseFixture
    Write-Utf8File -Path (Join-Path $unrelatedRight.Root 'src/App/Unrelated.cs') -Content "internal sealed class Unrelated { }`n"
    $unrelatedRight.Base = Commit-All -Root $unrelatedRight.Root -Message 'unrelated history'
    Assert-Rejected -Name 'unrelated trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $unrelatedLeft -Base $unrelatedRight.Base -Candidate $unrelatedLeft.Base
    }
}
finally {
    foreach ($root in $script:TempRoots) {
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($script:Failed -ne 0 -or $script:Passed -ne $script:ExpectedSelfTests) {
    throw "AICopilot governance migration validator self-tests failed: passed=$($script:Passed) failed=$($script:Failed) expected=$($script:ExpectedSelfTests)."
}

Write-Host "AICopilot governance migration validator self-tests passed: $($script:Passed)/$($script:ExpectedSelfTests)."
$global:LASTEXITCODE = 0
exit 0
