[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$InventoryPath = 'artifacts/test-inventory.json',
    [string]$ResultsDirectory = 'artifacts/test-results',
    [string]$BaselinePath = 'scripts/tests/baselines/aicopilot-coverage.json',
    [string]$OutputPath = 'artifacts/quality/aicopilot-coverage.json',
    [switch]$UpdateBaseline,
    [switch]$RunGuardSelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$productionSourcePattern = '^src/(analyzers|core|hosts|infrastructure|services|shared)/.+\.cs$'
$portablePdbSha256Algorithm = [Guid]'8829d00f-11b8-4213-878b-770e8597ac16'

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Path))
}

function Get-TextSha256 {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)

    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.Encoding]::UTF8.GetBytes($Value))).ToLowerInvariant()
}

function Assert-ExactIdentitySet {
    param(
        [Parameter(Mandatory)][string[]]$Expected,
        [Parameter(Mandatory)][string[]]$Actual,
        [Parameter(Mandatory)][string]$Label
    )

    $expectedDuplicates = @($Expected | Group-Object | Where-Object Count -ne 1)
    $actualDuplicates = @($Actual | Group-Object | Where-Object Count -ne 1)
    $expectedSorted = @($Expected | Sort-Object)
    $actualSorted = @($Actual | Sort-Object)
    if ($expectedDuplicates.Count -ne 0 -or
        $actualDuplicates.Count -ne 0 -or
        ($expectedSorted -join "`n") -cne ($actualSorted -join "`n")) {
        $missing = @($expectedSorted | Where-Object { $_ -notin $actualSorted })
        $unexpected = @($actualSorted | Where-Object { $_ -notin $expectedSorted })
        throw "$Label omitted, duplicated, or added authoritative identities: missing=[$($missing -join ',')], unexpected=[$($unexpected -join ',')]."
    }
}

