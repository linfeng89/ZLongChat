using Microsoft.Extensions.Logging;
using ZLongChat.Communication.Abstractions;
using ZLongChat.Contracts;

namespace ZLongChat.PC.Services;

/// <summary>前端会话状态（供 UI 显示，与底层通信状态解耦）</summary>
public enum ChatSessionStatus
{
    /// <summary>未登录</summary>
    Offline,

    /// <summary>已登录，信令在线</summary>
    Online,

    /// <summary>正在建立 P2P</summary>
    Connecting,

    /// <summary>P2P 直连可用</summary>
    P2PConnected,

    /// <summary>P2P 不可用，走服务端转发</summary>
    RelayOnly,

    /// <summary>P2P 多次重连仍失败，已停止自动重连（仍可用服务端转发）</summary>
    DirectConnectFailed
}

/// <summary>
/// 前端会话编排：把「登录」「建连」「发消息」这些业务流程串起来。
///
/// 它只依赖通信抽象（<see cref="ISignalingService"/> / <see cref="IMessageTransport"/> / <see cref="IChatApiClient"/>），
/// 完全不知道底层是 SignalR 还是 WebRTC。MainWindow 因此只剩纯 UI 代码。
/// </summary>
public sealed class ChatSession : IAsyncDisposable
{
    private readonly ClientConfiguration _configuration;
    private readonly ISignalingService _signaling;
    private readonly IMessageTransport _transport;
    private readonly IMessageTransport _p2pTransport;
    private readonly IChatApiClient _api;
    private readonly ILogger<ChatSession>? _logger;

    private CancellationTokenSource? _reconnectCts;
    private string _userId = string.Empty;
    private string _targetUserId = string.Empty;
    private bool _isInitiator;
    private bool _disposed;

    /// <summary>连续重连次数，连接成功或手动发起时清零</summary>
    private int _reconnectAttempt;

    /// <summary>是否已有一次重连在等待执行（防止 ICE failed 与看门狗重复计数）</summary>
    private bool _reconnectScheduled;

    /// <summary>建连看门狗的代号，用于作废过期的看门狗</summary>
    private int _watchdogGeneration;

    public ChatSession(
        ClientConfiguration configuration,
        ISignalingService signaling,
        IMessageTransport transport,
        IMessageTransport p2pTransport,
        IChatApiClient api,
        ILogger<ChatSession>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _signaling = signaling ?? throw new ArgumentNullException(nameof(signaling));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _p2pTransport = p2pTransport ?? throw new ArgumentNullException(nameof(p2pTransport));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _logger = logger;

        _transport.MessageReceived += OnMessageReceived;
        _transport.Notice += OnNotice;
        _p2pTransport.StateChanged += OnP2pStateChanged;
        _signaling.SdpReceived += OnSdpReceived;
    }

    /// <summary>过程提示（进度、降级原因等），UI 直接追加到消息列表</summary>
    public event EventHandler<string>? Notice;

    /// <summary>会话状态变化</summary>
    public event EventHandler<ChatSessionStatus>? StatusChanged;

    /// <summary>收到对端消息</summary>
    public event EventHandler<ChatMessage>? MessageReceived;

    /// <summary>本端用户ID</summary>
    public string UserId => _userId;

    /// <summary>对端用户ID</summary>
    public string TargetUserId => _targetUserId;

    /// <summary>是否已登录信令在线</summary>
    public bool IsLoggedIn => _signaling.State == TransportState.Connected;

    // ------------------------------------------------------------ 业务流程

    /// <summary>登录：调用 REST 接口换取令牌，再连上信令服务</summary>
    public async Task LoginAsync(string userId, string password = "", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("请输入你的用户ID", nameof(userId));
        }

        _userId = userId.Trim();
        RaiseStatus(ChatSessionStatus.Offline);

        // 1) 先拉取服务端下发的配置：成功则覆盖本地的 ICE / 超时 / 重连间隔等。
        //    必须在连接信令之前完成，因为 Hub 路径也可能由服务端指定。
        if (_configuration.UseServerProvidedConfig)
        {
            await TryApplyRemoteConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }

        // 2) REST 登录。接口不可用不阻塞信令连接，保证可用性与改造前一致。
        try
        {
            var login = await _api.LoginAsync(_userId, password, cancellationToken).ConfigureAwait(false);
            RaiseNotice($"🔑 登录接口返回令牌：{login.Token}");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "REST 登录接口调用失败");
            RaiseNotice($"⚠️  登录接口调用失败（{ex.Message}），继续连接信令服务");
        }

        // 3) 连接信令服务
        await _signaling.ConnectAsync(_userId, cancellationToken).ConfigureAwait(false);

        RaiseStatus(ChatSessionStatus.Online);
        RaiseNotice("✅ 登录成功，已连接到调度服务器");
    }

    /// <summary>
    /// 拉取服务端配置并覆盖本地值。
    /// 失败不抛异常：客户端仍用本地配置继续跑（服务端不可用时体验不降级）。
    /// </summary>
    private async Task TryApplyRemoteConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var remote = await _api.GetClientConfigurationAsync(cancellationToken).ConfigureAwait(false);
            _configuration.ApplyRemote(remote);

            // WebRTC 通道需要感知新的 ICE 列表
            if (_p2pTransport is IIceServerConfigurable configurable)
            {
                configurable.UpdateIceServers(_configuration.IceServers);
            }

            RaiseNotice($"⚙️  已应用服务端配置（ICE 服务器 {_configuration.IceServers.Count} 个）");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "获取服务端配置失败，改用本地配置");
            RaiseNotice($"⚠️  获取服务端配置失败（{ex.Message}），使用本地配置");
        }
    }

    /// <summary>向对端发起 P2P 连接（失败会自动降级为服务端转发）</summary>
    public async Task ConnectToPeerAsync(string targetUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            throw new ArgumentException("请输入对方用户ID", nameof(targetUserId));
        }

        if (!IsLoggedIn)
        {
            throw new InvalidOperationException("请先登录");
        }

        _targetUserId = targetUserId.Trim();
        _isInitiator = true;

        // 手动发起视为一次全新尝试：清掉退避计数与尚未执行的重连
        CancelPendingReconnect();
        _reconnectAttempt = 0;

        RaiseNotice($"🔗 正在发起和 {_targetUserId} 的P2P连接...");
        RaiseStatus(ChatSessionStatus.Connecting);

        await _transport.ConnectAsync(new TransportConnectContext(_targetUserId, AsInitiator: true), cancellationToken)
            .ConfigureAwait(false);

        // 手动发起同样要有超时兜底，否则对端不在时会一直卡在"连接中"
        StartConnectWatchdog();
    }

    /// <summary>发送文本消息（复合通道：优先 P2P，超时自动转服务端）</summary>
    public async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var peer = _transport.PeerUserId ?? _targetUserId;
        if (string.IsNullOrEmpty(peer))
        {
            throw new InvalidOperationException("还没有确定聊天对象，请先发起连接");
        }

        var message = ChatMessage.CreateText(_userId, peer, text.Trim());
        await _transport.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>查询某用户的节点信息（供 P2P 发现流程使用）</summary>
    public Task<NodeInfo> QueryNodeAsync(string userId, CancellationToken cancellationToken = default)
        => _api.GetNodeAsync(userId, cancellationToken);

    /// <summary>上报本节点地址（供 P2P 发现流程使用）</summary>
    public Task ReportNodeAsync(string ip, int port, CancellationToken cancellationToken = default)
        => _api.ReportNodeAsync(new NodeReportRequest { UserId = _userId, Ip = ip, Port = port }, cancellationToken);

    // ------------------------------------------------------------ 事件转发

    private void OnMessageReceived(object? sender, TransportMessageEventArgs e)
        => MessageReceived?.Invoke(this, e.Message);

    private void OnNotice(object? sender, string message)
        => Notice?.Invoke(this, message);

    private void OnSdpReceived(object? sender, SdpSignal signal)
    {
        // 被动方：对方主动连进来，记录对端并切换为「等待方」角色
        if (signal.IsOffer)
        {
            _targetUserId = signal.FromUserId;
            _isInitiator = false;
            RaiseStatus(ChatSessionStatus.Connecting);
        }
    }

    private void OnP2pStateChanged(object? sender, TransportStateChangedEventArgs e)
    {
        switch (e.State)
        {
            case TransportState.Connected:
                // 连上了就把重连计数清零，下次断开重新计数；同时作废看门狗
                _reconnectAttempt = 0;
                _watchdogGeneration++;
                RaiseStatus(ChatSessionStatus.P2PConnected);
                break;

            case TransportState.Failed:
                // P2P 断了不影响发消息：复合通道会自动走服务端转发
                RaiseStatus(ChatSessionStatus.RelayOnly);
                ScheduleReconnect();
                break;
        }
    }

    /// <summary>
    /// P2P 建连看门狗。
    ///
    /// 必须有它：当对端已下线时，我方发出的 Offer 拿不到 Answer，
    /// ICE 永远不会进入 failed 状态 —— 没有看门狗，重试循环会静默卡死在这里。
    /// </summary>
    private void StartConnectWatchdog()
    {
        int timeoutSeconds = _configuration.P2PConnectTimeoutSeconds;
        if (timeoutSeconds <= 0)
        {
            return;
        }

        int generation = ++_watchdogGeneration;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)).ConfigureAwait(false);

                // 已被更新的一次尝试取代，或已连上、已释放 → 什么都不做
                if (_disposed || generation != _watchdogGeneration || _p2pTransport.IsAvailable)
                {
                    return;
                }

                RaiseNotice($"⏱️ P2P 建连超时（{timeoutSeconds} 秒内未连通），判定本次尝试失败");
                RaiseStatus(ChatSessionStatus.RelayOnly);
                ScheduleReconnect();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "建连看门狗异常");
            }
        });
    }

    /// <summary>
    /// 自动重连策略：指数退避 + 次数上限。
    ///
    /// 只有主动发起方重连，被动方等待对方重新发起（与改造前一致）。
    /// 达到上限后**停止重连**并明确告知用户，避免「没网 / 对端一直不在」时无限刷屏 ——
    /// 此时消息仍能通过服务端转发，聊天本身不受影响。
    /// </summary>
    private void ScheduleReconnect()
    {
        if (!_isInitiator || string.IsNullOrEmpty(_targetUserId) || !IsLoggedIn)
        {
            RaiseNotice("⏳ 等待对方重新发起连接...");
            return;
        }

        // 已经有一次重连在等待执行：不重复计数（ICE failed 与看门狗可能同时触发）
        if (_reconnectScheduled)
        {
            return;
        }

        int maxAttempts = _configuration.MaxReconnectAttempts;

        // 上限已到：停止重连，转为可操作的提示
        if (maxAttempts > 0 && _reconnectAttempt >= maxAttempts)
        {
            RaiseStatus(ChatSessionStatus.DirectConnectFailed);
            RaiseNotice($"⛔ P2P 直连已连续失败 {_reconnectAttempt} 次，已停止自动重连。");
            RaiseNotice("   · 消息仍会通过服务端转发，聊天不受影响");
            RaiseNotice("   · 请检查网络（对方是否在线 / 防火墙是否放行 / 网卡是否正常）");
            RaiseNotice("   · 修好后点「发起连接」可手动重试");
            return;
        }

        _reconnectAttempt++;
        _reconnectScheduled = true;

        // 指数退避：3s → 6s → 12s …，封顶 MaxReconnectDelaySeconds
        int delaySeconds = (int)Math.Min(
            _configuration.ReconnectDelaySeconds * Math.Pow(2, _reconnectAttempt - 1),
            _configuration.MaxReconnectDelaySeconds);
        int attempt = _reconnectAttempt;

        CancelPendingReconnect();

        var cts = new CancellationTokenSource();
        _reconnectCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                string progress = maxAttempts > 0 ? $"（第 {attempt}/{maxAttempts} 次）" : string.Empty;
                RaiseNotice($"🔄 {delaySeconds} 秒后自动重连{progress}...");

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cts.Token).ConfigureAwait(false);

                if (cts.IsCancellationRequested || _disposed)
                {
                    return;
                }

                _reconnectScheduled = false;

                await _transport.ConnectAsync(
                    new TransportConnectContext(_targetUserId, AsInitiator: true),
                    cts.Token).ConfigureAwait(false);

                // 发起后启动看门狗：连不上（例如对端已下线）也要能判定失败并继续计数
                StartConnectWatchdog();
            }
            catch (OperationCanceledException)
            {
                _reconnectScheduled = false;
            }
            catch (Exception ex)
            {
                _reconnectScheduled = false;
                _logger?.LogWarning(ex, "自动重连失败");
                RaiseNotice($"❌ 自动重连失败：{ex.Message}");

                // 连"发起"都失败，同样算一次失败，继续走退避
                ScheduleReconnect();
            }
        });
    }

    private void CancelPendingReconnect()
    {
        _reconnectScheduled = false;

        var cts = _reconnectCts;
        _reconnectCts = null;

        if (cts is not null)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
            cts.Dispose();
        }
    }

    private void RaiseNotice(string message) => Notice?.Invoke(this, message);

    private void RaiseStatus(ChatSessionStatus status) => StatusChanged?.Invoke(this, status);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _transport.MessageReceived -= OnMessageReceived;
        _transport.Notice -= OnNotice;
        _p2pTransport.StateChanged -= OnP2pStateChanged;
        _signaling.SdpReceived -= OnSdpReceived;

        CancelPendingReconnect();

        try
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "释放通信通道失败");
        }

        try
        {
            await _signaling.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "释放信令连接失败");
        }

        _api.Dispose();
    }
}
