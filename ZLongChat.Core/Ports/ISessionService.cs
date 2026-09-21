using System.Collections.Concurrent;
using ZLongChat.Contracts.Shudong;

namespace ZLongChat.Core.Ports;

/// <summary>
/// 会话端口：接住 → 临时陪伴 → 延时 → 结束。
/// <b>所有状态与倒计时由服务端裁决</b>（契约 §1.2 原则 3）。
/// </summary>
public interface ISessionService
{
    /// <summary>接住某人并发出第一句话</summary>
    Task<CatchResult> CatchAsync(string signalId, string firstMessage, string idempotencyKey, CallContext context, CancellationToken ct = default);

    /// <summary>取会话信息</summary>
    Task<SessionInfo?> GetAsync(string sessionId, CallContext context, CancellationToken ct = default);

    /// <summary>陪伴中发消息</summary>
    Task<SendMessageResult> SendMessageAsync(string sessionId, string content, string idempotencyKey, CallContext context, CancellationToken ct = default);

    /// <summary>AI 缓冲中呼叫真人</summary>
    Task<HumanQueueResult> RequestHumanAsync(string sessionId, string idempotencyKey, CallContext context, CancellationToken ct = default);

    /// <summary>倾诉者发起延时（15 分钟）</summary>
    Task<ExtensionResult> RequestExtensionAsync(string sessionId, string idempotencyKey, CallContext context, CancellationToken ct = default);

    /// <summary>倾听者响应延时（30 秒未响应视为拒绝）</summary>
    Task<ExtensionResult> RespondExtensionAsync(string sessionId, bool accept, string idempotencyKey, CallContext context, CancellationToken ct = default);

    /// <summary>结束陪伴（进入 10 秒结束缓冲）</summary>
    Task<EndSessionResult> EndAsync(string sessionId, string idempotencyKey, CallContext context, CancellationToken ct = default);
}

/// <summary>会话记录（内部实体）</summary>
public sealed class SessionRecord
{
    public required string SessionId { get; init; }
    public required string SignalId { get; init; }
    public required string SeekerUserId { get; init; }
    public required string ListenerUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset AcceptExpiresAt { get; init; }

    public SessionState State { get; set; } = SessionState.Waiting;
    public CompanionType CompanionType { get; set; } = CompanionType.Human;
    public int RemainingSeconds { get; set; } = 30 * 60;
    public int ExtensionUsed { get; set; }
    public bool HumanRequested { get; set; }
    public DateTimeOffset? ExtensionRequestedAt { get; set; }
}

/// <summary>内存实现（骨架用）</summary>
public sealed class InMemorySessionService : ISessionService
{
    /// <summary>等待倾诉者进入的时间窗（产品文档 12.2：3 分钟）</summary>
    public static readonly TimeSpan AcceptWindow = TimeSpan.FromMinutes(3);

    /// <summary>陪伴时长</summary>
    public const int DefaultCompanionSeconds = 30 * 60;

    /// <summary>结束缓冲（产品文档 12.2：10 秒）</summary>
    public const int BufferSeconds = 10;

    /// <summary>延时次数上限与单次时长</summary>
    public const int ExtensionLimit = 3;
    public const int ExtensionSeconds = 15 * 60;

    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly ISignalLookup _signals;

    public InMemorySessionService(ISignalLookup signals) => _signals = signals;

    /// <inheritdoc />
    public Task<CatchResult> CatchAsync(string signalId, string firstMessage, string idempotencyKey, CallContext context, CancellationToken ct = default)
    {
        var signal = _signals.Find(signalId)
            ?? throw new ShudongException(ShudongErrorCodes.NotFound, "信号不存在");

        // ★ 硬规则：broadcast 不提供接住（契约 §1.3）
        if (signal.Mode != SignalMode.Companion)
        {
            throw new ShudongException(ShudongErrorCodes.ModeNotCatchable,
                "这条信号只希望被听到，不提供接住");
        }

        if (signal.OwnerUserId == context.UserId)
        {
            throw new ShudongException(ShudongErrorCodes.SelfCatchForbidden, "不能接住自己的信号");
        }

        if (signal.Status is SignalStatus.Caught or SignalStatus.InSession)
        {
            throw new ShudongException(ShudongErrorCodes.SignalAlreadyCaught, "该信号已被接住");
        }

        if (signal.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new ShudongException(ShudongErrorCodes.SignalExpired, "该信号已过期");
        }

        var now = DateTimeOffset.UtcNow;
        var session = new SessionRecord
        {
            SessionId = $"sess_{Guid.NewGuid():N}"[..16],
            SignalId = signal.SignalId,
            SeekerUserId = signal.OwnerUserId,
            ListenerUserId = context.UserId,
            CreatedAt = now,
            AcceptExpiresAt = now.Add(AcceptWindow),
            RemainingSeconds = DefaultCompanionSeconds
        };

        _sessions[session.SessionId] = session;

        signal.Status = SignalStatus.Caught;
        signal.CaughtByUserId = context.UserId;
        signal.SessionId = session.SessionId;

        return Task.FromResult(new CatchResult(
            session.SessionId, session.State, session.AcceptExpiresAt, ShudongUiUris.Chat));
    }

