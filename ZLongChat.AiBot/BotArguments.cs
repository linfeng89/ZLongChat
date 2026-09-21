using ZLongChat.Contracts;

namespace ZLongChat.AiBot;

/// <summary>
/// 命令行参数解析：把命令行覆盖到已从 appsettings.json 读到的配置上。
/// 优先级：命令行 &gt; 环境变量 &gt; appsettings.Local.json &gt; appsettings.json &gt; 代码缺省值。
/// </summary>
public static class BotArguments
{
    /// <summary>用法说明</summary>
    public const string Usage = """
        ZLongChat AI 机器人

        用法：
          dotnet run --project ZLongChat.AiBot -- [选项]

        选项：
          -s, --server <url>     调度服务地址（默认取配置 ZLongChat:Client:ServerBaseUrl）
          -u, --user-id <id>     机器人用户ID（默认取配置 ZLongChat:AiBot:UserId）
          -n, --history <count>  每个会话保留的历史消息条数
          -c, --chunk <size>     流式分片字符数（占位助手用）
          -d, --delay <ms>       流式分片间隔毫秒（占位助手用）
          -h, --help             显示本帮助

        示例：
          dotnet run --project ZLongChat.AiBot
          dotnet run --project ZLongChat.AiBot -- --server http://10.0.0.9:5000 --user-id aibot
        """;

    /// <summary>
    /// 把命令行参数覆盖到配置对象上。
    /// </summary>
    /// <returns>true 表示可以继续启动；false 表示应当退出（--help 或参数错误）</returns>
    public static bool TryApply(string[] args, ClientConfiguration client, AiBotSettings bot)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(bot);

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    return false;

                case "-s" or "--server":
                    if (!TryReadValue(args, ref i, out var server))
                    {
                        return Reject(args[i]);
                    }
                    client.ServerBaseUrl = server;
                    break;

                case "-u" or "--user-id":
                    if (!TryReadValue(args, ref i, out var userId))
                    {
                        return Reject(args[i]);
                    }
                    bot.UserId = userId;
                    break;

                case "-n" or "--history":
                    if (!TryReadPositiveInt(args, ref i, out int history))
                    {
                        return Reject(args[i]);
                    }
                    bot.HistoryLimit = history;
                    break;

                case "-c" or "--chunk":
                    if (!TryReadPositiveInt(args, ref i, out int chunk))
                    {
                        return Reject(args[i]);
                    }
                    bot.ChunkSize = chunk;
                    break;

                case "-d" or "--delay":
                    if (!TryReadNonNegativeInt(args, ref i, out int delay))
                    {
                        return Reject(args[i]);
                    }
                    bot.ChunkDelayMs = delay;
                    break;

                default:
                    return Reject(args[i]);
            }
        }

        client.Normalize();
        bot.Normalize();

        if (string.IsNullOrWhiteSpace(bot.UserId))
        {
            Console.Error.WriteLine("机器人用户ID不能为空。");
            return false;
        }

        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadPositiveInt(string[] args, ref int index, out int value)
        => TryReadInt(args, ref index, minimum: 1, out value);

    private static bool TryReadNonNegativeInt(string[] args, ref int index, out int value)
        => TryReadInt(args, ref index, minimum: 0, out value);

    private static bool TryReadInt(string[] args, ref int index, int minimum, out int value)
    {
        value = 0;

        if (!TryReadValue(args, ref index, out var text) ||
            !int.TryParse(text, out int parsed) ||
            parsed < minimum)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool Reject(string argument)
    {
        Console.Error.WriteLine($"无法识别的参数或参数值：{argument}");
        Console.Error.WriteLine();
        Console.Error.WriteLine(Usage);
        return false;
    }
}
