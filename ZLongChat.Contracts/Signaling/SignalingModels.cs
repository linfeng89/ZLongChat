using System.Text.Json.Serialization;

namespace ZLongChat.Contracts;

/// <summary>SDP 信令载荷</summary>
public sealed class SdpSignal
{
    /// <summary>发送方用户ID</summary>
    public string FromUserId { get; set; } = string.Empty;

    /// <summary>会话描述文本</summary>
    public string Sdp { get; set; } = string.Empty;

    /// <summary>类型：offer / answer</summary>
    public string SdpType { get; set; } = string.Empty;

    /// <summary>是否为 offer</summary>
    public bool IsOffer => string.Equals(SdpType, SdpTypes.Offer, StringComparison.OrdinalIgnoreCase);
}

/// <summary>SDP 类型常量</summary>
public static class SdpTypes
{
    public const string Offer = "offer";
    public const string Answer = "answer";
}

/// <summary>
/// ICE 候选信令载荷。
/// 属性名保持小写以兼容现有线上 JSON 格式（老客户端仍能互通）。
/// </summary>
public sealed class IceCandidateSignal
{
    /// <summary>候选字符串</summary>
    [JsonPropertyName("candidate")]
    public string Candidate { get; set; } = string.Empty;

    /// <summary>媒体行标识</summary>
    [JsonPropertyName("sdpMid")]
    public string? SdpMid { get; set; }

    /// <summary>媒体行索引</summary>
    [JsonPropertyName("sdpMLineIndex")]
    public int SdpMLineIndex { get; set; }

    /// <summary>发送方用户ID（服务端填充，不参与序列化）</summary>
    [JsonIgnore]
    public string FromUserId { get; set; } = string.Empty;
}
