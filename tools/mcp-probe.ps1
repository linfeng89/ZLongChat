# MCP 端点端到端自检（骨架阶段的验收脚本）
# 用法：pwsh -File tools\mcp-probe.ps1 [-BaseUrl http://localhost:5000] [-Token dev_token_probe1]

param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$Token   = "dev_token_probe1",
    [string]$McpPath = "/mcp"
)

$ErrorActionPreference = "Stop"
$uri = "$BaseUrl$McpPath"
$headers = @(
    "Content-Type: application/json",
    "Accept: application/json, text/event-stream",
    "Authorization: Bearer $Token"
)

function Invoke-Mcp {
    param([string]$Json)
    $file = Join-Path $env:TEMP ("mcp_probe_" + [guid]::NewGuid().ToString("N") + ".json")
    [System.IO.File]::WriteAllText($file, $Json, [System.Text.UTF8Encoding]::new($false))
    try {
        $raw = curl.exe -sS -X POST $uri @($headers | ForEach-Object { "-H"; $_ }) --data-binary "@$file" 2>&1
    } finally {
        Remove-Item $file -ErrorAction SilentlyContinue
    }
    # 剥掉 SSE 的 event:/data: 包装
    ($raw | Where-Object { $_ -like "data: *" }) -replace '^data: ', '' | Out-String
}

$fail = 0

# ---------------- 1. initialize ----------------
$init = Invoke-Mcp '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"mcp-probe","version":"1.0"}}}'
$initObj = $init | ConvertFrom-Json
if ($initObj.result.serverInfo.name) {
    Write-Host "✅ initialize 成功：$($initObj.result.serverInfo.name) $($initObj.result.serverInfo.version)"
} else {
    Write-Host "❌ initialize 失败：$init"; $fail++
}

# ---------------- 2. tools/list ----------------
$toolsRaw = Invoke-Mcp '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
$tools = ($toolsRaw | ConvertFrom-Json).result.tools
Write-Host "`n📋 工具总数：$($tools.Count)"
if ($tools.Count -ne 16) { Write-Host "❌ 期望 16 个工具"; $fail++ }

Write-Host "`n--- 模型可见（无 _meta.ui 或 visibility 含 model）---"
$modelVisible = $tools | Where-Object {
    $v = $_._meta.ui.visibility
    -not $v -or ($v -contains "model")
}
$modelVisible | ForEach-Object {
    $ui = $_._meta.ui.resourceUri
    "  {0,-26} ui={1}" -f $_.name, ($(if ($ui) { $ui } else { "-" }))
}

Write-Host "`n--- 仅面板可调（visibility = [app]）---"
$appOnly = $tools | Where-Object { ($_._meta.ui.visibility) -contains "app" -and ($_._meta.ui.visibility) -notcontains "model" }
$appOnly | ForEach-Object { "  {0,-26} visibility=[app]" -f $_.name }

Write-Host "`n--- 校验每个工具的 _meta.ui ---"
foreach ($t in $tools) {
    $ui = $t._meta.ui
    if ($null -eq $ui) { Write-Host "  ❌ $($t.name) 缺 _meta.ui"; $fail++; continue }
    $vis = @($ui.visibility)
    if ($vis.Count -eq 0) { Write-Host "  ❌ $($t.name) 缺 visibility"; $fail++; continue }
    foreach ($v in $vis) {
        if ($v -ne "model" -and $v -ne "app") { Write-Host "  ❌ $($t.name) 非法 visibility=$v"; $fail++ }
    }
}
Write-Host "  （逐项结果见下）"

Write-Host "`n--- 校验 _meta.ui 标注 ---"
$expectUi = @{
    "open_publish"       = "ui://shudong/publish.html?v=1"
    "get_active_signals" = "ui://shudong/signals.html?v=1"
    "get_signal_detail"  = "ui://shudong/signal-detail.html?v=1"
    "open_session"       = "ui://shudong/chat.html?v=1"
    "get_my_state"       = "ui://shudong/state.html?v=1"
    "get_support_resources" = "ui://shudong/crisis.html?v=1"
}
foreach ($name in $expectUi.Keys) {
    $t = $tools | Where-Object { $_.name -eq $name }
    if (-not $t) { Write-Host "  ❌ 缺工具 $name"; $fail++; continue }
    if ($t._meta.ui.resourceUri -ne $expectUi[$name]) {
        Write-Host "  ❌ $name resourceUri = $($t._meta.ui.resourceUri)，期望 $($expectUi[$name])"; $fail++
    } else { Write-Host "  ✅ $name → $($expectUi[$name])" }
}

