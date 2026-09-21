namespace ZLongChat.Contracts;

/// <summary>登录请求</summary>
public sealed class UserLoginRequest
{
    /// <summary>用户ID</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>密码（开发阶段未校验）</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>登录响应</summary>
public sealed class UserLoginResponse
{
    /// <summary>访问令牌</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>用户ID</summary>
    public string UserId { get; set; } = string.Empty;
}

/// <summary>节点上报请求：告诉调度服务自己的直连地址</summary>
public sealed class NodeReportRequest
{
    /// <summary>用户ID</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>节点公网/局域网 IP</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>节点端口</summary>
    public int Port { get; set; }
}

/// <summary>节点查询结果</summary>
public sealed class NodeInfo
{
    /// <summary>是否在线</summary>
    public bool IsOnline { get; set; }

    /// <summary>节点地址（ip:port），不在线时为空串</summary>
    public string NodeAddress { get; set; } = string.Empty;
}

/// <summary>通用操作结果（无返回值接口用）</summary>
public sealed class OperationResult
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>失败原因</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }
}
