namespace ZLongChat.Core;

/// <summary>调用者角色</summary>
public enum UserRole
{
    /// <summary>尚未确定</summary>
    Unknown = 0,

    /// <summary>只会倾诉</summary>
    Seeker = 1,

    /// <summary>只会倾听</summary>
    Listener = 2,

    /// <summary>两者都可</summary>
    Both = 3
}

/// <summary>
/// 一次调用的身份上下文。
///
/// <b>业务层永远看不到 token</b> —— MCP 层 / Web 层负责把 token 换成它。
/// </summary>
/// <param name="UserId">实名 ID（服务端内部使用，不向前台暴露）</param>
/// <param name="AnonymousId">前台匿名 ID（每次会话结束轮换）</param>
/// <param name="Role">角色</param>
public sealed record CallContext(
    string UserId,
    string AnonymousId,
    UserRole Role = UserRole.Both)
{
    /// <summary>是否已认证</summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
}

/// <summary>
/// 身份端口：把外部凭据换成 <see cref="CallContext"/>。
/// MCP 层、Web 层共用同一实现。
/// </summary>
public interface IIdentityService
{
    /// <summary>
    /// 解析访问令牌。失败时抛 <see cref="Contracts.Shudong.ShudongException"/>（UNAUTHORIZED）。
    /// </summary>
    CallContext Resolve(string? token);

    /// <summary>会话结束时轮换匿名 ID（契约 §2.3：每次会话结束自动轮换）</summary>
    void RotateAnonymousId(string userId);
}

/// <summary>
/// 开发用实现：token 形如 <c>dev_token_&lt;userId&gt;</c>。
/// 生产环境替换为真实令牌校验，接口不变。
/// </summary>
public sealed class DevIdentityService : IIdentityService
{
    private const string Prefix = "dev_token_";

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _anonymousIds =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public CallContext Resolve(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new Contracts.Shudong.ShudongException(
                Contracts.Shudong.ShudongErrorCodes.Unauthorized, "访问令牌无效或已过期");
        }

        var userId = token[Prefix.Length..];
        if (string.IsNullOrEmpty(userId))
        {
            throw new Contracts.Shudong.ShudongException(
                Contracts.Shudong.ShudongErrorCodes.Unauthorized, "访问令牌无效");
        }

        var anonymousId = _anonymousIds.GetOrAdd(userId, _ => $"anon_{Guid.NewGuid():N}"[..12]);
        return new CallContext(userId, anonymousId);
    }

    /// <inheritdoc />
    public void RotateAnonymousId(string userId) => _anonymousIds.TryRemove(userId, out _);
}
