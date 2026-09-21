using Microsoft.AspNetCore.SignalR;
using ZLongChat.API.Services;
using ZLongChat.Contracts;

namespace ZLongChat.API.Hubs;

/// <summary>
/// 聊天信令 Hub：负责 SDP / ICE 转发，以及消息的服务端兜底转发与离线补发。
///
/// 改造前它和 REST 接口、DTO、静态存储全挤在一个 Program.cs 里，
/// 还依赖静态字段 <c>InMemoryStore</c>；现在依赖通过构造函数注入，
/// 存储换成 Redis / 数据库时这个类一行都不用改。
/// </summary>
public sealed class ChatHub : Hub<IChatHubClient>
{
    private readonly IUserConnectionRegistry _connections;
    private readonly IOfflineMessageStore _offlineMessages;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IUserConnectionRegistry connections,
        IOfflineMessageStore offlineMessages,
        ILogger<ChatHub> logger)
    {
        _connections = connections;
        _offlineMessages = offlineMessages;
        _logger = logger;
    }

    /// <summary>当前连接对应的用户ID（来自连接串上的 query 参数）</summary>
    private string CurrentUserId => Context.GetHttpContext()?.Request.Query["userId"].ToString() ?? string.Empty;

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId;

        if (!string.IsNullOrEmpty(userId))
        {
            _connections.Set(userId, Context.ConnectionId);
            _logger.LogInformation("用户{UserId}上线，连接ID：{ConnectionId}", userId, Context.ConnectionId);

            // 上线立刻推送所有离线消息，一条都不能漏
            var pending = _offlineMessages.Drain(userId);
            if (pending.Count > 0)
            {
                _logger.LogInformation("用户{UserId}有{Count}条离线消息，开始推送", userId, pending.Count);
                foreach (var message in pending)
                {
                    await Clients.Caller.ReceiveMessage(message);
                }
                _logger.LogInformation("离线消息推送完成");
            }
            else
            {
                _logger.LogInformation("用户{UserId}没有离线消息", userId);
            }
        }

        await base.OnConnectedAsync();
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = CurrentUserId;
        if (!string.IsNullOrEmpty(userId))
        {
            _connections.Remove(userId);
            _logger.LogInformation("用户{UserId}下线", userId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>转发 SDP 信令</summary>
    public async Task SendSdp(string toUserId, string sdp, string sdpType)
    {
        _logger.LogInformation(
            "收到SDP转发请求：目标用户={ToUserId}, 类型={SdpType}, SDP长度={Length}",
            toUserId, sdpType, sdp?.Length ?? 0);

        if (string.IsNullOrEmpty(toUserId) || string.IsNullOrEmpty(sdp))
        {
            _logger.LogError("SDP参数为空，转发失败");
            return;
        }

        if (_connections.TryGet(toUserId, out var connectionId))
        {
            await Clients.Client(connectionId).ReceiveSdp(CurrentUserId, sdp, sdpType);
            _logger.LogInformation("SDP已转发给用户{ToUserId}", toUserId);
        }
        else
        {
            _logger.LogWarning("用户{ToUserId}不在线，SDP转发失败", toUserId);
        }
    }

    /// <summary>转发 ICE 候选</summary>
    public async Task SendIceCandidate(string toUserId, string candidateJson)
    {
        if (string.IsNullOrEmpty(toUserId) || string.IsNullOrEmpty(candidateJson))
        {
            return;
        }

        if (_connections.TryGet(toUserId, out var connectionId))
        {
            await Clients.Client(connectionId).ReceiveIceCandidate(CurrentUserId, candidateJson);
        }
        else
        {
            _logger.LogWarning("用户{ToUserId}不在线，ICE候选转发失败", toUserId);
        }
    }

    /// <summary>发送消息：对方在线直接转发，不在线进离线队列</summary>
    public async Task SendOfflineMessage(string toUserId, ChatMessage message)
    {
        if (string.IsNullOrEmpty(toUserId) || message is null)
        {
            return;
        }

        // 补全两端用户ID，让客户端拿到完整上下文
        message.ToUserId = toUserId;
        if (string.IsNullOrEmpty(message.FromUserId))
        {
            message.FromUserId = CurrentUserId;
        }

        if (_connections.TryGet(toUserId, out var connectionId))
        {
            await Clients.Client(connectionId).ReceiveMessage(message);
            _logger.LogInformation("消息已转发：{FromUserId} → {ToUserId}", message.FromUserId, toUserId);
        }
        else
        {
            _offlineMessages.Enqueue(toUserId, message);
            _logger.LogInformation("用户{ToUserId}不在线，消息已入离线队列", toUserId);
        }
    }
}
