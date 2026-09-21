using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using System.Collections.Concurrent;
using System.Text;
using ZLongChat.Communication.Abstractions;
using ZLongChat.Contracts;

namespace ZLongChat.Communication.WebRtc;

/// <summary>
/// 基于 WebRTC DataChannel 的 P2P 消息通道。
///
/// 改造前这些逻辑（PeerConnection 生命周期、ICE 缓存、二进制分帧、心跳保活、ACK 超时）
/// 全部散落在 MainWindow 里，且直接操作 UI 控件；现在收敛成一个干净的传输实现。
///
/// 通道可用性由「收到对端心跳」确认，而不是只看 DataChannel 的 readyState
/// —— 这是原实现踩过坑后得出的结论，保持不变。
/// </summary>
public sealed class WebRtcMessageTransport : IMessageTransport, IIceServerConfigurable
{
    private readonly ISignalingService _signaling;
    private readonly ClientConfiguration _configuration;
    private readonly ILogger<WebRtcMessageTransport>? _logger;

    /// <summary>待 ACK 的消息：消息ID → 完成源</summary>
    private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>> _pendingAcks = new();

    /// <summary>提前到达的 ICE 候选（远程描述还没设置好时先缓存）</summary>
    private readonly Queue<IceCandidateSignal> _pendingIceCandidates = new();

    private readonly object _gate = new();

    private RTCPeerConnection? _peerConnection;
    private RTCDataChannel? _dataChannel;
    private System.Timers.Timer? _heartbeatTimer;

    private DateTime _lastHeartbeatTime = DateTime.MinValue;
    private bool _channelConfirmed;
    private bool _remoteDescriptionSet;
    private bool _isOfferer;
    private long _messageIdCounter;
    private bool _disposed;

    /// <summary>本次建连已收集到的 ICE 候选数量，用于区分「本机没网」与「协商失败」</summary>
    private int _gatheredCandidateCount;

    private TransportState _state = TransportState.Disconnected;
    private string _targetUserId = string.Empty;

    public WebRtcMessageTransport(
        ISignalingService signaling,
        ClientConfiguration configuration,
        ILogger<WebRtcMessageTransport>? logger = null)
    {
        _signaling = signaling ?? throw new ArgumentNullException(nameof(signaling));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;

        // 被动方同样要能收到 Offer，所以构造时就订阅信令
        // （改造前是在登录时注册 HubConnection.On("ReceiveSdp")，时机一致）
        _signaling.SdpReceived += OnSdpReceived;
        _signaling.IceCandidateReceived += OnIceCandidateReceived;
    }

    /// <inheritdoc />
    public void UpdateIceServers(IReadOnlyList<IceServerOptions> iceServers)
    {
        ArgumentNullException.ThrowIfNull(iceServers);

        if (iceServers.Count == 0)
        {
            return;
        }

        _configuration.IceServers = iceServers.ToList();

        // InitPeerConnection 每次建连都会重新读取该列表，所以立刻生效
        _logger?.LogInformation(
            "ICE 服务器已更新（{Count} 个）：{Urls}",
            _configuration.IceServers.Count,
            string.Join("、", _configuration.IceServers.Select(server => server.Url)));
    }

    /// <inheritdoc />
    public string Name => TransportNames.P2P;

    /// <inheritdoc />
    public TransportState State => _state;

    /// <inheritdoc />
    public bool IsAvailable => _channelConfirmed && _dataChannel?.readyState == RTCDataChannelState.open;

    /// <inheritdoc />
    public string? PeerUserId => string.IsNullOrEmpty(_targetUserId) ? null : _targetUserId;

    public event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    public event EventHandler<TransportMessageEventArgs>? MessageReceived;

    public event EventHandler<string>? Notice;

