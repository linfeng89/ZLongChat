using System.Text.Json.Serialization;

namespace ZLongChat.Contracts.Shudong;

// ============================================================
// 树洞泡泡 · MCP 边界模型
//
// 这些类型是 MCP 工具的参数与返回形状，因此放在契约层：
//   · Agent（宿主）看到的是它们
//   · Widget 渲染的是它们
//   · 服务端实现的是它们
// 定义见 docs/MCP工具契约.md
// ============================================================

/// <summary>信号模式（对应状态 2 / 状态 3）</summary>
public enum SignalMode
{
    /// <summary>状态 2「被听到」：进池可看，<b>不可接住</b></summary>
    Broadcast = 1,

    /// <summary>状态 3「需要陪伴」：进池可看，<b>可接住</b></summary>
    Companion = 2
}

/// <summary>陪伴者类型（身份透明的数据基础，widget 必须据此显示标识）</summary>
public enum CompanionType
{
    /// <summary>尚无陪伴者</summary>
    None = 0,

    /// <summary>AI 缓冲（必须显示「AI」标识）</summary>
    Ai = 1,

    /// <summary>真人倾听者</summary>
    Human = 2
}

/// <summary>信号状态</summary>
public enum SignalStatus
{
    Active = 1,
    Seen = 2,
    Catching = 3,
    Caught = 4,
    InSession = 5,
    Expired = 6
}

/// <summary>会话状态</summary>
public enum SessionState
{
    /// <summary>已接住，等待倾诉者进入（3 分钟窗口）</summary>
    Waiting = 1,

    /// <summary>陪伴中</summary>
    Active = 2,

    /// <summary>结束缓冲（10 秒，禁止发消息，保留举报入口）</summary>
    Buffer = 3,

    /// <summary>已结束</summary>
    Ended = 4,

    /// <summary>已释放（未进入 / 超时）</summary>
    Released = 5
}

/// <summary>留存状态（见产品文档 13.3：唯一例外是举报）</summary>
public enum RetentionState
{
    /// <summary>常规：聊完即焚</summary>
    Ephemeral = 0,

    /// <summary>举报触发：保存该次会话最后 20 条消息</summary>
    ReportSaved = 1
}

/// <summary>举报原因</summary>
public enum ReportReason
{
    Harassment = 1,
    Sexual = 2,
    Fraud = 3,
    SelfHarm = 4,
    Violence = 5,
    Other = 6
}

// ---------------------------------------------------------------- 请求

/// <summary>发布倾诉（状态 2 / 3）</summary>
public sealed record PublishSignalCommand(
    string Content,
    IReadOnlyList<string> Tags,
    SignalMode Mode,
    string IdempotencyKey);

/// <summary>浏览树洞池</summary>
public sealed record BrowseSignalsQuery(
    IReadOnlyList<string>? Tags,
    string? Cursor,
    int Limit);

// ---------------------------------------------------------------- 返回

/// <summary>列表项摘要（<b>不含正文</b>——正文只在 widget 内取）</summary>
public sealed record SignalSummary(
    string SignalId,
    IReadOnlyList<string> Tags,
    string Preview,
    int CharCount,
    int WaitingSeconds,
    int ListenerCount,
    SignalMode Mode);

/// <summary>浏览结果（分页）</summary>
public sealed record SignalBrowsePage(
    IReadOnlyList<SignalSummary> Items,
    string? NextCursor);

/// <summary>信号元信息（<b>不含正文</b>）</summary>
public sealed record SignalDetail(
    string SignalId,
    IReadOnlyList<string> Tags,
    int CharCount,
    int WaitingSeconds,
    bool CanCatch);

/// <summary>
/// 安全兜底提醒。**不是识别结论**，只是"顺手递一张写着热线的纸条"。
///
/// 之所以叫 Hint 而不是 Detection：叫法决定义务。
/// 一旦返回体里出现 <c>detected</c>，就等于宣称了识别能力，之后每次漏报都是我们的错。
/// </summary>
public sealed record SupportHint(string Message, string ResourceUri);

/// <summary>发布结果</summary>
public sealed record PublishResult(
    string SignalId,
    SignalStatus Status,
    DateTimeOffset ExpiresAt,
    RetentionState Retention,
    SupportHint? Support = null);

/// <summary>我作为倾诉者的状态</summary>
public sealed record SeekerState(
    string SignalId,
    SignalStatus Status,
    int WaitingSeconds,
    CompanionType CompanionType,
    string? SessionId);

/// <summary>我作为倾听者的状态</summary>
public sealed record ListenerState(
    string SessionId,
    string PeerAnonymousId,
    int RemainingSeconds,
    int ExtensionUsed);

/// <summary>剩余配额</summary>
public sealed record QuotaState(int SeekRemaining, int ListenRemaining);

/// <summary>我的完整状态</summary>
public sealed record MyState(
    SeekerState? AsSeeker,
    ListenerState? AsListener,
    QuotaState Quota);

/// <summary>接住结果</summary>
public sealed record CatchResult(
    string SessionId,
    SessionState State,
    DateTimeOffset AcceptExpiresAt,
    string RoomResourceUri);