function ConvertTo-ProductionSourcePath {
    param([Parameter(Mandatory)][string]$Path)

    $normalized = $Path.Replace('\', '/')
    if ([IO.Path]::IsPathRooted($Path)) {
        $normalized = [IO.Path]::GetRelativePath(
            [IO.Path]::GetFullPath($RepositoryRoot),
            [IO.Path]::GetFullPath($Path)).Replace('\', '/')
        if ($normalized -eq '..' -or $normalized.StartsWith('../', [StringComparison]::Ordinal)) {
            return $null
        }
    }
    else {
        $normalized = $normalized.TrimStart([char[]]@('.', '/'))
        if ($normalized -notmatch '^src/') {
            $normalized = "src/$normalized"
        }
    }

    if ($normalized -notmatch $productionSourcePattern -or
        $normalized -match '(?i)/(?:bin|obj)/') {
        return $null
    }
    return $normalized
}

function Get-CoverageMetrics {
    param([Parameter(Mandatory)][Collections.IDictionary]$Map)

    $totalLines = $Map.Count
    $coveredLines = @($Map.Values | Where-Object { [int]$_['hits'] -gt 0 }).Count
    $totalBranches = 0
    $coveredBranches = 0
    foreach ($line in $Map.Values) {
        $totalBranches += [int]$line['branchValid']
        $coveredBranches += [int]$line['branchCovered']
    }
    return [ordered]@{
        coveredLines = $coveredLines
        totalLines = $totalLines
        lineRate = if ($totalLines -eq 0) { 0.0 } else { [Math]::Round($coveredLines / $totalLines, 8) }
        coveredBranches = $coveredBranches
        totalBranches = $totalBranches
        branchRate = if ($totalBranches -eq 0) { 0.0 } else { [Math]::Round($coveredBranches / $totalBranches, 8) }
    }
}

function Merge-WithAuthoritativeUniverse {
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Universe,
        [Parameter(Mandatory)][Collections.IDictionary]$Observed
    )

    $merged = @{}
    foreach ($key in $Universe.Keys) {
        $line = $Universe[$key]
        $merged[$key] = [ordered]@{
            filename = [string]$line['filename']
            number = [int]$line['number']
            hits = 0
            branchValid = 0
            branchCovered = 0
        }
    }
    foreach ($key in $Observed.Keys) {
        if (-not $merged.ContainsKey($key)) {
            throw "Coverage report contains a line outside the authoritative portable-PDB universe: $key."
        }
        $candidate = $Observed[$key]
        $existing = $merged[$key]
        $existing['hits'] = [Math]::Max([int]$existing['hits'], [int]$candidate['hits'])
        $existing['branchValid'] = [Math]::Max(
            [int]$existing['branchValid'],
            [int]$candidate['branchValid'])
        $existing['branchCovered'] = [Math]::Max(
            [int]$existing['branchCovered'],
            [int]$candidate['branchCovered'])
    }
    return $merged
}

function Get-SourceUniverse {
    param([Parameter(Mandatory)][Collections.IDictionary]$Map)

    $files = @(
        $Map.Values |
            Group-Object { [string]$_['filename'] } |
            Sort-Object Name |
            ForEach-Object {
                $fileLines = @($_.Group)
                $fileBranches = 0
                foreach ($fileLine in $fileLines) {
                    $fileBranches += [int]$fileLine['branchValid']
                }
                [pscustomobject][ordered]@{
                    path = "src/$([string]$_.Name)"
                    linesValid = $fileLines.Count
                    branchesValid = $fileBranches
                }
            }
    )
    $canonical = @($files | ForEach-Object {
        "$($_.path)`0$($_.linesValid)`0$($_.branchesValid)"
    }) -join "`n"
    return [ordered]@{
        fileCount = $files.Count
        linesValid = [int](($files | Measure-Object linesValid -Sum).Sum ?? 0)
        branchesValid = [int](($files | Measure-Object branchesValid -Sum).Sum ?? 0)
        fileMetricsSha256 = Get-TextSha256 $canonical
    }
}

function Assert-NewUniverseIsObserved {
    param(
        [Parameter(Mandatory)][string[]]$BaselineIds,
        [Parameter(Mandatory)][string[]]$CurrentIds,
        [Parameter(Mandatory)][string[]]$ObservedIds,
        [Parameter(Mandatory)][string]$Label
    )

    $added = @($CurrentIds | Where-Object { $_ -notin $BaselineIds })
    $missing = @($added | Where-Object { $_ -notin $ObservedIds })
    if ($missing.Count -ne 0) {
        throw "New production $Label is absent from every required coverage report and cannot be accepted by UpdateBaseline: $($missing -join ',')."
    }
}

function Assert-ReviewedUniverseUpdate {
    param(
        [Parameter(Mandatory)][string[]]$BaselineAssemblyIds,
        [Parameter(Mandatory)][string[]]$CurrentAssemblyIds,
        [Parameter(Mandatory)][string[]]$BaselineDocumentSourceIds,
        [Parameter(Mandatory)][string[]]$CurrentDocumentSourceIds,
        [Parameter(Mandatory)][string[]]$BaselineExecutableSourceIds,
        [Parameter(Mandatory)][string[]]$CurrentExecutableSourceIds,
        [Parameter(Mandatory)][string[]]$ObservedAssemblyIds,
        [Parameter(Mandatory)][string[]]$ObservedSourceIds
    )

    $invalidBaselineExecutableSources = @(
        $BaselineExecutableSourceIds |
            Where-Object { $_ -notin $BaselineDocumentSourceIds }
    )
    $invalidCurrentExecutableSources = @(
        $CurrentExecutableSourceIds |
            Where-Object { $_ -notin $CurrentDocumentSourceIds }
    )
    if ($invalidBaselineExecutableSources.Count -ne 0 -or
        $invalidCurrentExecutableSources.Count -ne 0) {
        throw "Coverage executable-source identities must be a subset of portable-PDB document identities: baseline=[$($invalidBaselineExecutableSources -join ',')], current=[$($invalidCurrentExecutableSources -join ',')]."
    }

    Assert-NewUniverseIsObserved `
        $BaselineAssemblyIds `
        $CurrentAssemblyIds `
        $ObservedAssemblyIds `
        'assembly'
    Assert-NewUniverseIsObserved `
        $BaselineExecutableSourceIds `
        $CurrentExecutableSourceIds `
        $ObservedSourceIds `
        'executable source'
}

function Assert-ObservedProductionCoverage {
    param(
        [Parameter(Mandatory)][string[]]$ExpectedAssemblyIds,
        [Parameter(Mandatory)][string[]]$ExpectedExecutableSourceIds,
        [Parameter(Mandatory)][string[]]$ObservedAssemblyIds,
        [Parameter(Mandatory)][string[]]$ObservedSourceIds
    )

    Assert-ExactIdentitySet $ExpectedAssemblyIds $ObservedAssemblyIds `
        'Required coverage production assembly observation'
    Assert-ExactIdentitySet $ExpectedExecutableSourceIds $ObservedSourceIds `
        'Required coverage executable production source observation'
}

function Assert-CleanHeadBinding {
    param(
        [Parameter(Mandatory)][string]$InventoryHead,
        [Parameter(Mandatory)][string]$CurrentHead,
        [Parameter(Mandatory)][bool]$InventoryClean,
        [Parameter(Mandatory)][int]$CurrentChangeCount,
        [Parameter(Mandatory)][int]$StatusExitCode
    )

    if ($StatusExitCode -ne 0 -or
        [string]::IsNullOrWhiteSpace($CurrentHead) -or
        $CurrentHead -cne $InventoryHead -or
        -not $InventoryClean -or
        $CurrentChangeCount -ne 0) {
        throw "Authoritative coverage requires one clean committed HEAD; inventoryHead=$InventoryHead, currentHead=$CurrentHead, inventoryClean=$InventoryClean, currentChanges=$CurrentChangeCount."
    }
}

function Assert-CoverageThreshold {
    param(
        [Parameter(Mandatory)][object]$Metrics,
        [Parameter(Mandatory)][double]$MinimumLineRate,
        [Parameter(Mandatory)][double]$MinimumBranchRate
    )

    if ([double]$Metrics.lineRate + 0.000000001 -lt $MinimumLineRate -or
        [double]$Metrics.branchRate + 0.000000001 -lt $MinimumBranchRate) {
        throw "Coverage regressed: line=$($Metrics.lineRate)/$MinimumLineRate, branch=$($Metrics.branchRate)/$MinimumBranchRate."
    }
}

function Resolve-LogicalCoverageCopies {
    param(
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][object[]]$Copies
    )

    if ($Copies.Count -eq 0) {
        throw "$ProjectName coverage binding requires at least one physical report."
    }
    foreach ($copy in $Copies) {
        if ([string]::IsNullOrWhiteSpace([string]$copy.path) -or
            [string]::IsNullOrWhiteSpace([string]$copy.sha256)) {
            throw "$ProjectName coverage binding contains a physical copy without path/SHA256 identity."
        }
    }
    $digests = @($Copies.sha256 | Sort-Object -Unique)
    if ($digests.Count -ne 1) {
        throw "$ProjectName coverage physical copies differ by SHA256 and cannot be treated as one logical report: $($digests -join ',')."
    }
    return [pscustomobject][ordered]@{
        sha256 = [string]$digests[0]
        physicalCopies = $Copies.Count
        paths = @($Copies.path | Sort-Object)
    }
}

function Assert-CoverageDigestOwner {
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$OwnerByDigest,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][string]$Digest
    )

    if ($OwnerByDigest.Contains($Digest)) {
        throw "Coverage logical report digest is bound across runners: digest=$Digest, first=$($OwnerByDigest[$Digest]), second=$ProjectName."
    }
    $OwnerByDigest[$Digest] = $ProjectName
}

