Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AICopilotGitChangedFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$BaseRef,
        [string]$HeadRef = 'HEAD'
    )

    $root = (Resolve-Path $RepositoryRoot).Path
    if ([string]::IsNullOrWhiteSpace($BaseRef) -or
        [string]::IsNullOrWhiteSpace($HeadRef)) {
        throw 'AICopilot Git path discovery requires non-empty base and head refs.'
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
            '-C', $root,
            '-c', 'core.quotePath=true',
            'diff',
            '--no-renames',
            '--name-only',
            '-z',
            '--diff-filter=ACMRTUXBD',
            "$BaseRef...$HeadRef",
            '--')) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Unable to start Git for AICopilot changed-path discovery.'
    }

    try {
        $standardError = $process.StandardError.ReadToEndAsync()
        $bytes = [IO.MemoryStream]::new()
        try {
            $process.StandardOutput.BaseStream.CopyTo($bytes)
            $process.WaitForExit()
            $errorText = $standardError.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) {
                throw "Unable to calculate changed files for $BaseRef...${HeadRef}:`n$errorText"
            }

            $payload = $bytes.ToArray()
            if ($payload.Length -eq 0) {
                return @()
            }
            if ($payload[-1] -ne 0) {
                throw 'Git changed-path output was not terminated by a NUL byte.'
            }

            try {
                $text = [Text.UTF8Encoding]::new(
                    $false,
                    $true).GetString($payload, 0, $payload.Length - 1)
            }
            catch {
                throw 'Git returned a changed path that is not valid UTF-8.'
            }

            return @($text.Split(
                    [char]0,
                    [StringSplitOptions]::None))
        }
        finally {
            $bytes.Dispose()
        }
    }
    finally {
        $process.Dispose()
    }
}

Export-ModuleMember -Function Get-AICopilotGitChangedFiles
