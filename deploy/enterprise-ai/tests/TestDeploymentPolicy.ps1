Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
function Require-Text([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $Path)
    if ($text -notmatch $Pattern) { throw $Message }
}
function Forbid-Text([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $Path)
    if ($text -match $Pattern) { throw $Message }
}
function Forbid-Path([string]$Path, [string]$Message) {
    if (Test-Path -LiteralPath (Join-Path $root $Path)) { throw $Message }
}

Forbid-Text 'Directory.Build.targets' 'ValidateAICopilotDeploymentPolicy|deploy/enterprise-ai/tests/TestDeploymentPolicy\.ps1' 'Ordinary AICopilot builds must not run DeploymentContract tests through an MSBuild hook.'
@(
    '.github/workflows/aicopilot-enable-direct-cloud-readonly-db.yml',
    '.github/workflows/aicopilot-enable-real-cloud-ai-read.yml',
    '.github/workflows/aicopilot-provision-cloud-readonly-db-role.yml',
    'scripts/Set-AICopilotCloudReadOnlyDbSecret.sh',
    'scripts/Set-AICopilotCloudAiReadSecret.sh',
    'scripts/Provision-AICopilotCloudReadOnlyDbRole.sh'
) | ForEach-Object {
    Forbid-Path $_ "Retired GitHub-secret production write path must not return: $_"
}

$productionConfigurationSources = @(
    Get-ChildItem -LiteralPath (Join-Path $root '.github/workflows') -File |
        Where-Object { $_.Extension -in @('.yml', '.yaml') }
    Get-ChildItem -LiteralPath (Join-Path $root 'scripts') -Filter '*.sh' -File -Recurse
)
$retiredWritePattern = '(?m)gh\s+secret\s+set\s+(?:DATA_ANALYSIS_CLOUD_READONLY_(?:CONNECTION_STRING|USERNAME|PASSWORD)|CLOUD_AI_READ_BASE_URL|CLOUD_AI_SERVICE_ACCOUNT_TOKEN)|set_env_value\s+(?:DATA_ANALYSIS_CLOUD_READONLY|CLOUD_AI_READ)'
foreach ($source in $productionConfigurationSources) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath $source.FullName
    if ($text -match $retiredWritePattern) {
        throw "Production Cloud readonly configuration must flow through Keychain/Deploy-FromZero, not GitHub-secret workflow or helper: $($source.FullName)"
    }
}

Require-Text 'deploy/enterprise-ai/build-and-push.sh' 'normalize_services' 'AICopilot image builder must normalize an explicit service set.'
Require-Text 'deploy/enterprise-ai/build-and-push.sh' 'build_artifacts_root="\$OUTPUT_DIR/service-build/\$service"' 'AICopilot backend builds must isolate SDK artifacts by service outside the detached source worktree.'
Require-Text 'deploy/enterprise-ai/build-and-push.sh' '--artifacts-path "\$build_artifacts_root"' 'AICopilot dotnet publish must use the service-private artifacts root.'
Require-Text 'deploy/enterprise-ai/build-and-push.sh' 'docker manifest inspect --insecure --verbose "\$image_ref"' 'AICopilot image digest resolution must support the configured HTTP Harbor without weakening immutable digest deployment.'
Require-Text 'deploy/enterprise-ai/local-release.sh' 'normalized="\$normalized,migration"' 'AICopilot backend deployment must preserve the migration safety closure.'
Require-Text '.github/workflows/aicopilot-routine-request.yml' 'name:\s*aicopilot-production-state-inspect' 'AICopilot routine workflow must be physically limited to read-only production-state inspection.'
Require-Text '.github/workflows/aicopilot-routine-request.yml' 'INSPECT_AICOPILOT_STATE' 'AICopilot production-state inspection must require its explicit read-only confirmation.'
Require-Text '.github/workflows/aicopilot-routine-request.yml' 'Upload current production state' 'AICopilot production-state inspection must export an allowlisted receipt.'
Forbid-Text '.github/workflows/aicopilot-routine-request.yml' 'request_base64|request_sha256|operation:\s*|Deliver immutable request|--request-stdin|inputs\.operation' 'AICopilot GitHub Actions transport must not retain a manually dispatchable deployment operation.'
Require-Text '资料/AICopilot业务规则.md' '只构建.*受影响|受影响.*镜像' 'AICopilot incremental image deployment red line is missing.'
Write-Host 'AICopilot deployment policy architecture test passed.'
