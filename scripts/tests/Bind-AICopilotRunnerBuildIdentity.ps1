[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$InventoryPath = 'artifacts/test-inventory.json',
    [Parameter(Mandatory)] [string]$ProjectName,
    [Parameter(Mandatory)] [string]$OutputPath,
    [switch]$SynchronizeRunnerBuildIdentity
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$global:LASTEXITCODE = 0

function Resolve-InputPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $root $Path))
}

function Resolve-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or
        [System.IO.Path]::IsPathRooted($Path)) {
        throw "$Label must be a non-empty repository-relative path: '$Path'."
    }
    $normalizedPath = $Path.Replace('\', '/')
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $root $normalizedPath))
    $relativePath = [System.IO.Path]::GetRelativePath($root, $fullPath).Replace('\', '/')
    if ($relativePath -eq '..' -or
        $relativePath.StartsWith('../', [System.StringComparison]::Ordinal) -or
        $relativePath -cne $normalizedPath) {
        throw "$Label escapes or is not canonical relative to RepositoryRoot: '$Path'."
    }
    return $fullPath
}

function Get-RequiredPropertyValue {
    param(
        [Parameter(Mandatory)][object]$InputObject,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Label
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Label is missing required property '$Name'."
    }
    return $property.Value
}

function Assert-Sha256 {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value,
        [Parameter(Mandatory)][string]$Label
    )

    if ($Value -cnotmatch '^[0-9a-f]{64}$') {
        throw "$Label must be a lowercase SHA-256 digest."
    }
}

function Get-TextSha256 {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)

    return [System.Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.Encoding]::UTF8.GetBytes($Value))).ToLowerInvariant()
}

function Assert-ExactSet {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Expected,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Actual,
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
        throw "$Label differs from the authoritative set: missing=[$($missing -join ',')], unexpected=[$($unexpected -join ',')]."
    }
}

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$resolvedInventoryPath = Resolve-InputPath $InventoryPath
$resolvedOutputPath = Resolve-InputPath $OutputPath
if (-not (Test-Path -LiteralPath $resolvedInventoryPath -PathType Leaf)) {
    throw "Test inventory does not exist: $resolvedInventoryPath"
}
if ($resolvedOutputPath -ceq $resolvedInventoryPath) {
    throw 'Runner build identity OutputPath must not overwrite its authoritative inventory.'
}