$expectAppOnly = @("publish_signal", "get_signal_detail_content", "catch_signal", "send_message")
foreach ($name in $expectAppOnly) {
    $t = $tools | Where-Object { $_.name -eq $name }
    if (-not $t) { Write-Host "  ❌ 缺工具 $name"; $fail++; continue }
    $v = $t._meta.ui.visibility
    if ($v -contains "model") { Write-Host "  ❌ $name 不应暴露给模型"; $fail++ }
    else { Write-Host "  ✅ $name 仅面板可调" }
}

Write-Host "`n--- 工具名清单 ---"
($tools.name | Sort-Object) -join ", "

# ---------------- 3. resources/list ----------------
$resRaw = Invoke-Mcp '{"jsonrpc":"2.0","id":3,"method":"resources/list","params":{}}'
$resources = ($resRaw | ConvertFrom-Json).result.resources
Write-Host "`n🎨 UI 资源总数：$($resources.Count)"
$resources | ForEach-Object { "  {0,-42} {1}" -f $_.uri, $_.mimeType }
if ($resources.Count -ne 6) { Write-Host "❌ 期望 6 个 UI 资源"; $fail++ }

# ---------------- 4. resources/read ----------------
foreach ($r in $resources) {
    $readReq = @{ jsonrpc = "2.0"; id = 4; method = "resources/read"; params = @{ uri = $r.uri } } | ConvertTo-Json -Depth 5 -Compress
    $readRaw = Invoke-Mcp $readReq
    $text = ($readRaw | ConvertFrom-Json).result.contents[0].text
    if ($text -and $text -like "*<!doctype html>*") { Write-Host "  [OK] $($r.uri) ($($text.Length) bytes)" }
    else { Write-Host "  [FAIL] $($r.uri)"; $fail++ }
}

# ---------------- 5. 未授权调用工具应被拒 ----------------
$callReq = @{ jsonrpc = "2.0"; id = 5; method = "tools/call"; params = @{ name = "get_my_state"; arguments = @{} } } | ConvertTo-Json -Depth 6 -Compress
$file = Join-Path $env:TEMP ("mcp_noauth_" + [guid]::NewGuid().ToString("N") + ".json")
[System.IO.File]::WriteAllText($file, $callReq, [System.Text.UTF8Encoding]::new($false))
try {
    $raw = curl.exe -sS -X POST $uri -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" --data-binary "@$file" 2>&1
} finally { Remove-Item $file -ErrorAction SilentlyContinue }
$json = (($raw | Where-Object { $_ -like "data: *" }) -replace '^data: ', '') -join ""
Write-Host "`n🔒 无令牌 tools/call 响应："
Write-Host "   $json"
$noAuth = $json | ConvertFrom-Json
if ($noAuth.error -or $noAuth.result.isError) { Write-Host "   ✅ 无令牌被拒" } else { Write-Host "   ❌ 无令牌竟然成功"; $fail++ }

# ---------------- 6. 未授权令牌应被拒 ----------------
$file = Join-Path $env:TEMP ("mcp_badtoken_" + [guid]::NewGuid().ToString("N") + ".json")
[System.IO.File]::WriteAllText($file, $callReq, [System.Text.UTF8Encoding]::new($false))
try {
    $raw = curl.exe -sS -X POST $uri -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -H "Authorization: Bearer not_a_real_token" --data-binary "@$file" 2>&1
} finally { Remove-Item $file -ErrorAction SilentlyContinue }
$json = (($raw | Where-Object { $_ -like "data: *" }) -replace '^data: ', '') -join ""
Write-Host "`n🔒 伪造令牌 tools/call 响应："
Write-Host "   $json"
$badToken = $json | ConvertFrom-Json
if ($badToken.error -or $badToken.result.isError) { Write-Host "   ✅ 伪造令牌被拒" } else { Write-Host "   ❌ 伪造令牌竟然成功"; $fail++ }

Write-Host "`n============================"
if ($fail -eq 0) { Write-Host "✅ 全部通过" } else { Write-Host "❌ 失败 $fail 项" }
Write-Host "============================"
exit $fail
