[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('Validate', 'Describe')]
    [string]$Mode = 'Validate',
    [string]$RepositoryRoot,
    [Parameter(Mandatory)]
    [string]$TrustedBaseRevision,
    [string]$CandidateRevision = 'HEAD',
    [ValidateSet('BaseAncestorOfHead', 'HeadAncestorOfBase')]
    [string]$AnchorRelationship = 'BaseAncestorOfHead',
    [string]$OutputPath,
    [string]$MigrationId,
    [string]$RuleIdsCsv,
    [string]$Owner,
    [string]$ApprovedBy,
    [string]$Reason,
    [string]$IssuedAtUtc,
    [string]$ExpiresAtUtc,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RuleId = 'AI-TEST-GOV-MIG-001'
$script:ReceiptSchemaVersion = '1.0'
$script:BaselinePath = 'scripts/tests/baselines/aicopilot-test-governance.baseline.json'
$script:GovernancePolicyPath = 'scripts/tests/TestAICopilotTestGovernancePolicy.ps1'
$script:PendingRoot = 'scripts/tests/baselines/migrations/pending/'
$script:ConsumedRoot = 'scripts/tests/baselines/migrations/consumed/'
$script:CancelledRoot = 'scripts/tests/baselines/migrations/cancelled/'
$script:ValidatorPath = 'scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1'
$script:TrustedWrapperPath = 'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1'
$script:SelfTestPath = 'scripts/tests/baselines/migrations/TestAICopilotGovernanceMigrationValidator.v1.ps1'
$script:SchemaPath = 'scripts/tests/baselines/migrations/aicopilot-governance-migration-receipt.schema.json'
$script:CanonicalWorkflowPath = '.github/workflows/aicopilot-ci.yml'
$script:CanonicalWorkflowName = 'aicopilot-ci'
$script:RequiredFinalJobId = 'required-final'
$script:DescribeRequiredArgumentNames = @(
    'MigrationId',
    'RuleIdsCsv',
    'Owner',
    'ApprovedBy',
    'Reason'
)
$script:DescribeAnchorRelationship = 'BaseAncestorOfHead'
$script:ReservedWorkflowTrustReferenceTokens = @(
    'aicopilot-ci',
    'migration-validator-selftest',
    'build-test',
    'required-final',
    'aicopilot-ci/required-final',
    'aicopilot-ci / required-final',
    'AI-TEST-GOV-MIG-TRUSTED-EXECUTOR-V1',
    'AI-TEST-GOV-MIG-TRUSTED-SELFTEST-V1',
    'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1',
    'scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1',
    'scripts/tests/baselines/migrations/TestAICopilotGovernanceMigrationValidator.v1.ps1',
    'scripts/tests/baselines/migrations/aicopilot-governance-migration-receipt.schema.json'
)
$script:NonCanonicalWorkflowTopLevelKeyPattern = '^(?<Key>[A-Za-z][A-Za-z0-9_-]*):(?:[ \t].*)?$'
$script:NonCanonicalWorkflowNamePattern = '^name: (?<Name>[A-Za-z0-9][A-Za-z0-9 ._()/+-]*)$'
$script:NonCanonicalWorkflowJobsHeader = 'jobs:'
$script:NonCanonicalWorkflowJobIdPattern = '^  (?<JobId>[A-Za-z_][A-Za-z0-9_-]*):$'
$script:NonCanonicalWorkflowDirectJobPropertyPattern = '^    (?<Key>[A-Za-z][A-Za-z0-9_-]*):(?:[ \t].*)?$'
$script:NonCanonicalWorkflowDirectJobNamePattern = '^    name: (?<Name>[A-Za-z0-9][A-Za-z0-9 ._()/+-]*)$'
$script:TrustImplementationPaths = @(
    $script:TrustedWrapperPath,
    $script:SelfTestPath,
    $script:ValidatorPath,
    $script:SchemaPath
)
$script:V1TrustUpgradePaths = @($script:ValidatorPath)
$script:BaseOwnedHarnessPaths = @(
    $script:TrustedWrapperPath,
    $script:SelfTestPath,
    $script:SchemaPath
)
$script:MaximumReceiptLifetime = [TimeSpan]::FromDays(7)
$script:MaximumReceiptBytes = 1MB
$script:MaximumReceiptChanges = 5000
$script:JsonSerializerOptions = [Text.Json.JsonSerializerOptions]::new()
$script:ApprovedOwners = @(
    'AI.Architecture',
    'AI.AgentWorkflow',
    'AI.Persistence',
    'AI.Tests',
    'AI.Deployment',
    'AI.Security',
    'AI.Web'
)
$script:ApprovedApprovers = @('ShuJinHao')
$script:TrustUpgradeRuleId = 'AI-TEST-GOV-TRUST-UPGRADE-001'
$script:ApprovedGovernedRuleIds = @(
    'AI-ARCH-001',
    'AI-EVAL-001',
    'AI-TEST-001',
    'AI-TEST-002',
    'AI-TEST-003',
    'AI-TEST-004',
    'AI-TEST-DUP-001',
    'AI-TEST-GOV-001',
    'AI-TEST-UI-001',
    'AI-TEST-GOV-TRUST-UPGRADE-001'
)

function Stop-MigrationValidation {
    param(
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Message
    )

    throw "$($script:RuleId)-$Code $Message"
}

function Test-TrustUpgradeRuleIdSingleton {
    param(
        [AllowEmptyCollection()][object[]]$RuleIds,
        [Parameter(Mandatory)][string]$Location
    )

    $rawRuleIds = @($RuleIds)
    $trustUpgradeCount = @($rawRuleIds | Where-Object {
        [string]$_ -ceq $script:TrustUpgradeRuleId
    }).Count
    $isTrustUpgrade = $rawRuleIds.Count -eq 1 -and $trustUpgradeCount -eq 1
    if ($trustUpgradeCount -ne 0 -and -not $isTrustUpgrade) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "$Location must contain AI-TEST-GOV-TRUST-UPGRADE-001 as its one raw singleton entry.")
    }
    return $isTrustUpgrade
}

function Get-Sha256Hex {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function ConvertFrom-StrictUtf8Bytes {
    param(
        [Parameter(Mandatory)][byte[]]$Bytes,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Context
    )

    try {
        return [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    }
    catch {
        Stop-MigrationValidation -Code $Code -Message "$Context is not valid UTF-8."
    }
}

function Get-OrdinalSortedStrings {
    param(
        [AllowEmptyCollection()][object[]]$Values,
        [switch]$Unique
    )

    $items = [Collections.Generic.List[string]]::new()
    foreach ($value in @($Values)) { $items.Add([string]$value) }
    $items.Sort([StringComparer]::Ordinal)
    if (-not $Unique) { return @($items) }

    $result = [Collections.Generic.List[string]]::new()
    $previous = $null
    foreach ($item in $items) {
        if ($null -eq $previous -or -not [StringComparer]::Ordinal.Equals($previous, $item)) {
            $result.Add($item)
            $previous = $item
        }
    }
    return @($result)
}

function Get-OrdinalSortedChangeRecords {
    param([AllowEmptyCollection()][object[]]$Values)

    $items = [Collections.Generic.List[object]]::new()
    foreach ($value in @($Values)) { $items.Add($value) }
    $comparer = [Collections.Generic.Comparer[object]]::Create(
        [Comparison[object]]{
            param($left, $right)
            return [StringComparer]::Ordinal.Compare([string]$left.path, [string]$right.path)
        })
    $items.Sort($comparer)
    return @($items)
}

function Invoke-GitBytes {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.ArgumentList.Add('-C')
    $startInfo.ArgumentList.Add($script:RepositoryRoot)
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        Stop-MigrationValidation -Code 'GIT' -Message "could not start git $($Arguments -join ' ')."
    }

    $errorTask = $process.StandardError.ReadToEndAsync()
    $buffer = [IO.MemoryStream]::new()
    try {
        $process.StandardOutput.BaseStream.CopyTo($buffer)
        $process.WaitForExit()
        $errorText = $errorTask.GetAwaiter().GetResult().Trim()
        $result = [pscustomobject]@{
            ExitCode = $process.ExitCode
            Bytes = $buffer.ToArray()
            Error = $errorText
        }
    }
    finally {
        $buffer.Dispose()
        $process.Dispose()
    }

    if ($result.ExitCode -ne 0 -and -not $AllowFailure) {
        Stop-MigrationValidation -Code 'GIT' -Message (
            "git {0} failed with exit code {1}: {2}" -f
            ($Arguments -join ' '), $result.ExitCode, $result.Error)
    }

    return $result
}

function Invoke-GitText {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $result = Invoke-GitBytes -Arguments $Arguments -AllowFailure:$AllowFailure
    return [pscustomobject]@{
        ExitCode = $result.ExitCode
        Text = [Text.Encoding]::UTF8.GetString($result.Bytes)
        Error = $result.Error
    }
}

function Resolve-Commit {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Revision) -or $Revision -match '^0{40}$') {
        Stop-MigrationValidation -Code 'REVISION' -Message "$Name must be a non-zero commit revision."
    }

    $result = Invoke-GitText -Arguments @('rev-parse', '--verify', "$Revision^{commit}") -AllowFailure
    if ($result.ExitCode -ne 0) {
        Stop-MigrationValidation -Code 'REVISION' -Message "$Name is not an available commit: $Revision."
    }

    $resolved = $result.Text.Trim()
    if ($resolved -cnotmatch '^[0-9a-f]{40}$') {
        Stop-MigrationValidation -Code 'REVISION' -Message "$Name did not resolve to one full commit SHA."
    }

    return $resolved
}

function Assert-Ancestry {
    param(
        [Parameter(Mandatory)][string]$Ancestor,
        [Parameter(Mandatory)][string]$Descendant,
        [Parameter(Mandatory)][string]$Context
    )

    $result = Invoke-GitText -Arguments @('merge-base', '--is-ancestor', $Ancestor, $Descendant) -AllowFailure
    if ($result.ExitCode -ne 0) {
        Stop-MigrationValidation -Code 'ANCESTRY' -Message "$Context requires $Ancestor to be an ancestor of $Descendant."
    }
}

function Assert-LinearHistoryRange {
    param(
        [Parameter(Mandatory)][string]$Ancestor,
        [Parameter(Mandatory)][string]$Descendant,
        [Parameter(Mandatory)][string]$Context,
        [switch]$ValidateAncestorEndpoint
    )

    if ($ValidateAncestorEndpoint) {
        $ancestorLine = (Invoke-GitText -Arguments @(
            'rev-list', '--parents', '-n', '1', $Ancestor)).Text.Trim()
        $ancestorParts = @($ancestorLine.Split(' ', [StringSplitOptions]::RemoveEmptyEntries))
        if ($ancestorParts.Count -lt 1 -or $ancestorParts[0] -cne $Ancestor) {
            Stop-MigrationValidation -Code 'HISTORY' -Message (
                "$Context could not validate the untrusted ancestor endpoint '$Ancestor'.")
        }
        if ($ancestorParts.Count -gt 2) {
            Stop-MigrationValidation -Code 'HISTORY' -Message (
                "$Context only supports a linear v1 history; untrusted ancestor endpoint " +
                "'$Ancestor' has $($ancestorParts.Count - 1) parents.")
        }
    }

    $history = (Invoke-GitText -Arguments @(
        'rev-list', '--parents', "$Ancestor..$Descendant")).Text
    foreach ($line in @($history -split "`r?`n" | Where-Object { $_ -ne '' })) {
        $parts = @($line.Split(' ', [StringSplitOptions]::RemoveEmptyEntries))
        if ($parts.Count -ne 2) {
            Stop-MigrationValidation -Code 'HISTORY' -Message (
                "$Context only supports a linear v1 history; commit '$($parts[0])' has " +
                "$($parts.Count - 1) parents.")
        }
    }
}

function Assert-FirstParentAncestry {
    param(
        [Parameter(Mandatory)][string]$Ancestor,
        [Parameter(Mandatory)][string]$Descendant,
        [Parameter(Mandatory)][string]$Context
    )

    $history = (Invoke-GitText -Arguments @('rev-list', '--first-parent', $Descendant)).Text
    $firstParentCommits = @($history -split "`r?`n" | Where-Object { $_ -ne '' })
    if ($Ancestor -cnotin $firstParentCommits) {
        Stop-MigrationValidation -Code 'ANCESTRY' -Message (
            "$Context requires $Ancestor on the first-parent chain of $Descendant.")
    }
}