/// <summary>会话信息</summary>
public sealed record SessionInfo(
    string SessionId,
    SessionState State,
    CompanionType CompanionType,
    int RemainingSeconds,
    int ExtensionUsed,
    int ExtensionLimit);

/// <summary>发消息结果</summary>
public sealed record SendMessageResult(string MessageId, DateTimeOffset SentAt);

/// <summary>延时请求/响应结果</summary>
public sealed record ExtensionResult(string State, int RemainingSeconds);

/// <summary>呼叫真人结果</summary>
public sealed record HumanQueueResult(bool Queued, int QueuePosition);

/// <summary>结束会话结果</summary>
public sealed record EndSessionResult(SessionState State, int BufferSeconds, bool ReportWindowOpen);

/// <summary>
/// 举报结果。
/// <para>
/// <c>Disclosure</c> 是<b>必须展示给用户的留存告知</b>（产品文档 13.6），widget 不得忽略。
/// </para>
/// </summary>
public sealed record ReportResult(
    string ReportId,
    bool SessionFrozen,
    RetentionState Retention,
    string Disclosure);

/// <summary>撤回结果</summary>
public sealed record CancelSignalResult(bool QuotaRefunded);

/// <summary>危机资源单项</summary>
public sealed record SupportResource(string Name, string Phone, string? Url, string? Hours);

/// <summary>危机资源</summary>
public sealed record SupportResources(IReadOnlyList<SupportResource> Resources);

// ---------------------------------------------------------------- 错误

/// <summary>
/// 统一错误。MCP 层捕获后转成结构化错误返回（见契约 §8.1 错误码表）。
/// </summary>
public sealed class ShudongException : Exception
{
    public ShudongException(string code, string message, string? disclosure = null)
        : base(message)
    {
        Code = code;
        Disclosure = disclosure;
    }

    /// <summary>错误码，如 <c>MODE_NOT_CATCHABLE</c></summary>
    public string Code { get; }

    /// <summary>需要展示给用户的告知（如举报留存告知）</summary>
    public string? Disclosure { get; }
}

/// <summary>
/// 工具失败时返回给调用方的结构化错误（放在 <c>CallToolResult.structuredContent</c>）。
///
/// 为什么不让调用方去解析错误文案：widget 需要按 <see cref="Code"/> 分支
/// （例如 <c>MODE_NOT_CATCHABLE</c> 要换成"这条只是想被听到"的提示），
/// 文案随时会改，错误码不会。
/// </summary>
public sealed record ShudongErrorPayload(string Code, string Message, string? Disclosure);

/// <summary>错误码常量（与契约 §8.1 一致）</summary>
public static class ShudongErrorCodes
{
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string QuotaExceeded = "QUOTA_EXCEEDED";
    public const string RateLimited = "RATE_LIMITED";
    public const string SignalAlreadyCaught = "SIGNAL_ALREADY_CAUGHT";
    public const string SignalExpired = "SIGNAL_EXPIRED";
    public const string SignalInSession = "SIGNAL_IN_SESSION";
    public const string SessionEnded = "SESSION_ENDED";
    public const string SessionInBuffer = "SESSION_IN_BUFFER";
    public const string SelfCatchForbidden = "SELF_CATCH_FORBIDDEN";
    public const string ModeNotCatchable = "MODE_NOT_CATCHABLE";
    public const string AlreadyInListenerSession = "ALREADY_IN_LISTENER_SESSION";
    public const string ExtensionLimitReached = "EXTENSION_LIMIT_REACHED";
    public const string NoPendingRequest = "NO_PENDING_REQUEST";
    public const string NotYourRole = "NOT_YOUR_ROLE";
    public const string ContentRejected = "CONTENT_REJECTED";
    public const string ValidationError = "VALIDATION_ERROR";
}

/// <summary>UI 资源 URI（契约 §5.1）</summary>
public static class ShudongUiUris
{
    public const string Publish = "ui://shudong/publish.html?v=1";
    public const string Signals = "ui://shudong/signals.html?v=1";
    public const string SignalDetail = "ui://shudong/signal-detail.html?v=1";
    public const string Chat = "ui://shudong/chat.html?v=1";
    public const string State = "ui://shudong/state.html?v=1";
    public const string Crisis = "ui://shudong/crisis.html?v=1";
}

/// <summary>工具名常量（避免前后端写错字符串）</summary>
public static class ShudongTools
{
    // 入口（model 可见，带 widget）
    public const string OpenPublish = "open_publish";
    public const string GetActiveSignals = "get_active_signals";
    public const string GetSignalDetail = "get_signal_detail";
    public const string OpenSession = "open_session";
    public const string GetMyState = "get_my_state";
    public const string GetSupportResources = "get_support_resources";

    // 含正文 → app-only
    public const string PublishSignal = "publish_signal";
    public const string CatchSignal = "catch_signal";
    public const string SendMessage = "send_message";
    public const string GetSignalDetailContent = "get_signal_detail_content";

    // 非敏感动作
    public const string RequestHuman = "request_human";
    public const string RequestExtension = "request_extension";
    public const string RespondExtension = "respond_extension";
    public const string EndSession = "end_session";
    public const string Report = "report";
    public const string CancelSignal = "cancel_signal";
}
