[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$LedgerPath = 'scripts/tests/baselines/aicopilot-test-declaration-transition.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Sha256 {
    param([Parameter(Mandatory)] [string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

$root = (Resolve-Path $RepositoryRoot).Path
$resolvedLedgerPath = if ([System.IO.Path]::IsPathRooted($LedgerPath)) {
    $LedgerPath
} else {
    Join-Path $root $LedgerPath
}
if (-not (Test-Path $resolvedLedgerPath -PathType Leaf)) {
    throw "AICopilot declaration transition ledger is missing: $resolvedLedgerPath"
}
$ledger = Get-Content $resolvedLedgerPath -Raw | ConvertFrom-Json
if ([int]$ledger.schemaVersion -ne 2) {
    throw "Unsupported declaration transition schemaVersion='$($ledger.schemaVersion)'."
}
$expectedSourceCommit = '198cc59318f4a1748c719b9b8ecff1d969952ce8'
$expectedSourcePath = 'src/tests'
$expectedSourceTree = '88aee67db521a1a33ff6de524c0163d513396123'
$expectedTransitionContentSha256 = 'a4b736c63a19f31ffafc5e7f0a70e46636d1c5f17365654195bb7a63c3eff65a'
if ([string]$ledger.source.commit -cne $expectedSourceCommit -or
    [string]$ledger.source.sourcePath -cne $expectedSourcePath -or
    [string]$ledger.source.gitTree -cne $expectedSourceTree -or
    [int]$ledger.source.declarationCount -ne 785 -or
    [string]$ledger.source.orderedSymbolSha256 -cne '62a8fa4fe89e98b8b7be0a74f020db16c5bbdac3714b1838e194b2bef189e312' -or
    [string]$ledger.source.transitionContentSha256 -cne $expectedTransitionContentSha256) {
    throw 'Declaration transition source provenance differs from the frozen 198cc59318f4a1748c719b9b8ecff1d969952ce8:src/tests tree and 785-declaration symbol set.'
}
$actualSourceTree = ((& git -C $root rev-parse "${expectedSourceCommit}:$expectedSourcePath" 2>$null) -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($actualSourceTree) -or $actualSourceTree -cne $expectedSourceTree) {
    throw "Frozen declaration source tree is unavailable or changed. expected=$expectedSourceTree; actual=$actualSourceTree."
}

$transitions = @($ledger.transitions)
if ($transitions.Count -ne 785) {
    throw "Declaration transition ledger must contain exactly 785 records; found $($transitions.Count)."
}
$duplicateOldSymbols = @($transitions | Group-Object oldSymbol | Where-Object Count -ne 1)
if ($duplicateOldSymbols.Count -ne 0) {
    throw "Declaration transition ledger contains empty or duplicate oldSymbol records: $($duplicateOldSymbols.Name -join ', ')."
}
$orderedSymbols = (($transitions | ForEach-Object { [string]$_.oldSymbol }) -join "`n") + "`n"
$actualSourceHash = Get-Sha256 $orderedSymbols
if ($actualSourceHash -cne [string]$ledger.source.orderedSymbolSha256) {
    throw "Declaration transition ordered oldSymbol SHA differs from the frozen source. expected=$($ledger.source.orderedSymbolSha256); actual=$actualSourceHash."
}
$transitionContent = [Text.StringBuilder]::new()
foreach ($transition in $transitions) {
    [void]$transitionContent.Append([string]$transition.oldSymbol)
    [void]$transitionContent.Append([char]0)
    [void]$transitionContent.Append([string]$transition.disposition)
    [void]$transitionContent.Append([char]0)
    [void]$transitionContent.Append((@($transition.replacementIds) -join [char]0))
    [void]$transitionContent.Append([char]0)
    [void]$transitionContent.Append([string]$transition.reason)
    [void]$transitionContent.Append("`n")
}
$actualTransitionContentSha256 = Get-Sha256 $transitionContent.ToString()
if ($actualTransitionContentSha256 -cne $expectedTransitionContentSha256) {
    throw "Declaration transition disposition/replacement/reason content differs from the frozen controlled review. expected=$expectedTransitionContentSha256; actual=$actualTransitionContentSha256."
}

$currentBaselineRelativePath = [string]$ledger.current.baselinePath
if ($currentBaselineRelativePath -cne 'scripts/tests/baselines/aicopilot-test-cases.json') {
    throw "Declaration transition ledger points at an unexpected current baseline '$currentBaselineRelativePath'."
}
$currentBaseline = Get-Content (Join-Path $root $currentBaselineRelativePath) -Raw | ConvertFrom-Json
$currentIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($case in @($currentBaseline.cases)) {
    [void]$currentIds.Add("$([string]$case.class).$([string]$case.method)")
}
if ([int]$ledger.current.availableDeclarationCount -gt $currentIds.Count) {
    throw "Current declaration inventory shrank below the ledger generation point. ledger=$($ledger.current.availableDeclarationCount); actual=$($currentIds.Count)."
}

$analyzerSources = @(
    Get-ChildItem (Join-Path $root 'src/analyzers') -Filter '*.cs' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object { Get-Content $_.FullName -Raw }
) -join "`n"
$webBehaviorSources = @{
    'WEB:AICopilot.Web.unit.frontend-lint-contract.rejects-bare-catch-clauses' = @{
        Path = 'src/vues/AICopilot.Web/tests/unit/frontendLintContract.spec.ts'
        Needle = "it('rejects bare catch clauses in TypeScript and Vue while accepting named catches'"
    }
    'WEB:AICopilot.Web.unit.chat-errors.scope-current-session' = @{
        Path = 'src/vues/AICopilot.Web/tests/unit/chatErrorStore.spec.ts'
        Needle = "it('scopes active errors to the current session'"
    }
    'WEB:AICopilot.Web.unit.config.refresh-fixed-agent-slots' = @{
        Path = 'src/vues/AICopilot.Web/tests/unit/configStoreFacade.spec.ts'
        Needle = "it('refreshes only fixed agent slot domains'"
    }
    'WEB:AICopilot.Web.unit.rag.upload-delete-governance-facade' = @{
        Path = 'src/vues/AICopilot.Web/tests/unit/ragStoreFacade.spec.ts'
        Needle = "it('keeps upload/delete/governance behavior behind the facade'"
    }
    'WEB:AICopilot.Web.smoke.inline-agent-run-restores-session-state' = @{
        Path = 'src/vues/AICopilot.Web/tests/smoke/acceptance.spec.ts'
        Needle = "test('inline agent run restores task, workspace, approvals, and artifacts'"
    }
    'WEB:AICopilot.Web.smoke.config-fixed-slots-no-internal-preload' = @{
        Path = 'src/vues/AICopilot.Web/tests/smoke/acceptance.spec.ts'
        Needle = "test('config renders fixed agent slots without internal operations preload'"
    }
}
foreach ($webBehavior in $webBehaviorSources.GetEnumerator()) {
    $webSourcePath = Join-Path $root ([string]$webBehavior.Value.Path)
    if (-not (Test-Path $webSourcePath -PathType Leaf) -or
        -not (Get-Content $webSourcePath -Raw).Contains(
            [string]$webBehavior.Value.Needle,
            [StringComparison]::Ordinal)) {
        throw "Current web behavior '$($webBehavior.Key)' is missing its exact Vitest/Playwright declaration."
    }
}
$allowedDispositions = @('retained-self', 'retired-duplicate', 'retired-source-guard', 'replaced')
$computedCounts = @{}
foreach ($name in $allowedDispositions) {
    $computedCounts[$name] = 0
}

foreach ($transition in $transitions) {
    $oldSymbol = [string]$transition.oldSymbol
    $disposition = [string]$transition.disposition
    $replacementIds = @($transition.replacementIds | ForEach-Object { [string]$_ })
    $reason = [string]$transition.reason

    if ([string]::IsNullOrWhiteSpace($oldSymbol) -or $oldSymbol -notmatch '\([^()]*\)$') {
        throw "Declaration transition has an invalid oldSymbol '$oldSymbol'."
    }
    if ($disposition -notin $allowedDispositions) {
        throw "$oldSymbol has unknown disposition '$disposition'."
    }
    $computedCounts[$disposition]++
    if ([string]::IsNullOrWhiteSpace($reason) -or $reason -match '(?i)rename|重命名') {
        throw "$oldSymbol must have a concrete transition reason and must not use a generic rename claim."
    }
    if (@($replacementIds | Group-Object | Where-Object Count -ne 1).Count -ne 0) {
        throw "$oldSymbol has empty or duplicate replacementIds."
    }

    if ($disposition -in @('retired-duplicate', 'retired-source-guard')) {
        if ($replacementIds.Count -ne 0) {
            throw "$oldSymbol is retired and must not declare replacementIds."
        }
        if ($disposition -eq 'retired-source-guard' -and $reason -notmatch '(?i)source|源码|反射|正则|字符串') {
            throw "$oldSymbol is retired-source-guard but the reason does not state the deleted source/reflection/string mechanism."
        }
        if ($disposition -eq 'retired-duplicate' -and $reason -notmatch '(?i)duplicate|重复') {
            throw "$oldSymbol is retired-duplicate but the reason does not identify the duplicate coverage."
        }
        continue
    }

    if ($replacementIds.Count -eq 0) {
        throw "$oldSymbol is $disposition and must resolve to at least one current declaration or Analyzer diagnostic."
    }
    if ($disposition -eq 'retained-self') {
        $oldId = $oldSymbol -replace '\([^()]*\)$', ''
        $expectedMethodName = $oldId.Substring($oldId.LastIndexOf('.') + 1)
        $actualMethodName = $replacementIds[0].Substring($replacementIds[0].LastIndexOf('.') + 1)
        if ($replacementIds.Count -ne 1 -or $actualMethodName -cne $expectedMethodName) {
            throw "$oldSymbol is retained-self but does not resolve to the same method name '$expectedMethodName'."
        }
    }

    foreach ($replacementId in $replacementIds) {
        if ($replacementId.StartsWith('WEB:', [StringComparison]::Ordinal)) {
            if (-not $webBehaviorSources.ContainsKey($replacementId)) {
                throw "$oldSymbol resolves to unknown exact web behavior '$replacementId'."
            }
        } elseif ($replacementId.StartsWith('ANALYZER:', [StringComparison]::Ordinal)) {
            $diagnosticId = $replacementId.Substring('ANALYZER:'.Length)
            $diagnosticNeedle = '"' + $diagnosticId + '"'
            if ($diagnosticId -notmatch '^AIARCH[0-9]{3}$' -or
                -not $analyzerSources.Contains($diagnosticNeedle, [StringComparison]::Ordinal)) {
                throw "$oldSymbol resolves to unknown Analyzer diagnostic '$replacementId'."
            }
        } elseif (-not $currentIds.Contains($replacementId)) {
            throw "$oldSymbol resolves to unknown current declaration '$replacementId'."
        }
    }
}

$templateRetirements = @(
    $transitions |
        Where-Object disposition -eq 'retired-source-guard' |
        Group-Object reason |
        Where-Object Count -gt 1
)
if ($templateRetirements.Count -ne 0) {
    throw "retired-source-guard reasons must be exact per declaration; repeated template reason: $($templateRetirements[0].Name)"
}

$expectedSummary = @{
    'retained-self' = [int]$ledger.summary.retainedSelf
    'retired-duplicate' = [int]$ledger.summary.retiredDuplicate
    'retired-source-guard' = [int]$ledger.summary.retiredSourceGuard
    'replaced' = [int]$ledger.summary.replaced
}
foreach ($name in $allowedDispositions) {
    if ($computedCounts[$name] -ne $expectedSummary[$name]) {
        throw "Declaration transition summary differs for $name. expected=$($expectedSummary[$name]); actual=$($computedCounts[$name])."
    }
}

Write-Host "AICopilot declaration transition ledger passed. old=785; current=$($currentIds.Count); retained=$($computedCounts['retained-self']); replaced=$($computedCounts['replaced']); retiredDuplicate=$($computedCounts['retired-duplicate']); retiredSourceGuard=$($computedCounts['retired-source-guard']); unknown=0."
