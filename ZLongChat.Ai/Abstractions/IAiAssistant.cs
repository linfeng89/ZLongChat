using ZLongChat.Contracts;

namespace ZLongChat.Ai.Abstractions;

/// <summary>
/// AI 助手能力抽象。
///
/// 与 <c>IMessageTransport</c>（通信抽象）平级：通信层负责「消息怎么送达」，
/// 这一层只负责「收到消息后怎么生成回复」，两者互不知情。
///
/// 之所以返回 <see cref="IAsyncEnumerable{T}"/> 而不是完整字符串：
/// 大模型的流式输出是标准形态，保留分片能直接对接
/// <c>Microsoft.Extensions.AI</c> 的 <c>IChatClient.GetStreamingResponseAsync</c>，
/// 将来要做「打字机效果」也不用改接口。
/// </summary>
public interface IAiAssistant
{
    /// <summary>助手名称，用于日志与界面展示</summary>
    string Name { get; }

    /// <summary>
    /// 根据对话上下文流式生成回复。
    /// </summary>
    /// <param name="context">对话上下文（含历史消息）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>回复的文本分片</returns>
    IAsyncEnumerable<string> StreamReplyAsync(AiConversationContext context, CancellationToken cancellationToken = default);
}
