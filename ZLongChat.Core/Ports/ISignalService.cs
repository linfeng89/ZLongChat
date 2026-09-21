using System.Collections.Concurrent;
using ZLongChat.Contracts.Shudong;

namespace ZLongChat.Core.Ports;

/// <summary>
/// 信号端口：发布 / 浏览 / 详情 / 我的状态 / 撤回。
///
/// <b>注意</b>：浏览与详情<b>不含正文</b>，正文只能经 <see cref="GetContentAsync"/> 获取（app-only 工具调用）。
/// 这是"敏感内容不进 LLM"的实现基础（契约 §6）。
/// </summary>
public interface ISignalService
{
    /// <summary>发布信号（仅状态 2 / 3；状态 1 完全端侧，不经过服务端）</summary>
    Task<PublishResult> PublishAsync(PublishSignalCommand command, CallContext context, CancellationToken ct = default);

    /// <summary>浏览树洞池（分页，仅摘要）</summary>
    Task<SignalBrowsePage> BrowseAsync(BrowseSignalsQuery query, CallContext context, CancellationToken ct = default);

    /// <summary>取信号元信息（不含正文）</summary>
    Task<SignalDetail?> GetDetailAsync(string signalId, CallContext context, CancellationToken ct = default);

    /// <summary>取信号正文（<b>仅允许 app-only 工具调用</b>）</summary>
    Task<string?> GetContentAsync(string signalId, CallContext context, CancellationToken ct = default);

    /// <summary>我作为倾诉者 / 倾听者的状态</summary>
    Task<MyState> GetMyStateAsync(CallContext context, CancellationToken ct = default);

    /// <summary>撤回我的信号（仅未被接住时）</summary>
    Task<CancelSignalResult> CancelAsync(string signalId, CallContext context, CancellationToken ct = default);
}

/// <summary>
/// 信号<b>原始记录</b>读取端口。
///
/// 会话层裁决「这条能不能被接住」时必须看到原始记录（模式、归属、状态），
/// 但 MCP 工具层<b>不应该</b>拿到它 —— 原始记录里含正文，
/// 一旦出现在工具签名上，就等于给"正文进 LLM"开了后门。
/// 所以拆成独立接口，只注入给需要它的会话服务。
/// </summary>
public interface ISignalLookup
{
    /// <summary>按 ID 取原始记录；不存在返回 <c>null</c></summary>
    SignalRecord? Find(string signalId);
}

/// <summary>信号记录（内部实体）</summary>
public sealed class SignalRecord
{
    public required string SignalId { get; init; }
    public required string OwnerUserId { get; init; }
    public required string Content { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required SignalMode Mode { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }

    public SignalStatus Status { get; set; } = SignalStatus.Active;
    public int ListenerCount { get; set; }
    public string? CaughtByUserId { get; set; }
    public string? SessionId { get; set; }
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// 内存实现（骨架用）。真实实现换 Redis / PostgreSQL，接口不变。
/// </summary>
public sealed class InMemorySignalService : ISignalService, ISignalLookup
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);
    private const int PreviewLength = 40;
    private const int MaxContentLength = 1000;

