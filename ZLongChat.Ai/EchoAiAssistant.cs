using System.Runtime.CompilerServices;
using ZLongChat.Ai.Abstractions;

namespace ZLongChat.Ai;

/// <summary>
/// 占位实现：不调用任何大模型，把收到的话原样回显。
///
/// 作用是在**不依赖任何云服务、不需要 API Key** 的前提下把整条链路跑通：
/// 客户端 → Hub → AiBot → 回复 → Hub → 客户端。
/// 链路验证无误后，把宿主里的 <c>new EchoAiAssistant()</c> 换成真实实现即可。
/// </summary>
public sealed class EchoAiAssistant : IAiAssistant
{
    private readonly int _chunkSize;
    private readonly TimeSpan _chunkDelay;

    /// <param name="chunkSize">每个分片的字符数（用于模拟流式输出）</param>
    /// <param name="chunkDelay">分片之间的延迟（用于模拟生成耗时）</param>
    public EchoAiAssistant(int chunkSize = 6, TimeSpan? chunkDelay = null)
    {
        _chunkSize = Math.Max(1, chunkSize);
        _chunkDelay = chunkDelay ?? TimeSpan.FromMilliseconds(60);
    }

    /// <inheritdoc />
    public string Name => "Echo 占位助手（未接入真实大模型）";

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamReplyAsync(
        AiConversationContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 用轮次编号回显，便于肉眼确认「多轮历史」确实被带上了
        int turn = Math.Max(1, (context.History.Count + 1) / 2);
        string reply =
            $"收到你的第 {turn} 轮消息：「{context.Prompt}」。" +
            $"我是 {context.AssistantUserId} 的占位助手，还没有接入真实大模型。" +
            "把宿主程序里的 EchoAiAssistant 换成接了 IChatClient 的实现，就能得到真实回答。";

        for (int offset = 0; offset < reply.Length; offset += _chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int length = Math.Min(_chunkSize, reply.Length - offset);
            yield return reply.Substring(offset, length);

            if (_chunkDelay > TimeSpan.Zero)
            {
                await Task.Delay(_chunkDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
