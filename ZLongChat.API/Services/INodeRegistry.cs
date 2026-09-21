using System.Collections.Concurrent;

namespace ZLongChat.API.Services;

/// <summary>
/// 节点地址表：用户上报的直连地址（ip:port）。
///
/// ⚠️ 改造前这张表和「在线连接表」共用同一个 ConcurrentDictionary，
/// 结果 REST 接口写入 ip:port、Hub 写入 connectionId，互相覆盖 —— 现在彻底分开。
/// </summary>
public interface INodeRegistry
{
    /// <summary>记录节点地址</summary>
    void Set(string userId, string address);

    /// <summary>查询节点地址</summary>
    bool TryGet(string userId, out string address);
}

/// <summary>内存实现</summary>
public sealed class InMemoryNodeRegistry : INodeRegistry
{
    private readonly ConcurrentDictionary<string, string> _nodes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Set(string userId, string address) => _nodes[userId] = address;

    /// <inheritdoc />
    public bool TryGet(string userId, out string address) => _nodes.TryGetValue(userId, out address!);
}
