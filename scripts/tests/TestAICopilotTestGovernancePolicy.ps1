[CmdletBinding()]
param(
    [ValidateSet('ValidateProject', 'ValidateRepository', 'ValidateSnapshot', 'ValidateRepositorySnapshot', 'ValidateStatic', 'ValidateDiscovery', 'ValidateRunnerConfiguration', 'ValidateRunnerCaseNormalization', 'GenerateBaseline')]
    [string]$Mode = 'ValidateProject',
    [string]$RepositoryRoot,
    [string]$ProjectPath,
    [string]$ProjectName,
    [string]$AssemblyPath,
    [string]$ReferencePathsFile,
    [string]$RunnerConfigPath,
    [string]$CurrentSnapshotPath,
    [string]$BaselinePath,
    [string]$WaiverPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$AllowBaselineWrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ruleId = 'AI-TEST-GOV-001'
$baselineSchemaVersion = '1.0'
$waiverSchemaVersion = '1.0'
$maximumWaiverDays = 30
$approvedWaiverApprovers = @('ShuJinHao')
$allowedTestKinds = @('Architecture', 'Unit', 'Aggregate', 'Application', 'Contract', 'Conformance', 'Persistence', 'Workflow', 'Integration', 'EndToEnd', 'UI', 'GoldenEval', 'Deployment', 'Performance', 'SoakChaos', 'Security')
$allowedRuntimes = @('Pure', 'InProcess', 'Filesystem', 'SQLite', 'Postgres', 'Redis', 'RabbitMQ', 'Docker', 'Aspire', 'Avalonia', 'Browser', 'Windows', 'LiveExternal')
$allowedRisks = @('P0', 'P1', 'P2')
$allowedOwners = @('AI.Architecture', 'AI.Domain', 'AI.Identity', 'AI.AgentWorkflow', 'AI.Rag', 'AI.DataAnalysis', 'AI.Mcp', 'AI.Persistence', 'AI.Infrastructure', 'AI.Http', 'AI.CloudRead', 'AI.Web', 'AI.Deployment', 'AI.Security', 'AI.Tests')
$allowedCapabilities = @('Architecture', 'Authentication', 'Authorization', 'Identity', 'AgentWorkflow', 'Approval', 'Artifacts', 'Rag', 'DataAnalysis', 'TextToSql', 'Mcp', 'ToolPlugin', 'CloudReadOnly', 'AiEval', 'Persistence', 'Cache', 'Messaging', 'Deployment', 'Web', 'Configuration', 'TestGovernance')
$allowedTestAttributeTypes = @('Xunit.FactAttribute', 'Xunit.TheoryAttribute')
$allowedSupportProjects = @('src/tests/AICopilot.Testing.McpServer/AICopilot.Testing.McpServer.csproj')
$allowedProjectSdkIdentities = @('Microsoft.NET.Sdk', 'Microsoft.NET.Sdk.Web', 'Microsoft.NET.Sdk.Worker', 'Aspire.AppHost.Sdk')
$reviewedBaselineSourceHead = '88a9687a40e7c78d671bdc634e90941b91f3bde1'
$xunitRunnerConfigSha256 = '3aaf68ea8927dce2c9ee5404088745084d709c1ff2d00bf41c90d9406d31b8a1'
$canonicalTestBuildPropsSha256 = '45ee96ad044c85fd484e7fc779c012ffcca0db2d3d4f99b5d6827ce3094212f4'
$rootBuildTargetsSha256 = 'bda7249faa78b13157cfb2304ea4bb6e0a89f2b108bc7ceb94eaebea3d6ec2af'
$codeOwnersSha256 = '0fb603aed9e7dd6f56e23418ed806d203522932f219feb07798332e5e86b5f75'
$gitAttributesSha256 = '47d4d45b69fa1ca09520b982dd316c7d75cc536dd98a91e7fdd42e60ccd78baf'
$aiEvalCaseManifestSha256 = '4055a65b1691623d6b86bf64125a1caf7ff8ae4001b494f02c1ed970e773c33b'
$playwrightSkipManifestSha256 = '954c055754f4b1eb7b72b1eb509f0e5a3966e4fa0d977691039d145a8f1e37f0'
$repositoryProjectRosterCount = 32
$repositoryProjectRosterSha256 = '3004147906f03eb7fac04fa1f8d7496eb14cebb241e20b383b3352e040017de2'
$solutionProjectRosterCount = 32
$solutionProjectRosterSha256 = '3004147906f03eb7fac04fa1f8d7496eb14cebb241e20b383b3352e040017de2'
$solutionFileSha256 = '265c2d6d0fc777413ad87234115368280402bed84f98c9a7b188911094e867f4'
$globalJsonSha256 = '18303059fe920620f05e25d0157b7ed4a74934841e6a34b0b86d713fbf631444'
$buildFileManifestCount = 2
$buildFileManifestSha256 = '3730f5d3693e5aa74a0d3fe1d2d64c4203cab26da11ba2dc4337b3f7bc4de11c'
$workflowManifestCount = 9
$workflowManifestSha256 = 'd1bac30f1c530aece06266fae02b82b0894651729a4d02fb5d9817cc22e5caf7'
$testProjectAssetSha256 = [ordered]@{
    'src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj' = '9ff22152feced2004aeecbb1d643eaec01880bb42254b34ad858d24af109c6de'
    'src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj' = '495adf4ae6a8c1a2985a26c6c55bd28bfa4c1578ea69120a0bd6ff6f4cec84f2'
    'src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj' = '03b8cab6a29fd5bf6ad75ca9e8ab00c87535d41386c2dcdca87f33268dbaead8'
    'src/tests/AICopilot.CloudAiReadLiveTests/AICopilot.CloudAiReadLiveTests.csproj' = 'cfcca58c70d4dc9bbcdbc509767d4151feb34342aeb1bb7770dfcfc5169a5aaf'
}
$supportAssetSha256 = [ordered]@{
    'src/tests/AICopilot.Testing.McpServer/AICopilot.Testing.McpServer.csproj' = '4d3957d806cf21b2b0bed2e29cbd410eed54b7c9b11891e09178f412b48d96ad'
    'src/tests/AICopilot.Testing.McpServer/Program.cs' = 'db8dde8cc27cbac4db3c61325dab6c46f7833c7664620950b56c97b9b4d94304'
}
$canonicalFrozenSourceHashPaths = @{
    'AICopilot.AiEvalTests' = @('src/tests/AICopilot.AiEvalTests/GoldenCaseTests.cs')
    'AICopilot.ArchitectureTests' = @(
        'src/tests/AICopilot.ArchitectureTests/ArchitectureBoundaryTests.cs',
        'src/tests/AICopilot.ArchitectureTests/DddAggregateBoundaryTests.cs'
    )
    'AICopilot.BackendTests' = @()
    'AICopilot.CloudAiReadLiveTests' = @('src/tests/AICopilot.CloudAiReadLiveTests/CloudAiReadLiveContractTests.cs')
}
$vitestUnitSourceCount = 31
$vitestUnitSourceManifestSha256 = '055f451c079ddb33fb6892afbfc4b3b60e9fb807349a51fa404044f32421c0e8'
$backendTestSourceCount = 120
$backendTestSourceManifestSha256 = 'c27af7e760699458acd3afb43c191650181956d414f0c96241fd0ccfc2ec7435'
$vitestPackageJsonSha256 = '6ca7e341d7a421d964af87e17e3358dec96cdf28852b3453fb317f98d6baf9a9'
$vitestPackageLockSha256 = '8136844dc863225fa750c8dd3a10eeec087135c7434e3303f59bfe1e1dd252c4'
$vitestConfigSha256 = 'e30c3cbfec8089345e5b4ea5950ce3edc975f033722c3532ee26b7e075e37571'
$playwrightSmokeSourceSha256 = 'f87a5e2bf68f302165b9be9f44487110fa9fdc2975b92bfb826d901fc0ae1a91'
$playwrightSmokeConfigSha256 = '6abb22587cfbbcee798b6c85df2b858b1acf953b847c08f3625ff0cb32380838'
$deploymentBehaviorSha256 = 'a157bad95903c01872e896961b8a69c127c9937a17e098a56c5eb09d4c2768f2'
$deploymentPolicySha256 = 'f873e20a8378ced4d0a19c3e9685078783eecfbeb3bba6042063051c575719fc'
$requiredWorkflowSha256 = @{
    '.github/workflows/aicopilot-ci.yml' = 'd4bd74ec5c3be8b245cdb7381170b01aa27cd3474ff4c396fe1c26b309edc44d'
}
$requiredWorkflowJobSha256 = @{
    '.github/workflows/aicopilot-ci.yml' = 'c00214f864f42e74d0246e02e8b3adc952e36ae7303f59c180e43440f107201e'
}
$allowedTestProjectTargetHashes = @{}

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path).Replace('\', '/')
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory)][string]$BasePath,
        [Parameter(Mandatory)][string]$Path
    )
    return [System.IO.Path]::GetRelativePath($BasePath, $Path).Replace('\', '/')
}

function Get-OptionalProperty {
    param(
        [AllowNull()][object]$InputObject,
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()][object]$DefaultValue = $null
    )

    if ($null -eq $InputObject) { return $DefaultValue }
    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name) -and $null -ne $InputObject[$Name]) {
            return $InputObject[$Name]
        }
        return $DefaultValue
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $DefaultValue }
    return $property.Value
}

function Get-XmlElementsByLocalName {
    param(
        [Parameter(Mandatory)][xml]$Xml,
        [Parameter(Mandatory)][string[]]$Names
    )

    return [object[]]@($Xml.SelectNodes('//*') | Where-Object { $_.LocalName -in $Names })
}

function Get-XmlAttributeValue {
    param(
        [Parameter(Mandatory)][System.Xml.XmlNode]$Node,
        [Parameter(Mandatory)][string]$Name
    )

    foreach ($attribute in @($Node.Attributes)) {
        if ($attribute.LocalName -ieq $Name) { return [string]$attribute.Value }
    }
    return ''
}

function Test-XmlElementHasAnyAttribute {
    param(
        [Parameter(Mandatory)][System.Xml.XmlNode]$Node,
        [Parameter(Mandatory)][string[]]$Names
    )

    foreach ($attribute in @($Node.Attributes)) {
        if ($attribute.LocalName -in $Names) { return $true }
    }
    return $false
}

function Add-PolicyError {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Message
    )
    $Errors.Add("$Code $Message")
}

function Assert-NoPolicyErrors {
    param([Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors)
    if ($Errors.Count -gt 0) {
        throw ("AICopilot test governance failed:`n- " + ($Errors -join "`n- "))
    }
}

function Test-RunnerConfigurationFile {
    param(
        [Parameter(Mandatory)][string]$ResolvedRunnerConfigPath,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory)][string]$Context
    )

    $resolvedPath = Get-NormalizedPath $ResolvedRunnerConfigPath
    $runnerDirectory = Split-Path $resolvedPath -Parent
    $runnerConfigs = @(if (Test-Path $runnerDirectory -PathType Container) {
        Get-ChildItem -Force $runnerDirectory -Filter '*xunit.runner.json' -File
    })
    if ($runnerConfigs.Count -ne 1 -or (Get-NormalizedPath $runnerConfigs[0].FullName) -ne $resolvedPath) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context must contain exactly one generic xunit.runner.json; assembly-specific runner overrides are forbidden."
        return
    }
    if (-not (Test-Path $resolvedPath -PathType Leaf)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context does not contain the required xunit.runner.json: $ResolvedRunnerConfigPath."
        return
    }
    if ((Get-FileHash $resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $xunitRunnerConfigSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context xunit.runner.json differs from the reviewed failSkips configuration."
        return
    }
    try {
        $runnerConfiguration = Get-Content $resolvedPath -Raw | ConvertFrom-Json
        if ([bool](Get-OptionalProperty $runnerConfiguration 'failSkips' $false) -ne $true) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context xunit.runner.json must set failSkips=true."
        }
    } catch {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context xunit.runner.json is not valid JSON: $($_.Exception.Message)"
    }
}

function Write-JsonAtomically {
    param(
        [Parameter(Mandatory)][object]$Value,
        [Parameter(Mandatory)][string]$Path
    )

    $directory = Split-Path $Path -Parent
    if (-not (Test-Path $directory)) {
        [void](New-Item $directory -ItemType Directory -Force)
    }
    $temporaryPath = Join-Path $directory ".$([System.IO.Path]::GetFileName($Path)).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = $Value | ConvertTo-Json -Depth 100
        [System.IO.File]::WriteAllText($temporaryPath, "$json`n", [System.Text.UTF8Encoding]::new($false))
        $null = Get-Content $temporaryPath -Raw | ConvertFrom-Json -Depth 100
        Move-Item $temporaryPath $Path -Force
    } finally {
        Remove-Item $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

function ConvertTo-Sha256 {
    param([Parameter(Mandatory)][string]$Value)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.Normalize([Text.NormalizationForm]::FormC))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-ActiveSdkMetadataLoadContextPath {
    $activeSdk = (& dotnet --version | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($activeSdk)) {
        throw "$ruleId-SCAN dotnet --version returned no SDK version."
    }

    $sdkDirectories = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(& dotnet --list-sdks)) {
        if ($line -match '^(?<version>\S+)\s+\[(?<root>.+)\]$' -and $Matches.version -eq $activeSdk) {
            $sdkDirectories.Add((Join-Path $Matches.root $Matches.version))
        }
    }
    foreach ($sdkDirectory in @($sdkDirectories | Select-Object -Last 1)) {
        $candidate = Join-Path $sdkDirectory 'System.Reflection.MetadataLoadContext.dll'
        if (Test-Path $candidate -PathType Leaf) {
            return (Get-NormalizedPath $candidate)
        }
    }
    throw "$ruleId-SCAN active SDK $activeSdk does not expose System.Reflection.MetadataLoadContext.dll."
}

function Get-MetadataResolverPaths {
    param(
        [Parameter(Mandatory)][string]$TestAssemblyPath,
        [string[]]$AdditionalReferencePaths = @()
    )

    $pathsBySimpleName = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $pathsBySimpleName[[System.IO.Path]::GetFileNameWithoutExtension($TestAssemblyPath)] = $TestAssemblyPath
    foreach ($referencePath in @($AdditionalReferencePaths)) {
        if ([string]::IsNullOrWhiteSpace($referencePath) -or -not (Test-Path $referencePath -PathType Leaf)) { continue }
        $simpleName = [System.IO.Path]::GetFileNameWithoutExtension($referencePath)
        if (-not $pathsBySimpleName.ContainsKey($simpleName)) {
            $pathsBySimpleName[$simpleName] = [System.IO.Path]::GetFullPath($referencePath)
        }
    }
    $assemblyDirectory = Split-Path $TestAssemblyPath -Parent
    foreach ($file in @(Get-ChildItem -Force $assemblyDirectory -Filter '*.dll' -File -Recurse | Sort-Object { $_.DirectoryName.Length }, FullName)) {
        if (-not $pathsBySimpleName.ContainsKey($file.BaseName)) {
            $pathsBySimpleName[$file.BaseName] = $file.FullName
        }
    }

    $runtimeDirectories = [System.Collections.Generic.List[object]]::new()
    foreach ($line in @(& dotnet --list-runtimes)) {
        if ($line -match '^(?<name>\S+)\s+(?<version>\S+)\s+\[(?<root>.+)\]$') {
            $candidate = Join-Path $Matches.root $Matches.version
            if (Test-Path $candidate -PathType Container) {
                $parsedVersion = $null
                if ([Version]::TryParse($Matches.version, [ref]$parsedVersion)) {
                    $runtimeDirectories.Add([pscustomobject]@{ Path = $candidate; Version = $parsedVersion })
                }
            }
        }
    }
    foreach ($runtime in @($runtimeDirectories | Sort-Object Version -Descending)) {
        foreach ($file in @(Get-ChildItem -Force $runtime.Path -Filter '*.dll' -File | Sort-Object Name)) {
            if (-not $pathsBySimpleName.ContainsKey($file.BaseName)) {
                $pathsBySimpleName[$file.BaseName] = $file.FullName
            }
        }
    }

    return [string[]]@($pathsBySimpleName.Values)
}

function Test-TypeDerivesFrom {
    param(
        [AllowNull()][object]$Type,
        [Parameter(Mandatory)][string]$FullName
    )

    $current = $Type
    while ($null -ne $current) {
        if ($current.FullName -eq $FullName) { return $true }
        $current = $current.BaseType
    }
    return $false
}

function Test-TypeExecutesDeclaration {
    param(
        [Parameter(Mandatory)][object]$CandidateType,
        [Parameter(Mandatory)][object]$DeclaringType
    )

    if ($CandidateType.IsAbstract) { return $false }
    if (-not $DeclaringType.IsGenericTypeDefinition) {
        return $DeclaringType.IsAssignableFrom($CandidateType)
    }

    $current = $CandidateType
    while ($null -ne $current) {
        if ($current.IsGenericType -and $current.GetGenericTypeDefinition().FullName -eq $DeclaringType.FullName) {
            return $true
        }
        $current = $current.BaseType
    }
    return $false
}

function Get-TestAttributeCategory {
    param([Parameter(Mandatory)][object]$AttributeType)

    if (Test-TypeDerivesFrom -Type $AttributeType -FullName 'Xunit.TheoryAttribute') { return 'Theory' }
    if (Test-TypeDerivesFrom -Type $AttributeType -FullName 'Xunit.FactAttribute') { return 'Fact' }
    return $null
}

