using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using ZLongChat.Ai.Abstractions;
using ZLongChat.Communication.Abstractions;
using ZLongChat.Contracts;

namespace ZLongChat.AiBot;

/// <summary>
/// AI 机器人的主体：作为「一个普通用户」接入 Hub，收到消息就生成回复回发。
///
/// 它只依赖通信抽象（<see cref="ISignalingService"/> / <see cref="IMessageTransport"/>），
/// 所以底层走 SignalR 转发还是 WebRTC 直连，它都无感；
/// 生成回复的逻辑则完全交给 <see cref="IAiAssistant"/>。
///
/// 消息用有界队列串行处理：大模型调用有延迟，避免同一会话并发多轮导致回复错序。
/// </summary>
public sealed class AiBotWorker : IAsyncDisposable
{
    private readonly string _userId;
    private readonly IAiAssistant _assistant;
    private readonly ISignalingService _signaling;
    private readonly IMessageTransport _transport;
    private readonly ILogger<AiBotWorker>? _logger;
    private readonly int _historyLimit;

    private readonly Channel<ChatMessage> _inbox = Channel.CreateUnbounded<ChatMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>每个对端一条对话历史</summary>
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _histories = new(StringComparer.Ordinal);

    private bool _disposed;

    public AiBotWorker(
        string userId,
        IAiAssistant assistant,
        ISignalingService signaling,
        IMessageTransport transport,
        int historyLimit = 20,
        ILogger<AiBotWorker>? logger = null)
    {
        _userId = string.IsNullOrWhiteSpace(userId)
            ? throw new ArgumentException("机器人用户ID不能为空", nameof(userId))
            : userId;
        _assistant = assistant ?? throw new ArgumentNullException(nameof(assistant));
        _signaling = signaling ?? throw new ArgumentNullException(nameof(signaling));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _historyLimit = Math.Max(2, historyLimit);
        _logger = logger;
    }

    /// <summary>连接服务并开始处理消息，直到取消</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _transport.MessageReceived += OnMessageReceived;
        _transport.StateChanged += OnTransportStateChanged;

        await _signaling.ConnectAsync(_userId, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation(
            "AI 机器人 {UserId} 已上线，助手实现：{Assistant}",
            _userId, _assistant.Name);

        try
        {
            await ConsumeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常的 Ctrl+C 退出
        }
    }

    // ------------------------------------------------------------ 收消息

    private void OnMessageReceived(object? sender, TransportMessageEventArgs e)
    {
        var message = e.Message;

        // 只处理文本，且忽略自己发出去的（避免自问自答死循环）
        if (message.Type != ChatMessageType.Text)
        {
            return;
        }

        if (string.Equals(message.FromUserId, _userId, StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrEmpty(message.FromUserId))
        {
            return;
        }

        _inbox.Writer.TryWrite(message);
    }

    private void OnTransportStateChanged(object? sender, TransportStateChangedEventArgs e)
        => _logger?.LogDebug("通道 {Transport} 状态：{State}（{Reason}）", _transport.Name, e.State, e.Reason);

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (var incoming in _inbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await HandleAsync(incoming, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "处理来自 {Peer} 的消息失败", incoming.FromUserId);
            }
        }
    }

    // ------------------------------------------------------------ 生成回复

    private async Task HandleAsync(ChatMessage incoming, CancellationToken cancellationToken)
    {
        var peer = incoming.FromUserId;
        var history = _histories.GetOrAdd(peer, _ => new List<ChatMessage>());

        AppendAndTrim(history, incoming);

        _logger?.LogInformation("← {Peer}：{Text}", peer, incoming.GetText());

        var context = new AiConversationContext
        {
            ConversationId = peer,
            UserId = peer,
            AssistantUserId = _userId,
            History = Snapshot(history)
        };

        // 流式分片在这里聚合成一条完整消息再发出
        // （协议层目前按整条消息投递；将来要做「打字机效果」只需新增一种消息类型）
        var builder = new StringBuilder();
        await foreach (var chunk in _assistant.StreamReplyAsync(context, cancellationToken).ConfigureAwait(false))
        {
            builder.Append(chunk);
        }

        var replyText = builder.ToString();
        if (string.IsNullOrWhiteSpace(replyText))
        {
            _logger?.LogWarning("助手 {Assistant} 返回了空回复，已跳过", _assistant.Name);
            return;
        }

        var reply = ChatMessage.CreateText(_userId, peer, replyText);
        AppendAndTrim(history, reply);

        await _transport.SendAsync(reply, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("→ {Peer}：{Text}", peer, replyText);
    }

    // ------------------------------------------------------------ 历史管理

    private void AppendAndTrim(List<ChatMessage> history, ChatMessage message)
    {
        lock (history)
        {
            history.Add(message);

            int excess = history.Count - _historyLimit;
            if (excess > 0)
            {
                history.RemoveRange(0, excess);
            }
        }
    }

    private static IReadOnlyList<ChatMessage> Snapshot(List<ChatMessage> history)
    {
        lock (history)
        {
            return history.ToArray();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        _transport.MessageReceived -= OnMessageReceived;
        _transport.StateChanged -= OnTransportStateChanged;
        _inbox.Writer.TryComplete();

        return ValueTask.CompletedTask;
    }
}
