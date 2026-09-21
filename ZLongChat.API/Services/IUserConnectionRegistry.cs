using System.Collections.Concurrent;
using ZLongChat.Contracts;

namespace ZLongChat.API.Services;

/// <summary>
/// 在线用户 ↔ SignalR 连接 映射。
/// 抽成接口后，将来做多实例部署可以换成 Redis 实现。
/// </summary>
public interface IUserConnectionRegistry
{
    /// <summary>记录/更新某用户的连接</summary>
    void Set(string userId, string connectionId);

    /// <summary>尝试取某用户的连接ID</summary>
    bool TryGet(string userId, out string connectionId);

    /// <summary>移除某用户</summary>
    void Remove(string userId);
}

/// <summary>内存实现</summary>
public sealed class InMemoryUserConnectionRegistry : IUserConnectionRegistry
{
    private readonly ConcurrentDictionary<string, string> _connections = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Set(string userId, string connectionId) => _connections[userId] = connectionId;

    /// <inheritdoc />
    public bool TryGet(string userId, out string connectionId) => _connections.TryGetValue(userId, out connectionId!);

    /// <inheritdoc />
    public void Remove(string userId) => _connections.TryRemove(userId, out _);
}