function Add-TraitsFromAttributes {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Attributes,
        [Parameter(Mandatory)][System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[string]]]$Traits
    )

    foreach ($attribute in $Attributes) {
        if ($attribute.AttributeType.FullName -ne 'Xunit.TraitAttribute' -or $attribute.ConstructorArguments.Count -lt 2) {
            continue
        }
        $name = [string]$attribute.ConstructorArguments[0].Value
        $value = [string]$attribute.ConstructorArguments[1].Value
        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($value)) { continue }
        if (-not $Traits.ContainsKey($name)) {
            $Traits[$name] = [System.Collections.Generic.List[string]]::new()
        }
        if (-not $Traits[$name].Contains($value)) {
            $Traits[$name].Add($value)
        }
    }
}

function Get-TestTraits {
    param(
        [Parameter(Mandatory)][object]$ExecutionType,
        [Parameter(Mandatory)][object]$Method
    )

    $traits = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[string]]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $currentType = $ExecutionType
    while ($null -ne $currentType -and $currentType.FullName -ne 'System.Object') {
        Add-TraitsFromAttributes -Attributes @([System.Reflection.CustomAttributeData]::GetCustomAttributes($currentType)) -Traits $traits
        $currentType = $currentType.BaseType
    }
    Add-TraitsFromAttributes -Attributes @([System.Reflection.CustomAttributeData]::GetCustomAttributes($Method)) -Traits $traits

    $ordered = [ordered]@{}
    foreach ($name in @($traits.Keys | Sort-Object)) {
        $ordered[$name] = [string[]]@($traits[$name] | Sort-Object -Unique)
    }
    return [pscustomobject]$ordered
}

function Get-MethodParameterSignature {
    param([Parameter(Mandatory)][object]$Method)
    return (@($Method.GetParameters() | ForEach-Object { $_.ParameterType.ToString() }) -join ',')
}

function Get-TestAttributePolicy {
    param([Parameter(Mandatory)][object]$Attribute)

    $values = [ordered]@{
        Skip = ''
        Explicit = $false
        SkipWhen = ''
        SkipUnless = ''
        SkipType = ''
        SkipExceptions = ''
        Timeout = 0
    }
    foreach ($argument in @($Attribute.NamedArguments)) {
        if (-not $values.Contains($argument.MemberName)) { continue }
        $value = $argument.TypedValue.Value
        $values[$argument.MemberName] = if ($null -eq $value) { '' } else { [string]$value }
    }

    $isDisabled = -not [string]::IsNullOrWhiteSpace([string]$values.Skip) -or
        [string]$values.Explicit -eq 'True' -or
        -not [string]::IsNullOrWhiteSpace([string]$values.SkipWhen) -or
        -not [string]::IsNullOrWhiteSpace([string]$values.SkipUnless) -or
        -not [string]::IsNullOrWhiteSpace([string]$values.SkipExceptions)
    $signature = @($values.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '|'
    return [pscustomobject][ordered]@{
        signature = $signature
        isDisabled = $isDisabled
        skip = [string]$values.Skip
        explicit = [string]$values.Explicit -eq 'True'
        skipWhen = [string]$values.SkipWhen
        skipUnless = [string]$values.SkipUnless
        skipType = [string]$values.SkipType
        skipExceptions = [string]$values.SkipExceptions
        timeout = [int]$values.Timeout
    }
}

function ConvertTo-AttributeTypedValueSignature {
    param([Parameter(Mandatory)][object]$Argument)

    $argumentType = [string]$Argument.ArgumentType.FullName
    $value = $Argument.Value
    if ($null -eq $value) { return "$argumentType=<null>" }
    if ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) {
        $items = @($value | ForEach-Object { ConvertTo-AttributeTypedValueSignature -Argument $_ })
        return "$argumentType=[$($items -join ',')]"
    }
    $valueText = if ($value -is [Type]) { [string]$value.FullName } else { [string]$value }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($valueText.Normalize([Text.NormalizationForm]::FormC)))
    return "$argumentType=$encoded"
}

function Get-CustomAttributeSignature {
    param([Parameter(Mandatory)][object]$Attribute)

    $constructor = @($Attribute.ConstructorArguments | ForEach-Object { ConvertTo-AttributeTypedValueSignature -Argument $_ }) -join ';'
    $named = @($Attribute.NamedArguments | Sort-Object MemberName | ForEach-Object {
        "$($_.MemberName):$(ConvertTo-AttributeTypedValueSignature -Argument $_.TypedValue)"
    }) -join ';'
    return "$($Attribute.AttributeType.FullName)|ctor=$constructor|named=$named"
}

function Get-TestAssemblySnapshot {
    param(
        [Parameter(Mandatory)][string]$ResolvedProjectPath,
        [Parameter(Mandatory)][string]$ResolvedProjectName,
        [Parameter(Mandatory)][string]$ResolvedAssemblyPath,
        [string[]]$AdditionalReferencePaths = @()
    )

    if (-not (Test-Path $ResolvedAssemblyPath -PathType Leaf)) {
        throw "$ruleId-SCAN test assembly does not exist: $ResolvedAssemblyPath"
    }

    $metadataLoadContextPath = Get-ActiveSdkMetadataLoadContextPath
    if ($null -eq ('System.Reflection.MetadataLoadContext' -as [type])) {
        Add-Type -Path $metadataLoadContextPath
    }
    [string[]]$resolverPaths = @(Get-MetadataResolverPaths -TestAssemblyPath $ResolvedAssemblyPath -AdditionalReferencePaths $AdditionalReferencePaths)
    $resolver = [System.Reflection.PathAssemblyResolver]::new($resolverPaths)
    $context = [System.Reflection.MetadataLoadContext]::new($resolver)
    $tests = [System.Collections.Generic.List[object]]::new()
    $seenIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    try {
        $assembly = $context.LoadFromAssemblyPath($ResolvedAssemblyPath)
        $relativeProjectPath = Get-RelativePath -BasePath $RepositoryRoot -Path $ResolvedProjectPath
        $allTypes = @($assembly.GetTypes() | Sort-Object FullName)
        foreach ($declaringType in $allTypes) {
            $bindingFlags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
            foreach ($method in @($declaringType.GetMethods($bindingFlags) | Sort-Object Name, MetadataToken)) {
                if ($method.IsSpecialName) { continue }
                $methodAttributes = @([System.Reflection.CustomAttributeData]::GetCustomAttributes($method))
                $testAttributes = @($methodAttributes | Where-Object { $null -ne (Get-TestAttributeCategory -AttributeType $_.AttributeType) })
                if ($testAttributes.Count -eq 0) { continue }
                if ($testAttributes.Count -ne 1) {
                    throw "$ruleId-SCAN $($declaringType.FullName).$($method.Name) has $($testAttributes.Count) Fact/Theory-derived attributes."
                }

                $testAttribute = $testAttributes[0]
                $attributeCategory = Get-TestAttributeCategory -AttributeType $testAttribute.AttributeType
                $parameterSignature = Get-MethodParameterSignature -Method $method
                $declaringTypeName = [string]$method.DeclaringType.FullName
                $testAttributeType = [string]$testAttribute.AttributeType.FullName
                if ($testAttributeType -notin $allowedTestAttributeTypes) {
                    throw "$ruleId-SCAN unregistered Fact/Theory-derived attribute '$testAttributeType' on $declaringTypeName.$($method.Name)."
                }
                $genericArity = @($method.GetGenericArguments()).Count
                $symbol = "$declaringTypeName.$($method.Name)($parameterSignature)"
                $logicalKey = "aicopilot-test-decl-v1|$declaringTypeName|$($method.Name)``$genericArity|$parameterSignature"
                $logicalId = "aicopilot-test-decl-v1:$(ConvertTo-Sha256 $logicalKey)"
                $physicalKey = "aicopilot-test-physical-v1|$relativeProjectPath|$logicalId"
                $id = "aicopilot-test-physical-v1:$(ConvertTo-Sha256 $physicalKey)"
                if (-not $seenIds.Add($id)) {
                    throw "$ruleId-SCAN duplicate test identity '$id' in $ResolvedAssemblyPath."
                }

                $inlineDataAttributes = @($methodAttributes | Where-Object { $_.AttributeType.FullName -eq 'Xunit.InlineDataAttribute' })
                $inlineDataRows = $inlineDataAttributes.Count
                $inlineDataSignatures = @($inlineDataAttributes | ForEach-Object { Get-CustomAttributeSignature -Attribute $_ } | Sort-Object)
                $dynamicDataSources = @($methodAttributes |
                    Where-Object { $_.AttributeType.FullName -in @('Xunit.MemberDataAttribute', 'Xunit.ClassDataAttribute') } |
                    ForEach-Object { Get-CustomAttributeSignature -Attribute $_ } |
                    Sort-Object -Unique)
                $rowProjection = if ($attributeCategory -eq 'Theory' -and $inlineDataRows -gt 0) { $inlineDataRows } else { 1 }
                $executionTypes = [System.Collections.Generic.List[object]]::new()
                $executionCandidates = if ($method.IsStatic) {
                    @($declaringType)
                } else {
                    @($allTypes | Where-Object { Test-TypeExecutesDeclaration -CandidateType $_ -DeclaringType $declaringType })
                }
                foreach ($executionType in $executionCandidates) {
                    $executionTypeName = [string]$executionType.FullName
                    $executionKey = "aicopilot-test-execution-v1|$relativeProjectPath|$executionTypeName|$logicalId"
                    $executionTypes.Add([pscustomobject][ordered]@{
                        id = "aicopilot-test-execution-v1:$(ConvertTo-Sha256 $executionKey)"
                        name = $executionTypeName
                        traits = Get-TestTraits -ExecutionType $executionType -Method $method
                    })
                }
                if ($executionTypes.Count -eq 0) {
                    throw "$ruleId-SCAN $symbol has no concrete execution type and would not be discovered by the required runner."
                }
                $attributePolicy = Get-TestAttributePolicy -Attribute $testAttribute
                $dataAttributePolicies = @($methodAttributes |
                    Where-Object { $_.AttributeType.FullName -in @('Xunit.InlineDataAttribute', 'Xunit.MemberDataAttribute', 'Xunit.ClassDataAttribute') } |
                    ForEach-Object { Get-TestAttributePolicy -Attribute $_ })
                $dataPolicySignature = @($dataAttributePolicies | ForEach-Object { $_.signature } | Sort-Object) -join '||'
                $attributePolicy.signature = "$($attributePolicy.signature)|DataPolicies=$dataPolicySignature"
                $attributePolicy.isDisabled = [bool]$attributePolicy.isDisabled -or @($dataAttributePolicies | Where-Object { $_.isDisabled }).Count -gt 0

                $tests.Add([pscustomobject][ordered]@{
                    id = $id
                    logicalId = $logicalId
                    symbol = $symbol
                    executionType = $declaringTypeName
                    declaringType = $declaringTypeName
                    methodName = [string]$method.Name
                    parameterSignature = $parameterSignature
                    attributeCategory = $attributeCategory
                    testAttributeType = $testAttributeType
                    testAttributePolicy = $attributePolicy
                    inlineDataRows = $inlineDataRows
                    inlineDataSignatures = [string[]]$inlineDataSignatures
                    dynamicDataSources = [string[]]$dynamicDataSources
                    executionTypes = [object[]]@($executionTypes | Sort-Object name)
                    projectedCases = $rowProjection * $executionTypes.Count
                    traits = Get-TestTraits -ExecutionType $declaringType -Method $method
                })
            }
        }
    } finally {
        $context.Dispose()
    }

    return [pscustomobject][ordered]@{
        projectPath = Get-RelativePath -BasePath $RepositoryRoot -Path $ResolvedProjectPath
        projectName = $ResolvedProjectName
        assemblyPath = Get-RelativePath -BasePath $RepositoryRoot -Path $ResolvedAssemblyPath
        assemblySha256 = (Get-FileHash $ResolvedAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        declarations = $tests.Count
        executionTemplates = [int](($tests | ForEach-Object { @($_.executionTypes).Count } | Measure-Object -Sum).Sum)
        projectedCases = [int](($tests | Measure-Object -Property projectedCases -Sum).Sum)
        tests = [object[]]@($tests | Sort-Object id)
    }
}

function Get-TestProjectSpecifications {
    param(
        [Parameter(Mandatory)][string]$RequestedConfiguration,
        [switch]$AllowMissingAssembly
    )

    $specifications = [System.Collections.Generic.List[object]]::new()
    $testRoot = Join-Path $RepositoryRoot 'src/tests'
    foreach ($projectFile in @(Get-ChildItem -Force $testRoot -Recurse -Filter '*.csproj' -File | Sort-Object FullName)) {
        [xml]$projectXml = Get-Content $projectFile.FullName -Raw
        $directIsTestProjectNodes = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('IsTestProject') | Where-Object {
            $_.ParentNode.LocalName -ieq 'PropertyGroup' -and
            $_.ParentNode.ParentNode.LocalName -ieq 'Project' -and
            [string]$_.InnerText -match '(?i)^\s*true\s*$'
        })
        $isTestProject = $directIsTestProjectNodes.Count -gt 0
        if (-not $isTestProject) { continue }
        $projectNameValue = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
        $assemblyNameValue = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('AssemblyName') | Where-Object {
            $_.ParentNode.LocalName -ieq 'PropertyGroup' -and $_.ParentNode.ParentNode.LocalName -ieq 'Project'
        } | ForEach-Object { $_.InnerText } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Last 1)
        if ($assemblyNameValue.Count -eq 0) { $assemblyNameValue = @($projectNameValue) }
        $targetFramework = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('TargetFramework') | Where-Object {
            $_.ParentNode.LocalName -ieq 'PropertyGroup' -and $_.ParentNode.ParentNode.LocalName -ieq 'Project'
        } | ForEach-Object { $_.InnerText } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Last 1)
        if ($targetFramework.Count -ne 1) {
            throw "$ruleId-BASELINE cannot resolve one TargetFramework from $($projectFile.FullName)."
        }
        $resolvedAssembly = Join-Path $projectFile.Directory.FullName "bin/$RequestedConfiguration/$($targetFramework[0])/$($assemblyNameValue[0]).dll"
        if (-not $AllowMissingAssembly -and -not (Test-Path $resolvedAssembly -PathType Leaf)) {
            throw "$ruleId-SCAN build $($projectFile.FullName) for $RequestedConfiguration before running this mode. Missing: $resolvedAssembly"
        }
        $specifications.Add([pscustomobject]@{
            ProjectPath = Get-NormalizedPath $projectFile.FullName
            ProjectName = $projectNameValue
            AssemblyPath = Get-NormalizedPath $resolvedAssembly
            RunnerConfigPath = Get-NormalizedPath (Join-Path (Split-Path $resolvedAssembly -Parent) 'xunit.runner.json')
        })
    }
    return [object[]]@($specifications)
}

function Get-ProjectSourceFiles {
    param([Parameter(Mandatory)][string]$ResolvedProjectPath)

    $projectDirectory = Split-Path $ResolvedProjectPath -Parent
    return [string[]]@(Get-ChildItem -Force $projectDirectory -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' } |
        ForEach-Object { Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName } |
        Sort-Object -Unique)
}

function Get-CanonicalRequiredCommandPrefixes {
    return [string[]]@(
        './scripts/tests/TestAICopilotTestGovernanceBehavior.ps1',
        './scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateStatic',
        './deploy/enterprise-ai/tests/TestDeploymentPolicy.ps1',
        'docker info',
        'dotnet build AICopilot.slnx -c Release',
        './scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateRepository',
        './scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateDiscovery',
        'dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj -c Release --no-build',
        'dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj -c Release --no-build',
        'dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj -c Release --no-build',
        'bash deploy/enterprise-ai/tests/deployment-behavior.sh',
        'npm run test:unit',
        'npm run build'
    )
}

function Get-CanonicalWorkflowRunSteps {
    return [object[]]@(
        [pscustomobject]@{
            Name = 'Run AICopilot test governance self-tests'
            Run = "./scripts/tests/TestAICopilotTestGovernanceBehavior.ps1`n./scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release"
        },
        [pscustomobject]@{
            Name = 'Build AICopilot solution'
            Run = 'dotnet build AICopilot.slnx -c Release --no-restore'
        },
        [pscustomobject]@{
            Name = 'Validate AICopilot test repository and discovery'
            Run = "./scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateRepository -Configuration Release`n./scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateDiscovery -Configuration Release"
        }
    )
}

