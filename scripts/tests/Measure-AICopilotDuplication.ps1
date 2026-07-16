[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$BaselinePath = (Join-Path $PSScriptRoot 'baselines/aicopilot-duplication.json'),
    [string]$OutputPath = (Join-Path $RepositoryRoot 'artifacts/quality/aicopilot-duplication.json'),
    [string]$BaseRef = 'origin/main',
    [switch]$UpdateBaseline
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-AICopilotQualityBase.ps1')
$script:structuralKeywords = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($keyword in @(
    'abstract','as','async','await','base','bool','break','by','case','catch','char','class','const','constructor',
    'continue','decimal','default','delegate','do','double','else','enum','event','explicit','export','extends',
    'extern','false','finally','fixed','float','for','foreach','from','function','get','global','goto','if','implements',
    'implicit','import','in','init','instanceof','int','interface','internal','is','let','lock','long','namespace','new',
    'null','object','of','operator','out','override','params','partial','private','protected','public','readonly','record',
    'ref','required','return','sbyte','sealed','set','short','sizeof','static','string','struct','super','switch','this',
    'throw','true','try','typeof','uint','ulong','unchecked','unsafe','ushort','using','var','virtual','void','volatile',
    'while','with','yield')) {
    [void]$script:structuralKeywords.Add($keyword)
}

function Get-SourceFiles {
    param(
        [Parameter(Mandatory)][string[]]$Roots,
        [switch]$Tests
    )

    $files = foreach ($relativeRoot in $Roots) {
        $root = Join-Path $RepositoryRoot $relativeRoot
        if (-not (Test-Path $root -PathType Container)) {
            continue
        }

        Get-ChildItem $root -Recurse -File | Where-Object {
            $_.Extension -in @('.cs', '.ts', '.vue') -and
            $_.FullName -notmatch '[/\\](bin|obj|node_modules|dist|coverage)[/\\]' -and
            $_.FullName -notmatch '[/\\]Migrations[/\\]' -and
            $_.Name -notmatch '(\.Designer|ModelSnapshot|\.g)\.cs$' -and
            ($Tests -or $_.FullName -notmatch '[/\\](tests|testing)[/\\]')
        }
    }

    @($files | Sort-Object FullName -Unique)
}

function Normalize-SourceLine {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Line,
        [Parameter(Mandatory)][ValidateSet('Exact', 'Near', 'Structural')][string]$Mode
    )

    $normalized = $Line.Trim()
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized -match '^(//|/\*|\*|#|using\s|import\s|namespace\s|[{};,]+$)') {
        return $null
    }

    $normalized = [regex]::Replace($normalized, '\s+', ' ')
    if ($Mode -in @('Near', 'Structural')) {
        $normalized = [regex]::Replace($normalized, '"(?:\\.|[^"\\])*"', '"STR"')
        $normalized = [regex]::Replace($normalized, "'(?:\\.|[^'\\])*'", "'STR'")
        $normalized = [regex]::Replace($normalized, '(?<![A-Za-z_])(?:0x[0-9A-Fa-f]+|\d+(?:\.\d+)?)(?![A-Za-z_])', 'NUM')
    }
    if ($Mode -eq 'Structural') {
        $normalized = [regex]::Replace(
            $normalized,
            '\b[A-Za-z_][A-Za-z0-9_]*\b',
            { param($match) if ($script:structuralKeywords.Contains($match.Value)) { $match.Value } else { 'ID' } })
    }

    $normalized
}

