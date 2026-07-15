[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$InventoryPath = (Join-Path $PSScriptRoot 'aicopilot-compatibility-inventory.json'),
    [string]$BaselinePath = (Join-Path $PSScriptRoot 'baselines/aicopilot-compatibility.json'),
    [string]$OutputPath = 'artifacts/quality/aicopilot-compatibility.json',
    [switch]$UpdateBaseline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:sourceLineCache = @{}
$script:sourceTextCache = @{}
$script:roslynLoaded = $false

function Import-RoslynParser {
    if ($script:roslynLoaded) {
        return
    }
    if ($null -ne ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) {
        $script:roslynLoaded = $true
        return
    }
    $sdkLine = (& dotnet --list-sdks | Select-Object -Last 1)
    if ([string]$sdkLine -notmatch '^\s*[^\s]+\s+\[(?<root>[^\]]+)\]') {
        throw 'Unable to locate the .NET SDK Roslyn parser.'
    }
    $sdkVersion = ([string]$sdkLine).Trim().Split(' ')[0]
    $roslynRoot = Join-Path ([string]$Matches.root) "$sdkVersion/Roslyn/bincore"
    Add-Type -Path (Join-Path $roslynRoot 'Microsoft.CodeAnalysis.dll')
    Add-Type -Path (Join-Path $roslynRoot 'Microsoft.CodeAnalysis.CSharp.dll')
    $script:roslynLoaded = $true
}

function Remove-CodeComments {
    param(
        [Parameter(Mandatory)] [string]$Text,
        [switch]$RemoveStrings
    )

    $builder = [Text.StringBuilder]::new($Text.Length)
    $state = 'code'
    $quote = [char]0
    for ($index = 0; $index -lt $Text.Length; $index++) {
        $current = $Text[$index]
        $next = if ($index + 1 -lt $Text.Length) { $Text[$index + 1] } else { [char]0 }
        if ($state -eq 'lineComment') {
            if ($current -in "`r", "`n") { $state = 'code'; [void]$builder.Append($current) } else { [void]$builder.Append(' ') }
            continue
        }
        if ($state -eq 'blockComment') {
            if ($current -eq '*' -and $next -eq '/') {
                [void]$builder.Append(' '); [void]$builder.Append(' '); $index++; $state = 'code'
            } elseif ($current -in "`r", "`n") { [void]$builder.Append($current) } else { [void]$builder.Append(' ') }
            continue
        }
        if ($state -eq 'string') {
            if ($current -eq '\\') {
                [void]$builder.Append($(if ($RemoveStrings) { ' ' } else { $current }))
                if ($index + 1 -lt $Text.Length) {
                    $index++; [void]$builder.Append($(if ($RemoveStrings) { ' ' } else { $Text[$index] }))
                }
            } elseif ($current -eq $quote) {
                [void]$builder.Append($(if ($RemoveStrings) { ' ' } else { $current })); $state = 'code'
            } elseif ($current -in "`r", "`n") {
                [void]$builder.Append($current)
            } else {
                [void]$builder.Append($(if ($RemoveStrings) { ' ' } else { $current }))
            }
            continue
        }
        if ($current -eq '/' -and $next -eq '/') {
            [void]$builder.Append(' '); [void]$builder.Append(' '); $index++; $state = 'lineComment'; continue
        }
        if ($current -eq '/' -and $next -eq '*') {
            [void]$builder.Append(' '); [void]$builder.Append(' '); $index++; $state = 'blockComment'; continue
        }
        if ($current -in @('"', "'", '`')) {
            $quote = $current; $state = 'string'; [void]$builder.Append($(if ($RemoveStrings) { ' ' } else { $current })); continue
        }
        [void]$builder.Append($current)
    }
    $builder.ToString()
}

function Get-CSharpCallerCount {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Token,
        [string[]]$ExcludeContains = @()
    )

    Import-RoslynParser
    $rootNode = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        (Get-SourceText $Path)).GetRoot()
    $normalizedToken = ($Token -replace '\s+', '')
    if ($normalizedToken.EndsWith('(', [StringComparison]::Ordinal)) {
        $target = $normalizedToken.Substring(0, $normalizedToken.Length - 1).TrimStart('.')
        return @(
            $rootNode.DescendantNodes() |
                Where-Object { $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax] } |
                Where-Object {
                    $nodeText = $_.ToString()
                    $expression = ($_.Expression.ToString() -replace '\s+', '')
                    ($expression -ceq $target -or $expression.EndsWith(".$target", [StringComparison]::Ordinal)) -and
                    @($ExcludeContains | Where-Object { $nodeText.Contains([string]$_, [StringComparison]::Ordinal) }).Count -eq 0
                }
        ).Count
    }

    $syntaxText = ($rootNode.DescendantTokens() | ForEach-Object { $_.Text }) -join ' '
    return [regex]::Matches(
        ($syntaxText -replace '\s+', ''),
        [regex]::Escape($normalizedToken)).Count
}