function Assert-DirectSingleParentTransition {
    param(
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Context
    )

    $parents = (Invoke-GitText -Arguments @(
        'rev-list', '--parents', '-n', '1', $TargetRevision)).Text.Trim().Split(' ')
    if ($parents.Count -ne 2 -or $parents[1] -cne $BaseRevision) {
        Stop-MigrationValidation -Code $Code -Message (
            "$Context must be one single-parent commit directly after its trusted base.")
    }
}

function Assert-SafeRepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or
        $Path -cne $Path.Trim() -or
        $Path.StartsWith('/') -or
        $Path.Contains('\') -or
        $Path.Contains('//') -or
        $Path -match '(^|/)\.\.?(/|$)' -or
        $Path -match '[\x00-\x1F\x7F]' -or
        $Path.Contains('*') -or
        $Path.Contains('?') -or
        $Path.Contains('[') -or
        $Path.Contains(']') -or
        $Path -match '[<>:"|]') {
        Stop-MigrationValidation -Code 'PATH' -Message "unsafe repository path '$Path'."
    }

    $windowsReservedName = '^(?i:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\..*)?$'
    foreach ($segment in $Path.Split('/')) {
        if ($segment.Length -gt 255 -or
            $segment.EndsWith(' ', [StringComparison]::Ordinal) -or
            $segment.EndsWith('.', [StringComparison]::Ordinal) -or
            $segment -match $windowsReservedName) {
            Stop-MigrationValidation -Code 'PATH' -Message (
                "repository path '$Path' is not portable to supported Windows worktrees.")
        }
    }
}

function Get-RevisionPaths {
    param([Parameter(Mandatory)][string]$Revision)

    $result = Invoke-GitBytes -Arguments @(
        '-c', 'core.quotepath=false', 'ls-tree', '-r', '-z', '--name-only', $Revision)
    if ($result.Bytes.Length -eq 0) {
        return @()
    }

    $text = ConvertFrom-StrictUtf8Bytes `
        -Bytes $result.Bytes `
        -Code 'PATH' `
        -Context "repository tree at $Revision"
    $nul = [char[]]@([char]0)
    $paths = @($text.TrimEnd($nul).Split($nul, [StringSplitOptions]::None) |
        Where-Object { $_ -ne '' })
    $caseLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $paths) {
        Assert-SafeRepositoryPath -Path $path
        if ($caseLedger.ContainsKey($path) -and $caseLedger[$path] -cne $path) {
            Stop-MigrationValidation -Code 'PATH' -Message (
                "case-colliding repository paths '$($caseLedger[$path])' and '$path'.")
        }
        $caseLedger[$path] = $path
    }

    return @(Get-OrdinalSortedStrings -Values $paths)
}

function Get-GitEntry {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path,
        [switch]$AllowMissing
    )

    Assert-SafeRepositoryPath -Path $Path
    $result = Invoke-GitBytes -Arguments @(
        '-c', 'core.quotepath=false', 'ls-tree', '-z', $Revision, '--', $Path)
    if ($result.Bytes.Length -eq 0) {
        if ($AllowMissing) { return $null }
        Stop-MigrationValidation -Code 'BLOB' -Message "$Revision has no tracked file '$Path'."
    }

    $text = (ConvertFrom-StrictUtf8Bytes `
        -Bytes $result.Bytes `
        -Code 'BLOB' `
        -Context "git entry for '$Path' at $Revision").TrimEnd([char]0)
    $match = [regex]::Match($text, '^([0-9]{6}) ([a-z]+) ([0-9a-f]+)\t(.+)$')
    if (-not $match.Success -or $match.Groups[4].Value -cne $Path) {
        Stop-MigrationValidation -Code 'BLOB' -Message "could not parse git entry for '$Path' at $Revision."
    }
    if ($match.Groups[2].Value -cne 'blob' -or $match.Groups[1].Value -cnotin @('100644', '100755')) {
        Stop-MigrationValidation -Code 'MODE' -Message (
            "'$Path' uses forbidden git type/mode $($match.Groups[2].Value)/$($match.Groups[1].Value).")
    }

    return [pscustomobject]@{
        Mode = $match.Groups[1].Value
        ObjectId = $match.Groups[3].Value
        Path = $Path
    }
}

function Get-GitBlobBytes {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    $entry = Get-GitEntry -Revision $Revision -Path $Path
    return (Invoke-GitBytes -Arguments @('cat-file', 'blob', $entry.ObjectId)).Bytes
}

function Get-GitBlobText {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    return [Text.Encoding]::UTF8.GetString((Get-GitBlobBytes -Revision $Revision -Path $Path))
}

function Get-StrictUtf8GitBlobText {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    try {
        return [Text.UTF8Encoding]::new($false, $true).GetString(
            (Get-GitBlobBytes -Revision $Revision -Path $Path))
    }
    catch {
        Stop-MigrationValidation -Code 'TRUST' -Message "'$Path' is not valid UTF-8 at $Revision."
    }
}

function Get-OrdinalOccurrenceCount {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Value
    )

    if ($Value.Length -eq 0) { return 0 }
    $count = 0
    $offset = 0
    while ($offset -le $Text.Length - $Value.Length) {
        $index = $Text.IndexOf($Value, $offset, [StringComparison]::Ordinal)
        if ($index -lt 0) { break }
        $count++
        $offset = $index + $Value.Length
    }
    return $count
}

function Assert-StrictNonCanonicalWorkflowIdentityGrammar {
    param(
        [Parameter(Mandatory)][string]$WorkflowPath,
        [Parameter(Mandatory)][string]$Text
    )

    $normalizedText = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
    $lines = @($normalizedText.Split([char]10))
    $workflowNameCount = 0
    $jobsHeaderIndices = [Collections.Generic.List[int]]::new()
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        $line = [string]$lines[$lineIndex]
        if ([string]::IsNullOrWhiteSpace($line) -or
            $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) {
            continue
        }

        $leadingSpaces = 0
        while ($leadingSpaces -lt $line.Length -and $line[$leadingSpaces] -eq ' ') {
            $leadingSpaces++
        }
        if ($leadingSpaces -lt $line.Length -and
            $line[$leadingSpaces] -eq "`t" -and
            $leadingSpaces -le 4) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "non-canonical workflow '$WorkflowPath' uses ambiguous tab indentation at line $($lineIndex + 1).")
        }
        if ($leadingSpaces -ne 0) { continue }

        $topLevelMatch = [regex]::Match(
            $line,
            $script:NonCanonicalWorkflowTopLevelKeyPattern,
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if (-not $topLevelMatch.Success) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "non-canonical workflow '$WorkflowPath' must use plain block-style top-level keys; line=$($lineIndex + 1).")
        }
        $topLevelKey = $topLevelMatch.Groups['Key'].Value
        if ($topLevelKey -ceq 'name') {
            if (-not [regex]::IsMatch(
                $line,
                $script:NonCanonicalWorkflowNamePattern,
                [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' must use one plain static workflow name without quotes, escapes, block scalars, anchors, aliases or expressions.")
            }
            $workflowNameCount++
        }
        elseif ($topLevelKey -ceq 'jobs') {
            if ($line -cne $script:NonCanonicalWorkflowJobsHeader) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' must use one plain block-style jobs mapping.")
            }
            $jobsHeaderIndices.Add($lineIndex)
        }
    }
    if ($workflowNameCount -ne 1 -or $jobsHeaderIndices.Count -ne 1) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "non-canonical workflow '$WorkflowPath' must contain exactly one strict plain name and jobs mapping; names=$workflowNameCount jobs=$($jobsHeaderIndices.Count).")
    }

    $jobIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $currentJobId = $null
    $currentDirectPropertyCount = 0
    $currentDirectNameCount = 0
    for ($lineIndex = $jobsHeaderIndices[0] + 1; $lineIndex -lt $lines.Count; $lineIndex++) {
        $line = [string]$lines[$lineIndex]
        if ([string]::IsNullOrWhiteSpace($line) -or
            $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) {
            continue
        }

        $leadingSpaces = 0
        while ($leadingSpaces -lt $line.Length -and $line[$leadingSpaces] -eq ' ') {
            $leadingSpaces++
        }
        if ($leadingSpaces -lt $line.Length -and
            $line[$leadingSpaces] -eq "`t" -and
            $leadingSpaces -le 4) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "non-canonical workflow '$WorkflowPath' uses ambiguous jobs indentation at line $($lineIndex + 1).")
        }
        if ($leadingSpaces -eq 0) { break }
        if ($leadingSpaces -eq 2) {
            if ($null -ne $currentJobId -and $currentDirectPropertyCount -eq 0) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' job '$currentJobId' has no strict direct property.")
            }
            $jobMatch = [regex]::Match(
                $line,
                $script:NonCanonicalWorkflowJobIdPattern,
                [Text.RegularExpressions.RegexOptions]::CultureInvariant)
            if (-not $jobMatch.Success) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' job IDs must be plain block keys without quotes, escapes, flow mappings, anchors or aliases; line=$($lineIndex + 1).")
            }
            $currentJobId = $jobMatch.Groups['JobId'].Value
            if (-not $jobIds.Add($currentJobId)) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' contains duplicate job ID '$currentJobId'.")
            }
            $currentDirectPropertyCount = 0
            $currentDirectNameCount = 0
            continue
        }
        if ($null -eq $currentJobId) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "non-canonical workflow '$WorkflowPath' contains jobs content before one strict job ID.")
        }
        if ($leadingSpaces -lt 4) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "non-canonical workflow '$WorkflowPath' uses non-canonical job indentation at line $($lineIndex + 1).")
        }
        if ($leadingSpaces -eq 4) {
            $propertyMatch = [regex]::Match(
                $line,
                $script:NonCanonicalWorkflowDirectJobPropertyPattern,
                [Text.RegularExpressions.RegexOptions]::CultureInvariant)
            if (-not $propertyMatch.Success) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' direct job properties must use plain block keys; line=$($lineIndex + 1).")
            }
            $currentDirectPropertyCount++
            if ($propertyMatch.Groups['Key'].Value -ceq 'name') {
                $currentDirectNameCount++
                if ($currentDirectNameCount -ne 1 -or
                    -not [regex]::IsMatch(
                        $line,
                        $script:NonCanonicalWorkflowDirectJobNamePattern,
                        [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                    Stop-MigrationValidation -Code 'TRUST' -Message (
                        "non-canonical workflow '$WorkflowPath' direct job name must be one plain static value without quotes, escapes, block scalars, anchors, aliases or expressions; job='$currentJobId'.")
                }
            }
            continue
        }
        if ($currentDirectPropertyCount -eq 0) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "non-canonical workflow '$WorkflowPath' job '$currentJobId' must establish a strict direct property before nested content.")
        }
    }
    if ($null -ne $currentJobId -and $currentDirectPropertyCount -eq 0) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "non-canonical workflow '$WorkflowPath' job '$currentJobId' has no strict direct property.")
    }
    if ($jobIds.Count -eq 0) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "non-canonical workflow '$WorkflowPath' must contain at least one strict plain job ID.")
    }
}

function Assert-NoCompetingWorkflowTrustReferences {
    param([Parameter(Mandatory)][string]$Revision)

    $workflowPaths = @((Get-RevisionPaths -Revision $Revision) | Where-Object {
        $_.StartsWith('.github/workflows/', [StringComparison]::Ordinal) -and
            ($_.EndsWith('.yml', [StringComparison]::Ordinal) -or
                $_.EndsWith('.yaml', [StringComparison]::Ordinal))
    })
    if (@($workflowPaths | Where-Object {
        [string]$_ -ceq $script:CanonicalWorkflowPath
    }).Count -ne 1) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "target revision must contain exactly one canonical workflow '$($script:CanonicalWorkflowPath)'.")
    }

    foreach ($workflowPath in $workflowPaths) {
        if ([string]$workflowPath -ceq $script:CanonicalWorkflowPath) { continue }

        $workflowText = Get-StrictUtf8GitBlobText -Revision $Revision -Path ([string]$workflowPath)
        foreach ($reservedToken in $script:ReservedWorkflowTrustReferenceTokens) {
            if ($workflowText.Contains([string]$reservedToken, [StringComparison]::Ordinal)) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$workflowPath' reuses reserved trust token '$reservedToken'.")
            }
        }
        Assert-StrictNonCanonicalWorkflowIdentityGrammar `
            -WorkflowPath ([string]$workflowPath) `
            -Text $workflowText
    }
}

