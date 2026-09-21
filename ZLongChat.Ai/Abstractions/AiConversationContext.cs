using ZLongChat.Contracts;

namespace ZLongChat.Ai.Abstractions;

/// <summary>
/// 一次 AI 调用所需的对话上下文。
/// 把「谁在跟谁说话」和「之前聊过什么」打包，避免接口参数随需求膨胀。
/// </summary>
public sealed record AiConversationContext
{
    /// <summary>会话标识。单聊场景直接用对端用户ID。</summary>
    public required string ConversationId { get; init; }

    /// <summary>发起本次提问的用户ID</summary>
    public required string UserId { get; init; }

    /// <summary>助手自身的用户ID（用于在历史里区分谁说的话）</summary>
    public required string AssistantUserId { get; init; }

    /// <summary>
    /// 对话历史（按时间正序）。最后一条通常是本次要回答的问题。
    /// </summary>
    public IReadOnlyList<ChatMessage> History { get; init; } = Array.Empty<ChatMessage>();

    /// <summary>本次要回答的问题文本（历史中最后一条消息）</summary>
    public string Prompt => History.Count == 0 ? string.Empty : History[^1].GetText();

    /// <summary>历史里是否出现过该用户的消息</summary>
    public bool HasHistory => History.Count > 1;
}
