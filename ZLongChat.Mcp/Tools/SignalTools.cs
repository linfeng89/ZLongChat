using System.ComponentModel;
using ModelContextProtocol.Server;
using ZLongChat.Contracts.Shudong;
using ZLongChat.Core;
using ZLongChat.Core.Ports;

namespace ZLongChat.Mcp.Tools;

/// <summary>
/// 信号与倾诉工具（契约 §4.1 / §4.2 / §4.3 / §4.4 / §4.5 / §4.13）。
///
/// <b>隐私分级</b>：含正文的工具标 <c>_meta.ui.visibility = ["app"]</c>，
/// agent 的工具列表里看不到，只能由 widget 调用（契约 §6）。
/// </summary>
[McpServerToolType]
public sealed class SignalTools
{
    /// <summary>
    /// 兜底卡片文案。刻意<b>不出现"我们检测到"</b>这类措辞 —— 见 <see cref="SupportHint"/> 的说明。
    /// </summary>
    private const string SupportHintMessage = "如果你现在很难受，这里有一些随时可以打的电话。你不需要一个人扛。";

    private readonly ISignalService _signals;
    private readonly ISafetyService _safety;
    private readonly CallContextAccessor _caller;

    public SignalTools(ISignalService signals, ISafetyService safety, CallContextAccessor caller)
    {
        _signals = signals;
        _safety = safety;
        _caller = caller;
    }

    // ---------------------------------------------------------------- 入口（model 可见）

    [McpServerTool(Name = ShudongTools.OpenPublish, ReadOnly = true, OpenWorld = false)]
    [Description("打开倾诉发布面板，让用户自己写下想说的话。当用户想找个地方说说时使用。")]
    [McpMeta("ui", JsonValue = """{"resourceUri":"ui://shudong/publish.html?v=1","visibility":["model","app"]}""")]
    public async Task<MyState> OpenPublishAsync(CancellationToken cancellationToken)
    {
        // 只返回配额，不发布 —— 正文必须由用户在面板内输入（契约 §4.1）
        var context = _caller.Current();
        return await _signals.GetMyStateAsync(context, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = ShudongTools.GetActiveSignals, ReadOnly = true, OpenWorld = false)]
    [Description("浏览树洞池：看看有谁正在等待被听见。只返回摘要，不含正文。")]
    [McpMeta("ui", JsonValue = """{"resourceUri":"ui://shudong/signals.html?v=1","visibility":["model","app"]}""")]
    public async Task<SignalBrowsePage> GetActiveSignalsAsync(
        [Description("按标签筛选，可留空")] string[]? tags = null,
        [Description("分页游标，首页留空")] string? cursor = null,
        [Description("每页条数，1–20，默认 10")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var context = _caller.Current();
        var query = new BrowseSignalsQuery(tags, cursor, limit);
        return await _signals.BrowseAsync(query, context, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = ShudongTools.GetSignalDetail, ReadOnly = true, OpenWorld = false)]
    [Description("打开某条信号的详情。只返回元信息，正文由面板自行获取。")]
    [McpMeta("ui", JsonValue = """{"resourceUri":"ui://shudong/signal-detail.html?v=1","visibility":["model","app"]}""")]
    public async Task<SignalDetail> GetSignalDetailAsync(
        [Description("信号 ID")] string signalId,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        return await _signals.GetDetailAsync(signalId, context, cancellationToken).ConfigureAwait(false)
               ?? throw new ShudongException(ShudongErrorCodes.NotFound, "信号不存在");
    }

    [McpServerTool(Name = ShudongTools.GetMyState, ReadOnly = true, OpenWorld = false)]
    [Description("查看我当前的状态：我发出的信号、我正在陪伴的会话、剩余次数。")]
    [McpMeta("ui", JsonValue = """{"resourceUri":"ui://shudong/state.html?v=1","visibility":["model","app"]}""")]
    public async Task<MyState> GetMyStateAsync(CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        return await _signals.GetMyStateAsync(context, cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- 含正文（仅 app / widget 可调）

    [McpServerTool(Name = ShudongTools.PublishSignal, Idempotent = true, OpenWorld = false)]
    [Description("发布一条倾诉信号。只能由发布面板调用，内容不会进入模型上下文。")]
    [McpMeta("ui", JsonValue = """{"visibility":["app"]}""")]
    public async Task<PublishResult> PublishSignalAsync(
        [Description("倾诉内容，最多 1000 字")] string content,
        [Description("标签，最多 3 个")] string[]? tags,
        [Description("broadcast=只希望被听到（不可接住）；companion=需要陪伴（可接住）")]
        string mode,
        [Description("幂等键，客户端生成的 UUID")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        var command = new PublishSignalCommand(content, tags ?? [], ParseMode(mode), idempotencyKey);
        var result = await _signals.PublishAsync(command, context, cancellationToken).ConfigureAwait(false);

        // 安全兜底：内容进池时"顺手看一眼"有没有明显信号，有就递一张热线卡片。
        // 注意这里是**规则匹配**，不是语义识别，也不承诺覆盖率（见 docs/审核与危机识别设计.md §2）。
        // 判断放在工具层：这是"要不要多塞一张卡片"的交互决策，不是领域状态变更。
        return _safety.ShouldSurfaceSupport(content)
            ? result with { Support = new SupportHint(SupportHintMessage, ShudongUiUris.Crisis) }
            : result;
    }

    [McpServerTool(Name = ShudongTools.GetSignalDetailContent, ReadOnly = true, OpenWorld = false)]
    [Description("读取某条信号的正文。只能由详情面板调用，正文不会进入模型上下文。")]
    [McpMeta("ui", JsonValue = """{"visibility":["app"]}""")]
    public async Task<SignalContent> GetSignalDetailContentAsync(
        [Description("信号 ID")] string signalId,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        var content = await _signals.GetContentAsync(signalId, context, cancellationToken).ConfigureAwait(false)
                      ?? throw new ShudongException(ShudongErrorCodes.NotFound, "信号不存在");

        return new SignalContent(signalId, content);
    }

    [McpServerTool(Name = ShudongTools.CancelSignal, Idempotent = true, OpenWorld = false)]
    [Description("撤回我发出的信号。仅在还没有人接住时可用。")]
    [McpMeta("ui", JsonValue = """{"visibility":["model","app"]}""")]
    public async Task<CancelSignalResult> CancelSignalAsync(
        [Description("信号 ID")] string signalId,
        [Description("幂等键")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        return await _signals.CancelAsync(signalId, context, cancellationToken).ConfigureAwait(false);
    }

    private static SignalMode ParseMode(string mode) => mode?.ToLowerInvariant() switch
    {
        "broadcast" => SignalMode.Broadcast,
        "companion" => SignalMode.Companion,
        _ => throw new ShudongException(ShudongErrorCodes.ValidationError,
                "mode 只能是 broadcast（只希望被听到）或 companion（需要陪伴）")
    };
}

/// <summary>信号正文（app-only 返回）</summary>
public sealed record SignalContent(string SignalId, string Content);
