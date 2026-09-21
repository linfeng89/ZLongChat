namespace ZLongChat.Contracts;

/// <summary>
/// REST 路由与 Hub 路径常量，前后端共用。
/// </summary>
public static class ChatApiRoutes
{
    /// <summary>用户登录</summary>
    public const string Login = "/api/user/login";

    /// <summary>节点上报</summary>
    public const string NodeReport = "/api/node/report";

    /// <summary>查询某用户节点信息（服务端路由模板）</summary>
    public const string NodeQueryTemplate = "/api/node/{userId}";

    /// <summary>查询某用户节点信息（客户端拼 URL 用）</summary>
    public static string Node(string userId) => $"/api/node/{Uri.EscapeDataString(userId)}";

    /// <summary>获取服务端下发的客户端配置（ICE 服务器等）</summary>
    public const string ClientConfig = "/api/client-config";

    /// <summary>SignalR Hub 路径</summary>
    public const string ChatHubPath = "/chathub";
}
