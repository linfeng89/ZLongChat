using ZLongChat.Contracts;

namespace ZLongChat.Communication.Abstractions;

/// <summary>
/// 可热更新 ICE 服务器配置的通道。
///
/// 单独抽一个窄接口而不是塞进 <see cref="IMessageTransport"/>：
/// 只有 WebRTC 这类需要 ICE 的通道才关心这件事，
/// 上层用 <c>is IIceServerConfigurable</c> 判断即可，不污染主抽象。
///
/// 典型用途：客户端登录后从服务端拉取 ICE 配置，再应用到已建好的通道上。
/// </summary>
public interface IIceServerConfigurable
{
    /// <summary>用新的 ICE 服务器列表覆盖当前配置（对后续建连生效）</summary>
    void UpdateIceServers(IReadOnlyList<IceServerOptions> iceServers);
}
