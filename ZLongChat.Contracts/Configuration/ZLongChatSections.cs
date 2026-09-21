namespace ZLongChat.Contracts;

/// <summary>
/// 配置节名称常量。前后端、机器人共用同一份，保证「同一个设置在所有项目里叫同一个名字」。
///
/// 统一挂在 <c>ZLongChat</c> 根节下，避免与框架自带的 <c>Logging</c>、<c>AllowedHosts</c> 等冲突。
/// </summary>
public static class ZLongChatSections
{
    /// <summary>根节</summary>
    public const string Root = "ZLongChat";

    /// <summary>服务端监听配置（仅 API 使用）：ZLongChat:Server</summary>
    public const string Server = Root + ":Server";

    /// <summary>
    /// 客户端配置（API 持有并下发，PC / AiBot 本地也可有一份作为兜底）：
    /// ZLongChat:Client
    /// </summary>
    public const string Client = Root + ":Client";

    /// <summary>鉴权配置：ZLongChat:Auth</summary>
    public const string Auth = Root + ":Auth";

    /// <summary>AI 机器人配置：ZLongChat:AiBot</summary>
    public const string AiBot = Root + ":AiBot";

    /// <summary>树洞泡泡配置（限额、留存、MCP 端点）：ZLongChat:Shudong</summary>
    public const string Shudong = Root + ":Shudong";
}