    private readonly ConcurrentDictionary<string, SignalRecord> _signals = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _idempotency = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<PublishResult> PublishAsync(PublishSignalCommand command, CallContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Content))
        {
            throw new ShudongException(ShudongErrorCodes.ValidationError, "内容不能为空");
        }

        if (command.Content.Length > MaxContentLength)
        {
            throw new ShudongException(ShudongErrorCodes.ValidationError, $"内容超过 {MaxContentLength} 字上限");
        }

        // 幂等：同 key 直接返回首次结果
        if (!string.IsNullOrEmpty(command.IdempotencyKey) &&
            _idempotency.TryGetValue(Key(context, command.IdempotencyKey), out var existingId) &&
            _signals.TryGetValue(existingId, out var existing))
        {
            return Task.FromResult(new PublishResult(existing.SignalId, existing.Status, existing.ExpiresAt, RetentionState.Ephemeral));
        }

        var now = DateTimeOffset.UtcNow;
        var record = new SignalRecord
        {
            SignalId = $"sig_{Guid.NewGuid():N}"[..16],
            OwnerUserId = context.UserId,
            Content = command.Content,
            Tags = command.Tags ?? Array.Empty<string>(),
            Mode = command.Mode,
            CreatedAt = now,
            ExpiresAt = now.Add(DefaultTtl),
            IdempotencyKey = command.IdempotencyKey
        };

        _signals[record.SignalId] = record;

        if (!string.IsNullOrEmpty(command.IdempotencyKey))
        {
            _idempotency[Key(context, command.IdempotencyKey)] = record.SignalId;
        }

        return Task.FromResult(new PublishResult(record.SignalId, record.Status, record.ExpiresAt, RetentionState.Ephemeral));
    }

    /// <inheritdoc />
    public Task<SignalBrowsePage> BrowseAsync(BrowseSignalsQuery query, CallContext context, CancellationToken ct = default)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 10 : query.Limit, 1, 20);
        var now = DateTimeOffset.UtcNow;

        var candidates = _signals.Values
            .Where(s => s.Status == SignalStatus.Active || s.Status == SignalStatus.Seen)
            .Where(s => s.ExpiresAt > now)
            .Where(s => s.OwnerUserId != context.UserId)                       // 不展示自己的
            .Where(s => query.Tags is null || query.Tags.Count == 0 || s.Tags.Any(query.Tags.Contains))
            .OrderByDescending(s => s.CreatedAt)                               // 确定性排序（契约 §6.5）
            .ToList();

        // 游标 = 上一页最后一条的 SignalId
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            var index = candidates.FindIndex(s => s.SignalId == query.Cursor);
            if (index >= 0)
            {
                candidates = candidates.Skip(index + 1).ToList();
            }
        }

        var page = candidates.Take(limit).Select(ToSummary).ToList();
        var nextCursor = candidates.Count > limit ? page[^1].SignalId : null;

        return Task.FromResult(new SignalBrowsePage(page, nextCursor));
    }

    /// <inheritdoc />
    public Task<SignalDetail?> GetDetailAsync(string signalId, CallContext context, CancellationToken ct = default)
    {
        if (!_signals.TryGetValue(signalId, out var signal))
        {
            return Task.FromResult<SignalDetail?>(null);
        }

        var detail = new SignalDetail(
            signal.SignalId,
            signal.Tags,
            signal.Content.Length,
            WaitingSeconds(signal),
            // 硬规则：broadcast 不可接住（契约 §1.3）
            CanCatch: signal.Mode == SignalMode.Companion
                      && signal.OwnerUserId != context.UserId
                      && (signal.Status == SignalStatus.Active || signal.Status == SignalStatus.Seen));

        return Task.FromResult<SignalDetail?>(detail);
    }

    /// <inheritdoc />
    public Task<string?> GetContentAsync(string signalId, CallContext context, CancellationToken ct = default)
        => Task.FromResult(_signals.TryGetValue(signalId, out var s) ? s.Content : null);

    /// <inheritdoc />
    public Task<MyState> GetMyStateAsync(CallContext context, CancellationToken ct = default)
    {
        var mine = _signals.Values
            .Where(s => s.OwnerUserId == context.UserId)
            .Where(s => s.Status is SignalStatus.Active or SignalStatus.Seen or SignalStatus.Catching or SignalStatus.Caught or SignalStatus.InSession)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();

        SeekerState? seeker = mine is null
            ? null
            : new SeekerState(
                mine.SignalId,
                mine.Status,
                WaitingSeconds(mine),
                mine.CaughtByUserId is null ? CompanionType.None : CompanionType.Human,
                mine.SessionId);

        // 倾听者侧状态由会话服务提供，骨架先返回 null
        var quota = new QuotaState(SeekRemaining: 3, ListenRemaining: 5);
        return Task.FromResult(new MyState(seeker, null, quota));
    }

    /// <inheritdoc />
    public Task<CancelSignalResult> CancelAsync(string signalId, CallContext context, CancellationToken ct = default)
    {
        if (!_signals.TryGetValue(signalId, out var signal) || signal.OwnerUserId != context.UserId)
        {
            throw new ShudongException(ShudongErrorCodes.NotFound, "信号不存在");
        }

        if (signal.Status is SignalStatus.Caught or SignalStatus.InSession)
        {
            throw new ShudongException(ShudongErrorCodes.SignalInSession, "已进入会话，无法撤回");
        }

        signal.Status = SignalStatus.Expired;
        return Task.FromResult(new CancelSignalResult(QuotaRefunded: true));
    }

    /// <inheritdoc />
    public SignalRecord? Find(string signalId)
        => _signals.TryGetValue(signalId, out var s) ? s : null;

    private static string Key(CallContext context, string idempotencyKey) => $"{context.UserId}:{idempotencyKey}";

    private static int WaitingSeconds(SignalRecord signal)
        => (int)Math.Max(0, (DateTimeOffset.UtcNow - signal.CreatedAt).TotalSeconds);

    private static SignalSummary ToSummary(SignalRecord signal) => new(
        signal.SignalId,
        signal.Tags,
        signal.Content.Length <= PreviewLength ? signal.Content : signal.Content[..PreviewLength] + "…",
        signal.Content.Length,
        WaitingSeconds(signal),
        signal.ListenerCount,
        signal.Mode);
}
