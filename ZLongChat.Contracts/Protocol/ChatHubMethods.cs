namespace ZLongChat.Contracts;

/// <summary>
/// SignalR Hub 方法名常量。
/// 前后端共用同一份常量，改名时编译期就能发现两端不一致。
/// </summary>
public static class ChatHubMethods
{
    // ---------- 客户端 → 服务端 ----------

    /// <summary>转发 SDP（参数：toUserId, sdp, sdpType）</summary>
    public const string SendSdp = "SendSdp";

    /// <summary>转发 ICE 候选（参数：toUserId, candidateJson）</summary>
    public const string SendIceCandidate = "SendIceCandidate";

    /// <summary>发送消息（对方在线直接转发，离线进队列）（参数：toUserId, message）</summary>
    public const string SendOfflineMessage = "SendOfflineMessage";

    // ---------- 服务端 → 客户端 ----------

    /// <summary>收到 SDP（参数：fromUserId, sdp, sdpType）</summary>
    public const string ReceiveSdp = "ReceiveSdp";

    /// <summary>收到 ICE 候选（参数：fromUserId, candidateJson）</summary>
    public const string ReceiveIceCandidate = "ReceiveIceCandidate";

    /// <summary>收到聊天消息（参数：ChatMessage）</summary>
    public const string ReceiveMessage = "ReceiveMessage";

    /// <summary>收到离线消息批量推送（参数：ChatMessage[]）</summary>
    public const string ReceiveOfflineMessages = "ReceiveOfflineMessages";
}