function Get-ScriptCallerCount {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Token,
        [string[]]$ExcludeContains = @()
    )
    $sourceText = Get-SourceText $Path
    $clean = Remove-CodeComments $sourceText -RemoveStrings
    $count = 0
    foreach ($line in $clean -split '\r?\n') {
        if (@($ExcludeContains | Where-Object { $line.Contains([string]$_, [StringComparison]::Ordinal) }).Count -ne 0) {
            continue
        }
        if ($Token.EndsWith('(', [StringComparison]::Ordinal)) {
            $name = [regex]::Escape($Token.Substring(0, $Token.Length - 1).TrimStart('.'))
            if ($line -match "(?i)^\s*(?:export\s+)?(?:async\s+)?function\s+$name\s*\(") {
                continue
            }
        }
        $count += [regex]::Matches($line, [regex]::Escape($Token)).Count
    }
    if ([IO.Path]::GetExtension($Path) -eq '.vue') {
        $commentClean = Remove-CodeComments $sourceText
        $directivePattern = '(?i)(?:\bv-[\w-]+|[:@][\w-]+)\s*=\s*["''][^"'']*' +
            [regex]::Escape($Token)
        foreach ($line in $commentClean -split '\r?\n') {
            if ($line -match $directivePattern) {
                $count += [regex]::Matches($line, [regex]::Escape($Token)).Count
            }
        }
    }
    return $count
}

function Get-SourceLines {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not $script:sourceLineCache.ContainsKey($Path)) {
        $script:sourceLineCache[$Path] = @(Get-Content $Path)
    }
    return @($script:sourceLineCache[$Path])
}

function Get-SourceText {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not $script:sourceTextCache.ContainsKey($Path)) {
        $script:sourceTextCache[$Path] = Get-Content $Path -Raw
    }
    return [string]$script:sourceTextCache[$Path]
}

function Read-JsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path $Path -PathType Leaf)) {
        throw "Required JSON file does not exist: $Path"
    }

    Get-Content $Path -Raw | ConvertFrom-Json -Depth 32
}

function Assert-TextEvidence {
    param(
        [Parameter(Mandatory)]$Evidence,
        [Parameter(Mandatory)][string]$Context
    )

    foreach ($property in @('path', 'contains')) {
        $value = [string]$Evidence.$property
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "$Context is missing non-empty '$property'."
        }
    }

    $repositoryFullPath = [IO.Path]::GetFullPath($RepositoryRoot) + [IO.Path]::DirectorySeparatorChar
    $evidencePath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot ([string]$Evidence.path)))
    if (-not $evidencePath.StartsWith($repositoryFullPath, [StringComparison]::Ordinal)) {
        throw "$Context escapes the repository root: $($Evidence.path)"
    }
    if (-not (Test-Path $evidencePath -PathType Leaf)) {
        throw "$Context references a missing file: $($Evidence.path)"
    }

    $source = Remove-CodeComments (Get-SourceText $evidencePath)
    if (-not $source.Contains([string]$Evidence.contains, [StringComparison]::Ordinal)) {
        throw "$Context is stale; '$($Evidence.contains)' was not found in $($Evidence.path)."
    }
}