function Get-WorkflowRunSteps {
    param([Parameter(Mandatory)][string]$WorkflowContent)

    $lines = [regex]::Split($WorkflowContent.Replace("`r`n", "`n"), "`n")
    $steps = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $nameMatch = [regex]::Match($lines[$index], '^(?<indent>\s*)-\s+name:\s*(?<name>.+?)\s*$')
        if (-not $nameMatch.Success) { continue }
        $stepIndent = $nameMatch.Groups['indent'].Value.Length
        $end = $index + 1
        while ($end -lt $lines.Count) {
            $nextStep = [regex]::Match($lines[$end], '^(?<indent>\s*)-\s+')
            if ($nextStep.Success -and $nextStep.Groups['indent'].Value.Length -eq $stepIndent) { break }
            $end++
        }

        $run = $null
        $shell = $null
        $hasIf = $false
        $ambiguous = $false
        $seenDirectKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $unexpectedKeys = [System.Collections.Generic.List[string]]::new()
        for ($cursor = $index + 1; $cursor -lt $end; $cursor++) {
            $propertyMatch = [regex]::Match($lines[$cursor], '^(?<indent>\s*)(?<name>[A-Za-z0-9_-]+):\s*(?<value>.*)$')
            if (-not $propertyMatch.Success -or $propertyMatch.Groups['indent'].Value.Length -ne ($stepIndent + 2)) { continue }
            $directKey = $propertyMatch.Groups['name'].Value
            if (-not $seenDirectKeys.Add($directKey)) {
                $ambiguous = $true
                continue
            }
            if ($directKey -notin @('run', 'shell')) {
                $unexpectedKeys.Add($directKey)
            }
            switch ($directKey) {
                'shell' { $shell = $propertyMatch.Groups['value'].Value.Trim() }
                'if' { $hasIf = $true }
                'run' {
                    $value = $propertyMatch.Groups['value'].Value.Trim()
                    if ($value -ne '|') {
                        $run = $value
                        continue
                    }
                    $runIndent = $propertyMatch.Groups['indent'].Value.Length
                    $blockLines = [System.Collections.Generic.List[string]]::new()
                    $blockCursor = $cursor + 1
                    while ($blockCursor -lt $end) {
                        $line = $lines[$blockCursor]
                        $leading = [regex]::Match($line, '^\s*').Value.Length
                        if (-not [string]::IsNullOrWhiteSpace($line) -and $leading -le $runIndent) { break }
                        $blockLines.Add($line)
                        $blockCursor++
                    }
                    while ($blockLines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($blockLines[$blockLines.Count - 1])) {
                        $blockLines.RemoveAt($blockLines.Count - 1)
                    }
                    $contentIndent = @($blockLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { [regex]::Match($_, '^\s*').Value.Length } | Measure-Object -Minimum).Minimum
                    $run = (@($blockLines | ForEach-Object {
                        if ([string]::IsNullOrWhiteSpace($_)) { return '' }
                        return $_.Substring([int]$contentIndent).TrimEnd()
                    }) -join "`n")
                }
            }
        }
        $name = $nameMatch.Groups['name'].Value.Trim().Trim('"', "'")
        $steps.Add([pscustomobject]@{ Name = $name; Run = $run; Shell = $shell; HasIf = $hasIf; Ambiguous = $ambiguous; UnexpectedKeys = [string[]]$unexpectedKeys })
        $index = $end - 1
    }
    return [object[]]@($steps)
}

function Get-WorkflowJobEnvelope {
    param(
        [Parameter(Mandatory)][string]$WorkflowContent,
        [Parameter(Mandatory)][string]$JobName
    )

    $lines = [regex]::Split($WorkflowContent.Replace("`r`n", "`n"), "`n")
    $matches = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $jobMatch = [regex]::Match($lines[$index], '^(?<indent>\s*)' + [regex]::Escape($JobName) + ':\s*$')
        if (-not $jobMatch.Success -or $jobMatch.Groups['indent'].Value.Length -ne 2) { continue }
        $jobIndent = 2
        $end = $index + 1
        while ($end -lt $lines.Count) {
            $nextJob = [regex]::Match($lines[$end], '^(?<indent>\s*)[A-Za-z0-9_-]+:\s*$')
            if ($nextJob.Success -and $nextJob.Groups['indent'].Value.Length -eq $jobIndent) { break }
            $end++
        }
        $timeoutValues = [System.Collections.Generic.List[string]]::new()
        $runsOnValues = [System.Collections.Generic.List[string]]::new()
        $directKeys = [System.Collections.Generic.List[string]]::new()
        $seenDirectKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $ambiguous = $false
        $hasIf = $false
        for ($cursor = $index + 1; $cursor -lt $end; $cursor++) {
            $propertyMatch = [regex]::Match($lines[$cursor], '^(?<indent>\s*)(?<name>[A-Za-z0-9_-]+):\s*(?<value>.*)$')
            if (-not $propertyMatch.Success -or $propertyMatch.Groups['indent'].Value.Length -ne ($jobIndent + 2)) { continue }
            $directKey = $propertyMatch.Groups['name'].Value
            $directKeys.Add($directKey)
            if (-not $seenDirectKeys.Add($directKey)) { $ambiguous = $true }
            if ($directKey -eq 'timeout-minutes') {
                $timeoutValues.Add($propertyMatch.Groups['value'].Value.Trim())
            } elseif ($directKey -eq 'runs-on') {
                $runsOnValues.Add($propertyMatch.Groups['value'].Value.Trim())
            } elseif ($directKey -eq 'if') {
                $hasIf = $true
            }
        }
        $unexpectedKeys = @($directKeys | Where-Object { $_ -notin @('runs-on', 'timeout-minutes', 'steps') } | Sort-Object -Unique)
        $jobContent = @($lines[$index..($end - 1)]) -join "`n"
        $matches.Add([pscustomobject]@{
            TimeoutValues = [string[]]$timeoutValues
            RunsOnValues = [string[]]$runsOnValues
            HasIf = $hasIf
            Ambiguous = $ambiguous
            UnexpectedKeys = [string[]]$unexpectedKeys
            Content = $jobContent
        })
    }
    return [object[]]@($matches)
}

function Get-CanonicalWorkflowStepNames {
    param([Parameter(Mandatory)][string]$WorkflowPath)

    return [string[]]@(
        'Checkout',
        'Run AICopilot test governance self-tests',
        'Setup .NET',
        'Setup Node',
        'Verify .NET SDK',
        'Restore AICopilot solution',
        'Restore web dependencies',
        'Enforce incremental deployment policy',
        'Require Linux Docker runtime',
        'Build AICopilot solution',
        'Validate AICopilot test repository and discovery',
        'Run architecture tests',
        'Run deterministic AI eval tests',
        'Run backend tests',
        'Run deployment behavior tests',
        'Test and build web app',
        'Reconcile required test results',
        'Upload required test evidence'
    )
}

function Get-WorkflowStepNames {
    param([Parameter(Mandatory)][string]$JobContent)

    $lines = [regex]::Split($JobContent.Replace("`r`n", "`n"), "`n")
    $stepsMarkers = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        $stepMatch = [regex]::Match($line, '^\s{6}-\s+(?<body>.+)$')
        if (-not $stepMatch.Success) { continue }
        $nameMatch = [regex]::Match($stepMatch.Groups['body'].Value, '^name:\s*(?<name>.+?)\s*$')
        if ($nameMatch.Success) {
            $stepsMarkers.Add($nameMatch.Groups['name'].Value.Trim().Trim('"', "'"))
        } else {
            $stepsMarkers.Add('<unnamed>')
        }
    }
    return [string[]]$stepsMarkers
}

function Get-GeneratedProjectPolicy {
    param(
        [Parameter(Mandatory)][string]$GeneratedProjectName,
        [string]$GeneratedProjectPath
    )

    $freezeMode = 'None'
    $frozenTypePatterns = @()
    $allowedNewTestKinds = @()
    $allowedNewRuntimes = @()
    $forbiddenNewTestKinds = @()
    $discoveryCeilings = @()
    $frozenSourceFiles = @()
    $frozenSourceHashes = [ordered]@{}
    $protectBaselineRemovals = $true

    if ($GeneratedProjectName -in @(
        'AICopilot.ArchitectureTests',
        'AICopilot.BackendTests',
        'AICopilot.AiEvalTests',
        'AICopilot.CloudAiReadLiveTests')) {
        # Phase 0 freezes every legacy bucket. New tests must be created in the
        # typed target projects introduced by later AI-TEST batches, never by
        # adding more declarations to these mixed projects.
        $freezeMode = 'All'
        if (-not [string]::IsNullOrWhiteSpace($GeneratedProjectPath)) {
            $frozenSourceFiles = Get-ProjectSourceFiles -ResolvedProjectPath $GeneratedProjectPath
            if ($GeneratedProjectName -in @('AICopilot.ArchitectureTests', 'AICopilot.AiEvalTests', 'AICopilot.CloudAiReadLiveTests')) {
                foreach ($relativeSource in $frozenSourceFiles) {
                    $frozenSourceHashes[$relativeSource] = (Get-FileHash (Join-Path $RepositoryRoot $relativeSource) -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            }
        }
    } else {
        throw "$ruleId-ROUTE unreviewed test project '$GeneratedProjectName'."
    }

    return [pscustomobject][ordered]@{
        isLegacy = $true
        freezeMode = $freezeMode
        frozenTypePatterns = [string[]]$frozenTypePatterns
        allowedNewTestKinds = [string[]]$allowedNewTestKinds
        allowedNewRuntimes = [string[]]$allowedNewRuntimes
        forbiddenNewTestKinds = [string[]]$forbiddenNewTestKinds
        discoveryCeilings = [object[]]$discoveryCeilings
        frozenSourceFiles = [string[]]$frozenSourceFiles
        frozenSourceHashes = [pscustomobject]$frozenSourceHashes
        protectBaselineRemovals = $protectBaselineRemovals
    }
}

function Get-TraitValues {
    param(
        [AllowNull()][object]$Traits,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Traits) { return @() }
    if ($Traits -is [System.Collections.IDictionary]) {
        if (-not $Traits.Contains($Name)) { return @() }
        return [string[]]@($Traits[$Name] | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    }
    $property = $Traits.PSObject.Properties[$Name]
    if ($null -eq $property) { return @() }
    return [string[]]@($property.Value | ForEach-Object { [string]$_ } | Sort-Object -Unique)
}

function Get-TraitMapSignature {
    param([AllowNull()][object]$Traits)

    if ($null -eq $Traits) { return '' }
    $names = if ($Traits -is [System.Collections.IDictionary]) {
        @($Traits.Keys | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    } else {
        @($Traits.PSObject.Properties | ForEach-Object { [string]$_.Name } | Sort-Object -Unique)
    }
    return (@($names | ForEach-Object {
        $name = $_
        $values = @(Get-TraitValues -Traits $Traits -Name $name)
        "$name=$($values -join ',')"
    }) -join ';')
}

function Get-TestTraitSignature {
    param([Parameter(Mandatory)][object]$Test)

    $executionTraits = @((Get-OptionalProperty $Test 'executionTypes' @()) |
        Sort-Object name |
        ForEach-Object {
            $executionType = $_
            $executionTraitSignature = Get-TraitMapSignature -Traits $executionType.traits
            "$([string]$executionType.name):$executionTraitSignature"
        }) -join '||'
    return "Declaration=$(Get-TraitMapSignature -Traits $Test.traits)|Executions=$executionTraits"
}

function Test-NewTestMetadata {
    param(
        [Parameter(Mandatory)][object]$Test,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory)][string]$Location
    )

    if ([string]$Test.testAttributeType -notin $allowedTestAttributeTypes) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message "$Location uses unregistered test attribute '$($Test.testAttributeType)'."
    }
    if (@(Get-OptionalProperty $Test 'dynamicDataSources' @()).Count -gt 0) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Location uses dynamic MemberData/ClassData; Phase 0 requires deterministic InlineData projection."
    }

    $singleValueTraits = @('TestKind', 'Risk', 'Owner')
    $multiValueTraits = @('Capability', 'Runtime')
    foreach ($name in $singleValueTraits) {
        $values = @(Get-TraitValues -Traits $Test.traits -Name $name)
        if ($values.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location requires exactly one $name trait; found $($values.Count)."
        }
    }
    foreach ($name in $multiValueTraits) {
        $values = @(Get-TraitValues -Traits $Test.traits -Name $name)
        if ($values.Count -lt 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location requires at least one $name trait."
        }
    }

    $testKind = @(Get-TraitValues -Traits $Test.traits -Name 'TestKind')
    $runtime = @(Get-TraitValues -Traits $Test.traits -Name 'Runtime')
    $risk = @(Get-TraitValues -Traits $Test.traits -Name 'Risk')
    if ([bool](Get-OptionalProperty (Get-OptionalProperty $Test 'testAttributePolicy' $null) 'isDisabled' $false)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Location is Skip/Explicit/conditional-skip and cannot enter a required AICopilot test lane."
    }
    if ($testKind.Count -eq 1 -and $testKind[0] -notin @($Baseline.allowedMetadata.testKinds)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unsupported TestKind '$($testKind[0])'."
    }
    if ($testKind.Count -eq 1 -and $testKind[0] -match '^(?:Regression|NonUi|General|Misc|Phase.*|Batch.*)$') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location uses forbidden legacy TestKind '$($testKind[0])'."
    }
    foreach ($value in $runtime) {
        if ($value -notin @($Baseline.allowedMetadata.runtimes)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unsupported Runtime '$value'."
        }
    }
    if ($risk.Count -eq 1 -and $risk[0] -notin @($Baseline.allowedMetadata.risks)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unsupported Risk '$($risk[0])'."
    }
    foreach ($value in @(Get-TraitValues -Traits $Test.traits -Name 'Capability')) {
        if ($value -notin $allowedCapabilities) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unregistered Capability '$value'."
        }
    }
    foreach ($value in @(Get-TraitValues -Traits $Test.traits -Name 'Owner')) {
        if ($value -notin $allowedOwners) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unregistered Owner '$value'."
        }
    }
}

function Test-IsFrozenTest {
    param(
        [Parameter(Mandatory)][object]$Project,
        [Parameter(Mandatory)][object]$Test
    )

    if ([string]$Project.freezeMode -eq 'All') { return $true }
    if ([string]$Project.freezeMode -ne 'Types') { return $false }
    foreach ($pattern in @($Project.frozenTypePatterns)) {
        if ([string]$Test.executionType -like $pattern -or [string]$Test.declaringType -like $pattern) {
            return $true
        }
    }
    return $false
}

function Get-ProjectedCasesPerExecution {
    param([Parameter(Mandatory)][object]$Test)

    $executionCount = @($Test.executionTypes).Count
    if ($executionCount -le 0) { return 0 }
    return [int]([int]$Test.projectedCases / $executionCount)
}

function Get-ProjectedCaseDecreaseDeltaTest {
    param(
        [Parameter(Mandatory)][object]$BaselineTest,
        [Parameter(Mandatory)][object]$CurrentTest
    )

    $baselinePerExecution = Get-ProjectedCasesPerExecution -Test $BaselineTest
    $currentPerExecution = Get-ProjectedCasesPerExecution -Test $CurrentTest
    if ($currentPerExecution -ge $baselinePerExecution) { return $null }

    $removedInlineDataSignatures = @($BaselineTest.inlineDataSignatures | Where-Object { $_ -notin @($CurrentTest.inlineDataSignatures) } | Sort-Object -Unique)
    if ($removedInlineDataSignatures.Count -gt 0) { return $null }
    $identityMaterial = @(
        [string]$BaselineTest.id,
        [string]$baselinePerExecution,
        [string]$currentPerExecution,
        ($removedInlineDataSignatures -join '|')
    ) -join '|'
    $deltaTest = $BaselineTest.PSObject.Copy()
    $deltaTest.id = "aicopilot-test-case-decrease-v1:$(ConvertTo-Sha256 $identityMaterial)"
    $deltaTest | Add-Member -NotePropertyName declarationId -NotePropertyValue ([string]$BaselineTest.id) -Force
    $deltaTest | Add-Member -NotePropertyName baselineCasesPerExecution -NotePropertyValue $baselinePerExecution -Force
    $deltaTest | Add-Member -NotePropertyName currentCasesPerExecution -NotePropertyValue $currentPerExecution -Force
    $deltaTest | Add-Member -NotePropertyName projectedCasesLostPerExecution -NotePropertyValue ($baselinePerExecution - $currentPerExecution) -Force
    $deltaTest | Add-Member -NotePropertyName projectedCasesLost -NotePropertyValue (($baselinePerExecution - $currentPerExecution) * @($CurrentTest.executionTypes).Count) -Force
    $deltaTest | Add-Member -NotePropertyName removedInlineDataSignatures -NotePropertyValue ([string[]]$removedInlineDataSignatures) -Force
    return $deltaTest
}

function Get-InlineDataRemovalDeltaTests {
    param(
        [Parameter(Mandatory)][object]$BaselineTest,
        [Parameter(Mandatory)][object]$CurrentTest
    )

    $executionCount = @($CurrentTest.executionTypes).Count
    foreach ($signature in @($BaselineTest.inlineDataSignatures | Where-Object { $_ -notin @($CurrentTest.inlineDataSignatures) } | Sort-Object -Unique)) {
        $deltaTest = $BaselineTest.PSObject.Copy()
        $deltaTest.id = "aicopilot-test-inline-removal-v1:$(ConvertTo-Sha256 "$($BaselineTest.id)|$signature")"
        $deltaTest | Add-Member -NotePropertyName declarationId -NotePropertyValue ([string]$BaselineTest.id) -Force
        $deltaTest | Add-Member -NotePropertyName removedInlineDataSignature -NotePropertyValue ([string]$signature) -Force
        $deltaTest | Add-Member -NotePropertyName projectedCasesLost -NotePropertyValue $executionCount -Force
        Write-Output $deltaTest
    }
}

