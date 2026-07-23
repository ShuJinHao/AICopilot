Set-StrictMode -Version Latest

function Get-AICopilotCanonicalBaselineRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Compatibility', 'Duplication', 'Mutation')]
        [string]$BaselineKind
    )

    switch ($BaselineKind) {
        'Compatibility' { return 'scripts/tests/baselines/aicopilot-compatibility.json' }
        'Duplication' { return 'scripts/tests/baselines/aicopilot-duplication.json' }
        'Mutation' { return 'scripts/tests/baselines/aicopilot-mutation.json' }
    }
}

function Resolve-AICopilotCanonicalBaselinePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)]
        [ValidateSet('Compatibility', 'Duplication', 'Mutation')]
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
        [ValidateSet('Compatibility', 'Duplication', 'Mutation')]
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

    $baseBaselinePaths = @(
        & git -C $root ls-tree --name-only $baseCommit -- $relativePath 2>$null |
            ForEach-Object { [string]$_ }
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Cannot inspect $BaselineKind baseline '$relativePath' at base commit '$baseCommit'."
    }
    if ($baseBaselinePaths.Count -eq 0) {
        return [pscustomobject]@{
            BaseCommit = $baseCommit
            Mode = 'Bootstrap'
            RelativePath = $relativePath
            BaseBaselineJson = $null
        }
    }
    if ($baseBaselinePaths.Count -ne 1 -or $baseBaselinePaths[0] -cne $relativePath) {
        throw "$BaselineKind baseline '$relativePath' has ambiguous base-tree identity."
    }

    $baseBaselineJson = @(& git -C $root show "$baseCommit`:$relativePath" 2>$null) -join "`n"
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($baseBaselineJson)) {
        throw "Cannot read non-empty $BaselineKind baseline '$relativePath' from base commit '$baseCommit'."
    }

    return [pscustomobject]@{
        BaseCommit = $baseCommit
        Mode = 'Ratchet'
        RelativePath = $relativePath
        BaseBaselineJson = $baseBaselineJson
    }
}