function Invoke-GuardSelfTest {
    $assemblyFailure = $null
    try {
        Assert-ExactIdentitySet @('A|A', 'B|B') @('A|A') 'Production assembly/PDB evidence'
    }
    catch {
        $assemblyFailure = $_.Exception.Message
    }
    if ($assemblyFailure -notmatch 'omitted, duplicated, or added') {
        throw "Production assembly omission fixture did not fail closed: $assemblyFailure"
    }

    $sourceFailure = $null
    try {
        Assert-ExactIdentitySet @('A|src/core/A.cs', 'B|src/core/B.cs') @('A|src/core/A.cs') 'Portable-PDB document evidence'
    }
    catch {
        $sourceFailure = $_.Exception.Message
    }
    if ($sourceFailure -notmatch 'omitted, duplicated, or added') {
        throw "Production source omission fixture did not fail closed: $sourceFailure"
    }

    $universe = @{
        'core/A.cs:1' = [ordered]@{ filename = 'core/A.cs'; number = 1; hits = 0; branchValid = 0; branchCovered = 0 }
        'core/B.cs:1' = [ordered]@{ filename = 'core/B.cs'; number = 1; hits = 0; branchValid = 0; branchCovered = 0 }
    }
    $observed = @{
        'core/A.cs:1' = [ordered]@{ filename = 'core/A.cs'; number = 1; hits = 1; branchValid = 2; branchCovered = 2 }
    }
    $merged = Merge-WithAuthoritativeUniverse $universe $observed
    $metrics = Get-CoverageMetrics $merged
    $thresholdFailure = $null
    try {
        Assert-CoverageThreshold $metrics 1.0 1.0
    }
    catch {
        $thresholdFailure = $_.Exception.Message
    }
    if ($merged.Count -ne 2 -or
        [int]$merged['core/B.cs:1']['hits'] -ne 0 -or
        $thresholdFailure -notmatch 'Coverage regressed') {
        throw "Unloaded production source was not retained as zero-hit coverage: $thresholdFailure"
    }

    $updateFailure = $null
    try {
        Assert-NewUniverseIsObserved @('A|src/core/A.cs') `
            @('A|src/core/A.cs', 'B|src/core/B.cs') `
            @('A|src/core/A.cs') `
            'source'
    }
    catch {
        $updateFailure = $_.Exception.Message
    }
    if ($updateFailure -notmatch 'cannot be accepted by UpdateBaseline') {
        throw "Unloaded new-source baseline-update fixture did not fail closed: $updateFailure"
    }

    Assert-ReviewedUniverseUpdate `
        @('A|A') `
        @('A|A') `
        @('A|src/core/A.cs') `
        @('A|src/core/A.cs', 'A|src/core/IMarker.cs') `
        @('A|src/core/A.cs') `
        @('A|src/core/A.cs') `
        @('A|A') `
        @('A|src/core/A.cs')

    $existingObservationFailure = $null
    try {
        Assert-ObservedProductionCoverage `
            @('A|A', 'B|B') `
            @('A|src/core/A.cs', 'B|src/core/B.cs') `
            @('A|A') `
            @('A|src/core/A.cs')
    }
    catch {
        $existingObservationFailure = $_.Exception.Message
    }
    if ($existingObservationFailure -notmatch 'Required coverage production assembly observation') {
        throw "Existing production assembly/source observation fixture did not fail closed: $existingObservationFailure"
    }

    $existingSourceObservationFailure = $null
    try {
        Assert-ObservedProductionCoverage `
            @('A|A', 'B|B') `
            @('A|src/core/A.cs', 'B|src/core/B.cs') `
            @('A|A', 'B|B') `
            @('A|src/core/A.cs')
    }
    catch {
        $existingSourceObservationFailure = $_.Exception.Message
    }
    if ($existingSourceObservationFailure -notmatch 'Required coverage executable production source observation') {
        throw "Existing executable production source observation fixture did not fail closed: $existingSourceObservationFailure"
    }

    $orphanFailure = $null
    try {
        Assert-ExactIdentitySet @('/results/bound.xml') `
            @('/results/bound.xml', '/results/orphan.xml') `
            'Required coverage report tree'
    }
    catch {
        $orphanFailure = $_.Exception.Message
    }
    if ($orphanFailure -notmatch 'Required coverage report tree omitted, duplicated, or added') {
        throw "Orphan coverage report fixture did not fail closed: $orphanFailure"
    }

    $sameDigestCopies = Resolve-LogicalCoverageCopies 'RunnerA' @(
        [pscustomobject]@{ path = '/results/a/first.xml'; sha256 = 'abc' },
        [pscustomobject]@{ path = '/results/a/second.xml'; sha256 = 'abc' }
    )
    if ($sameDigestCopies.physicalCopies -ne 2 -or
        [string]$sameDigestCopies.sha256 -cne 'abc') {
        throw 'Byte-identical VSTest coverage copies were not reconciled as one logical report.'
    }

    $conflictingCopyFailure = $null
    try {
        Resolve-LogicalCoverageCopies 'RunnerA' @(
            [pscustomobject]@{ path = '/results/a/first.xml'; sha256 = 'abc' },
            [pscustomobject]@{ path = '/results/a/second.xml'; sha256 = 'def' }
        ) *> $null
    }
    catch {
        $conflictingCopyFailure = $_.Exception.Message
    }
    if ($conflictingCopyFailure -notmatch 'physical copies differ by SHA256') {
        throw "Conflicting physical coverage copy fixture did not fail closed: $conflictingCopyFailure"
    }

    $digestOwners = @{}
    Assert-CoverageDigestOwner $digestOwners 'RunnerA' 'abc'
    $crossRunnerFailure = $null
    try {
        Assert-CoverageDigestOwner $digestOwners 'RunnerB' 'abc'
    }
    catch {
        $crossRunnerFailure = $_.Exception.Message
    }
    if ($crossRunnerFailure -notmatch 'bound across runners') {
        throw "Cross-runner coverage reuse fixture did not fail closed: $crossRunnerFailure"
    }

    $dirtyHeadFailure = $null
    try {
        Assert-CleanHeadBinding 'abc' 'abc' $false 1 0
    }
    catch {
        $dirtyHeadFailure = $_.Exception.Message
    }
    if ($dirtyHeadFailure -notmatch 'requires one clean committed HEAD') {
        throw "Dirty-HEAD coverage fixture did not fail closed: $dirtyHeadFailure"
    }

    Write-Host 'AICopilot coverage omission guards passed. cases=11.'
}

function Get-PortablePdbUniverse {
    param(
        [Parameter(Mandatory)][object[]]$AssemblyEntries,
        [Parameter(Mandatory)][object[]]$SourceEntries
    )

    $sourceByIdentity = @{}
    foreach ($source in $SourceEntries) {
        $identity = "$([string]$source.assembly)|$([string]$source.source)"
        if ($sourceByIdentity.ContainsKey($identity)) {
            throw "Production source inventory contains duplicate identity '$identity'."
        }
        $sourceByIdentity[$identity] = $source
    }

    $expectedAssemblyIds = @($AssemblyEntries | ForEach-Object {
        "$([string]$_.project)|$([string]$_.assembly)"
    })
    $actualProjectIds = @($AssemblyEntries | ForEach-Object { [string]$_.project })
    $sourceProjectIds = @($SourceEntries | ForEach-Object { [string]$_.project } | Sort-Object -Unique)
    Assert-ExactIdentitySet $sourceProjectIds $actualProjectIds 'Production assembly/PDB evidence'

    $universe = @{}
    $documentSourceIds = [Collections.Generic.List[string]]::new()
    $executableSourceIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $assemblyEvidence = [Collections.Generic.List[object]]::new()
    $assemblyCanonical = [Collections.Generic.List[string]]::new()
    foreach ($entry in @($AssemblyEntries | Sort-Object assembly, project)) {
        foreach ($property in @(
            'project', 'assembly', 'targetFramework', 'assemblyPath', 'pdbPath',
            'assemblySha256', 'pdbSha256')) {
            if ($null -eq $entry.PSObject.Properties[$property] -or
                [string]::IsNullOrWhiteSpace([string]$entry.$property)) {
                throw "Production coverage assembly evidence is missing '$property'."
            }
        }
        if ([string]$entry.project -notmatch '^src/(analyzers|core|hosts|infrastructure|services|shared)/.+\.csproj$' -or
            [string]$entry.assemblyPath -notmatch '^src/(analyzers|core|hosts|infrastructure|services|shared)/.+\.dll$' -or
            [string]$entry.pdbPath -notmatch '^src/(analyzers|core|hosts|infrastructure|services|shared)/.+\.pdb$') {
            throw "Production assembly/PDB evidence escapes the authoritative source roots: $($entry.project)."
        }
        $assemblyPath = Resolve-RepositoryPath ([string]$entry.assemblyPath)
        $pdbPath = Resolve-RepositoryPath ([string]$entry.pdbPath)
        if (-not (Test-Path $assemblyPath -PathType Leaf) -or
            -not (Test-Path $pdbPath -PathType Leaf) -or
            (Get-FileHash $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$entry.assemblySha256 -or
            (Get-FileHash $pdbPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$entry.pdbSha256) {
            throw "Production assembly/PDB differs from the inventory-bound build: assembly=$assemblyPath pdb=$pdbPath."
        }

        $assemblyDocumentIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $assemblyLineKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $stream = [IO.File]::OpenRead($pdbPath)
        try {
            $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
            try {
                $reader = $provider.GetMetadataReader()
                foreach ($documentHandle in $reader.Documents) {
                    $document = $reader.GetDocument($documentHandle)
                    $documentPath = ConvertTo-ProductionSourcePath ($reader.GetString($document.Name))
                    if ($null -eq $documentPath) {
                        continue
                    }
                    $sourceIdentity = "$([string]$entry.assembly)|$documentPath"
                    if (-not $sourceByIdentity.ContainsKey($sourceIdentity)) {
                        throw "Portable PDB contains a production document outside its evaluated project source inventory: $sourceIdentity."
                    }
                    if ($document.HashAlgorithm.IsNil -or
                        $reader.GetGuid($document.HashAlgorithm) -ne $portablePdbSha256Algorithm) {
                        throw "Portable PDB document does not use SHA-256 source binding: $sourceIdentity."
                    }
                    $documentHash = [Convert]::ToHexString(
                        $reader.GetBlobBytes($document.Hash)).ToLowerInvariant()
                    if ($documentHash -cne [string]$sourceByIdentity[$sourceIdentity].sha256) {
                        throw "Portable PDB source checksum differs from the inventory-bound source: $sourceIdentity."
                    }
                    $null = $assemblyDocumentIds.Add($sourceIdentity)
                }

                $expectedDocumentIds = @($SourceEntries | Where-Object {
                    [string]$_.assembly -ceq [string]$entry.assembly
                } | ForEach-Object { "$([string]$_.assembly)|$([string]$_.source)" })
                Assert-ExactIdentitySet $expectedDocumentIds @($assemblyDocumentIds) `
                    "Portable-PDB document evidence for $([string]$entry.assembly)"
                foreach ($sourceId in $assemblyDocumentIds) {
                    $documentSourceIds.Add($sourceId)
                }

                foreach ($methodHandle in $reader.MethodDebugInformation) {
                    $method = $reader.GetMethodDebugInformation($methodHandle)
                    foreach ($sequencePoint in $method.GetSequencePoints()) {
                        if ($sequencePoint.IsHidden) {
                            continue
                        }
                        $documentHandle = if (-not $sequencePoint.Document.IsNil) {
                            $sequencePoint.Document
                        }
                        else {
                            $method.Document
                        }
                        if ($documentHandle.IsNil) {
                            throw "Portable PDB contains a visible sequence point without a document: $pdbPath."
                        }
                        $document = $reader.GetDocument($documentHandle)
                        $documentPath = ConvertTo-ProductionSourcePath ($reader.GetString($document.Name))
                        if ($null -eq $documentPath) {
                            continue
                        }
                        $sourceIdentity = "$([string]$entry.assembly)|$documentPath"
                        if (-not $sourceByIdentity.ContainsKey($sourceIdentity)) {
                            throw "Portable PDB sequence point escaped its evaluated source inventory: $sourceIdentity."
                        }
                        $null = $executableSourceIds.Add($sourceIdentity)
                        $coveragePath = $documentPath.Substring(4)
                        for ($lineNumber = [int]$sequencePoint.StartLine;
                            $lineNumber -le [int]$sequencePoint.EndLine;
                            $lineNumber++) {
                            $key = "${coveragePath}:$lineNumber"
                            $null = $assemblyLineKeys.Add($key)
                            if (-not $universe.ContainsKey($key)) {
                                $universe[$key] = [ordered]@{
                                    filename = $coveragePath
                                    number = $lineNumber
                                    hits = 0
                                    branchValid = 0
                                    branchCovered = 0
                                }
                            }
                        }
                    }
                }
            }
            finally {
                $provider.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
        if ($assemblyLineKeys.Count -eq 0) {
            throw "Production assembly has no authoritative portable-PDB sequence points: $($entry.assembly)."
        }
        $lineDigest = Get-TextSha256 (@($assemblyLineKeys | Sort-Object) -join "`n")
        $documentDigest = Get-TextSha256 (@($assemblyDocumentIds | Sort-Object) -join "`n")
        $assemblyEvidence.Add([pscustomobject][ordered]@{
            project = [string]$entry.project
            assembly = [string]$entry.assembly
            targetFramework = [string]$entry.targetFramework
            documents = $assemblyDocumentIds.Count
            documentSha256 = $documentDigest
            sequencePointLines = $assemblyLineKeys.Count
            sequencePointSha256 = $lineDigest
        })
        $assemblyCanonical.Add(
            "$([string]$entry.project)`0$([string]$entry.assembly)`0$([string]$entry.targetFramework)`0$documentDigest`0$lineDigest")
    }

    Assert-ExactIdentitySet `
        @($SourceEntries | ForEach-Object { "$([string]$_.assembly)|$([string]$_.source)" }) `
        @($documentSourceIds) `
        'Portable-PDB production source evidence'
    return [ordered]@{
        map = $universe
        assemblyIds = @($expectedAssemblyIds | Sort-Object)
        sourceIds = @($documentSourceIds | Sort-Object)
        executableSourceIds = @($executableSourceIds | Sort-Object)
        assemblyCount = $assemblyEvidence.Count
        assemblySha256 = Get-TextSha256 (@($assemblyCanonical | Sort-Object) -join "`n")
        evidence = @($assemblyEvidence)
    }
}

