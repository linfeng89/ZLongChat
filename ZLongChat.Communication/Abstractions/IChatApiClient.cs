using ZLongChat.Contracts;

namespace ZLongChat.Communication.Abstractions;

/// <summary>
/// 服务端 REST 接口。
/// 把 HTTP 调用从 UI 里彻底抽出来：以后换成 gRPC / GraphQL，
/// 只要再写一个实现，UI 完全不用改。
/// </summary>
public interface IChatApiClient : IDisposable
{
    /// <summary>登录并获取令牌</summary>
    Task<UserLoginResponse> LoginAsync(string userId, string password = "", CancellationToken cancellationToken = default);

    /// <summary>上报本节点直连地址</summary>
    Task ReportNodeAsync(NodeReportRequest request, CancellationToken cancellationToken = default);

    /// <summary>查询某用户是否在线及其节点地址</summary>
    Task<NodeInfo> GetNodeAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取服务端下发的客户端配置（ICE 服务器、超时与重连参数等）。
    /// 把 STUN/TURN 收敛到服务端维护，客户端不必各自配置。
    /// </summary>
    Task<ClientConfiguration> GetClientConfigurationAsync(CancellationToken cancellationToken = default);
}
