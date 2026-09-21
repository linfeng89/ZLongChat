using System.Buffers.Binary;

namespace ZLongChat.Contracts;

/// <summary>
/// P2P 二进制帧协议。
///
/// 这一层刻意与「用哪种通道」解耦：WebRTC DataChannel 用它，
/// 以后换成原始 TCP / QUIC / WebSocket 二进制帧也可以直接复用。
///
/// 帧布局：
/// <code>
///   +--------+------------------+-----------------+
///   | Type   | MessageId (可选)  | Payload (可选)   |
///   | 1 byte | 8 bytes 小端      | N bytes         |
///   +--------+------------------+-----------------+
/// </code>
/// 心跳帧只有 Type 一个字节；文本 / ACK / 语音帧带 8 字节消息ID。
/// </summary>
public static class ChatBinaryProtocol
{
    /// <summary>文本消息</summary>
    public const byte TypeText = 0x01;

    /// <summary>心跳包</summary>
    public const byte TypeHeartbeat = 0x02;

    /// <summary>语音帧（预留）</summary>
    public const byte TypeVoice = 0x03;

    /// <summary>ACK 送达确认</summary>
    public const byte TypeAck = 0x04;

    /// <summary>消息ID字段长度</summary>
    public const int MessageIdLength = 8;

    /// <summary>带消息ID的帧头长度：1 字节类型 + 8 字节消息ID</summary>
    public const int HeaderLength = 1 + MessageIdLength;

    /// <summary>心跳帧长度</summary>
    public const int HeartbeatLength = 1;

    /// <summary>编码心跳帧</summary>
    public static byte[] EncodeHeartbeat() => new[] { TypeHeartbeat };

    /// <summary>编码 ACK 帧</summary>
    public static byte[] EncodeAck(long messageId)
    {
        var frame = new byte[HeaderLength];
        frame[0] = TypeAck;
        WriteMessageId(frame.AsSpan(1), messageId);
        return frame;
    }

    /// <summary>编码文本帧</summary>
    public static byte[] EncodeText(long messageId, ReadOnlySpan<byte> payload)
        => EncodeWithHeader(TypeText, messageId, payload);

    /// <summary>编码语音帧</summary>
    public static byte[] EncodeVoice(long messageId, ReadOnlySpan<byte> payload)
        => EncodeWithHeader(TypeVoice, messageId, payload);

    /// <summary>
    /// 解析帧头。返回 false 表示帧太短或格式非法（调用方应丢弃该帧）。
    /// </summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> frame, out byte messageType, out long messageId)
    {
        messageType = 0;
        messageId = 0;

        if (frame.Length < HeartbeatLength)
        {
            return false;
        }

        messageType = frame[0];

        // 心跳帧没有消息ID
        if (messageType == TypeHeartbeat)
        {
            return true;
        }

        if (frame.Length < HeaderLength)
        {
            return false;
        }

        messageId = ReadMessageId(frame[1..]);
        return true;
    }

    /// <summary>取出帧负载（跳过帧头）。心跳帧返回空。</summary>
    public static ReadOnlySpan<byte> ReadPayload(ReadOnlySpan<byte> frame, byte messageType)
    {
        int offset = messageType == TypeHeartbeat ? HeartbeatLength : HeaderLength;
        return frame.Length > offset ? frame[offset..] : ReadOnlySpan<byte>.Empty;
    }

    private static byte[] EncodeWithHeader(byte messageType, long messageId, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[HeaderLength + payload.Length];
        frame[0] = messageType;
        WriteMessageId(frame.AsSpan(1), messageId);
        payload.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }

    private static void WriteMessageId(Span<byte> destination, long value)
        => BinaryPrimitives.WriteInt64LittleEndian(destination, value);

    private static long ReadMessageId(ReadOnlySpan<byte> source)
        => BinaryPrimitives.ReadInt64LittleEndian(source);
}
