[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$InventoryPath = 'artifacts/test-inventory.json',
    [string]$ResultsDirectory = 'artifacts/test-results',
    [string]$RunnerInputDirectory = 'artifacts/runner-inputs',
    [string]$BaselinePath = 'scripts/tests/baselines/aicopilot-coverage.json',
    [string]$OutputPath = 'artifacts/quality/aicopilot-coverage.json',
    [string]$BaseRef = 'origin/main',
    [switch]$UpdateBaseline,
    [switch]$RunGuardSelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Resolve-AICopilotQualityBase.ps1')

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

function Resolve-CoberturaProductionSourcePath {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$FileName,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$SourceRoots
    )

    $normalizedFileName = $FileName.Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($normalizedFileName)) {
        return $null
    }

    $repositoryFullPath = [IO.Path]::GetFullPath($RepositoryRoot)
    $candidatePaths = [Collections.Generic.List[string]]::new()
    if ([IO.Path]::IsPathRooted($FileName)) {
        $candidatePaths.Add([IO.Path]::GetFullPath($FileName))
    }
    elseif ($SourceRoots.Count -ne 0) {
        foreach ($sourceRoot in $SourceRoots) {
            if ([string]::IsNullOrWhiteSpace($sourceRoot)) {
                continue
            }
            $rootPath = if ([IO.Path]::IsPathRooted($sourceRoot)) {
                [IO.Path]::GetFullPath($sourceRoot)
            }
            else {
                [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $sourceRoot))
            }
            $candidatePaths.Add([IO.Path]::GetFullPath((Join-Path $rootPath $FileName)))
        }
    }
    else {
        $repositoryRelative = ConvertTo-ProductionSourcePath $FileName
        if ($null -ne $repositoryRelative) {
            $candidatePaths.Add([IO.Path]::GetFullPath((Join-Path $RepositoryRoot $repositoryRelative)))
        }
    }

    $resolved = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($candidatePath in @($candidatePaths | Sort-Object -Unique)) {
        $normalizedCandidate = [IO.Path]::GetFullPath($candidatePath)
        $relativePath = [IO.Path]::GetRelativePath(
            $repositoryFullPath,
            $normalizedCandidate).Replace('\', '/')
        if ($relativePath -eq '..' -or
            $relativePath.StartsWith('../', [StringComparison]::Ordinal)) {
            if ($normalizedCandidate.Replace('\', '/') -match
                '/src/(analyzers|core|hosts|infrastructure|services|shared)/.+\.cs$') {
                throw "Coverage production-looking source escapes RepositoryRoot: $normalizedCandidate."
            }
            continue
        }
        if ($relativePath -match $productionSourcePattern -and
            $relativePath -notmatch '(?i)/(?:bin|obj)/') {
            $null = $resolved.Add($relativePath)
        }
    }
    if ($resolved.Count -gt 1) {
        throw "Cobertura source roots resolve one filename to multiple production sources: filename=$FileName, candidates=[$(@($resolved | Sort-Object) -join ',')]."
    }
    if ($resolved.Count -eq 1) {
        return @($resolved)[0]
    }
    return $null
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

function Assert-CoverageReportCount {
    param(
        [Parameter(Mandatory)][int]$BaselineCount,
        [Parameter(Mandatory)][int]$ActualCount,
        [Parameter(Mandatory)][bool]$AllowBaselineUpdate
    )

    if ($BaselineCount -ne $ActualCount -and -not $AllowBaselineUpdate) {
        throw "Coverage report count changed: actual=$ActualCount, baseline=$BaselineCount."
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

function Assert-TrxStorageMatchesRunner {
    param(
        [Parameter(Mandatory)][string[]]$Storages,
        [Parameter(Mandatory)][string]$ProjectName
    )

    $storageAssembly = if ($Storages.Count -eq 1) {
        [IO.Path]::GetFileNameWithoutExtension($Storages[0])
    }
    else {
        ''
    }
    if ($Storages.Count -ne 1 -or
        -not [string]::Equals(
            $storageAssembly,
            $ProjectName,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$ProjectName TRX test-definition storage does not match the inventory runner assembly."
    }
}

function Resolve-RunnerBuildIdentityManifest {
    param(
        [Parameter(Mandatory)][object]$Manifest,
        [Parameter(Mandatory)][string[]]$RequiredProjectNames,
        [Parameter(Mandatory)][Collections.IDictionary]$AssemblyEvidenceByName
    )

    foreach ($property in @('runnerCount', 'synchronizedAssemblyCount', 'sha256', 'runners')) {
        if ($null -eq $Manifest.PSObject.Properties[$property]) {
            throw "Pre-test runner build identity is missing '$property'."
        }
    }
    $records = @($Manifest.runners)
    if ([int]$Manifest.runnerCount -ne $RequiredProjectNames.Count -or
        $records.Count -ne $RequiredProjectNames.Count) {
        throw "Pre-test runner build identity count differs from required inventory: declared=$([int]$Manifest.runnerCount) records=$($records.Count) required=$($RequiredProjectNames.Count)."
    }
    Assert-ExactIdentitySet `
        -Expected $RequiredProjectNames `
        -Actual @($records | ForEach-Object { [string]$_.projectName }) `
        -Label 'Pre-test runner build identity projects'

    $byProject = @{}
    $calculatedSynchronizedAssemblyCount = 0
    $canonical = ($records | Sort-Object projectName | ForEach-Object {
        $runnerIdentity = $_
        $projectName = [string]$runnerIdentity.projectName
        if ($byProject.ContainsKey($projectName)) {
            throw "Pre-test runner build identity contains duplicate project '$projectName'."
        }
        $targetPath = [string]$runnerIdentity.targetPath
        if ([string]::IsNullOrWhiteSpace($targetPath) -or [IO.Path]::IsPathRooted($targetPath)) {
            throw "$projectName pre-test runner build identity has an invalid repository-relative targetPath '$targetPath'."
        }
        $targetFullPath = Resolve-RepositoryPath $targetPath
        $targetRelativePath = [IO.Path]::GetRelativePath(
            [IO.Path]::GetFullPath($RepositoryRoot),
            $targetFullPath).Replace('\', '/')
        if ($targetRelativePath -eq '..' -or
            $targetRelativePath.StartsWith('../', [StringComparison]::Ordinal) -or
            $targetRelativePath -cne $targetPath.Replace('\', '/') -or
            [IO.Path]::GetFileNameWithoutExtension($targetFullPath) -cne $projectName) {
            throw "$projectName pre-test runner build identity targetPath escapes or differs from its runner identity: $targetPath."
        }

        $assemblyRecords = @($runnerIdentity.productionAssemblies)
        if ([int]$runnerIdentity.productionAssemblyCount -ne $assemblyRecords.Count -or
            [int]$runnerIdentity.synchronizedAssemblyCount -ne
                @($assemblyRecords | Where-Object {
                    $_.PSObject.Properties['synchronized'] -and
                    $_.synchronized -is [bool] -and
                    [bool]$_.synchronized
                }).Count) {
            throw "$projectName pre-test runner build identity count is inconsistent."
        }
        $calculatedSynchronizedAssemblyCount += [int]$runnerIdentity.synchronizedAssemblyCount
        $assemblyByName = @{}
        foreach ($runnerAssembly in $assemblyRecords) {
            $assemblyName = [string]$runnerAssembly.assembly
            if ([string]::IsNullOrWhiteSpace($assemblyName) -or
                $assemblyByName.ContainsKey($assemblyName) -or
                -not $AssemblyEvidenceByName.ContainsKey($assemblyName)) {
                throw "$projectName pre-test runner build identity contains an invalid, duplicate, or unknown assembly '$assemblyName'."
            }
            if ($null -eq $runnerAssembly.PSObject.Properties['synchronized'] -or
                $runnerAssembly.synchronized -isnot [bool]) {
                throw "$projectName pre-test runner build identity has an invalid synchronized flag for $assemblyName."
            }
            $expectedAssembly = $AssemblyEvidenceByName[$assemblyName]
            if ([string]$runnerAssembly.assemblySha256 -cne [string]$expectedAssembly.assemblySha256 -or
                [string]$runnerAssembly.pdbSha256 -cne [string]$expectedAssembly.pdbSha256) {
                throw "$projectName pre-test runner build identity is not bound to the canonical inventory assembly/PDB: $assemblyName."
            }
            foreach ($hashProperty in @(
                'preSynchronizationAssemblySha256',
                'preSynchronizationPdbSha256',
                'assemblySha256',
                'pdbSha256')) {
                if ([string]$runnerAssembly.$hashProperty -notmatch '^[0-9a-f]{64}$') {
                    throw "$projectName pre-test runner build identity has an invalid $hashProperty for $assemblyName."
                }
            }
            $identityChanged = (
                [string]$runnerAssembly.preSynchronizationAssemblySha256 -cne
                    [string]$runnerAssembly.assemblySha256 -or
                [string]$runnerAssembly.preSynchronizationPdbSha256 -cne
                    [string]$runnerAssembly.pdbSha256)
            if ([bool]$runnerAssembly.synchronized -ne $identityChanged) {
                throw "$projectName pre-test runner build identity synchronized flag differs from its pre/post hashes for $assemblyName."
            }
            $assemblyByName[$assemblyName] = $runnerAssembly
        }
        $byProject[$projectName] = [pscustomobject]@{
            record = $runnerIdentity
            assemblies = $assemblyByName
        }
        @(
            "$projectName`0$targetPath`0$([int]$runnerIdentity.productionAssemblyCount)`0$([int]$runnerIdentity.synchronizedAssemblyCount)"
            @($assemblyRecords | Sort-Object assembly | ForEach-Object {
                "$projectName`0$([string]$_.assembly)`0$([bool]$_.synchronized)`0$([string]$_.preSynchronizationAssemblySha256)`0$([string]$_.preSynchronizationPdbSha256)`0$([string]$_.assemblySha256)`0$([string]$_.pdbSha256)"
            })
        ) -join "`n"
    }) -join "`n"
    if ([int]$Manifest.synchronizedAssemblyCount -ne $calculatedSynchronizedAssemblyCount) {
        throw "Pre-test runner build identity synchronized count differs from its records: declared=$([int]$Manifest.synchronizedAssemblyCount) actual=$calculatedSynchronizedAssemblyCount."
    }
    if ([string]$Manifest.sha256 -notmatch '^[0-9a-f]{64}$' -or
        (Get-TextSha256 $canonical) -cne [string]$Manifest.sha256) {
        throw 'Pre-test runner build identity digest differs from the inventory-bound manifest.'
    }
    return [pscustomobject]@{
        byProject = $byProject
        canonical = $canonical
        synchronizedAssemblyCount = $calculatedSynchronizedAssemblyCount
    }
}

function Resolve-RunnerLaunchInputDocument {
    param(
        [Parameter(Mandatory)][object]$Document,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][string]$RepositoryHead,
        [Parameter(Mandatory)][string]$InventorySha256,
        [Parameter(Mandatory)][Collections.IDictionary]$AssemblyEvidenceByName,
        [Parameter(Mandatory)][object]$InventoryRunnerIdentity
    )

    foreach ($property in @(
        'schemaVersion',
        'generatedAtUtc',
        'repositoryHead',
        'inventorySha256',
        'projectName',
        'targetPath',
        'productionAssemblyCount',
        'synchronizedAssemblyCount',
        'sha256',
        'productionAssemblies')) {
        if ($null -eq $Document.PSObject.Properties[$property]) {
            throw "$ProjectName per-runner launch input is missing '$property'."
        }
    }
    if ([int]$Document.schemaVersion -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$Document.generatedAtUtc) -or
        [string]$Document.repositoryHead -cne $RepositoryHead -or
        [string]$Document.inventorySha256 -cne $InventorySha256 -or
        [string]$Document.projectName -cne $ProjectName) {
        throw "$ProjectName per-runner launch input is not bound to the current inventory/HEAD/project."
    }
    $manifest = [pscustomobject]@{
        runnerCount = 1
        synchronizedAssemblyCount = [int]$Document.synchronizedAssemblyCount
        sha256 = [string]$Document.sha256
        runners = @(
            [pscustomobject]@{
                projectName = [string]$Document.projectName
                targetPath = [string]$Document.targetPath
                productionAssemblyCount = [int]$Document.productionAssemblyCount
                synchronizedAssemblyCount = [int]$Document.synchronizedAssemblyCount
                productionAssemblies = @($Document.productionAssemblies)
            }
        )
    }
    $resolved = Resolve-RunnerBuildIdentityManifest `
        $manifest `
        @($ProjectName) `
        $AssemblyEvidenceByName
    $launchIdentity = $resolved.byProject[$ProjectName]
    if ([string]$launchIdentity.record.targetPath -cne
        [string]$InventoryRunnerIdentity.record.targetPath) {
        throw "$ProjectName per-runner launch target differs from the evaluated inventory closure."
    }
    Assert-ExactIdentitySet `
        -Expected @($InventoryRunnerIdentity.assemblies.Keys) `
        -Actual @($launchIdentity.assemblies.Keys) `
        -Label "$ProjectName per-runner launch production closure"
    foreach ($assemblyName in @($InventoryRunnerIdentity.assemblies.Keys | Sort-Object)) {
        $inventoryAssembly = $InventoryRunnerIdentity.assemblies[$assemblyName]
        $launchAssembly = $launchIdentity.assemblies[$assemblyName]
        if ([string]$launchAssembly.assemblySha256 -cne
                [string]$inventoryAssembly.assemblySha256 -or
            [string]$launchAssembly.pdbSha256 -cne
                [string]$inventoryAssembly.pdbSha256) {
            throw "$ProjectName per-runner launch bytes differ from the evaluated inventory closure: $assemblyName."
        }
    }
    return $launchIdentity
}

