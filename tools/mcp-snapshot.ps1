# 树洞泡泡 MCP —— 契约快照测试（契约 §9）
#
# 为什么需要它：MCP 工具名、参数名、_meta.ui、资源 URI 一旦改动，
# 已经装好的 widget 与新老客户端就会对不上。契约 §9 要求"工具 schema 与返回结构做快照测试"。
# 这个脚本把 tools/list + resources/list 的结果规范化后落盘，之后每次比对。
#
#   比对：  .\tools\mcp-snapshot.ps1
#   更新：  .\tools\mcp-snapshot.ps1 -UpdateSnapshot      # 只在**有意**改契约时用

param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$McpPath = "/mcp",
    [string]$Token   = "dev_token_snapshot",
    [string]$Snapshot = "$PSScriptRoot\..\docs\contracts\mcp-manifest.json",
    [switch]$UpdateSnapshot
)

$ErrorActionPreference = "Stop"
$uri = "$BaseUrl$McpPath"

function Invoke-Mcp {
    param([string]$Json)
    $file = Join-Path $env:TEMP ("mcp_snap_" + [guid]::NewGuid().ToString("N") + ".json")
    [System.IO.File]::WriteAllText($file, $Json, [System.Text.UTF8Encoding]::new($false))
    try {
        $raw = curl.exe -sS -X POST $uri `
            -H "Content-Type: application/json" `
            -H "Accept: application/json, text/event-stream" `
            -H "Authorization: Bearer $Token" `
            --data-binary "@$file" 2>&1
    } finally { Remove-Item $file -ErrorAction SilentlyContinue }
    (($raw | Where-Object { $_ -like "data: *" }) -replace '^data: ', '') -join ""
}

$tools = (Invoke-Mcp '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' | ConvertFrom-Json).result.tools
$resources = (Invoke-Mcp '{"jsonrpc":"2.0","id":2,"method":"resources/list","params":{}}' | ConvertFrom-Json).result.resources

# 只保留"对客户端有约束力"的字段：描述文案改动不该让快照失败，
# 但工具名 / 参数 / 必填 / 注解 / _meta.ui / 资源 URI 与 MIME 改动必须失败。
$manifest = [ordered]@{
    tools = @($tools | Sort-Object name | ForEach-Object {
        [ordered]@{
            name        = $_.name
            inputSchema = $_.inputSchema
            annotations = $_.annotations
            meta        = $_.'_meta'
        }
    })
    resources = @($resources | Sort-Object uri | ForEach-Object {
        [ordered]@{ uri = $_.uri; mimeType = $_.mimeType }
    })
}

$json = ($manifest | ConvertTo-Json -Depth 20) -replace "`r`n", "`n"

$dir = Split-Path -Parent $Snapshot
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

if ($UpdateSnapshot) {
    [System.IO.File]::WriteAllText($Snapshot, $json + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host "✅ 已更新快照：$Snapshot"
    exit 0
}

if (-not (Test-Path $Snapshot)) {
    Write-Host "❌ 找不到快照：$Snapshot"
    Write-Host "   首次生成请运行：.\tools\mcp-snapshot.ps1 -UpdateSnapshot"
    exit 1
}

$expected = ([System.IO.File]::ReadAllText($Snapshot, [System.Text.UTF8Encoding]::new($false))) -replace "`r`n", "`n"

if ($expected.TrimEnd("`n") -eq $json.TrimEnd("`n")) {
    Write-Host "✅ MCP 契约快照一致（$($manifest.tools.Count) 个工具 / $($manifest.resources.Count) 个 UI 资源）"
    exit 0
}

Write-Host "❌ MCP 契约已漂移"
$expFile = Join-Path $env:TEMP "mcp_expected.json"
$actFile = Join-Path $env:TEMP "mcp_actual.json"
[System.IO.File]::WriteAllText($expFile, $expected, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($actFile, $json, [System.Text.UTF8Encoding]::new($false))
Write-Host "   期望：$expFile"
Write-Host "   实际：$actFile"
Write-Host "--- diff ---"
Compare-Object ($expected -split "`n") ($json -split "`n") | Select-Object -First 60 | ForEach-Object {
    "{0} {1}" -f $_.SideIndicator, $_.InputObject
}
exit 1
