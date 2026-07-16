Set-StrictMode -Version Latest

function Get-AICopilotCanonicalBaselineRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Compatibility', 'Coverage', 'Duplication', 'Mutation', 'TestCases')]
        [string]$BaselineKind
    )

    switch ($BaselineKind) {
        'Compatibility' { return 'scripts/tests/baselines/aicopilot-compatibility.json' }
        'Coverage' { return 'scripts/tests/baselines/aicopilot-coverage.json' }
        'Duplication' { return 'scripts/tests/baselines/aicopilot-duplication.json' }
        'Mutation' { return 'scripts/tests/baselines/aicopilot-mutation.json' }
        'TestCases' { return 'scripts/tests/baselines/aicopilot-test-cases.json' }
    }
}

function Resolve-AICopilotCanonicalBaselinePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)]
        [ValidateSet('Compatibility', 'Coverage', 'Duplication', 'Mutation', 'TestCases')]
        [string]$BaselineKind,
        [Parameter(Mandatory)] [string]$BaselinePath
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $fullPath = if ([IO.Path]::IsPathRooted($BaselinePath)) {
        [IO.Path]::GetFullPath($BaselinePath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $root $BaselinePath))
    }
    $relativePath = [IO.Path]::GetRelativePath($root, $fullPath).Replace('\', '/')
    if ($relativePath -eq '..' -or $relativePath.StartsWith('../', [StringComparison]::Ordinal)) {
        throw "$BaselineKind baseline path escapes the repository root."
    }

    $canonicalPath = Get-AICopilotCanonicalBaselineRelativePath -BaselineKind $BaselineKind
    if ($relativePath -cne $canonicalPath) {
        throw "$BaselineKind baseline identity is fixed at '$canonicalPath'; candidate path '$relativePath' cannot rename or reset it."
    }

    return [pscustomobject]@{
        FullPath = $fullPath
        RelativePath = $relativePath
    }
}

function Resolve-AICopilotQualityBase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [string]$BaseRef
    )

    if ([string]::IsNullOrWhiteSpace($BaseRef) -or $BaseRef -match '^0+$') {
        throw 'A non-empty, non-zero base ref is required.'
    }

    $head = [string](& git -C $RepositoryRoot rev-parse 'HEAD^{commit}' 2>$null | Select-Object -First 1)
    $resolved = [string](& git -C $RepositoryRoot rev-parse "$BaseRef^{commit}" 2>$null | Select-Object -First 1)
    $head = $head.Trim()
    $resolved = $resolved.Trim()
    if ($head -notmatch '^[0-9a-f]{40}$' -or $resolved -notmatch '^[0-9a-f]{40}$') {
        throw "Cannot resolve base ref '$BaseRef' and candidate HEAD to full commit SHAs."
    }
    if ($resolved -ceq $head) {
        throw 'Base ref must not resolve to candidate HEAD.'
    }

    & git -C $RepositoryRoot merge-base --is-ancestor $resolved $head 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Base ref '$resolved' is not an ancestor of candidate HEAD '$head'."
    }

    return $resolved
}

function Get-AICopilotBaselineContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [string]$BaseRef,
        [Parameter(Mandatory)]
        [ValidateSet('Compatibility', 'Coverage', 'Duplication', 'Mutation')]
        [string]$BaselineKind,
        [Parameter(Mandatory)] [string]$BaselinePath
    )

    $baseCommit = Resolve-AICopilotQualityBase -RepositoryRoot $RepositoryRoot -BaseRef $BaseRef
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $identity = Resolve-AICopilotCanonicalBaselinePath `
        -RepositoryRoot $root `
        -BaselineKind $BaselineKind `
        -BaselinePath $BaselinePath
    $relativePath = [string]$identity.RelativePath

    $baseBaselineJson = @(& git -C $root show "$baseCommit`:$relativePath" 2>$null) -join "`n"
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($baseBaselineJson)) {
        return [pscustomobject]@{
            BaseCommit = $baseCommit
            Mode = 'Ratchet'
            RelativePath = $relativePath
            BaseBaselineJson = $baseBaselineJson
        }
    }

    return [pscustomobject]@{
        BaseCommit = $baseCommit
        Mode = 'Bootstrap'
        RelativePath = $relativePath
        BaseBaselineJson = $null
    }
}
