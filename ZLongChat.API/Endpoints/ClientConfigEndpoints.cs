using Microsoft.Extensions.Options;
using ZLongChat.Contracts;

namespace ZLongChat.API.Endpoints;

/// <summary>
/// 客户端配置下发接口。
///
/// 设计意图：STUN / TURN 这类参数只应该在**服务端**维护一份，
/// 客户端登录后拉取即可，避免「每台客户端都要各自配一遍、改一次要改 N 处」。
/// 客户端拉取失败时仍会使用自己的本地配置兜底。
/// </summary>
public static class ClientConfigEndpoints
{
    /// <summary>注册客户端配置接口</summary>
    public static IEndpointRouteBuilder MapClientConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ChatApiRoutes.ClientConfig, (IOptions<ClientConfiguration> options) =>
        {
            // ClientConfiguration 上标记了 [JsonIgnore] 的字段（ServerBaseUrl、
            // UseServerProvidedConfig）属于客户端本地决定，序列化时自动不会下发。
            return Results.Ok(options.Value);
        });

        return app;
    }
}