    /// <inheritdoc />
    public Task<SessionInfo?> GetAsync(string sessionId, CallContext context, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<SessionInfo?>(null);
        }

        var info = new SessionInfo(
            session.SessionId, session.State, session.CompanionType,
            session.RemainingSeconds, session.ExtensionUsed, ExtensionLimit);

        return Task.FromResult<SessionInfo?>(info);
    }

    /// <inheritdoc />
    public Task<SendMessageResult> SendMessageAsync(string sessionId, string content, string idempotencyKey, CallContext context, CancellationToken ct = default)
    {
        var session = Require(sessionId);

        if (session.State == SessionState.Buffer)
        {
            throw new ShudongException(ShudongErrorCodes.SessionInBuffer, "对话已进入结束缓冲，无法发送");
        }

        if (session.State is SessionState.Ended or SessionState.Released)
        {
            throw new ShudongException(ShudongErrorCodes.SessionEnded, "会话已结束");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ShudongException(ShudongErrorCodes.ValidationError, "内容不能为空");
        }

        // 倾诉者首次发消息即视为「已进入」，会话激活
        if (session.State == SessionState.Waiting && context.UserId == session.SeekerUserId)
        {
            session.State = SessionState.Active;
        }

        return Task.FromResult(new SendMessageResult($"msg_{Guid.NewGuid():N}"[..16], DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public Task<HumanQueueResult> RequestHumanAsync(string sessionId, string idempotencyKey, CallContext context, CancellationToken ct = default)
    {
        var session = Require(sessionId);
        session.HumanRequested = true;
        return Task.FromResult(new HumanQueueResult(Queued: true, QueuePosition: 1));
    }

    /// <inheritdoc />
    public Task<ExtensionResult> RequestExtensionAsync(string sessionId, string idempotencyKey, CallContext context, CancellationToken ct = default)
    {
        var session = Require(sessionId);

        if (context.UserId != session.SeekerUserId)
        {
            throw new ShudongException(ShudongErrorCodes.NotYourRole, "只有倾诉者可以发起延时");
        }

        if (session.ExtensionUsed >= ExtensionLimit)
        {
            throw new ShudongException(ShudongErrorCodes.ExtensionLimitReached, "延时次数已用完");
        }

        session.ExtensionRequestedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(new ExtensionResult("pending", 30));
    }

    /// <inheritdoc />
    public Task<ExtensionResult> RespondExtensionAsync(string sessionId, bool accept, string idempotencyKey, CallContext context, CancellationToken ct = default)
    {
        var session = Require(sessionId);

        if (context.UserId != session.ListenerUserId)
        {
            throw new ShudongException(ShudongErrorCodes.NotYourRole, "只有倾听者可以响应延时");
        }

        if (session.ExtensionRequestedAt is null)
        {
            throw new ShudongException(ShudongErrorCodes.NoPendingRequest, "没有待响应的延时请求");
        }

        session.ExtensionRequestedAt = null;

        if (accept)
        {
            session.RemainingSeconds += ExtensionSeconds;
            session.ExtensionUsed++;   // 只有同意才扣次数
            return Task.FromResult(new ExtensionResult("accepted", session.RemainingSeconds));
        }

        return Task.FromResult(new ExtensionResult("declined", session.RemainingSeconds));
    }

    /// <inheritdoc />
    public Task<EndSessionResult> EndAsync(string sessionId, string idempotencyKey, CallContext context, CancellationToken ct = default)
    {
        var session = Require(sessionId);
        session.State = SessionState.Buffer;   // 10 秒结束缓冲，保留举报入口

        return Task.FromResult(new EndSessionResult(SessionState.Buffer, BufferSeconds, ReportWindowOpen: true));
    }

    private SessionRecord Require(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s)
            ? s
            : throw new ShudongException(ShudongErrorCodes.NotFound, "会话不存在");
}
