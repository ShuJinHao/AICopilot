[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path $RepositoryRoot).Path
$selector = Join-Path $root 'scripts/tests/Select-AICopilotCiTests.ps1'
$allowedCategories = @('Architecture', 'Security', 'Business', 'DeploymentContract', 'Quality', 'CrossProject')
function Assert-ValidCategories([object]$Selection) {
    $invalid = @(@($Selection.selectedDotNetProjects) |
        ForEach-Object { @($_.categories) } |
        Where-Object { $_ -notin $allowedCategories })
    if ($invalid.Count -gt 0) {
        throw "Selector emitted non-canonical categories: $($invalid -join ', ')"
    }
}

function Write-FixtureFile {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $fullPath = Join-Path $Root $Path
    [void](New-Item (Split-Path $fullPath -Parent) -ItemType Directory -Force)
    [IO.File]::WriteAllText(
        $fullPath,
        $Content.Trim() + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function Set-FixtureSolution {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$BusinessProjectPaths
    )

    $businessProjects = @($BusinessProjectPaths | Sort-Object | ForEach-Object {
            "  <Project Path=`"$_`" />"
        }) -join [Environment]::NewLine
    Write-FixtureFile -Root $Root -Path 'AICopilot.slnx' -Content @"
<Solution>
  <Project Path="src/core/AICopilot.Product/AICopilot.Product.csproj" />
  <Project Path="src/core/AICopilot.Other/AICopilot.Other.csproj" />
  <Project Path="src/tests/AICopilot.Architecture/AICopilot.Architecture.csproj" />
$businessProjects
  <Project Path="src/tests/AICopilot.Business.Other/AICopilot.Business.Other.csproj" />
</Solution>
"@
}

function New-DynamicRunnerFixture {
    param(
        [Parameter(Mandatory)][string]$Root,
        [switch]$IncludeRemainingBusiness,
        [switch]$UnownedLegacy
    )

    Write-FixtureFile -Root $Root -Path 'src/core/AICopilot.Product/AICopilot.Product.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
'@
    Write-FixtureFile -Root $Root -Path 'src/core/AICopilot.Other/AICopilot.Other.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
'@
    Write-FixtureFile -Root $Root -Path 'src/tests/AICopilot.Architecture/AICopilot.Architecture.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <AICopilotTestKind>Architecture</AICopilotTestKind>
    <AICopilotTestRuntime>Pure</AICopilotTestRuntime>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../core/AICopilot.Product/AICopilot.Product.csproj" />
  </ItemGroup>
</Project>
'@
    Write-FixtureFile -Root $Root -Path 'src/tests/AICopilot.Business.Legacy/AICopilot.Business.Legacy.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <AICopilotTestKind>Unit</AICopilotTestKind>
    <AICopilotTestRuntime>Pure</AICopilotTestRuntime>
    <AICopilotTestOwner>ProductBusiness</AICopilotTestOwner>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../core/AICopilot.Product/AICopilot.Product.csproj" />
  </ItemGroup>
</Project>
'@
    if ($UnownedLegacy) {
        Write-FixtureFile -Root $Root -Path 'src/tests/AICopilot.Business.Legacy/AICopilot.Business.Legacy.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <AICopilotTestKind>Unit</AICopilotTestKind>
    <AICopilotTestRuntime>Pure</AICopilotTestRuntime>
  </PropertyGroup>
</Project>
'@
    }
    Write-FixtureFile -Root $Root -Path 'src/tests/AICopilot.Business.Legacy/LegacyTests.cs' -Content 'internal sealed class LegacyTests { }'
    Write-FixtureFile -Root $Root -Path 'src/tests/AICopilot.Business.Other/AICopilot.Business.Other.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <AICopilotTestKind>Unit</AICopilotTestKind>
    <AICopilotTestRuntime>Pure</AICopilotTestRuntime>
    <AICopilotTestOwner>OtherBusiness</AICopilotTestOwner>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../core/AICopilot.Other/AICopilot.Other.csproj" />
  </ItemGroup>
</Project>
'@
    $businessProjectPaths = [Collections.Generic.List[string]]::new()
    $businessProjectPaths.Add(
        'src/tests/AICopilot.Business.Legacy/AICopilot.Business.Legacy.csproj')
    if ($IncludeRemainingBusiness) {
        Write-FixtureFile -Root $Root -Path 'src/tests/AICopilot.Business.Remaining/AICopilot.Business.Remaining.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <AICopilotTestKind>Unit</AICopilotTestKind>
    <AICopilotTestRuntime>Pure</AICopilotTestRuntime>
    <AICopilotTestOwner>ProductBusiness</AICopilotTestOwner>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../core/AICopilot.Product/AICopilot.Product.csproj" />
  </ItemGroup>
</Project>
'@
        $businessProjectPaths.Add(
            'src/tests/AICopilot.Business.Remaining/AICopilot.Business.Remaining.csproj')
    }
    Set-FixtureSolution `
        -Root $Root `
        -BusinessProjectPaths @($businessProjectPaths)

    & git -C $Root init -q
    if ($LASTEXITCODE -ne 0) { throw 'Failed to initialize AICopilot selector fixture repository.' }
    & git -C $Root add .
    if ($LASTEXITCODE -ne 0) { throw 'Failed to stage AICopilot selector fixture repository.' }
    & git -C $Root -c user.name=selector-fixture -c user.email=selector@example.invalid `
        commit -q -m baseline
    if ($LASTEXITCODE -ne 0) { throw 'Failed to commit AICopilot selector fixture baseline.' }
    return ((& git -C $Root rev-parse HEAD) -join '').Trim()
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "aicopilot-ci-selector-$([Guid]::NewGuid().ToString('N'))"
[void](New-Item $temporaryRoot -ItemType Directory -Force)
try {
    $positiveOutput = Join-Path $temporaryRoot 'positive.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @('src/core/AICopilot.Core.AiGateway/Aggregates/Sessions/Session.cs') `
        -OutputPath $positiveOutput `
        -GitHubOutputPath ''
    $positive = Get-Content $positiveOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $positive
    $positiveNames = @($positive.selectedDotNetProjects.projectName)
    if ($positiveNames -notcontains 'AICopilot.ArchitectureTests' -or
        $positiveNames -notcontains 'AICopilot.Architecture.AnalyzerTests' -or
        $positiveNames -notcontains 'AICopilot.AnalyzerFixtureTests' -or
        $positiveNames -notcontains 'AICopilot.AggregateTests') {
        throw "Positive selector fixture omitted mandatory or affected projects: $($positiveNames -join ', ')"
    }
    foreach ($fastSecurityName in @(
            'AICopilot.UnitTests',
            'AICopilot.InProcessTests',
            'AICopilot.PersistenceFilesystemTests')) {
        $fastSecurity = @($positive.selectedDotNetProjects |
            Where-Object projectName -ceq $fastSecurityName)
        if ($fastSecurity.Count -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$fastSecurity[0].testFilter) -or
            @($fastSecurity[0].categories).Count -ne 1 -or
            @($fastSecurity[0].categories) -notcontains 'Security') {
            throw "Default source selection omitted filtered fast Security: $fastSecurityName"
        }
    }
    if ($positiveNames -contains 'AICopilot.GoldenEvalTests' -or
        $positiveNames -contains 'AICopilot.EndToEndTests' -or
        $positiveNames -contains 'AICopilot.HttpIntegrationTests' -or
        $positiveNames -contains 'AICopilot.PersistenceTests' -or
        [bool]$positive.requiresDocker -or
        -not [bool]$positive.productionBuildRequired -or
        @($positive.matchedSecurityImpactRules).Count -ne 0) {
        throw 'Generic source selection included an explicit Quality or heavy Security project.'
    }

    $docsOutput = Join-Path $temporaryRoot 'docs.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @(
            'docs/example.md',
            '资料/历史说明.md',
            'CLAUDE.md',
            'AICopilot 项目部署与维护指南.md') `
        -OutputPath $docsOutput `
        -GitHubOutputPath ''
    $docs = Get-Content $docsOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $docs
    if (@($docs.unclassifiedFiles).Count -ne 0 -or
        @($docs.selectedDotNetProjects).Count -ne 0 -or
        @($docs.requiredExplicitModes).Count -ne 0 -or
        [bool]$docs.dotNetAffected -or
        [bool]$docs.productionBuildRequired -or
        [bool]$docs.requiresDocker) {
        throw 'Ordinary documentation-only changes did not skip .NET build and test work.'
    }

    $webManifestFiles = @(
        'src/vues/AICopilot.Web/package-lock.json',
        'src/vues/AICopilot.Web/package.json')
    $webManifestOutput = Join-Path $temporaryRoot 'web-manifest.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles $webManifestFiles `
        -OutputPath $webManifestOutput `
        -GitHubOutputPath ''
    $webManifest = Get-Content $webManifestOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $webManifest
    if (-not [bool]$webManifest.web.affected -or
        [bool]$webManifest.web.full -or
        @(Compare-Object $webManifestFiles @($webManifest.web.changedFiles)).Count -ne 0 -or
        @($webManifest.unclassifiedFiles).Count -ne 0 -or
        @($webManifest.requiredExplicitModes).Count -ne 0 -or
        [bool]$webManifest.productionBuildRequired -or
        [bool]$webManifest.deploymentAffected) {
        throw 'Web package manifests did not stay in the affected Web lane.'
    }

    foreach ($contractPath in @(
            'AGENTS.md',
            'docs/AICopilot业务规则.md',
            'docs/AICopilot安全部署契约.md',
            'docs/AI架构路线图.md',
            'docs/Agent工作流与异常契约.md',
            'docs/Cloud只读数据分析契约.md',
            'docs/DDD聚合根边界.md')) {
        $contractOutput = Join-Path $temporaryRoot "$([IO.Path]::GetFileName($contractPath)).json"
        & $selector `
            -RepositoryRoot $root `
            -ChangedFiles @($contractPath) `
            -OutputPath $contractOutput `
            -GitHubOutputPath ''
        $contract = Get-Content $contractOutput -Raw | ConvertFrom-Json
        Assert-ValidCategories $contract
        $contractProjects = @($contract.selectedDotNetProjects)
        $contractFilesystem = @($contractProjects |
            Where-Object projectName -ceq 'AICopilot.ContractFilesystemTests')
        $expectedProjectCount = if ($contractPath -ceq 'docs/AICopilot安全部署契约.md') {
            2
        } else {
            1
        }
        if ($contractProjects.Count -ne $expectedProjectCount -or
            $contractFilesystem.Count -ne 1 -or
            @($contractFilesystem[0].categories).Count -ne 1 -or
            @($contractFilesystem[0].categories) -notcontains 'Business' -or
            -not [bool]$contract.dotNetAffected -or
            [bool]$contract.productionBuildRequired -or
            [bool]$contract.requiresDocker) {
            throw "Active AI contract did not select its filesystem contract lane: $contractPath"
        }
        if ($contractPath -ceq 'docs/AICopilot安全部署契约.md' -and
            (-not [bool]$contract.deploymentAffected -or
             @($contractProjects.projectName) -notcontains 'AICopilot.DeploymentTests')) {
            throw 'AICopilot deployment contract did not select the deployment filesystem lane.'
        }
    }

    $identityOutput = Join-Path $temporaryRoot 'identity-security.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @(
            'src/services/AICopilot.IdentityService/Commands/BindCloudIdentityCommand.cs') `
        -OutputPath $identityOutput `
        -GitHubOutputPath ''
    $identity = Get-Content $identityOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $identity
    foreach ($heavyName in @(
            'AICopilot.PersistenceTests',
            'AICopilot.HttpIntegrationTests')) {
        $heavySelection = @($identity.selectedDotNetProjects |
            Where-Object projectName -ceq $heavyName)
        if ($heavySelection.Count -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$heavySelection[0].testFilter) -or
            @($heavySelection[0].categories).Count -ne 1 -or
            @($heavySelection[0].categories) -notcontains 'Security') {
            throw "Identity impact did not select the filtered heavy Security project: $heavyName"
        }
    }
    if (-not [bool]$identity.requiresDocker -or
        @($identity.matchedSecurityImpactRules) -notcontains 'identity-persistence' -or
        @($identity.matchedSecurityImpactRules) -notcontains 'identity-http') {
        throw 'Identity path/owner mapping did not emit its Security impact evidence.'
    }

    $identitySecurityTestOutput = Join-Path $temporaryRoot 'identity-security-test.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @(
            'src/tests/AICopilot.PersistenceTests/CloudOidcBindingConcurrencyTests.cs') `
        -OutputPath $identitySecurityTestOutput `
        -GitHubOutputPath ''
    $identitySecurityTest = Get-Content $identitySecurityTestOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $identitySecurityTest
    $identityPersistence = @($identitySecurityTest.selectedDotNetProjects |
        Where-Object projectName -ceq 'AICopilot.PersistenceTests')
    if ($identityPersistence.Count -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$identityPersistence[0].testFilter) -or
        @($identityPersistence[0].categories).Count -ne 1 -or
        @($identityPersistence[0].categories) -notcontains 'Security' -or
        @($identitySecurityTest.requiredExplicitModes).Count -ne 0) {
        throw 'Identity Security test change did not retain the project-owned Security filter.'
    }

    $securityProjectFileOutput = Join-Path $temporaryRoot 'security-project-file.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @(
            'src/tests/AICopilot.UnitTests/AICopilot.UnitTests.csproj') `
        -OutputPath $securityProjectFileOutput `
        -GitHubOutputPath ''
    $securityProjectFile = Get-Content $securityProjectFileOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $securityProjectFile
    $unitProject = @($securityProjectFile.selectedDotNetProjects |
        Where-Object projectName -ceq 'AICopilot.UnitTests')
    if ($unitProject.Count -ne 1 -or
        -not [string]::IsNullOrWhiteSpace([string]$unitProject[0].testFilter) -or
        @($unitProject[0].categories) -notcontains 'Business' -or
        @($unitProject[0].reasons) -notcontains
            'affected-test:src/tests/AICopilot.UnitTests/AICopilot.UnitTests.csproj' -or
        @($securityProjectFile.requiredExplicitModes).Count -ne 0) {
        throw 'Security-capable test project file change did not retain its unfiltered Business owner lane.'
    }

    $aspireTestKitOutput = Join-Path $temporaryRoot 'aspire-test-kit.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @(
            'src/testing/AICopilot.AspireIntegrationTestKit/FakeCloudOidcProviderHost.cs') `
        -OutputPath $aspireTestKitOutput `
        -GitHubOutputPath ''
    $aspireTestKit = Get-Content $aspireTestKitOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $aspireTestKit
    $aspireHttp = @($aspireTestKit.selectedDotNetProjects |
        Where-Object projectName -ceq 'AICopilot.HttpIntegrationTests')
    if ($aspireHttp.Count -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$aspireHttp[0].testFilter) -or
        @($aspireHttp[0].categories).Count -ne 1 -or
        @($aspireHttp[0].categories) -notcontains 'Security' -or
        @($aspireTestKit.selectedDotNetProjects.categories) -contains 'Quality' -or
        @($aspireTestKit.requiredExplicitModes).Count -ne 0) {
        throw 'Aspire TestKit change did not execute its filtered Security dependent without expanding to Quality or Full.'
    }

    $agentOutput = Join-Path $temporaryRoot 'harness-security.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @(
            'src/services/AICopilot.AiGatewayService/Agents/ChatStreamHandler.cs') `
        -OutputPath $agentOutput `
        -GitHubOutputPath ''
    $agent = Get-Content $agentOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $agent
    $agentHttp = @($agent.selectedDotNetProjects |
        Where-Object projectName -ceq 'AICopilot.HttpIntegrationTests')
    if ($agentHttp.Count -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$agentHttp[0].testFilter) -or
        @($agentHttp[0].categories) -notcontains 'Security' -or
        @($agent.selectedDotNetProjects.projectName) -contains 'AICopilot.PersistenceTests' -or
        @($agent.matchedSecurityImpactRules).Count -ne 1 -or
        @($agent.matchedSecurityImpactRules) -notcontains 'harness-http') {
        throw 'Harness path/owner mapping did not isolate the expected Security filter.'
    }

    foreach ($securityCase in @(
            [pscustomobject]@{
                Id = 'mcp-http'
                Path = 'src/services/AICopilot.McpService/McpServers/McpServerManagement.cs'
            },
            [pscustomobject]@{
                Id = 'model-secret-http'
                Path = 'src/core/AICopilot.Core.AiGateway/Aggregates/LanguageModel/LanguageModel.cs'
            })) {
        $caseOutput = Join-Path $temporaryRoot "$($securityCase.Id).json"
        & $selector `
            -RepositoryRoot $root `
            -ChangedFiles @($securityCase.Path) `
            -OutputPath $caseOutput `
            -GitHubOutputPath ''
        $caseSelection = Get-Content $caseOutput -Raw | ConvertFrom-Json
        Assert-ValidCategories $caseSelection
        $caseHttp = @($caseSelection.selectedDotNetProjects |
            Where-Object projectName -ceq 'AICopilot.HttpIntegrationTests')
        if ($caseHttp.Count -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$caseHttp[0].testFilter) -or
            @($caseHttp[0].categories) -notcontains 'Security' -or
            @($caseSelection.matchedSecurityImpactRules).Count -ne 1 -or
            @($caseSelection.matchedSecurityImpactRules) -notcontains $securityCase.Id) {
            throw "Security path/owner mapping failed: $($securityCase.Id)"
        }
    }

    [xml]$productionSolution = Get-Content (
        Join-Path $root 'AICopilot.Production.slnx') -Raw
    $productionProjects = @($productionSolution.SelectNodes("//*[local-name()='Project']") |
        ForEach-Object { ([string]$_.GetAttribute('Path')).Replace('\', '/') } |
        Sort-Object -Unique)
    $expectedProductionProjects = @(Get-ChildItem (Join-Path $root 'src') `
            -Filter '*.csproj' -File -Recurse |
        ForEach-Object {
            [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
        } |
        Where-Object {
            -not $_.StartsWith('src/tests/', [StringComparison]::Ordinal) -and
            -not $_.StartsWith('src/testing/', [StringComparison]::Ordinal)
        } |
        Sort-Object -Unique)
    if (@(Compare-Object $expectedProductionProjects $productionProjects).Count -ne 0 -or
        @($productionProjects | Where-Object {
                $_.StartsWith('src/tests/', [StringComparison]::Ordinal) -or
                $_.StartsWith('src/testing/', [StringComparison]::Ordinal)
            }).Count -ne 0) {
        throw 'AICopilot production project graph is incomplete or includes test infrastructure.'
    }

    $unicodeRoot = Join-Path $temporaryRoot 'unicode-paths'
    $unicodeBase = New-DynamicRunnerFixture -Root $unicodeRoot
    & git -C $unicodeRoot config core.quotePath true
    if ($LASTEXITCODE -ne 0) { throw 'Failed to enable C-style Git path quoting in the Unicode fixture.' }
    $unicodePath = 'docs/中文 路径说明.md'
    Write-FixtureFile -Root $unicodeRoot -Path $unicodePath -Content '# Unicode path fixture'
    & git -C $unicodeRoot add .
    if ($LASTEXITCODE -ne 0) { throw 'Failed to stage the Unicode path fixture.' }
    & git -C $unicodeRoot -c user.name=selector-fixture -c user.email=selector@example.invalid `
        commit -q -m unicode-path
    if ($LASTEXITCODE -ne 0) { throw 'Failed to commit the Unicode path fixture.' }
    Import-Module (Join-Path $root 'scripts/tests/AICopilotGitPaths.psm1') -Force
    $unicodeChanged = @(Get-AICopilotGitChangedFiles `
            -RepositoryRoot $unicodeRoot `
            -BaseRef $unicodeBase `
            -HeadRef HEAD)
    if ($unicodeChanged.Count -ne 1 -or $unicodeChanged[0] -cne $unicodePath) {
        throw "NUL-delimited Git path discovery corrupted a Unicode path: $($unicodeChanged -join ', ')"
    }
    $unicodeOutput = Join-Path $temporaryRoot 'unicode.json'
    & $selector `
        -RepositoryRoot $unicodeRoot `
        -BaseRef $unicodeBase `
        -HeadRef HEAD `
        -OutputPath $unicodeOutput `
        -GitHubOutputPath ''
    $unicode = Get-Content $unicodeOutput -Raw | ConvertFrom-Json
    if (@($unicode.changedFiles).Count -ne 1 -or
        @($unicode.changedFiles)[0] -cne $unicodePath -or
        @($unicode.selectedDotNetProjects).Count -ne 0) {
        throw 'Selector did not preserve the exact Unicode Git path from discovery to evidence.'
    }

    $globalBuildOutput = Join-Path $temporaryRoot 'global-build.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @('Directory.Build.targets') `
        -OutputPath $globalBuildOutput `
        -GitHubOutputPath ''
    $globalBuild = Get-Content $globalBuildOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $globalBuild
    $globalBuildCategories = @($globalBuild.selectedDotNetProjects.categories |
        Sort-Object -Unique)
    if (@($globalBuild.unclassifiedFiles).Count -ne 0 -or
        -not [bool]$globalBuild.deploymentAffected -or
        $globalBuildCategories -notcontains 'Business' -or
        $globalBuildCategories -notcontains 'DeploymentContract' -or
        @($globalBuildCategories | Where-Object {
                $_ -notin @('Architecture', 'Security', 'Business', 'DeploymentContract')
            }).Count -ne 0) {
        throw 'Directory.Build.targets did not select all automatic release lanes while deferring explicit-only lanes.'
    }

    $manualOutput = Join-Path $temporaryRoot 'manual.json'
    & $selector `
        -RepositoryRoot $root `
        -Mode Quality `
        -OutputPath $manualOutput `
        -GitHubOutputPath ''
    $manual = Get-Content $manualOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $manual
    if ([string]$manual.mode -cne 'Quality' -or
        @($manual.unclassifiedFiles).Count -ne 0 -or
        @($manual.selectedDotNetProjects.categories) -notcontains 'Quality' -or
        @($manual.selectedDotNetProjects.categories) -contains 'Business' -or
        @($manual.selectedDotNetProjects.categories) -contains 'DeploymentContract') {
        throw 'Explicit Quality selection did not stay within red-line plus Quality categories.'
    }
    $qualityHttp = @($manual.selectedDotNetProjects |
        Where-Object projectName -eq 'AICopilot.HttpIntegrationTests')
    if ($qualityHttp.Count -ne 1 -or
        -not [string]::IsNullOrWhiteSpace([string]$qualityHttp[0].testFilter) -or
        @($qualityHttp[0].categories) -notcontains 'Quality') {
        throw 'Full HttpIntegration was not confined to explicit Quality mode.'
    }

    $deploymentOutput = Join-Path $temporaryRoot 'deployment.json'
    & $selector -RepositoryRoot $root -Mode Deployment -ChangedFiles @(
        'src/core/AICopilot.Core.AiGateway/Aggregates/Sessions/Session.cs',
        'deploy/Deploy.ps1') `
        -OutputPath $deploymentOutput -GitHubOutputPath ''
    $deployment = Get-Content $deploymentOutput -Raw | ConvertFrom-Json
    if ([string]$deployment.mode -cne 'Deployment' -or
        -not [bool]$deployment.deploymentAffected -or
        @($deployment.selectedDotNetProjects.categories) -notcontains 'DeploymentContract' -or
        @($deployment.selectedDotNetProjects.categories | Where-Object {
                $_ -notin @('Architecture', 'Security', 'DeploymentContract')
            }).Count -ne 0) {
        throw 'Deployment changes did not select only the affected DeploymentContract lane.'
    }

    $deferredOutput = Join-Path $temporaryRoot 'deferred.json'
    & $selector -RepositoryRoot $root -ChangedFiles @(
        'src/tests/AICopilot.GoldenEvalTests/AICopilot.GoldenEvalTests.csproj') `
        -OutputPath $deferredOutput -GitHubOutputPath ''
    $deferred = Get-Content $deferredOutput -Raw | ConvertFrom-Json
    if (@($deferred.deferredExplicitFiles) -notcontains
        'Quality:src/tests/AICopilot.GoldenEvalTests/AICopilot.GoldenEvalTests.csproj' -or
        @($deferred.selectedDotNetProjects.categories) -contains 'Quality') {
        throw 'Known Quality changes were not deferred from automatic CI.'
    }

    $dynamicRoot = Join-Path $temporaryRoot 'dynamic-runner'
    $dynamicBase = New-DynamicRunnerFixture -Root $dynamicRoot
    $legacyRoot = Join-Path $dynamicRoot 'src/tests/AICopilot.Business.Legacy'
    $currentRoot = Join-Path $dynamicRoot 'src/tests/AICopilot.Business.Current'
    Move-Item -LiteralPath $legacyRoot -Destination $currentRoot
    Move-Item `
        -LiteralPath (Join-Path $currentRoot 'AICopilot.Business.Legacy.csproj') `
        -Destination (Join-Path $currentRoot 'AICopilot.Business.Current.csproj')
    Move-Item `
        -LiteralPath (Join-Path $currentRoot 'LegacyTests.cs') `
        -Destination (Join-Path $currentRoot 'CurrentTests.cs')
    Set-FixtureSolution `
        -Root $dynamicRoot `
        -BusinessProjectPaths @(
            'src/tests/AICopilot.Business.Current/AICopilot.Business.Current.csproj')

    $dynamicChangedFiles = @(
        'AICopilot.slnx',
        'src/tests/AICopilot.Business.Legacy/AICopilot.Business.Legacy.csproj',
        'src/tests/AICopilot.Business.Legacy/LegacyTests.cs',
        'src/tests/AICopilot.Business.Current/AICopilot.Business.Current.csproj',
        'src/tests/AICopilot.Business.Current/CurrentTests.cs')
    $dynamicOutput = Join-Path $temporaryRoot 'dynamic.json'
    & $selector `
        -RepositoryRoot $dynamicRoot `
        -BaseRef $dynamicBase `
        -ChangedFiles $dynamicChangedFiles `
        -OutputPath $dynamicOutput `
        -GitHubOutputPath ''
    $dynamic = Get-Content $dynamicOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $dynamic
    $dynamicNames = @($dynamic.selectedDotNetProjects.projectName)
    if ($dynamicNames -notcontains 'AICopilot.Business.Current' -or
        $dynamicNames -contains 'AICopilot.Business.Legacy' -or
        $dynamicNames -contains 'AICopilot.Business.Other' -or
        @($dynamic.selectedDotNetProjects.categories | Where-Object {
                $_ -notin @('Architecture', 'Business')
            }).Count -ne 0 -or
        @($dynamic.unclassifiedFiles).Count -ne 0 -or
        @($dynamic.requiredExplicitModes) -contains 'Full' -or
        @($dynamic.retiredBusinessProjects) -notcontains
            'src/tests/AICopilot.Business.Legacy/AICopilot.Business.Legacy.csproj') {
        throw 'Business runner add/delete/migration did not stay dynamically scoped to affected Business.'
    }

    $dynamicDeploymentOutput = Join-Path $temporaryRoot 'dynamic-deployment.json'
    & $selector `
        -RepositoryRoot $dynamicRoot `
        -Mode Deployment `
        -BaseRef $dynamicBase `
        -ChangedFiles $dynamicChangedFiles `
        -OutputPath $dynamicDeploymentOutput `
        -GitHubOutputPath ''
    $dynamicDeployment = Get-Content $dynamicDeploymentOutput -Raw | ConvertFrom-Json
    if (@($dynamicDeployment.unclassifiedFiles).Count -ne 0 -or
        @($dynamicDeployment.requiredExplicitModes) -contains 'Full' -or
        @($dynamicDeployment.selectedDotNetProjects |
            ForEach-Object { @($_.categories) }) -contains 'Business' -or
        @($dynamicDeployment.retiredBusinessProjects) -notcontains
            'src/tests/AICopilot.Business.Legacy/AICopilot.Business.Legacy.csproj') {
        throw 'Deployment mode did not defer a baseline-attributed Business runner migration.'
    }

    $retirementRoot = Join-Path $temporaryRoot 'retired-runner'
    $retirementBase = New-DynamicRunnerFixture `
        -Root $retirementRoot `
        -IncludeRemainingBusiness
    Remove-Item (Join-Path $retirementRoot 'src/tests/AICopilot.Business.Legacy') `
        -Recurse `
        -Force
    Set-FixtureSolution `
        -Root $retirementRoot `
        -BusinessProjectPaths @(
            'src/tests/AICopilot.Business.Remaining/AICopilot.Business.Remaining.csproj')
    $retirementOutput = Join-Path $temporaryRoot 'retired-runner.json'
    & $selector `
        -RepositoryRoot $retirementRoot `
        -BaseRef $retirementBase `
        -ChangedFiles @(
            'AICopilot.slnx',
            'src/tests/AICopilot.Business.Legacy/AICopilot.Business.Legacy.csproj',
            'src/tests/AICopilot.Business.Legacy/LegacyTests.cs') `
        -OutputPath $retirementOutput `
        -GitHubOutputPath ''
    $retirement = Get-Content $retirementOutput -Raw | ConvertFrom-Json
    $retirementBusinessNames = @($retirement.selectedDotNetProjects |
        Where-Object { @($_.categories) -contains 'Business' } |
        Select-Object -ExpandProperty projectName)
    if ($retirementBusinessNames.Count -ne 1 -or
        $retirementBusinessNames -notcontains 'AICopilot.Business.Remaining' -or
        $retirementBusinessNames -contains 'AICopilot.Business.Other' -or
        @($retirement.unclassifiedFiles).Count -ne 0 -or
        @($retirement.requiredExplicitModes) -contains 'Full') {
        throw "Deleted Business runner did not select only its surviving owner scope: $($retirementBusinessNames -join ', ')"
    }

    $unownedRetirementRoot = Join-Path $temporaryRoot 'unowned-retired-runner'
    $unownedRetirementBase = New-DynamicRunnerFixture `
        -Root $unownedRetirementRoot `
        -UnownedLegacy
    Remove-Item (Join-Path $unownedRetirementRoot 'src/tests/AICopilot.Business.Legacy') `
        -Recurse `
        -Force
    Set-FixtureSolution -Root $unownedRetirementRoot -BusinessProjectPaths @()
    $unownedRetirementOutput = Join-Path $temporaryRoot 'unowned-retired-runner.json'
    $unownedRetirementFailed = $false
    try {
        & $selector `
            -RepositoryRoot $unownedRetirementRoot `
            -BaseRef $unownedRetirementBase `
            -ChangedFiles @(
                'AICopilot.slnx',
                'src/tests/AICopilot.Business.Legacy/AICopilot.Business.Legacy.csproj',
                'src/tests/AICopilot.Business.Legacy/LegacyTests.cs') `
            -OutputPath $unownedRetirementOutput `
            -GitHubOutputPath ''
    } catch {
        $unownedRetirementFailed = $_.Exception.Message -match 'cannot safely attribute'
    }
    if (-not $unownedRetirementFailed) {
        throw 'Deleted Business runner without baseline owner evidence did not fail closed.'
    }
    $unownedRetirement = Get-Content $unownedRetirementOutput -Raw | ConvertFrom-Json
    if (@($unownedRetirement.unclassifiedFiles) -notcontains 'AICopilot.slnx' -or
        @($unownedRetirement.requiredExplicitModes) -notcontains 'Full') {
        throw 'Unowned deleted Business runner did not preserve fail-closed evidence.'
    }

    $crossOutput = Join-Path $temporaryRoot 'cross.json'
    & $selector -RepositoryRoot $root -Mode CrossProject -ChangedFiles @() `
        -OutputPath $crossOutput -GitHubOutputPath ''
    $cross = Get-Content $crossOutput -Raw | ConvertFrom-Json
    if (@($cross.selectedDotNetProjects).Count -ne 1 -or
        @($cross.selectedDotNetProjects.categories | Where-Object { $_ -cne 'CrossProject' }).Count -ne 0) {
        throw 'CrossProject mode emitted a non-cross-project runner.'
    }

    if ((Get-Content $selector -Raw).Contains('aicopilot-test-classification.json', [StringComparison]::Ordinal)) {
        throw 'AICopilot selector still reads the retired historical classification inventory.'
    }

    $negativeOutput = Join-Path $temporaryRoot 'negative.json'
    $negativeFailed = $false
    try {
        & $selector `
            -RepositoryRoot $root `
            -ChangedFiles @('src/Unowned.Business/Unknown.cs') `
            -OutputPath $negativeOutput `
            -GitHubOutputPath ''
    } catch {
        $negativeFailed = $_.Exception.Message -match 'cannot safely attribute' -and
            $_.Exception.Message -match 'src/Unowned\.Business/Unknown\.cs'
    }
    if (-not $negativeFailed) {
        throw 'Unknown business path did not fail closed with the file listed.'
    }
    $negative = Get-Content $negativeOutput -Raw | ConvertFrom-Json
    if (@($negative.unclassifiedFiles) -notcontains 'src/Unowned.Business/Unknown.cs') {
        throw 'Unknown business path is absent from selector evidence.'
    }
} finally {
    if (Test-Path $temporaryRoot) {
        Remove-Item $temporaryRoot -Recurse -Force
    }
}

$workflowText = Get-Content (Join-Path $root '.github/workflows/aicopilot-ci.yml') -Raw
$runnerText = Get-Content (Join-Path $root 'scripts/tests/Invoke-AICopilotCiSelectedTests.ps1') -Raw
$webRestoreIndex = $workflowText.IndexOf(
    '- name: Restore web dependencies',
    [StringComparison]::Ordinal)
$webAuditIndex = $workflowText.IndexOf(
    '- name: Audit web dependencies',
    [StringComparison]::Ordinal)
$webChecksIndex = $workflowText.IndexOf(
    '- name: Lint, type-check, build and test affected web scope',
    [StringComparison]::Ordinal)
if ($webRestoreIndex -lt 0 -or
    $webAuditIndex -le $webRestoreIndex -or
    $webChecksIndex -le $webAuditIndex -or
    $workflowText -notmatch 'npm audit --audit-level=moderate --registry=https://registry\.npmjs\.org') {
    throw 'AICopilot default CI does not run the moderate web audit after npm ci.'
}
if ($workflowText -notmatch '\$selectorInputs\.Count\s+-gt\s+0[\s\S]*?Test-AICopilotCiTestSelection\.ps1') {
    throw 'AICopilot default CI does not gate selector behavior tests on affected selector inputs.'
}
if ($workflowText -match "\`$env:CI_MODE\s+-ne\s+'default'" -or
    ($workflowText.Split('Test-AICopilotCiTestSelection.ps1', [StringSplitOptions]::None).Length - 1) -ne 2) {
    throw 'AICopilot selector behavior tests are still wired to an unrelated explicit mode.'
}
if ($workflowText -notmatch 'if\s*\(\[string\]::IsNullOrWhiteSpace\(\$baseRef\)\s+-or\s+\$baseRef\s+-match\s+''\^0\+\$''\)\s*\{\s*\$baseRef\s*=\s*''HEAD\^''') {
    throw 'AICopilot manual CI modes do not have a deterministic base ref.'
}
if ($workflowText -notmatch 'Import-Module\s+\./scripts/tests/AICopilotGitPaths\.psm1' -or
    $workflowText -notmatch 'Get-AICopilotGitChangedFiles' -or
    $workflowText -match 'git\s+diff\s+--name-only' -or
    $workflowText -notmatch '-ChangedFiles\s+\$changedFiles') {
    throw 'AICopilot workflow does not use one NUL-safe changed-path set for input gating and selection.'
}
if ($workflowText -match 'Restore selected \.NET test projects' -or
    $runnerText -notmatch "'restore',\s+\`$selectedGraphPath" -or
    $runnerText -notmatch "'restore',\s+\`$productionGraphPath" -or
    $runnerText -notmatch "'--list-tests'" -or
    $runnerText -notmatch "'--no-build'" -or
    $runnerText -notmatch 'discovered zero tests') {
    throw 'AICopilot CI does not enforce one build per graph and non-zero filtered discovery.'
}
if ($workflowText -notmatch '\$actual\.Major\s+-ne\s+\$requested\.Major' -or
    $workflowText -notmatch '\$actual\.Minor\s+-ne\s+\$requested\.Minor' -or
    $workflowText -notmatch '\$actual\s+-lt\s+\$requested' -or
    $workflowText -match 'test\s+"\$\(dotnet --version\)"\s+=\s+"10\.0\.301"') {
    throw 'AICopilot CI SDK verification does not honor the global.json latestFeature roll-forward contract.'
}
if ($runnerText -notmatch "ForEach-Object\s*\{\s*\[int\]\`$_\['discovered'\]\s*\}" -or
    $runnerText -match 'Measure-Object\s+discovered\s+-Sum') {
    throw 'AICopilot CI discovery aggregation does not safely read ordered result dictionaries.'
}

Write-Host 'AICOPILOT_CI_SELECTION_BEHAVIOR_OK positive=1 docs=1 webManifest=1 activeContract=7 securityMapping=4 securityTest=1 securityProjectFile=1 testKitDependency=1 unicodePath=1 productionGraph=1 quality=1 deployment=1 deferred=1 dynamic=1 dynamicDeployment=1 retiredBusiness=1 unownedRetired=1 cross=1 negative=1 workflowGate=1 webAuditGate=1 sdkContract=1 graphBuild=1 discoveryAggregation=1'