$inventorySha256 = (Get-FileHash -LiteralPath $resolvedInventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()
$inventory = Get-Content -LiteralPath $resolvedInventoryPath -Raw | ConvertFrom-Json -Depth 64
if ([int](Get-RequiredPropertyValue $inventory 'schemaVersion' 'Test inventory') -ne 3) {
    throw "Runner build identity binding requires test inventory schemaVersion=3."
}
$inventoryHead = [string](Get-RequiredPropertyValue $inventory 'repositoryHead' 'Test inventory')
if ($inventoryHead -cnotmatch '^[0-9a-f]{40}$') {
    throw "Test inventory repositoryHead is not a lowercase 40-character Git SHA: '$inventoryHead'."
}
$currentHeadLines = @(& git -C $root rev-parse HEAD 2>$null)
$headExitCode = $LASTEXITCODE
$currentHead = ($currentHeadLines -join "`n").Trim()
if ($headExitCode -ne 0 -or $currentHead -cnotmatch '^[0-9a-f]{40}$') {
    throw "Cannot resolve the current repository HEAD for runner build identity binding."
}
if ($currentHead -cne $inventoryHead) {
    throw "Current repository HEAD differs from the inventory-bound HEAD: inventory=$inventoryHead current=$currentHead."
}

$productionAssemblyRecords = @(
    Get-RequiredPropertyValue $inventory 'productionAssemblies' 'Test inventory'
)
if ($productionAssemblyRecords.Count -eq 0) {
    throw 'Test inventory contains no productionAssemblies.'
}
$productionByName = [System.Collections.Generic.Dictionary[string, object]]::new(
    [System.StringComparer]::Ordinal)
foreach ($assemblyRecord in $productionAssemblyRecords) {
    $assemblyName = [string](Get-RequiredPropertyValue $assemblyRecord 'assembly' 'Production assembly')
    if ([string]::IsNullOrWhiteSpace($assemblyName) -or
        $assemblyName -cnotmatch '^[A-Za-z0-9_.-]+$' -or
        $productionByName.ContainsKey($assemblyName)) {
        throw "Test inventory contains an empty, invalid, or duplicate production assembly '$assemblyName'."
    }
    $assemblyPath = [string](Get-RequiredPropertyValue $assemblyRecord 'assemblyPath' "$assemblyName production assembly")
    $pdbPath = [string](Get-RequiredPropertyValue $assemblyRecord 'pdbPath' "$assemblyName production assembly")
    $assemblyFullPath = Resolve-RepositoryRelativePath $assemblyPath "$assemblyName canonical assemblyPath"
    $pdbFullPath = Resolve-RepositoryRelativePath $pdbPath "$assemblyName canonical pdbPath"
    if ([System.IO.Path]::GetFileName($assemblyFullPath) -cne "$assemblyName.dll" -or
        [System.IO.Path]::GetFileName($pdbFullPath) -cne "$assemblyName.pdb") {
        throw "$assemblyName canonical assembly/PDB paths differ from their exact assembly identity."
    }
    if (-not (Test-Path -LiteralPath $assemblyFullPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $pdbFullPath -PathType Leaf)) {
        throw "$assemblyName canonical production DLL/PDB pair is missing."
    }
    $assemblySha256 = [string](Get-RequiredPropertyValue $assemblyRecord 'assemblySha256' "$assemblyName production assembly")
    $pdbSha256 = [string](Get-RequiredPropertyValue $assemblyRecord 'pdbSha256' "$assemblyName production assembly")
    Assert-Sha256 $assemblySha256 "$assemblyName canonical assemblySha256"
    Assert-Sha256 $pdbSha256 "$assemblyName canonical pdbSha256"
    $actualCanonicalAssemblySha256 = (
        Get-FileHash -LiteralPath $assemblyFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $actualCanonicalPdbSha256 = (
        Get-FileHash -LiteralPath $pdbFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualCanonicalAssemblySha256 -cne $assemblySha256 -or
        $actualCanonicalPdbSha256 -cne $pdbSha256) {
        throw "$assemblyName canonical production DLL/PDB differs from the inventory-bound bytes."
    }
    $productionByName.Add($assemblyName, [pscustomobject]@{
        record = $assemblyRecord
        assemblyPath = $assemblyFullPath
        pdbPath = $pdbFullPath
        assemblySha256 = $assemblySha256
        pdbSha256 = $pdbSha256
    })
}

$projectRecords = @(
    Get-RequiredPropertyValue $inventory 'projects' 'Test inventory'
)
$requiredRunnerProjects = @(
    $projectRecords | Where-Object {
        [string](Get-RequiredPropertyValue $_ 'role' 'Inventory project') -ceq 'Runner' -and
        [bool](Get-RequiredPropertyValue $_ 'required' 'Inventory runner')
    }
)
if ($requiredRunnerProjects.Count -eq 0) {
    throw 'Test inventory contains no required runner projects.'
}
$requiredRunnerNames = @(
    $requiredRunnerProjects | ForEach-Object {
        [string](Get-RequiredPropertyValue $_ 'projectName' 'Required runner')
    }
)
if (@($requiredRunnerNames | Group-Object | Where-Object Count -ne 1).Count -ne 0) {
    throw 'Test inventory contains duplicate required runner projectName values.'
}
$selectedProjects = @(
    $requiredRunnerProjects | Where-Object {
        [string]$_.projectName -ceq $ProjectName
    }
)
if ($selectedProjects.Count -ne 1) {
    throw "ProjectName must identify exactly one required inventory runner: '$ProjectName'."
}
$selectedProjectPath = [string](Get-RequiredPropertyValue $selectedProjects[0] 'path' "$ProjectName inventory runner")
$selectedProjectFullPath = Resolve-RepositoryRelativePath $selectedProjectPath "$ProjectName project path"
if (-not (Test-Path -LiteralPath $selectedProjectFullPath -PathType Leaf) -or
    [System.IO.Path]::GetFileNameWithoutExtension($selectedProjectFullPath) -cne $ProjectName -or
    [System.IO.Path]::GetExtension($selectedProjectFullPath) -cne '.csproj') {
    throw "$ProjectName inventory runner path does not identify its exact existing project: '$selectedProjectPath'."
}

$runnerBuildIdentity = Get-RequiredPropertyValue $inventory 'runnerBuildIdentity' 'Test inventory'
foreach ($propertyName in @('runnerCount', 'synchronizedAssemblyCount', 'sha256', 'runners')) {
    $null = Get-RequiredPropertyValue $runnerBuildIdentity $propertyName 'Test inventory runnerBuildIdentity'
}
$runnerIdentityRecords = @($runnerBuildIdentity.runners)
if ([int]$runnerBuildIdentity.runnerCount -ne $requiredRunnerNames.Count -or
    $runnerIdentityRecords.Count -ne $requiredRunnerNames.Count) {
    throw "runnerBuildIdentity count differs from required inventory runners."
}
Assert-ExactSet `
    -Expected $requiredRunnerNames `
    -Actual @($runnerIdentityRecords | ForEach-Object {
        [string](Get-RequiredPropertyValue $_ 'projectName' 'runnerBuildIdentity runner')
    }) `
    -Label 'runnerBuildIdentity projects'

$runnerIdentityByName = [System.Collections.Generic.Dictionary[string, object]]::new(
    [System.StringComparer]::Ordinal)
$calculatedManifestSynchronizedCount = 0
$manifestCanonicalRecords = [System.Collections.Generic.List[string]]::new()
foreach ($runnerIdentity in @($runnerIdentityRecords | Sort-Object projectName)) {
    $runnerProjectName = [string](Get-RequiredPropertyValue $runnerIdentity 'projectName' 'runnerBuildIdentity runner')
    if ([string]::IsNullOrWhiteSpace($runnerProjectName) -or
        $runnerIdentityByName.ContainsKey($runnerProjectName)) {
        throw "runnerBuildIdentity contains an empty or duplicate runner '$runnerProjectName'."
    }
    $runnerTargetPath = [string](Get-RequiredPropertyValue $runnerIdentity 'targetPath' "$runnerProjectName runnerBuildIdentity")
    $runnerTargetFullPath = Resolve-RepositoryRelativePath $runnerTargetPath "$runnerProjectName targetPath"
    if ([System.IO.Path]::GetFileNameWithoutExtension($runnerTargetFullPath) -cne $runnerProjectName -or
        [System.IO.Path]::GetExtension($runnerTargetFullPath) -cne '.dll') {
        throw "$runnerProjectName runnerBuildIdentity targetPath differs from its exact runner identity."
    }
    $runnerAssemblies = @(
        Get-RequiredPropertyValue $runnerIdentity 'productionAssemblies' "$runnerProjectName runnerBuildIdentity"
    )
    $declaredProductionAssemblyCount = [int](
        Get-RequiredPropertyValue $runnerIdentity 'productionAssemblyCount' "$runnerProjectName runnerBuildIdentity")
    $declaredSynchronizedCount = [int](
        Get-RequiredPropertyValue $runnerIdentity 'synchronizedAssemblyCount' "$runnerProjectName runnerBuildIdentity")
    if ($declaredProductionAssemblyCount -ne $runnerAssemblies.Count) {
        throw "$runnerProjectName runnerBuildIdentity production assembly count is inconsistent."
    }
    $runnerAssemblyNames = @(
        $runnerAssemblies | ForEach-Object {
            [string](Get-RequiredPropertyValue $_ 'assembly' "$runnerProjectName runner assembly")
        }
    )
    if (@($runnerAssemblyNames | Group-Object | Where-Object Count -ne 1).Count -ne 0) {
        throw "$runnerProjectName runnerBuildIdentity contains duplicate production assemblies."
    }
    $calculatedRunnerSynchronizedCount = 0
    $runnerCanonicalRecords = [System.Collections.Generic.List[string]]::new()
    $runnerCanonicalRecords.Add(
        "$runnerProjectName`0$runnerTargetPath`0$declaredProductionAssemblyCount`0$declaredSynchronizedCount")
    foreach ($runnerAssembly in @($runnerAssemblies | Sort-Object assembly)) {
        $assemblyName = [string]$runnerAssembly.assembly
        if (-not $productionByName.ContainsKey($assemblyName)) {
            throw "$runnerProjectName runnerBuildIdentity contains unknown production assembly '$assemblyName'."
        }
        $synchronizedProperty = $runnerAssembly.PSObject.Properties['synchronized']
        if ($null -eq $synchronizedProperty -or
            $synchronizedProperty.Value -isnot [bool]) {
            throw "$runnerProjectName runnerBuildIdentity has an invalid synchronized flag for '$assemblyName'."
        }
        $preAssemblySha256 = [string](
            Get-RequiredPropertyValue $runnerAssembly 'preSynchronizationAssemblySha256' "$runnerProjectName/$assemblyName")
        $prePdbSha256 = [string](
            Get-RequiredPropertyValue $runnerAssembly 'preSynchronizationPdbSha256' "$runnerProjectName/$assemblyName")
        $postAssemblySha256 = [string](
            Get-RequiredPropertyValue $runnerAssembly 'assemblySha256' "$runnerProjectName/$assemblyName")
        $postPdbSha256 = [string](
            Get-RequiredPropertyValue $runnerAssembly 'pdbSha256' "$runnerProjectName/$assemblyName")
        Assert-Sha256 $preAssemblySha256 "$runnerProjectName/$assemblyName preSynchronizationAssemblySha256"
        Assert-Sha256 $prePdbSha256 "$runnerProjectName/$assemblyName preSynchronizationPdbSha256"
        Assert-Sha256 $postAssemblySha256 "$runnerProjectName/$assemblyName assemblySha256"
        Assert-Sha256 $postPdbSha256 "$runnerProjectName/$assemblyName pdbSha256"
        $canonicalAssembly = $productionByName[$assemblyName]
        if ($postAssemblySha256 -cne [string]$canonicalAssembly.assemblySha256 -or
            $postPdbSha256 -cne [string]$canonicalAssembly.pdbSha256) {
            throw "$runnerProjectName runnerBuildIdentity is not bound to canonical inventory bytes for '$assemblyName'."
        }
        $identityChanged = (
            $preAssemblySha256 -cne $postAssemblySha256 -or
            $prePdbSha256 -cne $postPdbSha256)
        if ([bool]$synchronizedProperty.Value -ne $identityChanged) {
            throw "$runnerProjectName runnerBuildIdentity synchronized flag differs from its pre/post hashes for '$assemblyName'."
        }
        if ([bool]$synchronizedProperty.Value) {
            $calculatedRunnerSynchronizedCount++
        }
        $runnerCanonicalRecords.Add(
            "$runnerProjectName`0$assemblyName`0$([bool]$synchronizedProperty.Value)`0$preAssemblySha256`0$prePdbSha256`0$postAssemblySha256`0$postPdbSha256")
    }
    if ($declaredSynchronizedCount -ne $calculatedRunnerSynchronizedCount) {
        throw "$runnerProjectName runnerBuildIdentity synchronized count is inconsistent."
    }
    $calculatedManifestSynchronizedCount += $declaredSynchronizedCount
    $manifestCanonicalRecords.Add(($runnerCanonicalRecords -join "`n"))
    $runnerIdentityByName.Add($runnerProjectName, [pscustomobject]@{
        record = $runnerIdentity
        targetFullPath = $runnerTargetFullPath
        assemblyNames = @($runnerAssemblyNames | Sort-Object)
    })
}
if ([int]$runnerBuildIdentity.synchronizedAssemblyCount -ne $calculatedManifestSynchronizedCount) {
    throw 'runnerBuildIdentity synchronizedAssemblyCount differs from its runner records.'
}
$declaredManifestSha256 = [string]$runnerBuildIdentity.sha256
Assert-Sha256 $declaredManifestSha256 'runnerBuildIdentity sha256'
$calculatedManifestSha256 = Get-TextSha256 ($manifestCanonicalRecords -join "`n")
if ($calculatedManifestSha256 -cne $declaredManifestSha256) {
    throw "runnerBuildIdentity canonical SHA-256 differs from its records."
}

$selectedIdentity = $runnerIdentityByName[$ProjectName]
$selectedRunnerRecord = $selectedIdentity.record
$runnerTargetPath = [string]$selectedRunnerRecord.targetPath
$runnerTargetFullPath = [string]$selectedIdentity.targetFullPath
if (-not (Test-Path -LiteralPath $runnerTargetFullPath -PathType Leaf)) {
    throw "$ProjectName runner target does not exist: $runnerTargetPath"
}
$runnerOutputDirectory = Split-Path $runnerTargetFullPath -Parent
$expectedAssemblyNames = @($selectedIdentity.assemblyNames)
$expectedAssemblyNameSet = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($assemblyName in $expectedAssemblyNames) {
    [void]$expectedAssemblyNameSet.Add($assemblyName)
}

$preSynchronizationRecords = [System.Collections.Generic.List[object]]::new()
foreach ($assemblyName in @($productionByName.Keys | Sort-Object)) {
    $runnerAssemblyPath = Join-Path $runnerOutputDirectory "$assemblyName.dll"
    $runnerPdbPath = Join-Path $runnerOutputDirectory "$assemblyName.pdb"
    $runnerAssemblyExists = Test-Path -LiteralPath $runnerAssemblyPath -PathType Leaf
    $runnerPdbExists = Test-Path -LiteralPath $runnerPdbPath -PathType Leaf
    if (-not $expectedAssemblyNameSet.Contains($assemblyName)) {
        if ($runnerAssemblyExists -or $runnerPdbExists) {
            throw "$ProjectName runner output contains production DLL/PDB outside its exact inventory closure: assembly=$assemblyName dll=$runnerAssemblyExists pdb=$runnerPdbExists."
        }
        continue
    }
    if (-not $runnerAssemblyExists -or -not $runnerPdbExists) {
        throw "$ProjectName runner output is missing an exact-closure production DLL/PDB pair: assembly=$assemblyName dll=$runnerAssemblyExists pdb=$runnerPdbExists."
    }
    $canonicalAssembly = $productionByName[$assemblyName]
    $preAssemblySha256 = (
        Get-FileHash -LiteralPath $runnerAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $prePdbSha256 = (
        Get-FileHash -LiteralPath $runnerPdbPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $requiresSynchronization = (
        $preAssemblySha256 -cne [string]$canonicalAssembly.assemblySha256 -or
        $prePdbSha256 -cne [string]$canonicalAssembly.pdbSha256)
    $preSynchronizationRecords.Add([pscustomobject]@{
        assembly = $assemblyName
        runnerAssemblyPath = $runnerAssemblyPath
        runnerPdbPath = $runnerPdbPath
        canonicalAssembly = $canonicalAssembly
        preSynchronizationAssemblySha256 = $preAssemblySha256
        preSynchronizationPdbSha256 = $prePdbSha256
        requiresSynchronization = $requiresSynchronization
    })
}
Assert-ExactSet `
    -Expected $expectedAssemblyNames `
    -Actual @($preSynchronizationRecords | ForEach-Object { [string]$_.assembly }) `
    -Label "$ProjectName observed production assembly closure"

$mismatches = @($preSynchronizationRecords | Where-Object requiresSynchronization)
if ($mismatches.Count -ne 0 -and -not $SynchronizeRunnerBuildIdentity) {
    $firstMismatch = $mismatches[0]
    $canonicalAssembly = $firstMismatch.canonicalAssembly
    throw "$ProjectName runner bytes differ from the canonical inventory and synchronization was not explicitly enabled: assembly=$($firstMismatch.assembly) expectedDll=$($canonicalAssembly.assemblySha256) actualDll=$($firstMismatch.preSynchronizationAssemblySha256) expectedPdb=$($canonicalAssembly.pdbSha256) actualPdb=$($firstMismatch.preSynchronizationPdbSha256)."
}
foreach ($mismatch in $mismatches) {
    Copy-Item -LiteralPath $mismatch.canonicalAssembly.assemblyPath `
        -Destination $mismatch.runnerAssemblyPath -Force
    Copy-Item -LiteralPath $mismatch.canonicalAssembly.pdbPath `
        -Destination $mismatch.runnerPdbPath -Force
}

$boundAssemblyRecords = [System.Collections.Generic.List[object]]::new()
foreach ($record in @($preSynchronizationRecords | Sort-Object assembly)) {
    $actualAssemblySha256 = (
        Get-FileHash -LiteralPath $record.runnerAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $actualPdbSha256 = (
        Get-FileHash -LiteralPath $record.runnerPdbPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualAssemblySha256 -cne [string]$record.canonicalAssembly.assemblySha256 -or
        $actualPdbSha256 -cne [string]$record.canonicalAssembly.pdbSha256) {
        throw "$ProjectName runner synchronization did not produce canonical inventory bytes for '$($record.assembly)'."
    }
    $synchronized = (
        [string]$record.preSynchronizationAssemblySha256 -cne $actualAssemblySha256 -or
        [string]$record.preSynchronizationPdbSha256 -cne $actualPdbSha256)
    $boundAssemblyRecords.Add([pscustomobject][ordered]@{
        assembly = [string]$record.assembly
        synchronized = [bool]$synchronized
        preSynchronizationAssemblySha256 = [string]$record.preSynchronizationAssemblySha256
        preSynchronizationPdbSha256 = [string]$record.preSynchronizationPdbSha256
        assemblySha256 = $actualAssemblySha256
        pdbSha256 = $actualPdbSha256
    })
}

$synchronizedAssemblyCount = @($boundAssemblyRecords | Where-Object synchronized).Count
$runnerCanonical = @(
    "$ProjectName`0$runnerTargetPath`0$($boundAssemblyRecords.Count)`0$synchronizedAssemblyCount"
    @($boundAssemblyRecords | ForEach-Object {
        "$ProjectName`0$([string]$_.assembly)`0$([bool]$_.synchronized)`0$([string]$_.preSynchronizationAssemblySha256)`0$([string]$_.preSynchronizationPdbSha256)`0$([string]$_.assemblySha256)`0$([string]$_.pdbSha256)"
    })
) -join "`n"
$document = [pscustomobject][ordered]@{
    schemaVersion = 1
    generatedAtUtc = [System.DateTimeOffset]::UtcNow.ToString('O')
    repositoryHead = $inventoryHead
    inventorySha256 = $inventorySha256
    projectName = $ProjectName
    targetPath = $runnerTargetPath
    productionAssemblyCount = $boundAssemblyRecords.Count
    synchronizedAssemblyCount = $synchronizedAssemblyCount
    sha256 = Get-TextSha256 $runnerCanonical
    productionAssemblies = @($boundAssemblyRecords)
}

$outputDirectory = Split-Path $resolvedOutputPath -Parent
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$temporaryOutputPath = Join-Path $outputDirectory (
    ".$([System.IO.Path]::GetFileName($resolvedOutputPath)).$([Guid]::NewGuid().ToString('N')).tmp")
try {
    $json = $document | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText(
        $temporaryOutputPath,
        "$json`n",
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Move($temporaryOutputPath, $resolvedOutputPath, $true)
}
finally {
    if (Test-Path -LiteralPath $temporaryOutputPath -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryOutputPath -Force
    }
}

Write-Host (
    "AICopilot runner build identity: project=$ProjectName, assemblies=$($boundAssemblyRecords.Count), " +
    "synchronized=$synchronizedAssemblyCount, sha256=$($document.sha256).")
Write-Host "Runner build identity written to $resolvedOutputPath"