function Test-WaiverManifest {
    param(
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    if ([string]$WaiverManifest.schemaVersion -ne $waiverSchemaVersion) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "unsupported waiver schemaVersion '$($WaiverManifest.schemaVersion)'."
    }
    if ([string]$WaiverManifest.ruleId -ne $ruleId) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver manifest ruleId must be $ruleId."
    }

    $seenIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $seenRegressionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $today = [DateOnly]::FromDateTime([DateTime]::UtcNow)
    $baselineProjects = @($Baseline.projects | ForEach-Object { [string]$_.projectPath })
    foreach ($waiver in @($WaiverManifest.waivers)) {
        $required = @('id', 'projectPath', 'symbol', 'changeKind', 'regressionId', 'targetProject', 'testKind', 'owner', 'reason', 'approvedBy', 'expiresOn')
        foreach ($name in $required) {
            if ([string]::IsNullOrWhiteSpace([string](Get-OptionalProperty $waiver $name ''))) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver is missing '$name'."
            }
        }
        $id = [string](Get-OptionalProperty $waiver 'id' '')
        if (-not [string]::IsNullOrWhiteSpace($id) -and -not $seenIds.Add($id)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "duplicate waiver id '$id'."
        }
        $regressionId = [string](Get-OptionalProperty $waiver 'regressionId' '')
        if (-not [string]::IsNullOrWhiteSpace($regressionId) -and -not $seenRegressionIds.Add($regressionId)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "regressionId '$regressionId' is claimed by more than one waiver."
        }
        foreach ($name in @('projectPath', 'symbol', 'regressionId', 'targetProject')) {
            $value = [string](Get-OptionalProperty $waiver $name '')
            if ($value -match '[*?\[\]]') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' must not use wildcard $name '$value'."
            }
        }
        $targetProject = [string](Get-OptionalProperty $waiver 'targetProject' '')
        $projectPathValue = [string](Get-OptionalProperty $waiver 'projectPath' '')
        if ($projectPathValue -notin $baselineProjects) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' source project '$projectPathValue' is not a reviewed test project."
        }
        if ($targetProject -eq $projectPathValue) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' targetProject must leave the frozen legacy bucket."
        }
        if ($targetProject -notin $baselineProjects) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' targetProject '$targetProject' is not a reviewed test project."
        }
        $changeKind = [string](Get-OptionalProperty $waiver 'changeKind' '')
        if ($changeKind -notin @('Add', 'AttributeChange', 'InlineDataIncrease', 'InlineDataChange', 'InlineDataRemoval', 'DynamicDataSourceChange', 'ExecutionTypeIncrease', 'ExecutionTypeDecrease', 'ProjectedCaseDecrease', 'Remove')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' has unsupported changeKind '$changeKind'."
        }
        $testKind = [string](Get-OptionalProperty $waiver 'testKind' '')
        $owner = [string](Get-OptionalProperty $waiver 'owner' '')
        $approvedBy = [string](Get-OptionalProperty $waiver 'approvedBy' '')
        if ($testKind -notin $allowedTestKinds) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' has unregistered testKind '$testKind'."
        }
        if ($owner -notin $allowedOwners) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' has unregistered owner '$owner'."
        }
        if ($approvedBy -notin $approvedWaiverApprovers) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' approver '$approvedBy' is not registered."
        }
        $expiresOnValue = [string](Get-OptionalProperty $waiver 'expiresOn' '')
        try {
            $expiresOn = [DateOnly]::ParseExact($expiresOnValue, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
            if ($expiresOn -lt $today) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' expired on $expiresOnValue."
            } elseif ($expiresOn.DayNumber - $today.DayNumber -gt $maximumWaiverDays) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' exceeds the $maximumWaiverDays-day maximum."
            }
        } catch {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' expiresOn '$expiresOnValue' is not yyyy-MM-dd."
        }
    }
}

function Test-BaselineStructure {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [switch]$AllowSyntheticPolicy
    )

    if ([string]$Baseline.schemaVersion -ne $baselineSchemaVersion) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "unsupported baseline schemaVersion '$($Baseline.schemaVersion)'."
    }
    if ([string]$Baseline.ruleId -ne $ruleId) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "baseline ruleId must be $ruleId."
    }
    foreach ($metadata in @(
        @{ Name = 'testKinds'; Expected = $allowedTestKinds },
        @{ Name = 'runtimes'; Expected = $allowedRuntimes },
        @{ Name = 'risks'; Expected = $allowedRisks },
        @{ Name = 'owners'; Expected = $allowedOwners },
        @{ Name = 'capabilities'; Expected = $allowedCapabilities }
    )) {
        $actual = @((Get-OptionalProperty $Baseline.allowedMetadata $metadata.Name @()) | ForEach-Object { [string]$_ } | Sort-Object -Unique)
        $expected = @($metadata.Expected | Sort-Object -Unique)
        if (($actual -join '|') -ne ($expected -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "allowedMetadata.$($metadata.Name) differs from the canonical registry."
        }
    }
    $seenProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($project in @($Baseline.projects)) {
        if (-not $seenProjects.Add([string]$project.projectPath)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "duplicate project '$($project.projectPath)'."
        }
        if (-not $AllowSyntheticPolicy) {
            $expectedPolicy = Get-GeneratedProjectPolicy -GeneratedProjectName ([string]$project.projectName)
            foreach ($name in @('freezeMode', 'protectBaselineRemovals')) {
                if ([string](Get-OptionalProperty $project $name '') -ne [string](Get-OptionalProperty $expectedPolicy $name '')) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) $name differs from the canonical project policy."
                }
            }
            foreach ($name in @('frozenTypePatterns', 'allowedNewTestKinds', 'allowedNewRuntimes', 'forbiddenNewTestKinds')) {
                $actual = @((Get-OptionalProperty $project $name @()) | ForEach-Object { [string]$_ } | Sort-Object -Unique)
                $expected = @((Get-OptionalProperty $expectedPolicy $name @()) | ForEach-Object { [string]$_ } | Sort-Object -Unique)
                if (($actual -join '|') -ne ($expected -join '|')) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) $name differs from the canonical project policy."
                }
            }
            $actualCeilings = (Get-OptionalProperty $project 'discoveryCeilings' @()) | ConvertTo-Json -Depth 20 -Compress
            $expectedCeilings = (Get-OptionalProperty $expectedPolicy 'discoveryCeilings' @()) | ConvertTo-Json -Depth 20 -Compress
            if ($actualCeilings -ne $expectedCeilings) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) discoveryCeilings differs from the canonical project policy."
            }
            $expectedHashPaths = @($canonicalFrozenSourceHashPaths[[string]$project.projectName] | Sort-Object -Unique)
            $actualHashPaths = @((Get-OptionalProperty $project 'frozenSourceHashes' ([pscustomobject]@{})).PSObject.Properties |
                ForEach-Object { [string]$_.Name } |
                Sort-Object -Unique)
            if (($actualHashPaths -join '|') -ne ($expectedHashPaths -join '|')) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) frozenSourceHashes keys differ from the canonical content-freeze roster."
            }
        }
        $seenTests = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $executionTemplateCount = 0
        $projectedCaseCount = 0
        foreach ($test in @($project.tests)) {
            if (-not $seenTests.Add([string]$test.id)) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "duplicate test id '$($test.id)' in $($project.projectPath)."
            }
            if ([string]$test.id -notmatch '^aicopilot-test-physical-v1:[0-9a-f]{64}$' -or [string]$test.logicalId -notmatch '^aicopilot-test-decl-v1:[0-9a-f]{64}$') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "invalid stable identity for '$($test.symbol)' in $($project.projectPath)."
            }
            if ([string]$test.testAttributeType -notin $allowedTestAttributeTypes) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "unregistered test attribute '$($test.testAttributeType)' for '$($test.symbol)'."
            }
            if (@($test.executionTypes).Count -eq 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "test declaration '$($test.symbol)' has no concrete execution type."
            }
            $executionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($executionType in @($test.executionTypes)) {
                if ([string]$executionType.id -notmatch '^aicopilot-test-execution-v1:[0-9a-f]{64}$' -or -not $executionIds.Add([string]$executionType.id)) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "invalid or duplicate execution identity for '$($test.symbol)'."
                }
                $executionTemplateCount++
            }
            if ([int]$test.projectedCases % @($test.executionTypes).Count -ne 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "projected case count for '$($test.symbol)' is not divisible by its concrete execution count."
            }
            $projectedCaseCount += [int]$test.projectedCases
        }
        if ([int](Get-OptionalProperty $project 'baselineDeclarations' -1) -ne @($project.tests).Count -or
            [int](Get-OptionalProperty $project 'baselineExecutionTemplates' -1) -ne $executionTemplateCount -or
            [int](Get-OptionalProperty $project 'baselineProjectedCases' -1) -ne $projectedCaseCount) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) summary counts do not match its immutable test records."
        }
        if (-not $AllowSyntheticPolicy) {
            $runnerCases = [int](Get-OptionalProperty $project 'baselineRunnerCases' -1)
            $runnerDigest = [string](Get-OptionalProperty $project 'runnerCaseDigest' '')
            if ($runnerCases -lt $projectedCaseCount -or $runnerDigest -notmatch '^[0-9a-f]{64}$') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) must preserve an exact Release runner count/digest; runner count cannot be below the static projection."
            }
        }
    }

    if (-not $AllowSyntheticPolicy) {
        if ([string](Get-OptionalProperty $Baseline.provenance 'baselineStatus' '') -ne 'Reviewed') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message 'baseline provenance must state Reviewed after the independent source, HTTP/Aspire, commit, and GitHub closure.'
        }
        if ([string](Get-OptionalProperty $Baseline.provenance 'sourceHead' '') -ne $reviewedBaselineSourceHead) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "baseline provenance sourceHead must remain anchored to the independently reviewed commit $reviewedBaselineSourceHead until an explicit baseline migration."
        }
        $overlayPaths = @((Get-OptionalProperty $Baseline.provenance 'pendingOverlay' @()) | ForEach-Object { [string]$_.path } | Sort-Object -Unique)
        if ($overlayPaths.Count -ne 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message 'reviewed baseline must not retain a pending worktree overlay; test bodies remain protected by the frozen source manifests.'
        }
        $baselineProjectPaths = @($Baseline.projects |
            Where-Object { [string]$_.projectName -ne 'AICopilot.CloudAiReadLiveTests' } |
            ForEach-Object { [string]$_.projectPath } |
            Sort-Object -Unique)
        $expectedWorkflowPaths = @('.github/workflows/aicopilot-ci.yml')
        $actualWorkflowPaths = @($Baseline.ciRequirements | ForEach-Object { [string]$_.workflowPath } | Sort-Object -Unique)
        if (($actualWorkflowPaths -join '|') -ne ($expectedWorkflowPaths -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message 'ciRequirements must protect the canonical AICopilot workflow.'
        }
        foreach ($requirement in @($Baseline.ciRequirements)) {
            $requiredProjects = @($requirement.requiredTestProjects | ForEach-Object { [string]$_ } | Sort-Object -Unique)
            if (($requiredProjects -join '|') -ne ($baselineProjectPaths -join '|')) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($requirement.workflowPath) requiredTestProjects differs from the reviewed project set."
            }
            $requiredCommands = @($requirement.requiredCommandPrefixes | ForEach-Object { [string]$_ })
            $canonicalCommands = @(Get-CanonicalRequiredCommandPrefixes)
            if (($requiredCommands -join '|') -ne ($canonicalCommands -join '|')) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($requirement.workflowPath) requiredCommandPrefixes differs from the canonical gate order."
            }
        }
    }
}

