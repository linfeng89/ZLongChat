using Microsoft.AspNetCore.SignalR;
using ZLongChat.Contracts;

namespace ZLongChat.API.Hubs;

/// <summary>
/// 服务端 → 客户端的强类型调用接口。
/// 方法名通过 <see cref="HubMethodNameAttribute"/> 锁定到 <see cref="ChatHubMethods"/> 常量，
/// 保证服务端改名时客户端编译期就能发现。
/// </summary>
public interface IChatHubClient
{
    /// <summary>把 SDP 推给对端</summary>
    [HubMethodName(ChatHubMethods.ReceiveSdp)]
    Task ReceiveSdp(string fromUserId, string sdp, string sdpType);

    /// <summary>把 ICE 候选推给对端</summary>
    [HubMethodName(ChatHubMethods.ReceiveIceCandidate)]
    Task ReceiveIceCandidate(string fromUserId, string candidateJson);

    /// <summary>把聊天消息推给对端</summary>
    [HubMethodName(ChatHubMethods.ReceiveMessage)]
    Task ReceiveMessage(ChatMessage message);

    /// <summary>批量推送离线消息</summary>
    [HubMethodName(ChatHubMethods.ReceiveOfflineMessages)]
    Task ReceiveOfflineMessages(List<ChatMessage> messages);
}
