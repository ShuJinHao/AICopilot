[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$InventoryPath = (Join-Path $PSScriptRoot 'aicopilot-compatibility-inventory.json'),
    [string]$BaselinePath = (Join-Path $PSScriptRoot 'baselines/aicopilot-compatibility.json'),
    [string]$OutputPath = 'artifacts/quality/aicopilot-compatibility.json',
    [string]$BaseRef = 'origin/main',
    [switch]$UpdateBaseline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:sourceLineCache = @{}
$script:sourceTextCache = @{}
. (Join-Path $PSScriptRoot 'Resolve-AICopilotQualityBase.ps1')

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

function Invoke-CSharpSemanticProbe {
    $project = Join-Path $PSScriptRoot 'tools/AICopilot.CompatibilitySymbolProbe/AICopilot.CompatibilitySymbolProbe.csproj'
    if (-not (Test-Path $project -PathType Leaf)) {
        throw "C# compatibility symbol probe project does not exist: $project"
    }
    $output = Join-Path ([IO.Path]::GetTempPath()) "aicopilot-compatibility-symbols-$([Guid]::NewGuid().ToString('N')).json"
    try {
        $log = @(& dotnet run `
                --project $project `
                --configuration Release `
                --no-launch-profile `
                -- `
                $RepositoryRoot `
                $InventoryPath `
                $output 2>&1)
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $output -PathType Leaf)) {
            throw "C# compatibility symbol probe failed: $($log -join [Environment]::NewLine)"
        }
        return Get-Content $output -Raw | ConvertFrom-Json -Depth 64
    }
    finally {
        Remove-Item $output -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-TypeScriptSemanticProbe {
    $probe = Join-Path $PSScriptRoot 'tools/AICopilot.TypeScriptCompatibilityProbe.mjs'
    if (-not (Test-Path $probe -PathType Leaf)) {
        throw "TypeScript compatibility symbol probe does not exist: $probe"
    }
    $output = Join-Path ([IO.Path]::GetTempPath()) "aicopilot-typescript-compatibility-$([Guid]::NewGuid().ToString('N')).json"
    try {
        $log = @(& node $probe $RepositoryRoot $output 2>&1)
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $output -PathType Leaf)) {
            throw "TypeScript compatibility symbol probe failed: $($log -join [Environment]::NewLine)"
        }
        return Get-Content $output -Raw | ConvertFrom-Json -Depth 64
    }
    finally {
        Remove-Item $output -Force -ErrorAction SilentlyContinue
    }
}

function Get-PowerShellSemanticSignals {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$RelativePath,
        [Parameter(Mandatory)] [regex]$SignalPattern
    )

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $Path,
        [ref]$tokens,
        [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "PowerShell compatibility AST parse failed for '$RelativePath': $(@($parseErrors).Message -join '; ')"
    }

    $signals = [Collections.Generic.List[object]]::new()
    $functions = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
            }, $true))
    $commands = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst]
            }, $true))
    foreach ($function in $functions) {
        $name = [string]$function.Name
        if (-not $SignalPattern.IsMatch($name)) {
            continue
        }
        $referenceCount = @($commands | Where-Object {
                [string]$_.GetCommandName() -ieq $name -and
                -not ($_.Extent.StartOffset -ge $function.Extent.StartOffset -and
                    $_.Extent.EndOffset -le $function.Extent.EndOffset)
            }).Count
        $signals.Add([pscustomobject]@{
                path = $RelativePath
                line = [int]$function.Extent.StartLineNumber
                text = [string](($function.Extent.Text -split '\r?\n', 2)[0]).Trim()
                name = $name
                symbolId = $null
                semanticDisposition = $null
                referenceCount = $referenceCount
                language = 'PowerShell'
            })
    }

    $assignments = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [System.Management.Automation.Language.VariableExpressionAst]
            }, $true))
    $variables = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.VariableExpressionAst]
            }, $true))
    foreach ($group in @($assignments | Group-Object {
                ([System.Management.Automation.Language.AssignmentStatementAst]$_).Left.VariablePath.UserPath.ToLowerInvariant()
            })) {
        $declarations = @($group.Group)
        $declaration = $declarations[0]
        $name = [string]$declaration.Left.VariablePath.UserPath
        if (-not $SignalPattern.IsMatch($name)) {
            continue
        }
        $referenceCount = 0
        foreach ($variable in $variables) {
            if ($variable.VariablePath.UserPath -ine $name) {
                continue
            }
            $insideDeclaration = $false
            foreach ($assignment in $declarations) {
                if ($assignment.Extent.StartOffset -le $variable.Extent.StartOffset -and
                    $assignment.Extent.EndOffset -ge $variable.Extent.EndOffset) {
                    $insideDeclaration = $true
                    break
                }
            }
            if (-not $insideDeclaration) {
                $referenceCount++
            }
        }
        $signals.Add([pscustomobject]@{
                path = $RelativePath
                line = [int]$declaration.Extent.StartLineNumber
                text = [string]$declaration.Extent.Text.Trim()
                name = $name
                symbolId = $null
                semanticDisposition = $null
                referenceCount = $referenceCount
                language = 'PowerShell'
            })
    }

    return @($signals)
}

function Remove-ShellCommentsAndStrings {
    param([Parameter(Mandatory)] [string]$Line)

    $builder = [Text.StringBuilder]::new($Line.Length)
    $quote = [char]0
    for ($index = 0; $index -lt $Line.Length; $index++) {
        $current = $Line[$index]
        if ($quote -ne [char]0) {
            if ($current -eq '\' -and $quote -eq '"' -and $index + 1 -lt $Line.Length) {
                [void]$builder.Append(' ')
                $index++
                [void]$builder.Append(' ')
            }
            elseif ($current -eq $quote) {
                $quote = [char]0
                [void]$builder.Append(' ')
            }
            else {
                [void]$builder.Append(' ')
            }
            continue
        }
        if ($current -in @('"', "'")) {
            $quote = $current
            [void]$builder.Append(' ')
            continue
        }
        if ($current -eq '#') {
            [void]$builder.Append(' ', $Line.Length - $index)
            break
        }
        [void]$builder.Append($current)
    }
    $builder.ToString()
}

function Get-ShellSemanticSignals {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$RelativePath,
        [Parameter(Mandatory)] [regex]$SignalPattern
    )

    $rawLines = @(Get-SourceLines $Path)
    $lines = @($rawLines | ForEach-Object { Remove-ShellCommentsAndStrings ([string]$_) })
    $functionDeclarations = [Collections.Generic.List[object]]::new()
    $variableDeclarations = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = [string]$lines[$index]
        if ($line -match '^\s*(?:function\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\(\s*\))?\s*\{') {
            $depth = 0
            $end = $index
            for ($cursor = $index; $cursor -lt $lines.Count; $cursor++) {
                $depth += [regex]::Matches([string]$lines[$cursor], '\{').Count
                $depth -= [regex]::Matches([string]$lines[$cursor], '\}').Count
                $end = $cursor
                if ($depth -le 0) {
                    break
                }
            }
            $functionDeclarations.Add([pscustomobject]@{
                    name = [string]$Matches.name
                    start = $index
                    end = $end
                })
        }
        if ($line -match '^\s*(?:(?:export|readonly|local)\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=') {
            $variableDeclarations.Add([pscustomobject]@{
                    name = [string]$Matches.name
                    line = $index
                })
        }
    }

    $signals = [Collections.Generic.List[object]]::new()
    foreach ($declaration in $functionDeclarations) {
        if (-not $SignalPattern.IsMatch([string]$declaration.name)) {
            continue
        }
        $namePattern = '(?<![A-Za-z0-9_])' + [regex]::Escape([string]$declaration.name) + '(?=\s|$|[;&|])'
        $referenceCount = 0
        for ($index = 0; $index -lt $lines.Count; $index++) {
            if ($index -ge [int]$declaration.start -and $index -le [int]$declaration.end) {
                continue
            }
            $executableLine = [regex]::Replace(
                [string]$lines[$index],
                '^\s*(?:function\s+)?[A-Za-z_][A-Za-z0-9_]*\s*(?:\(\s*\))?\s*\{',
                '')
            if ($executableLine -notmatch '\bcommand\s+-v\b') {
                $referenceCount += [regex]::Matches($executableLine, $namePattern).Count
            }
        }
        $signals.Add([pscustomobject]@{
                path = $RelativePath
                line = [int]$declaration.start + 1
                text = ([string]$rawLines[[int]$declaration.start]).Trim()
                name = [string]$declaration.name
                symbolId = $null
                semanticDisposition = $null
                referenceCount = $referenceCount
                language = 'Shell'
            })
    }

    foreach ($group in @($variableDeclarations | Group-Object { ([string]$_.name).ToLowerInvariant() })) {
        $declarations = @($group.Group)
        $declaration = $declarations[0]
        if (-not $SignalPattern.IsMatch([string]$declaration.name)) {
            continue
        }
        $variablePattern = '\$(?:\{' + [regex]::Escape([string]$declaration.name) + '\}|' +
            [regex]::Escape([string]$declaration.name) + '(?![A-Za-z0-9_]))'
        $declarationLines = @($declarations | ForEach-Object { [int]$_.line })
        $referenceCount = 0
        for ($index = 0; $index -lt $lines.Count; $index++) {
            if ($index -in $declarationLines) {
                continue
            }
            $referenceCount += [regex]::Matches([string]$lines[$index], $variablePattern).Count
        }
        $signals.Add([pscustomobject]@{
                path = $RelativePath
                line = [int]$declaration.line + 1
                text = ([string]$rawLines[[int]$declaration.line]).Trim()
                name = [string]$declaration.name
                symbolId = $null
                semanticDisposition = $null
                referenceCount = $referenceCount
                language = 'Shell'
            })
    }

    return @($signals)
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

function Assert-UniqueTextEvidence {
    param(
        [Parameter(Mandatory)]$Evidence,
        [Parameter(Mandatory)][string]$Context
    )

    Assert-TextEvidence $Evidence $Context
    $evidencePath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot ([string]$Evidence.path)))
    $source = Remove-CodeComments (Get-SourceText $evidencePath)
    $token = [string]$Evidence.contains
    $count = 0
    $offset = 0
    while ($offset -lt $source.Length) {
        $match = $source.IndexOf($token, $offset, [StringComparison]::Ordinal)
        if ($match -lt 0) {
            break
        }
        $count++
        $offset = $match + $token.Length
    }
    if ($count -ne 1) {
        throw "$Context must identify exactly one active declaration; found $count occurrences of '$token' in $($Evidence.path)."
    }
}

function Get-CallerCount {
    param(
        [Parameter(Mandatory)]$Scan,
        [Parameter(Mandatory)][string]$Context,
        [Parameter(Mandatory)][string]$SemanticKey,
        [Parameter(Mandatory)][Collections.Generic.Dictionary[string, int]]$SemanticCallerCounts
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
    if ($extensions -contains '.cs') {
        if ($extensions.Count -ne 1) {
            throw "$Context cannot mix C# semantic scans with lexical script extensions."
        }
        if (-not $SemanticCallerCounts.ContainsKey($SemanticKey)) {
            throw "$Context is missing exact C# semantic result '$SemanticKey'."
        }
        return [int]$SemanticCallerCounts[$SemanticKey]
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
    if ($extensions.Count -eq 1 -and $extensions[0] -in @('.ps1', '.sh')) {
        if ($excludeContains.Count -ne 0) {
            throw "$Context cannot use lexical excludeContains with an exact script AST scan."
        }
        $signalRegex = [regex]::new(
            '(?:Alias|Adapter|Wrapper|Fallback|Compatibility|Legacy|Shadow|DualWrite|Obsolete)',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
                [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        $candidateNames = @([regex]::Matches(
                $token,
                '[A-Za-z_][A-Za-z0-9_-]*') | ForEach-Object Value | Where-Object {
                $signalRegex.IsMatch([string]$_)
            } | Select-Object -Unique)
        if ($candidateNames.Count -ne 1) {
            throw "$Context must identify exactly one compatibility signal name for the script AST scan."
        }
        $count = 0
        foreach ($file in @($files | Sort-Object FullName -Unique)) {
            $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/')
            $scriptSignals = if ($extensions[0] -eq '.ps1') {
                @(Get-PowerShellSemanticSignals $file.FullName $relativePath $signalRegex)
            }
            else {
                @(Get-ShellSemanticSignals $file.FullName $relativePath $signalRegex)
            }
            $count += @($scriptSignals | Where-Object {
                    [string]$_.name -ieq [string]$candidateNames[0]
                } | Measure-Object -Property referenceCount -Sum).Sum
        }
        return [int]$count
    }
    $count = 0
    foreach ($file in @($files | Sort-Object FullName -Unique)) {
        $count += Get-ScriptCallerCount $file.FullName $token $excludeContains
    }
    $count
}

$inventory = Read-JsonFile $InventoryPath
if ([int]$inventory.schemaVersion -ne 3) {
    throw "Unsupported compatibility inventory schemaVersion '$($inventory.schemaVersion)'."
}
$semanticProbe = Invoke-CSharpSemanticProbe
$typeScriptProbe = Invoke-TypeScriptSemanticProbe
$semanticCallerCounts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
foreach ($producerCheck in @($semanticProbe.ProducerChecks)) {
    if ([int]$producerCheck.DeclarationCount -ne 1) {
        throw "C# producer '$($producerCheck.ItemId)' must resolve to exactly one declaration of '$($producerCheck.SymbolId)' in '$($producerCheck.Path)'; found $($producerCheck.DeclarationCount)."
    }
}
foreach ($callerCount in @($semanticProbe.CallerCounts)) {
    $key = "$([string]$callerCount.ItemId)/$([string]$callerCount.ScanId)"
    if ($semanticCallerCounts.ContainsKey($key)) {
        throw "C# semantic probe returned duplicate caller result '$key'."
    }
    $semanticCallerCounts.Add($key, [int]$callerCount.Count)
}
$csharpDispositions = @{}
foreach ($property in @($inventory.csharpSymbols.candidateDispositions.PSObject.Properties)) {
    $csharpDispositions[[string]$property.Name] = [string]$property.Value
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

    $callerCount = Get-CallerCount `
        $item.callerScan `
        "$context callerScan" `
        "$([string]$item.id)/primary" `
        $semanticCallerCounts
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
    if ([string]$item.id -notmatch '^AI-ORDINARY-[A-Z0-9-]+$') {
        throw "$context must use an AI-ORDINARY-* ID."
    }
    if ([string]$item.replacement -notmatch '^notApplicable:\s+\S') {
        throw "$context must declare replacement='notApplicable: ...'; compatibility or migration paths require a versioned compatibility item instead."
    }
    foreach ($compatibilityOnlyProperty in @('latestDeletionBatch', 'deletionDeadline', 'callEvidence', 'coverageTests')) {
        if ($null -ne $item.PSObject.Properties[$compatibilityOnlyProperty]) {
            throw "$context cannot declare compatibility lifecycle property '$compatibilityOnlyProperty'."
        }
    }

    if ([IO.Path]::GetExtension([string]$item.producer.path) -eq '.cs') {
        Assert-TextEvidence $item.producer "$context producer"
    }
    else {
        Assert-UniqueTextEvidence $item.producer "$context producer"
    }
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
        $callerCount = Get-CallerCount `
            $scan `
            "$context callerScan '$($scan.id)'" `
            "$([string]$item.id)/$([string]$scan.id)" `
            $semanticCallerCounts
        if ($callerCount -le 0) {
            throw "$context callerScan '$($scan.id)' has no production call sites; physically delete the abstraction."
        }
        $callSiteCounts["$([string]$item.id)/$([string]$scan.id)"] = $callerCount
    }
}

$signalNamePattern = '(?:Alias|Adapter|Wrapper|Fallback|Compatibility|Legacy|Shadow|DualWrite|Obsolete)'
$signalNameRegex = [regex]::new(
    $signalNamePattern,
    [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
# This inventory owns application and test-quality-tool source only. Production CD
# under .github/deploy is outside this test-architecture batch and is audited separately.
$scanRoots = @(
    'src/core',
    'src/hosts',
    'src/infrastructure',
    'src/services',
    'src/shared',
    'src/testing',
    'src/vues/AICopilot.Web/src',
    'scripts/tests'
)
$signals = [Collections.Generic.List[object]]::new()
foreach ($semanticSignal in @($semanticProbe.CandidateSignals)) {
    $symbolId = [string]$semanticSignal.SymbolId
    if (-not $csharpDispositions.ContainsKey($symbolId)) {
        throw "C# compatibility signal has no exact symbol disposition: $symbolId"
    }
    $signals.Add([pscustomobject]@{
            path = [string]$semanticSignal.Path
            line = [int]$semanticSignal.Line
            text = [string]$semanticSignal.Text
            name = [string]$semanticSignal.SymbolId
            symbolId = $symbolId
            semanticDisposition = [string]$csharpDispositions[$symbolId]
            referenceCount = [int]$semanticSignal.ReferenceCount
            language = 'CSharp'
        })
}
foreach ($typeScriptSignal in @($typeScriptProbe.signals)) {
    $signals.Add([pscustomobject]@{
            path = [string]$typeScriptSignal.path
            line = [int]$typeScriptSignal.line
            text = [string]$typeScriptSignal.text
            name = [string]$typeScriptSignal.name
            symbolId = $null
            semanticDisposition = $null
            referenceCount = [int]$typeScriptSignal.referenceCount
            language = 'TypeScript'
        })
}
foreach ($relativeRoot in $scanRoots) {
    $root = Join-Path $RepositoryRoot $relativeRoot
    if (-not (Test-Path $root -PathType Container)) {
        throw "Compatibility candidate scan root does not exist: $relativeRoot"
    }
    foreach ($file in Get-ChildItem $root -Recurse -File) {
        $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/')
        $isFixtureOrGeneratedQualityData = $relativePath -match '(?i)^scripts/tests/(?:.*Behavior\.ps1|(?:baselines|fixtures|generated|artifacts)/)'
        if ($file.Extension -notin @('.cs', '.ps1', '.sh', '.yml', '.yaml', '.props', '.targets', '.csproj') -or
            $file.FullName -match '[\/](bin|obj|Migrations|node_modules|dist)[\/]' -or
            $isFixtureOrGeneratedQualityData) {
            continue
        }
        if ($file.Extension -eq '.cs') {
            continue
        }

        if ($file.Extension -eq '.ps1') {
            foreach ($signal in @(Get-PowerShellSemanticSignals $file.FullName $relativePath $signalNameRegex)) {
                $signals.Add($signal)
            }
            continue
        }
        if ($file.Extension -eq '.sh') {
            foreach ($signal in @(Get-ShellSemanticSignals $file.FullName $relativePath $signalNameRegex)) {
                $signals.Add($signal)
            }
            continue
        }

        $lines = @(Get-SourceLines $file.FullName)
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = [string]$lines[$index]
            $isCandidate = if ($file.Extension -in @('.yml', '.yaml')) {
                $line -match '(?i)^\s*(?:name|id|[A-Za-z0-9_-]+)\s*:\s*[^#]*(?:archive\s+fallback|legacy|compatibility|dual[-_ ]?write|shadow\s+path)'
            } else {
                $line -match '(?i)<(?:[A-Za-z0-9_.-]*(?:Legacy|Compatibility|Fallback|DualWrite|ShadowPath)[A-Za-z0-9_.-]*)\b'
            }
            if ($isCandidate) {
                $signals.Add([pscustomobject]@{
                        path = $relativePath
                        line = $index + 1
                        text = $line.Trim()
                        name = $line.Trim()
                        symbolId = $null
                        semanticDisposition = $null
                        referenceCount = 1
                        language = 'Configuration'
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
            if ([IO.Path]::GetExtension([string]$evidence.path) -eq '.cs') {
                continue
            }
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
    if ([int]$signal.referenceCount -le 0) {
        throw "$($signal.language) compatibility signal '$($signal.name)' at $($signal.path):$($signal.line) has no exact executable references; physically delete it."
    }
    $matches = @(if (-not [string]::IsNullOrWhiteSpace([string]$signal.semanticDisposition)) {
        [pscustomobject]@{
                id = [string]$signal.semanticDisposition
                disposition = if (@($items | Where-Object id -CEQ ([string]$signal.semanticDisposition)).Count -eq 1) {
                    'compatibility'
                }
                else {
                    'ordinaryAbstraction'
                }
                path = [string]$signal.path
                contains = [string]$signal.symbolId
            }
    }
    else {
        $dispositions | Where-Object {
                $_.path -ceq $signal.path -and
                $signal.text.Contains($_.contains, [StringComparison]::Ordinal)
            }
    })
    if ($matches.Count -ne 1) {
        throw "Compatibility signal must have exactly one disposition: $($signal.path):$($signal.line):$($signal.text)"
    }
    $match = $matches[0]
    if ([string]$signal.name -match '(?i)(?:Legacy|Compatibility|Obsolete|Shadow|DualWrite)' -and
        -not ([string]$match.id).StartsWith('AI-COMPAT-', [StringComparison]::Ordinal)) {
        throw "$($signal.language) migration signal '$($signal.name)' must use an AI-COMPAT disposition, not '$($match.id)'."
    }
    if ([string]::IsNullOrWhiteSpace([string]$signal.semanticDisposition)) {
        $key = "$($match.id)|$($match.path)|$($match.contains)"
        $dispositionHitCounts[$key]++
    }
}
foreach ($disposition in $dispositions) {
    $key = "$($disposition.id)|$($disposition.path)|$($disposition.contains)"
    if ([int]$dispositionHitCounts[$key] -eq 0) {
        throw "Compatibility candidate disposition has no active source signal: $key"
    }
}

$baseline = Read-JsonFile $BaselinePath
if ([int]$baseline.schemaVersion -ne 3) {
    throw "Unsupported compatibility baseline schemaVersion '$($baseline.schemaVersion)'."
}
$baselineContext = Get-AICopilotBaselineContext `
    -RepositoryRoot $RepositoryRoot `
    -BaseRef $BaseRef `
    -BaselineKind Compatibility `
    -BaselinePath $BaselinePath
$baseLedgerItems = if ($baselineContext.Mode -eq 'Ratchet') {
    $baseBaseline = $baselineContext.BaseBaselineJson | ConvertFrom-Json -Depth 32
    if ([int]$baseBaseline.schemaVersion -notin @(2, 3)) {
        throw "Unsupported base compatibility baseline schemaVersion '$($baseBaseline.schemaVersion)'."
    }
    @($baseBaseline.compatibilityItems)
} else {
    @($baseline.compatibilityItems)
}

if ($UpdateBaseline) {
    $baselineItems = @($baseLedgerItems)
    $baselineItemIds = @($baselineItems | ForEach-Object { [string]$_.id })
    $newLedgerIds = @(
        $items |
            ForEach-Object { [string]$_.id } |
            Where-Object { $_ -notin $baselineItemIds }
    )
    if ($newLedgerIds.Count -ne 0) {
        throw "Compatibility or migration baseline updates may only delete or tighten comparison-baseline entries; new IDs are forbidden: [$($newLedgerIds -join ', ')]."
    }
    foreach ($item in $items) {
        $previous = @($baselineItems | Where-Object { [string]$_.id -ceq [string]$item.id })
        if ($previous.Count -ne 1) {
            throw "Compatibility baseline update cannot resolve comparison-baseline entry '$($item.id)'."
        }
        if ([DateTimeOffset]::Parse([string]$item.deletionDeadline) -gt
            [DateTimeOffset]::Parse([string]$previous[0].deletionDeadline)) {
            throw "Compatibility deletion deadline relaxation is forbidden for '$($item.id)'."
        }
        if ([int]$callSiteCounts["$([string]$item.id)/primary"] -gt
            [int]$previous[0].maximumCallSites) {
            throw "Compatibility call-site growth cannot be accepted by UpdateBaseline for '$($item.id)'."
        }
    }
    $baseline = [ordered]@{
        schemaVersion = 3
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
    }
    $baselineDirectory = Split-Path $BaselinePath -Parent
    New-Item $baselineDirectory -ItemType Directory -Force | Out-Null
    $baseline | ConvertTo-Json -Depth 8 | Set-Content $BaselinePath -Encoding utf8NoBOM
    $baseline = Read-JsonFile $BaselinePath
}
$baselineItems = @($baseline.compatibilityItems)
$inventoryIds = @($items | ForEach-Object { [string]$_.id } | Sort-Object)
$baselineIds = @($baselineItems | ForEach-Object { [string]$_.id } | Sort-Object)
if (($inventoryIds -join "`n") -cne ($baselineIds -join "`n")) {
    throw "Compatibility IDs differ from the comparison baseline: inventory=[$($inventoryIds -join ', ')], baseline=[$($baselineIds -join ', ')]."
}

foreach ($item in $items) {
    $baselineItem = @($baselineItems | Where-Object { [string]$_.id -ceq [string]$item.id })
    if ($baselineItem.Count -ne 1) {
        throw "Compatibility baseline must contain exactly one entry for '$($item.id)'."
    }
    if ([string]$baselineItem[0].deletionDeadline -cne [string]$item.deletionDeadline) {
        throw "Compatibility deadline differs from the comparison baseline for '$($item.id)'."
    }
    $actualCallSites = [int]$callSiteCounts["$([string]$item.id)/primary"]
    $maximumCallSites = [int]$baselineItem[0].maximumCallSites
    if ($actualCallSites -gt $maximumCallSites) {
        throw "Compatibility call sites grew for '$($item.id)': actual=$actualCallSites maximum=$maximumCallSites."
    }
    if ($baselineContext.Mode -eq 'Bootstrap' -and $actualCallSites -ne $maximumCallSites) {
        throw "Initial compatibility baseline must exactly reconcile call sites for '$($item.id)': actual=$actualCallSites baseline=$maximumCallSites."
    }
    if ($baselineContext.Mode -eq 'Ratchet') {
        $baseItem = @($baseLedgerItems | Where-Object { [string]$_.id -ceq [string]$item.id })
        if ($baseItem.Count -ne 1) {
            throw "New compatibility or migration item '$($item.id)' is forbidden after baseline bootstrap."
        }
        if ([DateTimeOffset]::Parse([string]$baselineItem[0].deletionDeadline) -gt
            [DateTimeOffset]::Parse([string]$baseItem[0].deletionDeadline) -or
            $maximumCallSites -gt [int]$baseItem[0].maximumCallSites) {
            throw "Candidate compatibility baseline weakens base deadline/call-site limits for '$($item.id)'."
        }
    }
}

if ([int]$baseline.unclassifiedCompatibilitySignals -ne 0) {
    throw "Compatibility baseline must keep unclassifiedCompatibilitySignals at zero."
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
