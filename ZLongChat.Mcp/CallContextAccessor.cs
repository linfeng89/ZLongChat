using Microsoft.AspNetCore.Http;
using ZLongChat.Core;

namespace ZLongChat.Mcp;

/// <summary>
/// 把「当前 MCP 请求」换成 <see cref="CallContext"/>。
///
/// 令牌来源（按优先级）：
/// <list type="number">
///   <item>HTTP 传输：<c>Authorization: Bearer &lt;token&gt;</c> 头</item>
///   <item>stdio 传输：环境变量 <c>SHUDONG_TOKEN</c></item>
/// </list>
///
/// <b>业务层永远看不到 token</b> —— 本类负责在边界处把它消化掉。
/// </summary>
public sealed class CallContextAccessor
{
    private const string BearerPrefix = "Bearer ";
    private const string EnvTokenName = "SHUDONG_TOKEN";

    private readonly IIdentityService _identity;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly string? _environmentToken;

    public CallContextAccessor(IIdentityService identity, IHttpContextAccessor? httpContextAccessor = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _httpContextAccessor = httpContextAccessor;
        _environmentToken = Environment.GetEnvironmentVariable(EnvTokenName);
    }

    /// <summary>解析当前调用者。令牌无效时抛 <c>ShudongException(UNAUTHORIZED)</c>。</summary>
    public CallContext Current()
    {
        var header = _httpContextAccessor?.HttpContext?.Request.Headers.Authorization.ToString();
        var token = ExtractBearer(header) ?? _environmentToken;
        return _identity.Resolve(token);
    }

    private static string? ExtractBearer(string? header)
        => !string.IsNullOrWhiteSpace(header) &&
           header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? header[BearerPrefix.Length..].Trim()
            : null;
}