function Get-CallerCount {
    param(
        [Parameter(Mandatory)]$Scan,
        [Parameter(Mandatory)][string]$Context
    )

    $roots = @($Scan.roots)
    $extensions = @($Scan.extensions)
    $token = [string]$Scan.contains
    if ($roots.Count -eq 0 -or $extensions.Count -eq 0 -or [string]::IsNullOrWhiteSpace($token)) {
        throw "$Context requires non-empty roots, extensions and contains."
    }
    if (@($extensions | Where-Object { [string]$_ -notmatch '^\.[a-z0-9]+$' }).Count -ne 0) {
        throw "$Context contains invalid extensions."
    }

    $repositoryFullPath = [IO.Path]::GetFullPath($RepositoryRoot) + [IO.Path]::DirectorySeparatorChar
    $excluded = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($relativePath in @($Scan.excludePaths)) {
        $fullPath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot ([string]$relativePath)))
        if (-not $fullPath.StartsWith($repositoryFullPath, [StringComparison]::Ordinal)) {
            throw "$Context excludePath escapes the repository root: $relativePath"
        }
        $null = $excluded.Add($fullPath)
    }

    $files = [Collections.Generic.List[IO.FileInfo]]::new()
    foreach ($relativeRoot in $roots) {
        $rootPath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot ([string]$relativeRoot)))
        if (-not ($rootPath + [IO.Path]::DirectorySeparatorChar).StartsWith(
                $repositoryFullPath,
                [StringComparison]::Ordinal)) {
            throw "$Context root escapes the repository: $relativeRoot"
        }
        if (-not (Test-Path $rootPath -PathType Container)) {
            throw "$Context root does not exist: $relativeRoot"
        }
        foreach ($file in Get-ChildItem $rootPath -Recurse -File) {
            if ($file.Extension -in $extensions -and
                $file.FullName -notmatch '[\\/](bin|obj|node_modules|dist|coverage)[\\/]' -and
                -not $excluded.Contains($file.FullName)) {
                $files.Add($file)
            }
        }
    }

    [array]$excludeContains = @()
    if ($null -ne $Scan.PSObject.Properties['excludeContains']) {
        $excludeContains = @($Scan.excludeContains)
    }
    $count = 0
    foreach ($file in @($files | Sort-Object FullName -Unique)) {
        $count += if ($file.Extension -eq '.cs') {
            Get-CSharpCallerCount $file.FullName $token $excludeContains
        } else {
            Get-ScriptCallerCount $file.FullName $token $excludeContains
        }
    }
    $count
}

$inventory = Read-JsonFile $InventoryPath
if ([int]$inventory.schemaVersion -ne 2) {
    throw "Unsupported compatibility inventory schemaVersion '$($inventory.schemaVersion)'."
}

$items = @($inventory.items)
$ordinaryAbstractions = @($inventory.ordinaryAbstractions)
$duplicateIds = @(@($items) + @($ordinaryAbstractions) | Group-Object id | Where-Object Count -gt 1)
if ($duplicateIds.Count -ne 0) {
    throw "Compatibility inventory contains duplicate IDs: $($duplicateIds.Name -join ', ')."
}

$callSiteCounts = @{}

foreach ($item in $items) {
    $context = "Compatibility item '$($item.id)'"
    foreach ($property in @('id', 'replacement', 'deletionCondition', 'latestDeletionBatch', 'deletionDeadline')) {
        if ([string]::IsNullOrWhiteSpace([string]$item.$property)) {
            throw "$context is missing non-empty '$property'."
        }
    }

    if ([string]$item.latestDeletionBatch -notmatch '^\d{4}-(0[1-9]|1[0-2])$') {
        throw "$context has invalid latestDeletionBatch '$($item.latestDeletionBatch)'."
    }

    $deadline = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact(
            [string]$item.deletionDeadline,
            'yyyy-MM-dd',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$deadline)) {
        throw "$context has invalid deletionDeadline '$($item.deletionDeadline)'."
    }
    if ($deadline.Date -lt [DateTime]::UtcNow.Date) {
        throw "$context expired on $($item.deletionDeadline); delete it or approve a new deadline explicitly."
    }

    Assert-TextEvidence $item.producer "$context producer"
    Assert-TextEvidence $item.consumer "$context consumer"

    $callEvidence = @($item.callEvidence)
    $coverageTests = @($item.coverageTests)
    if ($callEvidence.Count -eq 0 -or $coverageTests.Count -eq 0) {
        throw "$context requires at least one callEvidence and one coverageTests entry."
    }

    for ($index = 0; $index -lt $callEvidence.Count; $index++) {
        Assert-TextEvidence $callEvidence[$index] "$context callEvidence[$index]"
    }
    for ($index = 0; $index -lt $coverageTests.Count; $index++) {
        Assert-TextEvidence $coverageTests[$index] "$context coverageTests[$index]"
    }
    if ($null -ne $item.PSObject.Properties['candidateEvidence']) {
        foreach ($evidence in @($item.candidateEvidence)) {
            Assert-TextEvidence $evidence "$context candidateEvidence"
        }
    }

    $callerCount = Get-CallerCount $item.callerScan "$context callerScan"
    if ($callerCount -le 0) {
        throw "$context has no production call sites; physically delete the compatibility path and its ledger item."
    }
    $callSiteCounts["$([string]$item.id)/primary"] = $callerCount
}

