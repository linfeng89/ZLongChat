using ZLongChat.Contracts;

namespace ZLongChat.Communication.Abstractions;

/// <summary>
/// 信令服务：负责在对端之间交换 SDP / ICE 候选，以及通过服务端转发消息。
///
/// 当前实现是 SignalR，将来换成 WebSocket / gRPC / MQTT 只需要换实现类，
/// P2P 通道（<see cref="IMessageTransport"/>）不需要任何改动。
/// </summary>
public interface ISignalingService : IAsyncDisposable
{
    /// <summary>本端用户ID（未连接时为 null）</summary>
    string? LocalUserId { get; }

    /// <summary>当前连接状态</summary>
    TransportState State { get; }

    /// <summary>收到对端 SDP</summary>
    event EventHandler<SdpSignal>? SdpReceived;

    /// <summary>收到对端 ICE 候选</summary>
    event EventHandler<IceCandidateSignal>? IceCandidateReceived;

    /// <summary>收到服务端转发的聊天消息（在线转发或离线补发）</summary>
    event EventHandler<TransportMessageEventArgs>? MessageReceived;

    /// <summary>连接状态变化</summary>
    event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    /// <summary>连接信令服务</summary>
    Task ConnectAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>断开设连接</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>发送 SDP</summary>
    Task SendSdpAsync(string toUserId, string sdp, string sdpType, CancellationToken cancellationToken = default);

    /// <summary>发送 ICE 候选</summary>
    Task SendIceCandidateAsync(string toUserId, IceCandidateSignal candidate, CancellationToken cancellationToken = default);

    /// <summary>通过服务端转发消息（P2P 不可用时的兜底通道）</summary>
    Task RelayMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
}
