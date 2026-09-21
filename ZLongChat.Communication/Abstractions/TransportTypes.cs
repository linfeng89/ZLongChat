using ZLongChat.Contracts;

namespace ZLongChat.Communication.Abstractions;

/// <summary>通信通道的连接状态</summary>
public enum TransportState
{
    /// <summary>未连接</summary>
    Disconnected,

    /// <summary>连接中</summary>
    Connecting,

    /// <summary>已连接且可用</summary>
    Connected,

    /// <summary>重连中</summary>
    Reconnecting,

    /// <summary>连接失败/已断开，不可用</summary>
    Failed
}

/// <summary>建连参数。不同通信方式需要的信息不同，统一放这里，避免接口随实现变动。</summary>
/// <param name="TargetUserId">对端用户ID</param>
/// <param name="AsInitiator">是否由本端主动发起建连</param>
public sealed record TransportConnectContext(string TargetUserId, bool AsInitiator = true)
{
    /// <summary>扩展参数：给未来通信方式预留的自定义参数（如房间号、频道名）</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>收到消息的事件参数</summary>
public sealed class TransportMessageEventArgs : EventArgs
{
    /// <summary>收到的消息</summary>
    public ChatMessage Message { get; }

    /// <summary>消息来自哪个通道</summary>
    public string TransportName { get; }

    public TransportMessageEventArgs(ChatMessage message, string transportName)
    {
        Message = message;
        TransportName = transportName;
    }
}

/// <summary>连接状态变化的事件参数</summary>
public sealed class TransportStateChangedEventArgs : EventArgs
{
    /// <summary>新状态</summary>
    public TransportState State { get; }

    /// <summary>变化原因（用于日志/提示）</summary>
    public string? Reason { get; }

    public TransportStateChangedEventArgs(TransportState state, string? reason = null)
    {
        State = state;
        Reason = reason;
    }
}

/// <summary>通信层异常。上层据此判断「换一条通道重试」。</summary>
public class TransportException : Exception
{
    public TransportException(string message) : base(message) { }

    public TransportException(string message, Exception innerException) : base(message, innerException) { }
}
