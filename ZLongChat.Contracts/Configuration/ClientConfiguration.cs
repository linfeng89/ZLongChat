namespace ZLongChat.Contracts;

using System.Text.Json.Serialization;

/// <summary>单个 ICE 服务器配置</summary>
public sealed class IceServerOptions
{
    /// <summary>地址，如 stun:stun.l.google.com:19302</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>TURN 用户名（STUN 不需要）</summary>
    public string? Username { get; set; }

    /// <summary>TURN 密码（STUN 不需要）。建议用环境变量注入，不要提交进仓库。</summary>
    public string? Credential { get; set; }
}

/// <summary>
/// 客户端配置：客户端连接服务端、建立 P2P 通道所需的**全部**参数。
///
/// 设计要点：
/// <list type="bullet">
///   <item>整个解决方案里只有这一个配置类型、一个节名（<c>ZLongChat:Client</c>），
///         取代了以前 API 的 <c>STUNSettings</c> 与 PC 的 <c>WebRtc:IceServers</c> 两套写法。</item>
///   <item>服务端持有同一份结构，并通过 <c>/api/client-config</c> 下发，
///         于是「STUN/TURN 改一处，所有客户端生效」。</item>
///   <item>客户端本地仍可留一份（连不上服务端时兜底）。</item>
/// </list>
/// </summary>
public sealed class ClientConfiguration
{
    // ---------------------------------------------------------- 连接服务端

    /// <summary>服务端基地址（仅客户端本地使用，不下发）</summary>
    [JsonIgnore]
    public string ServerBaseUrl { get; set; } = ZLongChatDefaults.ServerBaseUrl;

    /// <summary>SignalR Hub 路径</summary>
    public string ChatHubPath { get; set; } = ZLongChatDefaults.ChatHubPath;

    /// <summary>P2P 发送等待 ACK 的超时时间（毫秒），超时即降级服务端转发</summary>
    public int AckTimeoutMs { get; set; } = ZLongChatDefaults.AckTimeoutMs;

    /// <summary>P2P 心跳发送间隔（毫秒）</summary>
    public int HeartbeatIntervalMs { get; set; } = ZLongChatDefaults.HeartbeatIntervalMs;

    /// <summary>超过多久没收到对端心跳就判定连接已死（秒）</summary>
    public int HeartbeatTimeoutSeconds { get; set; } = ZLongChatDefaults.HeartbeatTimeoutSeconds;

    /// <summary>P2P 断开后自动重连的延迟（秒）</summary>
    public int ReconnectDelaySeconds { get; set; } = ZLongChatDefaults.ReconnectDelaySeconds;

    /// <summary>
    /// P2P 自动重连的次数上限。超过后停止重连并提示用户，
    /// 避免在「本机没网络 / 对端一直不在」时无限刷屏（0 或负数 = 不限次数）。
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = ZLongChatDefaults.MaxReconnectAttempts;

    /// <summary>单次重连等待的上限（秒）。重连延迟按 2 倍退避递增，但不超过此值。</summary>
    public int MaxReconnectDelaySeconds { get; set; } = ZLongChatDefaults.MaxReconnectDelaySeconds;

    /// <summary>
    /// P2P 建连超时（秒）。超过此时长仍未连通即判定本次尝试失败。
    ///
    /// 这个超时是必要的：当对端已下线时，我方发出的 Offer 不会得到 Answer，
    /// ICE 也就永远不会进入 failed 状态 —— 没有这个看门狗，重试循环会静默卡死。
    /// </summary>
    public int P2PConnectTimeoutSeconds { get; set; } = ZLongChatDefaults.P2PConnectTimeoutSeconds;

    // ---------------------------------------------------------- P2P 通道

    /// <summary>DataChannel 名称</summary>
    public string DataChannelLabel { get; set; } = ZLongChatDefaults.DataChannelLabel;

    /// <summary>ICE 收集超时（毫秒）</summary>
    public int GatherTimeoutMs { get; set; } = ZLongChatDefaults.GatherTimeoutMs;

    /// <summary>是否跳过 IPv6 候选（省去无效等待）</summary>
    public bool SkipIPv6Candidates { get; set; } = true;

    /// <summary>ICE 服务器列表（STUN 打洞 / TURN 中继）</summary>
    public List<IceServerOptions> IceServers { get; set; } = new();

