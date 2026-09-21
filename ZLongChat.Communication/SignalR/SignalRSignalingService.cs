using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ZLongChat.Communication.Abstractions;
using ZLongChat.Contracts;

namespace ZLongChat.Communication.SignalR;

/// <summary>
/// 基于 SignalR 的信令服务实现。
/// 这是 <see cref="ISignalingService"/> 的默认实现，也是「想换信令方式时唯一要替换的类」。
/// </summary>
public sealed class SignalRSignalingService : ISignalingService
{
    private readonly ClientConfiguration _configuration;
    private readonly ILogger<SignalRSignalingService>? _logger;

    private HubConnection? _hub;
    private string? _localUserId;
    private TransportState _state = TransportState.Disconnected;

    public SignalRSignalingService(ClientConfiguration configuration, ILogger<SignalRSignalingService>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
    }

    /// <inheritdoc />
    public string? LocalUserId => _localUserId;

    /// <inheritdoc />
    public TransportState State => _state;

    public event EventHandler<SdpSignal>? SdpReceived;

    public event EventHandler<IceCandidateSignal>? IceCandidateReceived;

    public event EventHandler<TransportMessageEventArgs>? MessageReceived;

    public event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public async Task ConnectAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("用户ID不能为空", nameof(userId));
        }

        if (_hub is not null)
        {
            await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }

        _localUserId = userId;
        SetState(TransportState.Connecting, $"正在连接 {_configuration.BuildHubUrl(userId)}");

        var hub = new HubConnectionBuilder()
            .WithUrl(_configuration.BuildHubUrl(userId))
            .WithAutomaticReconnect()
            .Build();

        RegisterHandlers(hub);

        hub.Reconnecting += error =>
        {
            SetState(TransportState.Reconnecting, error?.Message);
            return Task.CompletedTask;
        };
        hub.Reconnected += _ =>
        {
            SetState(TransportState.Connected, "信令连接已恢复");
            return Task.CompletedTask;
        };
        hub.Closed += error =>
        {
            SetState(TransportState.Failed, error?.Message ?? "信令连接已关闭");
            return Task.CompletedTask;
        };

        _hub = hub;

        try
        {
            await hub.StartAsync(cancellationToken).ConfigureAwait(false);
            SetState(TransportState.Connected, "信令已连接");
        }
        catch (Exception ex)
        {
            SetState(TransportState.Failed, ex.Message);
            _logger?.LogError(ex, "连接信令服务失败：{Url}", _configuration.BuildHubUrl(userId));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var hub = _hub;
        _hub = null;

        if (hub is not null)
        {
            try
            {
                await hub.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "释放 SignalR 连接时出错");
            }
        }

        SetState(TransportState.Disconnected, "已断开设连接");
    }

    /// <inheritdoc />
    public Task SendSdpAsync(string toUserId, string sdp, string sdpType, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("转发SDP：目标={ToUserId} 类型={SdpType} 长度={Length}", toUserId, sdpType, sdp?.Length ?? 0);

        if (string.IsNullOrEmpty(toUserId) || string.IsNullOrEmpty(sdp))
        {
            _logger?.LogError("SDP参数为空，转发失败");
            throw new TransportException("SDP参数为空");
        }

        return SendAsync(ChatHubMethods.SendSdp, cancellationToken, toUserId, sdp, sdpType);
    }

    /// <inheritdoc />
    public Task SendIceCandidateAsync(string toUserId, IceCandidateSignal candidate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(toUserId) || candidate is null || string.IsNullOrEmpty(candidate.Candidate))
        {
            return Task.CompletedTask;
        }

        // 服务端只做透传，所以这里序列化成字符串（保持与改造前一致的线上格式）
        var candidateJson = JsonSerializer.Serialize(candidate);
        return SendAsync(ChatHubMethods.SendIceCandidate, cancellationToken, toUserId, candidateJson);
    }

    /// <inheritdoc />
    public Task RelayMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return SendAsync(ChatHubMethods.SendOfflineMessage, cancellationToken, message.ToUserId, message);
    }

    private async Task SendAsync(string method, CancellationToken cancellationToken, params object?[] args)
    {
        var hub = _hub;
        if (hub is null || hub.State != HubConnectionState.Connected)
        {
            throw new TransportException($"信令未连接（当前状态：{hub?.State.ToString() ?? "null"}）");
        }

        await hub.SendCoreAsync(method, args, cancellationToken).ConfigureAwait(false);
    }

    private void RegisterHandlers(HubConnection hub)
    {
        // 收到对端 SDP
        hub.On<string, string, string>(ChatHubMethods.ReceiveSdp, (fromUserId, sdp, sdpType) =>
        {
            _logger?.LogInformation("收到SDP：来自={FromUserId} 类型={SdpType}", fromUserId, sdpType);
            SdpReceived?.Invoke(this, new SdpSignal
            {
                FromUserId = fromUserId,
                Sdp = sdp,
                SdpType = sdpType
            });
        });

        // 收到对端 ICE 候选
        hub.On<string, string>(ChatHubMethods.ReceiveIceCandidate, (fromUserId, candidateJson) =>
        {
            var candidate = DeserializeCandidate(candidateJson);
            if (candidate is null)
            {
                _logger?.LogWarning("ICE候选格式错误，已跳过：{Json}", candidateJson);
                return;
            }

            candidate.FromUserId = fromUserId;
            IceCandidateReceived?.Invoke(this, candidate);
        });

        // 收到服务端转发的消息（在线转发 / 离线补发都走这里）
        hub.On<ChatMessage>(ChatHubMethods.ReceiveMessage, message =>
        {
            if (message is null)
            {
                return;
            }

            message.ToUserId = string.IsNullOrEmpty(message.ToUserId) ? _localUserId ?? string.Empty : message.ToUserId;
            MessageReceived?.Invoke(this, new TransportMessageEventArgs(message, TransportNames.ServerRelay));
        });

        // 批量离线消息（服务端目前逐条推送，这里保留批量入口以便将来扩展）
        hub.On<List<ChatMessage>>(ChatHubMethods.ReceiveOfflineMessages, messages =>
        {
            if (messages is null)
            {
                return;
            }

            foreach (var message in messages)
            {
                message.ToUserId = string.IsNullOrEmpty(message.ToUserId) ? _localUserId ?? string.Empty : message.ToUserId;
                MessageReceived?.Invoke(this, new TransportMessageEventArgs(message, TransportNames.ServerRelay));
            }
        });
    }

    private static IceCandidateSignal? DeserializeCandidate(string? candidateJson)
    {
        if (string.IsNullOrEmpty(candidateJson))
        {
            return null;
        }

        try
        {
            var candidate = JsonSerializer.Deserialize<IceCandidateSignal>(candidateJson);
            return string.IsNullOrEmpty(candidate?.Candidate) ? null : candidate;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SetState(TransportState state, string? reason = null)
    {
        if (_state == state && reason is null)
        {
            return;
        }

        _state = state;
        _logger?.LogDebug("信令状态变更：{State}（{Reason}）", state, reason);
        StateChanged?.Invoke(this, new TransportStateChangedEventArgs(state, reason));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}