function Test-StaticPolicy {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    Test-BaselineStructure -Baseline $Baseline -Errors $Errors
    Test-WaiverManifest -WaiverManifest $WaiverManifest -Baseline $Baseline -Errors $Errors

    $baselineProjects = @($Baseline.projects | ForEach-Object { [string]$_.projectPath } | Sort-Object -Unique)
    $currentProjects = @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration -AllowMissingAssembly |
        ForEach-Object { Get-RelativePath -BasePath $RepositoryRoot -Path $_.ProjectPath } |
        Sort-Object -Unique)
    if (($baselineProjects -join '|') -ne ($currentProjects -join '|')) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "test project set differs from the reviewed baseline. Current=[$($currentProjects -join ', ')], baseline=[$($baselineProjects -join ', ')]."
    }

    $solutionPath = Join-Path $RepositoryRoot 'AICopilot.slnx'
    if (-not (Test-Path $solutionPath -PathType Leaf) -or
        (Get-FileHash $solutionPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $solutionFileSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message 'AICopilot.slnx differs from the exact reviewed build graph file.'
    }
    [xml]$solutionXml = Get-Content $solutionPath -Raw
    $solutionProjects = @(Get-XmlElementsByLocalName -Xml $solutionXml -Names @('Project') |
        ForEach-Object { Get-XmlAttributeValue -Node $_ -Name 'Path' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
    if ($solutionProjects.Count -ne $solutionProjectRosterCount -or
        (ConvertTo-Sha256 -Value ($solutionProjects -join "`n")) -ne $solutionProjectRosterSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message 'AICopilot.slnx project roster differs from the reviewed 32-project build graph.'
    }
    foreach ($projectPathValue in $currentProjects) {
        if ($projectPathValue -notin $solutionProjects) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "$projectPathValue is not included in AICopilot.slnx."
        }
    }

    $directoryBuildFiles = @(Get-ChildItem -Force $RepositoryRoot -Recurse -File |
        Where-Object {
            $_.Name -match '(?i)^Directory\.Build\.(?:props|targets)$' -and
            $_.FullName -notmatch '[/\\](?:bin|obj|node_modules)[/\\]'
        } |
        Sort-Object FullName)
    $directoryBuildManifest = @($directoryBuildFiles | ForEach-Object {
        $relativePath = Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName
        $fileHash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "${relativePath}:${fileHash}"
    }) -join "`n"
    if ($directoryBuildFiles.Count -ne $buildFileManifestCount -or
        (ConvertTo-Sha256 -Value $directoryBuildManifest) -ne $buildFileManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message 'Directory.Build.props/targets roster or content differs from the exact reviewed two-file hard-gate graph.'
    }
    $rootBuildTargets = Get-NormalizedPath (Join-Path $RepositoryRoot 'Directory.Build.targets')
    $nestedTargets = @(Get-ChildItem -Force $RepositoryRoot -Recurse -Filter 'Directory.Build.targets' -File |
        Where-Object {
            $_.FullName -notmatch '[/\\](?:bin|obj|node_modules)[/\\]' -and
            (Get-NormalizedPath $_.FullName) -ne $rootBuildTargets
        })
    foreach ($nestedTarget in $nestedTargets) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "nested targets file shadows the root hard gate: $(Get-RelativePath -BasePath $RepositoryRoot -Path $nestedTarget.FullName)."
    }
    $testRoot = Get-NormalizedPath (Join-Path $RepositoryRoot 'src/tests')
    $canonicalTestBuildProps = Get-NormalizedPath (Join-Path $testRoot 'Directory.Build.props')
    if (-not (Test-Path $rootBuildTargets -PathType Leaf) -or
        (Get-FileHash $rootBuildTargets -Algorithm SHA256).Hash.ToLowerInvariant() -ne $rootBuildTargetsSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message 'Directory.Build.targets differs from the reviewed AICopilot test/deployment hard-gate wiring.'
    }
    if (-not (Test-Path $canonicalTestBuildProps -PathType Leaf) -or
        (Get-FileHash $canonicalTestBuildProps -Algorithm SHA256).Hash.ToLowerInvariant() -ne $canonicalTestBuildPropsSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message 'src/tests/Directory.Build.props differs from the reviewed shared runner/analyzer configuration.'
    }
    $gitAttributesPath = Join-Path $RepositoryRoot '.gitattributes'
    if (-not (Test-Path $gitAttributesPath -PathType Leaf) -or
        (Get-FileHash $gitAttributesPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $gitAttributesSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message '.gitattributes must keep the exact LF normalization rules for every byte-hashed governance/test asset.'
    }
    $globalJsonPath = Join-Path $RepositoryRoot 'global.json'
    if (-not (Test-Path $globalJsonPath -PathType Leaf) -or
        (Get-FileHash $globalJsonPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $globalJsonSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message 'global.json must keep the exact reviewed .NET 10.0.301 SDK selection policy.'
    }
    $codeOwnersPath = Join-Path $RepositoryRoot '.github/CODEOWNERS'
    if (-not (Test-Path $codeOwnersPath -PathType Leaf)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CODEOWNER" -Message '.github/CODEOWNERS is missing; governance assets and test method bodies require reviewed ownership.'
    } else {
        $codeOwnersContent = (Get-Content $codeOwnersPath -Raw).Replace("`r`n", "`n")
        if ((Get-FileHash $codeOwnersPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $codeOwnersSha256) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CODEOWNER" -Message '.github/CODEOWNERS differs from the exact reviewed ownership graph; later shadow rules are not allowed.'
        }
        foreach ($requiredCodeOwnerRule in @(
            '/.github/workflows/** @ShuJinHao',
            '/.gitattributes @ShuJinHao',
            '/global.json @ShuJinHao',
            '/Directory.Build.targets @ShuJinHao',
            '/scripts/tests/TestAICopilotTestGovernancePolicy.ps1 @ShuJinHao',
            '/scripts/tests/TestAICopilotTestGovernanceBehavior.ps1 @ShuJinHao',
            '/scripts/tests/baselines/ @ShuJinHao',
            '/src/tests/**/*.cs @ShuJinHao',
            '/src/tests/**/*.csproj @ShuJinHao',
            '/src/tests/Directory.Build.props @ShuJinHao',
            '/src/tests/xunit.runner.json @ShuJinHao'
        )) {
            if ($codeOwnersContent -notmatch "(?m)^$([regex]::Escape($requiredCodeOwnerRule))$") {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CODEOWNER" -Message "CODEOWNERS does not exactly protect '$requiredCodeOwnerRule'."
            }
        }
    }
    $testSourceFiles = @(Get-ChildItem -Force $testRoot -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' })
    $testSourceText = (@($testSourceFiles | ForEach-Object { (Get-Content $_.FullName -Raw).Replace("`r`n", "`n") }) -join "`n")
    if ($testSourceText -match '(?m):\s*(?:Fact|Theory)Attribute\s*$') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message 'custom Fact/Theory attributes are forbidden in the reviewed AICopilot test graph.'
    }
    if ($testSourceText -match '(?i)(?:\b(?:Assert|[A-Za-z_]\w*Assert\w*)\s*\.\s*Skip(?:When|Unless|If)?\s*\(|(?<![.\w])Skip(?:When|Unless|If)?\s*\(|\bSkip\s*=|\bSkipWhen\s*=|\bSkipUnless\s*=|\bExplicit\s*=)') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message 'test source contains a lexically visible static or runtime Skip/Explicit path.'
    }
    $aiEvalCaseRoot = Join-Path $testRoot 'AICopilot.AiEvalTests/cases'
    $aiEvalCaseFiles = @(Get-ChildItem -Force $aiEvalCaseRoot -Filter '*.json' -File | Sort-Object Name)
    $aiEvalCaseManifest = @($aiEvalCaseFiles | ForEach-Object {
        "$(Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName)|$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())"
    }) -join "`n"
    if ($aiEvalCaseFiles.Count -ne 6 -or (ConvertTo-Sha256 -Value $aiEvalCaseManifest) -ne $aiEvalCaseManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-GOLDEN" -Message 'the six reviewed deterministic AiEval JSON paths/content differ from the frozen manifest.'
    }
    $playwrightSkipRecords = [System.Collections.Generic.List[string]]::new()
    $smokeRoot = Join-Path $RepositoryRoot 'src/vues/AICopilot.Web/tests/smoke'
    foreach ($smokeFile in @(Get-ChildItem -Force $smokeRoot -Recurse -Filter '*.ts' -File | Sort-Object FullName)) {
        $lineNumber = 0
        foreach ($line in @(Get-Content $smokeFile.FullName)) {
            $lineNumber++
            if ($line.Contains('test.skip(', [StringComparison]::Ordinal)) {
                $relativeSmokePath = Get-RelativePath -BasePath $RepositoryRoot -Path $smokeFile.FullName
                $playwrightSkipRecords.Add("${relativeSmokePath}:${lineNumber}:$($line.Trim())")
            }
        }
    }
    $playwrightSkipManifest = @($playwrightSkipRecords) -join "`n"
    if ($playwrightSkipRecords.Count -ne 3 -or (ConvertTo-Sha256 -Value $playwrightSkipManifest) -ne $playwrightSkipManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-UI-SKIP" -Message 'Playwright smoke must keep exactly the three reviewed viewport-specific legacy skips until AI-TEST-UI-001 removes them explicitly; additions or silent drift are forbidden.'
    }
    $vitestRoot = Join-Path $RepositoryRoot 'src/vues/AICopilot.Web/tests/unit'
    $vitestSourceFiles = @(Get-ChildItem -Force $vitestRoot -Recurse -Filter '*.spec.ts' -File | Sort-Object FullName)
    $vitestSourceManifest = @($vitestSourceFiles | ForEach-Object {
        $relativePath = Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName
        $sourceHash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "${relativePath}:${sourceHash}"
    }) -join "`n"
    if ($vitestSourceFiles.Count -ne $vitestUnitSourceCount -or
        (ConvertTo-Sha256 -Value $vitestSourceManifest) -ne $vitestUnitSourceManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-UI-FROZEN" -Message 'Vitest unit-test source roster/content differs from the reviewed 31-file Phase 0 baseline.'
    }
    $vitestSourceText = @($vitestSourceFiles | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    if ($vitestSourceText -match '(?i)\b(?:it|test|describe)\s*\.\s*(?:skip|todo|only|skipIf|runIf)\s*\(' -or
        $vitestSourceText -match '(?i)\b(?:xit|xtest|xdescribe)\s*\(') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message 'Vitest required unit tests must not contain skip/todo/only or conditional runner declarations.'
    }
    $backendTestRoot = Join-Path $RepositoryRoot 'src/tests/AICopilot.BackendTests'
    $backendTestSources = @(Get-ChildItem -Force $backendTestRoot -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' } |
        Sort-Object FullName)
    $backendTestSourceManifest = @($backendTestSources | ForEach-Object {
        $relativePath = Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName
        $sourceHash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "${relativePath}:${sourceHash}"
    }) -join "`n"
    if ($backendTestSources.Count -ne $backendTestSourceCount -or
        (ConvertTo-Sha256 -Value $backendTestSourceManifest) -ne $backendTestSourceManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message 'BackendTests source bodies/roster differ from the exact reviewed 120-file Phase 0 freeze.'
    }
    foreach ($frozenWebAsset in @(
        @{ Path = 'src/vues/AICopilot.Web/package.json'; Sha256 = $vitestPackageJsonSha256 },
        @{ Path = 'src/vues/AICopilot.Web/package-lock.json'; Sha256 = $vitestPackageLockSha256 },
        @{ Path = 'src/vues/AICopilot.Web/vitest.config.ts'; Sha256 = $vitestConfigSha256 },
        @{ Path = 'src/vues/AICopilot.Web/tests/smoke/acceptance.spec.ts'; Sha256 = $playwrightSmokeSourceSha256 },
        @{ Path = 'src/vues/AICopilot.Web/playwright.smoke.config.ts'; Sha256 = $playwrightSmokeConfigSha256 }
    )) {
        $assetPath = Join-Path $RepositoryRoot $frozenWebAsset.Path
        if (-not (Test-Path $assetPath -PathType Leaf) -or
            (Get-FileHash $assetPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $frozenWebAsset.Sha256) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-UI-FROZEN" -Message "$($frozenWebAsset.Path) differs from the reviewed Vitest runner configuration."
        }
    }
    foreach ($frozenDeploymentAsset in @(
        @{ Path = 'deploy/enterprise-ai/tests/deployment-behavior.sh'; Sha256 = $deploymentBehaviorSha256 },
        @{ Path = 'deploy/enterprise-ai/tests/TestDeploymentPolicy.ps1'; Sha256 = $deploymentPolicySha256 }
    )) {
        $assetPath = Join-Path $RepositoryRoot $frozenDeploymentAsset.Path
        if (-not (Test-Path $assetPath -PathType Leaf) -or
            (Get-FileHash $assetPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $frozenDeploymentAsset.Sha256) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-DEPLOYMENT-FROZEN" -Message "$($frozenDeploymentAsset.Path) differs from the reviewed deployment-test Phase 0 baseline."
        }
    }
    $unsupportedTestProjects = @(Get-ChildItem -Force $testRoot -Recurse -File | Where-Object {
        $_.Name -match '\.[A-Za-z0-9]+proj$' -and $_.Extension -ne '.csproj' -and $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]'
    })
    foreach ($unsupportedTestProject in $unsupportedTestProjects) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $unsupportedTestProject.FullName) is a non-C# test project; Phase 0 permits only reviewed csproj projects."
    }
    $unsupportedRepositoryProjects = @(Get-ChildItem -Force $RepositoryRoot -Recurse -File | Where-Object {
        $_.Extension -in @('.fsproj', '.vbproj') -and $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]'
    })
    foreach ($unsupportedRepositoryProject in $unsupportedRepositoryProjects) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $unsupportedRepositoryProject.FullName) uses an unreviewed project language that could hide a test project."
    }
    foreach ($runSettingsFile in @(Get-ChildItem -Force $RepositoryRoot -Recurse -Filter '*.runsettings' -File | Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' })) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $runSettingsFile.FullName) is an alternate VSTest configuration source; required lanes use only the canonical failSkips JSON."
    }
    foreach ($packageSourceOverride in @(Get-ChildItem -Force $RepositoryRoot -Recurse -File | Where-Object {
        $_.Name -in @('NuGet.config', '.npmrc') -and $_.FullName -notmatch '[/\\](?:bin|obj|node_modules)[/\\]'
    })) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $packageSourceOverride.FullName) is an unreviewed package-source/runner configuration override."
    }
    $allTestRootProjects = @(Get-ChildItem -Force $testRoot -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' } |
        ForEach-Object { Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName } |
        Sort-Object -Unique)
    $reviewedTestRootProjects = @($baselineProjects + $allowedSupportProjects | Sort-Object -Unique)
    if (($allTestRootProjects -join '|') -ne ($reviewedTestRootProjects -join '|')) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "every csproj under src/tests must be an explicitly reviewed test or support project. Found=[$($allTestRootProjects -join ', ')], reviewed=[$($reviewedTestRootProjects -join ', ')]."
    }
    foreach ($testProjectAsset in $testProjectAssetSha256.GetEnumerator()) {
        $assetPath = Join-Path $RepositoryRoot $testProjectAsset.Key
        if (-not (Test-Path $assetPath -PathType Leaf) -or
            (Get-FileHash $assetPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$testProjectAsset.Value) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "$($testProjectAsset.Key) differs from the exact reviewed test-runner/project dependency asset."
        }
    }
    $repositoryProjectFiles = @(Get-ChildItem -Force $RepositoryRoot -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj|node_modules)[/\\]' } |
        Sort-Object FullName)
    $repositoryProjectRoster = @($repositoryProjectFiles | ForEach-Object {
        Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName
    }) -join "`n"
    if ($repositoryProjectFiles.Count -ne $repositoryProjectRosterCount -or
        (ConvertTo-Sha256 -Value $repositoryProjectRoster) -ne $repositoryProjectRosterSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message 'repository csproj roster differs from the reviewed 32-project Phase 0 graph.'
    }
    foreach ($projectFile in $repositoryProjectFiles) {
        $projectContent = Get-Content $projectFile.FullName -Raw
        [xml]$projectXml = $projectContent
        $relativeProjectPath = Get-RelativePath -BasePath $RepositoryRoot -Path $projectFile.FullName
        $normalizedProjectPath = Get-NormalizedPath $projectFile.FullName
        $isInsideTestRoot = $normalizedProjectPath.StartsWith("$testRoot/", [StringComparison]::Ordinal)
        $allProjectElements = @($projectXml.SelectNodes('//*'))
        if (@(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Import')).Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an explicit MSBuild import; project-local import indirection is not allowed in the reviewed graph."
        }
        $allIsTestProjectNodes = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('IsTestProject'))
        $directIsTestProjectNodes = @($allIsTestProjectNodes | Where-Object {
            $_.ParentNode.LocalName -ieq 'PropertyGroup' -and
            $_.ParentNode.ParentNode.LocalName -ieq 'Project' -and
            [string]$_.InnerText -match '(?i)^\s*true\s*$'
        })
        $conditionalDirectIsTestProjectNodes = @($directIsTestProjectNodes | Where-Object {
            -not [string]::IsNullOrWhiteSpace((Get-XmlAttributeValue -Node $_ -Name 'Condition'))
        })
        $packageReferenceNodes = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('PackageReference'))
        $testPackageNodes = @($packageReferenceNodes | Where-Object {
            (Get-XmlAttributeValue -Node $_ -Name 'Include') -match '(?i)^(?:xunit(?:\.|$)|NUnit(?:\.|$)|MSTest(?:\.|$)|TUnit(?:\.|$)|Microsoft\.NET\.Test\.Sdk$|Microsoft\.Testing\.Platform(?:\.|$)|Microsoft\.TestPlatform(?:\.|$))'
        })
        $testingPlatformMarkers = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('TestingPlatformApplication', 'IsTestingPlatformApplication') | Where-Object { [string]$_.InnerText -match '(?i)^\s*true\s*$' })
        $indirectPackageIdentityNodes = @($packageReferenceNodes | Where-Object { (Get-XmlAttributeValue -Node $_ -Name 'Include') -match '\$\(' })
        if ($indirectPackageIdentityNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an MSBuild expression as PackageReference identity and can hide test packages."
        }
        $projectRoot = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Project') | Select-Object -First 1)
        $rootSdkIdentity = if ($projectRoot.Count -eq 1) { Get-XmlAttributeValue -Node $projectRoot[0] -Name 'Sdk' } else { '' }
        if ($rootSdkIdentity -match '\$\(') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an indirect Project SDK identity."
        }
        $projectSdkIdentities = [System.Collections.Generic.List[string]]::new()
        foreach ($sdkIdentity in @($rootSdkIdentity -split ';')) {
            if (-not [string]::IsNullOrWhiteSpace($sdkIdentity)) { $projectSdkIdentities.Add($sdkIdentity.Trim()) }
        }
        foreach ($sdkNode in @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Sdk') | Where-Object { $_.ParentNode.LocalName -ieq 'Project' })) {
            $sdkName = Get-XmlAttributeValue -Node $sdkNode -Name 'Name'
            if ([string]::IsNullOrWhiteSpace($sdkName)) { $sdkName = [string]$sdkNode.InnerText }
            if (-not [string]::IsNullOrWhiteSpace($sdkName)) { $projectSdkIdentities.Add($sdkName.Trim()) }
        }
        foreach ($sdkIdentity in $projectSdkIdentities) {
            $sdkName = ($sdkIdentity -split '/', 2)[0]
            if ($sdkName -notin $allowedProjectSdkIdentities) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses unreviewed Project SDK '$sdkIdentity'."
            }
        }
        $rawReferenceNodes = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Reference'))
        if ($rawReferenceNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses raw assembly Reference items; the reviewed graph permits only explicit PackageReference/ProjectReference dependencies."
        }
        $projectReferenceNodes = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('ProjectReference'))
        $indirectProjectReferenceNodes = @($projectReferenceNodes | Where-Object { (Get-XmlAttributeValue -Node $_ -Name 'Include') -match '\$\(' })
        if ($indirectProjectReferenceNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an MSBuild expression as ProjectReference identity and can hide a test dependency."
        }
        $testProjectReferenceNodes = @($projectReferenceNodes | Where-Object {
            $referencePath = Get-XmlAttributeValue -Node $_ -Name 'Include'
            if ([string]::IsNullOrWhiteSpace($referencePath) -or $referencePath -match '\$\(') { return $false }
            $resolvedReference = Get-NormalizedPath (Join-Path $projectFile.Directory.FullName $referencePath)
            return (Get-RelativePath -BasePath $RepositoryRoot -Path $resolvedReference) -in $baselineProjects
        })
        if (-not $isInsideTestRoot -and $testProjectReferenceNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath references a reviewed test project from production/support code."
        }
        if ($projectContent -match '(?i)(?:RunSettings|\.runsettings)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$relativeProjectPath configures an alternate test-runner setting that can override failSkips."
        }
        if ($projectContent -match '(?i)<[A-Za-z0-9_.-]*(?:Xunit.*Runner|Runner.*Xunit|RunnerJson|TestRunner)[A-Za-z0-9_.-]*>') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$relativeProjectPath declares a project-local test runner property; only the shared reviewed configuration is allowed."
        }
        if ($conditionalDirectIsTestProjectNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath makes IsTestProject=true conditional; reviewed test identity must be direct and unconditional."
        }
        $forbiddenBuildElements = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @(
            'DesignTimeBuild', 'IsCrossTargetingBuild', 'DirectoryBuildTargetsPath', 'ImportDirectoryBuildTargets',
            'VSTestTestAdapterPath', 'VSTestTestCaseFilter', 'VSTestSetting', 'RestoreSources', 'RestoreAdditionalProjectSources',
            'UsingTask', 'TaskFactory', 'InitialTargets'
        ))
        if ($forbiddenBuildElements.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath defines an MSBuild lifecycle property that can suppress the required hard gate."
        }
        if (@(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Target')).Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath embeds project-local MSBuild Target execution; only the exact reviewed root Directory.Build.targets is allowed."
        }
        if (-not $isInsideTestRoot -and ($allIsTestProjectNodes.Count -gt 0 -or $testingPlatformMarkers.Count -gt 0 -or $projectFile.BaseName -match '(?i)(?:^|\.)Tests?$')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath declares or is named as a test project outside src/tests."
        }
        if (($testPackageNodes.Count -gt 0 -or $testingPlatformMarkers.Count -gt 0) -and $directIsTestProjectNodes.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses a test package without one explicit IsTestProject=true."
        }
        if (($testPackageNodes.Count -gt 0 -or $testingPlatformMarkers.Count -gt 0) -and -not $isInsideTestRoot) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath is a test project outside src/tests."
        }
        if ($directIsTestProjectNodes.Count -gt 0) {
            if (-not $isInsideTestRoot) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath explicitly declares IsTestProject=true outside src/tests."
            }
            if ($relativeProjectPath -notin $baselineProjects) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "$relativeProjectPath is an unreviewed test project outside the baseline/CI matrix."
            }
        }
        if ($relativeProjectPath -in $baselineProjects -and ($directIsTestProjectNodes.Count -ne 1 -or $allIsTestProjectNodes.Count -ne 1)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath must own exactly one unconditional, direct IsTestProject=true declaration."
        }
        if ($relativeProjectPath -in $allowedSupportProjects) {
            if ($allIsTestProjectNodes.Count -ne 0 -or $testPackageNodes.Count -ne 0 -or $testingPlatformMarkers.Count -ne 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "$relativeProjectPath is a support host and must not become a hidden test project."
            }
            $supportSource = @(Get-ChildItem -Force $projectFile.Directory.FullName -Recurse -Filter '*.cs' -File |
                Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' } |
                ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
            if ($supportSource -match '(?m)^\s*\[(?:Fact|Theory)(?:Attribute)?\b') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "$relativeProjectPath support host must not contain test declarations."
            }
        }
        $runnerConfigOverrides = @($allProjectElements | Where-Object { Test-XmlElementHasAnyAttribute -Node $_ -Names @('Include', 'Update', 'Remove') } | Where-Object {
            @($_.Attributes | ForEach-Object { [string]$_.Value } | Where-Object { $_ -match '(?i)xunit\.runner\.json' }).Count -gt 0
        })
        if ($runnerConfigOverrides.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$relativeProjectPath overrides xunit.runner.json; only src/tests/Directory.Build.props may define it."
        }
        $gateTargetOverrides = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Target') | Where-Object { (Get-XmlAttributeValue -Node $_ -Name 'Name') -match '^ValidateAICopilotTestGovernance' })
        if ($gateTargetOverrides.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath overrides an AICopilot test-governance MSBuild target."
        }
        if ($relativeProjectPath -in $baselineProjects) {
            $projectTargets = @(Get-XmlElementsByLocalName -Xml $projectXml -Names @('Target'))
            $expectedTargetHash = [string]$allowedTestProjectTargetHashes[$relativeProjectPath]
            if ($projectTargets.Count -eq 0) {
                if (-not [string]::IsNullOrWhiteSpace($expectedTargetHash)) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath removed its reviewed MSBuild target set."
                }
            } elseif ([string]::IsNullOrWhiteSpace($expectedTargetHash)) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath introduces an unreviewed MSBuild target."
            } else {
                $targetMaterial = @($projectTargets | ForEach-Object { $_.OuterXml }) -join "`n"
                if ((ConvertTo-Sha256 -Value $targetMaterial) -ne $expectedTargetHash) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath changed its reviewed MSBuild target set."
                }
            }
        }
    }

    $currentSupportAssets = @(Get-ChildItem -Force (Join-Path $RepositoryRoot 'src/tests/AICopilot.Testing.McpServer') -Recurse -File |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' -and $_.Extension -in @('.cs', '.csproj') } |
        ForEach-Object { Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName } |
        Sort-Object -Unique)
    $expectedSupportAssets = @($supportAssetSha256.Keys | Sort-Object)
    if (($currentSupportAssets -join '|') -ne ($expectedSupportAssets -join '|')) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message 'AICopilot.Testing.McpServer support-only source roster differs from the reviewed freeze.'
    }
    foreach ($supportAsset in $supportAssetSha256.GetEnumerator()) {
        $assetPath = Join-Path $RepositoryRoot $supportAsset.Key
        if (-not (Test-Path $assetPath -PathType Leaf) -or
            (Get-FileHash $assetPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$supportAsset.Value) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "$($supportAsset.Key) differs from the exact reviewed support-only asset."
        }
    }
    foreach ($buildFile in @(Get-ChildItem -Force $RepositoryRoot -Recurse -File | Where-Object { $_.Name -match '\.(?:props|targets)$' -and $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' })) {
        $buildContent = Get-Content $buildFile.FullName -Raw
        [xml]$buildXml = $buildContent
        $allBuildElements = @($buildXml.SelectNodes('//*'))
        $normalizedBuildFile = Get-NormalizedPath $buildFile.FullName
        $relativeBuildFile = Get-RelativePath -BasePath $RepositoryRoot -Path $buildFile.FullName
        if ($normalizedBuildFile.StartsWith("$testRoot/", [StringComparison]::Ordinal) -and $normalizedBuildFile -ne $canonicalTestBuildProps) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile is an unreviewed test props/targets indirection; only src/tests/Directory.Build.props is allowed."
        }
        $buildIsTestProjectNodes = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @('IsTestProject'))
        $buildPackageReferenceNodes = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @('PackageReference'))
        $declaresTestProject = @($buildIsTestProjectNodes | Where-Object { [string]$_.InnerText -match '(?i)^\s*true\s*$' }).Count -gt 0
        $declaresTestPackage = @($buildPackageReferenceNodes | Where-Object {
            (Get-XmlAttributeValue -Node $_ -Name 'Include') -match '(?i)^(?:xunit(?:\.|$)|NUnit(?:\.|$)|MSTest(?:\.|$)|TUnit(?:\.|$)|Microsoft\.NET\.Test\.Sdk$|Microsoft\.Testing\.Platform(?:\.|$)|Microsoft\.TestPlatform(?:\.|$))'
        }).Count -gt 0
        $declaresTestingPlatform = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @('TestingPlatformApplication', 'IsTestingPlatformApplication') | Where-Object {
            [string]$_.InnerText -match '(?i)^\s*true\s*$'
        }).Count -gt 0
        $indirectPackageIdentityNodes = @($buildPackageReferenceNodes | Where-Object { (Get-XmlAttributeValue -Node $_ -Name 'Include') -match '\$\(' })
        if ($indirectPackageIdentityNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile uses an MSBuild expression as PackageReference identity and can hide test packages."
        }
        if ($buildContent -match '(?i)(?:RunSettings|\.runsettings)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$relativeBuildFile configures an alternate VSTest runsettings source that can override failSkips."
        }
        if (($declaresTestProject -or $declaresTestPackage -or $declaresTestingPlatform) -and -not (Get-NormalizedPath $buildFile.FullName).StartsWith("$testRoot/", [StringComparison]::Ordinal)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile defines imported test identity/packages outside src/tests."
        }
        if ($buildIsTestProjectNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile must not define IsTestProject; every reviewed test csproj must own that identity directly."
        }
        $forbiddenBuildFileElements = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @(
            'DesignTimeBuild', 'IsCrossTargetingBuild', 'DirectoryBuildTargetsPath', 'ImportDirectoryBuildTargets',
            'VSTestTestAdapterPath', 'VSTestTestCaseFilter', 'VSTestSetting', 'RestoreSources', 'RestoreAdditionalProjectSources',
            'UsingTask', 'TaskFactory', 'InitialTargets'
        ))
        if ($forbiddenBuildFileElements.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile defines an MSBuild lifecycle property that can suppress the required hard gate."
        }
        $runnerConfigOverrides = @($allBuildElements | Where-Object { Test-XmlElementHasAnyAttribute -Node $_ -Names @('Include', 'Update', 'Remove') } | Where-Object {
            @($_.Attributes | ForEach-Object { [string]$_.Value } | Where-Object { $_ -match '(?i)xunit\.runner\.json' }).Count -gt 0
        })
        if ($runnerConfigOverrides.Count -gt 0 -and $normalizedBuildFile -ne $canonicalTestBuildProps) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$relativeBuildFile overrides xunit.runner.json; only src/tests/Directory.Build.props may define it."
        }
        $gateTargetOverrides = @(Get-XmlElementsByLocalName -Xml $buildXml -Names @('Target') | Where-Object { (Get-XmlAttributeValue -Node $_ -Name 'Name') -match '^ValidateAICopilotTestGovernance' })
        $rootTargets = Get-NormalizedPath (Join-Path $RepositoryRoot 'Directory.Build.targets')
        if ($gateTargetOverrides.Count -gt 0 -and $normalizedBuildFile -ne $rootTargets) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile overrides an AICopilot test-governance MSBuild target."
        }
    }

    foreach ($project in @($Baseline.projects | Where-Object { [string]$_.freezeMode -eq 'All' })) {
        $projectFile = Join-Path $RepositoryRoot $project.projectPath
        $currentSources = @(Get-ProjectSourceFiles -ResolvedProjectPath $projectFile)
        if (($currentSources -join '|') -ne (@($project.frozenSourceFiles | Sort-Object -Unique) -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$($project.projectName) source-file roster differs from the frozen Phase 0 baseline."
        }
        $sourceHashes = Get-OptionalProperty $project 'frozenSourceHashes' $null
        if ($null -ne $sourceHashes) {
            foreach ($hashProperty in @($sourceHashes.PSObject.Properties)) {
                $sourcePath = Join-Path $RepositoryRoot $hashProperty.Name
                if (-not (Test-Path $sourcePath -PathType Leaf) -or
                    (Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$hashProperty.Value) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$($hashProperty.Name) differs from the content-frozen architecture/eval/live source."
                }
            }
        }
    }

    $canonicalAICopilotWorkflow = Get-NormalizedPath (Join-Path $RepositoryRoot '.github/workflows/aicopilot-ci.yml')
    $workflowFiles = @(Get-ChildItem -Force (Join-Path $RepositoryRoot '.github/workflows') -File |
        Where-Object { $_.Extension -in @('.yml', '.yaml') } |
        Sort-Object FullName)
    $workflowManifest = @($workflowFiles | ForEach-Object {
        $relativePath = Get-RelativePath -BasePath $RepositoryRoot -Path $_.FullName
        $fileHash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "${relativePath}:${fileHash}"
    }) -join "`n"
    if ($workflowFiles.Count -ne $workflowManifestCount -or
        (ConvertTo-Sha256 -Value $workflowManifest) -ne $workflowManifestSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message 'workflow roster/content differs from the exact reviewed nine-workflow identity graph.'
    }
    foreach ($workflowFile in $workflowFiles) {
        if ((Get-NormalizedPath $workflowFile.FullName) -eq $canonicalAICopilotWorkflow) { continue }
        $otherWorkflowContent = (Get-Content $workflowFile.FullName -Raw).Replace("`r`n", "`n")
        if ($otherWorkflowContent -match '(?i)(?<![A-Za-z0-9_-])(?:aicopilot-ci|build-test)(?![A-Za-z0-9_-])') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $workflowFile.FullName) duplicates the protected aicopilot-ci/build-test check identity."
        }
    }

    $runnerConfigs = @(Get-ChildItem -Force $testRoot -Recurse -Filter '*xunit.runner.json' -File | Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' })
    $canonicalRunnerConfig = Join-Path $testRoot 'xunit.runner.json'
    if ($runnerConfigs.Count -ne 1 -or (Get-NormalizedPath $runnerConfigs[0].FullName) -ne (Get-NormalizedPath $canonicalRunnerConfig)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message 'src/tests must contain exactly one shared xunit.runner.json; project-local overrides are forbidden.'
    } else {
        Test-RunnerConfigurationFile -ResolvedRunnerConfigPath $canonicalRunnerConfig -Errors $Errors -Context 'shared source configuration'
    }
    $testBuildProps = Join-Path $testRoot 'Directory.Build.props'
    [xml]$testBuildPropsXml = Get-Content $testBuildProps -Raw
    $runnerConfigItems = @($testBuildPropsXml.SelectNodes('//None') | Where-Object {
        [string]$_.Include -eq '$(MSBuildThisFileDirectory)xunit.runner.json' -and
        [string]$_.Link -eq 'xunit.runner.json' -and
        [string]$_.CopyToOutputDirectory -eq 'PreserveNewest'
    })
    if ($runnerConfigItems.Count -ne 1) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message 'src/tests/Directory.Build.props must copy the single failSkips runner configuration into every test output.'
    }

    foreach ($requirement in @($Baseline.ciRequirements)) {
        $workflowPath = Join-Path $RepositoryRoot $requirement.workflowPath
        if (-not (Test-Path $workflowPath -PathType Leaf)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "required workflow does not exist: $($requirement.workflowPath)."
            continue
        }
        $workflowContent = (Get-Content $workflowPath -Raw).Replace('\', '/')
        $expectedWorkflowHash = [string]$requiredWorkflowSha256[[string]$requirement.workflowPath]
        $actualWorkflowHash = (Get-FileHash $workflowPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($expectedWorkflowHash) -or $actualWorkflowHash -ne $expectedWorkflowHash) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) differs from the exact reviewed workflow; extra events/jobs/defaults/env are forbidden."
        }
        $requiredJobName = 'build-test'
        $jobEnvelopes = @(Get-WorkflowJobEnvelope -WorkflowContent $workflowContent -JobName $requiredJobName)
        if ($jobEnvelopes.Count -ne 1 -or $jobEnvelopes[0].HasIf -or $jobEnvelopes[0].Ambiguous -or
            @($jobEnvelopes[0].UnexpectedKeys).Count -gt 0 -or
            @($jobEnvelopes[0].RunsOnValues).Count -ne 1 -or [string]$jobEnvelopes[0].RunsOnValues[0] -ne 'ubuntu-latest' -or
            @($jobEnvelopes[0].TimeoutValues).Count -ne 1 -or [string]$jobEnvelopes[0].TimeoutValues[0] -ne '25') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) job '$requiredJobName' must be an unconditional ubuntu-latest job with exact direct properties and timeout-minutes: 25."
        }
        if ($jobEnvelopes.Count -eq 1) {
            $actualJobSha256 = ConvertTo-Sha256 -Value (([string]$jobEnvelopes[0].Content).TrimEnd())
            $expectedJobSha256 = [string]$requiredWorkflowJobSha256[[string]$requirement.workflowPath]
            if ([string]::IsNullOrWhiteSpace($expectedJobSha256) -or $actualJobSha256 -ne $expectedJobSha256) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) job '$requiredJobName' differs from the exact reviewed job body."
            }
            $actualStepNames = @(Get-WorkflowStepNames -JobContent ([string]$jobEnvelopes[0].Content))
            $expectedStepNames = @(Get-CanonicalWorkflowStepNames -WorkflowPath ([string]$requirement.workflowPath))
            if (($actualStepNames -join '|') -cne ($expectedStepNames -join '|')) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) required job step names/order differ from the exact reviewed sequence."
            }
            $runSteps = @(Get-WorkflowRunSteps -WorkflowContent ([string]$jobEnvelopes[0].Content))
            $deploymentSteps = @($runSteps | Where-Object { [string]$_.Name -eq 'Run deployment behavior tests' })
            $expectedDeploymentRun = "set -euo pipefail`nbash deploy/enterprise-ai/tests/deployment-behavior.sh 2>&1 | tee artifacts/test-results/deployment-behavior.log"
            if ($deploymentSteps.Count -ne 1 -or
                [string]$deploymentSteps[0].Shell -cne 'bash' -or
                [string]$deploymentSteps[0].Run -cne $expectedDeploymentRun -or
                [bool]$deploymentSteps[0].HasIf -or
                [bool]$deploymentSteps[0].Ambiguous -or
                @($deploymentSteps[0].UnexpectedKeys).Count -gt 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI-PIPELINE" -Message "$($requirement.workflowPath) deployment behavior evidence must use unconditional bash, set -euo pipefail, and capture stderr through tee without hiding the test exit code."
            }
        }
        if ($workflowContent -match '(?mi)^\s*continue-on-error:\s*true\s*$' -or $workflowContent -match '(?mi)^\s*if:\s*(?:false|\$\{\{\s*false\s*\}\})\s*$') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) contains a disabled or continue-on-error step that can hide a required gate."
        }
        if ($workflowContent -match '(?i)(?:IsTestProject|ImportDirectoryBuildTargets)\s*=\s*false' -or
            $workflowContent -match '(?i)(?:DirectoryBuildTargetsPath|DesignTimeBuild)\s*=') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) passes an MSBuild property that can bypass the required test gate."
        }
        if ($workflowContent -match '(?i)(?:--settings(?:\s|=)|RunSettingsFilePath|\.runsettings)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) configures alternate VSTest runsettings and can override failSkips."
        }
        if ($workflowContent -notmatch '(?m)^\s*pull_request:\s*\{\}\s*$') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) must emit the required status for every pull request; path-filtered pull_request triggers are forbidden."
        }
        foreach ($triggerAsset in @('.gitattributes', 'Directory.Build.targets', 'scripts/**', '.github/CODEOWNERS', '.github/workflows/**')) {
            if (-not $workflowContent.Contains('- "' + $triggerAsset + '"', [StringComparison]::Ordinal)) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) push trigger does not cover '$triggerAsset'."
            }
        }
        foreach ($requiredProject in @($requirement.requiredTestProjects)) {
            $pattern = '(?m)^[ \t]*(?:run:[ \t]*)?dotnet[ \t]+test[ \t]+' + [regex]::Escape([string]$requiredProject) + '(?=[ \t]|$)'
            $matches = [regex]::Matches($workflowContent, $pattern)
            if ($matches.Count -eq 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) does not schedule '$requiredProject'."
            }
        }
        $lastCommandPosition = -1
        foreach ($commandPrefix in @($requirement.requiredCommandPrefixes)) {
            $pattern = '(?m)^[ \t]*(?:run:[ \t]*)?' + [regex]::Escape([string]$commandPrefix) + '(?=[ \t]|$)'
            $match = [regex]::Match($workflowContent, $pattern)
            if (-not $match.Success) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) is missing required command '$commandPrefix'."
            } elseif ($match.Index -le $lastCommandPosition) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) schedules '$commandPrefix' out of governance order."
            } else {
                $lastCommandPosition = $match.Index
            }
        }
    }
}

