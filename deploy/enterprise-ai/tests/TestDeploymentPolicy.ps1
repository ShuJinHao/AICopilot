Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
function Require-Text([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $Path)
    if ($text -notmatch $Pattern) { throw $Message }
}

Require-Text 'deploy/enterprise-ai/build-and-push.sh' 'normalize_services' 'AICopilot image builder must normalize an explicit service set.'
Require-Text 'deploy/enterprise-ai/build-and-push.sh' 'build_artifacts_root="\$REPO_ROOT/artifacts/service-build/\$service"' 'AICopilot backend builds must isolate SDK artifacts by service.'
Require-Text 'deploy/enterprise-ai/build-and-push.sh' '--artifacts-path "\$build_artifacts_root"' 'AICopilot dotnet publish must use the service-private artifacts root.'
Require-Text 'deploy/enterprise-ai/local-release.sh' 'normalized="\$normalized,migration"' 'AICopilot backend deployment must preserve the migration safety closure.'
Require-Text '.github/workflows/aicopilot-routine-request.yml' 'operation:\s*[\s\S]*?- deploy\s*[\s\S]*?- inspect' 'AICopilot routine workflow must expose read-only production-state inspection.'
Require-Text '.github/workflows/aicopilot-routine-request.yml' "if: inputs\.operation == 'deploy'" 'AICopilot deployment request must be gated by operation=deploy.'
Require-Text '.github/workflows/aicopilot-routine-request.yml' "Checkout pinned runner source\s*[\s\S]*?if: inputs\.operation == 'deploy'" 'AICopilot read-only inspection must not wait for repository checkout.'
Require-Text 'AGENTS.md' '只构建.*受影响|受影响.*镜像' 'AICopilot incremental image deployment red line is missing.'
Write-Host 'AICopilot deployment policy architecture test passed.'
