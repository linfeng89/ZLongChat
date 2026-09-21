using System.Collections.Concurrent;
using ZLongChat.Contracts;

namespace ZLongChat.API.Services;

/// <summary>
/// 离线消息队列。抽成接口后可以换成 Redis / 数据库持久化。
/// </summary>
public interface IOfflineMessageStore
{
    /// <summary>把消息加入某用户的离线队列</summary>
    void Enqueue(string userId, ChatMessage message);

    /// <summary>取出并清空某用户的离线消息</summary>
    IReadOnlyList<ChatMessage> Drain(string userId);
}

/// <summary>内存实现（进程重启即丢失，与改造前行为一致）</summary>
public sealed class InMemoryOfflineMessageStore : IOfflineMessageStore
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ChatMessage>> _queues = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Enqueue(string userId, ChatMessage message)
    {
        var queue = _queues.GetOrAdd(userId, _ => new ConcurrentQueue<ChatMessage>());
        queue.Enqueue(message);
    }

    /// <inheritdoc />
    public IReadOnlyList<ChatMessage> Drain(string userId)
    {
        if (!_queues.TryRemove(userId, out var queue))
        {
            return Array.Empty<ChatMessage>();
        }

        return queue.ToArray();
    }
}
