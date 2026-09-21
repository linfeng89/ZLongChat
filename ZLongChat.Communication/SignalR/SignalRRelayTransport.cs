using Microsoft.Extensions.Logging;
using ZLongChat.Communication.Abstractions;
using ZLongChat.Contracts;

namespace ZLongChat.Communication.SignalR;

/// <summary>
/// 把信令服务的「服务端转发」能力适配成标准 <see cref="IMessageTransport"/>。
///
/// 这样「P2P 直连」和「服务端转发」就是两个平级的通道，
/// 上层用同一套接口调用，复合通道（<see cref="Composite.FallbackMessageTransport"/>）
/// 也能无差别地做降级。
/// </summary>
public sealed class SignalRRelayTransport : IMessageTransport
{
    private readonly ISignalingService _signaling;
    private readonly ILogger<SignalRRelayTransport>? _logger;
    private string _targetUserId = string.Empty;

    public SignalRRelayTransport(ISignalingService signaling, ILogger<SignalRRelayTransport>? logger = null)
    {
        _signaling = signaling ?? throw new ArgumentNullException(nameof(signaling));
        _logger = logger;

        _signaling.MessageReceived += OnSignalingMessageReceived;
        _signaling.StateChanged += OnSignalingStateChanged;
    }

    /// <inheritdoc />
    public string Name => TransportNames.ServerRelay;

    /// <inheritdoc />
    public TransportState State => _signaling.State;

    /// <inheritdoc />
    public bool IsAvailable => _signaling.State == TransportState.Connected;

    /// <inheritdoc />
    public string? PeerUserId => string.IsNullOrEmpty(_targetUserId) ? null : _targetUserId;

    public event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    public event EventHandler<TransportMessageEventArgs>? MessageReceived;

    public event EventHandler<string>? Notice;

    /// <inheritdoc />
    public Task ConnectAsync(TransportConnectContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 转发通道复用信令连接，这里只记录对端
        _targetUserId = context.TargetUserId ?? string.Empty;
        Notice?.Invoke(this, $"🔌 {Name} 就绪（对端：{_targetUserId}）");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SendAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!IsAvailable)
        {
            throw new TransportException($"{Name} 不可用（{State}）");
        }

        var target = string.IsNullOrEmpty(message.ToUserId) ? _targetUserId : message.ToUserId;
        if (string.IsNullOrEmpty(target))
        {
            throw new TransportException("服务端转发需要指定对端用户ID");
        }

        message.ToUserId = target;

        try
        {
            await _signaling.RelayMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (TransportException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "服务端转发失败");
            throw new TransportException($"服务端转发失败：{ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // 不主动断开信令连接：这条通道是寄生在信令之上的
        _targetUserId = string.Empty;
        return Task.CompletedTask;
    }

    private void OnSignalingMessageReceived(object? sender, TransportMessageEventArgs e)
        => MessageReceived?.Invoke(this, e);

    private void OnSignalingStateChanged(object? sender, TransportStateChangedEventArgs e)
        => StateChanged?.Invoke(this, e);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _signaling.MessageReceived -= OnSignalingMessageReceived;
        _signaling.StateChanged -= OnSignalingStateChanged;
        return ValueTask.CompletedTask;
    }
}
