using System.Text;

namespace ZLongChat.Contracts;

/// <summary>
/// 消息业务类型。与「用哪种通信方式发出去」无关。
/// </summary>
public enum ChatMessageType
{
    /// <summary>文本消息</summary>
    Text = 1,

    /// <summary>语音帧（预留）</summary>
    Voice = 2
}

/// <summary>
/// 聊天消息。服务端转发、离线队列、P2P 通道三处共用同一个模型，
/// 避免以前「服务端发 ChatMessage、客户端按 byte[] 收」这类两端不一致的 bug。
/// </summary>
public sealed class ChatMessage
{
    /// <summary>发送方用户ID</summary>
    public string FromUserId { get; set; } = string.Empty;

    /// <summary>接收方用户ID</summary>
    public string ToUserId { get; set; } = string.Empty;

    /// <summary>消息类型</summary>
    public ChatMessageType Type { get; set; } = ChatMessageType.Text;

    /// <summary>消息负载（文本消息为 UTF-8 字节）</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>发送时间</summary>
    public DateTime SendTime { get; set; } = DateTime.Now;

    /// <summary>创建一个 UTF-8 文本消息</summary>
    public static ChatMessage CreateText(string fromUserId, string toUserId, string text) => new()
    {
        FromUserId = fromUserId,
        ToUserId = toUserId,
        Type = ChatMessageType.Text,
        Content = Encoding.UTF8.GetBytes(text),
        SendTime = DateTime.Now
    };

    /// <summary>把负载按 UTF-8 解码为文本</summary>
    public string GetText() => Encoding.UTF8.GetString(Content);

    /// <summary>创建当前消息的浅拷贝，用于填充传输层补充的字段（如接收方）</summary>
    public ChatMessage Clone() => new()
    {
        FromUserId = FromUserId,
        ToUserId = ToUserId,
        Type = Type,
        Content = Content,
        SendTime = SendTime
    };
}
