[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$OutputPath = 'artifacts/test-inventory.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-DirectProperty {
    param(
        [Parameter(Mandatory)] [xml]$ProjectXml,
        [Parameter(Mandatory)] [string]$Name
    )

    foreach ($group in @($ProjectXml.Project.PropertyGroup)) {
        $node = @($group.ChildNodes | Where-Object { $_.LocalName -eq $Name }) | Select-Object -First 1
        if ($null -ne $node) {
            return ([string]$node.InnerText).Trim()
        }
    }

    return ''
}

$root = (Resolve-Path $RepositoryRoot).Path
$solutionPath = Join-Path $root 'AICopilot.slnx'
if (-not (Test-Path $solutionPath -PathType Leaf)) {
    throw "AICopilot solution was not found: $solutionPath"
}

$solutionText = (Get-Content $solutionPath -Raw).Replace('\\', '/')
$projectFiles = @(Get-ChildItem (Join-Path $root 'src/tests') -Filter '*.csproj' -File -Recurse | Sort-Object FullName)
$inventory = [System.Collections.Generic.List[object]]::new()
$supportProjectAllowlist = @(
    'src/tests/AICopilot.Testing.McpServer/AICopilot.Testing.McpServer.csproj'
)

foreach ($projectFile in $projectFiles) {
    [xml]$projectXml = Get-Content $projectFile.FullName -Raw
    $relativePath = [System.IO.Path]::GetRelativePath($root, $projectFile.FullName).Replace('\\', '/')
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
    $isTestProjectText = Get-DirectProperty $projectXml 'IsTestProject'
    $isTestProject = $isTestProjectText -ieq 'true'
    $role = Get-DirectProperty $projectXml 'AICopilotTestRole'

    if (-not $isTestProject -and $role -ne 'Support') {
        throw "$relativePath must declare either IsTestProject=true or AICopilotTestRole=Support."
    }

    if ($solutionText -notmatch [regex]::Escape("Path=`"$relativePath`"")) {
        throw "$relativePath is not included in AICopilot.slnx."
    }

    if ($role -eq 'Support') {
        if ($relativePath -notin $supportProjectAllowlist) {
            throw "$relativePath declares Support but is not in the fixed support-project allowlist."
        }

        if ($isTestProjectText -ine 'false') {
            throw "$relativePath is Support and must directly declare IsTestProject=false."
        }

        $packageIds = @(
            $projectXml.SelectNodes('/Project/ItemGroup/PackageReference') |
                ForEach-Object { ([string]$_.Include).Trim() } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        $forbiddenTestPackages = @(
            $packageIds | Where-Object {
                $_ -ieq 'Microsoft.NET.Test.Sdk' -or
                $_ -ieq 'Microsoft.Testing.Platform' -or
                $_ -match '(?i)(^|\.)(xunit|nunit|mstest)(\.|$)'
            }
        )
        if ($forbiddenTestPackages.Count -ne 0) {
            throw "$relativePath is Support and must not reference test SDK/framework packages: $($forbiddenTestPackages -join ', ')."
        }

        $supportSources = @(
            Get-ChildItem $projectFile.Directory.FullName -Filter '*.cs' -File -Recurse |
                Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        )
        foreach ($source in $supportSources) {
            if ((Get-Content $source.FullName -Raw) -match '(?m)\[\s*(?:Xunit\.)?(?:Fact|Theory)(?:Attribute)?\s*(?:\(|\])') {
                throw "$relativePath is Support and must not declare Fact/Theory tests; found in $($source.FullName)."
            }
        }

        $inventory.Add([pscustomobject]@{
            projectName = $projectName
            path = $relativePath
            role = 'Support'
            kind = $null
            runtime = $null
            cadence = $null
            owner = $null
            required = $false
        })
        continue
    }

    $kind = Get-DirectProperty $projectXml 'AICopilotTestKind'
    $runtime = Get-DirectProperty $projectXml 'AICopilotTestRuntime'
    $cadence = Get-DirectProperty $projectXml 'AICopilotTestCadence'
    $owner = Get-DirectProperty $projectXml 'AICopilotTestOwner'
    $requiredText = Get-DirectProperty $projectXml 'AICopilotRequired'

    foreach ($entry in @{
        AICopilotTestKind = $kind
        AICopilotTestRuntime = $runtime
        AICopilotTestCadence = $cadence
        AICopilotTestOwner = $owner
        AICopilotRequired = $requiredText
    }.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.Value)) {
            throw "$relativePath is missing direct $($entry.Key) metadata."
        }
    }

    if ($requiredText -notin @('true', 'false')) {
        throw "$relativePath has invalid AICopilotRequired='$requiredText'; expected true or false."
    }

    $required = $requiredText -eq 'true'
    if ($required -and $cadence -ne 'PR') {
        throw "$relativePath is required but does not use PR cadence."
    }

    if ($runtime -eq 'LiveExternal' -and ($required -or $cadence -ne 'Manual')) {
        throw "$relativePath uses LiveExternal and must be Manual with AICopilotRequired=false."
    }

    $inventory.Add([pscustomobject]@{
        projectName = $projectName
        path = $relativePath
        role = 'Runner'
        kind = $kind
        runtime = $runtime
        cadence = $cadence
        owner = $owner
        required = $required
    })
}

if (@($inventory | Where-Object { $_.role -eq 'Runner' -and $_.required }).Count -eq 0) {
    throw 'No required AICopilot test runner was discovered.'
}

$resolvedOutputPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $root $OutputPath
}

$outputDirectory = Split-Path $resolvedOutputPath -Parent
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$document = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    solution = 'AICopilot.slnx'
    projects = @($inventory)
}
$document | ConvertTo-Json -Depth 5 | Set-Content $resolvedOutputPath -Encoding utf8

$requiredCount = @($inventory | Where-Object { $_.role -eq 'Runner' -and $_.required }).Count
$manualCount = @($inventory | Where-Object { $_.role -eq 'Runner' -and -not $_.required }).Count
Write-Host "AICopilot test inventory: required=$requiredCount, manual=$manualCount, support=$(@($inventory | Where-Object role -eq 'Support').Count)."
Write-Host "Inventory written to $resolvedOutputPath"
