[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function New-UnicodeText {
    param([Parameter(Mandatory = $true)][int[]]$CodePoints)
    $builder = New-Object System.Text.StringBuilder
    foreach ($codePoint in $CodePoints) {
        [void]$builder.Append([char]$codePoint)
    }
    return $builder.ToString()
}

$scanRoots = @(
    (Join-Path $repoRoot "src/vues/AICopilot.Web/src"),
    (Join-Path $repoRoot "src/vues/AICopilot.Web/tests/smoke"),
    (Join-Path $repoRoot (New-UnicodeText -CodePoints @(0x8d44, 0x6599)))
)

$extensions = @(".vue", ".ts", ".mjs", ".md", ".ps1")
$mojibakePatterns = @(
    [string][char]0xFFFD,
    (New-UnicodeText -CodePoints @(0x9352, 0x5815)),
    (New-UnicodeText -CodePoints @(0x6769, 0x612e)),
    (New-UnicodeText -CodePoints @(0x5bb8, 0x30e4, 0x7d94)),
    (New-UnicodeText -CodePoints @(0x9427, 0x8bf2)),
    (New-UnicodeText -CodePoints @(0x93ba, 0x0443)),
    (New-UnicodeText -CodePoints @(0x9422, 0x3126, 0x57db)),
    (New-UnicodeText -CodePoints @(0x7035, 0x55d9, 0x721c)),
    (New-UnicodeText -CodePoints @(0x6d7c, 0x6c33)),
    (New-UnicodeText -CodePoints @(0x7039, 0x2103)),
    (New-UnicodeText -CodePoints @(0x6d5c, 0x0445)),
    (New-UnicodeText -CodePoints @(0x6d60, 0x8bf2)),
    (New-UnicodeText -CodePoints @(0x942d, 0x30e8, 0x7611)),
    (New-UnicodeText -CodePoints @(0x93c9, 0x51ae, 0x6aba)),
    (New-UnicodeText -CodePoints @(0x6748, 0x64b3, 0x53c6)),
    (New-UnicodeText -CodePoints @(0x95ab, 0x590b, 0x5ae8)),
    (New-UnicodeText -CodePoints @(0x93c6, 0x509b, 0x68e4)),
    (New-UnicodeText -CodePoints @(0x93c1, 0x7248, 0x5d41)),
    (New-UnicodeText -CodePoints @(0x9422, 0x71b8, 0x579a)),
    (New-UnicodeText -CodePoints @(0x7eef, 0x8364, 0x7cba))
)

$violations = New-Object System.Collections.Generic.List[string]

foreach ($root in $scanRoots) {
    if (-not (Test-Path $root)) {
        continue
    }

    Get-ChildItem -Path $root -Recurse -File |
        Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() } |
        ForEach-Object {
            $fullPath = [System.IO.Path]::GetFullPath($_.FullName)
            if ($fullPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                $relativePath = $fullPath.Substring($repoRoot.Length).TrimStart('\', '/')
            } else {
                $relativePath = $fullPath
            }
            $lines = @(Get-Content -LiteralPath $_.FullName -Encoding UTF8)
            for ($index = 0; $index -lt $lines.Count; $index++) {
                foreach ($pattern in $mojibakePatterns) {
                    if ($lines[$index].Contains($pattern)) {
                        $violations.Add("${relativePath}:$($index + 1): suspected mojibake marker")
                        break
                    }
                }
            }
        }
}

if ($violations.Count -gt 0) {
    Write-Error ("Text encoding check failed:`n" + ($violations -join "`n"))
    exit 1
}

Write-Host "Text encoding check passed."