function Get-CloneMeasurement {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][IO.FileInfo[]]$Files,
        [Parameter(Mandatory)][ValidateSet('Exact', 'Near', 'Structural')][string]$Mode,
        [Parameter(Mandatory)][int]$WindowSize,
        [switch]$AssertionsOnly
    )

    $signatures = @{}
    foreach ($file in $Files) {
        $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/')
        $logicalLines = [Collections.Generic.List[object]]::new()
        $lineNumber = 0
        foreach ($line in [IO.File]::ReadLines($file.FullName)) {
            $lineNumber++
            if ($AssertionsOnly -and $line -notmatch '(\.Should\(|Assert\.|expect\(|\.to(Be|Equal|Match|Contain|Throw)|Shouldly)') {
                continue
            }

            $normalized = Normalize-SourceLine $line $Mode
            if ($null -ne $normalized) {
                $logicalLines.Add([pscustomobject]@{ Number = $lineNumber; Text = $normalized })
            }
        }

        if ($logicalLines.Count -lt $WindowSize) {
            continue
        }

        for ($index = 0; $index -le $logicalLines.Count - $WindowSize; $index++) {
            $window = @($logicalLines[$index..($index + $WindowSize - 1)])
            $text = ($window.Text -join "`n")
            if ($text.Length -lt 100) {
                continue
            }

            $bytes = [Text.Encoding]::UTF8.GetBytes($text)
            $signature = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
            if (-not $signatures.ContainsKey($signature)) {
                $signatures[$signature] = [Collections.Generic.List[object]]::new()
            }
            $signatures[$signature].Add([pscustomobject]@{
                    path = $relativePath
                    line = $window[0].Number
                    tokens = @($text -split '\s+' | Where-Object Length -gt 0).Count
                })
        }
    }

    $duplicateGroups = [Collections.Generic.List[object]]::new()
    foreach ($signature in $signatures.Keys) {
        $instances = @($signatures[$signature] | Sort-Object path, line)
        if ($instances.Count -lt 2) {
            continue
        }
        $duplicateGroups.Add([pscustomobject]@{
                signature = $signature
                instances = $instances
                instanceCount = $instances.Count
                duplicatedLines = $WindowSize * $instances.Count
                duplicatedTokens = ($instances | Measure-Object tokens -Sum).Sum
            })
    }

    $orderedGroups = @(
        $duplicateGroups | Sort-Object `
            @{ Expression = 'duplicatedTokens'; Descending = $true },
            @{ Expression = 'signature'; Descending = $false }
    )
    $instanceCount = 0
    $duplicatedLines = 0
    $duplicatedTokens = 0
    foreach ($group in $orderedGroups) {
        $instanceCount += [int]$group.instanceCount
        $duplicatedLines += [int]$group.duplicatedLines
        $duplicatedTokens += [int]$group.duplicatedTokens
    }
    [pscustomobject]@{
        metrics = [ordered]@{
            groupCount = $orderedGroups.Count
            instanceCount = $instanceCount
            duplicatedLines = $duplicatedLines
            duplicatedTokens = $duplicatedTokens
        }
        groups = @($orderedGroups)
        examples = @($orderedGroups | Select-Object -First 20)
    }
}

$productionFiles = @(Get-SourceFiles @(
    'src/analyzers',
    'src/core',
    'src/hosts',
    'src/infrastructure',
    'src/services',
    'src/shared',
    'src/vues/AICopilot.Web/src'
))
$supportFiles = @(Get-SourceFiles @('src/testing') -Tests)
$testFiles = @(Get-SourceFiles @('src/tests', 'src/vues/AICopilot.Web/tests') -Tests)

$measurements = [ordered]@{
    productionExact = Get-CloneMeasurement $productionFiles Exact 8
    productionNear = Get-CloneMeasurement $productionFiles Near 8
    productionStructural = Get-CloneMeasurement $productionFiles Structural 8
    testSupportHelpers = Get-CloneMeasurement $supportFiles Near 6
    testAssertionFlows = Get-CloneMeasurement $testFiles Near 3 -AssertionsOnly
}

$report = [ordered]@{
    schemaVersion = 4
    instanceIdentity = 'path+line'
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    sourceCounts = [ordered]@{
        production = $productionFiles.Count
        testSupport = $supportFiles.Count
        tests = $testFiles.Count
    }
    categories = [ordered]@{}
}
foreach ($category in $measurements.Keys) {
    $report.categories[$category] = $measurements[$category]
}

function Assert-NoDuplicationGrowth {
    param(
        [Parameter(Mandatory)] [object]$Expected,
        [Parameter(Mandatory)] [string[]]$Categories
    )

    foreach ($category in $Categories) {
        $categoryBaseline = $Expected.categories.$category
        if ($null -eq $categoryBaseline -or $null -eq $categoryBaseline.maximum -or
            $null -eq $categoryBaseline.signatures) {
            throw "Duplication baseline is missing category '$category'."
        }
        foreach ($group in @($measurements[$category].groups)) {
            $signature = [string]$group.signature
            $signatureProperty = $categoryBaseline.signatures.PSObject.Properties[$signature]
            if ($null -eq $signatureProperty) {
                throw "Duplication signature grew: category=$category signature=$signature. Aggregate totals cannot authorize a signature swap. See $OutputPath."
            }

            $signatureBaseline = $signatureProperty.Value
            foreach ($metric in @(
                @{ actual = 'instanceCount'; maximum = 'maximumInstanceCount' },
                @{ actual = 'duplicatedLines'; maximum = 'maximumDuplicatedLines' },
                @{ actual = 'duplicatedTokens'; maximum = 'maximumDuplicatedTokens' }
            )) {
                $actualValue = [int]$group.($metric.actual)
                $maximumValue = [int]$signatureBaseline.($metric.maximum)
                if ($actualValue -gt $maximumValue) {
                    throw "Duplication signature instance metric grew: category=$category signature=$signature metric=$($metric.actual) actual=$actualValue maximum=$maximumValue. See $OutputPath."
                }
            }
        }

        $maximum = $categoryBaseline.maximum
        foreach ($metric in @('groupCount', 'instanceCount', 'duplicatedLines', 'duplicatedTokens')) {
            $actualValue = [int]$measurements[$category].metrics.$metric
            $maximumValue = [int]$maximum.$metric
            if ($actualValue -gt $maximumValue) {
                throw "Duplication metric grew: $category.$metric actual=$actualValue maximum=$maximumValue. See $OutputPath."
            }
        }
    }
}

function New-DuplicationBaseline {
    $value = [ordered]@{
        schemaVersion = 4
        instanceIdentity = 'path+line'
        categories = [ordered]@{}
    }
    foreach ($category in $measurements.Keys) {
        $signatureMaximum = [ordered]@{}
        foreach ($group in @($measurements[$category].groups)) {
            $signatureMaximum[[string]$group.signature] = [ordered]@{
                maximumInstanceCount = [int]$group.instanceCount
                maximumDuplicatedLines = [int]$group.duplicatedLines
                maximumDuplicatedTokens = [int]$group.duplicatedTokens
            }
        }
        $value.categories[$category] = [ordered]@{
            maximum = $measurements[$category].metrics
            signatures = $signatureMaximum
        }
    }
    return $value
}

function Assert-DuplicationBaselineDoesNotWeaken {
    param(
        [Parameter(Mandatory)] [object]$Base,
        [Parameter(Mandatory)] [object]$Candidate
    )

    foreach ($category in $measurements.Keys) {
        $baseCategory = $Base.categories.$category
        $candidateCategory = $Candidate.categories.$category
        if ($null -eq $baseCategory -or $null -eq $candidateCategory) {
            throw "Duplication baseline ratchet is missing category '$category'."
        }
        foreach ($metric in @('groupCount', 'instanceCount', 'duplicatedLines', 'duplicatedTokens')) {
            if ([int]$candidateCategory.maximum.$metric -gt [int]$baseCategory.maximum.$metric) {
                throw "Candidate duplication baseline weakens base maximum '$category.$metric'."
            }
        }
        foreach ($signatureProperty in @($candidateCategory.signatures.PSObject.Properties)) {
            $baseSignature = $baseCategory.signatures.PSObject.Properties[[string]$signatureProperty.Name]
            if ($null -eq $baseSignature) {
                throw "Candidate duplication baseline adds unratcheted signature '$category/$($signatureProperty.Name)'."
            }
            foreach ($metric in @('maximumInstanceCount', 'maximumDuplicatedLines', 'maximumDuplicatedTokens')) {
                if ([int]$signatureProperty.Value.$metric -gt [int]$baseSignature.Value.$metric) {
                    throw "Candidate duplication baseline weakens base signature maximum '$category/$($signatureProperty.Name)/$metric'."
                }
            }
        }
    }
}

$outputDirectory = Split-Path $OutputPath -Parent
New-Item $outputDirectory -ItemType Directory -Force | Out-Null
$report | ConvertTo-Json -Depth 12 | Set-Content $OutputPath -Encoding utf8NoBOM

if (-not (Test-Path $BaselinePath -PathType Leaf)) {
    throw "Duplication baseline does not exist: $BaselinePath"
}
$baselineContext = Get-AICopilotBaselineContext `
    -RepositoryRoot $RepositoryRoot `
    -BaseRef $BaseRef `
    -BaselineKind Duplication `
    -BaselinePath $BaselinePath
$expected = Get-Content $BaselinePath -Raw | ConvertFrom-Json -Depth 16
if ([int]$expected.schemaVersion -ne 4 -or [string]$expected.instanceIdentity -ne 'path+line') {
    throw "Unsupported duplication baseline schemaVersion '$($expected.schemaVersion)'."
}

if ($baselineContext.Mode -eq 'Ratchet') {
    $baseExpected = $baselineContext.BaseBaselineJson | ConvertFrom-Json -Depth 16
    Assert-NoDuplicationGrowth $baseExpected @($measurements.Keys)
    Assert-DuplicationBaselineDoesNotWeaken $baseExpected $expected
}
elseif ($baselineContext.Mode -eq 'Bootstrap' -and -not $UpdateBaseline) {
    $actualBaselineJson = (New-DuplicationBaseline) | ConvertTo-Json -Depth 8 -Compress
    $candidateBaselineJson = $expected | ConvertTo-Json -Depth 8 -Compress
    if ($candidateBaselineJson -cne $actualBaselineJson) {
        throw 'Initial duplication baseline must exactly reconcile the candidate measurements.'
    }
}

if ($UpdateBaseline) {
    $baseline = New-DuplicationBaseline
    $baselineDirectory = Split-Path $BaselinePath -Parent
    New-Item $baselineDirectory -ItemType Directory -Force | Out-Null
    $baseline | ConvertTo-Json -Depth 8 | Set-Content $BaselinePath -Encoding utf8NoBOM
    $expected = Get-Content $BaselinePath -Raw | ConvertFrom-Json -Depth 16
}

Assert-NoDuplicationGrowth $expected @($measurements.Keys)

$summary = $measurements.Keys | ForEach-Object {
    $metrics = $measurements[$_].metrics
    "$_=$($metrics.groupCount)/$($metrics.duplicatedLines)lines"
}
Write-Host "AICopilot duplication ratchet passed. $($summary -join '; ')."