foreach ($item in $ordinaryAbstractions) {
    $context = "Ordinary abstraction '$($item.id)'"
    foreach ($property in @('id', 'disposition', 'decisionReason', 'replacement', 'deletionCondition')) {
        if ([string]::IsNullOrWhiteSpace([string]$item.$property)) {
            throw "$context is missing non-empty '$property'."
        }
    }
    if ([string]$item.disposition -ne 'ordinaryAbstraction') {
        throw "$context must use disposition=ordinaryAbstraction."
    }

    Assert-TextEvidence $item.producer "$context producer"
    $consumers = @($item.consumers)
    $candidateEvidence = @($item.candidateEvidence)
    $callerScans = @($item.callerScans)
    if ($consumers.Count -eq 0 -or $candidateEvidence.Count -eq 0 -or $callerScans.Count -eq 0) {
        throw "$context requires consumers, candidateEvidence and callerScans."
    }
    foreach ($consumer in $consumers) {
        Assert-TextEvidence $consumer "$context consumer"
    }
    foreach ($evidence in $candidateEvidence) {
        Assert-TextEvidence $evidence "$context candidateEvidence"
    }

    $duplicateScanIds = @($callerScans | Group-Object id | Where-Object Count -gt 1)
    if ($duplicateScanIds.Count -ne 0) {
        throw "$context has duplicate callerScan IDs: $($duplicateScanIds.Name -join ', ')."
    }
    foreach ($scan in $callerScans) {
        if ([string]::IsNullOrWhiteSpace([string]$scan.id)) {
            throw "$context callerScan requires a non-empty id."
        }
        $callerCount = Get-CallerCount $scan "$context callerScan '$($scan.id)'"
        if ($callerCount -le 0) {
            throw "$context callerScan '$($scan.id)' has no production call sites; physically delete the abstraction."
        }
        $callSiteCounts["$([string]$item.id)/$([string]$scan.id)"] = $callerCount
    }
}

$signalNamePattern = '(?:Alias|Adapter|Wrapper|Fallback|Compatibility|Legacy|Shadow|DualWrite|Obsolete)'
$csharpCandidatePattern = "(?i)^\s*(?:(?:public|private|protected|internal|static|async|sealed|virtual|override|partial|readonly|abstract|extern|new)\s+)*(?:class|interface|record|struct)\s+\w*$signalNamePattern\w*|^\s*(?:(?:public|private|protected|internal)\s+)(?:(?:static|async|sealed|virtual|override|partial|readonly|abstract|extern|new)\s+)*[\w.<>,?\[\]]+\s+\w*$signalNamePattern\w*\s*\(|^\s*[\w.<>,?\[\]]+\s+\w*$signalNamePattern\w*\s*\([^;]*\);\s*$|^\s*(?:(?:public|private|protected|internal)\s+)(?:(?:static|readonly|const|virtual|override|abstract|new)\s+)*[\w.<>,?\[\]]+\s+\w*$signalNamePattern\w*\s*(?:=|\{|;)|\[Obsolete\b"
$typeScriptCandidatePattern = "(?i)^\s*(?:export\s+)?(?:const|let|var|function|type|interface|class)\s+\w*$signalNamePattern\w*|^\s*\w*$signalNamePattern\w*\??\s*:"
$scanRoots = @(
    'src/core',
    'src/hosts',
    'src/infrastructure',
    'src/services',
    'src/shared',
    'src/vues/AICopilot.Web/src',
    '.github',
    'deploy',
    'scripts'
)
$signals = [Collections.Generic.List[object]]::new()
foreach ($relativeRoot in $scanRoots) {
    $root = Join-Path $RepositoryRoot $relativeRoot
    if (-not (Test-Path $root -PathType Container)) {
        throw "Compatibility candidate scan root does not exist: $relativeRoot"
    }
    foreach ($file in Get-ChildItem $root -Recurse -File) {
        if ($file.Extension -notin @('.cs', '.ts', '.vue', '.ps1', '.sh', '.yml', '.yaml', '.props', '.targets', '.csproj') -or
            $file.FullName -match '[\/](bin|obj|Migrations|node_modules|dist)[\/]' -or
            $file.FullName -match '[\/]scripts[\/]tests[\/]') {
            continue
        }

        $lines = @(Get-SourceLines $file.FullName)
        if ($file.Extension -eq '.vue') {
            $scriptEnd = [Array]::IndexOf($lines, '</script>')
            if ($scriptEnd -lt 0) {
                continue
            }
            $lines = @($lines[0..$scriptEnd])
        }
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = [string]$lines[$index]
            $isCandidate = if ($file.Extension -eq '.cs') {
                $line -match $csharpCandidatePattern
            } elseif ($file.Extension -in @('.ts', '.vue')) {
                $line -match $typeScriptCandidatePattern
            } elseif ($file.Extension -in @('.yml', '.yaml')) {
                $line -match '(?i)^\s*(?:name|id|[A-Za-z0-9_-]+)\s*:\s*[^#]*(?:archive\s+fallback|legacy|compatibility|dual[-_ ]?write|shadow\s+path)'
            } elseif ($file.Extension -in @('.ps1', '.sh')) {
                $line -match '(?i)^\s*(?:function\s+)?(?:\$?LEGACY[_A-Z0-9]*|\$?COMPAT(?:IBILITY)?[_A-Z0-9]*|\$?DUAL_?WRITE[_A-Z0-9]*|\$?SHADOW_?PATH[_A-Z0-9]*)\s*(?:=|\(|\{)'
            } else {
                $line -match '(?i)<(?:[A-Za-z0-9_.-]*(?:Legacy|Compatibility|Fallback|DualWrite|ShadowPath)[A-Za-z0-9_.-]*)\b'
            }
            if ($isCandidate) {
                $signals.Add([pscustomobject]@{
                        path = [IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/')
                        line = $index + 1
                        text = $line.Trim()
                    })
            }
        }
    }
}

