using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZLongChat.Ai;
using ZLongChat.Ai.Abstractions;
using ZLongChat.AiBot;
using ZLongChat.Communication.SignalR;
using ZLongChat.Contracts;

// ============================================================
// AI 机器人组合根
//
// 机器人 = 一个连到 Hub 的普通用户，因此完全复用通信层：
//   信令 ISignalingService（SignalR）
//   通道 IMessageTransport（服务端转发）
// 没有新增任何通信代码。
//
// 配置节名与后端 / 客户端完全一致（ZLongChat:Client、ZLongChat:AiBot），
// 优先级：命令行 > 环境变量 > appsettings.Local.json > appsettings.json > 代码缺省值。
// ============================================================

// ---------------- 读取配置 ----------------
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var clientConfiguration = configuration
    .GetSection(ZLongChatSections.Client)
    .Get<ClientConfiguration>() ?? new ClientConfiguration();

var botSettings = configuration
    .GetSection(ZLongChatSections.AiBot)
    .Get<AiBotSettings>() ?? new AiBotSettings();

// 命令行参数优先级最高
if (!BotArguments.TryApply(args, clientConfiguration, botSettings))
{
    return 1;
}

using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddConsole()
    .SetMinimumLevel(LogLevel.Information));

await using var signaling = new SignalRSignalingService(
    clientConfiguration,
    loggerFactory.CreateLogger<SignalRSignalingService>());

// 机器人常驻服务端侧，不需要 NAT 穿透，所以直接用「服务端转发」通道
await using var transport = new SignalRRelayTransport(
    signaling,
    loggerFactory.CreateLogger<SignalRRelayTransport>());

// ★★★ 唯一需要改动的地方 ★★★
// 现在用不依赖任何云服务的占位实现，把整条链路跑通。
// 接入真实大模型时只需换成 MicrosoftExtensionsAiAssistant：
//
//   using Microsoft.Extensions.AI;
//   IChatClient chatClient = new OpenAIClient(apiKey)
//       .GetChatClient("gpt-4o")
//       .AsIChatClient();
//   IAiAssistant assistant = new MicrosoftExtensionsAiAssistant(
//       chatClient,
//       systemPrompt: "你是一个简洁的中文聊天助手。");
//
// 其余代码（包括 AiBotWorker 与整个通信层）一行都不用改。
IAiAssistant assistant = new EchoAiAssistant(
    botSettings.ChunkSize,
    TimeSpan.FromMilliseconds(botSettings.ChunkDelayMs));

await using var bot = new AiBotWorker(
    botSettings.UserId,
    assistant,
    signaling,
    transport,
    botSettings.HistoryLimit,
    loggerFactory.CreateLogger<AiBotWorker>());

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("正在退出...");
    cancellation.Cancel();
};

Console.WriteLine("========================================");
Console.WriteLine($"🤖 AI 机器人：{botSettings.UserId}");
Console.WriteLine($"📡 调度服务：{clientConfiguration.ServerBaseUrl}");
Console.WriteLine($"🧠 助手实现：{assistant.Name}");
Console.WriteLine($"📜 历史长度：{botSettings.HistoryLimit} 条/会话");
Console.WriteLine("按 Ctrl+C 退出");
Console.WriteLine("========================================");

try
{
    await bot.RunAsync(cancellation.Token);
}
catch (Exception ex)
{
    loggerFactory.CreateLogger("AiBot").LogError(ex, "AI 机器人异常退出");
    return 1;
}

return 0;
