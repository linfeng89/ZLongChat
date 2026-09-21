using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;
using ZLongChat.Contracts;
using ZLongChat.Contracts.Shudong;
using ZLongChat.Core;
using ZLongChat.Core.Ports;
using ZLongChat.Mcp.Resources;
using ZLongChat.Mcp.Tools;

namespace ZLongChat.Mcp;

/// <summary>
/// MCP 层的组合根扩展。
///
/// 用法（宿主项目 Program.cs 两行）：
/// <code>
/// builder.Services.AddShudongMcp();
/// ...
/// app.MapShudongMcp();
/// </code>
///
/// <b>为什么注册放在这里而不是 API 的 Program.cs</b>：
/// 将来把 MCP 挂到别的宿主（独立进程 / stdio 传输 / 测试宿主）时，
/// 不需要把这一串注册抄一遍 —— 抄一遍就会漂移。
/// </summary>
public static class ShudongMcpExtensions
{
    /// <summary>
    /// 注册树洞泡泡的 MCP 服务与业务实现。
    /// 业务实现用 <c>TryAdd</c>，宿主可以先行覆盖（例如换成 Redis / 数据库实现）。
    /// </summary>
    public static IServiceCollection AddShudongMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 令牌从 Authorization 头里取，所以必须能拿到 HttpContext
        services.AddHttpContextAccessor();

        // 令牌 → 身份（实名 UserId + 定期轮换的匿名 ID）
        services.TryAddSingleton<IIdentityService, DevIdentityService>();

        // ---- 领域实现（内存版，骨架用）----
        //
        // 为什么具体类型也要注册：ISignalService 与 ISignalLookup 必须指向**同一个**
        // 内存实例，否则会话层看到的信号和工具层看到的不是同一份数据。
        // 换成 Redis / 数据库时，替换这四行即可，工具与业务代码零改动。
        services.TryAddSingleton<InMemorySignalService>();
        services.TryAddSingleton<ISignalService>(sp => sp.GetRequiredService<InMemorySignalService>());
        services.TryAddSingleton<ISignalLookup>(sp => sp.GetRequiredService<InMemorySignalService>());
        services.TryAddSingleton<ISessionService, InMemorySessionService>();
        services.TryAddSingleton<ISafetyService, InMemorySafetyService>();
        services.TryAddSingleton<CallContextAccessor>();

        services.AddMcpServer(options =>
            {
                // 业务异常 → 结构化错误（错误码 + 文案 + 告知），否则会被 SDK 压成一句无信息量的提示
                options.Filters.Request.CallToolFilters.Add(next =>
                    async (request, cancellationToken) =>
                    {
                        try
                        {
                            return await next(request, cancellationToken).ConfigureAwait(false);
                        }
                        catch (ShudongException exception)
                        {
                            return ShudongToolErrors.FromException(exception);
                        }
                    });
            })
            .WithHttpTransport()
            .WithTools<SignalTools>()
            .WithTools<CompanionTools>()
            .WithTools<SafetyTools>()
            .WithResources<ShudongUiResources>();

        return services;
    }

    /// <summary>映射 MCP 端点（HTTP 传输，Streamable HTTP）。</summary>
    public static IEndpointRouteBuilder MapShudongMcp(
        this IEndpointRouteBuilder endpoints,
        string path = ZLongChatDefaults.ShudongMcpPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapMcp(path);
        return endpoints;
    }
}