function Get-ObservedCoverageMap {
    param(
        [Parameter(Mandatory)][string]$ReportPath,
        [Parameter(Mandatory)][Collections.IDictionary]$PdbMap,
        [Parameter(Mandatory)][hashtable]$SourceByPath
    )

    [xml]$coverage = Get-Content $ReportPath -Raw
    $map = @{}
    $sourceIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $assemblyIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($package in @($coverage.coverage.packages.package)) {
        $packageName = [string]$package.name
        foreach ($class in @($package.classes.class)) {
            $sourcePath = ConvertTo-ProductionSourcePath ([string]$class.filename)
            if ($null -eq $sourcePath) {
                continue
            }
            if (-not $SourceByPath.ContainsKey($sourcePath)) {
                throw "Coverage report contains a production-looking source outside the evaluated universe: $sourcePath."
            }
            $source = $SourceByPath[$sourcePath]
            if ($packageName -cne [string]$source.assembly) {
                throw "Coverage package/source ownership differs from the evaluated production assembly: package=$packageName source=$sourcePath expected=$($source.assembly)."
            }
            $null = $sourceIds.Add("$([string]$source.assembly)|$sourcePath")
            $null = $assemblyIds.Add("$([string]$source.project)|$([string]$source.assembly)")
            foreach ($line in @($class.lines.line)) {
                $lineNumber = [int]$line.number
                $coveragePath = $sourcePath.Substring(4)
                $key = "${coveragePath}:$lineNumber"
                if ($lineNumber -le 0 -or -not $PdbMap.ContainsKey($key)) {
                    throw "Coverage report contains a line outside the authoritative portable-PDB universe: $key."
                }
                $hits = [int]$line.hits
                $branchValid = 0
                $branchCovered = 0
                if ([string]$line.branch -ieq 'true' -and
                    [string]$line.'condition-coverage' -match '\((?<covered>\d+)\s*/\s*(?<total>\d+)\)') {
                    $branchCovered = [int]$Matches.covered
                    $branchValid = [int]$Matches.total
                }
                if ($hits -lt 0 -or $branchCovered -lt 0 -or
                    $branchValid -lt 0 -or $branchCovered -gt $branchValid) {
                    throw "Coverage report contains invalid hit/branch metrics at $key."
                }
                if (-not $map.ContainsKey($key)) {
                    $map[$key] = [ordered]@{
                        filename = $coveragePath
                        number = $lineNumber
                        hits = $hits
                        branchValid = $branchValid
                        branchCovered = $branchCovered
                    }
                }
                else {
                    $existing = $map[$key]
                    $existing['hits'] = [Math]::Max([int]$existing['hits'], $hits)
                    $existing['branchValid'] = [Math]::Max([int]$existing['branchValid'], $branchValid)
                    $existing['branchCovered'] = [Math]::Max([int]$existing['branchCovered'], $branchCovered)
                }
            }
        }
    }
    return [ordered]@{
        map = $map
        sourceIds = @($sourceIds)
        assemblyIds = @($assemblyIds)
    }
}