function Assert-RunnerTargetStoragePath {
    param(
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][string]$TargetFullPath,
        [Parameter(Mandatory)][string]$EvidenceFullPath,
        [Parameter(Mandatory)][string]$EvidenceLabel,
        [Parameter(Mandatory)][StringComparison]$PathComparison
    )

    if (-not [string]::Equals($EvidenceFullPath, $TargetFullPath, $PathComparison)) {
        throw "$ProjectName TRX $EvidenceLabel differs from its inventory-bound pre-test runner target: manifest=$TargetFullPath trx=$EvidenceFullPath."
    }
}

function Assert-RunnerBuildIdentityPostTest {
    param(
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][string]$TargetPath,
        [Parameter(Mandatory)][string[]]$Storages,
        [Parameter(Mandatory)][string[]]$CodeBases,
        [Parameter(Mandatory)][Collections.IDictionary]$AssemblyRecords,
        [Parameter(Mandatory)][string[]]$AllProductionAssemblyNames
    )

    Assert-TrxStorageMatchesRunner $Storages $ProjectName
    if ($CodeBases.Count -ne 1 -or
        -not [string]::Equals(
            [IO.Path]::GetFileNameWithoutExtension($CodeBases[0]),
            $ProjectName,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$ProjectName TRX must contain exactly one case-preserving runner TestMethod.codeBase."
    }
    $targetFullPath = Resolve-RepositoryPath $TargetPath
    $storageFullPath = if ([IO.Path]::IsPathRooted($Storages[0])) {
        [IO.Path]::GetFullPath($Storages[0])
    }
    else {
        Resolve-RepositoryPath $Storages[0]
    }
    $codeBaseFullPath = if ([IO.Path]::IsPathRooted($CodeBases[0])) {
        [IO.Path]::GetFullPath($CodeBases[0])
    }
    else {
        Resolve-RepositoryPath $CodeBases[0]
    }
    $codeBasePathComparison = if ($IsWindows) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else {
        [StringComparison]::Ordinal
    }
    Assert-RunnerTargetStoragePath `
        $ProjectName `
        $targetFullPath `
        $codeBaseFullPath `
        'codeBase' `
        $codeBasePathComparison
    Assert-RunnerTargetStoragePath `
        $ProjectName `
        $targetFullPath `
        $storageFullPath `
        'storage' `
        ([StringComparison]::OrdinalIgnoreCase)

    $runnerBinDirectory = Split-Path $targetFullPath -Parent
    $expectedAssemblyNames = @($AssemblyRecords.Keys | Sort-Object)
    foreach ($assemblyName in @($AllProductionAssemblyNames | Sort-Object -Unique)) {
        $runnerAssemblyPath = Join-Path $runnerBinDirectory "$assemblyName.dll"
        $runnerPdbPath = Join-Path $runnerBinDirectory "$assemblyName.pdb"
        $assemblyExists = Test-Path $runnerAssemblyPath -PathType Leaf
        $pdbExists = Test-Path $runnerPdbPath -PathType Leaf
        if ($assemblyName -notin $expectedAssemblyNames) {
            if ($assemblyExists -or $pdbExists) {
                throw "$ProjectName post-test runner output contains production DLL/PDB outside its launch closure: assembly=$assemblyName dll=$assemblyExists pdb=$pdbExists."
            }
            continue
        }
        $record = $AssemblyRecords[$assemblyName]
        if (-not $assemblyExists -or
            -not $pdbExists -or
            (Get-FileHash $runnerAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
                [string]$record.assemblySha256 -or
            (Get-FileHash $runnerPdbPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
                [string]$record.pdbSha256) {
            throw "$ProjectName post-test runner input differs from its inventory-bound pre-test manifest: $assemblyName."
        }
    }
    return $runnerBinDirectory
}

function Get-CoberturaLineNodes {
    param([Parameter(Mandatory)][Xml.XmlElement]$ClassNode)

    return @($ClassNode.SelectNodes('lines/line'))
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

    Assert-TrxStorageMatchesRunner `
        @('/results/aicopilot.aggregatetests.dll') `
        'AICopilot.AggregateTests'
    $storagePrefixFailure = $null
    try {
        Assert-TrxStorageMatchesRunner `
            @('/results/aicopilot.aggregatetests.shadow.dll') `
            'AICopilot.AggregateTests'
    }
    catch {
        $storagePrefixFailure = $_.Exception.Message
    }
    if ($storagePrefixFailure -notmatch 'does not match the inventory runner assembly') {
        throw "Same-prefix TRX storage fixture did not fail closed: $storagePrefixFailure"
    }

    $emptyCoveragePath = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText(
            $emptyCoveragePath,
            '<coverage><packages><package name="A"><classes><class filename="core/A.cs"><lines /></class></classes></package></packages></coverage>',
            [Text.UTF8Encoding]::new($false))
        $emptyCoverage = Get-ObservedCoverageMap `
            $emptyCoveragePath `
            @{
                'core/A.cs:1' = [ordered]@{
                    filename = 'core/A.cs'
                    number = 1
                    hits = 0
                    branchValid = 0
                    branchCovered = 0
                }
            } `
            @{
                'src/core/A.cs' = [pscustomobject]@{
                    assembly = 'A'
                    project = 'src/core/A/A.csproj'
                    source = 'src/core/A.cs'
                }
            }
        if ($emptyCoverage.map.Count -ne 0 -or
            @($emptyCoverage.sourceIds).Count -ne 0 -or
            @($emptyCoverage.assemblyIds).Count -ne 0) {
            throw 'Cobertura <lines/> must contribute zero sequence points and zero observed source/assembly identities.'
        }
    }
    finally {
        Remove-Item $emptyCoveragePath -Force -ErrorAction SilentlyContinue
    }

    $rootStrippedAnalyzerPath = [IO.Path]::GetFullPath(
        (Join-Path $RepositoryRoot 'src/analyzers/AICopilot.Architecture.Analyzers/AICopilotArchitectureAnalyzer.cs')).TrimStart(
            [IO.Path]::DirectorySeparatorChar)
    $resolvedAnalyzerPath = Resolve-CoberturaProductionSourcePath `
        $rootStrippedAnalyzerPath `
        @([IO.Path]::DirectorySeparatorChar.ToString())
    if ([string]$resolvedAnalyzerPath -cne
        'src/analyzers/AICopilot.Architecture.Analyzers/AICopilotArchitectureAnalyzer.cs') {
        throw "Root-stripped absolute Cobertura source did not resolve to the exact repository path: $resolvedAnalyzerPath"
    }

    $externalSourceFailure = $null
    try {
        Resolve-CoberturaProductionSourcePath `
            'outside/src/core/A.cs' `
            @([IO.Path]::GetTempPath()) *> $null
    }
    catch {
        $externalSourceFailure = $_.Exception.Message
    }
    if ($externalSourceFailure -notmatch 'escapes RepositoryRoot') {
        throw "External production-looking Cobertura source fixture did not fail closed: $externalSourceFailure"
    }

    $ambiguousSourceFailure = $null
    try {
        Resolve-CoberturaProductionSourcePath `
            'Fixture.cs' `
            @(
                (Join-Path $RepositoryRoot 'src/core'),
                (Join-Path $RepositoryRoot 'src/shared')
            ) *> $null
    }
    catch {
        $ambiguousSourceFailure = $_.Exception.Message
    }
    if ($ambiguousSourceFailure -notmatch 'multiple production sources') {
        throw "Ambiguous Cobertura source-root fixture did not fail closed: $ambiguousSourceFailure"
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

    Assert-CoverageReportCount 16 17 $true
    $reportCountFailure = $null
    try {
        Assert-CoverageReportCount 16 17 $false
    }
    catch {
        $reportCountFailure = $_.Exception.Message
    }
    if ($reportCountFailure -notmatch 'Coverage report count changed') {
        throw "Coverage report-count transition fixture did not fail closed: $reportCountFailure"
    }

    $hashA = ('a' * 64) -join ''
    $hashB = ('b' * 64) -join ''
    $manifestTargetPath = 'artifacts/coverage-runner-identity-fixture/RunnerA.dll'
    $newManifest = {
        param(
            [object]$Synchronized = $false,
            [string]$PreAssemblyHash = $hashA,
            [string]$AssemblyName = 'AssemblyA',
            [int]$RunnerCount = 1,
            [string]$DigestOverride = ''
        )

        $synchronizedCount = if ($Synchronized -is [bool] -and [bool]$Synchronized) { 1 } else { 0 }
        $record = [pscustomobject]@{
            assembly = $AssemblyName
            synchronized = $Synchronized
            preSynchronizationAssemblySha256 = $PreAssemblyHash
            preSynchronizationPdbSha256 = $hashB
            assemblySha256 = $hashA
            pdbSha256 = $hashB
        }
        $canonical = @(
            "RunnerA`0$manifestTargetPath`01`0$([int]([bool]$Synchronized))"
            "RunnerA`0$AssemblyName`0$([bool]$Synchronized)`0$PreAssemblyHash`0$hashB`0$hashA`0$hashB"
        ) -join "`n"
        return [pscustomobject]@{
            runnerCount = $RunnerCount
            synchronizedAssemblyCount = $synchronizedCount
            sha256 = if ([string]::IsNullOrWhiteSpace($DigestOverride)) {
                Get-TextSha256 $canonical
            }
            else {
                $DigestOverride
            }
            runners = @(
                [pscustomobject]@{
                    projectName = 'RunnerA'
                    targetPath = $manifestTargetPath
                    productionAssemblyCount = 1
                    synchronizedAssemblyCount = $synchronizedCount
                    productionAssemblies = @($record)
                }
            )
        }
    }
    $assemblyEvidence = @{
        AssemblyA = [pscustomobject]@{
            assemblySha256 = $hashA
            pdbSha256 = $hashB
        }
    }
    $validManifest = & $newManifest
    $resolvedManifest = Resolve-RunnerBuildIdentityManifest `
        $validManifest `
        @('RunnerA') `
        $assemblyEvidence
    if ($resolvedManifest.byProject.Count -ne 1) {
        throw 'Valid pre-test runner build identity manifest did not resolve one runner.'
    }
    $launchDocument = [pscustomobject]@{
        schemaVersion = 1
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        repositoryHead = ('c' * 40) -join ''
        inventorySha256 = $hashB
        projectName = 'RunnerA'
        targetPath = $manifestTargetPath
        productionAssemblyCount = 1
        synchronizedAssemblyCount = 0
        sha256 = [string]$validManifest.sha256
        productionAssemblies = @($validManifest.runners[0].productionAssemblies)
    }
    $resolvedLaunch = Resolve-RunnerLaunchInputDocument `
        $launchDocument `
        'RunnerA' `
        $launchDocument.repositoryHead `
        $hashB `
        $assemblyEvidence `
        $resolvedManifest.byProject['RunnerA']
    if ($resolvedLaunch.assemblies.Count -ne 1) {
        throw 'Valid per-runner launch input did not resolve one production assembly.'
    }
    $launchDocument.inventorySha256 = $hashA
    $launchBindingFailure = $null
    try {
        Resolve-RunnerLaunchInputDocument `
            $launchDocument `
            'RunnerA' `
            $launchDocument.repositoryHead `
            $hashB `
            $assemblyEvidence `
            $resolvedManifest.byProject['RunnerA'] *> $null
    }
    catch {
        $launchBindingFailure = $_.Exception.Message
    }
    if ($launchBindingFailure -notmatch 'not bound to the current inventory/HEAD/project') {
        throw "Per-runner launch inventory binding fixture did not fail closed: $launchBindingFailure"
    }

    foreach ($manifestFailureFixture in @(
        [pscustomobject]@{
            manifest = (& $newManifest -RunnerCount 2)
            expected = 'count differs from required inventory'
            label = 'count'
        },
        [pscustomobject]@{
            manifest = (& $newManifest -DigestOverride $hashB)
            expected = 'digest differs'
            label = 'digest'
        },
        [pscustomobject]@{
            manifest = (& $newManifest -Synchronized 'false')
            expected = 'invalid synchronized flag'
            label = 'flag'
        },
        [pscustomobject]@{
            manifest = (& $newManifest -PreAssemblyHash 'x')
            expected = 'invalid preSynchronizationAssemblySha256'
            label = 'pre-hash'
        },
        [pscustomobject]@{
            manifest = (& $newManifest -AssemblyName 'UnknownAssembly')
            expected = 'invalid, duplicate, or unknown assembly'
            label = 'membership'
        }
    )) {
        $manifestFailure = $null
        try {
            Resolve-RunnerBuildIdentityManifest `
                $manifestFailureFixture.manifest `
                @('RunnerA') `
                $assemblyEvidence *> $null
        }
        catch {
            $manifestFailure = $_.Exception.Message
        }
        if ($manifestFailure -notmatch [string]$manifestFailureFixture.expected) {
            throw "Pre-test runner build identity $($manifestFailureFixture.label) fixture did not fail closed: $manifestFailure"
        }
    }

    $postTestRoot = Join-Path $RepositoryRoot "artifacts/coverage-runner-post-test-$([Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $postTestRoot -Force | Out-Null
        $targetFullPath = Join-Path $postTestRoot 'RunnerA.dll'
        $assemblyPath = Join-Path $postTestRoot 'AssemblyA.dll'
        $pdbPath = Join-Path $postTestRoot 'AssemblyA.pdb'
        [IO.File]::WriteAllBytes($targetFullPath, [byte[]](1, 2, 3))
        [IO.File]::WriteAllBytes($assemblyPath, [byte[]](4, 5, 6))
        [IO.File]::WriteAllBytes($pdbPath, [byte[]](7, 8, 9))
        $postTestRecord = [pscustomobject]@{
            assemblySha256 = (Get-FileHash $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
            pdbSha256 = (Get-FileHash $pdbPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $postTestTargetPath = [IO.Path]::GetRelativePath(
            [IO.Path]::GetFullPath($RepositoryRoot),
            $targetFullPath).Replace('\', '/')
        Assert-RunnerBuildIdentityPostTest `
            'RunnerA' `
            $postTestTargetPath `
            @($targetFullPath.ToLowerInvariant()) `
            @($targetFullPath) `
            @{ AssemblyA = $postTestRecord } `
            @('AssemblyA', 'AssemblyExtra') *> $null
        Assert-RunnerTargetStoragePath `
            'RunnerA' `
            $targetFullPath `
            $targetFullPath.ToLowerInvariant() `
            'storage' `
            ([StringComparison]::OrdinalIgnoreCase)
        Assert-RunnerTargetStoragePath `
            'RunnerA' `
            $targetFullPath `
            $targetFullPath.ToLowerInvariant() `
            'codeBase' `
            ([StringComparison]::OrdinalIgnoreCase)

        $storageFailure = $null
        try {
            Assert-RunnerBuildIdentityPostTest `
                'RunnerA' `
                $postTestTargetPath `
                @((Join-Path $postTestRoot 'other/RunnerA.dll')) `
                @($targetFullPath) `
                @{ AssemblyA = $postTestRecord } `
                @('AssemblyA', 'AssemblyExtra') *> $null
        }
        catch {
            $storageFailure = $_.Exception.Message
        }
        if ($storageFailure -notmatch 'TRX storage differs') {
            throw "Runner target/TRX storage binding fixture did not fail closed: $storageFailure"
        }

        $codeBaseFailure = $null
        try {
            Assert-RunnerBuildIdentityPostTest `
                'RunnerA' `
                $postTestTargetPath `
                @($targetFullPath.ToLowerInvariant()) `
                @((Join-Path $postTestRoot 'other/RunnerA.dll')) `
                @{ AssemblyA = $postTestRecord } `
                @('AssemblyA', 'AssemblyExtra') *> $null
        }
        catch {
            $codeBaseFailure = $_.Exception.Message
        }
        if ($codeBaseFailure -notmatch 'TRX codeBase differs') {
            throw "Runner target/TRX codeBase binding fixture did not fail closed: $codeBaseFailure"
        }

        [IO.File]::WriteAllBytes(
            (Join-Path $postTestRoot 'AssemblyExtra.dll'),
            [byte[]](16, 17, 18))
        [IO.File]::WriteAllBytes(
            (Join-Path $postTestRoot 'AssemblyExtra.pdb'),
            [byte[]](19, 20, 21))
        $postTestExtraFailure = $null
        try {
            Assert-RunnerBuildIdentityPostTest `
                'RunnerA' `
                $postTestTargetPath `
                @($targetFullPath.ToLowerInvariant()) `
                @($targetFullPath) `
                @{ AssemblyA = $postTestRecord } `
                @('AssemblyA', 'AssemblyExtra') *> $null
        }
        catch {
            $postTestExtraFailure = $_.Exception.Message
        }
        if ($postTestExtraFailure -notmatch 'outside its launch closure') {
            throw "Post-test runner closure-extra fixture did not fail closed: $postTestExtraFailure"
        }
        Remove-Item `
            (Join-Path $postTestRoot 'AssemblyExtra.dll'), `
            (Join-Path $postTestRoot 'AssemblyExtra.pdb') `
            -Force

        [IO.File]::WriteAllBytes($assemblyPath, [byte[]](9, 9, 9))
        $postTestHashFailure = $null
        try {
            Assert-RunnerBuildIdentityPostTest `
                'RunnerA' `
                $postTestTargetPath `
                @($targetFullPath.ToLowerInvariant()) `
                @($targetFullPath) `
                @{ AssemblyA = $postTestRecord } `
                @('AssemblyA', 'AssemblyExtra') *> $null
        }
        catch {
            $postTestHashFailure = $_.Exception.Message
        }
        if ($postTestHashFailure -notmatch 'post-test runner input differs') {
            throw "Post-test runner manifest hash fixture did not fail closed: $postTestHashFailure"
        }
    }
    finally {
        Remove-Item $postTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host 'AICopilot coverage omission guards passed. cases=29.'
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
    $coverageSourceRoots = @(
        $coverage.SelectNodes('/coverage/sources/source') |
            ForEach-Object { [string]$_.InnerText }
    )
    $map = @{}
    $sourceIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $assemblyIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($package in @($coverage.SelectNodes('/coverage/packages/package'))) {
        $packageName = [string]$package.GetAttribute('name')
        foreach ($class in @($package.SelectNodes('classes/class'))) {
            $sourcePath = Resolve-CoberturaProductionSourcePath `
                ([string]$class.GetAttribute('filename')) `
                $coverageSourceRoots
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
            $lineNodes = @(Get-CoberturaLineNodes $class)
            if ($lineNodes.Count -eq 0) {
                continue
            }
            foreach ($line in $lineNodes) {
                $lineNumber = [int]$line.GetAttribute('number')
                $coveragePath = $sourcePath.Substring(4)
                $key = "${coveragePath}:$lineNumber"
                if ($lineNumber -le 0 -or -not $PdbMap.ContainsKey($key)) {
                    throw "Coverage report contains a line outside the authoritative portable-PDB universe: $key."
                }
                $hits = [int]$line.GetAttribute('hits')
                $branchValid = 0
                $branchCovered = 0
                if ([string]$line.GetAttribute('branch') -ieq 'true' -and
                    [string]$line.GetAttribute('condition-coverage') -match '\((?<covered>\d+)\s*/\s*(?<total>\d+)\)') {
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
            $null = $sourceIds.Add("$([string]$source.assembly)|$sourcePath")
            $null = $assemblyIds.Add("$([string]$source.project)|$([string]$source.assembly)")
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
$resolvedRunnerInputDirectory = Resolve-RepositoryPath $RunnerInputDirectory
$resolvedBaselinePath = Resolve-RepositoryPath $BaselinePath
$resolvedOutputPath = Resolve-RepositoryPath $OutputPath

if (-not (Test-Path $resolvedInventoryPath -PathType Leaf)) {
    throw "Test inventory does not exist: $resolvedInventoryPath"
}
if (-not (Test-Path $resolvedResultsDirectory -PathType Container)) {
    throw "Test results directory does not exist: $resolvedResultsDirectory"
}
if (-not (Test-Path $resolvedRunnerInputDirectory -PathType Container)) {
    throw "Per-runner launch input directory does not exist: $resolvedRunnerInputDirectory"
}
if (-not (Test-Path $resolvedBaselinePath -PathType Leaf)) {
    throw "Coverage baseline does not exist: $resolvedBaselinePath"
}
$baselineContext = Get-AICopilotBaselineContext `
    -RepositoryRoot $RepositoryRoot `
    -BaseRef $BaseRef `
    -BaselineKind Coverage `
    -BaselinePath $resolvedBaselinePath

$inventorySha256 = (Get-FileHash $resolvedInventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()
$inventory = Get-Content $resolvedInventoryPath -Raw | ConvertFrom-Json -Depth 64
$repositoryCleanProperty = $inventory.PSObject.Properties['repositoryClean']
$productionAssembliesProperty = $inventory.PSObject.Properties['productionAssemblies']
$runnerBuildIdentityProperty = $inventory.PSObject.Properties['runnerBuildIdentity']
if ([int]$inventory.schemaVersion -lt 3 -or
    [string]::IsNullOrWhiteSpace([string]$inventory.repositoryHead) -or
    $null -eq $inventory.productionUniverse -or
    $null -eq $repositoryCleanProperty -or
    $null -eq $productionAssembliesProperty -or
    @($productionAssembliesProperty.Value).Count -eq 0 -or
    $null -eq $runnerBuildIdentityProperty) {
    throw 'Coverage requires schemaVersion>=3 inventory with repository/SHA, production source universe, evaluated assembly/PDB evidence, and pre-test runner build identity.'
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
$runnerBuildIdentity = $runnerBuildIdentityProperty.Value
$resolvedRunnerBuildIdentity = Resolve-RunnerBuildIdentityManifest `
    $runnerBuildIdentity `
    @($requiredProjects | ForEach-Object { [string]$_.projectName }) `
    $assemblyEvidenceByName
$runnerBuildIdentityByProject = $resolvedRunnerBuildIdentity.byProject
$expectedRunnerInputFiles = @($requiredProjects | ForEach-Object {
    "$([string]$_.projectName).json"
})
$actualRunnerInputFiles = @(
    Get-ChildItem $resolvedRunnerInputDirectory -Filter '*.json' -File |
        ForEach-Object { $_.Name }
)
Assert-ExactIdentitySet `
    -Expected $expectedRunnerInputFiles `
    -Actual $actualRunnerInputFiles `
    -Label 'Per-runner launch input evidence'
$runnerLaunchInputByProject = @{}
foreach ($project in $requiredProjects) {
    $projectName = [string]$project.projectName
    $runnerInputPath = Join-Path $resolvedRunnerInputDirectory "$projectName.json"
    $runnerInputDocument = Get-Content $runnerInputPath -Raw | ConvertFrom-Json -Depth 32
    $launchIdentity = Resolve-RunnerLaunchInputDocument `
        $runnerInputDocument `
        $projectName `
        $currentHead `
        $inventorySha256 `
        $assemblyEvidenceByName `
        $runnerBuildIdentityByProject[$projectName]
    $runnerLaunchInputByProject[$projectName] = [pscustomobject]@{
        path = $runnerInputPath
        sha256 = (Get-FileHash $runnerInputPath -Algorithm SHA256).Hash.ToLowerInvariant()
        identity = $launchIdentity
    }
}
foreach ($project in $requiredProjects) {
    $projectName = [string]$project.projectName
    if (-not $runnerBuildIdentityByProject.ContainsKey($projectName)) {
        throw "Pre-test runner build identity is missing required project '$projectName'."
    }
    if (-not $runnerLaunchInputByProject.ContainsKey($projectName)) {
        throw "Per-runner launch input is missing required project '$projectName'."
    }
    $runnerLaunchInput = $runnerLaunchInputByProject[$projectName]
    $preTestAssemblyByName = $runnerLaunchInput.identity.assemblies
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
    $codeBases = @($trx.TestRun.TestDefinitions.UnitTest.TestMethod | ForEach-Object {
        [string]$_.codeBase
    } | Sort-Object -Unique)
    $null = Assert-RunnerBuildIdentityPostTest `
        $projectName `
        ([string]$runnerLaunchInput.identity.record.targetPath) `
        $storages `
        $codeBases `
        $preTestAssemblyByName `
        @($assemblyEvidenceByName.Keys)

    $runnerCoverage = Get-ObservedCoverageMap `
        $boundCoveragePath `
        $pdbUniverse.map `
        $sourceByPath
    foreach ($observedAssemblyId in $runnerCoverage.assemblyIds) {
        $observedAssemblyName = ([string]$observedAssemblyId).Substring(
            ([string]$observedAssemblyId).LastIndexOf('|') + 1)
        if (-not $assemblyEvidenceByName.ContainsKey($observedAssemblyName)) {
            throw "$($project.projectName) report contains unknown production assembly '$observedAssemblyName'."
        }
        if (-not $preTestAssemblyByName.ContainsKey($observedAssemblyName)) {
            throw "$($project.projectName) report observed production assembly '$observedAssemblyName' that was absent from its inventory-bound pre-test runner input."
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
        runnerInputPath = [string]$runnerLaunchInput.path
        runnerInputSha256 = [string]$runnerLaunchInput.sha256
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
if ([int]$baseline.schemaVersion -eq 2) {
    Assert-CoverageReportCount `
        ([int]$baseline.requiredReportCount) `
        $requiredProjects.Count `
        ([bool]$UpdateBaseline)
}

if ($baselineContext.Mode -eq 'Ratchet') {
    $baseBaseline = $baselineContext.BaseBaselineJson | ConvertFrom-Json -Depth 64
    if ([int]$baseBaseline.schemaVersion -notin @(1, 2)) {
        throw "Unsupported base coverage baseline schemaVersion '$($baseBaseline.schemaVersion)'."
    }
    if ([double]$baseline.minimumLineRate + 0.00000001 -lt [double]$baseBaseline.minimumLineRate -or
        [double]$baseline.minimumBranchRate + 0.00000001 -lt [double]$baseBaseline.minimumBranchRate) {
        throw 'Candidate coverage baseline weakens base line/branch thresholds.'
    }
}

if ($baselineContext.Mode -eq 'Bootstrap' -and -not $UpdateBaseline -and
    ([Math]::Abs([double]$baseline.minimumLineRate - [double]$metrics.lineRate) -gt 0.00000001 -or
     [Math]::Abs([double]$baseline.minimumBranchRate - [double]$metrics.branchRate) -gt 0.00000001)) {
    throw "Initial coverage baseline must exactly reconcile candidate quality: line=$($metrics.lineRate)/$($baseline.minimumLineRate), branch=$($metrics.branchRate)/$($baseline.minimumBranchRate)."
}

if ([int]$baseline.schemaVersion -eq 1) {
    if (-not $UpdateBaseline) {
        throw 'Coverage baseline schemaVersion=1 must be migrated once from a clean full-universe run with -UpdateBaseline.'
    }
}
else {
    $isBootstrapUpdate = $baselineContext.Mode -eq 'Bootstrap' -and $UpdateBaseline
    if (-not $isBootstrapUpdate) {
        Assert-CoverageThreshold $metrics `
            ([double]$baseline.minimumLineRate) `
            ([double]$baseline.minimumBranchRate)
    }
    if ($UpdateBaseline) {
        if ($baselineContext.Mode -ne 'Bootstrap') {
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
            runnerInputPath = [IO.Path]::GetRelativePath(
                $RepositoryRoot,
                $_.runnerInputPath).Replace('\', '/')
            runnerInputSha256 = $_.runnerInputSha256
            observedProductionAssemblies = $_.observedProductionAssemblies
            observedProductionSources = $_.observedProductionSources
        }
    })
}
New-Item (Split-Path $resolvedOutputPath -Parent) -ItemType Directory -Force | Out-Null
$output | ConvertTo-Json -Depth 12 | Set-Content $resolvedOutputPath -Encoding utf8NoBOM

if ($UpdateBaseline) {
    $minimumLineRate = if ($baselineContext.Mode -eq 'Bootstrap') {
        [double]$metrics.lineRate
    }
    elseif ([int]$baseline.schemaVersion -eq 2) {
        [Math]::Max([double]$baseline.minimumLineRate, [double]$metrics.lineRate)
    }
    else {
        [double]$metrics.lineRate
    }
    $minimumBranchRate = if ($baselineContext.Mode -eq 'Bootstrap') {
        [double]$metrics.branchRate
    }
    elseif ([int]$baseline.schemaVersion -eq 2) {
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