    /// <inheritdoc />
    public async Task ConnectAsync(TransportConnectContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResetConnectionState();
        _targetUserId = context.TargetUserId ?? string.Empty;
        _isOfferer = context.AsInitiator;

        if (!_isOfferer)
        {
            // 被动方什么都不用做，等对方 Offer 到达即可
            SetState(TransportState.Connecting, "等待对方发起连接");
            return;
        }

        if (string.IsNullOrEmpty(_targetUserId))
        {
            throw new TransportException("发起连接需要指定对端用户ID");
        }

        SetState(TransportState.Connecting, $"正在连接 {_targetUserId}");

        InitPeerConnection();

        _dataChannel = await _peerConnection!.createDataChannel(_configuration.DataChannelLabel, new RTCDataChannelInit
        {
            ordered = true,
            maxRetransmits = 3,
            negotiated = false,
            id = 0
        }).ConfigureAwait(false);

        AttachDataChannelEvents(_dataChannel);

        var offer = _peerConnection.createOffer();
        await _peerConnection.setLocalDescription(offer).ConfigureAwait(false);

        // 立刻发 SDP，不等 ICE 收集完成 —— Trickle ICE 会自动补发候选
        await _signaling.SendSdpAsync(_targetUserId, offer.sdp, SdpTypes.Offer, cancellationToken).ConfigureAwait(false);
        RaiseNotice($"📨 发送Offer信令给{_targetUserId}");
    }

    /// <inheritdoc />
    public async Task SendAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var channel = _dataChannel;
        if (!IsAvailable || channel is null)
        {
            throw new TransportException($"{Name} 当前不可用（{_state}）");
        }

        long messageId = Interlocked.Increment(ref _messageIdCounter);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingAcks[messageId] = completion;

        byte[] frame = message.Type == ChatMessageType.Voice
            ? ChatBinaryProtocol.EncodeVoice(messageId, message.Content)
            : ChatBinaryProtocol.EncodeText(messageId, message.Content);

        try
        {
            channel.send(frame);
        }
        catch (Exception ex)
        {
            _pendingAcks.TryRemove(messageId, out _);
            throw new TransportException($"P2P 发送失败：{ex.Message}", ex);
        }

        // 等对端 ACK，超时就抛异常，由上层（FallbackMessageTransport）改走服务端
        try
        {
            await completion.Task
                .WaitAsync(TimeSpan.FromMilliseconds(_configuration.AckTimeoutMs), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _pendingAcks.TryRemove(messageId, out _);
            throw new TransportException($"P2P 发送超时（{_configuration.AckTimeoutMs}ms 内未收到ACK）");
        }
        catch (OperationCanceledException)
        {
            _pendingAcks.TryRemove(messageId, out _);
            throw;
        }
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        StopHeartbeat();
        ClosePeerConnection();
        ResetConnectionState();
        SetState(TransportState.Disconnected, "P2P 已断开");
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- 信令处理

    private void OnSdpReceived(object? sender, SdpSignal signal)
    {
        _ = HandleSdpAsync(signal);
    }

    private async Task HandleSdpAsync(SdpSignal signal)
    {
        try
        {
            if (!IsFromTargetPeer(signal.FromUserId))
            {
                return;
            }

            if (signal.IsOffer)
            {
                await HandleOfferAsync(signal).ConfigureAwait(false);
            }
            else
            {
                await HandleAnswerAsync(signal).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理SDP信令失败");
            RaiseNotice($"❌ 处理SDP信令失败：{ex.Message}");
            SetState(TransportState.Failed, ex.Message);
        }
    }

    private async Task HandleOfferAsync(SdpSignal signal)
    {
        _targetUserId = signal.FromUserId;
        _isOfferer = false;

        RaiseNotice($"📩 收到来自 {signal.FromUserId} 的offer信令");
        SetState(TransportState.Connecting, "收到Offer，正在应答");

        lock (_gate)
        {
            _pendingIceCandidates.Clear();
            _remoteDescriptionSet = false;
        }

        InitPeerConnection();

        // 注意：SIPSorcery 的 setRemoteDescription 是同步的，返回 SetDescriptionResultEnum
        var setResult = _peerConnection!.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = signal.Sdp
        });

        if (setResult != SetDescriptionResultEnum.OK)
        {
            // 改造前这里完全没检查返回值，出错也照常往下走；保留原行为，但把问题暴露到日志和界面
            _logger?.LogWarning("设置远程Offer失败：{Result}", setResult);
            RaiseNotice($"⚠️  设置远程Offer返回异常：{setResult}");
        }

        lock (_gate)
        {
            _remoteDescriptionSet = true;
        }

        FlushPendingIceCandidates();

        var answer = _peerConnection.createAnswer();
        await _peerConnection.setLocalDescription(answer).ConfigureAwait(false);

        await _signaling.SendSdpAsync(_targetUserId, answer.sdp, SdpTypes.Answer).ConfigureAwait(false);
        RaiseNotice($"📨 发送Answer信令给{_targetUserId}");
    }