if ($RunGuardSelfTest) {
    Invoke-GuardSelfTest
    return
}

$resolvedInventoryPath = Resolve-RepositoryPath $InventoryPath
$resolvedResultsDirectory = Resolve-RepositoryPath $ResultsDirectory
$resolvedBaselinePath = Resolve-RepositoryPath $BaselinePath
$resolvedOutputPath = Resolve-RepositoryPath $OutputPath

if (-not (Test-Path $resolvedInventoryPath -PathType Leaf)) {
    throw "Test inventory does not exist: $resolvedInventoryPath"
}
if (-not (Test-Path $resolvedResultsDirectory -PathType Container)) {
    throw "Test results directory does not exist: $resolvedResultsDirectory"
}
if (-not (Test-Path $resolvedBaselinePath -PathType Leaf)) {
    throw "Coverage baseline bootstrap is forbidden; reviewed baseline does not exist: $resolvedBaselinePath"
}

$inventory = Get-Content $resolvedInventoryPath -Raw | ConvertFrom-Json -Depth 64
$repositoryCleanProperty = $inventory.PSObject.Properties['repositoryClean']
$productionAssembliesProperty = $inventory.PSObject.Properties['productionAssemblies']
if ([int]$inventory.schemaVersion -lt 3 -or
    [string]::IsNullOrWhiteSpace([string]$inventory.repositoryHead) -or
    $null -eq $inventory.productionUniverse -or
    $null -eq $repositoryCleanProperty -or
    $null -eq $productionAssembliesProperty -or
    @($productionAssembliesProperty.Value).Count -eq 0) {
    throw 'Coverage requires schemaVersion>=3 inventory with repository/SHA, production source universe, and evaluated assembly/PDB evidence.'
}
$currentHead = ((& git -C $RepositoryRoot rev-parse HEAD 2>$null) -join "`n").Trim()
$currentStatus = @(& git -C $RepositoryRoot status --porcelain=v1 --untracked-files=all 2>$null)
$statusExitCode = $LASTEXITCODE
Assert-CleanHeadBinding `
    ([string]$inventory.repositoryHead) `
    $currentHead `
    ([bool]$inventory.repositoryClean) `
    $currentStatus.Count `
    $statusExitCode

$sourceEntries = @($inventory.productionUniverse.sources)
if ($sourceEntries.Count -ne [int]$inventory.productionUniverse.sourceCount) {
    throw 'Production source inventory count differs from its declared universe.'
}
$sourceContentCanonical = [Collections.Generic.List[string]]::new()
$sourceIdentityCanonical = [Collections.Generic.List[string]]::new()
$sourceByPath = @{}
foreach ($source in @($sourceEntries | Sort-Object assembly, project, source)) {
    foreach ($property in @('assembly', 'project', 'source', 'sha256')) {
        if ($null -eq $source.PSObject.Properties[$property] -or
            [string]::IsNullOrWhiteSpace([string]$source.$property)) {
            throw "Production source inventory is missing '$property'."
        }
    }
    $sourcePath = [string]$source.source
    if ($sourcePath -notmatch $productionSourcePattern -or $sourceByPath.ContainsKey($sourcePath)) {
        throw "Production source inventory contains an invalid or duplicate path '$sourcePath'."
    }
    $fullSourcePath = Resolve-RepositoryPath $sourcePath
    if (-not (Test-Path $fullSourcePath -PathType Leaf) -or
        (Get-FileHash $fullSourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$source.sha256) {
        throw "Production source differs from the inventory-bound clean HEAD: $sourcePath."
    }
    $sourceByPath[$sourcePath] = $source
    $sourceContentCanonical.Add("$([string]$source.assembly)`0$([string]$source.project)`0$sourcePath`0$([string]$source.sha256)")
    $sourceIdentityCanonical.Add("$([string]$source.assembly)`0$([string]$source.project)`0$sourcePath")
}
if ((Get-TextSha256 ($sourceContentCanonical -join "`n")) -cne [string]$inventory.productionUniverse.sha256 -or
    (Get-TextSha256 ($sourceIdentityCanonical -join "`n")) -cne [string]$inventory.productionUniverse.identitySha256 -or
    @($sourceEntries.assembly | Sort-Object -Unique).Count -ne [int]$inventory.productionUniverse.assemblyCount) {
    throw 'Production source universe digest/count differs from the inventory-bound clean HEAD.'
}