function Assert-TargetWorkflowTrustClosure {
    param([Parameter(Mandatory)][string]$Revision)

    $workflowPath = $script:CanonicalWorkflowPath
    Assert-NoCompetingWorkflowTrustReferences -Revision $Revision
    $text = (Get-StrictUtf8GitBlobText -Revision $Revision -Path $workflowPath).
        Replace("`r`n", "`n").Replace("`r", "`n")
    $requiredHeader = @'
name: aicopilot-ci

on:
  workflow_dispatch:
  push:
    branches: [main]
    paths:
      - "src/**"
      - "deploy/**"
      - "docs/**"
      - "scripts/**"
      - ".dockerignore"
      - ".gitattributes"
      - "AGENTS.md"
      - "Directory.Build.props"
      - "Directory.Build.targets"
      - "global.json"
      - "AICopilot.slnx"
      - ".github/CODEOWNERS"
      - ".github/workflows/**"
  pull_request: {}

permissions:
  contents: read

jobs:
'@
    $requiredGate = @'
      - name: Validate trusted AICopilot governance migration
        shell: pwsh
        run: |
          # AI-TEST-GOV-MIG-TRUSTED-EXECUTOR-V1
          $eventName = '${{ github.event_name }}'
          $candidate = '${{ github.event.pull_request.head.sha || github.sha }}'
          if ($eventName -eq 'workflow_dispatch') {
            if ('${{ github.ref }}' -ne 'refs/heads/main') {
              throw 'Manual AICopilot governance validation must run from refs/heads/main.'
            }
            $trustedBase = (git rev-parse origin/main | Out-String).Trim()
            $relationship = 'HeadAncestorOfBase'
          }
          else {
            $trustedBase = '${{ github.event.pull_request.base.sha || github.event.before }}'
            $relationship = 'BaseAncestorOfHead'
          }
          $trustedWrapperPath = 'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1'
          $entry = (git ls-tree $trustedBase -- $trustedWrapperPath | Out-String).Trim()
          $entryPattern = '^100644 blob (?<ObjectId>[0-9a-f]+)\t' + [regex]::Escape($trustedWrapperPath) + '$'
          if ($LASTEXITCODE -ne 0 -or $entry -notmatch $entryPattern) {
            throw 'Trusted base does not contain the reviewed AICopilot migration wrapper.'
          }
          $temporaryWrapper = Join-Path $env:RUNNER_TEMP 'aicopilot-governance-migration-wrapper.ps1'
          try {
            & git cat-file blob $Matches.ObjectId > $temporaryWrapper
            if ($LASTEXITCODE -ne 0) { throw 'Could not extract the trusted AICopilot migration wrapper.' }
            $extractedObjectId = (git hash-object --no-filters -- $temporaryWrapper | Out-String).Trim()
            if ($LASTEXITCODE -ne 0 -or $extractedObjectId -cne $Matches.ObjectId) {
              throw 'Extracted AICopilot migration wrapper differs from the trusted Git blob.'
            }
            & pwsh -NoLogo -NoProfile -NonInteractive -File $temporaryWrapper `
              -RepositoryRoot . `
              -TrustedBaseRevision $trustedBase `
              -CandidateRevision $candidate `
              -AnchorRelationship $relationship
            if ($LASTEXITCODE -ne 0) {
              throw "Trusted AICopilot migration validation failed with exit code $LASTEXITCODE."
            }
          }
          finally {
            Remove-Item $temporaryWrapper -Force -ErrorAction SilentlyContinue
          }
'@
    $requiredSelfTestJob = @'
  migration-validator-selftest:
    runs-on: ubuntu-24.04
    timeout-minutes: 25

    steps:
      - name: Checkout untrusted candidate validator
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7
        with:
          fetch-depth: 0
          persist-credentials: false
          ref: ${{ github.event.pull_request.head.sha || github.sha }}

      - name: Fetch trusted origin/main without persisted credentials
        shell: pwsh
        env:
          AICOPILOT_GITHUB_TOKEN: ${{ github.token }}
        run: |
          if ([string]::IsNullOrWhiteSpace($env:AICOPILOT_GITHUB_TOKEN)) {
            throw 'A read-only GitHub token is required to fetch trusted origin/main.'
          }
          $authorization = [Convert]::ToBase64String(
            [Text.Encoding]::UTF8.GetBytes("x-access-token:$($env:AICOPILOT_GITHUB_TOKEN)"))
          Write-Output "::add-mask::$authorization"
          & git -c "http.extraheader=AUTHORIZATION: basic $authorization" fetch --no-tags origin '+refs/heads/main:refs/remotes/origin/main'
          if ($LASTEXITCODE -ne 0) { throw 'Could not fetch trusted origin/main.' }
          Remove-Item Env:AICOPILOT_GITHUB_TOKEN -ErrorAction SilentlyContinue
          Remove-Variable authorization -ErrorAction SilentlyContinue

      - name: Run base-owned AICopilot governance migration self-tests
        shell: pwsh
        run: |
          # AI-TEST-GOV-MIG-TRUSTED-SELFTEST-V1
          $eventName = '${{ github.event_name }}'
          if ($eventName -eq 'workflow_dispatch') {
            if ('${{ github.ref }}' -ne 'refs/heads/main') {
              throw 'Manual AICopilot governance self-tests must run from refs/heads/main.'
            }
            $trustedBase = (git rev-parse origin/main | Out-String).Trim()
          }
          else {
            $trustedBase = '${{ github.event.pull_request.base.sha || github.event.before }}'
          }
          $candidateValidator = (Resolve-Path 'scripts/tests/baselines/migrations/ValidateAICopilotGovernanceMigration.v1.ps1').Path
          $trustedHarnessAssets = @(
            'scripts/tests/baselines/migrations/TestAICopilotGovernanceMigrationValidator.v1.ps1',
            'scripts/tests/baselines/migrations/InvokeAICopilotGovernanceMigrationFromTrustedBase.v1.ps1',
            'scripts/tests/baselines/migrations/aicopilot-governance-migration-receipt.schema.json'
          )
          $temporaryHarnessRoot = Join-Path $env:RUNNER_TEMP "aicopilot-governance-migration-harness-$([Guid]::NewGuid().ToString('N'))"
          try {
            [IO.Directory]::CreateDirectory($temporaryHarnessRoot) | Out-Null
            foreach ($assetPath in $trustedHarnessAssets) {
              $entry = (git ls-tree $trustedBase -- $assetPath | Out-String).Trim()
              $entryPattern = '^100644 blob (?<ObjectId>[0-9a-f]+)\t' + [regex]::Escape($assetPath) + '$'
              if ($LASTEXITCODE -ne 0 -or $entry -notmatch $entryPattern) {
                throw "Trusted base does not contain reviewed mode-100644 harness asset '$assetPath'."
              }
              $objectId = $Matches.ObjectId
              $destination = Join-Path $temporaryHarnessRoot ([IO.Path]::GetFileName($assetPath))
              & git cat-file blob $objectId > $destination
              if ($LASTEXITCODE -ne 0) { throw "Could not extract trusted harness asset '$assetPath'." }
              $extractedObjectId = (git hash-object --no-filters -- $destination | Out-String).Trim()
              if ($LASTEXITCODE -ne 0 -or $extractedObjectId -cne $objectId) {
                throw "Extracted harness asset differs from trusted Git blob '$assetPath'."
              }
            }
            $temporarySelfTest = Join-Path $temporaryHarnessRoot 'TestAICopilotGovernanceMigrationValidator.v1.ps1'
            & pwsh -NoLogo -NoProfile -NonInteractive -File $temporarySelfTest `
              -ValidatorPath $candidateValidator
            if ($LASTEXITCODE -ne 0) {
              throw "Trusted AICopilot governance migration self-tests failed with exit code $LASTEXITCODE."
            }
          }
          finally {
            Remove-Item $temporaryHarnessRoot -Recurse -Force -ErrorAction SilentlyContinue
          }
'@
    $requiredBuildPrefix = @'
  build-test:
    runs-on: ubuntu-24.04
    timeout-minutes: 25

    steps:
      - name: Checkout
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7
        with:
          fetch-depth: 0
          persist-credentials: false
          ref: ${{ github.event.pull_request.head.sha || github.sha }}

      - name: Fetch trusted origin/main without persisted credentials
        shell: pwsh
        env:
          AICOPILOT_GITHUB_TOKEN: ${{ github.token }}
        run: |
          if ([string]::IsNullOrWhiteSpace($env:AICOPILOT_GITHUB_TOKEN)) {
            throw 'A read-only GitHub token is required to fetch trusted origin/main.'
          }
          $authorization = [Convert]::ToBase64String(
            [Text.Encoding]::UTF8.GetBytes("x-access-token:$($env:AICOPILOT_GITHUB_TOKEN)"))
          Write-Output "::add-mask::$authorization"
          & git -c "http.extraheader=AUTHORIZATION: basic $authorization" fetch --no-tags origin '+refs/heads/main:refs/remotes/origin/main'
          if ($LASTEXITCODE -ne 0) { throw 'Could not fetch trusted origin/main.' }
          Remove-Item Env:AICOPILOT_GITHUB_TOKEN -ErrorAction SilentlyContinue
          Remove-Variable authorization -ErrorAction SilentlyContinue
'@
    $requiredAfterGate = @'

      - name: Run AICopilot test governance self-tests
        shell: pwsh
        run: |
          ./scripts/tests/TestAICopilotTestGovernanceBehavior.ps1
          ./scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release

      - name: Setup .NET
        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5
        with:
          dotnet-version: "10.0.301"

      - name: Setup Node
        uses: actions/setup-node@48b55a011bda9f5d6aeb4c2d9c7362e8dae4041e # v6
        with:
          node-version: "22"
          cache: "npm"
          cache-dependency-path: "src/vues/AICopilot.Web/package-lock.json"

      - name: Verify .NET SDK
        run: test "$(dotnet --version)" = "10.0.301"

      - name: Restore AICopilot solution
        run: dotnet restore AICopilot.slnx

      - name: Restore web dependencies
        working-directory: src/vues/AICopilot.Web
        run: npm ci

      - name: Enforce incremental deployment policy
        shell: pwsh
        run: ./deploy/enterprise-ai/tests/TestDeploymentPolicy.ps1

      - name: Require Linux Docker runtime
        run: docker info

      - name: Build AICopilot solution
        run: dotnet build AICopilot.slnx -c Release --no-restore

      - name: Validate AICopilot test repository and discovery
        shell: pwsh
        run: |
          ./scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateRepository -Configuration Release
          ./scripts/tests/TestAICopilotTestGovernancePolicy.ps1 -Mode ValidateDiscovery -Configuration Release

      - name: Run architecture tests
        run: dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=architecture.trx" --results-directory artifacts/test-results

      - name: Run deterministic AI eval tests
        run: dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=ai-eval.trx" --results-directory artifacts/test-results

      - name: Run backend tests
        run: dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=backend.trx" --results-directory artifacts/test-results

      - name: Run deployment behavior tests
        shell: bash
        run: |
          set -euo pipefail
          bash deploy/enterprise-ai/tests/deployment-behavior.sh 2>&1 | tee artifacts/test-results/deployment-behavior.log

      - name: Test and build web app
        working-directory: src/vues/AICopilot.Web
        run: |
          npm run test:unit -- --reporter=json --outputFile=../../../artifacts/test-results/vitest.json
          npm run build

      - name: Reconcile required test results
        shell: pwsh
        run: |
          $expected = @{
            'architecture.trx' = 91
            'ai-eval.trx' = 6
            'backend.trx' = 924
          }
          foreach ($entry in $expected.GetEnumerator()) {
            $path = Join-Path 'artifacts/test-results' $entry.Key
            if (-not (Test-Path $path -PathType Leaf)) {
              throw "Missing required TRX: $path"
            }
            [xml]$trx = Get-Content $path -Raw
            $counters = $trx.TestRun.ResultSummary.Counters
            $total = [int]$counters.total
            $passed = [int]$counters.passed
            $failed = [int]$counters.failed
            $notExecuted = [int]$counters.notExecuted
            if ($total -ne $entry.Value -or $passed -ne $entry.Value -or $failed -ne 0 -or $notExecuted -ne 0) {
              throw "$($entry.Key) reconciliation failed: expected=$($entry.Value), total=$total, passed=$passed, failed=$failed, notExecuted=$notExecuted"
            }
          }

          $vitestPath = 'artifacts/test-results/vitest.json'
          if (-not (Test-Path $vitestPath -PathType Leaf)) {
            throw "Missing required Vitest result: $vitestPath"
          }
          $vitest = Get-Content $vitestPath -Raw | ConvertFrom-Json
          if (-not [bool]$vitest.success -or
              [int]$vitest.numTotalTests -ne 184 -or
              [int]$vitest.numPassedTests -ne 184 -or
              [int]$vitest.numFailedTests -ne 0 -or
              [int]$vitest.numPendingTests -ne 0 -or
              @($vitest.testResults).Count -ne 31) {
            throw "Vitest reconciliation failed: files=$(@($vitest.testResults).Count), total=$($vitest.numTotalTests), passed=$($vitest.numPassedTests), failed=$($vitest.numFailedTests), pending=$($vitest.numPendingTests)"
          }

          $deploymentPath = 'artifacts/test-results/deployment-behavior.log'
          if (-not (Test-Path $deploymentPath -PathType Leaf)) {
            throw "Missing required deployment behavior log: $deploymentPath"
          }
          $deploymentLines = @(Get-Content $deploymentPath)
          $deploymentCases = @($deploymentLines | Where-Object { $_ -match '^TEST ' }).Count
          if ($deploymentCases -ne 33 -or
              @($deploymentLines | Where-Object { $_ -eq 'NON_PRODUCTION_MECHANISM_TEST productionEligible=false result=passed' }).Count -ne 1 -or
              @($deploymentLines | Where-Object { $_ -eq 'All AICopilot deployment behavior tests passed.' }).Count -ne 1) {
            throw "Deployment behavior reconciliation failed: cases=$deploymentCases"
          }

      - name: Upload required test evidence
        if: always()
        uses: actions/upload-artifact@b7c566a772e6b6bfb58ed0dc250532a479d7789f # v6
        with:
          name: aicopilot-required-test-results
          path: artifacts/test-results/**
          if-no-files-found: warn
'@
    $requiredFinalJob = @'
  required-final:
    needs:
      - migration-validator-selftest
      - build-test
    if: ${{ always() }}
    runs-on: ubuntu-24.04
    timeout-minutes: 1

    steps:
      - name: Require successful AICopilot self-test and build-test
        shell: bash
        env:
          MIGRATION_SELFTEST_RESULT: ${{ needs.migration-validator-selftest.result }}
          BUILD_TEST_RESULT: ${{ needs.build-test.result }}
        run: |
          test "$MIGRATION_SELFTEST_RESULT" = "success"
          test "$BUILD_TEST_RESULT" = "success"
'@

    $header = $requiredHeader.TrimEnd("`r", "`n")
    $selfTestJob = $requiredSelfTestJob.TrimEnd("`r", "`n")
    $buildPrefix = $requiredBuildPrefix.TrimEnd("`r", "`n")
    $gate = $requiredGate.TrimEnd("`r", "`n")
    $afterGate = $requiredAfterGate.TrimEnd("`r", "`n")
    $finalJob = $requiredFinalJob.TrimEnd("`r", "`n")
    $canonicalWorkflow = "$header`n$selfTestJob`n`n$buildPrefix`n$gate$afterGate`n`n$finalJob`n"
    if ($text -cne $canonicalWorkflow) {
        $firstDifference = 0
        $sharedLength = [Math]::Min($text.Length, $canonicalWorkflow.Length)
        while ($firstDifference -lt $sharedLength -and
            $text[$firstDifference] -ceq $canonicalWorkflow[$firstDifference]) {
            $firstDifference++
        }
        $actualHash = Get-Sha256Hex -Bytes ([Text.Encoding]::UTF8.GetBytes($text))
        $expectedHash = Get-Sha256Hex -Bytes ([Text.Encoding]::UTF8.GetBytes($canonicalWorkflow))
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' must equal the complete canonical required workflow byte-for-byte after LF normalization; " +
            "firstDifference=$firstDifference actualLength=$($text.Length) expectedLength=$($canonicalWorkflow.Length) " +
            "actualSha256=$actualHash expectedSha256=$expectedHash.")
    }

    $topLevelLines = @($text.Split("`n") | Where-Object {
        $_ -match '^\S' -and -not $_.StartsWith('#', [StringComparison]::Ordinal)
    })
    if (($topLevelLines -join '|') -cne 'name: aicopilot-ci|on:|permissions:|jobs:') {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' top-level trust envelope is not canonical.")
    }
    $jobsText = $text.Substring($text.IndexOf("jobs:`n", [StringComparison]::Ordinal) + 6)
    if ([regex]::Matches($text, '(?m)^jobs:\s*$').Count -ne 1 -or
        [regex]::Matches($jobsText, '(?m)^  migration-validator-selftest:\s*$').Count -ne 1 -or
        [regex]::Matches($jobsText, '(?m)^  build-test:\s*$').Count -ne 1 -or
        [regex]::Matches($jobsText, '(?m)^  required-final:\s*$').Count -ne 1 -or
        [regex]::Matches($jobsText, '(?m)^  [A-Za-z0-9_-]+:\s*$').Count -ne 3) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' must contain only the isolated self-test, canonical build-test, and fail-closed required-final jobs.")
    }

    $buildStart = $text.IndexOf("  build-test:`n", [StringComparison]::Ordinal)
    $finalStart = $text.IndexOf("  required-final:`n", [StringComparison]::Ordinal)
    if ($buildStart -lt 0 -or $finalStart -le $buildStart) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' required job order is not canonical.")
    }
    $buildText = $text.Substring($buildStart, $finalStart - $buildStart)
    $buildDirectLines = @($buildText.Split("`n") | Where-Object {
        $_ -match '^    \S' -and
        -not $_.Substring(4).StartsWith('#', [StringComparison]::Ordinal)
    })
    if (($buildDirectLines -join '|') -cne
        '    runs-on: ubuntu-24.04|    timeout-minutes: 25|    steps:') {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' build-test direct keys are not canonical.")
    }
    if ([regex]::IsMatch($buildText, '(?m)^    (?:if|needs|continue-on-error)\s*:')) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' build-test must run in parallel and cannot be conditional or continue on error.")
    }

    $finalText = $text.Substring($finalStart)
    $finalDirectLines = @($finalText.Split("`n") | Where-Object {
        $_ -match '^    \S' -and
        -not $_.Substring(4).StartsWith('#', [StringComparison]::Ordinal)
    })
    if (($finalDirectLines -join '|') -cne
        '    needs:|    if: ${{ always() }}|    runs-on: ubuntu-24.04|    timeout-minutes: 1|    steps:') {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' required-final direct keys are not canonical.")
    }
    if ([regex]::IsMatch($finalText, '(?m)^    continue-on-error\s*:') -or
        $finalText.Contains('actions/checkout', [StringComparison]::Ordinal) -or
        $finalText.Contains('CandidateRevision', [StringComparison]::Ordinal) -or
        $finalText.Contains('candidateValidator', [StringComparison]::Ordinal)) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' required-final must only aggregate results and cannot check out or execute candidate content.")
    }

    foreach ($contract in @(
        @{ Value = 'AI-TEST-GOV-MIG-TRUSTED-EXECUTOR-V1'; Count = 1 },
        @{ Value = 'AI-TEST-GOV-MIG-TRUSTED-SELFTEST-V1'; Count = 1 },
        @{ Value = $script:TrustedWrapperPath; Count = 2 },
        @{ Value = $script:SelfTestPath; Count = 1 },
        @{ Value = $script:SchemaPath; Count = 1 },
        @{ Value = $script:ValidatorPath; Count = 1 },
        @{ Value = '-ValidatorPath $candidateValidator'; Count = 1 },
        @{ Value = 'required-final:'; Count = 1 },
        @{ Value = 'if: ${{ always() }}'; Count = 1 },
        @{ Value = 'MIGRATION_SELFTEST_RESULT: ${{ needs.migration-validator-selftest.result }}'; Count = 1 },
        @{ Value = 'BUILD_TEST_RESULT: ${{ needs.build-test.result }}'; Count = 1 },
        @{ Value = 'AICOPILOT_GITHUB_TOKEN: ${{ github.token }}'; Count = 2 },
        @{ Value = 'git -c "http.extraheader=AUTHORIZATION: basic $authorization" fetch --no-tags origin'; Count = 2 },
        @{ Value = 'Remove-Item Env:AICOPILOT_GITHUB_TOKEN -ErrorAction SilentlyContinue'; Count = 2 },
        @{ Value = 'persist-credentials: false'; Count = 2 },
        @{ Value = 'ref: ${{ github.event.pull_request.head.sha || github.sha }}'; Count = 2 },
        @{ Value = 'uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7'; Count = 2 }
    )) {
        if ((Get-OrdinalOccurrenceCount -Text $text -Value ([string]$contract.Value)) -ne
            [int]$contract.Count) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "'$workflowPath' trusted contract '$($contract.Value)' must appear exactly once.")
        }
    }
    if ($text.Contains('pull_request_target', [StringComparison]::Ordinal) -or
        $text.Contains('refs/pull/', [StringComparison]::Ordinal) -or
        $text.Contains('github.event.pull_request.merge_commit_sha', [StringComparison]::Ordinal)) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' cannot validate or check out a synthetic merge revision.")
    }
}

