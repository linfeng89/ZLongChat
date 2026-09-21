using System.ComponentModel;
using ModelContextProtocol.Server;
using ZLongChat.Contracts.Shudong;
using ZLongChat.Core.Ports;

namespace ZLongChat.Mcp.Tools;

/// <summary>
/// 安全工具（契约 §4.12 / §4.14）。
///
/// <b>刻意很窄</b>：产品已把「危机干预」降级为「安全兜底」——
/// 只提供"取热线资源"，<b>不提供任何"识别危机"的工具</b>（见产品文档 13.5）。
///
/// 这不是能力缺失，而是设计选择：不宣称识别能力，就不产生持续履行的义务。
/// </summary>
[McpServerToolType]
public sealed class SafetyTools
{
    private readonly ISafetyService _safety;
    private readonly CallContextAccessor _caller;

    public SafetyTools(ISafetyService safety, CallContextAccessor caller)
    {
        _safety = safety;
        _caller = caller;
    }

    [McpServerTool(Name = ShudongTools.GetSupportResources, ReadOnly = true, OpenWorld = false)]
    [Description("获取心理援助热线与紧急求助资源。任何情况下都应能调用，不需要登录。")]
    [McpMeta("ui", JsonValue = """{"resourceUri":"ui://shudong/crisis.html?v=1","visibility":["model","app"]}""")]
    public Task<SupportResources> GetSupportResourcesAsync(
        [Description("地区，可留空")] string? region = null,
        CancellationToken cancellationToken = default)
        => _safety.GetSupportResourcesAsync(region, cancellationToken);

    [McpServerTool(Name = ShudongTools.Report, Idempotent = true, OpenWorld = false)]
    [Description("举报本次对话。提交后房间冻结，本次对话将保存至服务端用于审核。")]
    [McpMeta("ui", JsonValue = """{"visibility":["model","app"]}""")]
    public async Task<ReportResult> ReportAsync(
        [Description("会话 ID")] string sessionId,
        [Description("原因：harassment / sexual / fraud / self_harm / violence / other")] string reason,
        [Description("补充说明，可留空")] string? detail,
        [Description("幂等键")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        var parsed = ParseReason(reason);

        // 返回值里带 Disclosure，widget **必须展示**（契约 §4.12）
        return await _safety.ReportAsync(sessionId, parsed, detail, idempotencyKey, context, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ReportReason ParseReason(string reason) => reason?.ToLowerInvariant() switch
    {
        "harassment" => ReportReason.Harassment,
        "sexual" => ReportReason.Sexual,
        "fraud" => ReportReason.Fraud,
        "self_harm" => ReportReason.SelfHarm,
        "violence" => ReportReason.Violence,
        "other" => ReportReason.Other,
        _ => throw new ShudongException(ShudongErrorCodes.ValidationError, "举报原因不合法")
    };
}
