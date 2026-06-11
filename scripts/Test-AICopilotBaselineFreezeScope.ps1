[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$errors = [System.Collections.Generic.List[string]]::new()

function Add-Error {
    param([string]$Message)
    $script:errors.Add($Message) | Out-Null
}

function Read-RepoText {
    param([string]$RelativePath)

    $fullPath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        Add-Error "Missing required file: $RelativePath"
        return ""
    }

    return Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8
}

$requiredDocuments = @{
    "AGENTS.md" = @(
        "AICopilot 可以读取已批准范围内的 Cloud 业务数据",
        "AICopilot 不得注册、修改、删除、补录、审批、派发或触发 Cloud 业务数据",
        "Cloud-AICopilot OIDC 身份对齐的长期结论",
        "AICopilot 保留本地 AI 用户、AI 角色、AI 权限"
    )
    "资料/AICopilot业务规则.md" = @(
        '只承担 AI 助手和受控编排能力',
        "Cloud 只读边界",
        "Human-in-the-loop 不能把禁止的 Cloud 业务写入变成允许动作",
        "MCP 是受控工具入口，不是 Cloud 业务写入口"
    )
    "AICopilot 项目部署与维护指南.md" = @(
        "deploy/enterprise-ai",
        "CLOUD_READONLY_MODE=Disabled",
        "CLOUD_OIDC_ENABLED=true",
        "不提交真实密钥"
    )
}

foreach ($entry in $requiredDocuments.GetEnumerator()) {
    $content = Read-RepoText $entry.Key
    foreach ($marker in $entry.Value) {
        if ($content -notlike "*$marker*") {
            Add-Error "$($entry.Key) is missing required marker: $marker"
        }
    }
}

$allDocumentText = ($requiredDocuments.Keys | ForEach-Object { Read-RepoText $_ }) -join "`n"
$forbiddenClaims = @(
    "Cloud 写已开放",
    "query_cloud_data_readonly enabled by default",
    "已配置真实 token",
    "已配置真实 endpoint",
    "真实 Pilot 已执行",
    "正式 GA 已通过"
)

foreach ($claim in $forbiddenClaims) {
    if ($allDocumentText -like "*$claim*") {
        Add-Error "Forbidden baseline claim found: $claim"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "AICopilot baseline scope check failed:"
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem"
    }
    exit 1
}

Write-Host "AICopilot baseline scope check passed."
