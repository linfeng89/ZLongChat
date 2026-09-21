using ZLongChat.Contracts;

namespace ZLongChat.API.Options;

/// <summary>
/// 鉴权配置（对应 <c>ZLongChat:Auth</c> 节）。
/// 当前是开发阶段的占位实现：登录只校验用户ID非空，并签发固定前缀的假令牌。
/// </summary>
public sealed class AuthSettings
{
    /// <summary>开发令牌前缀</summary>
    public string DevTokenPrefix { get; set; } = ZLongChatDefaults.DevTokenPrefix;
}