function Get-FileState {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    $entry = Get-GitEntry -Revision $Revision -Path $Path -AllowMissing
    if ($null -eq $entry) {
        return [pscustomobject]@{ Mode = $null; Sha256 = $null }
    }

    $bytes = (Invoke-GitBytes -Arguments @('cat-file', 'blob', $entry.ObjectId)).Bytes
    return [pscustomobject]@{
        Mode = $entry.Mode
        Sha256 = Get-Sha256Hex -Bytes $bytes
    }
}

function Get-DiffRecords {
    param(
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision
    )

    $result = Invoke-GitBytes -Arguments @(
        'diff-tree', '-r', '--no-commit-id', '--name-status', '--no-renames', '-z',
        $BaseRevision, $TargetRevision)
    if ($result.Bytes.Length -eq 0) {
        return @()
    }

    $nul = [char[]]@([char]0)
    $diffText = ConvertFrom-StrictUtf8Bytes `
        -Bytes $result.Bytes `
        -Code 'DIFF' `
        -Context "git diff $BaseRevision..$TargetRevision"
    $parts = @($diffText.TrimEnd($nul).Split($nul, [StringSplitOptions]::None))
    if ($parts.Count % 2 -ne 0) {
        Stop-MigrationValidation -Code 'DIFF' -Message 'git diff-tree returned an invalid name-status stream.'
    }

    $records = [Collections.Generic.List[object]]::new()
    $caseLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    for ($index = 0; $index -lt $parts.Count; $index += 2) {
        $status = $parts[$index]
        $path = $parts[$index + 1]
        if ($status -cnotin @('A', 'M', 'D')) {
            Stop-MigrationValidation -Code 'DIFF' -Message "unsupported git status '$status' for '$path'."
        }
        Assert-SafeRepositoryPath -Path $path
        if ($caseLedger.ContainsKey($path) -and $caseLedger[$path] -cne $path) {
            Stop-MigrationValidation -Code 'PATH' -Message (
                "case-colliding diff paths '$($caseLedger[$path])' and '$path'.")
        }
        $caseLedger[$path] = $path

        $before = Get-FileState -Revision $BaseRevision -Path $path
        $after = Get-FileState -Revision $TargetRevision -Path $path
        if (($status -ceq 'A' -and ($null -ne $before.Mode -or $null -eq $after.Mode)) -or
            ($status -ceq 'M' -and ($null -eq $before.Mode -or $null -eq $after.Mode)) -or
            ($status -ceq 'D' -and ($null -eq $before.Mode -or $null -ne $after.Mode))) {
            Stop-MigrationValidation -Code 'DIFF' -Message "status '$status' disagrees with '$path' blob presence."
        }

        $records.Add([pscustomobject][ordered]@{
            path = $path
            status = $status
            beforeMode = $before.Mode
            beforeSha256 = $before.Sha256
            afterMode = $after.Mode
            afterSha256 = $after.Sha256
        })
    }

    return @(Get-OrdinalSortedChangeRecords -Values @($records))
}

function Test-IsReceiptStatePath {
    param([Parameter(Mandatory)][string]$Path)

    return $Path.StartsWith($script:PendingRoot, [StringComparison]::Ordinal) -or
        $Path.StartsWith($script:ConsumedRoot, [StringComparison]::Ordinal) -or
        $Path.StartsWith($script:CancelledRoot, [StringComparison]::Ordinal)
}

function Test-IsTrustImplementationPath {
    param([Parameter(Mandatory)][string]$Path)

    return $script:TrustImplementationPaths -ccontains $Path
}

function Test-IsV1TrustUpgradePath {
    param([Parameter(Mandatory)][string]$Path)

    return $script:V1TrustUpgradePaths -ccontains $Path
}

function Test-IsBaseOwnedHarnessPath {
    param([Parameter(Mandatory)][string]$Path)

    return $script:BaseOwnedHarnessPaths -ccontains $Path
}

function Assert-TrustImplementationAssets {
    param([Parameter(Mandatory)][string]$Revision)

    foreach ($path in $script:TrustImplementationPaths) {
        try {
            $entry = Get-GitEntry -Revision $Revision -Path $path
        }
        catch {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "trusted implementation asset '$path' is missing at $Revision.")
        }
        if ($entry.Mode -cne '100644') {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "trusted implementation asset '$path' must use mode 100644 at $Revision.")
        }
    }
}

function Test-IsProtectedPath {
    param([Parameter(Mandatory)][string]$Path)

    $exactPaths = @(
        '.github/CODEOWNERS',
        '.gitattributes',
        'global.json',
        'Directory.Build.props',
        'Directory.Build.targets',
        'Directory.Packages.props',
        'NuGet.Config',
        'AICopilot.slnx',
        '.config/dotnet-tools.json',
        'scripts/tests/TestAICopilotTestGovernancePolicy.ps1',
        'scripts/tests/TestAICopilotTestGovernanceBehavior.ps1',
        'deploy/enterprise-ai/tests/TestDeploymentPolicy.ps1',
        'deploy/enterprise-ai/tests/deployment-behavior.sh',
        'src/tests/Directory.Build.props',
        'src/tests/xunit.runner.json',
        'src/vues/AICopilot.Web/package.json',
        'src/vues/AICopilot.Web/package-lock.json',
        'src/vues/AICopilot.Web/vitest.config.ts',
        'src/vues/AICopilot.Web/playwright.smoke.config.ts'
    )
    if ($Path -in $exactPaths) { return $true }
    if ($Path.StartsWith('.github/workflows/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('.github/actions/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('scripts/tests/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('src/analyzers/', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith('src/architecture/', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith('src/testing/', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith('src/tests/', [StringComparison]::OrdinalIgnoreCase) -or
        ($Path.StartsWith('deploy/', [StringComparison]::OrdinalIgnoreCase) -and
            $Path.Contains('/tests/', [StringComparison]::OrdinalIgnoreCase)) -or
        $Path.StartsWith('src/vues/AICopilot.Web/tests/unit/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('src/vues/AICopilot.Web/tests/smoke/', [StringComparison]::Ordinal) -or
        $Path.Equals('.editorconfig', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.EndsWith('/.editorconfig', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.Equals('packages.lock.json', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.EndsWith('/packages.lock.json', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.Equals('NuGet.Config', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.EndsWith('/NuGet.Config', [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $extension = [IO.Path]::GetExtension($Path)
    return $extension -in @('.csproj', '.fsproj', '.vbproj', '.props', '.targets', '.rsp')
}

function Get-ProtectedManifestDigest {
    param([Parameter(Mandatory)][string]$Revision)

    $lines = [Collections.Generic.List[string]]::new()
    foreach ($path in (Get-RevisionPaths -Revision $Revision)) {
        if (-not (Test-IsProtectedPath -Path $path) -or (Test-IsReceiptStatePath -Path $path)) {
            continue
        }
        $state = Get-FileState -Revision $Revision -Path $path
        $lines.Add("$($state.Mode)`0$($state.Sha256)`0$path`n")
    }

    return Get-Sha256Hex -Bytes ([Text.Encoding]::UTF8.GetBytes(($lines -join '')))
}