function Test-ProjectSnapshot {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    $projectEntries = @($Baseline.projects | Where-Object { $_.projectPath -eq $Snapshot.projectPath })
    if ($projectEntries.Count -ne 1) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "snapshot project '$($Snapshot.projectPath)' does not map to exactly one baseline project."
        return
    }
    $project = $projectEntries[0]
    if ([string]$project.projectName -ne [string]$Snapshot.projectName) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "project name mismatch for $($Snapshot.projectPath): current=$($Snapshot.projectName), baseline=$($project.projectName)."
    }

    $baselineById = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($test in @($project.tests)) { $baselineById[[string]$test.id] = $test }
    $currentIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $deltas = [System.Collections.Generic.List[object]]::new()
    foreach ($test in @($Snapshot.tests)) {
        if (-not $currentIds.Add([string]$test.id)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message "duplicate current test id '$($test.id)'."
            continue
        }
        if (-not $baselineById.ContainsKey([string]$test.id)) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'Add'; Test = $test })
            continue
        }
        $baselineTest = $baselineById[[string]$test.id]
        if ([string]$test.testAttributeType -ne [string]$baselineTest.testAttributeType -or
            [string]$test.attributeCategory -ne [string]$baselineTest.attributeCategory -or
            [string](Get-OptionalProperty $test.testAttributePolicy 'signature' '') -ne [string](Get-OptionalProperty $baselineTest.testAttributePolicy 'signature' '')) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'AttributeChange'; Test = $test })
        }
        if ((Get-TestTraitSignature -Test $test) -cne (Get-TestTraitSignature -Test $baselineTest)) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'TraitChange'; Test = $test })
        }
        if ([int]$test.inlineDataRows -gt [int]$baselineTest.inlineDataRows) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'InlineDataIncrease'; Test = $test })
        } elseif ((@($test.inlineDataSignatures) -join '|') -ne (@($baselineTest.inlineDataSignatures) -join '|')) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'InlineDataChange'; Test = $test })
        }
        foreach ($inlineDataRemoval in @(Get-InlineDataRemovalDeltaTests -BaselineTest $baselineTest -CurrentTest $test)) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'InlineDataRemoval'; Test = $inlineDataRemoval })
        }
        $oldDynamicSources = @($baselineTest.dynamicDataSources)
        $newDynamicSources = @($test.dynamicDataSources)
        if (($newDynamicSources -join '|') -ne ($oldDynamicSources -join '|')) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'DynamicDataSourceChange'; Test = $test })
        }
        $baselineExecutionIds = @($baselineTest.executionTypes | ForEach-Object { [string]$_.id })
        foreach ($executionType in @($test.executionTypes | Where-Object { [string]$_.id -notin $baselineExecutionIds })) {
            $executionDeltaTest = $test.PSObject.Copy()
            $executionDeltaTest.executionType = [string]$executionType.name
            $executionDeltaTest.traits = $executionType.traits
            $deltas.Add([pscustomobject]@{ ChangeKind = 'ExecutionTypeIncrease'; Test = $executionDeltaTest })
        }
        $currentExecutionIds = @($test.executionTypes | ForEach-Object { [string]$_.id })
        foreach ($executionType in @($baselineTest.executionTypes | Where-Object { [string]$_.id -notin $currentExecutionIds })) {
            $executionDeltaTest = $baselineTest.PSObject.Copy()
            $executionDeltaTest.id = [string]$executionType.id
            $executionDeltaTest | Add-Member -NotePropertyName declarationId -NotePropertyValue ([string]$baselineTest.id) -Force
            $executionDeltaTest | Add-Member -NotePropertyName projectedCasesLost -NotePropertyValue (Get-ProjectedCasesPerExecution -Test $baselineTest) -Force
            $executionDeltaTest.executionType = [string]$executionType.name
            $executionDeltaTest.traits = $executionType.traits
            $deltas.Add([pscustomobject]@{ ChangeKind = 'ExecutionTypeDecrease'; Test = $executionDeltaTest })
        }
        $projectedCaseDecrease = Get-ProjectedCaseDecreaseDeltaTest -BaselineTest $baselineTest -CurrentTest $test
        if ($null -ne $projectedCaseDecrease) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'ProjectedCaseDecrease'; Test = $projectedCaseDecrease })
        }
    }

    $projectWaivers = @($WaiverManifest.waivers | Where-Object { $_.projectPath -eq $Snapshot.projectPath })
    $usedWaiverIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($baselineTest in @($project.tests | Where-Object { [string]$_.id -notin $currentIds })) {
        if ([bool]$project.protectBaselineRemovals -or (Test-IsFrozenTest -Project $project -Test $baselineTest)) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'Remove'; Test = $baselineTest })
        }
    }
    foreach ($delta in $deltas) {
        $test = $delta.Test
        $location = "$($Snapshot.projectPath) :: $($test.symbol) [$($test.id)]"
        if ($delta.ChangeKind -in @('Remove', 'ExecutionTypeDecrease', 'ProjectedCaseDecrease', 'InlineDataRemoval')) {
            $matchingWaivers = @($projectWaivers | Where-Object { $_.symbol -eq $test.id -and $_.changeKind -eq $delta.ChangeKind })
            if ($matchingWaivers.Count -ne 1) {
                $lossCode = switch ($delta.ChangeKind) {
                    'Remove' { "$ruleId-REMOVAL" }
                    'InlineDataRemoval' { "$ruleId-INLINE-DATA" }
                    default { "$ruleId-CASE-DECREASE" }
                }
                Add-PolicyError -Errors $Errors -Code $lossCode -Message "$location is a protected '$($delta.ChangeKind)' and requires one exact verified-migration waiver."
            } else {
                [void]$usedWaiverIds.Add([string]$matchingWaivers[0].id)
            }
            continue
        }
        Test-NewTestMetadata -Test $test -Baseline $Baseline -Errors $Errors -Location $location
        $testKind = @(Get-TraitValues -Traits $test.traits -Name 'TestKind')
        $runtime = @(Get-TraitValues -Traits $test.traits -Name 'Runtime')

        if (@($project.allowedNewTestKinds).Count -gt 0 -and ($testKind.Count -ne 1 -or $testKind[0] -notin @($project.allowedNewTestKinds))) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-ROUTE" -Message "$location must use one of [$(@($project.allowedNewTestKinds) -join ', ')]."
        }
        foreach ($runtimeValue in $runtime) {
            if (@($project.allowedNewRuntimes).Count -gt 0 -and $runtimeValue -notin @($project.allowedNewRuntimes)) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-ROUTE" -Message "$location Runtime '$runtimeValue' is not allowed in $($project.projectName)."
            }
        }
        if ($testKind.Count -eq 1 -and $testKind[0] -in @($project.forbiddenNewTestKinds)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-ROUTE" -Message "$location must leave $($project.projectName); TestKind '$($testKind[0])' is forbidden here."
        }

        $isFrozen = Test-IsFrozenTest -Project $project -Test $test
        if (-not $isFrozen) { continue }
        if ([string]$project.freezeMode -eq 'All' -and $delta.ChangeKind -in @(
            'Add',
            'AttributeChange',
            'TraitChange',
            'InlineDataIncrease',
            'InlineDataChange',
            'DynamicDataSourceChange',
            'ExecutionTypeIncrease'
        )) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$location cannot expand the fully frozen legacy project; add the replacement only to its reviewed target project."
            continue
        }

        $matchingWaivers = @($projectWaivers | Where-Object { $_.symbol -eq $test.id -and $_.changeKind -eq $delta.ChangeKind })
        if ($matchingWaivers.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$location is frozen for '$($delta.ChangeKind)' and requires one exact waiver."
            continue
        }
        $waiver = $matchingWaivers[0]
        [void]$usedWaiverIds.Add([string]$waiver.id)
        $owner = @(Get-TraitValues -Traits $test.traits -Name 'Owner')
        if ($testKind.Count -ne 1 -or [string]$waiver.testKind -ne $testKind[0] -or $owner.Count -ne 1 -or [string]$waiver.owner -ne $owner[0]) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$($waiver.id)' does not match the test TestKind/Owner metadata."
        }
    }

    foreach ($waiver in $projectWaivers) {
        if (-not $usedWaiverIds.Contains([string]$waiver.id)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "stale waiver '$($waiver.id)' matches no current frozen delta."
        }
    }

    $removedCount = @($project.tests | Where-Object { $_.id -notin $currentIds }).Count
    Write-Host "Validated $($Snapshot.projectName): current=$(@($Snapshot.tests).Count), new/expanded=$($deltas.Count), removed=$removedCount"
}