$pdbUniverse = Get-PortablePdbUniverse @($inventory.productionAssemblies) $sourceEntries
if ([int]$pdbUniverse.assemblyCount -ne [int]$inventory.productionUniverse.assemblyCount) {
    throw "Production assembly/PDB count differs from the source universe: assemblies=$($pdbUniverse.assemblyCount), sourcesDeclare=$($inventory.productionUniverse.assemblyCount)."
}

$requiredProjects = @(
    $inventory.projects | Where-Object {
        $_.role -eq 'Runner' -and [bool]$_.required
    }
)
if ($requiredProjects.Count -eq 0) {
    throw 'Inventory contains no required runners for coverage reconciliation.'
}

$reportBindings = [Collections.Generic.List[object]]::new()
$observedMap = @{}
$observedSourceIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$observedAssemblyIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$coverageOwnerByDigest = @{}
$assemblyEvidenceByName = @{}
foreach ($assemblyEvidence in @($inventory.productionAssemblies)) {
    $assemblyName = [string]$assemblyEvidence.assembly
    if ($assemblyEvidenceByName.ContainsKey($assemblyName)) {
        throw "Production assembly evidence contains duplicate assembly '$assemblyName'."
    }
    $assemblyEvidenceByName[$assemblyName] = $assemblyEvidence
}
foreach ($project in $requiredProjects) {
    $runnerDirectory = Join-Path $resolvedResultsDirectory ([string]$project.projectName)
    if (-not (Test-Path $runnerDirectory -PathType Container)) {
        throw "Coverage runner directory is missing for $($project.projectName): $runnerDirectory"
    }
    $trxFiles = @(Get-ChildItem $runnerDirectory -Filter "$($project.projectName).trx" -File -Recurse)
    $coverageFilesForRunner = @(Get-ChildItem $runnerDirectory -Filter 'coverage.cobertura.xml' -File -Recurse)
    if ($trxFiles.Count -ne 1 -or $coverageFilesForRunner.Count -eq 0) {
        throw "$($project.projectName) coverage binding requires exactly one TRX and at least one physical report; trx=$($trxFiles.Count), physicalReports=$($coverageFilesForRunner.Count)."
    }
    [xml]$trx = Get-Content $trxFiles[0].FullName -Raw
    $attachments = @($trx.TestRun.ResultSummary.CollectorDataEntries.Collector.UriAttachments.UriAttachment.A)
    $attachmentHref = if ($attachments.Count -eq 1) {
        ([string]$attachments[0].href).Replace('\', '/')
    }
    else {
        ''
    }
    if ($attachments.Count -ne 1 -or
        ($attachmentHref -cne 'coverage.cobertura.xml' -and
            -not $attachmentHref.EndsWith(
                '/coverage.cobertura.xml', [StringComparison]::Ordinal))) {
        throw "$($project.projectName) TRX must contain exactly one XPlat coverage attachment."
    }
    $runnerDirectoryFullPath = [IO.Path]::GetFullPath($runnerDirectory)
    $coverageCopies = @($coverageFilesForRunner | ForEach-Object {
        $coverageFullPath = [IO.Path]::GetFullPath($_.FullName)
        $coverageRelativePath = [IO.Path]::GetRelativePath(
            $runnerDirectoryFullPath,
            $coverageFullPath).Replace('\', '/')
        if ($coverageRelativePath -eq '..' -or
            $coverageRelativePath.StartsWith('../', [StringComparison]::Ordinal)) {
            throw "$($project.projectName) coverage copy escapes its runner binding: $coverageFullPath."
        }
        [pscustomobject][ordered]@{
            path = $coverageFullPath
            relativePath = $coverageRelativePath
            sha256 = (Get-FileHash $coverageFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $logicalCoverage = Resolve-LogicalCoverageCopies ([string]$project.projectName) $coverageCopies
    Assert-CoverageDigestOwner `
        $coverageOwnerByDigest `
        ([string]$project.projectName) `
        ([string]$logicalCoverage.sha256)
    $attachmentMatches = @($coverageCopies | Where-Object {
        [string]$_.relativePath -ceq $attachmentHref -or
        ([string]$_.relativePath).EndsWith(
            "/$attachmentHref",
            [StringComparison]::Ordinal)
    })
    if ($attachmentMatches.Count -ne 1) {
        throw "$($project.projectName) TRX attachment must bind exactly one physical coverage copy; href=$attachmentHref, matches=$($attachmentMatches.Count)."
    }
    $boundCoveragePath = [string]$attachmentMatches[0].path
    $storages = @($trx.TestRun.TestDefinitions.UnitTest | ForEach-Object {
        [string]$_.storage
    } | Sort-Object -Unique)
    if ($storages.Count -ne 1 -or
        [IO.Path]::GetFileNameWithoutExtension($storages[0]) -cne [string]$project.projectName) {
        throw "$($project.projectName) TRX test-definition storage does not match the inventory runner assembly."
    }

    $runnerCoverage = Get-ObservedCoverageMap `
        $boundCoveragePath `
        $pdbUniverse.map `
        $sourceByPath
    $runnerBinDirectory = Split-Path ([IO.Path]::GetFullPath($storages[0])) -Parent
    foreach ($observedAssemblyId in $runnerCoverage.assemblyIds) {
        $observedAssemblyName = ([string]$observedAssemblyId).Substring(
            ([string]$observedAssemblyId).LastIndexOf('|') + 1)
        if (-not $assemblyEvidenceByName.ContainsKey($observedAssemblyName)) {
            throw "$($project.projectName) report contains unknown production assembly '$observedAssemblyName'."
        }
        $expectedAssembly = $assemblyEvidenceByName[$observedAssemblyName]
        $runnerAssemblyPath = Join-Path $runnerBinDirectory "$observedAssemblyName.dll"
        $runnerPdbPath = Join-Path $runnerBinDirectory "$observedAssemblyName.pdb"
        if (-not (Test-Path $runnerAssemblyPath -PathType Leaf) -or
            -not (Test-Path $runnerPdbPath -PathType Leaf) -or
            (Get-FileHash $runnerAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
                [string]$expectedAssembly.assemblySha256 -or
            (Get-FileHash $runnerPdbPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
                [string]$expectedAssembly.pdbSha256) {
            throw "$($project.projectName) loaded production assembly/PDB differs from the inventory-bound build: $observedAssemblyName."
        }
    }
    foreach ($key in $runnerCoverage.map.Keys) {
        $candidate = $runnerCoverage.map[$key]
        if (-not $observedMap.ContainsKey($key)) {
            $observedMap[$key] = $candidate
        }
        else {
            $existing = $observedMap[$key]
            $existing['hits'] = [Math]::Max([int]$existing['hits'], [int]$candidate['hits'])
            $existing['branchValid'] = [Math]::Max([int]$existing['branchValid'], [int]$candidate['branchValid'])
            $existing['branchCovered'] = [Math]::Max([int]$existing['branchCovered'], [int]$candidate['branchCovered'])
        }
    }
    foreach ($sourceId in $runnerCoverage.sourceIds) { $null = $observedSourceIds.Add($sourceId) }
    foreach ($assemblyId in $runnerCoverage.assemblyIds) { $null = $observedAssemblyIds.Add($assemblyId) }
    $reportBindings.Add([pscustomobject]@{
        projectName = [string]$project.projectName
        repositoryHead = $currentHead
        trxPath = $trxFiles[0].FullName
        trxSha256 = (Get-FileHash $trxFiles[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        coveragePath = $boundCoveragePath
        coverageSha256 = [string]$logicalCoverage.sha256
        physicalCopies = [int]$logicalCoverage.physicalCopies
        physicalCoveragePaths = @($logicalCoverage.paths)
        observedProductionAssemblies = @($runnerCoverage.assemblyIds | Sort-Object)
        observedProductionSources = @($runnerCoverage.sourceIds | Sort-Object)
    })
}

$allCoverageFiles = @(Get-ChildItem $resolvedResultsDirectory -Filter 'coverage.cobertura.xml' -File -Recurse)
$boundLogicalReports = @($reportBindings | ForEach-Object {
    "$($_.projectName)|$($_.coverageSha256)"
})
$actualLogicalReports = @(
    $allCoverageFiles |
        ForEach-Object {
            $relativePath = [IO.Path]::GetRelativePath(
                $resolvedResultsDirectory,
                [IO.Path]::GetFullPath($_.FullName)).Replace('\', '/')
            if ($relativePath -eq '..' -or
                $relativePath.StartsWith('../', [StringComparison]::Ordinal) -or
                -not $relativePath.Contains('/')) {
                throw "Coverage report tree contains a copy outside a runner directory: $($_.FullName)."
            }
            $runnerName = $relativePath.Substring(0, $relativePath.IndexOf('/'))
            "$runnerName|$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())"
        } |
        Sort-Object -Unique
)
Assert-ExactIdentitySet $boundLogicalReports $actualLogicalReports 'Required logical coverage report tree'

Assert-ObservedProductionCoverage `
    @($pdbUniverse.assemblyIds) `
    @($pdbUniverse.executableSourceIds) `
    @($observedAssemblyIds) `
    @($observedSourceIds)

$mergedMap = Merge-WithAuthoritativeUniverse $pdbUniverse.map $observedMap
$metrics = Get-CoverageMetrics $mergedMap
if ([int]$metrics.totalLines -le 0 -or [int]$metrics.totalBranches -le 0) {
    throw "Coverage reports/PDB universe contain no production line/branch data: lines=$($metrics.totalLines) branches=$($metrics.totalBranches)."
}
$sourceUniverse = Get-SourceUniverse $mergedMap

$baseline = Get-Content $resolvedBaselinePath -Raw | ConvertFrom-Json -Depth 64
if ([int]$baseline.schemaVersion -notin @(1, 2)) {
    throw "Unsupported coverage baseline schemaVersion '$($baseline.schemaVersion)'."
}
if ([int]$baseline.schemaVersion -eq 2) {
    foreach ($requiredBaselineProperty in @(
        'productionAssemblyIds',
        'productionSourceIds',
        'executableSourceCount',
        'executableSourceIds',
        'sourceUniverse')) {
        if ($null -eq $baseline.PSObject.Properties[$requiredBaselineProperty]) {
            throw "Coverage schemaVersion=2 baseline is missing '$requiredBaselineProperty'."
        }
    }
}
if ([int]$baseline.schemaVersion -eq 2 -and
    [int]$baseline.requiredReportCount -ne $requiredProjects.Count) {
    throw "Coverage report count changed: actual=$($requiredProjects.Count), baseline=$($baseline.requiredReportCount)."
}

if ([int]$baseline.schemaVersion -eq 1) {
    if (-not $UpdateBaseline) {
        throw 'Coverage baseline schemaVersion=1 must be migrated once from a clean full-universe run with -UpdateBaseline.'
    }
}
else {
    Assert-CoverageThreshold $metrics `
        ([double]$baseline.minimumLineRate) `
        ([double]$baseline.minimumBranchRate)
    if ($UpdateBaseline) {
        Assert-ReviewedUniverseUpdate `
            @($baseline.productionAssemblyIds) `
            @($pdbUniverse.assemblyIds) `
            @($baseline.productionSourceIds) `
            @($pdbUniverse.sourceIds) `
            @($baseline.executableSourceIds) `
            @($pdbUniverse.executableSourceIds) `
            @($observedAssemblyIds) `
            @($observedSourceIds)
    }
    else {
        if ([int]$inventory.productionUniverse.assemblyCount -ne [int]$baseline.productionAssemblyCount -or
            [int]$inventory.productionUniverse.sourceCount -ne [int]$baseline.productionSourceCount -or
            [string]$inventory.productionUniverse.identitySha256 -cne [string]$baseline.productionUniverseIdentitySha256 -or
            [int]$pdbUniverse.assemblyCount -ne [int]$baseline.productionPdbAssemblyCount -or
            [string]$pdbUniverse.assemblySha256 -cne [string]$baseline.productionPdbAssemblySha256 -or
            [int]$pdbUniverse.executableSourceIds.Count -ne [int]$baseline.executableSourceCount -or
            [int]$sourceUniverse.fileCount -ne [int]$baseline.sourceUniverse.fileCount -or
            [int]$sourceUniverse.linesValid -ne [int]$baseline.sourceUniverse.linesValid -or
            [int]$sourceUniverse.branchesValid -ne [int]$baseline.sourceUniverse.branchesValid -or
            [string]$sourceUniverse.fileMetricsSha256 -cne [string]$baseline.sourceUniverse.fileMetricsSha256) {
            throw 'Coverage production assembly/source/PDB universe differs from the reviewed schemaVersion=2 baseline.'
        }
        Assert-ExactIdentitySet @($baseline.productionAssemblyIds) @($pdbUniverse.assemblyIds) `
            'Reviewed production assembly universe'
        Assert-ExactIdentitySet @($baseline.productionSourceIds) @($pdbUniverse.sourceIds) `
            'Reviewed production source universe'
        Assert-ExactIdentitySet @($baseline.executableSourceIds) @($pdbUniverse.executableSourceIds) `
            'Reviewed executable production source universe'
    }
}

$output = [ordered]@{
    schemaVersion = 2
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    repositoryHead = $currentHead
    repositoryClean = $true
    productionUniverse = $inventory.productionUniverse
    productionAssemblies = [ordered]@{
        assemblyCount = $pdbUniverse.assemblyCount
        assemblySha256 = $pdbUniverse.assemblySha256
        documentSourceCount = @($pdbUniverse.sourceIds).Count
        executableSourceCount = @($pdbUniverse.executableSourceIds).Count
        evidence = $pdbUniverse.evidence
    }
    sourceUniverse = $sourceUniverse
    metrics = [ordered]@{
        reportCount = $reportBindings.Count
        attachmentCount = $allCoverageFiles.Count
        logicalReports = $reportBindings.Count
        physicalCopies = $allCoverageFiles.Count
        requiredProjects = @($requiredProjects.projectName | Sort-Object)
        observedProductionAssemblies = $observedAssemblyIds.Count
        observedProductionSources = $observedSourceIds.Count
        coveredLines = $metrics.coveredLines
        totalLines = $metrics.totalLines
        lineRate = $metrics.lineRate
        coveredBranches = $metrics.coveredBranches
        totalBranches = $metrics.totalBranches
        branchRate = $metrics.branchRate
    }
    reports = @($reportBindings | Sort-Object projectName | ForEach-Object {
        [ordered]@{
            projectName = $_.projectName
            repositoryHead = $_.repositoryHead
            trxPath = [IO.Path]::GetRelativePath($RepositoryRoot, $_.trxPath).Replace('\', '/')
            trxSha256 = $_.trxSha256
            coveragePath = [IO.Path]::GetRelativePath($RepositoryRoot, $_.coveragePath).Replace('\', '/')
            coverageSha256 = $_.coverageSha256
            physicalCopies = $_.physicalCopies
            physicalCoveragePaths = @($_.physicalCoveragePaths | ForEach-Object {
                [IO.Path]::GetRelativePath($RepositoryRoot, $_).Replace('\', '/')
            })
            observedProductionAssemblies = $_.observedProductionAssemblies
            observedProductionSources = $_.observedProductionSources
        }
    })
}
New-Item (Split-Path $resolvedOutputPath -Parent) -ItemType Directory -Force | Out-Null
$output | ConvertTo-Json -Depth 12 | Set-Content $resolvedOutputPath -Encoding utf8NoBOM

if ($UpdateBaseline) {
    $minimumLineRate = if ([int]$baseline.schemaVersion -eq 2) {
        [Math]::Max([double]$baseline.minimumLineRate, [double]$metrics.lineRate)
    }
    else {
        [double]$metrics.lineRate
    }
    $minimumBranchRate = if ([int]$baseline.schemaVersion -eq 2) {
        [Math]::Max([double]$baseline.minimumBranchRate, [double]$metrics.branchRate)
    }
    else {
        [double]$metrics.branchRate
    }
    $updatedBaseline = [ordered]@{
        schemaVersion = 2
        requiredReportCount = $reportBindings.Count
        minimumLineRate = $minimumLineRate
        minimumBranchRate = $minimumBranchRate
        productionAssemblyCount = [int]$inventory.productionUniverse.assemblyCount
        productionSourceCount = [int]$inventory.productionUniverse.sourceCount
        productionUniverseIdentitySha256 = [string]$inventory.productionUniverse.identitySha256
        productionPdbAssemblyCount = [int]$pdbUniverse.assemblyCount
        productionPdbAssemblySha256 = [string]$pdbUniverse.assemblySha256
        productionAssemblyIds = @($pdbUniverse.assemblyIds)
        productionSourceIds = @($pdbUniverse.sourceIds)
        executableSourceCount = @($pdbUniverse.executableSourceIds).Count
        executableSourceIds = @($pdbUniverse.executableSourceIds)
        sourceUniverse = $sourceUniverse
    }
    $updatedBaseline | ConvertTo-Json -Depth 8 | Set-Content $resolvedBaselinePath -Encoding utf8NoBOM
}

Write-Host "AICopilot coverage passed. logicalReports=$($reportBindings.Count), physicalCopies=$($allCoverageFiles.Count), assemblies=$($pdbUniverse.assemblyCount), sources=$($pdbUniverse.sourceIds.Count), lines=$($metrics.coveredLines)/$($metrics.totalLines) ($($metrics.lineRate)), branches=$($metrics.coveredBranches)/$($metrics.totalBranches) ($($metrics.branchRate))."