function Get-SolutionProjectPaths {
    param([Parameter(Mandatory)][string]$Revision)

    try {
        [xml]$solution = Get-GitBlobText -Revision $Revision -Path 'AICopilot.slnx'
    }
    catch {
        Stop-MigrationValidation -Code 'COUNTS' -Message "could not parse AICopilot.slnx at ${Revision}: $($_.Exception.Message)"
    }

    $projects = @($solution.SelectNodes("//*[local-name()='Project']") | ForEach-Object {
        [string]$_.Path
    })
    foreach ($project in $projects) { Assert-SafeRepositoryPath -Path $project }
    return @(Get-OrdinalSortedStrings -Values $projects -Unique)
}

function Get-BaselineState {
    param([Parameter(Mandatory)][string]$Revision)

    $baselineBytes = Get-GitBlobBytes -Revision $Revision -Path $script:BaselinePath
    try {
        $baselineJson = [Text.UTF8Encoding]::new($false, $true).GetString($baselineBytes)
        $baseline = ConvertFrom-StrictJson -Json $baselineJson -Location "$($script:BaselinePath)@$Revision"
    }
    catch {
        Stop-MigrationValidation -Code 'COUNTS' -Message "could not parse baseline at ${Revision}: $($_.Exception.Message)"
    }

    if (@($baseline.PSObject.Properties.Name) -cnotcontains 'projects' -or
        $baseline.projects -isnot [Array]) {
        Stop-MigrationValidation -Code 'COUNTS' -Message 'baseline.projects must be a JSON array.'
    }
    $projects = @($baseline.projects)
    $solutionProjects = @(Get-SolutionProjectPaths -Revision $Revision)
    $solutionProjectSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$solutionProjects, [StringComparer]::Ordinal)
    $projectPathLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $declarations = [long]0
    $executionTemplates = [long]0
    $projectedCases = [long]0
    $runnerCases = [long]0
    foreach ($project in $projects) {
        if ($null -eq $project -or $project -is [Array] -or $project -is [string] -or
            $project -is [ValueType]) {
            Stop-MigrationValidation -Code 'COUNTS' -Message 'baseline.projects[] must be a JSON object.'
        }
        $requiredNames = @(
            'projectPath', 'baselineDeclarations', 'baselineExecutionTemplates',
            'baselineProjectedCases', 'baselineRunnerCases')
        $actualNames = @($project.PSObject.Properties.Name)
        foreach ($requiredName in $requiredNames) {
            if ($actualNames -cnotcontains $requiredName) {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "baseline project is missing '$requiredName'.")
            }
        }
        if ($project.projectPath -isnot [string]) {
            Stop-MigrationValidation -Code 'COUNTS' -Message 'baseline projectPath must be a JSON string.'
        }
        $projectPath = [string]$project.projectPath
        Assert-SafeRepositoryPath -Path $projectPath
        if ($projectPathLedger.ContainsKey($projectPath)) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline contains duplicate or case-colliding project '$projectPath'.")
        }
        $projectPathLedger[$projectPath] = $projectPath
        $projectEntry = Get-GitEntry -Revision $Revision -Path $projectPath -AllowMissing
        if ($null -eq $projectEntry) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline test project '$projectPath' is not a tracked file at $Revision.")
        }
        if ($projectEntry.Mode -cne '100644') {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline test project '$projectPath' must use mode 100644 at $Revision.")
        }
        if (-not $solutionProjectSet.Contains($projectPath)) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline test project '$projectPath' is not an ordinal-exact AICopilot.slnx project at $Revision.")
        }
        foreach ($countName in $requiredNames[1..4]) {
            $countValue = $project.$countName
            if (($countValue -isnot [long] -and $countValue -isnot [int]) -or
                [long]$countValue -lt 0) {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "baseline $projectPath.$countName must be a non-negative integer.")
            }
        }
        $declarations += [long]$project.baselineDeclarations
        $executionTemplates += [long]$project.baselineExecutionTemplates
        $projectedCases += [long]$project.baselineProjectedCases
        $runnerCases += [long]$project.baselineRunnerCases
    }
    $revisionPaths = @(Get-RevisionPaths -Revision $Revision)
    $testProjects = @(Get-OrdinalSortedStrings -Values @(
        $projects | ForEach-Object { [string]$_.projectPath }) -Unique)
    $counts = [ordered]@{
        repositoryProjects = $solutionProjects.Count
        testProjects = $testProjects.Count
        testSourceFiles = @($revisionPaths | Where-Object {
            $_.StartsWith('src/tests/', [StringComparison]::OrdinalIgnoreCase) -and
            $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)
        }).Count
        declarations = $declarations
        executionTemplates = $executionTemplates
        projectedCases = $projectedCases
        runnerCases = $runnerCases
        vitestSourceFiles = @($revisionPaths | Where-Object {
            $_.StartsWith('src/vues/AICopilot.Web/tests/unit/', [StringComparison]::Ordinal)
        }).Count
        playwrightSourceFiles = @($revisionPaths | Where-Object {
            $_.StartsWith('src/vues/AICopilot.Web/tests/smoke/', [StringComparison]::Ordinal)
        }).Count
        deploymentTestAssets = @($revisionPaths | Where-Object {
            $_.StartsWith('deploy/', [StringComparison]::OrdinalIgnoreCase) -and
            $_.Contains('/tests/', [StringComparison]::OrdinalIgnoreCase) -and
            ($_.EndsWith('.ps1', [StringComparison]::OrdinalIgnoreCase) -or
                $_.EndsWith('.sh', [StringComparison]::OrdinalIgnoreCase))
        }).Count
        workflowFiles = @($revisionPaths | Where-Object {
            $_.StartsWith('.github/workflows/', [StringComparison]::Ordinal) -and
            ($_.EndsWith('.yml', [StringComparison]::OrdinalIgnoreCase) -or
                $_.EndsWith('.yaml', [StringComparison]::OrdinalIgnoreCase))
        }).Count
    }

    return [pscustomobject]@{
        State = [pscustomobject][ordered]@{
            baselineSha256 = Get-Sha256Hex -Bytes $baselineBytes
            protectedManifestSha256 = Get-ProtectedManifestDigest -Revision $Revision
            counts = [pscustomobject]$counts
        }
        RepositoryProjects = $solutionProjects
        TestProjects = $testProjects
    }
}

function Get-ProjectChanges {
    param(
        [Parameter(Mandatory)][object]$Source,
        [Parameter(Mandatory)][object]$Target
    )

    $sourceProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Source.RepositoryProjects), [StringComparer]::Ordinal)
    $targetProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Target.RepositoryProjects), [StringComparer]::Ordinal)
    $sourceTests = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Source.TestProjects), [StringComparer]::Ordinal)
    $targetTests = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Target.TestProjects), [StringComparer]::Ordinal)
    return [pscustomobject][ordered]@{
        added = @(Get-OrdinalSortedStrings -Values @(
            $Target.RepositoryProjects | Where-Object { -not $sourceProjects.Contains($_) }))
        removed = @(Get-OrdinalSortedStrings -Values @(
            $Source.RepositoryProjects | Where-Object { -not $targetProjects.Contains($_) }))
        addedTests = @(Get-OrdinalSortedStrings -Values @(
            $Target.TestProjects | Where-Object { -not $sourceTests.Contains($_) }))
        removedTests = @(Get-OrdinalSortedStrings -Values @(
            $Source.TestProjects | Where-Object { -not $targetTests.Contains($_) }))
    }
}

function Assert-NoDuplicateJsonKeys {
    param(
        [Parameter(Mandatory)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory)][string]$Location
    )

    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) {
                Stop-MigrationValidation -Code 'RECEIPT' -Message "duplicate JSON key '$($property.Name)' at $Location."
            }
            Assert-NoDuplicateJsonKeys -Element $property.Value -Location "$Location.$($property.Name)"
        }
    }
    elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Element.EnumerateArray()) {
            Assert-NoDuplicateJsonKeys -Element $item -Location "$Location[$index]"
            $index++
        }
    }
}