    // ---------------------------------------------------------- 客户端行为

    /// <summary>是否拉取服务端下发的配置（true 时服务端配置优先，仅客户端本地使用，不下发）</summary>
    [JsonIgnore]
    public bool UseServerProvidedConfig { get; set; } = true;

    /// <summary>拼接带 userId 的 Hub 地址</summary>
    public string BuildHubUrl(string userId)
    {
        var baseUrl = ServerBaseUrl.TrimEnd('/');
        var path = ChatHubPath.StartsWith('/') ? ChatHubPath : "/" + ChatHubPath;
        return $"{baseUrl}{path}?userId={Uri.EscapeDataString(userId)}";
    }

    /// <summary>
    /// 用服务端下发的配置覆盖本地值。
    /// 只覆盖「服务端有权决定」的字段；<see cref="ServerBaseUrl"/> 与
    /// <see cref="UseServerProvidedConfig"/> 属于客户端本地决定，不会被覆盖。
    /// </summary>
    public void ApplyRemote(ClientConfiguration remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        if (!string.IsNullOrWhiteSpace(remote.ChatHubPath))
        {
            ChatHubPath = remote.ChatHubPath;
        }

        AckTimeoutMs = Positive(remote.AckTimeoutMs, AckTimeoutMs);
        HeartbeatIntervalMs = Positive(remote.HeartbeatIntervalMs, HeartbeatIntervalMs);
        HeartbeatTimeoutSeconds = Positive(remote.HeartbeatTimeoutSeconds, HeartbeatTimeoutSeconds);
        ReconnectDelaySeconds = Positive(remote.ReconnectDelaySeconds, ReconnectDelaySeconds);
        MaxReconnectAttempts = remote.MaxReconnectAttempts;
        MaxReconnectDelaySeconds = Positive(remote.MaxReconnectDelaySeconds, MaxReconnectDelaySeconds);
        P2PConnectTimeoutSeconds = Positive(remote.P2PConnectTimeoutSeconds, P2PConnectTimeoutSeconds);
        GatherTimeoutMs = Positive(remote.GatherTimeoutMs, GatherTimeoutMs);
        SkipIPv6Candidates = remote.SkipIPv6Candidates;

        if (!string.IsNullOrWhiteSpace(remote.DataChannelLabel))
        {
            DataChannelLabel = remote.DataChannelLabel;
        }

        if (remote.IceServers.Count > 0)
        {
            IceServers = remote.IceServers;
        }
    }

    /// <summary>把明显不合理的配置修正回可用值，避免别人手写配置时把程序改崩</summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(ServerBaseUrl))
        {
            ServerBaseUrl = ZLongChatDefaults.ServerBaseUrl;
        }

        if (string.IsNullOrWhiteSpace(ChatHubPath))
        {
            ChatHubPath = ZLongChatDefaults.ChatHubPath;
        }

        if (string.IsNullOrWhiteSpace(DataChannelLabel))
        {
            DataChannelLabel = ZLongChatDefaults.DataChannelLabel;
        }

        AckTimeoutMs = Positive(AckTimeoutMs, ZLongChatDefaults.AckTimeoutMs);
        HeartbeatIntervalMs = Positive(HeartbeatIntervalMs, ZLongChatDefaults.HeartbeatIntervalMs);
        HeartbeatTimeoutSeconds = Positive(HeartbeatTimeoutSeconds, ZLongChatDefaults.HeartbeatTimeoutSeconds);
        ReconnectDelaySeconds = Positive(ReconnectDelaySeconds, ZLongChatDefaults.ReconnectDelaySeconds);
        MaxReconnectDelaySeconds = Positive(MaxReconnectDelaySeconds, ZLongChatDefaults.MaxReconnectDelaySeconds);
        P2PConnectTimeoutSeconds = Positive(P2PConnectTimeoutSeconds, ZLongChatDefaults.P2PConnectTimeoutSeconds);
        GatherTimeoutMs = Positive(GatherTimeoutMs, ZLongChatDefaults.GatherTimeoutMs);
        IceServers ??= new List<IceServerOptions>();
    }

    private static int Positive(int value, int fallback) => value > 0 ? value : fallback;
}
