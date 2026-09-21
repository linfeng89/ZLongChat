using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZLongChat.Contracts;

namespace ZLongChat.PC;

/// <summary>
/// 前端配置加载。
///
/// 优先级（后面的覆盖前面的）：
///   1. appsettings.json        —— 随程序发布的默认配置
///   2. appsettings.Local.json  —— 本机私有配置（可选，不建议提交进仓库）
///   3. 环境变量                 —— 部署时注入，适合放敏感值
///
/// 节名统一由 <see cref="ZLongChatSections"/> 提供，与后端、机器人完全一致。
/// 环境变量用双下划线表示层级（与 ASP.NET Core 约定相同）：
///   ZLongChat__Client__ServerBaseUrl=http://10.0.0.9:5000
/// </summary>
internal static class AppConfiguration
{
    /// <summary>构建配置树</summary>
    public static IConfigurationRoot Build(string basePath) =>
        new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

    /// <summary>读取客户端配置</summary>
    public static ClientConfiguration GetClientConfiguration(this IConfiguration configuration, ILogger? logger = null)
    {
        var client = configuration
            .GetSection(ZLongChatSections.Client)
            .Get<ClientConfiguration>() ?? new ClientConfiguration();

        // 修正明显不合理的值（例如超时写成 0），避免手写配置把程序改崩
        client.Normalize();

        logger?.LogInformation("调度服务地址：{Url}", client.ServerBaseUrl);

        if (client.UseServerProvidedConfig)
        {
            // 本地不再重复配置 ICE/超时，所以这里不能因为「本地 ICE 为空」而告警
            logger?.LogInformation(
                "客户端配置：登录时由服务端下发（ICE 服务器、超时、重连间隔等）");
        }
        else
        {
            logger?.LogInformation("客户端配置：仅使用本地配置（{Section}:UseServerProvidedConfig=false）",
                ZLongChatSections.Client);
            LogLocalIceServers(client.IceServers, logger);
        }

        return client;
    }

    private static void LogLocalIceServers(IReadOnlyList<IceServerOptions> iceServers, ILogger? logger)
    {
        if (iceServers.Count == 0)
        {
            logger?.LogWarning(
                "已禁用服务端配置下发，但本地也没有配置 ICE 服务器（{Section}:IceServers），P2P 打洞很可能失败",
                ZLongChatSections.Client);
            return;
        }

        // 只打印地址，绝不打印 TURN 凭据
        logger?.LogInformation(
            "本地 ICE 服务器（{Count} 个）：{Servers}",
            iceServers.Count,
            string.Join("、", iceServers.Select(server => server.Url)));
    }
}