function ConvertFrom-StrictJson {
    param(
        [Parameter(Mandatory)][string]$Json,
        [Parameter(Mandatory)][string]$Location
    )

    $document = $null
    try {
        $document = [Text.Json.JsonDocument]::Parse($Json)
        if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "JSON root at $Location must be an object."
        }
        Assert-NoDuplicateJsonKeys -Element $document.RootElement -Location $Location
        return ConvertFrom-JsonElement -Element $document.RootElement
    }
    catch {
        if ($_.Exception.Message.StartsWith($script:RuleId, [StringComparison]::Ordinal)) { throw }
        Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid JSON at ${Location}: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

function ConvertFrom-JsonElement {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $value = [ordered]@{}
            foreach ($property in $Element.EnumerateObject()) {
                $value[$property.Name] = ConvertFrom-JsonElement -Element $property.Value
            }
            return [pscustomobject]$value
        }
        ([Text.Json.JsonValueKind]::Array) {
            $items = [Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) {
                $items.Add((ConvertFrom-JsonElement -Element $item))
            }
            return ,$items.ToArray()
        }
        ([Text.Json.JsonValueKind]::String) { return $Element.GetString() }
        ([Text.Json.JsonValueKind]::Number) {
            $integer = [long]0
            if ($Element.TryGetInt64([ref]$integer)) { return $integer }
            $decimal = [decimal]0
            if ($Element.TryGetDecimal([ref]$decimal)) { return $decimal }
            Stop-MigrationValidation -Code 'RECEIPT' -Message "unsupported JSON number '$($Element.GetRawText())'."
        }
        ([Text.Json.JsonValueKind]::True) { return $true }
        ([Text.Json.JsonValueKind]::False) { return $false }
        ([Text.Json.JsonValueKind]::Null) { return $null }
        default {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "unsupported JSON value kind '$($Element.ValueKind)'."
        }
    }
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory)][object]$Object,
        [Parameter(Mandatory)][string[]]$Expected,
        [Parameter(Mandatory)][string]$Location
    )

    if ($null -eq $Object -or $Object -is [Array] -or $Object -is [string] -or
        $Object -is [ValueType]) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be a JSON object."
    }
    $actual = @($Object.PSObject.Properties.Name)
    $expectedSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$Expected, [StringComparer]::Ordinal)
    $actualSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$actual, [StringComparer]::Ordinal)
    $missing = @($Expected | Where-Object { -not $actualSet.Contains($_) })
    $unknown = @($actual | Where-Object { -not $expectedSet.Contains($_) })
    if ($missing.Count -gt 0 -or $unknown.Count -gt 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            "$Location properties mismatch; missing=[$($missing -join ',')] unknown=[$($unknown -join ',')].")
    }
}

function Assert-JsonString {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$Location
    )

    if ($Value -isnot [string]) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be a JSON string."
    }
}

function Assert-JsonArray {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$Location
    )

    if ($Value -isnot [Array]) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be a JSON array."
    }
}

function Assert-HashOrNull {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][bool]$MustExist,
        [Parameter(Mandatory)][string]$Location
    )

    if ($MustExist) {
        Assert-JsonString -Value $Value -Location $Location
        if ([string]$Value -cnotmatch '^[0-9a-f]{64}$') {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be one lowercase SHA-256."
        }
    }
    elseif ($null -ne $Value) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be null."
    }
}

function Assert-SortedUniqueStrings {
    param(
        [AllowNull()][object]$Values,
        [Parameter(Mandatory)][string]$Location,
        [switch]$Paths
    )

    Assert-JsonArray -Value $Values -Location $Location
    $items = @($Values)
    foreach ($item in $items) { Assert-JsonString -Value $item -Location "$Location[]" }
    if ($Paths) {
        $caseLedger = [Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($item in $items) {
            $path = [string]$item
            if ($caseLedger.ContainsKey($path)) {
                Stop-MigrationValidation -Code 'RECEIPT' -Message (
                    "$Location contains duplicate or case-colliding path '$path'.")
            }
            $caseLedger[$path] = $path
        }
    }
    $expected = @(Get-OrdinalSortedStrings -Values @(
        $items | ForEach-Object { [string]$_ }) -Unique)
    if ($items.Count -ne $expected.Count) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location contains duplicates."
    }
    for ($index = 0; $index -lt $items.Count; $index++) {
        if ([string]$items[$index] -cne $expected[$index]) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be ordinal-sorted and unique."
        }
        if ($Paths) { Assert-SafeRepositoryPath -Path ([string]$items[$index]) }
    }
}

function ConvertTo-UtcTimestamp {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Location
    )

    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact(
        $Value,
        'yyyy-MM-ddTHH:mm:ssZ',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$parsed)) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must use UTC yyyy-MM-ddTHH:mm:ssZ."
    }
    return $parsed.ToUniversalTime()
}

function Assert-StateShape {
    param(
        [Parameter(Mandatory)][object]$State,
        [Parameter(Mandatory)][string]$Location
    )

    Assert-ExactProperties -Object $State -Expected @(
        'baselineSha256', 'protectedManifestSha256', 'counts') -Location $Location
    Assert-HashOrNull -Value $State.baselineSha256 -MustExist $true -Location "$Location.baselineSha256"
    Assert-HashOrNull -Value $State.protectedManifestSha256 -MustExist $true -Location "$Location.protectedManifestSha256"
    Assert-ExactProperties -Object $State.counts -Expected @(
        'repositoryProjects', 'testProjects', 'testSourceFiles', 'declarations',
        'executionTemplates', 'projectedCases', 'runnerCases', 'vitestSourceFiles',
        'playwrightSourceFiles', 'deploymentTestAssets', 'workflowFiles') -Location "$Location.counts"
    foreach ($name in @(
        'repositoryProjects', 'testProjects', 'testSourceFiles', 'declarations',
        'executionTemplates', 'projectedCases', 'runnerCases', 'vitestSourceFiles',
        'playwrightSourceFiles', 'deploymentTestAssets', 'workflowFiles')) {
        $value = $State.counts.$name
        if ($value -isnot [long] -and $value -isnot [int]) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location.counts.$name must be an integer."
        }
        if ([long]$value -lt 0) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location.counts.$name cannot be negative."
        }
    }
}

function Assert-TestEvidenceDoesNotDecrease {
    param(
        [Parameter(Mandatory)][object]$Source,
        [Parameter(Mandatory)][object]$Target
    )

    foreach ($name in @(
        'testProjects', 'testSourceFiles', 'declarations', 'executionTemplates',
        'projectedCases', 'runnerCases', 'vitestSourceFiles',
        'playwrightSourceFiles', 'deploymentTestAssets')) {
        if ([long]$Target.counts.$name -lt [long]$Source.counts.$name) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "target $name cannot decrease during a v1 baseline migration; " +
                "source=$($Source.counts.$name) target=$($Target.counts.$name).")
        }
    }
}

function Assert-ReceiptShape {
    param(
        [Parameter(Mandatory)][object]$Receipt,
        [Parameter(Mandatory)][string]$ExpectedPath,
        [Parameter(Mandatory)][DateTimeOffset]$Now,
        [switch]$AllowExpired
    )

    Assert-ExactProperties -Object $Receipt -Expected @(
        'schemaVersion', 'ruleId', 'migrationId', 'issuedAgainstRevision',
        'issuedAtUtc', 'expiresAtUtc', 'owner', 'approvedBy', 'reason',
        'ruleIds', 'source', 'target', 'projectChanges', 'changes') -Location 'receipt'
    foreach ($name in @(
        'schemaVersion', 'ruleId', 'migrationId', 'issuedAgainstRevision',
        'issuedAtUtc', 'expiresAtUtc', 'owner', 'approvedBy', 'reason')) {
        Assert-JsonString -Value $Receipt.$name -Location "receipt.$name"
    }
    if ([string]$Receipt.schemaVersion -cne $script:ReceiptSchemaVersion -or
        [string]$Receipt.ruleId -cne $script:RuleId) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'unsupported schemaVersion or ruleId.'
    }
    if ([string]$Receipt.migrationId -cnotmatch '^AI-TEST-GOV-MIG-[A-Z0-9][A-Z0-9-]{2,80}$') {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid migrationId '$($Receipt.migrationId)'."
    }
    $expectedPendingPath = "$($script:PendingRoot)$($Receipt.migrationId).json"
    if ($ExpectedPath -cne $expectedPendingPath) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            "receipt path '$ExpectedPath' must equal '$expectedPendingPath'.")
    }
    if ([string]$Receipt.issuedAgainstRevision -cnotmatch '^[0-9a-f]{40}$') {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'issuedAgainstRevision must be one full lowercase commit SHA.'
    }
    if ([string]$Receipt.owner -cnotin $script:ApprovedOwners -or
        [string]$Receipt.approvedBy -cnotin $script:ApprovedApprovers) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'owner or approvedBy is not in the reviewed registry.'
    }
    $reasonText = [string]$Receipt.reason
    if ($reasonText -cne $reasonText.Trim() -or $reasonText.Length -lt 20 -or $reasonText.Length -gt 500) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'reason must be trimmed and contain 20-500 characters.'
    }

    $issued = ConvertTo-UtcTimestamp -Value ([string]$Receipt.issuedAtUtc) -Location 'receipt.issuedAtUtc'
    $expires = ConvertTo-UtcTimestamp -Value ([string]$Receipt.expiresAtUtc) -Location 'receipt.expiresAtUtc'
    if ($expires -le $issued -or $expires - $issued -gt $script:MaximumReceiptLifetime) {
        Stop-MigrationValidation -Code 'EXPIRY' -Message 'receipt lifetime must be positive and no longer than 7 days.'
    }
    if (-not $AllowExpired -and ($issued -gt $Now.AddMinutes(5) -or $expires -lt $Now)) {
        Stop-MigrationValidation -Code 'EXPIRY' -Message 'receipt is not currently valid.'
    }

    Assert-JsonArray -Value $Receipt.ruleIds -Location 'receipt.ruleIds'
    $ruleIdsValue = @($Receipt.ruleIds)
    if ($ruleIdsValue.Count -eq 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'ruleIds cannot be empty.'
    }
    $isTrustUpgrade = Test-TrustUpgradeRuleIdSingleton `
        -RuleIds $ruleIdsValue `
        -Location 'receipt.ruleIds'
    Assert-SortedUniqueStrings -Values $ruleIdsValue -Location 'receipt.ruleIds'
    foreach ($receiptRuleId in $ruleIdsValue) {
        if ([string]$receiptRuleId -cnotmatch '^[A-Z][A-Z0-9-]{2,80}$') {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid governed rule ID '$receiptRuleId'."
        }
        if ([string]$receiptRuleId -cnotin $script:ApprovedGovernedRuleIds) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message (
                "governed rule ID '$receiptRuleId' is not in the reviewed registry.")
        }
    }

    Assert-StateShape -State $Receipt.source -Location 'receipt.source'
    Assert-StateShape -State $Receipt.target -Location 'receipt.target'
    Assert-TestEvidenceDoesNotDecrease -Source $Receipt.source -Target $Receipt.target
    Assert-ExactProperties -Object $Receipt.projectChanges -Expected @(
        'added', 'removed', 'addedTests', 'removedTests') -Location 'receipt.projectChanges'
    foreach ($name in @('added', 'removed', 'addedTests', 'removedTests')) {
        Assert-SortedUniqueStrings -Values $Receipt.projectChanges.$name -Location "receipt.projectChanges.$name" -Paths
    }
    $addedProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Receipt.projectChanges.added), [StringComparer]::OrdinalIgnoreCase)
    $removedProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Receipt.projectChanges.removed), [StringComparer]::OrdinalIgnoreCase)
    if (@($addedProjects | Where-Object { $removedProjects.Contains($_) }).Count -ne 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            'projectChanges.added and projectChanges.removed must be disjoint.')
    }
    foreach ($addedTest in @($Receipt.projectChanges.addedTests)) {
        if (-not $addedProjects.Contains([string]$addedTest)) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message (
                'projectChanges.addedTests must be a subset of projectChanges.added.')
        }
    }
    foreach ($removedTest in @($Receipt.projectChanges.removedTests)) {
        if (-not $removedProjects.Contains([string]$removedTest)) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message (
                'projectChanges.removedTests must be a subset of projectChanges.removed.')
        }
    }

    Assert-JsonArray -Value $Receipt.changes -Location 'receipt.changes'
    $changes = @($Receipt.changes)
    if ($changes.Count -eq 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'changes cannot be empty.'
    }
    if ($changes.Count -gt $script:MaximumReceiptChanges) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            "changes exceeds the $($script:MaximumReceiptChanges)-path limit.")
    }
    $changePaths = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $hasProtectedChange = $false
    $hasTrustImplementationChange = $false
    $hasOrdinaryChange = $false
    $hasBaselineChange = $false
    $hasGovernancePolicyChange = $false
    $lastPath = $null
    foreach ($change in $changes) {
        Assert-ExactProperties -Object $change -Expected @(
            'path', 'status', 'beforeMode', 'beforeSha256', 'afterMode', 'afterSha256') -Location 'receipt.changes[]'
        Assert-JsonString -Value $change.path -Location 'receipt.changes[].path'
        Assert-JsonString -Value $change.status -Location "receipt.changes[$($change.path)].status"
        $path = [string]$change.path
        Assert-SafeRepositoryPath -Path $path
        if ($null -ne $lastPath -and [StringComparer]::Ordinal.Compare($lastPath, $path) -ge 0) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message 'changes must be ordinal-sorted by unique path.'
        }
        $lastPath = $path
        if ($changePaths.ContainsKey($path)) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "duplicate or case-colliding change path '$path'."
        }
        $changePaths[$path] = $path
        if (Test-IsReceiptStatePath -Path $path) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message 'pending/consumed receipt moves are implicit and cannot appear in changes.'
        }
        if (Test-IsBaseOwnedHarnessPath -Path $path) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "v1 receipts cannot change base-owned harness asset '$path'.")
        }
        if (Test-IsV1TrustUpgradePath -Path $path) {
            $hasTrustImplementationChange = $true
        } else {
            $hasOrdinaryChange = $true
        }
        if ($path -ceq $script:BaselinePath) { $hasBaselineChange = $true }
        if ($path -ceq $script:GovernancePolicyPath) { $hasGovernancePolicyChange = $true }
        if (Test-IsProtectedPath -Path $path) { $hasProtectedChange = $true }

        $status = [string]$change.status
        if ($status -cnotin @('A', 'M', 'D')) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid change status '$status' for '$path'."
        }
        $beforeExists = $status -cin @('M', 'D')
        $afterExists = $status -cin @('A', 'M')
        if ((Test-IsV1TrustUpgradePath -Path $path) -and
            ($status -cne 'M' -or
                [string]$change.beforeMode -cne '100644' -or
                [string]$change.afterMode -cne '100644')) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "v1 trust upgrade must modify only the existing mode-100644 validator '$path'.")
        }
        if ($beforeExists) { Assert-JsonString -Value $change.beforeMode -Location "change[$path].beforeMode" }
        if ($afterExists) { Assert-JsonString -Value $change.afterMode -Location "change[$path].afterMode" }
        if ($beforeExists -and [string]$change.beforeMode -cnotin @('100644', '100755')) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid beforeMode for '$path'."
        }
        if (-not $beforeExists -and $null -ne $change.beforeMode) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "beforeMode for added '$path' must be null."
        }
        if ($afterExists -and [string]$change.afterMode -cnotin @('100644', '100755')) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid afterMode for '$path'."
        }
        if (-not $afterExists -and $null -ne $change.afterMode) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "afterMode for deleted '$path' must be null."
        }
        Assert-HashOrNull -Value $change.beforeSha256 -MustExist $beforeExists -Location "change[$path].beforeSha256"
        Assert-HashOrNull -Value $change.afterSha256 -MustExist $afterExists -Location "change[$path].afterSha256"
    }
    if (-not $hasProtectedChange) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'a migration receipt must govern at least one protected asset change.'
    }
    if ($hasBaselineChange -and $hasGovernancePolicyChange) {
        Stop-MigrationValidation -Code 'POLICY' -Message (
            'baseline and its governance policy cannot change in the same migration receipt.')
    }
    if ($hasTrustImplementationChange -and (-not $isTrustUpgrade -or $hasOrdinaryChange)) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            'validator changes require one isolated AI-TEST-GOV-TRUST-UPGRADE-001 receipt.')
    }
    if ($isTrustUpgrade -and (-not $hasTrustImplementationChange -or $hasOrdinaryChange)) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            'AI-TEST-GOV-TRUST-UPGRADE-001 v1 may modify only the validator.')
    }
}

