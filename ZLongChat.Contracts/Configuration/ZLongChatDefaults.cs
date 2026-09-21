namespace ZLongChat.Contracts;

/// <summary>
/// 全部缺省值集中在这里，作为「没有配置文件时的兜底」。
///
/// 这样做的好处：<c>http://localhost:5000</c> 这类值以前散落在 4 个文件里，
/// 改一次要改 4 处、漏一处就行为不一致；现在只有这一个定义点。
/// 真正要改行为请改各项目的 appsettings.json，这里只保证「不带配置文件也能跑起来」。
/// </summary>
public static class ZLongChatDefaults
{
    // ---------------- 服务端 ----------------

    /// <summary>缺省监听端口</summary>
    public const int ServerPort = 5000;

    /// <summary>缺省服务地址</summary>
    public static readonly string ServerBaseUrl = $"http://localhost:{ServerPort}";

    /// <summary>缺省开发令牌前缀</summary>
    public const string DevTokenPrefix = "dev_token_";

    // ---------------- 客户端 ----------------

    /// <summary>缺省 SignalR Hub 路径</summary>
    public const string ChatHubPath = "/chathub";

    /// <summary>缺省 P2P ACK 超时（毫秒）</summary>
    public const int AckTimeoutMs = 3000;

    /// <summary>缺省心跳间隔（毫秒）</summary>
    public const int HeartbeatIntervalMs = 5000;

    /// <summary>缺省心跳超时（秒）</summary>
    public const int HeartbeatTimeoutSeconds = 15;

    /// <summary>缺省断线重连延迟（秒）</summary>
    public const int ReconnectDelaySeconds = 3;

    /// <summary>缺省自动重连次数上限（0 = 不限次数）</summary>
    public const int MaxReconnectAttempts = 3;

    /// <summary>缺省单次重连等待上限（秒）</summary>
    public const int MaxReconnectDelaySeconds = 30;

    /// <summary>缺省 P2P 建连超时（秒）：超过此时长仍未连通即判定本次尝试失败</summary>
    public const int P2PConnectTimeoutSeconds = 20;

    /// <summary>缺省 DataChannel 名称</summary>
    public const string DataChannelLabel = "chat";

    /// <summary>缺省 ICE 收集超时（毫秒）</summary>
    public const int GatherTimeoutMs = 30000;

    // ---------------- AI 机器人 ----------------

    /// <summary>缺省机器人用户ID</summary>
    public const string AiBotUserId = "aibot";

    /// <summary>缺省每个会话保留的历史消息条数</summary>
    public const int AiBotHistoryLimit = 20;

    /// <summary>缺省流式分片字符数</summary>
    public const int AiBotChunkSize = 6;

    /// <summary>缺省流式分片间隔（毫秒）</summary>
    public const int AiBotChunkDelayMs = 60;

    // ---------------- 树洞泡泡（MCP） ----------------

    /// <summary>缺省 MCP 端点路径（HTTP 传输）</summary>
    public const string ShudongMcpPath = "/mcp";
}