function Test-RepositorySnapshotPolicies {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][object]$SnapshotsByProject,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    $seenLogicalIds = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $seenRegressionIds = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $protectedCaseLosses = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)

    foreach ($projectPathValue in @($SnapshotsByProject.Keys | Sort-Object)) {
        $snapshot = $SnapshotsByProject[$projectPathValue]
        foreach ($test in @($snapshot.tests)) {
            if ($seenLogicalIds.ContainsKey([string]$test.logicalId) -and $seenLogicalIds[[string]$test.logicalId] -ne [string]$snapshot.projectPath) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-DUPLICATE" -Message "logical declaration '$($test.symbol)' exists in both '$($seenLogicalIds[[string]$test.logicalId])' and '$($snapshot.projectPath)'."
            } else {
                $seenLogicalIds[[string]$test.logicalId] = [string]$snapshot.projectPath
            }

            $regressionIds = @(Get-TraitValues -Traits $test.traits -Name 'RegressionId')
            if ($regressionIds.Count -gt 1) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "$($snapshot.projectPath) :: $($test.symbol) declares more than one RegressionId."
            } elseif ($regressionIds.Count -eq 1) {
                $regressionId = [string]$regressionIds[0]
                $location = "$($snapshot.projectPath) :: $($test.symbol)"
                if ($seenRegressionIds.ContainsKey($regressionId) -and $seenRegressionIds[$regressionId] -ne $location) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "RegressionId '$regressionId' is duplicated by '$($seenRegressionIds[$regressionId])' and '$location'."
                } else {
                    $seenRegressionIds[$regressionId] = $location
                }
            }
        }

        $baselineProject = @($Baseline.projects | Where-Object { $_.projectPath -eq $snapshot.projectPath })
        if ($baselineProject.Count -ne 1) { continue }
        foreach ($baselineTest in @($baselineProject[0].tests)) {
            $currentTest = @($snapshot.tests | Where-Object { $_.id -eq $baselineTest.id })
            if ($currentTest.Count -ne 1) { continue }
            $decrease = Get-ProjectedCaseDecreaseDeltaTest -BaselineTest $baselineTest -CurrentTest $currentTest[0]
            if ($null -ne $decrease) {
                $protectedCaseLosses[[string]$decrease.id] = $decrease
            }
            foreach ($inlineDataRemoval in @(Get-InlineDataRemovalDeltaTests -BaselineTest $baselineTest -CurrentTest $currentTest[0])) {
                $protectedCaseLosses[[string]$inlineDataRemoval.id] = $inlineDataRemoval
            }
        }
    }

    foreach ($waiver in @($WaiverManifest.waivers | Where-Object { $_.changeKind -in @('Remove', 'ExecutionTypeDecrease', 'ProjectedCaseDecrease', 'InlineDataRemoval') })) {
        $sourceProject = @($Baseline.projects | Where-Object { $_.projectPath -eq $waiver.projectPath })
        $sourceTest = @()
        $sourceLogicalId = $null
        $projectedCasesLost = 0
        if ($sourceProject.Count -eq 1 -and $waiver.changeKind -eq 'Remove') {
            $sourceTest = @($sourceProject[0].tests | Where-Object { $_.id -eq $waiver.symbol })
            if ($sourceTest.Count -eq 1) {
                $sourceLogicalId = [string]$sourceTest[0].logicalId
                $projectedCasesLost = [int]$sourceTest[0].projectedCases
            }
        } elseif ($sourceProject.Count -eq 1 -and $waiver.changeKind -eq 'ExecutionTypeDecrease') {
            $sourceTest = @($sourceProject[0].tests | Where-Object {
                @($_.executionTypes | Where-Object { $_.id -eq $waiver.symbol }).Count -eq 1
            })
            if ($sourceTest.Count -eq 1) {
                $projectedCasesLost = Get-ProjectedCasesPerExecution -Test $sourceTest[0]
            }
        } elseif ($sourceProject.Count -eq 1 -and $waiver.changeKind -in @('ProjectedCaseDecrease', 'InlineDataRemoval') -and $protectedCaseLosses.ContainsKey([string]$waiver.symbol)) {
            $decrease = $protectedCaseLosses[[string]$waiver.symbol]
            $sourceTest = @($sourceProject[0].tests | Where-Object { $_.id -eq $decrease.declarationId })
            $projectedCasesLost = [int]$decrease.projectedCasesLost
        }

        if ($sourceTest.Count -ne 1 -or $projectedCasesLost -le 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "migration waiver '$($waiver.id)' does not resolve one concrete baseline loss."
            continue
        }
        if (-not $SnapshotsByProject.ContainsKey([string]$waiver.targetProject)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "migration waiver '$($waiver.id)' target project was not scanned."
            continue
        }

        $targetBaselineProject = @($Baseline.projects | Where-Object { $_.projectPath -eq $waiver.targetProject })
        $targetMatches = @($SnapshotsByProject[[string]$waiver.targetProject].tests | Where-Object {
            $candidate = $_
            $regressionIds = @(Get-TraitValues -Traits $candidate.traits -Name 'RegressionId')
            $testKinds = @(Get-TraitValues -Traits $candidate.traits -Name 'TestKind')
            $owners = @(Get-TraitValues -Traits $candidate.traits -Name 'Owner')
            if ($regressionIds.Count -ne 1 -or $regressionIds[0] -ne [string]$waiver.regressionId -or
                $testKinds.Count -ne 1 -or $testKinds[0] -ne [string]$waiver.testKind -or
                $owners.Count -ne 1 -or $owners[0] -ne [string]$waiver.owner) {
                return $false
            }
            if ($waiver.changeKind -eq 'Remove' -and [string]$candidate.logicalId -ne $sourceLogicalId) {
                return $false
            }

            $targetBaselineTest = if ($targetBaselineProject.Count -eq 1) {
                @($targetBaselineProject[0].tests | Where-Object { $_.id -eq $candidate.id })
            } else { @() }
            $addedProjectedCases = if ($targetBaselineTest.Count -eq 0) {
                [int]$candidate.projectedCases
            } elseif ($targetBaselineTest.Count -eq 1) {
                [int]$candidate.projectedCases - [int]$targetBaselineTest[0].projectedCases
            } else { 0 }
            return $addedProjectedCases -ge $projectedCasesLost
        })
        if ($targetMatches.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "migration waiver '$($waiver.id)' is not backed by one uniquely classified RegressionId '$($waiver.regressionId)' with at least $projectedCasesLost newly added case(s) in '$($waiver.targetProject)'."
        }
    }
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [ValidateRange(1, 600)][int]$TimeoutSeconds = 120
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) {
        try { $process.Kill($true) } catch { }
        $process.WaitForExit()
    }
    [System.Threading.Tasks.Task]::WaitAll(@($stdoutTask, $stderrTask))
    return [pscustomobject]@{
        ExitCode = if ($timedOut) { -1 } else { $process.ExitCode }
        TimedOut = $timedOut
        StandardOutput = $stdoutTask.Result
        StandardError = $stderrTask.Result
    }
}

function Get-DotNetListedTests {
    param([Parameter(Mandatory)][string]$Output)

    $collect = $false
    $tests = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [regex]::Split($Output, '\r?\n')) {
        if ($line -match 'Tests are available\s*:|测试可用\s*:|Tests disponibles\s*:|Tests disponibles sont\s*:') {
            $collect = $true
            continue
        }
        if (-not $collect) { continue }
        if ($line -match '^\s{2,}\S') {
            $trimmed = $line.Trim()
            if ($trimmed -notmatch '^(Test Run|Total tests|Passed!|Failed!|警告|Warning)') {
                $tests.Add($trimmed)
            }
        }
    }
    return [string[]]@($tests)
}

function Get-NormalizedRunnerCases {
    param(
        [Parameter(Mandatory)][string[]]$Cases,
        [string]$WorkspaceRoot = $RepositoryRoot
    )

    if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
        throw "$ruleId-DISCOVERY runner-case normalization requires an explicit workspace root."
    }
    $rootWithForwardSlashes = $WorkspaceRoot.Replace('\', '/').TrimEnd('/')
    $quotedWorkspacePathPattern = '"' + [regex]::Escape($rootWithForwardSlashes) + '(?:/[^\"]*)?"'
    $pathRegexOptions = [Text.RegularExpressions.RegexOptions]::CultureInvariant
    if ($IsWindows) {
        $pathRegexOptions = $pathRegexOptions -bor [Text.RegularExpressions.RegexOptions]::IgnoreCase
    }
    $normalized = [string[]]@($Cases | ForEach-Object {
        $forwardSlashes = $_.Replace('\', '/')
        # xUnit truncates long display-name arguments before this policy sees them.
        # Only the current workspace root is unstable across runners. Root-relative
        # URLs, JSON pointers, protocol-relative URLs, external absolute values and
        # relative business data remain part of the case identity.
        $withoutWorkspacePaths = [regex]::Replace(
            $forwardSlashes,
            $quotedWorkspacePathPattern,
            '"<ABSOLUTE_PATH>"',
            $pathRegexOptions)
        $withoutWorkspacePaths.Normalize([Text.NormalizationForm]::FormC)
    })
    [Array]::Sort($normalized, [StringComparer]::Ordinal)
    return $normalized
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Get-NormalizedPath (Join-Path $PSScriptRoot '../..')
} else {
    $RepositoryRoot = Get-NormalizedPath $RepositoryRoot
}
if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $RepositoryRoot 'scripts/tests/baselines/aicopilot-test-governance.baseline.json'
}
if ([string]::IsNullOrWhiteSpace($WaiverPath)) {
    $WaiverPath = Join-Path $RepositoryRoot 'scripts/tests/baselines/aicopilot-test-governance.waivers.json'
}
$BaselinePath = Get-NormalizedPath $BaselinePath
$WaiverPath = Get-NormalizedPath $WaiverPath

