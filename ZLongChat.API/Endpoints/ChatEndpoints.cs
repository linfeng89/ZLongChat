using Microsoft.Extensions.Options;
using ZLongChat.API.Options;
using ZLongChat.API.Services;
using ZLongChat.Contracts;

namespace ZLongChat.API.Endpoints;

/// <summary>用户相关 REST 接口</summary>
public static class UserEndpoints
{
    /// <summary>注册用户接口</summary>
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // 用户登录校验
        app.MapPost(ChatApiRoutes.Login,
            (UserLoginRequest request, IOptions<AuthSettings> auth, ILoggerFactory loggerFactory) =>
            {
                if (string.IsNullOrEmpty(request.UserId))
                {
                    return Results.BadRequest("用户ID不能为空");
                }

                loggerFactory.CreateLogger("UserEndpoints")
                    .LogInformation("用户 {UserId} 登录成功", request.UserId);

                return Results.Ok(new UserLoginResponse
                {
                    // 令牌前缀来自 ZLongChat:Auth:DevTokenPrefix，不再写死在代码里
                    Token = $"{auth.Value.DevTokenPrefix}{request.UserId}",
                    UserId = request.UserId
                });
            });

        return app;
    }
}

/// <summary>节点相关 REST 接口</summary>
public static class NodeEndpoints
{
    /// <summary>注册节点接口</summary>
    public static IEndpointRouteBuilder MapNodeEndpoints(this IEndpointRouteBuilder app)
    {
        // 节点上报
        app.MapPost(ChatApiRoutes.NodeReport,
            (NodeReportRequest request, INodeRegistry nodes, ILoggerFactory loggerFactory) =>
            {
                if (string.IsNullOrEmpty(request.UserId))
                {
                    return Results.BadRequest("用户ID不能为空");
                }

                var address = $"{request.Ip}:{request.Port}";
                nodes.Set(request.UserId, address);

                loggerFactory.CreateLogger("NodeEndpoints")
                    .LogDebug("节点上报：用户{UserId} 地址{Address}", request.UserId, address);

                return Results.Ok(new OperationResult { Success = true });
            });

        // 查询好友在线状态
        app.MapGet(ChatApiRoutes.NodeQueryTemplate, (string userId, INodeRegistry nodes) =>
        {
            return nodes.TryGet(userId, out var address)
                ? Results.Ok(new NodeInfo { IsOnline = true, NodeAddress = address })
                : Results.Ok(new NodeInfo { IsOnline = false, NodeAddress = string.Empty });
        });

        return app;
    }
}