    private Task HandleAnswerAsync(SdpSignal signal)
    {
        var peerConnection = _peerConnection;
        if (peerConnection is null)
        {
            _logger?.LogWarning("收到Answer但PeerConnection尚未初始化，已忽略");
            return Task.CompletedTask;
        }

        var setResult = peerConnection.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.answer,
            sdp = signal.Sdp
        });

        if (setResult != SetDescriptionResultEnum.OK)
        {
            _logger?.LogWarning("设置远程Answer失败：{Result}", setResult);
            RaiseNotice($"⚠️  设置远程Answer返回异常：{setResult}");
        }

        lock (_gate)
        {
            _remoteDescriptionSet = true;
        }

        FlushPendingIceCandidates();
        RaiseNotice("✅ 远程Answer设置完成");
        return Task.CompletedTask;
    }

    private void OnIceCandidateReceived(object? sender, IceCandidateSignal signal)
    {
        try
        {
            if (string.IsNullOrEmpty(signal.Candidate))
            {
                RaiseNotice("⚠️  收到空的ICE候选，跳过");
                return;
            }

            if (!IsFromTargetPeer(signal.FromUserId))
            {
                return;
            }

            bool needsBuffering;
            lock (_gate)
            {
                needsBuffering = _peerConnection is null || !_remoteDescriptionSet;
                if (needsBuffering)
                {
                    _pendingIceCandidates.Enqueue(signal);
                }
            }

            if (needsBuffering)
            {
                RaiseNotice($"⏳ 连接未就绪，缓存ICE候选，当前缓存数：{_pendingIceCandidates.Count}");
                return;
            }

            RaiseNotice("📥 收到ICE候选，正在尝试连接...");
            AddIceCandidateSafe(signal);
        }
        catch (Exception ex)
        {
            RaiseNotice($"❌ 处理ICE候选出错：{ex.Message}");
        }
    }

    private void FlushPendingIceCandidates()
    {
        int flushed = 0;

        while (true)
        {
            IceCandidateSignal? next = null;
            lock (_gate)
            {
                if (_pendingIceCandidates.Count > 0)
                {
                    next = _pendingIceCandidates.Dequeue();
                }
            }

            if (next is null)
            {
                break;
            }

            AddIceCandidateSafe(next);
            flushed++;
        }

        RaiseNotice($"✅ 远程描述设置完成，补加 {flushed} 个缓存候选");
    }

    private void AddIceCandidateSafe(IceCandidateSignal signal)
    {
        try
        {
            var peerConnection = _peerConnection;
            if (peerConnection is null)
            {
                return;
            }

            peerConnection.addIceCandidate(new RTCIceCandidateInit
            {
                candidate = signal.Candidate,
                // sdpMid 为空时给默认值，SIPSorcery 要求不能为 null
                sdpMid = signal.SdpMid ?? "0",
                sdpMLineIndex = (ushort)signal.SdpMLineIndex
            });

            RaiseNotice("✅ 添加ICE候选成功");
        }
        catch (Exception ex)
        {
            RaiseNotice($"⚠️  添加ICE候选失败：{ex.Message}，跳过该候选");
        }
    }

    private bool IsFromTargetPeer(string fromUserId)
        => string.IsNullOrEmpty(_targetUserId) || string.Equals(_targetUserId, fromUserId, StringComparison.Ordinal);

    // ---------------------------------------------------------------- PeerConnection

    private void InitPeerConnection()
    {
        ClosePeerConnection();

        // 每次重新协商都重新统计候选数
        _gatheredCandidateCount = 0;

        if (_configuration.IceServers.Count == 0)
        {
            _logger?.LogWarning("ICE 服务器列表为空（配置项 {Section}:IceServers 未配置），P2P 打洞很可能失败",
                ZLongChatSections.Client);
            RaiseNotice($"⚠️  未配置 ICE 服务器（{ZLongChatSections.Client}:IceServers），P2P 打洞很可能失败");
        }

        var config = new RTCConfiguration
        {
            iceServers = _configuration.IceServers.Select(server => new RTCIceServer
            {
                urls = server.Url,
                username = server.Username,
                credential = server.Credential
            }).ToList(),
            iceTransportPolicy = RTCIceTransportPolicy.all,
            X_GatherTimeoutMs = _configuration.GatherTimeoutMs
        };

        var peerConnection = new RTCPeerConnection(config);

        peerConnection.onicecandidate += OnIceCandidate;
        peerConnection.oniceconnectionstatechange += OnIceConnectionStateChanged;
        peerConnection.ondatachannel += OnDataChannel;

        _peerConnection = peerConnection;
    }

    private void ClosePeerConnection()
    {
        var channel = _dataChannel;
        _dataChannel = null;

        if (channel is not null)
        {
            try { channel.close(); }
            catch (Exception ex) { _logger?.LogDebug(ex, "关闭 DataChannel 失败"); }
        }

        var peerConnection = _peerConnection;
        _peerConnection = null;

        if (peerConnection is not null)
        {
            try { peerConnection.Close("释放旧连接"); }
            catch (Exception ex) { _logger?.LogDebug(ex, "关闭 PeerConnection 失败"); }

            try { peerConnection.Dispose(); }
            catch (Exception ex) { _logger?.LogDebug(ex, "释放 PeerConnection 失败"); }
        }

        lock (_gate)
        {
            _pendingIceCandidates.Clear();
            _remoteDescriptionSet = false;
        }

        foreach (var pending in _pendingAcks)
        {
            pending.Value.TrySetResult(false);
        }
        _pendingAcks.Clear();
    }

    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        if (candidate is null || string.IsNullOrEmpty(candidate.candidate))
        {
            RaiseNotice("✅ ICE候选收集完成，等待连接建立...");
            return;
        }

        // IPv6 候选直接跳过：不转发也不尝试，省掉十几秒无效等待
        if (_configuration.SkipIPv6Candidates && !string.IsNullOrEmpty(candidate.address) && candidate.address.Contains(':'))
        {
            return;
        }

        _gatheredCandidateCount++;
        _ = SendIceCandidateAsync(candidate);
    }

    private async Task SendIceCandidateAsync(RTCIceCandidate candidate)
    {
        try
        {
            if (_signaling.State != TransportState.Connected || string.IsNullOrEmpty(_targetUserId))
            {
                return;
            }

            await _signaling.SendIceCandidateAsync(_targetUserId, new IceCandidateSignal
            {
                Candidate = candidate.candidate,
                SdpMid = candidate.sdpMid ?? "0",
                SdpMLineIndex = candidate.sdpMLineIndex
            }).ConfigureAwait(false);

            RaiseNotice($"📡 发送ICE候选：{candidate.type} - {candidate.address}:{candidate.port}");
        }
        catch (Exception ex)
        {
            RaiseNotice($"❌ 发送ICE候选失败：{ex.Message}");
        }
    }

    private void OnIceConnectionStateChanged(RTCIceConnectionState state)
    {
        _logger?.LogDebug("ICE 连接状态：{State}", state);

        switch (state)
        {
            case RTCIceConnectionState.connected:
                RaiseNotice("✅ P2P连接建立成功！");
                // 不急着标记可用，等收到第一个心跳才认为真的活着
                break;

            case RTCIceConnectionState.failed:
            case RTCIceConnectionState.disconnected:
            case RTCIceConnectionState.closed:
                _channelConfirmed = false;
                StopHeartbeat();

                // 区分两种失败：本机压根没收集到候选（多半是没网/没有可用网卡），
                // 与候选收集正常但对端打不通。前者给出明确可操作的提示，而不是让人干等重连。
                if (_gatheredCandidateCount == 0)
                {
                    RaiseNotice("❌ 未能收集到任何 ICE 候选：本机可能没有可用的网络接口"
                                + "（未联网 / 网线未插 / 网卡被禁用）。P2P 直连不可用，消息仍可通过服务端转发。");
                }

                SetState(TransportState.Failed, _gatheredCandidateCount == 0
                    ? "ICE 失败：本机未收集到任何网络候选"
                    : $"ICE 状态：{state}");
                break;
        }
    }

    private void OnDataChannel(RTCDataChannel channel)
    {
        // 被动方拿到对方创建的 DataChannel
        _dataChannel = channel;
        AttachDataChannelEvents(channel);
    }

    private void AttachDataChannelEvents(RTCDataChannel channel)
    {
        channel.onopen += () =>
        {
            RaiseNotice("📶 DataChannel通道已打开");
            SetState(TransportState.Connected, "DataChannel 已打开");
            StartHeartbeat();
        };

        channel.onmessage += OnDataChannelMessage;

        channel.onerror += error => RaiseNotice($"❌ 通道错误：{error}");
    }

    // ---------------------------------------------------------------- 数据帧

    private void OnDataChannelMessage(RTCDataChannel channel, DataChannelPayloadProtocols protocol, byte[] data)
    {
        if (data is null || data.Length == 0)
        {
            return;
        }

        if (!ChatBinaryProtocol.TryReadHeader(data, out byte messageType, out long messageId))
        {
            return;
        }

        switch (messageType)
        {
            case ChatBinaryProtocol.TypeHeartbeat:
                // 有来有回保活效果更好
                SendFrame(ChatBinaryProtocol.EncodeHeartbeat());
                _lastHeartbeatTime = DateTime.Now;
                // 收到心跳才说明连接真的活着
                _channelConfirmed = true;
                break;

            case ChatBinaryProtocol.TypeText:
                var text = Encoding.UTF8.GetString(ChatBinaryProtocol.ReadPayload(data, messageType));
                var message = ChatMessage.CreateText(
                    _targetUserId,
                    _signaling.LocalUserId ?? string.Empty,
                    text);

                MessageReceived?.Invoke(this, new TransportMessageEventArgs(message, Name));

                // 立即回 ACK
                SendFrame(ChatBinaryProtocol.EncodeAck(messageId));
                break;

            case ChatBinaryProtocol.TypeAck:
                if (_pendingAcks.TryRemove(messageId, out var completion))
                {
                    completion.TrySetResult(true);
                }
                break;

            case ChatBinaryProtocol.TypeVoice:
                RaiseNotice($"🎤 收到语音帧，长度：{ChatBinaryProtocol.ReadPayload(data, messageType).Length}字节");
                break;
        }
    }

    private void SendFrame(byte[] frame)
    {
        try
        {
            var channel = _dataChannel;
            if (channel?.readyState == RTCDataChannelState.open)
            {
                channel.send(frame);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "发送P2P帧失败");
        }
    }

    // ---------------------------------------------------------------- 心跳保活

    private void StartHeartbeat()
    {
        StopHeartbeat();

        var timer = new System.Timers.Timer(_configuration.HeartbeatIntervalMs) { AutoReset = true };
        timer.Elapsed += (_, _) =>
        {
            try
            {
                SendFrame(ChatBinaryProtocol.EncodeHeartbeat());

                if (_channelConfirmed &&
                    (DateTime.Now - _lastHeartbeatTime).TotalSeconds > _configuration.HeartbeatTimeoutSeconds)
                {
                    _channelConfirmed = false;
                    RaiseNotice("💔 心跳超时，判定连接已断开");
                    StopHeartbeat();
                    ClosePeerConnection();
                    SetState(TransportState.Failed, "心跳超时");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "心跳发送失败");
            }
        };

        _heartbeatTimer = timer;
        timer.Start();
        RaiseNotice("❤️ 心跳保活已启动");
    }

    private void StopHeartbeat()
    {
        var timer = _heartbeatTimer;
        _heartbeatTimer = null;

        if (timer is not null)
        {
            try
            {
                timer.Stop();
                timer.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "停止心跳定时器失败");
            }
        }
    }

    // ---------------------------------------------------------------- 内部工具

    private void ResetConnectionState()
    {
        lock (_gate)
        {
            _pendingIceCandidates.Clear();
            _remoteDescriptionSet = false;
        }

        _channelConfirmed = false;
        _lastHeartbeatTime = DateTime.MinValue;
        _pendingAcks.Clear();
    }

    private void SetState(TransportState state, string? reason = null)
    {
        _state = state;
        StateChanged?.Invoke(this, new TransportStateChangedEventArgs(state, reason));
    }

    private void RaiseNotice(string message)
    {
        _logger?.LogDebug("{Message}", message);
        Notice?.Invoke(this, message);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        _signaling.SdpReceived -= OnSdpReceived;
        _signaling.IceCandidateReceived -= OnIceCandidateReceived;

        StopHeartbeat();
        ClosePeerConnection();

        return ValueTask.CompletedTask;
    }
}