$dispositions = @(
    foreach ($item in @($items) + @($ordinaryAbstractions)) {
        $candidateEvidence = if ($null -eq $item.PSObject.Properties['candidateEvidence']) {
            @()
        }
        else {
            @($item.candidateEvidence)
        }
        foreach ($evidence in $candidateEvidence) {
            [pscustomobject]@{
                id = [string]$item.id
                disposition = if ($items -contains $item) { 'compatibility' } else { 'ordinaryAbstraction' }
                path = [string]$evidence.path
                contains = [string]$evidence.contains
            }
        }
    }
)
$dispositionHitCounts = @{}
foreach ($disposition in $dispositions) {
    $key = "$($disposition.id)|$($disposition.path)|$($disposition.contains)"
    $dispositionHitCounts[$key] = 0
}
foreach ($signal in $signals) {
    $matches = @($dispositions | Where-Object {
            $_.path -ceq $signal.path -and
            $signal.text.Contains($_.contains, [StringComparison]::Ordinal)
        })
    if ($matches.Count -ne 1) {
        throw "Compatibility signal must have exactly one disposition: $($signal.path):$($signal.line):$($signal.text)"
    }
    $match = $matches[0]
    $key = "$($match.id)|$($match.path)|$($match.contains)"
    $dispositionHitCounts[$key]++
}
foreach ($disposition in $dispositions) {
    $key = "$($disposition.id)|$($disposition.path)|$($disposition.contains)"
    if ([int]$dispositionHitCounts[$key] -eq 0) {
        throw "Compatibility candidate disposition has no active source signal: $key"
    }
}

if ($UpdateBaseline) {
    $baseline = [ordered]@{
        schemaVersion = 2
        candidateSignalCount = $signals.Count
        unclassifiedCompatibilitySignals = 0
        compatibilityItems = @(
            $items | Sort-Object id | ForEach-Object {
                [ordered]@{
                    id = [string]$_.id
                    deletionDeadline = [string]$_.deletionDeadline
                    maximumCallSites = [int]$callSiteCounts["$([string]$_.id)/primary"]
                }
            }
        )
        ordinaryAbstractions = @(
            $ordinaryAbstractions | Sort-Object id | ForEach-Object {
                $ordinaryId = [string]$_.id
                [ordered]@{
                    id = $ordinaryId
                    callerScans = @($_.callerScans | Sort-Object id | ForEach-Object {
                            [ordered]@{
                                id = [string]$_.id
                                maximumCallSites = [int]$callSiteCounts["$ordinaryId/$([string]$_.id)"]
                            }
                        })
                }
            }
        )
    }
    $baselineDirectory = Split-Path $BaselinePath -Parent
    New-Item $baselineDirectory -ItemType Directory -Force | Out-Null
    $baseline | ConvertTo-Json -Depth 8 | Set-Content $BaselinePath -Encoding utf8NoBOM
}

