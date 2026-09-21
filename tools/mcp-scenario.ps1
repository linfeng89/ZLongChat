# 树洞泡泡 MCP —— 端到端业务场景自检
#
# 覆盖契约里的关键不变量：
#   1. 发布 companion 信号 → 别人能看到 → 能接住 → 能发消息 → 能结束
#   2. broadcast 信号被服务端硬拒接住（MODE_NOT_CATCHABLE）
#   3. 不能接住自己的信号（SELF_CATCH_FORBIDDEN）
#   4. 举报返回留存告知（Disclosure 必须存在）
#   5. 求助资源始终可取

param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$McpPath = "/mcp"
)

$ErrorActionPreference = "Stop"
$uri = "$BaseUrl$McpPath"
$script:fail = 0

function Invoke-Call {
    param([string]$Token, [string]$Tool, [hashtable]$ToolArgs = @{}, [int]$Id = 1)

    $req = @{ jsonrpc = "2.0"; id = $Id; method = "tools/call"; params = @{ name = $Tool; arguments = $ToolArgs } } |
        ConvertTo-Json -Depth 8 -Compress
    $file = Join-Path $env:TEMP ("mcp_sc_" + [guid]::NewGuid().ToString("N") + ".json")
    [System.IO.File]::WriteAllText($file, $req, [System.Text.UTF8Encoding]::new($false))
    try {
        $raw = curl.exe -sS -X POST $uri `
            -H "Content-Type: application/json" `
            -H "Accept: application/json, text/event-stream" `
            -H "Authorization: Bearer $Token" `
            --data-binary "@$file" 2>&1
    } finally { Remove-Item $file -ErrorAction SilentlyContinue }

    $json = (($raw | Where-Object { $_ -like "data: *" }) -replace '^data: ', '') -join ""
    if (-not $json) { throw "空响应：$Tool" }
    return $json | ConvertFrom-Json
}

function Get-Payload {
    param($Response, [string]$Tool)
    if ($Response.error) { Write-Host "  [JSONRPC-ERR] $Tool :: $($Response.error.message)"; return $null }
    $r = $Response.result
    if ($r.isError) {
        $code = $r.structuredContent.code
        Write-Host "  [ERR] $Tool :: $code / $($r.content[0].text)"
        return [pscustomobject]@{ __error = $true; code = $code; message = $r.content[0].text; disclosure = $r.structuredContent.disclosure }
    }
    $sc = $r.structuredContent
    if ($null -eq $sc) { return ($r.content[0].text | ConvertFrom-Json) }
    return $sc
}

function Assert {
    param([bool]$Condition, [string]$What)
    if ($Condition) { Write-Host "  [OK]   $What" }
    else { Write-Host "  [FAIL] $What"; $script:fail++ }
}

$tokenA = "dev_token_ali"      # 倾诉者
$tokenB = "dev_token_bo"       # 倾听者

Write-Host "`n=== 1. 倾诉者取配额 ==="
$state = Get-Payload (Invoke-Call $tokenA "get_my_state" @{} 1) "get_my_state"
Assert ($null -ne $state.quota) "get_my_state 返回 quota"

Write-Host "`n=== 2. 发布 companion 信号 ==="
$pub = Get-Payload (Invoke-Call $tokenA "publish_signal" @{
    content = "今天面试没过，不太想说话，但也不想一个人待着。"
    tags = @("工作", "低落")
    mode = "companion"
    idempotencyKey = [guid]::NewGuid().ToString()
} 2) "publish_signal"
Assert ($null -ne $pub.signalId) "拿到 signalId"
$signalId = $pub.signalId

Write-Host "`n=== 3. 倾听者浏览树洞池 ==="
$page = Get-Payload (Invoke-Call $tokenB "get_active_signals" @{ limit = 20 } 3) "get_active_signals"
$found = $page.items | Where-Object { $_.signalId -eq $signalId }
Assert ($null -ne $found) "池子里能看到刚发的信号"
Assert ($found.mode -eq "companion" -or $found.mode -eq 1) "mode 是 companion（当前值：$($found.mode)）"

Write-Host "`n=== 4. 元信息不含正文 ==="
$meta = Get-Payload (Invoke-Call $tokenB "get_signal_detail" @{ signalId = $signalId } 4) "get_signal_detail"
$metaJson = $meta | ConvertTo-Json -Depth 6
Assert (-not ($metaJson -like "*面试没过*")) "get_signal_detail 不含正文"

Write-Host "`n=== 5. 正文单独取（app-only 工具） ==="
$content = Get-Payload (Invoke-Call $tokenB "get_signal_detail_content" @{ signalId = $signalId } 5) "get_signal_detail_content"
Assert ($content.content -like "*面试没过*") "get_signal_detail_content 能取到正文"