function Assert-ObjectJsonEqual {
    param(
        [AllowNull()][object]$Expected,
        [AllowNull()][object]$Actual,
        [Parameter(Mandatory)][string]$Location
    )

    $expectedJson = ConvertTo-CanonicalJson -Value $Expected
    $actualJson = ConvertTo-CanonicalJson -Value $Actual
    if ($expectedJson -cne $actualJson) {
        Stop-MigrationValidation -Code 'MISMATCH' -Message (
            "$Location differs from the receipt. expected=$expectedJson actual=$actualJson")
    }
}

function ConvertTo-CanonicalJsonElement {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $properties = [Collections.Generic.List[object]]::new()
            foreach ($property in $Element.EnumerateObject()) {
                $properties.Add([pscustomobject]@{
                    Name = $property.Name
                    Value = $property.Value.Clone()
                })
            }
            $comparer = [Collections.Generic.Comparer[object]]::Create(
                [Comparison[object]]{
                    param($left, $right)
                    return [StringComparer]::Ordinal.Compare([string]$left.Name, [string]$right.Name)
                })
            $properties.Sort($comparer)
            $members = @($properties | ForEach-Object {
                $nameJson = [Text.Json.JsonSerializer]::Serialize(
                    [string]$_.Name,
                    $script:JsonSerializerOptions)
                "$nameJson`:$(ConvertTo-CanonicalJsonElement -Element $_.Value)"
            })
            return "{$($members -join ',')}"
        }
        ([Text.Json.JsonValueKind]::Array) {
            $items = @($Element.EnumerateArray() | ForEach-Object {
                ConvertTo-CanonicalJsonElement -Element $_
            })
            return "[$($items -join ',')]"
        }
        ([Text.Json.JsonValueKind]::String) {
            return [Text.Json.JsonSerializer]::Serialize(
                $Element.GetString(),
                $script:JsonSerializerOptions)
        }
        ([Text.Json.JsonValueKind]::Number) { return $Element.GetRawText() }
        ([Text.Json.JsonValueKind]::True) { return 'true' }
        ([Text.Json.JsonValueKind]::False) { return 'false' }
        ([Text.Json.JsonValueKind]::Null) { return 'null' }
        default {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "unsupported JSON value kind '$($Element.ValueKind)'."
        }
    }
}

function ConvertTo-CanonicalJson {
    param([AllowNull()][object]$Value)

    $json = ConvertTo-Json -InputObject $Value -Depth 100 -Compress
    $document = [Text.Json.JsonDocument]::Parse($json)
    try {
        return ConvertTo-CanonicalJsonElement -Element $document.RootElement
    }
    finally {
        $document.Dispose()
    }
}

function Get-ReceiptAtRevision {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][DateTimeOffset]$Now,
        [switch]$AllowExpired
    )

    $entry = Get-GitEntry -Revision $Revision -Path $Path
    $sizeText = (Invoke-GitText -Arguments @('cat-file', '-s', $entry.ObjectId)).Text.Trim()
    $blobSize = [long]0
    if (-not [long]::TryParse($sizeText, [ref]$blobSize) -or $blobSize -gt $script:MaximumReceiptBytes) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "receipt exceeds $($script:MaximumReceiptBytes) bytes: $Path."
    }
    $bytes = (Invoke-GitBytes -Arguments @('cat-file', 'blob', $entry.ObjectId)).Bytes
    try {
        $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    }
    catch {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "receipt is not valid UTF-8: $Path."
    }
    $receipt = ConvertFrom-StrictJson -Json $json -Location $Path
    Assert-ReceiptShape -Receipt $receipt -ExpectedPath $Path -Now $Now -AllowExpired:$AllowExpired
    return [pscustomobject]@{ Receipt = $receipt; Bytes = $bytes }
}

function Get-PendingReceiptPaths {
    param([Parameter(Mandatory)][string]$Revision)

    $paths = @((Get-RevisionPaths -Revision $Revision) | Where-Object {
        $_.StartsWith($script:PendingRoot, [StringComparison]::Ordinal) -and
        $_.EndsWith('.json', [StringComparison]::Ordinal)
    })
    return @(Get-OrdinalSortedStrings -Values $paths)
}

function Assert-MigrationIdNotFinalized {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$MigrationId
    )

    $consumedPath = "$($script:ConsumedRoot)$MigrationId.json"
    if ($null -ne (Get-GitEntry -Revision $Revision -Path $consumedPath -AllowMissing)) {
        Stop-MigrationValidation -Code 'REPLAY' -Message "migration '$MigrationId' is already consumed."
    }
    $cancelledPath = "$($script:CancelledRoot)$MigrationId.json"
    if ($null -ne (Get-GitEntry -Revision $Revision -Path $cancelledPath -AllowMissing)) {
        Stop-MigrationValidation -Code 'REPLAY' -Message "migration '$MigrationId' is already cancelled."
    }
}

function Test-AuthorizationOnlyTransition {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff,
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Now
    )

    if ($Diff.Count -ne 1 -or [string]$Diff[0].status -cne 'A' -or
        -not ([string]$Diff[0].path).StartsWith($script:PendingRoot, [StringComparison]::Ordinal)) {
        return $false
    }
    if ([string]$Diff[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'pending receipt must use git mode 100644.'
    }

    $targetParents = (Invoke-GitText -Arguments @('rev-list', '--parents', '-n', '1', $TargetRevision)).Text.Trim().Split(' ')
    if ($targetParents.Count -ne 2 -or $targetParents[1] -cne $BaseRevision) {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'authorization must be one single-parent commit directly after its issued base.'
    }

    if (@(Get-PendingReceiptPaths -Revision $BaseRevision).Count -ne 0) {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'cannot authorize a second pending receipt.'
    }
    $path = [string]$Diff[0].path
    $loaded = Get-ReceiptAtRevision -Revision $TargetRevision -Path $path -Now $Now
    $receipt = $loaded.Receipt
    Assert-MigrationIdNotFinalized -Revision $BaseRevision -MigrationId ([string]$receipt.migrationId)
    if ([string]$receipt.issuedAgainstRevision -cne $BaseRevision) {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'issuedAgainstRevision must equal the authorization commit parent.'
    }
    $baseState = Get-BaselineState -Revision $BaseRevision
    Assert-ObjectJsonEqual -Expected $receipt.source -Actual $baseState.State -Location 'authorization source state'
    Write-Host "AICopilot governance migration authorization recorded: $($receipt.migrationId)"
    return $true
}

function Test-CancellationTransition {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff,
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Now
    )

    $pendingPaths = @(Get-PendingReceiptPaths -Revision $BaseRevision)
    if ($pendingPaths.Count -ne 1 -or $Diff.Count -ne 2) { return $false }
    Assert-DirectSingleParentTransition `
        -BaseRevision $BaseRevision `
        -TargetRevision $TargetRevision `
        -Code 'CANCEL' `
        -Context 'receipt cancellation'

    $pendingPath = $pendingPaths[0]
    $loaded = Get-ReceiptAtRevision -Revision $BaseRevision -Path $pendingPath -Now $Now -AllowExpired
    $migrationIdValue = [string]$loaded.Receipt.migrationId
    $cancelledPath = "$($script:CancelledRoot)$migrationIdValue.json"
    Assert-MigrationIdNotFinalized -Revision $BaseRevision -MigrationId $migrationIdValue
    $pendingDelete = @($Diff | Where-Object { $_.path -ceq $pendingPath -and $_.status -ceq 'D' })
    $cancelledAdd = @($Diff | Where-Object { $_.path -ceq $cancelledPath -and $_.status -ceq 'A' })
    if ($pendingDelete.Count -ne 1 -or $cancelledAdd.Count -ne 1) { return $false }
    if ([string]$pendingDelete[0].beforeMode -cne '100644' -or
        [string]$cancelledAdd[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'CANCEL' -Message 'pending and cancelled receipts must use git mode 100644.'
    }
    $cancelledBytes = Get-GitBlobBytes -Revision $TargetRevision -Path $cancelledPath
    if ((Get-Sha256Hex -Bytes $loaded.Bytes) -cne (Get-Sha256Hex -Bytes $cancelledBytes)) {
        Stop-MigrationValidation -Code 'CANCEL' -Message 'cancelled receipt blob differs from pending receipt.'
    }

    Write-Host "AICopilot governance migration receipt cancelled: $migrationIdValue"
    return $true
}