$baseline = Read-JsonFile $BaselinePath
if ([int]$baseline.schemaVersion -ne 2) {
    throw "Unsupported compatibility baseline schemaVersion '$($baseline.schemaVersion)'."
}
$baselineItems = @($baseline.compatibilityItems)
$inventoryIds = @($items.id | Sort-Object)
$baselineIds = @($baselineItems.id | Sort-Object)
if (($inventoryIds -join "`n") -cne ($baselineIds -join "`n")) {
    throw "Compatibility IDs differ from the reviewed baseline: inventory=[$($inventoryIds -join ', ')], baseline=[$($baselineIds -join ', ')]."
}

foreach ($item in $items) {
    $baselineItem = @($baselineItems | Where-Object { [string]$_.id -ceq [string]$item.id })
    if ($baselineItem.Count -ne 1) {
        throw "Compatibility baseline must contain exactly one entry for '$($item.id)'."
    }
    if ([string]$baselineItem[0].deletionDeadline -cne [string]$item.deletionDeadline) {
        throw "Compatibility deadline changed without baseline review for '$($item.id)'."
    }
    $actualCallSites = [int]$callSiteCounts["$([string]$item.id)/primary"]
    $maximumCallSites = [int]$baselineItem[0].maximumCallSites
    if ($actualCallSites -gt $maximumCallSites) {
        throw "Compatibility call sites grew for '$($item.id)': actual=$actualCallSites maximum=$maximumCallSites."
    }
}

$ordinaryIds = @($ordinaryAbstractions.id | Sort-Object)
$baselineOrdinary = @($baseline.ordinaryAbstractions)
$baselineOrdinaryIds = @($baselineOrdinary.id | Sort-Object)
if (($ordinaryIds -join "`n") -cne ($baselineOrdinaryIds -join "`n")) {
    throw "Ordinary abstraction IDs differ from the reviewed baseline: inventory=[$($ordinaryIds -join ', ')], baseline=[$($baselineOrdinaryIds -join ', ')]."
}
foreach ($item in $ordinaryAbstractions) {
    $baselineItem = @($baselineOrdinary | Where-Object { [string]$_.id -ceq [string]$item.id })
    if ($baselineItem.Count -ne 1) {
        throw "Compatibility baseline must contain exactly one ordinary abstraction '$($item.id)'."
    }
    $scanIds = @($item.callerScans.id | Sort-Object)
    $baselineScanIds = @($baselineItem[0].callerScans.id | Sort-Object)
    if (($scanIds -join "`n") -cne ($baselineScanIds -join "`n")) {
        throw "Caller-scan IDs changed for ordinary abstraction '$($item.id)'."
    }
    foreach ($scan in $item.callerScans) {
        $baselineScan = @($baselineItem[0].callerScans | Where-Object { [string]$_.id -ceq [string]$scan.id })
        $actualCallSites = [int]$callSiteCounts["$([string]$item.id)/$([string]$scan.id)"]
        if ($actualCallSites -gt [int]$baselineScan[0].maximumCallSites) {
            throw "Ordinary abstraction call sites grew for '$($item.id)/$($scan.id)': actual=$actualCallSites maximum=$($baselineScan[0].maximumCallSites)."
        }
    }
}
if ([int]$baseline.unclassifiedCompatibilitySignals -ne 0 -or
    $signals.Count -ne [int]$baseline.candidateSignalCount) {
    throw "Compatibility candidate signal set changed: actual=$($signals.Count), baseline=$($baseline.candidateSignalCount), unclassifiedBaseline=$($baseline.unclassifiedCompatibilitySignals)."
}

$callSummary = $items | Sort-Object id | ForEach-Object {
    "$($_.id)=$([int]$callSiteCounts["$([string]$_.id)/primary"])"
}
$summary = [ordered]@{
    schemaVersion = 2
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    activeCompatibilityItems = $items.Count
    ordinaryAbstractions = $ordinaryAbstractions.Count
    classifiedCandidateSignals = $signals.Count
    unclassifiedCompatibilitySignals = 0
    compatibilityCallSites = @($callSummary)
}
$resolvedOutputPath = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $RepositoryRoot $OutputPath }
New-Item (Split-Path $resolvedOutputPath -Parent) -ItemType Directory -Force | Out-Null
$summary | ConvertTo-Json -Depth 8 | Set-Content $resolvedOutputPath -Encoding utf8NoBOM
Write-Host "AICopilot compatibility inventory passed. active=$($items.Count); ordinary=$($ordinaryAbstractions.Count); classifiedSignals=$($signals.Count); unclassified=0; callSites=[$($callSummary -join ', ')]."