Write-Host "`n=== 6. 不能接住自己的信号 ==="
$self = Get-Payload (Invoke-Call $tokenA "catch_signal" @{
    signalId = $signalId; firstMessage = "自己接自己"; idempotencyKey = [guid]::NewGuid().ToString()
} 6) "catch_signal(self)"
Assert ($self.__error -and $self.code -eq "SELF_CATCH_FORBIDDEN") "自接被拒（$($self.code)）"

Write-Host "`n=== 7. 倾听者接住 ==="
$catched = Get-Payload (Invoke-Call $tokenB "catch_signal" @{
    signalId = $signalId; firstMessage = "我在，不用急着说话。"; idempotencyKey = [guid]::NewGuid().ToString()
} 7) "catch_signal"
Assert ($null -ne $catched.sessionId) "拿到 sessionId"
$sessionId = $catched.sessionId

Write-Host "`n=== 8. 双方发消息 ==="
$m1 = Get-Payload (Invoke-Call $tokenB "send_message" @{ sessionId = $sessionId; content = "想说什么都行。"; idempotencyKey = [guid]::NewGuid().ToString() } 8) "send_message(B)"
Assert ($null -ne $m1) "倾听者发消息成功"
$m2 = Get-Payload (Invoke-Call $tokenA "send_message" @{ sessionId = $sessionId; content = "谢谢。"; idempotencyKey = [guid]::NewGuid().ToString() } 9) "send_message(A)"
Assert ($null -ne $m2) "倾诉者发消息成功"

Write-Host "`n=== 9. 举报并检查留存告知 ==="
$rep = Get-Payload (Invoke-Call $tokenA "report" @{
    sessionId = $sessionId; reason = "other"; detail = "自检"; idempotencyKey = [guid]::NewGuid().ToString()
} 10) "report"
Assert ($null -ne $rep) "report 有返回"
Assert (-not [string]::IsNullOrWhiteSpace($rep.disclosure)) "report 带回留存告知"
Assert ($rep.retention -ne $null) "report 说明留存状态（$($rep.retention)）"

Write-Host "`n=== 10. broadcast 信号不可被接住（服务端硬规则） ==="
$pubBc = Get-Payload (Invoke-Call $tokenA "publish_signal" @{
    content = "只是想说出来，不用回应。"
    tags = @("随笔")
    mode = "broadcast"
    idempotencyKey = [guid]::NewGuid().ToString()
} 11) "publish_signal(broadcast)"
Assert ($null -ne $pubBc.signalId) "broadcast 发布成功"
$bc = Get-Payload (Invoke-Call $tokenB "catch_signal" @{
    signalId = $pubBc.signalId; firstMessage = "试图接住"; idempotencyKey = [guid]::NewGuid().ToString()
} 12) "catch_signal(broadcast)"
Assert ($bc.__error -and $bc.code -eq "MODE_NOT_CATCHABLE") "broadcast 被拒（$($bc.code)）"

Write-Host "`n=== 11. 求助资源始终可取（不需要登录） ==="
$sup = Get-Payload (Invoke-Call "" "get_support_resources" @{} 13) "get_support_resources"
Assert ($sup.resources.Count -gt 0) "取到 $($sup.resources.Count) 条求助资源"

Write-Host "`n=== 12. 参数校验：非法 mode ==="
$bad = Get-Payload (Invoke-Call $tokenA "publish_signal" @{
    content = "x"; tags = @(); mode = "nonsense"; idempotencyKey = [guid]::NewGuid().ToString()
} 14) "publish_signal(bad mode)"
Assert ($bad.__error -and $bad.code -eq "VALIDATION_ERROR") "非法 mode 被拒（$($bad.code)）"

Write-Host "`n=== 13. 安全兜底：规则命中才递卡片 ==="
$plain = Get-Payload (Invoke-Call $tokenA "publish_signal" @{
    content = "今天天气不错，随便写点什么。"; tags = @("随笔"); mode = "broadcast"
    idempotencyKey = [guid]::NewGuid().ToString()
} 15) "publish_signal(plain)"
Assert ($null -eq $plain.support) "普通内容不递卡片"

$hinted = Get-Payload (Invoke-Call $tokenA "publish_signal" @{
    content = "最近真的不想活了。"; tags = @("随笔"); mode = "broadcast"
    idempotencyKey = [guid]::NewGuid().ToString()
} 16) "publish_signal(hit)"
Assert ($null -ne $hinted.support) "命中时返回 support"
Assert ($hinted.support.resourceUri -eq "ui://shudong/crisis.html?v=1") "support 指向求助资源面板"
Assert ($hinted.support.message -notlike "*检测到*") "文案不含'检测到'（不宣称识别能力）"

Write-Host "`n============================"
if ($script:fail -eq 0) { Write-Host "✅ 场景自检全部通过" } else { Write-Host "❌ 失败 $($script:fail) 项" }
Write-Host "============================"
exit $script:fail