if ($Mode -eq 'ValidateRunnerCaseNormalization') {
    $macCases = [string[]]@(
        'Fixture.Case(value: "z")',
        'Fixture.Golden(path: "/Users/example/work/AICopilot/src/t"···)',
        'Fixture.Case(relativePath: "../draft/report.md")',
        'Fixture.Case(value: "A")',
        'Fixture.Case(rootRelativeUrl: "/api/v1/devices")',
        'Fixture.Case(jsonPointer: "/items/0")',
        'Fixture.Case(protocolRelativeUrl: "//cdn.example/x")',
        'Fixture.Case(url: "https://example.test/api")',
        'Fixture.Case(externalPath: "/var/data/report.json")',
        'Fixture.Case(value: "a")',
        'Fixture.Case(value: "Ä")',
        'Fixture.Case(value: "A")'
    )
    $linuxCases = [string[]]@(
        'Fixture.Case(value: "Ä")',
        'Fixture.Case(externalPath: "/var/data/report.json")',
        'Fixture.Case(relativePath: "../draft/report.md")',
        'Fixture.Golden(path: "/home/runner/work/AICopilot/AICopilot/src/tests/AI"···)',
        'Fixture.Case(value: "A")',
        'Fixture.Case(url: "https://example.test/api")',
        'Fixture.Case(protocolRelativeUrl: "//cdn.example/x")',
        'Fixture.Case(value: "a")',
        'Fixture.Case(jsonPointer: "/items/0")',
        'Fixture.Case(value: "z")',
        'Fixture.Case(rootRelativeUrl: "/api/v1/devices")',
        'Fixture.Case(value: "A")'
    )
    $windowsCases = [string[]]@(
        'Fixture.Case(value: "A")',
        'Fixture.Case(rootRelativeUrl: "/api/v1/devices")',
        'Fixture.Case(value: "Ä")',
        'Fixture.Case(url: "https://example.test/api")',
        'Fixture.Golden(path: "C:\work\AICopilot\src\tests\AI"···)',
        'Fixture.Case(jsonPointer: "/items/0")',
        'Fixture.Case(value: "z")',
        'Fixture.Case(relativePath: "../draft/report.md")',
        'Fixture.Case(externalPath: "/var/data/report.json")',
        'Fixture.Case(value: "A")',
        'Fixture.Case(protocolRelativeUrl: "//cdn.example/x")',
        'Fixture.Case(value: "a")'
    )
    $expectedCases = [string[]]@(
        'Fixture.Case(externalPath: "/var/data/report.json")',
        'Fixture.Case(jsonPointer: "/items/0")',
        'Fixture.Case(protocolRelativeUrl: "//cdn.example/x")',
        'Fixture.Case(relativePath: "../draft/report.md")',
        'Fixture.Case(rootRelativeUrl: "/api/v1/devices")',
        'Fixture.Case(url: "https://example.test/api")',
        'Fixture.Case(value: "A")',
        'Fixture.Case(value: "A")',
        'Fixture.Case(value: "a")',
        'Fixture.Case(value: "z")',
        'Fixture.Case(value: "Ä")',
        'Fixture.Golden(path: "<ABSOLUTE_PATH>"···)'
    )
    $normalizedMacCases = @(Get-NormalizedRunnerCases -Cases $macCases -WorkspaceRoot '/Users/example/work/AICopilot')
    $normalizedLinuxCases = @(Get-NormalizedRunnerCases -Cases $linuxCases -WorkspaceRoot '/home/runner/work/AICopilot/AICopilot')
    $normalizedWindowsCases = @(Get-NormalizedRunnerCases -Cases $windowsCases -WorkspaceRoot 'C:\work\AICopilot')
    $expectedText = $expectedCases -join "`n"
    foreach ($actual in @($normalizedMacCases, $normalizedLinuxCases, $normalizedWindowsCases)) {
        if (($actual -join "`n") -cne $expectedText) {
            throw "$ruleId-DISCOVERY workspace-path normalization, ordinal ordering and business-value preservation must match the reviewed cross-OS sequence."
        }
    }
    if ($normalizedMacCases.Count -ne $macCases.Count -or
        @($normalizedMacCases | Where-Object { $_ -ceq 'Fixture.Case(value: "A")' }).Count -ne 2) {
        throw "$ruleId-DISCOVERY runner-case normalization must preserve exact duplicate multiplicity."
    }
    Write-Host 'AICopilot runner display-name normalization fixture passed.'
    exit 0
}

if ($Mode -eq 'ValidateRunnerConfiguration') {
    if ([string]::IsNullOrWhiteSpace($RunnerConfigPath)) {
        throw "$ruleId-DISABLED ValidateRunnerConfiguration requires RunnerConfigPath."
    }
    $runnerErrors = [System.Collections.Generic.List[string]]::new()
    Test-RunnerConfigurationFile -ResolvedRunnerConfigPath (Get-NormalizedPath $RunnerConfigPath) -Errors $runnerErrors -Context 'built test output'
    Assert-NoPolicyErrors -Errors $runnerErrors
    Write-Host "AICopilot failSkips runner configuration passed: $RunnerConfigPath"
    exit 0
}

if ($Mode -eq 'GenerateBaseline') {
    if (-not $AllowBaselineWrite) {
        throw "$ruleId-BASELINE baseline generation requires -AllowBaselineWrite and reviewed output."
    }
    if (-not [string]::IsNullOrWhiteSpace($env:CI)) {
        throw "$ruleId-BASELINE CI must never regenerate the reviewed baseline."
    }

    $specifications = if (-not [string]::IsNullOrWhiteSpace($ProjectPath) -or -not [string]::IsNullOrWhiteSpace($AssemblyPath)) {
        if ([string]::IsNullOrWhiteSpace($ProjectPath) -or [string]::IsNullOrWhiteSpace($ProjectName) -or [string]::IsNullOrWhiteSpace($AssemblyPath)) {
            throw "$ruleId-BASELINE single-project generation requires ProjectPath, ProjectName, and AssemblyPath."
        }
        @([pscustomobject]@{
            ProjectPath = Get-NormalizedPath $ProjectPath
            ProjectName = $ProjectName
            AssemblyPath = Get-NormalizedPath $AssemblyPath
        })
    } else {
        @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration)
    }

    $projects = [System.Collections.Generic.List[object]]::new()
    foreach ($specification in $specifications) {
        $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath $specification.ProjectPath -ResolvedProjectName $specification.ProjectName -ResolvedAssemblyPath $specification.AssemblyPath
        $projectFileForDiscovery = Get-RelativePath -BasePath $RepositoryRoot -Path $specification.ProjectPath
        $discoveryRun = Invoke-CapturedProcess -FileName 'dotnet' -Arguments @('test', $projectFileForDiscovery, '-c', $Configuration, '--no-build', '--no-restore', '--list-tests', '--nologo') -WorkingDirectory $RepositoryRoot
        if ($discoveryRun.TimedOut -or $discoveryRun.ExitCode -ne 0) {
            throw "$ruleId-DISCOVERY baseline generation could not list $($specification.ProjectName): $($discoveryRun.StandardError.Trim())"
        }
        $runnerCases = @(Get-NormalizedRunnerCases -Cases (Get-DotNetListedTests -Output $discoveryRun.StandardOutput))
        $policy = Get-GeneratedProjectPolicy -GeneratedProjectName $specification.ProjectName -GeneratedProjectPath $specification.ProjectPath
        $projects.Add([pscustomobject][ordered]@{
            projectPath = $snapshot.projectPath
            projectName = $snapshot.projectName
            isLegacy = $policy.isLegacy
            freezeMode = $policy.freezeMode
            frozenTypePatterns = $policy.frozenTypePatterns
            frozenSourceFiles = $policy.frozenSourceFiles
            frozenSourceHashes = $policy.frozenSourceHashes
            allowedNewTestKinds = $policy.allowedNewTestKinds
            allowedNewRuntimes = $policy.allowedNewRuntimes
            forbiddenNewTestKinds = $policy.forbiddenNewTestKinds
            discoveryCeilings = $policy.discoveryCeilings
            protectBaselineRemovals = $policy.protectBaselineRemovals
            baselineDeclarations = $snapshot.declarations
            baselineExecutionTemplates = $snapshot.executionTemplates
            baselineProjectedCases = $snapshot.projectedCases
            baselineRunnerCases = $runnerCases.Count
            runnerCaseDigest = ConvertTo-Sha256 -Value ($runnerCases -join "`n")
            tests = $snapshot.tests
        })
    }
    $projectPaths = [string[]]@($projects |
        Where-Object { $_.projectName -ne 'AICopilot.CloudAiReadLiveTests' } |
        ForEach-Object { $_.projectPath } |
        Sort-Object -Unique)
    $baseline = [pscustomobject][ordered]@{
        schemaVersion = $baselineSchemaVersion
        ruleId = $ruleId
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        provenance = [pscustomobject][ordered]@{
            sourceHead = $reviewedBaselineSourceHead
            baselineStatus = 'Reviewed'
            note = 'sourceHead identifies the independently reviewed source snapshot, not this governance commit. The baseline was generated after full local validation of commit 88a9687 and successful GitHub run 29170924668 with zero annotations.'
        }
        scanner = [pscustomobject]@{
            engine = 'System.Reflection.MetadataLoadContext'
            activeDotnetSdk = (& dotnet --version | Out-String).Trim()
            metadataLoadContextSha256 = (Get-FileHash (Get-ActiveSdkMetadataLoadContextPath) -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        allowedMetadata = [pscustomobject]@{
            testKinds = $allowedTestKinds
            runtimes = $allowedRuntimes
            risks = $allowedRisks
            owners = $allowedOwners
            capabilities = $allowedCapabilities
        }
        ciRequirements = @(
            [pscustomobject]@{
                workflowPath = '.github/workflows/aicopilot-ci.yml'
                requiredTestProjects = $projectPaths
                requiredCommandPrefixes = Get-CanonicalRequiredCommandPrefixes
            }
        )
        projects = [object[]]@($projects | Sort-Object projectPath)
    }
    Write-JsonAtomically -Value $baseline -Path $BaselinePath
    Write-Host "Generated reviewed baseline candidate: $BaselinePath"
    Write-Host "Projects: $($projects.Count)"
    Write-Host "Expanded declarations: $(($projects | Measure-Object -Property baselineDeclarations -Sum).Sum)"
    Write-Host "Execution templates: $(($projects | Measure-Object -Property baselineExecutionTemplates -Sum).Sum)"
    Write-Host "Projected cases: $(($projects | Measure-Object -Property baselineProjectedCases -Sum).Sum)"
    exit 0
}

if (-not (Test-Path $BaselinePath -PathType Leaf)) {
    throw "$ruleId-BASELINE baseline does not exist: $BaselinePath"
}
if (-not (Test-Path $WaiverPath -PathType Leaf)) {
    throw "$ruleId-WAIVER waiver manifest does not exist: $WaiverPath"
}
$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json -Depth 100
$waiverManifest = Get-Content $WaiverPath -Raw | ConvertFrom-Json -Depth 100
$errors = [System.Collections.Generic.List[string]]::new()

if ($Mode -eq 'ValidateSnapshot') {
    Test-BaselineStructure -Baseline $baseline -Errors $errors -AllowSyntheticPolicy
    Test-WaiverManifest -WaiverManifest $waiverManifest -Baseline $baseline -Errors $errors
    if (-not (Test-Path $CurrentSnapshotPath -PathType Leaf)) {
        Add-PolicyError -Errors $errors -Code "$ruleId-SCAN" -Message "snapshot does not exist: $CurrentSnapshotPath"
    } else {
        $snapshot = Get-Content $CurrentSnapshotPath -Raw | ConvertFrom-Json -Depth 100
        Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
    }
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Synthetic AICopilot test governance snapshot passed.'
    exit 0
}

if ($Mode -eq 'ValidateRepositorySnapshot') {
    Test-BaselineStructure -Baseline $baseline -Errors $errors -AllowSyntheticPolicy
    Test-WaiverManifest -WaiverManifest $waiverManifest -Baseline $baseline -Errors $errors
    if (-not (Test-Path $CurrentSnapshotPath -PathType Leaf)) {
        Add-PolicyError -Errors $errors -Code "$ruleId-SCAN" -Message "repository snapshot does not exist: $CurrentSnapshotPath"
    } else {
        $repositorySnapshot = Get-Content $CurrentSnapshotPath -Raw | ConvertFrom-Json -Depth 100
        $snapshotsByProject = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        foreach ($snapshot in @((Get-OptionalProperty $repositorySnapshot 'snapshots' @()))) {
            if ($snapshotsByProject.ContainsKey([string]$snapshot.projectPath)) {
                Add-PolicyError -Errors $errors -Code "$ruleId-SCAN" -Message "duplicate repository snapshot for '$($snapshot.projectPath)'."
                continue
            }
            $snapshotsByProject[[string]$snapshot.projectPath] = $snapshot
            Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
        }
        Test-RepositorySnapshotPolicies -Baseline $baseline -WaiverManifest $waiverManifest -SnapshotsByProject $snapshotsByProject -Errors $errors
    }
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Synthetic AICopilot repository migration snapshot passed.'
    exit 0
}

Test-StaticPolicy -Baseline $baseline -WaiverManifest $waiverManifest -Errors $errors
if ($Mode -eq 'ValidateStatic') {
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'AICopilot test governance static policy passed.'
    exit 0
}

if ($Mode -eq 'ValidateDiscovery') {
    foreach ($project in @($baseline.projects)) {
        $projectFile = Join-Path $RepositoryRoot $project.projectPath
        $specification = @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration | Where-Object { $_.ProjectPath -eq (Get-NormalizedPath $projectFile) })
        if ($specification.Count -ne 1) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) cannot resolve one built test assembly."
            continue
        }
        Test-RunnerConfigurationFile -ResolvedRunnerConfigPath $specification[0].RunnerConfigPath -Errors $errors -Context $project.projectName
        $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath $specification[0].ProjectPath -ResolvedProjectName $specification[0].ProjectName -ResolvedAssemblyPath $specification[0].AssemblyPath
        $arguments = @('test', $projectFile, '-c', $Configuration, '--no-build', '--no-restore', '--list-tests', '--nologo')
        $run = Invoke-CapturedProcess -FileName 'dotnet' -Arguments $arguments -WorkingDirectory $RepositoryRoot
        if ($run.TimedOut) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) list-tests exceeded the 120-second hard timeout."
            continue
        }
        if ($run.ExitCode -ne 0) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) list-tests failed: $($run.StandardError.Trim())."
            continue
        }
        $listedTests = @(Get-NormalizedRunnerCases -Cases (Get-DotNetListedTests -Output $run.StandardOutput))
        $runnerDigest = ConvertTo-Sha256 -Value ($listedTests -join "`n")
        if ($listedTests.Count -ne [int]$project.baselineRunnerCases -or $runnerDigest -ne [string]$project.runnerCaseDigest) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) runner discovery differs from the reviewed Release baseline: current=$($listedTests.Count), baseline=$($project.baselineRunnerCases)."
        } else {
            Write-Host "Discovery reconciliation $($project.projectName): declarations=$($snapshot.declarations), projected=$($snapshot.projectedCases), runner=$($listedTests.Count)"
        }
        foreach ($ceiling in @($project.discoveryCeilings)) {
            $filter = [string]$ceiling.displayNameContains
            $matchingTests = if ([string]::IsNullOrWhiteSpace($filter)) { $listedTests } else { @($listedTests | Where-Object { $_.Contains($filter, [StringComparison]::Ordinal) }) }
            if ($matchingTests.Count -eq 0) {
                Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) discovery ceiling '$filter' matched zero tests."
            } elseif ($matchingTests.Count -gt [int]$ceiling.maximum) {
                Add-PolicyError -Errors $errors -Code "$ruleId-FROZEN" -Message "$($project.projectName) discovery ceiling '$filter' grew to $($matchingTests.Count), maximum=$($ceiling.maximum)."
            } else {
                Write-Host "Discovery ceiling $($project.projectName) '$filter': $($matchingTests.Count)/$($ceiling.maximum)"
            }
        }
    }
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'AICopilot legacy discovery ceilings passed.'
    exit 0
}

if ($Mode -eq 'ValidateProject') {
    if ([string]::IsNullOrWhiteSpace($ProjectPath) -or [string]::IsNullOrWhiteSpace($ProjectName) -or [string]::IsNullOrWhiteSpace($AssemblyPath)) {
        throw "$ruleId-SCAN ValidateProject requires ProjectPath, ProjectName, and AssemblyPath."
    }
    $additionalReferencePaths = if ([string]::IsNullOrWhiteSpace($ReferencePathsFile)) {
        @()
    } elseif (-not (Test-Path $ReferencePathsFile -PathType Leaf)) {
        throw "$ruleId-SCAN reference-path response file does not exist: $ReferencePathsFile"
    } else {
        [string[]]@(Get-Content $ReferencePathsFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath (Get-NormalizedPath $ProjectPath) -ResolvedProjectName $ProjectName -ResolvedAssemblyPath (Get-NormalizedPath $AssemblyPath) -AdditionalReferencePaths $additionalReferencePaths
    Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
    Assert-NoPolicyErrors -Errors $errors
    Write-Host "AICopilot test governance assembly policy passed: $ProjectName"
    exit 0
}

if ($Mode -eq 'ValidateRepository') {
    $snapshotsByProject = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($specification in @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration)) {
        Test-RunnerConfigurationFile -ResolvedRunnerConfigPath $specification.RunnerConfigPath -Errors $errors -Context $specification.ProjectName
        $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath $specification.ProjectPath -ResolvedProjectName $specification.ProjectName -ResolvedAssemblyPath $specification.AssemblyPath
        $snapshotsByProject[[string]$snapshot.projectPath] = $snapshot
        Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
    }
    Test-RepositorySnapshotPolicies -Baseline $baseline -WaiverManifest $waiverManifest -SnapshotsByProject $snapshotsByProject -Errors $errors
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'AICopilot test governance repository policy passed.'
    exit 0
}

throw "$ruleId unsupported mode '$Mode'."
