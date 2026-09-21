using System.Collections.Concurrent;
using ZLongChat.Contracts.Shudong;

namespace ZLongChat.Core.Ports;

/// <summary>
/// 安全端口：举报与危机资源。
///
/// <b>注意本接口刻意很窄</b>：
/// 产品已把「危机干预」降级为「安全兜底」—— 只展示热线，不宣称识别能力（见 13.5）。
/// 因此这里没有"危机识别"方法，只有"取资源"。
/// </summary>
public interface ISafetyService
{
    /// <summary>提交举报（<b>唯一的内容留存例外</b>，必须返回并展示告知）</summary>
    Task<ReportResult> ReportAsync(string sessionId, ReportReason reason, string? detail, string idempotencyKey, CallContext context, CancellationToken ct = default);

    /// <summary>取危机资源（不依赖登录、不依赖会话，应始终可调用）</summary>
    Task<SupportResources> GetSupportResourcesAsync(string? region, CancellationToken ct = default);

    /// <summary>
    /// 规则匹配明显信号，用于展示热线卡片。返回 true 表示"建议展示"。
    /// <b>不对外宣称识别能力，也不承诺覆盖率。</b>
    /// </summary>
    bool ShouldSurfaceSupport(string content);
}

/// <summary>举报记录（内部实体）</summary>
public sealed class ReportRecord
{
    public required string ReportId { get; init; }
    public required string SessionId { get; init; }
    public required string ReporterUserId { get; init; }
    public required ReportReason Reason { get; init; }
    public string? Detail { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required RetentionState Retention { get; init; }
}

/// <summary>内存实现（骨架用）</summary>
public sealed class InMemorySafetyService : ISafetyService
{
    /// <summary>举报时留存的最近消息条数（产品文档 13.6）</summary>
    public const int RetentionMessageCount = 20;

    /// <summary>举报留存告知文案（产品文档 18.1，必须展示）</summary>
    public const string ReportDisclosure = "举报已提交。本次对话将保存至服务端，用于安全审核。";

    /// <summary>
    /// 规则词表（<b>仅用于展示热线卡片，不构成任何识别承诺</b>）。
    /// 生产环境可扩充，但**不要对外宣传识别率**。
    /// </summary>
    private static readonly string[] SuggestionKeywords =
    [
        "自杀", "不想活", "活不下去", "结束生命", "轻生", "自残", "割腕", "跳楼"
    ];

    private readonly ConcurrentDictionary<string, ReportRecord> _reports = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<ReportResult> ReportAsync(string sessionId, ReportReason reason, string? detail, string idempotencyKey, CallContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ShudongException(ShudongErrorCodes.ValidationError, "会话 ID 不能为空");
        }

        var reportId = $"rep_{Guid.NewGuid():N}"[..16];
        var record = new ReportRecord
        {
            ReportId = reportId,
            SessionId = sessionId,
            ReporterUserId = context.UserId,
            Reason = reason,
            Detail = detail,
            CreatedAt = DateTimeOffset.UtcNow,
            Retention = RetentionState.ReportSaved
        };

        _reports[reportId] = record;

        // 必须把告知返回给调用方展示（契约 §4.12：disclosure 是硬要求）
        return Task.FromResult(new ReportResult(reportId, SessionFrozen: true, RetentionState.ReportSaved, ReportDisclosure));
    }

    /// <inheritdoc />
    public Task<SupportResources> GetSupportResourcesAsync(string? region, CancellationToken ct = default)
    {
        var resources = new List<SupportResource>
        {
            new("全国心理援助热线", "12356", null, "24 小时"),
            new("青少年服务台", "12355", null, "24 小时"),
            new("紧急救助", "110", null, "24 小时"),
            new("急救", "120", null, "24 小时")
        };

        return Task.FromResult(new SupportResources(resources));
    }

    /// <inheritdoc />
    public bool ShouldSurfaceSupport(string content)
        => !string.IsNullOrEmpty(content)
           && SuggestionKeywords.Any(content.Contains);
}
