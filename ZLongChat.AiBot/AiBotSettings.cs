using ZLongChat.Contracts;

namespace ZLongChat.AiBot;

/// <summary>
/// AI 机器人自身设置（对应 <c>ZLongChat:AiBot</c> 节）。
/// 与后端 / 客户端挂在同一个 <c>ZLongChat</c> 根节下，节名风格保持一致。
/// </summary>
public sealed class AiBotSettings
{
    /// <summary>机器人自己的用户ID（其他人就以这个ID给它发消息）</summary>
    public string UserId { get; set; } = ZLongChatDefaults.AiBotUserId;

    /// <summary>每个会话保留多少条历史消息（控制内存与上下文长度）</summary>
    public int HistoryLimit { get; set; } = ZLongChatDefaults.AiBotHistoryLimit;

    /// <summary>占位助手的流式分片字符数</summary>
    public int ChunkSize { get; set; } = ZLongChatDefaults.AiBotChunkSize;

    /// <summary>占位助手的流式分片间隔（毫秒）</summary>
    public int ChunkDelayMs { get; set; } = ZLongChatDefaults.AiBotChunkDelayMs;

    /// <summary>修正明显不合理的值</summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(UserId))
        {
            UserId = ZLongChatDefaults.AiBotUserId;
        }

        HistoryLimit = HistoryLimit > 0 ? HistoryLimit : ZLongChatDefaults.AiBotHistoryLimit;
        ChunkSize = ChunkSize > 0 ? ChunkSize : ZLongChatDefaults.AiBotChunkSize;
        ChunkDelayMs = ChunkDelayMs >= 0 ? ChunkDelayMs : ZLongChatDefaults.AiBotChunkDelayMs;
    }
}
