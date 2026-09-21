using System.ComponentModel;
using ModelContextProtocol.Server;
using ZLongChat.Contracts.Shudong;
using ZLongChat.Core.Ports;

namespace ZLongChat.Mcp.Tools;

/// <summary>
/// 陪伴会话工具（契约 §4.6 – §4.11）。
/// 会话状态与倒计时全部由服务端裁决，工具只是入口。
/// </summary>
[McpServerToolType]
public sealed class CompanionTools
{
    private readonly ISessionService _sessions;
    private readonly CallContextAccessor _caller;

    public CompanionTools(ISessionService sessions, CallContextAccessor caller)
    {
        _sessions = sessions;
        _caller = caller;
    }

    // ---------------------------------------------------------------- 入口（model 可见）

    [McpServerTool(Name = ShudongTools.OpenSession, ReadOnly = true, OpenWorld = false)]
    [Description("打开当前陪伴会话，开始聊天。")]
    [McpMeta("ui", JsonValue = """{"resourceUri":"ui://shudong/chat.html?v=1","visibility":["model","app"]}""")]
    public async Task<SessionInfo> OpenSessionAsync(
        [Description("会话 ID")] string sessionId,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        return await _sessions.GetAsync(sessionId, context, cancellationToken).ConfigureAwait(false)
               ?? throw new ShudongException(ShudongErrorCodes.NotFound, "会话不存在");
    }

    // ---------------------------------------------------------------- 含正文（仅 app / widget 可调）

    [McpServerTool(Name = ShudongTools.CatchSignal, Idempotent = true, OpenWorld = false)]
    [Description("接住某人并发出第一句话。只能由信号详情面板调用，内容不会进入模型上下文。")]
    [McpMeta("ui", JsonValue = """{"visibility":["app"]}""")]
    public async Task<CatchResult> CatchSignalAsync(
        [Description("信号 ID")] string signalId,
        [Description("第一句话，最多 500 字")] string firstMessage,
        [Description("幂等键")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        return await _sessions.CatchAsync(signalId, firstMessage, idempotencyKey, context, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = ShudongTools.SendMessage, Idempotent = true, OpenWorld = false)]
    [Description("在陪伴会话中发送一条消息。只能由聊天面板调用，内容不会进入模型上下文。")]
    [McpMeta("ui", JsonValue = """{"visibility":["app"]}""")]
    public async Task<SendMessageResult> SendMessageAsync(
        [Description("会话 ID")] string sessionId,
        [Description("消息内容")] string content,
        [Description("幂等键")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        return await _sessions.SendMessageAsync(sessionId, content, idempotencyKey, context, cancellationToken)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- 非敏感动作（model 也可调）

    [McpServerTool(Name = ShudongTools.RequestHuman, Idempotent = true, OpenWorld = false)]
    [Description("在 AI 陪伴中呼叫真人，加入等待队列。")]
    [McpMeta("ui", JsonValue = """{"visibility":["model","app"]}""")]
    public async Task<HumanQueueResult> RequestHumanAsync(
        [Description("会话 ID")] string sessionId,
        [Description("幂等键")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        return await _sessions.RequestHumanAsync(sessionId, idempotencyKey, context, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = ShudongTools.RequestExtension, Idempotent = true, OpenWorld = false)]
    [Description("请求延长 15 分钟陪伴。由倾诉者发起，需要倾听者同意。")]
    [McpMeta("ui", JsonValue = """{"visibility":["model","app"]}""")]
    public async Task<ExtensionResult> RequestExtensionAsync(
        [Description("会话 ID")] string sessionId,
        [Description("幂等键")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        return await _sessions.RequestExtensionAsync(sessionId, idempotencyKey, context, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = ShudongTools.RespondExtension, Idempotent = true, OpenWorld = false)]
    [Description("同意或拒绝对方的延时请求。30 秒未响应视为拒绝，会话继续。")]
    [McpMeta("ui", JsonValue = """{"visibility":["model","app"]}""")]
    public async Task<ExtensionResult> RespondExtensionAsync(
        [Description("会话 ID")] string sessionId,
        [Description("是否同意延长")] bool accept,
        [Description("幂等键")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        return await _sessions.RespondExtensionAsync(sessionId, accept, idempotencyKey, context, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = ShudongTools.EndSession, Idempotent = true, OpenWorld = false)]
    [Description("结束陪伴。进入 10 秒结束缓冲，期间保留举报入口。")]
    [McpMeta("ui", JsonValue = """{"visibility":["model","app"]}""")]
    public async Task<EndSessionResult> EndSessionAsync(
        [Description("会话 ID")] string sessionId,
        [Description("幂等键")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var context = _caller.Current();
        return await _sessions.EndAsync(sessionId, idempotencyKey, context, cancellationToken).ConfigureAwait(false);
    }
}
