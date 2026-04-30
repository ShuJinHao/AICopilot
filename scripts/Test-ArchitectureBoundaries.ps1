[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$coreRoot = Join-Path $repoRoot "src\core"
$servicesRoot = Join-Path $repoRoot "src\services"
$infrastructureRoot = Join-Path $repoRoot "src\infrastructure"
$agentPluginRoot = Join-Path $repoRoot "src\shared\AICopilot.AgentPlugin"
$ragWorkerRoot = Join-Path $repoRoot "src\hosts\AICopilot.RagWorker"

$coreForbiddenPatterns = @(
    "using\s+AICopilot\.Services",
    "using\s+AICopilot\.Infrastructure",
    "using\s+AICopilot\.Dapper",
    "using\s+AICopilot\.Embedding",
    "using\s+AICopilot\.EventBus",
    "using\s+AICopilot\.EntityFrameworkCore",
    "\.\.\\\.\.\\Services\\",
    "\.\.\\\.\.\\Infrastructure\\",
    "\.\.\\\.\.\\Hosts\\"
)

$servicesForbiddenPatterns = @(
    "using\s+AICopilot\.Dapper",
    "using\s+AICopilot\.Embedding",
    "using\s+AICopilot\.EventBus",
    "using\s+AICopilot\.EntityFrameworkCore",
    "using\s+AICopilot\.Infrastructure",
    "ModelContextProtocol",
    "Qdrant",
    "PdfPig",
    "VectorStore",
    "using\s+OpenAI",
    "Microsoft\.Agents\.AI",
    "Microsoft\.Agents\.AI\.Workflows",
    "Microsoft\.Agents\.AI\.OpenAI",
    "using\s+Microsoft\.Extensions\.AI",
    "Microsoft\.Extensions\.AI\.OpenAI",
    "Microsoft\.Extensions\.AI\.Abstractions",
    "\bAITool\b",
    "\bAIFunction\b",
    "\bAIContent\b",
    "\bToolApprovalRequestContent\b",
    "\bFunctionCallContent\b",
    "\bFunctionResultContent\b",
    "\bUsageContent\b",
    "\bnew\s+ChatMessage\b",
    "\bIEnumerable\s*<\s*ChatMessage\b",
    "\bList\s*<\s*ChatMessage\b",
    "\bnew\s+ChatOptions\b",
    "\bAction\s*<\s*ChatOptions\b",
    "\bChatClientAgent\b",
    "\bAIAgent\b",
    "(?<!Runtime)\bAgentSession\b",
    "\bWorkflowBuilder\b",
    "\bInProcessExecution\b",
    "OpenAIClient",
    "ApiKeyCredential",
    "HttpClientPipelineTransport",
    "\.\.\\\.\.\\Infrastructure\\",
    "<ProjectReference\s+Include=""\.\.\\AICopilot\.(?!Services\.Contracts|Services\.CrossCutting)"
)

$agentPluginForbiddenPatterns = @(
    "using\s+Microsoft\.Extensions\.AI",
    "Microsoft\.Extensions\.AI",
    "\bAITool\b",
    "\bAIFunction\b",
    "\bAIContent\b",
    "\bToolApprovalRequestContent\b",
    "\bFunctionCallContent\b",
    "\bFunctionResultContent\b",
    "\bUsageContent\b",
    "\bnew\s+ChatMessage\b",
    "\bnew\s+ChatOptions\b"
)

$infrastructureForbiddenPatterns = @(
    "using\s+AICopilot\.AiGatewayService",
    "using\s+AICopilot\.DataAnalysisService",
    "using\s+AICopilot\.RagService",
    "using\s+AICopilot\.McpService",
    "using\s+AICopilot\.IdentityService",
    "\.\.\\\.\.\\Services\\AICopilot\.(?!Services\.Contracts|Services\.CrossCutting)"
)

$ragWorkerForbiddenPatterns = @(
    "using\s+AICopilot\.EntityFrameworkCore",
    "using\s+AICopilot\.Embedding",
    "using\s+AICopilot\.EventBus",
    "AiCopilotDbContext",
    "VectorStore",
    "EmbeddingGenerator",
    "PdfPig",
    "SemanticKernel",
    "Qdrant",
    "DocumentParser",
    "TextSplitter",
    "ITokenCounter",
    "\.\.\\\.\.\\Infrastructure\\AICopilot\.Embedding",
    "\.\.\\\.\.\\Infrastructure\\AICopilot\.EventBus",
    "\.\.\\\.\.\\Infrastructure\\AICopilot\.EntityFrameworkCore"
)

$violations = New-Object System.Collections.Generic.List[string]

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    if ([System.IO.Path].GetMethod("GetRelativePath", [type[]]@([string], [string])) -ne $null) {
        return [System.IO.Path]::GetRelativePath($BasePath, $TargetPath)
    }

    $baseFullPath = (Resolve-Path -Path $BasePath).Path.TrimEnd('\') + '\'
    $targetFullPath = (Resolve-Path -Path $TargetPath).Path
    $baseUri = [Uri]$baseFullPath
    $targetUri = [Uri]$targetFullPath

    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

function Test-Files {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [string[]]$Patterns
    )

    $files = Get-ChildItem -Path $Root -Recurse -Include *.cs,*.csproj |
        Where-Object {
            $_.FullName -notmatch "\\bin\\" -and
            $_.FullName -notmatch "\\obj\\"
        }

    foreach ($file in $files) {
        $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
        foreach ($pattern in $Patterns) {
            if ($content -match $pattern) {
                $relativePath = Get-RelativePathCompat -BasePath $repoRoot -TargetPath $file.FullName
                $violations.Add("$relativePath matches '$pattern'")
            }
        }
    }
}

Test-Files -Root $coreRoot -Patterns $coreForbiddenPatterns
Test-Files -Root $servicesRoot -Patterns $servicesForbiddenPatterns
Test-Files -Root $agentPluginRoot -Patterns $agentPluginForbiddenPatterns
Test-Files -Root $infrastructureRoot -Patterns $infrastructureForbiddenPatterns
Test-Files -Root $ragWorkerRoot -Patterns $ragWorkerForbiddenPatterns

if ($violations.Count -gt 0) {
    Write-Host "Architecture boundary violations were found:"
    foreach ($violation in $violations) {
        Write-Host " - $violation"
    }
    exit 1
}

Write-Host "Architecture boundary check passed."
