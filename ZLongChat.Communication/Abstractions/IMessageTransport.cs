using ZLongChat.Contracts;

namespace ZLongChat.Communication.Abstractions;

/// <summary>
/// 统一消息通道。
///
/// 这是整套通信抽象的核心：不管底层是 WebRTC DataChannel、SignalR 中继、
/// 还是未来的 WebSocket / gRPC / MQTT / 原始 TCP，上层只认这个接口。
///
/// 典型用法：
/// <code>
///   await transport.ConnectAsync(new TransportConnectContext(peerId));
///   await transport.SendAsync(ChatMessage.CreateText(me, peerId, "hi"));
/// </code>
/// </summary>
public interface IMessageTransport : IAsyncDisposable
{
    /// <summary>通道名称，用于日志与「当前走哪条通道」展示</summary>
    string Name { get; }

    /// <summary>当前连接状态</summary>
    TransportState State { get; }

    /// <summary>
    /// 现在是否真的能发。注意与 <see cref="State"/> 的区别：
    /// P2P 通道只有握手完成并收到心跳后才算可用。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>当前对端用户ID（尚未确定时为 null）</summary>
    string? PeerUserId { get; }

    /// <summary>连接状态变化</summary>
    event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    /// <summary>收到对端消息</summary>
    event EventHandler<TransportMessageEventArgs>? MessageReceived;

    /// <summary>人类可读的过程提示（发送进度、降级原因等），由上层决定是否展示</summary>
    event EventHandler<string>? Notice;

    /// <summary>建立连接</summary>
    Task ConnectAsync(TransportConnectContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息。
    /// 失败时抛 <see cref="TransportException"/>，调用方可以捕获后改用其它通道重发。
    /// </summary>
    Task SendAsync(ChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>断开连接并清理资源</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