function Assert-ConsumptionTransition {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff,
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Now
    )

    Assert-DirectSingleParentTransition `
        -BaseRevision $BaseRevision `
        -TargetRevision $TargetRevision `
        -Code 'CONSUME' `
        -Context 'receipt consumption'

    $pendingPaths = @(Get-PendingReceiptPaths -Revision $BaseRevision)
    if ($pendingPaths.Count -ne 1) {
        Stop-MigrationValidation -Code 'CONSUME' -Message "trusted base must contain exactly one pending receipt; found $($pendingPaths.Count)."
    }

    $pendingPath = $pendingPaths[0]
    $loaded = Get-ReceiptAtRevision -Revision $BaseRevision -Path $pendingPath -Now $Now
    $receipt = $loaded.Receipt
    $migrationIdValue = [string]$receipt.migrationId
    $consumedPath = "$($script:ConsumedRoot)$migrationIdValue.json"
    Assert-MigrationIdNotFinalized -Revision $BaseRevision -MigrationId $migrationIdValue

    $baseParents = (Invoke-GitText -Arguments @('rev-list', '--parents', '-n', '1', $BaseRevision)).Text.Trim().Split(' ')
    if ($baseParents.Count -ne 2 -or $baseParents[1] -cne [string]$receipt.issuedAgainstRevision) {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'trusted base must be the isolated authorization commit for this receipt.'
    }
    $authorizationDiff = @(Get-DiffRecords -BaseRevision $baseParents[1] -TargetRevision $BaseRevision)
    if ($authorizationDiff.Count -ne 1 -or
        [string]$authorizationDiff[0].status -cne 'A' -or
        [string]$authorizationDiff[0].path -cne $pendingPath -or
        [string]$authorizationDiff[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'authorization commit must add only the pending receipt.'
    }

    $pendingDelete = @($Diff | Where-Object { $_.path -ceq $pendingPath -and $_.status -ceq 'D' })
    $consumedAdd = @($Diff | Where-Object { $_.path -ceq $consumedPath -and $_.status -ceq 'A' })
    if ($pendingDelete.Count -ne 1 -or $consumedAdd.Count -ne 1) {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'candidate must atomically move pending receipt to consumed.'
    }
    if ([string]$pendingDelete[0].beforeMode -cne '100644' -or
        [string]$consumedAdd[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'pending and consumed receipt must use git mode 100644.'
    }
    $consumedBytes = Get-GitBlobBytes -Revision $TargetRevision -Path $consumedPath
    if ((Get-Sha256Hex -Bytes $loaded.Bytes) -cne (Get-Sha256Hex -Bytes $consumedBytes)) {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'consumed receipt blob differs from pending receipt.'
    }

    $actualChanges = @($Diff | Where-Object {
        $_.path -cne $pendingPath -and $_.path -cne $consumedPath
    })
    Assert-ObjectJsonEqual -Expected @($receipt.changes) -Actual $actualChanges -Location 'candidate changes'
    Assert-TargetWorkflowTrustClosure -Revision $TargetRevision

    $sourceState = Get-BaselineState -Revision ([string]$receipt.issuedAgainstRevision)
    $targetState = Get-BaselineState -Revision $TargetRevision
    Assert-ObjectJsonEqual -Expected $receipt.source -Actual $sourceState.State -Location 'source state'
    Assert-ObjectJsonEqual -Expected $receipt.target -Actual $targetState.State -Location 'target state'
    $projectChanges = Get-ProjectChanges -Source $sourceState -Target $targetState
    Assert-ObjectJsonEqual -Expected $receipt.projectChanges -Actual $projectChanges -Location 'project changes'

    Write-Host "AICopilot governance migration receipt consumed: $migrationIdValue"
}

function New-ReceiptDescription {
    param(
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Issued,
        [Parameter(Mandatory)][DateTimeOffset]$Expires
    )

    $describeArgumentValues = [ordered]@{
        MigrationId = $MigrationId
        RuleIdsCsv = $RuleIdsCsv
        Owner = $Owner
        ApprovedBy = $ApprovedBy
        Reason = $Reason
    }
    $missingArgumentNames = [Collections.Generic.List[string]]::new()
    foreach ($argumentName in $script:DescribeRequiredArgumentNames) {
        if (-not $describeArgumentValues.Contains($argumentName)) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "Describe required-argument contract contains unknown name '$argumentName'.")
        }
        if ([string]::IsNullOrWhiteSpace([string]$describeArgumentValues[$argumentName])) {
            $missingArgumentNames.Add($argumentName)
        }
    }
    if ($missingArgumentNames.Count -ne 0) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message (
            "Describe is missing required argument(s): $($missingArgumentNames -join ', ').")
    }
    $describedRuleIds = @($RuleIdsCsv.Split(',', [StringSplitOptions]::None))
    if ($describedRuleIds.Count -eq 0 -or @($describedRuleIds | Where-Object {
        [string]::IsNullOrWhiteSpace($_) -or $_ -cne $_.Trim()
    }).Count -ne 0) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message 'RuleIdsCsv must be a comma-separated list without whitespace or empty items.'
    }
    $isTrustUpgradeRequest = Test-TrustUpgradeRuleIdSingleton `
        -RuleIds $describedRuleIds `
        -Location 'RuleIdsCsv'
    $normalizedRuleIds = @(Get-OrdinalSortedStrings -Values $describedRuleIds -Unique)

    $diff = @(Get-DiffRecords -BaseRevision $BaseRevision -TargetRevision $TargetRevision)
    if ($diff.Count -eq 0 -or @($diff | Where-Object { Test-IsProtectedPath -Path $_.path }).Count -eq 0) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message 'candidate must change at least one protected asset.'
    }
    foreach ($change in $diff) {
        if (Test-IsReceiptStatePath -Path $change.path) {
            Stop-MigrationValidation -Code 'DESCRIBE' -Message "candidate contains forbidden receipt-state change '$($change.path)'."
        }
        if (Test-IsBaseOwnedHarnessPath -Path $change.path) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "v1 receipts cannot change base-owned harness asset '$($change.path)'.")
        }
        $isTrustPath = Test-IsV1TrustUpgradePath -Path $change.path
        if ($isTrustPath -ne $isTrustUpgradeRequest) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                'v1 trust upgrades may modify only the validator and must be isolated from ordinary changes.')
        }
    }

    Assert-TargetWorkflowTrustClosure -Revision $TargetRevision

    $source = Get-BaselineState -Revision $BaseRevision
    $target = Get-BaselineState -Revision $TargetRevision
    return [pscustomobject][ordered]@{
        schemaVersion = $script:ReceiptSchemaVersion
        ruleId = $script:RuleId
        migrationId = $MigrationId
        issuedAgainstRevision = $BaseRevision
        issuedAtUtc = $Issued.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        expiresAtUtc = $Expires.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        owner = $Owner
        approvedBy = $ApprovedBy
        reason = $Reason.Trim()
        ruleIds = $normalizedRuleIds
        source = $source.State
        target = $target.State
        projectChanges = Get-ProjectChanges -Source $source -Target $target
        changes = $diff
    }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
}
if ($RemainingArguments.Count -ne 0) {
    Stop-MigrationValidation -Code 'PARAMETER' -Message (
        "unexpected positional arguments: $($RemainingArguments -join ', ').")
}
$script:RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not (Test-Path -LiteralPath (Join-Path $script:RepositoryRoot '.git'))) {
    Stop-MigrationValidation -Code 'ROOT' -Message "RepositoryRoot is not a Git worktree: $script:RepositoryRoot."
}

if ($TrustedBaseRevision -notmatch '^[0-9A-Fa-f]{40}$' -or
    $CandidateRevision -notmatch '^[0-9A-Fa-f]{40}$' -or
    $TrustedBaseRevision -match '^0{40}$' -or
    $CandidateRevision -match '^0{40}$') {
    Stop-MigrationValidation -Code 'REVISION' -Message (
        'TrustedBaseRevision and CandidateRevision must be explicit non-zero full 40-character SHAs.')
}

$trustedBase = Resolve-Commit -Revision $TrustedBaseRevision -Name 'TrustedBaseRevision'
$candidate = Resolve-Commit -Revision $CandidateRevision -Name 'CandidateRevision'
$head = Resolve-Commit -Revision 'HEAD' -Name 'HEAD'
$now = [DateTimeOffset]::UtcNow

if ($Mode -eq 'Describe') {
    if ($AnchorRelationship -cne $script:DescribeAnchorRelationship) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message (
            "Describe only supports $($script:DescribeAnchorRelationship).")
    }
    Assert-Ancestry -Ancestor $trustedBase -Descendant $candidate -Context 'Describe'
    Assert-LinearHistoryRange -Ancestor $trustedBase -Descendant $candidate -Context 'Describe'
    Assert-TrustImplementationAssets -Revision $trustedBase
    Assert-TrustImplementationAssets -Revision $candidate
    $issued = if ([string]::IsNullOrWhiteSpace($IssuedAtUtc)) {
        $now
    } else {
        ConvertTo-UtcTimestamp -Value $IssuedAtUtc -Location 'IssuedAtUtc'
    }
    $expires = if ([string]::IsNullOrWhiteSpace($ExpiresAtUtc)) {
        $issued.AddDays(7)
    } else {
        ConvertTo-UtcTimestamp -Value $ExpiresAtUtc -Location 'ExpiresAtUtc'
    }
    $description = New-ReceiptDescription -BaseRevision $trustedBase -TargetRevision $candidate -Issued $issued -Expires $expires
    Assert-ReceiptShape `
        -Receipt $description `
        -ExpectedPath "$($script:PendingRoot)$MigrationId.json" `
        -Now $now
    $json = $description | ConvertTo-Json -Depth 100
    if ([Text.Encoding]::UTF8.GetByteCount("$json`n") -gt $script:MaximumReceiptBytes) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message (
            "generated receipt exceeds the $($script:MaximumReceiptBytes)-byte limit.")
    }
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $json
    } else {
        $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
        $outputDirectory = Split-Path $resolvedOutput -Parent
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
            [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
        }
        [IO.File]::WriteAllText($resolvedOutput, "$json`n", [Text.UTF8Encoding]::new($false))
        Write-Host "Wrote AICopilot governance migration receipt description: $resolvedOutput"
    }
    exit 0
}

if ($candidate -cne $head) {
    Stop-MigrationValidation -Code 'REVISION' -Message 'Validate requires CandidateRevision to equal checked-out HEAD.'
}

if ($AnchorRelationship -eq 'HeadAncestorOfBase') {
    Assert-Ancestry -Ancestor $candidate -Descendant $trustedBase -Context 'release anchor'
    Assert-FirstParentAncestry -Ancestor $candidate -Descendant $trustedBase -Context 'release anchor'
    Assert-LinearHistoryRange `
        -Ancestor $candidate `
        -Descendant $trustedBase `
        -Context 'release anchor' `
        -ValidateAncestorEndpoint
    Assert-TrustImplementationAssets -Revision $trustedBase
    $reverseDiff = @(Get-DiffRecords -BaseRevision $candidate -TargetRevision $trustedBase)
    $protectedReverseDiff = @($reverseDiff | Where-Object { Test-IsProtectedPath -Path $_.path })
    if ($protectedReverseDiff.Count -ne 0) {
        Stop-MigrationValidation -Code 'RELEASE' -Message (
            "release commit differs from trusted main in $($protectedReverseDiff.Count) protected asset(s).")
    }
    Write-Host "AICopilot trusted release anchor passed: head=$candidate trusted=$trustedBase"
    exit 0
}

Assert-Ancestry -Ancestor $trustedBase -Descendant $candidate -Context 'candidate validation'
Assert-LinearHistoryRange -Ancestor $trustedBase -Descendant $candidate -Context 'candidate validation'
Assert-TrustImplementationAssets -Revision $trustedBase
Assert-TrustImplementationAssets -Revision $candidate
$diff = @(Get-DiffRecords -BaseRevision $trustedBase -TargetRevision $candidate)
if (Test-AuthorizationOnlyTransition -Diff $diff -BaseRevision $trustedBase -TargetRevision $candidate -Now $now) {
    exit 0
}

$pendingAtBase = @(Get-PendingReceiptPaths -Revision $trustedBase)
if ($pendingAtBase.Count -gt 0) {
    if (Test-CancellationTransition -Diff $diff -BaseRevision $trustedBase -TargetRevision $candidate -Now $now) {
        exit 0
    }
    Assert-ConsumptionTransition -Diff $diff -BaseRevision $trustedBase -TargetRevision $candidate -Now $now
    exit 0
}
$protectedDiff = @($diff | Where-Object { Test-IsProtectedPath -Path $_.path })
if ($protectedDiff.Count -ne 0) {
    Stop-MigrationValidation -Code 'IMMUTABLE' -Message (
        "protected assets changed without one pending receipt: $(@($protectedDiff.path) -join ', ').")
}

Write-Host "AICopilot protected governance transition is immutable: base=$trustedBase candidate=$candidate"
