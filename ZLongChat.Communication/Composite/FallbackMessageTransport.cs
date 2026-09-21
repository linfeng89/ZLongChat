using Microsoft.Extensions.Logging;
using ZLongChat.Communication.Abstractions;
using ZLongChat.Contracts;

namespace ZLongChat.Communication.Composite;

/// <summary>
/// 复合通道：优先走 P2P 直连，失败或超时自动降级到服务端转发。
///
/// 这就是改造前 MainWindow.SendMessage 里那段「P2P 优先 + 3 秒 ACK 超时转离线」的策略，
/// 现在被封装成一个可替换的策略对象：想改成「只走直连」或「按消息类型选通道」，
/// 换掉这个类即可，UI 不用动。
/// </summary>
public sealed class FallbackMessageTransport : IMessageTransport
{
    private readonly IMessageTransport _primary;
    private readonly IMessageTransport _fallback;
    private readonly ILogger<FallbackMessageTransport>? _logger;

    private string _targetUserId = string.Empty;
    private bool _disposed;

    /// <param name="primary">优先通道（通常是 P2P 直连）</param>
    /// <param name="fallback">兜底通道（通常是服务端转发）</param>
    /// <param name="logger">日志</param>
    public FallbackMessageTransport(
        IMessageTransport primary,
        IMessageTransport fallback,
        ILogger<FallbackMessageTransport>? logger = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _logger = logger;

        _primary.MessageReceived += OnMessageReceived;
        _fallback.MessageReceived += OnMessageReceived;
        _primary.StateChanged += OnStateChanged;
        _fallback.StateChanged += OnStateChanged;
        _primary.Notice += OnNotice;
        _fallback.Notice += OnNotice;
    }

    /// <inheritdoc />
    public string Name => $"{_primary.Name}（失败自动降级 {_fallback.Name}）";

    /// <inheritdoc />
    public TransportState State => _primary.IsAvailable ? TransportState.Connected : _fallback.State;

    /// <inheritdoc />
    public bool IsAvailable => _primary.IsAvailable || _fallback.IsAvailable;

    /// <inheritdoc />
    public string? PeerUserId => _primary.PeerUserId ?? _fallback.PeerUserId;

    /// <summary>当前实际生效的通道（用于 UI 展示「现在走的是直连还是转发」）</summary>
    public IMessageTransport ActiveTransport => _primary.IsAvailable ? _primary : _fallback;

    public event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    public event EventHandler<TransportMessageEventArgs>? MessageReceived;

    public event EventHandler<string>? Notice;

    /// <inheritdoc />
    public async Task ConnectAsync(TransportConnectContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        _targetUserId = context.TargetUserId ?? string.Empty;

        // 先让兜底通道就绪，保证 P2P 打不通时也一定能发出去
        await _fallback.ConnectAsync(context with { AsInitiator = false }, cancellationToken).ConfigureAwait(false);
        await _primary.ConnectAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrEmpty(message.ToUserId))
        {
            message.ToUserId = _primary.PeerUserId ?? _targetUserId;
        }

        if (_primary.IsAvailable)
        {
            try
            {
                await _primary.SendAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (TransportException ex)
            {
                _logger?.LogWarning(ex, "首选通道发送失败，降级到兜底通道");
                RaiseNotice($"⚠️  {_primary.Name}发送失败（{ex.Message}），降级为{_fallback.Name}");
            }
        }
        else
        {
            RaiseNotice($"ℹ️  {_primary.Name}不可用，直接使用{_fallback.Name}");
        }

        await _fallback.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _primary.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await _fallback.DisconnectAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnMessageReceived(object? sender, TransportMessageEventArgs e)
        => MessageReceived?.Invoke(this, e);

    private void OnStateChanged(object? sender, TransportStateChangedEventArgs e)
        => StateChanged?.Invoke(this, e);

    private void OnNotice(object? sender, string message)
        => Notice?.Invoke(this, message);

    private void RaiseNotice(string message)
    {
        _logger?.LogDebug("{Message}", message);
        Notice?.Invoke(this, message);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _primary.MessageReceived -= OnMessageReceived;
        _fallback.MessageReceived -= OnMessageReceived;
        _primary.StateChanged -= OnStateChanged;
        _fallback.StateChanged -= OnStateChanged;
        _primary.Notice -= OnNotice;
        _fallback.Notice -= OnNotice;

        await _primary.DisposeAsync().ConfigureAwait(false);
        await _fallback.DisposeAsync().ConfigureAwait(false);
    }
}
